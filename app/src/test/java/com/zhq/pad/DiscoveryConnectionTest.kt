package com.zhq.pad

import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.cancelAndJoin
import kotlinx.coroutines.channels.Channel
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.collectLatest
import kotlinx.coroutines.launch
import kotlinx.coroutines.runBlocking
import kotlinx.coroutines.withContext
import kotlinx.coroutines.withTimeout
import org.junit.Assert.*
import org.junit.Test
import java.net.Inet4Address
import java.net.InetAddress
import java.net.ServerSocket
import java.net.SocketTimeoutException

class DiscoveryConnectionTest {
    @Test fun targetTransitionClosesOldTcpBeforeNewSessionAndNewReceiverGetsOnlyNewInput() = runBlocking {
        val sender = OrderedMessageSender(this)
        val session = DiscoveredControllerSession(sender)
        val targets = MutableStateFlow<ReceiverTarget?>(null)
        val connected = Channel<ReceiverTarget>(Channel.UNLIMITED)
        val loopback = InetAddress.getByName("127.0.0.1") as Inet4Address
        ServerSocket(0, 1, loopback).use { a ->
            ServerSocket(0, 1, loopback).use { b ->
                a.soTimeout = 2000
                b.soTimeout = 2000
                val targetA = ReceiverTarget("a", loopback, a.localPort)
                val targetB = ReceiverTarget("b", loopback, b.localPort)
                val worker = launch {
                    targets.collectLatest { target ->
                        if (target != null) runDiscoveredControllerConnection(target, session,
                            onConnected = { connected.trySend(target) }, onDisconnected = {}, onFailure = {})
                    }
                }
                try {
                    targets.value = targetA
                    withContext(Dispatchers.IO) { a.accept() }.use { old ->
                        old.soTimeout = 2000
                        assertSame(targetA, withTimeout(2000) { connected.receive() })
                        session.button("cross", "down")
                        val input = old.getInputStream().bufferedReader()
                        assertEquals(OutgoingMessage.Button("cross", "down").wireText.trim(),
                            withContext(Dispatchers.IO) { input.readLine() })

                        targets.value = null // stale: cancels the sender and closes TCP
                        assertEquals(-1, withContext(Dispatchers.IO) { old.getInputStream().read() })
                        assertEquals(0L, session.generation)
                        targets.value = targetB
                        withContext(Dispatchers.IO) { b.accept() }.use { next ->
                            next.soTimeout = 2000
                            assertSame(targetB, withTimeout(2000) { connected.receive() })
                            session.button("cross", "up") // old finger must be forgotten
                            session.button("circle", "down")
                            session.button("circle", "up")
                            val nextInput = next.getInputStream().bufferedReader()
                            assertEquals(OutgoingMessage.Button("circle", "down").wireText.trim(),
                                withContext(Dispatchers.IO) { nextInput.readLine() })
                            assertEquals(OutgoingMessage.Button("circle", "up").wireText.trim(),
                                withContext(Dispatchers.IO) { nextInput.readLine() })
                            next.soTimeout = 100
                            try { withContext(Dispatchers.IO) { nextInput.readLine() }; fail("unexpected replay") }
                            catch (_: SocketTimeoutException) { }
                            targets.value = null
                            next.soTimeout = 2000
                            assertEquals(-1, withContext(Dispatchers.IO) { next.getInputStream().read() })
                        }
                    }
                } finally { worker.cancelAndJoin(); sender.close() }
            }
        }
    }
}
