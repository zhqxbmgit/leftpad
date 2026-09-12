package com.zhq.pad

internal const val ANDROID_17_API_LEVEL = 37
internal const val LOCAL_NETWORK_PERMISSION = "android.permission.ACCESS_LOCAL_NETWORK"

internal fun requiresLocalNetworkPermission(sdkInt: Int): Boolean =
    sdkInt >= ANDROID_17_API_LEVEL

internal fun canAttemptLanConnection(sdkInt: Int, permissionGranted: Boolean): Boolean =
    !requiresLocalNetworkPermission(sdkInt) || permissionGranted
