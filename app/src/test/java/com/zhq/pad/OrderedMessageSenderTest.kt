package com.zhq.pad

import kotlinx.coroutines.CompletableDeferred
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.NonCancellable
import kotlinx.coroutines.async
import kotlinx.coroutines.channels.Channel
import kotlinx.coroutines.coroutineScope
import kotlinx.coroutines.delay
import kotlinx.coroutines.runBlocking
import kotlinx.coroutines.withContext
import kotlinx.coroutines.withTimeout
import kotlinx.coroutines.withTimeoutOrNull
import org.junit.Assert.assertEquals
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Test
import java.io.ByteArrayOutputStream
import java.io.IOException
import java.io.OutputStreamWriter
import java.io.PrintWriter
import java.util.concurrent.ConcurrentHashMap
import java.util.concurrent.atomic.AtomicInteger

class OrderedMessageSenderTest {
    @Test
    fun singleMessageWritesExpectedWireText() = runBlocking {
        val sender = OrderedMessageSender(this)
        val sink = RecordingSink()
        try {
            sender.startSession(sink)

            assertEquals(
                EnqueueResult.ENQUEUED,
                sender.tryEnqueue(OutgoingMessage.Button("cross", "down"))
            )

            assertEquals(
                listOf("{\"button\":\"cross\",\"action\":\"down\"}\n"),
                sink.take(1)
            )
        } finally {
            sender.close()
        }
    }

    @Test
    fun downThenUpPreservesFifoOrder() = runBlocking {
        val sender = OrderedMessageSender(this)
        val sink = RecordingSink()
        try {
            sender.startSession(sink)
            val expected = listOf(
                OutgoingMessage.Button("cross", "down"),
                OutgoingMessage.Button("cross", "up")
            )

            expected.forEach { assertEquals(EnqueueResult.ENQUEUED, sender.tryEnqueue(it)) }

            assertEquals(expected.map { it.wireText }, sink.take(expected.size))
        } finally {
            sender.close()
        }
    }

    @Test
    fun oneThousandRapidMessagesPreserveExactOrder() = runBlocking {
        val sender = OrderedMessageSender(this)
        val sink = RecordingSink()
        try {
            sender.startSession(sink)
            val expected = List(1_000) { index ->
                OutgoingMessage.Button("cross", if (index % 2 == 0) "down" else "up")
            }

            val actual = mutableListOf<String>()
            expected.chunked(64).forEach { batch ->
                batch.forEach { message ->
                    assertEquals(EnqueueResult.ENQUEUED, sender.tryEnqueue(message))
                }
                actual += sink.take(batch.size)
            }

            assertEquals(expected.map { it.wireText }, actual)
        } finally {
            sender.close()
        }
    }

    @Test
    fun interleavedButtonsPreserveExactOrder() = runBlocking {
        val sender = OrderedMessageSender(this)
        val sink = RecordingSink()
        try {
            sender.startSession(sink)
            val expected = listOf(
                OutgoingMessage.Button("cross", "down"),
                OutgoingMessage.Button("circle", "down"),
                OutgoingMessage.Button("cross", "up"),
                OutgoingMessage.Button("circle", "up")
            )

            expected.forEach { sender.tryEnqueue(it) }

            assertEquals(expected.map { it.wireText }, sink.take(expected.size))
        } finally {
            sender.close()
        }
    }

    @Test
    fun commandAndButtonsShareOneOrderedSequence() = runBlocking {
        val sender = OrderedMessageSender(this)
        val sink = RecordingSink()
        try {
            sender.startSession(sink)
            val expected = listOf(
                OutgoingMessage.Button("cross", "down"),
                OutgoingMessage.Command("task_manager"),
                OutgoingMessage.Button("cross", "up")
            )

            expected.forEach { sender.tryEnqueue(it) }

            assertEquals(expected.map { it.wireText }, sink.take(expected.size))
        } finally {
            sender.close()
        }
    }

