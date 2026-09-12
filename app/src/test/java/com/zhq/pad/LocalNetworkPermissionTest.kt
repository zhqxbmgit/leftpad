package com.zhq.pad

import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test

class LocalNetworkPermissionTest {
    @Test
    fun android17WithoutPermissionCannotAttemptLanConnection() {
        assertFalse(canAttemptLanConnection(ANDROID_17_API_LEVEL, permissionGranted = false))
    }

    @Test
    fun android17WithPermissionCanAttemptLanConnection() {
        assertTrue(canAttemptLanConnection(ANDROID_17_API_LEVEL, permissionGranted = true))
    }

    @Test
    fun android16DoesNotRequireLocalNetworkPermission() {
        assertFalse(requiresLocalNetworkPermission(ANDROID_17_API_LEVEL - 1))
        assertTrue(
            canAttemptLanConnection(
                ANDROID_17_API_LEVEL - 1,
                permissionGranted = false
            )
        )
    }
}
