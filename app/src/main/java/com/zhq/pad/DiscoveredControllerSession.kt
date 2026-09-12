package com.zhq.pad

/** Main-thread input gate; the sender remains the only queue and writer owner. */
internal class DiscoveredControllerSession(private val sender: OrderedMessageSender) {
    var generation = 0L
        private set
    private val pressed = mutableSetOf<String>()

    fun start(sink: OutgoingMessageSink) {
        stop()
        generation = sender.startSession(sink)
    }

    fun stop() {
        sender.stopSession(generation)
        generation = 0L
        pressed.clear()
    }

    fun button(button: String, action: String) {
        if (generation == 0L) return
        when (action) {
            "down" -> pressed.add(button)
            // Releasing a finger held across a target switch must not send an old UP to the new PC.
            "up", "stop" -> if (!pressed.remove(button)) return
        }
        sender.tryEnqueue(OutgoingMessage.Button(button, action))
    }

    fun command(command: String) {
        if (generation != 0L) sender.tryEnqueue(OutgoingMessage.Command(command))
    }
}