    @Test
    fun printWriterSinkProducesUnchangedOrderedLfWireFormat() = runBlocking {
        val output = ByteArrayOutputStream()
        val printWriter = PrintWriter(OutputStreamWriter(output, Charsets.UTF_8))
        val sender = OrderedMessageSender(this)
        try {
            sender.startSession(PrintWriterMessageSink(printWriter))
            sender.tryEnqueue(OutgoingMessage.Button("cross", "down"))
            sender.tryEnqueue(OutgoingMessage.Command("task_manager"))
            sender.tryEnqueue(OutgoingMessage.Button("cross", "up"))

            val wireText = withTimeout(2_000) {
                var current: String
                do {
                    current = output.toString(Charsets.UTF_8.name())
                    if (current.count { it == '\n' } < 3) delay(1)
                } while (current.count { it == '\n' } < 3)
                current
            }

            assertEquals(
                "{\"button\":\"cross\",\"action\":\"down\"}\n" +
                    "{\"command\":\"task_manager\"}\n" +
                    "{\"button\":\"cross\",\"action\":\"up\"}\n",
                wireText
            )
        } finally {
            sender.close()
        }
    }

    @Test
    fun concurrentProducersDoNotInterleaveWrites() = runBlocking {
        val sender = OrderedMessageSender(this, queueCapacity = 1024)
        val sink = RecordingSink()
        try {
            sender.startSession(sink)
            val expected = ConcurrentHashMap.newKeySet<String>()

            coroutineScope {
                List(10) { producer ->
                    async(Dispatchers.Default) {
                        repeat(100) { item ->
                            val message = OutgoingMessage.Button("p$producer-$item", "down")
                            expected += message.wireText
                            assertEquals(EnqueueResult.ENQUEUED, sender.tryEnqueue(message))
                        }
                    }
                }.forEach { it.await() }
            }

            val actual = sink.take(1_000)
            assertEquals(expected, actual.toSet())
            assertEquals(1_000, actual.size)
            assertEquals(1, sink.maxConcurrentWrites.get())
        } finally {
            sender.close()
        }
    }

    @Test
    fun allWritesUseExactlyOneConsumer() = runBlocking {
        val sender = OrderedMessageSender(this)
        val sink = RecordingSink(writeDelayMillis = 2)
        try {
            sender.startSession(sink)
            repeat(50) { sender.tryEnqueue(OutgoingMessage.Button("cross", "down")) }

            sink.take(50)

            assertEquals(1, sink.maxConcurrentWrites.get())
        } finally {
            sender.close()
        }
    }

    @Test
    fun writeFailureStopsSessionAndReportsOnce() = runBlocking {
        val sender = OrderedMessageSender(this, queueCapacity = 16)
        val sink = FailingSink(failOnWrite = 3)
        try {
            val generation = sender.startSession(sink)
            repeat(10) { sender.tryEnqueue(OutgoingMessage.Button("cross", "down")) }

            val failure = withTimeout(2_000) { sender.failures.receive() }

            assertEquals(generation, failure.generation)
            assertTrue(failure.cause is IOException)
            assertEquals(3, sink.writeAttempts.get())
            assertEquals(
                EnqueueResult.NO_ACTIVE_SESSION,
                sender.tryEnqueue(OutgoingMessage.Button("cross", "up"))
            )
            assertNull(withTimeoutOrNull(100) { sender.failures.receive() })
        } finally {
            sender.close()
        }
    }

    @Test
    fun queueOverflowFailsSessionInsteadOfSilentlyDroppingUp() = runBlocking {
        val sender = OrderedMessageSender(this, queueCapacity = 2)
        val sink = BlockingSink()
        try {
            val generation = sender.startSession(sink)
            assertEquals(
                EnqueueResult.ENQUEUED,
                sender.tryEnqueue(OutgoingMessage.Button("cross", "down"))
            )
            sink.started.await()
            assertEquals(
                EnqueueResult.ENQUEUED,
                sender.tryEnqueue(OutgoingMessage.Button("circle", "down"))
            )
            assertEquals(
                EnqueueResult.ENQUEUED,
                sender.tryEnqueue(OutgoingMessage.Command("task_manager"))
            )

            assertEquals(
                EnqueueResult.SESSION_FAILED,
                sender.tryEnqueue(OutgoingMessage.Button("cross", "up"))
            )
            val failure = withTimeout(2_000) { sender.failures.receive() }
            assertEquals(generation, failure.generation)
            assertTrue(failure.cause is SenderQueueOverflowException)
            assertEquals(
                EnqueueResult.NO_ACTIVE_SESSION,
                sender.tryEnqueue(OutgoingMessage.Button("circle", "up"))
            )
        } finally {
            sink.release.complete(Unit)
            sender.close()
        }
    }

