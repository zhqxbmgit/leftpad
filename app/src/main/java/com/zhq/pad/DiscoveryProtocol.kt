package com.zhq.pad

import java.net.Inet4Address
import java.net.InetAddress
import java.nio.ByteBuffer
import java.nio.ByteOrder

internal data class ReceiverOffer(val nonce: Long, val receiverId: String, val port: Int)
// Identity equality intentionally distinguishes a rediscovery from a previous connection attempt.
internal class ReceiverTarget(val receiverId: String, val address: Inet4Address, val port: Int)

internal object DiscoveryProtocol {
    const val PORT = 8889
    const val CONTROLLER_PORT = 8888
    const val DISCOVER_LENGTH = 16
    const val OFFER_LENGTH = 40
    private val magic = byteArrayOf(0x4c, 0x50, 0x41, 0x44)

    fun discover(nonce: Long): ByteArray = ByteBuffer.allocate(DISCOVER_LENGTH)
        .order(ByteOrder.LITTLE_ENDIAN).put(magic).put(1).put(1).putShort(0).putLong(nonce).array()

    fun decodeOffer(packet: ByteArray, length: Int, acceptsNonce: (Long) -> Boolean): ReceiverOffer? {
        if (length != OFFER_LENGTH || packet.size < length) return null
        if (!packet.copyOfRange(0, 4).contentEquals(magic) || packet[4] != 1.toByte() ||
            packet[5] != 2.toByte() || packet[6] != 0.toByte() || packet[7] != 0.toByte() ||
            packet[35] != 0.toByte()) return null
        val wire = ByteBuffer.wrap(packet, 0, length).order(ByteOrder.LITTLE_ENDIAN)
        val nonce = wire.getLong(8)
        val port = wire.getShort(32).toInt() and 0xffff
        if (!acceptsNonce(nonce) || port != CONTROLLER_PORT || packet[34] != 1.toByte() ||
            wire.getInt(36) != 0) return null
        val id = packet.copyOfRange(16, 32).joinToString("") { "%02x".format(it.toInt() and 0xff) }
        return ReceiverOffer(nonce, id, port)
    }
}

internal fun broadcastDestinations(links: List<Pair<InetAddress, Int>>): Set<Inet4Address> {
    val destinations = linkedSetOf(InetAddress.getByAddress(byteArrayOf(-1, -1, -1, -1)) as Inet4Address)
    for ((address, prefix) in links) {
        // /31 and /32 have no broadcast host; do not send probes to a peer or to ourselves.
        if (address !is Inet4Address || prefix !in 0..30) continue
        val ip = ByteBuffer.wrap(address.address).int
        val mask = if (prefix == 0) 0 else -1 shl (32 - prefix)
        val broadcast = ByteBuffer.allocate(4).putInt(ip or mask.inv()).array()
        destinations += InetAddress.getByAddress(broadcast) as Inet4Address
    }
    return destinations
}

/** Worker-confined discovery selection, using monotonic milliseconds. */
internal class DiscoverySelection(private val onTarget: (ReceiverTarget?) -> Unit) {
    var target: ReceiverTarget? = null
        private set
    private var lastOffer = 0L
    private var retryAfter = 0L

    fun offer(offer: ReceiverOffer, source: InetAddress, now: Long) {
        if (source !is Inet4Address || source.isAnyLocalAddress || source.isLoopbackAddress ||
            source.isMulticastAddress || source.address.all { it == (-1).toByte() }) return
        val current = target
        if (current != null && current.receiverId != offer.receiverId) return
        if (current == null && now < retryAfter) return
        lastOffer = now
        val next = ReceiverTarget(offer.receiverId, source, offer.port)
        if (current == null || current.address != next.address || current.port != next.port) {
            target = next
            onTarget(next)
        }
    }

    fun tick(now: Long) {
        if (target != null && now - lastOffer >= STALE_MS) clear()
    }

    fun connectionFailed(failed: ReceiverTarget, now: Long) {
        if (target !== failed) return
        retryAfter = now + 1_000
        clear()
    }

    fun clear() {
        if (target == null) return
        target = null
        onTarget(null)
    }

    companion object { const val STALE_MS = 2_500L }
}

internal class DiscoveryProbeSchedule(start: Long) {
    private var next = start
    private var step = 0
    fun isDue(now: Long): Boolean = now >= next
    fun receiveWaitMillis(now: Long): Int = (next - now).coerceIn(1, 100).toInt()
    fun sent(now: Long, selected: Boolean) {
        val interval = if (selected) 1_000L else when (step++) { 0, 1 -> 250L; 2 -> 500L; else -> 1_000L }
        next = now + interval
    }
}
