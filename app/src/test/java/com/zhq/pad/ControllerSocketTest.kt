package com.zhq.pad

import org.junit.Assert.assertTrue
import org.junit.Test
import java.net.Socket

class ControllerSocketTest {
    @Test
    fun controllerSocketDisablesNagle() {
        Socket().use { socket ->
            configureControllerSocket(socket)

            assertTrue(socket.tcpNoDelay)
        }
    }
}
