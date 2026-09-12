package com.zhq.pad

import kotlinx.coroutines.CompletableDeferred
import kotlinx.coroutines.channels.Channel
import kotlinx.coroutines.runBlocking
import kotlinx.coroutines.withTimeout
import kotlinx.coroutines.withTimeoutOrNull
import org.junit.Assert.*
import org.junit.Test
import java.net.InetAddress
import java.nio.ByteBuffer
import java.nio.ByteOrder

class DiscoveryTest {
    private val nonce = 0x0807060504030201L
    private val id = "000102030405060708090a0b0c0d0e0f"
    private fun wire(): ByteArray = ByteBuffer.allocate(40).order(ByteOrder.LITTLE_ENDIAN)
        .put(byteArrayOf(76, 80, 65, 68, 1, 2, 0, 0)).putLong(nonce)
        .put(ByteArray(16) { it.toByte() }).putShort(8888).put(1).put(0).putInt(0).array()
    private fun decode(bytes: ByteArray) = DiscoveryProtocol.decodeOffer(bytes, bytes.size) { it == nonce }
    private fun address(ip: String) = InetAddress.getByName(ip)
    private fun offer(receiver: String = id) = ReceiverOffer(nonce, receiver, 8888)

    @Test fun discoverHasExactLittleEndianWireLayout() {
        assertArrayEquals(byteArrayOf(76, 80, 65, 68, 1, 1, 0, 0, 1, 2, 3, 4, 5, 6, 7, 8),
            DiscoveryProtocol.discover(nonce))
    }

    @Test fun offerHasExactLayoutOpaqueIdentityAndNoAddressField() {
        val bytes = wire()
        assertEquals(40, bytes.size)
        assertEquals(ReceiverOffer(nonce, id, 8888), decode(bytes))
        assertArrayEquals(ByteArray(16) { it.toByte() }, bytes.copyOfRange(16, 32))
        assertArrayEquals(byteArrayOf(-72, 34, 1, 0, 0, 0, 0, 0), bytes.copyOfRange(32, 40))
    }

    @Test fun malformedHeadersReservedNoncePortProtocolAndCapabilitiesRejected() {
        for (offset in listOf(0, 1, 2, 3, 4, 5, 6, 7, 8, 15, 32, 33, 34, 35, 36, 37, 38, 39)) {
            val bytes = wire()
            bytes[offset] = (bytes[offset].toInt() xor 0x40).toByte()
            assertNull("offset $offset", decode(bytes))
        }
    }

    @Test fun allTruncationsAndOversizedOffersRejected() {
        for (length in 0..39) assertNull(decode(wire().copyOf(length)))
        assertNull(decode(wire().copyOf(41)))
        assertNull(decode(wire().copyOf(2048)))
        assertNull(DiscoveryProtocol.decodeOffer(ByteArray(39), 40) { true })
        assertNotNull(DiscoveryProtocol.decodeOffer(wire().copyOf(41), 40) { it == nonce })
    }

    @Test fun searchingFirstValidOfferWinsAndSourceIpv4IsAuthoritative() {
        val changes = mutableListOf<ReceiverTarget?>()
        val selection = DiscoverySelection(changes::add)
        selection.offer(offer(), address("192.0.2.10"), 0)
        selection.offer(offer("b".repeat(32)), address("192.0.2.20"), 100)
        assertEquals(1, changes.size)
        assertEquals(id, selection.target!!.receiverId)
        assertEquals("192.0.2.10", selection.target!!.address.hostAddress)
        assertEquals(8888, selection.target!!.port)
    }

    @Test fun onlyCurrentReceiverRefreshesLivenessThenTimeoutReturnsSearching() {
        val changes = mutableListOf<ReceiverTarget?>()
        val selection = DiscoverySelection(changes::add)
        selection.offer(offer(), address("192.0.2.10"), 0)
        selection.offer(offer(), address("192.0.2.10"), 2000)
        selection.tick(4499)
        assertNotNull(selection.target)
        selection.offer(offer("b"), address("192.0.2.20"), 4499)
        selection.tick(4500)
        assertNull(selection.target)
        assertEquals(2, changes.size)
        assertNull(changes.last())
    }

    @Test fun sourceAddressChangeOfSameIdentityChangesTarget() {
        val changes = mutableListOf<ReceiverTarget?>()
        val selection = DiscoverySelection(changes::add)
        selection.offer(offer(), address("192.0.2.10"), 0)
        selection.offer(offer(), address("192.0.2.11"), 500)
        assertEquals(2, changes.size)
        assertEquals("192.0.2.11", selection.target!!.address.hostAddress)
    }

    @Test fun ipv6LoopbackMulticastAndUnspecifiedCannotBecomeTargets() {
        val selection = DiscoverySelection { fail("invalid source selected") }
        for (ip in listOf("::1", "2001:db8::1", "127.0.0.1", "0.0.0.0", "224.0.0.1", "255.255.255.255"))
            selection.offer(offer(), address(ip), 0)
    }

