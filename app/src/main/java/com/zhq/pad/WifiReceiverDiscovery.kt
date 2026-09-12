package com.zhq.pad

import android.content.Context
import android.net.ConnectivityManager
import android.net.LinkProperties
import android.net.Network
import android.net.NetworkCapabilities
import android.net.NetworkRequest
import android.os.SystemClock
import android.util.Log
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.channels.Channel
import kotlinx.coroutines.channels.awaitClose
import kotlinx.coroutines.currentCoroutineContext
import kotlinx.coroutines.delay
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.callbackFlow
import kotlinx.coroutines.flow.collectLatest
import kotlinx.coroutines.flow.distinctUntilChanged
import kotlinx.coroutines.isActive
import kotlinx.coroutines.withContext
import java.net.DatagramPacket
import java.net.DatagramSocket
import java.net.InetAddress
import java.net.InetSocketAddress
import java.net.SocketTimeoutException
import java.security.SecureRandom

internal class WifiReceiverDiscovery(context: Context) {
    private val connectivity = context.getSystemService(ConnectivityManager::class.java)
    val target = MutableStateFlow<ReceiverTarget?>(null)
    private val failures = Channel<ReceiverTarget>(Channel.UNLIMITED)

    fun connectionFailed(target: ReceiverTarget) { failures.trySend(target) }

    private data class WifiLink(val network: Network, val links: List<Pair<InetAddress, Int>>)

    private fun wifiLinks() = callbackFlow {
        val available = linkedMapOf<Network, WifiLink>()
        val callback = object : ConnectivityManager.NetworkCallback() {
            override fun onLinkPropertiesChanged(network: Network, properties: LinkProperties) {
                available[network] = WifiLink(network, properties.linkAddresses.map { it.address to it.prefixLength })
                trySend(available.values.firstOrNull())
            }
            override fun onLost(network: Network) {
                available.remove(network)
                trySend(available.values.firstOrNull())
            }
        }
        val request = NetworkRequest.Builder()
            .addTransportType(NetworkCapabilities.TRANSPORT_WIFI)
            .addCapability(NetworkCapabilities.NET_CAPABILITY_NOT_VPN)
            .build()
        connectivity.registerNetworkCallback(request, callback)
        awaitClose { connectivity.unregisterNetworkCallback(callback) }
    }.distinctUntilChanged()

    // Called only after the existing Android 17 local-network permission gate succeeds.
    suspend fun run() {
        try {
            wifiLinks().collectLatest { link ->
                target.value = null
                if (link != null) withContext(Dispatchers.IO) {
                    while (currentCoroutineContext().isActive) {
                        try { probe(link) }
                        catch (ex: java.io.IOException) { Log.w("LeftPadDiscovery", "Wi-Fi discovery retry", ex) }
                        finally { target.value = null }
                        delay(1_000)
                    }
                }
            }
        } finally { target.value = null }
    }

    private suspend fun probe(link: WifiLink) {
        val selection = DiscoverySelection {
            target.value = it
            Log.i("LeftPadDiscovery", if (it == null) "Searching" else
                "OFFER source=${it.address.hostAddress} receiver=${it.receiverId}")
        }
        val random = SecureRandom()
        val outstanding = linkedMapOf<Long, Long>()
        var schedule = DiscoveryProbeSchedule(SystemClock.elapsedRealtime())
        var invalidPackets = 0L
        var lastDiagnostic = -10_000L
        val destinations = broadcastDestinations(link.links)
        DatagramSocket(null).use { socket ->
            link.network.bindSocket(socket)
            socket.broadcast = true
            socket.bind(InetSocketAddress(0))
            socket.soTimeout = 100
            Log.i("LeftPadDiscovery", "Searching: Wi-Fi discovery socket bound")
            // One extra byte makes an oversized datagram unambiguously invalid even when truncated.
            val buffer = ByteArray(DiscoveryProtocol.OFFER_LENGTH + 1)
            while (currentCoroutineContext().isActive) {
                val now = SystemClock.elapsedRealtime()
                val previous = selection.target
                while (true) {
                    val failed = failures.tryReceive().getOrNull() ?: break
                    selection.connectionFailed(failed, now)
                }
                selection.tick(now)
                if (previous != null && selection.target == null) {
                    outstanding.clear()
                    schedule = DiscoveryProbeSchedule(now)
                }
                outstanding.entries.removeAll { now - it.value >= DiscoverySelection.STALE_MS }
                if (schedule.isDue(now)) {
                    val nonce = random.nextLong()
                    outstanding[nonce] = now
                    val bytes = DiscoveryProtocol.discover(nonce)
                    for (destination in destinations) {
                        try { socket.send(DatagramPacket(bytes, bytes.size, destination, DiscoveryProtocol.PORT)) }
                        catch (ex: java.io.IOException) {
                            if (now - lastDiagnostic >= 10_000) {
                                lastDiagnostic = now
                                Log.w("LeftPadDiscovery", "Broadcast send failed", ex)
                            }
                        }
                    }
                    schedule.sent(now, selection.target != null)
                }
                val packet = DatagramPacket(buffer, buffer.size)
                socket.soTimeout = schedule.receiveWaitMillis(SystemClock.elapsedRealtime())
                try { socket.receive(packet) } catch (_: SocketTimeoutException) { continue }
                val receivedAt = SystemClock.elapsedRealtime()
                // A late reply cannot revive a stale target before timeout processing.
                val beforeReceive = selection.target
                selection.tick(receivedAt)
                if (beforeReceive != null && selection.target == null) {
                    outstanding.clear()
                    schedule = DiscoveryProbeSchedule(receivedAt)
                }
                val offer = DiscoveryProtocol.decodeOffer(buffer, packet.length) {
                    outstanding[it]?.let { sent -> receivedAt - sent < DiscoverySelection.STALE_MS } == true
                }
                if (offer != null && packet.port == DiscoveryProtocol.PORT) {
                    selection.offer(offer, packet.address, receivedAt)
                } else {
                    invalidPackets++
                    if (receivedAt - lastDiagnostic >= 10_000) {
                        lastDiagnostic = receivedAt
                        Log.w("LeftPadDiscovery", "Ignored invalid offers: $invalidPackets")
                    }
                }
            }
        }
    }
}
