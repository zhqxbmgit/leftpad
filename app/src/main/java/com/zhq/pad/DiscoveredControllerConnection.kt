package com.zhq.pad

import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.NonCancellable
import kotlinx.coroutines.awaitCancellation
import kotlinx.coroutines.cancelAndJoin
import kotlinx.coroutines.coroutineScope
import kotlinx.coroutines.launch
import kotlinx.coroutines.withContext
import java.io.IOException
import java.io.PrintWriter
import java.net.InetSocketAddress
import java.net.Socket

/** One target's TCP lifetime. The caller uses collectLatest to await cleanup before replacing it. */
internal suspend fun runDiscoveredControllerConnection(
    target: ReceiverTarget,
    session: DiscoveredControllerSession,
    onConnected: () -> Unit,
    onDisconnected: () -> Unit,
    onFailure: (IOException?) -> Unit
) = coroutineScope {
    val connection = Socket()
    var writer: PrintWriter? = null
    var reader: kotlinx.coroutines.Job? = null
    try {
        withContext(Dispatchers.IO) {
            configureControllerSocket(connection)
            connection.connect(InetSocketAddress(target.address, target.port), 2_000)
            writer = PrintWriter(connection.getOutputStream(), true)
        }
        session.start(PrintWriterMessageSink(checkNotNull(writer)))
        onConnected()
        reader = launch(Dispatchers.IO) {
            try { while (connection.getInputStream().read() != -1) { } }
            catch (_: IOException) { }
            finally { onFailure(null) }
        }
        awaitCancellation()
    } catch (ex: IOException) {
        onFailure(ex)
    } finally {
        session.stop()
        onDisconnected()
        withContext(NonCancellable + Dispatchers.IO) {
            // Close the socket before taking PrintWriter's lock, unblocking both reader and writer.
            try { connection.close() } finally { writer?.close() }
            reader?.cancelAndJoin()
        }
    }
}