    @Test
    fun disconnectCancelsQueuedMessages() = runBlocking {
        val sender = OrderedMessageSender(this, queueCapacity = 4)
        val sink = BlockingSink()
        try {
            val generation = sender.startSession(sink)
            sender.tryEnqueue(OutgoingMessage.Button("cross", "down"))
            sink.started.await()
            sender.tryEnqueue(OutgoingMessage.Button("cross", "up"))

            sender.stopSession(generation)
            sink.release.complete(Unit)
            delay(100)

            assertEquals(
                listOf("{\"button\":\"cross\",\"action\":\"down\"}\n"),
                sink.completedWrites
            )
        } finally {
            sink.release.complete(Unit)
            sender.close()
        }
    }

    @Test
    fun reconnectDoesNotSendOldQueueIntoNewSession() = runBlocking {
        val sender = OrderedMessageSender(this, queueCapacity = 4)
        val oldSink = BlockingSink()
        val newSink = RecordingSink()
        try {
            sender.startSession(oldSink)
            sender.tryEnqueue(OutgoingMessage.Button("old", "down"))
            oldSink.started.await()
            sender.tryEnqueue(OutgoingMessage.Button("old", "up"))

            val newGeneration = sender.startSession(newSink)
            assertTrue(newGeneration > 1L)
            val newMessage = OutgoingMessage.Button("new", "down")
            sender.tryEnqueue(newMessage)
            oldSink.release.complete(Unit)

            assertEquals(listOf(newMessage.wireText), newSink.take(1))
            delay(100)
            assertEquals(
                listOf("{\"button\":\"old\",\"action\":\"down\"}\n"),
                oldSink.completedWrites
            )
        } finally {
            oldSink.release.complete(Unit)
            sender.close()
        }
    }

    @Test
    fun delayedOldSessionFailureCannotFailNewSession() = runBlocking {
        val sender = OrderedMessageSender(this)
        val oldSink = DelayedFailingSink()
        val newSink = RecordingSink()
        try {
            val oldGeneration = sender.startSession(oldSink)
            sender.tryEnqueue(OutgoingMessage.Button("old", "down"))
            oldSink.started.await()

            val newGeneration = sender.startSession(newSink)
            val newMessage = OutgoingMessage.Button("new", "down")
            sender.tryEnqueue(newMessage)
            oldSink.release.complete(Unit)

            assertTrue(newGeneration > oldGeneration)
            assertEquals(listOf(newMessage.wireText), newSink.take(1))
            assertNull(withTimeoutOrNull(200) { sender.failures.receive() })
            assertEquals(
                EnqueueResult.ENQUEUED,
                sender.tryEnqueue(OutgoingMessage.Button("new", "up"))
            )
        } finally {
            oldSink.release.complete(Unit)
            sender.close()
        }
    }

    private class RecordingSink(
        private val writeDelayMillis: Long = 0
    ) : OutgoingMessageSink {
        private val writes = Channel<String>(Channel.UNLIMITED)
        private val concurrentWrites = AtomicInteger()
        val maxConcurrentWrites = AtomicInteger()

        override suspend fun write(wireText: String) {
            val concurrent = concurrentWrites.incrementAndGet()
            maxConcurrentWrites.updateAndGet { previous -> maxOf(previous, concurrent) }
            try {
                if (writeDelayMillis > 0) delay(writeDelayMillis)
                writes.send(wireText)
            } finally {
                concurrentWrites.decrementAndGet()
            }
        }

        suspend fun take(count: Int): List<String> = withTimeout(5_000) {
            List(count) { writes.receive() }
        }
    }

    private class FailingSink(
        private val failOnWrite: Int
    ) : OutgoingMessageSink {
        val writeAttempts = AtomicInteger()

        override suspend fun write(wireText: String) {
            if (writeAttempts.incrementAndGet() == failOnWrite) {
                throw IOException("injected write failure")
            }
        }
    }

    private class BlockingSink : OutgoingMessageSink {
        val started = CompletableDeferred<Unit>()
        val release = CompletableDeferred<Unit>()
        val completedWrites = mutableListOf<String>()

        override suspend fun write(wireText: String) {
            started.complete(Unit)
            withContext(NonCancellable) { release.await() }
            completedWrites += wireText
        }
    }

    private class DelayedFailingSink : OutgoingMessageSink {
        val started = CompletableDeferred<Unit>()
        val release = CompletableDeferred<Unit>()

        override suspend fun write(wireText: String) {
            started.complete(Unit)
            withContext(NonCancellable) { release.await() }
            throw IOException("late failure from old session")
        }
    }
}
