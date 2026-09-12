package com.zhq.pad

import kotlinx.coroutines.CancellationException
import kotlinx.coroutines.CoroutineDispatcher
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.CoroutineStart
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.Job
import kotlinx.coroutines.channels.Channel
import kotlinx.coroutines.channels.ReceiveChannel
import kotlinx.coroutines.launch
import java.io.IOException
import java.io.PrintWriter

internal sealed interface OutgoingMessage {
    val wireText: String

    data class Button(val button: String, val action: String) : OutgoingMessage {
        override val wireText: String = "{\"button\":\"$button\",\"action\":\"$action\"}\n"
    }

    data class Command(val command: String) : OutgoingMessage {
        override val wireText: String = "{\"command\":\"$command\"}\n"
    }
}

internal fun interface OutgoingMessageSink {
    suspend fun write(wireText: String)
}

internal class PrintWriterMessageSink(
    private val writer: PrintWriter
) : OutgoingMessageSink {
    override suspend fun write(wireText: String) {
        writer.print(wireText)
        writer.flush()
        if (writer.checkError()) throw IOException("TCP writer reported a send failure")
    }
}

internal data class SenderFailure(
    val generation: Long,
    val cause: Throwable
)

internal enum class EnqueueResult {
    ENQUEUED,
    NO_ACTIVE_SESSION,
    SESSION_FAILED
}

internal class SenderQueueOverflowException(capacity: Int) :
    IOException("Outgoing message queue reached its $capacity-message limit")

internal class OrderedMessageSender(
    private val scope: CoroutineScope,
    private val queueCapacity: Int = DEFAULT_QUEUE_CAPACITY,
    private val dispatcher: CoroutineDispatcher = Dispatchers.IO
) : AutoCloseable {
    companion object {
        const val DEFAULT_QUEUE_CAPACITY = 128
    }

    private val lock = Any()
    private val failureChannel = Channel<SenderFailure>(Channel.UNLIMITED)
    private var nextGeneration = 0L
    private var currentSession: Session? = null
    private var closed = false

    val failures: ReceiveChannel<SenderFailure> = failureChannel

    init {
        require(queueCapacity > 0) { "queueCapacity must be positive" }
    }

    fun startSession(sink: OutgoingMessageSink): Long {
        val newSession: Session
        val previousSession: Session?
        synchronized(lock) {
            check(!closed) { "OrderedMessageSender is closed" }
            previousSession = currentSession
            newSession = Session(
                generation = ++nextGeneration,
                sink = sink,
                queue = Channel(queueCapacity)
            )
            newSession.job = scope.launch(dispatcher, start = CoroutineStart.LAZY) {
                consume(newSession)
            }
            currentSession = newSession
        }

        previousSession?.cancel()
        newSession.job.start()
        return newSession.generation
    }

    fun tryEnqueue(message: OutgoingMessage): EnqueueResult {
        val session = synchronized(lock) { currentSession }
            ?: return EnqueueResult.NO_ACTIVE_SESSION
        val result = session.queue.trySend(message)
        if (result.isSuccess) return EnqueueResult.ENQUEUED
        if (result.isClosed) return EnqueueResult.NO_ACTIVE_SESSION

        failSession(session, SenderQueueOverflowException(queueCapacity))
        return EnqueueResult.SESSION_FAILED
    }

    fun stopSession(generation: Long) {
        val session = synchronized(lock) {
            currentSession
                ?.takeIf { it.generation == generation }
                ?.also { currentSession = null }
        }
        session?.cancel()
    }

    override fun close() {
        val session = synchronized(lock) {
            if (closed) return
            closed = true
            currentSession.also { currentSession = null }
        }
        session?.cancel()
        failureChannel.close()
    }

    private suspend fun consume(session: Session) {
        try {
            for (message in session.queue) {
                session.sink.write(message.wireText)
            }
        } catch (error: CancellationException) {
            throw error
        } catch (error: Throwable) {
            failSession(session, error)
        }
    }

    private fun failSession(session: Session, cause: Throwable) {
        val isCurrent = synchronized(lock) {
            if (currentSession !== session) false
            else {
                currentSession = null
                true
            }
        }
        if (!isCurrent) return

        session.cancel()
        failureChannel.trySend(SenderFailure(session.generation, cause))
    }

    private class Session(
        val generation: Long,
        val sink: OutgoingMessageSink,
        val queue: Channel<OutgoingMessage>
    ) {
        lateinit var job: Job

        fun cancel() {
            queue.cancel()
            job.cancel()
        }
    }
}