    @Test fun failedConnectReleasesSelectionWithBackoffAndIgnoresLateOldFailure() {
        val selection = DiscoverySelection { }
        selection.offer(offer(), address("192.0.2.10"), 0)
        val old = selection.target!!
        selection.connectionFailed(old, 10)
        assertNull(selection.target)
        selection.offer(offer(), address("192.0.2.10"), 1009)
        assertNull(selection.target)
        selection.offer(offer(), address("192.0.2.10"), 1010)
        val next = selection.target!!
        selection.connectionFailed(old, 1011)
        assertSame(next, selection.target)
    }

    @Test fun directedBroadcastUsesActualPrefixAndDeduplicates() {
        fun destinations(ip: String, prefix: Int) = broadcastDestinations(listOf(address(ip) to prefix))
            .map { it.hostAddress }.toSet()
        assertEquals(setOf("255.255.255.255", "192.168.7.255"), destinations("192.168.7.42", 24))
        assertEquals(setOf("255.255.255.255", "172.20.255.255"), destinations("172.20.3.42", 16))
        assertEquals(setOf("255.255.255.255", "172.20.15.255"), destinations("172.20.3.42", 20))
        assertEquals(setOf("255.255.255.255", "192.168.3.255"), destinations("192.168.2.42", 23))
        val links = listOf(address("192.168.2.42") to 23, address("192.168.3.43") to 23,
            address("2001:db8::1") to 64, address("10.0.0.1") to 0)
        assertEquals(setOf("255.255.255.255", "192.168.3.255"), broadcastDestinations(links).map { it.hostAddress }.toSet())
        for (prefix in listOf(-1, 31, 32, 33)) assertEquals(setOf("255.255.255.255"), destinations("192.0.2.1", prefix))
    }

    @Test fun searchingProbesAtZero2505001000ThenEverySecond() {
        val schedule = DiscoveryProbeSchedule(0)
        for (time in listOf(0L, 250L, 500L, 1000L, 2000L, 3000L)) {
            assertFalse(schedule.isDue(time - 1))
            assertEquals(1, schedule.receiveWaitMillis(time - 1))
            assertTrue(schedule.isDue(time))
            schedule.sent(time, false)
            assertEquals(100, schedule.receiveWaitMillis(time))
        }
    }

    @Test fun connectedProbesEverySecond() {
        val schedule = DiscoveryProbeSchedule(0)
        schedule.sent(0, true)
        assertFalse(schedule.isDue(999))
        assertTrue(schedule.isDue(1000))
    }

    @Test fun staleAStopsRealSenderDropsQueueAndBReceivesOnlyFreshInput() = runBlocking {
        val sender = OrderedMessageSender(this)
        val session = DiscoveredControllerSession(sender)
        val started = CompletableDeferred<Unit>()
        val oldWrites = Channel<String>(Channel.UNLIMITED)
        val newWrites = Channel<String>(Channel.UNLIMITED)
        val selection = DiscoverySelection { session.stop() }
        try {
            selection.offer(offer(), address("192.0.2.10"), 0)
            session.start(OutgoingMessageSink {
                oldWrites.send(it)
                started.complete(Unit)
                kotlinx.coroutines.awaitCancellation()
            })
            val oldGeneration = session.generation
            session.button("cross", "down")
            withTimeout(2000) { started.await() }
            session.button("triangle", "down") // queued behind the blocked old write
            session.command("task_manager")
            selection.tick(2500)
            assertNull(selection.target)
            assertEquals(0L, session.generation)
            assertEquals(EnqueueResult.NO_ACTIVE_SESSION, sender.tryEnqueue(OutgoingMessage.Button("old", "up")))

            selection.offer(offer("b".repeat(32)), address("192.0.2.20"), 2501)
            assertEquals("b".repeat(32), selection.target!!.receiverId)
            assertEquals("192.0.2.20", selection.target!!.address.hostAddress)
            session.start(OutgoingMessageSink { newWrites.send(it) })
            assertTrue(session.generation > oldGeneration)
            session.button("cross", "up") // finger held over transition: suppressed
            session.button("triangle", "up")
            session.button("square", "down")
            session.button("square", "up")
            assertEquals(OutgoingMessage.Button("square", "down").wireText, withTimeout(2000) { newWrites.receive() })
            assertEquals(OutgoingMessage.Button("square", "up").wireText, withTimeout(2000) { newWrites.receive() })
            assertNull(withTimeoutOrNull(100) { newWrites.receive() })
            assertEquals(OutgoingMessage.Button("cross", "down").wireText, oldWrites.tryReceive().getOrThrow())
            assertTrue(oldWrites.tryReceive().isFailure)
        } finally { session.stop(); sender.close() }
    }
}
