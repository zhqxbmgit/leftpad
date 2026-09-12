package com.zhq.pad

import java.net.Socket

internal fun configureControllerSocket(socket: Socket) {
    socket.tcpNoDelay = true
}
