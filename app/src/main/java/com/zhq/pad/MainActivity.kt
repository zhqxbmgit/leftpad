package com.zhq.pad

import android.app.Activity
import android.content.BroadcastReceiver
import android.content.Context
import android.content.Intent
import android.content.IntentFilter
import android.content.pm.PackageManager
import android.os.BatteryManager
import android.os.Build
import android.os.Bundle
import android.util.Log
import android.view.WindowManager
import androidx.activity.ComponentActivity
import androidx.activity.compose.rememberLauncherForActivityResult
import androidx.activity.compose.setContent
import androidx.activity.enableEdgeToEdge
import androidx.activity.result.contract.ActivityResultContracts
import androidx.compose.animation.AnimatedVisibility
import androidx.compose.animation.core.animateFloatAsState
import androidx.compose.animation.slideInHorizontally
import androidx.compose.animation.slideOutHorizontally
import androidx.compose.foundation.BorderStroke
import androidx.compose.foundation.Canvas
import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.clickable
import androidx.compose.foundation.gestures.detectDragGestures
import androidx.compose.foundation.gestures.detectTapGestures
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.verticalScroll
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.automirrored.filled.ArrowForward
import androidx.compose.material.icons.filled.*
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.blur
import androidx.compose.ui.draw.clip
import androidx.compose.ui.draw.scale
import androidx.compose.ui.geometry.CornerRadius
import androidx.compose.ui.geometry.Offset
import androidx.compose.ui.geometry.Size
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.Path
import androidx.compose.ui.graphics.StrokeCap
import androidx.compose.ui.graphics.drawscope.Fill
import androidx.compose.ui.graphics.drawscope.Stroke
import androidx.compose.ui.input.pointer.pointerInput
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.res.painterResource
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.Dp
import androidx.compose.ui.unit.IntOffset
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import androidx.core.content.ContextCompat
import androidx.core.view.WindowCompat
import androidx.core.view.WindowInsetsCompat
import androidx.core.view.WindowInsetsControllerCompat
import com.zhq.pad.ui.theme.PadTheme
import kotlinx.coroutines.flow.collectLatest
import kotlin.math.roundToInt

// --- 极致赛博霓虹配色 (色彩纯净度强化) ---
object NeonTheme {
    val Background = Color(0xFF020205)
    val PanelBg = Color(0xEE0B0B0E)
    val ButtonInner = Color(0xFF020205) // 内部保持极黑，衬托边框
    
    val Triangle = Color(0xFF00FF41) // 荧光绿
    val Square = Color(0xFFFF00FF)   // 荧光粉紫
    val Circle = Color(0xFFFF2A2A)   // 荧光红
    val Cross = Color(0xFF00E5FF)    // 荧光青蓝
    val Accent = Color(0xFFB15CFF)   // 霓虹紫
    val Battery = Color(0xFF00FF41)
    val TaskMgr = Color(0xFFFFD600)  // 亮黄
    val Move = Color(0xFFFF8A00)
}

data class ButtonConfig(
    val id: String,
    val label: String,
    val color: Color,
    var xRatio: Float,
    var yRatio: Float,
    var wRatio: Float,
    var hRatio: Float,
    var isVisible: Boolean = true
)

enum class MainButtonAction(
    val protocolButtonKey: String,
    val mainLabel: String,
    val displayColor: Color
) {
    MOVE(protocolButtonKey = "move", mainLabel = "MOVE", displayColor = NeonTheme.Move),
    CROSS(protocolButtonKey = "cross", mainLabel = "X", displayColor = NeonTheme.Cross),
    CIRCLE(protocolButtonKey = "circle", mainLabel = "O", displayColor = NeonTheme.Circle),
    SQUARE(protocolButtonKey = "square", mainLabel = "□", displayColor = NeonTheme.Square),
    TRIANGLE(protocolButtonKey = "triangle", mainLabel = "△", displayColor = NeonTheme.Triangle),
    L1(protocolButtonKey = "l1", mainLabel = "L1", displayColor = NeonTheme.Accent),
    R1(protocolButtonKey = "r1", mainLabel = "R1", displayColor = NeonTheme.Accent),
    L2(protocolButtonKey = "l2", mainLabel = "L2", displayColor = NeonTheme.Accent),
    R2(protocolButtonKey = "r2", mainLabel = "R2", displayColor = NeonTheme.Accent),
    L3(protocolButtonKey = "l3", mainLabel = "L3", displayColor = NeonTheme.Accent),
    R3(protocolButtonKey = "r3", mainLabel = "R3", displayColor = NeonTheme.Accent);

    companion object {
        fun fromStoredValue(value: String?): MainButtonAction =
            values().firstOrNull { it.name == value } ?: MOVE
    }
}

private const val MAIN_BUTTON_PREFERENCES = "main_button_preferences"
private const val MAIN_BUTTON_MODE_KEY = "main_button_mode"

class MainActivity : ComponentActivity() {
    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        WindowCompat.setDecorFitsSystemWindows(window, false)
        val controller = WindowInsetsControllerCompat(window, window.decorView)
        controller.hide(WindowInsetsCompat.Type.systemBars())
        controller.systemBarsBehavior = WindowInsetsControllerCompat.BEHAVIOR_SHOW_TRANSIENT_BARS_BY_SWIPE
        enableEdgeToEdge()
        setContent { PadTheme { Scaffold(modifier = Modifier.fillMaxSize()) { innerPadding -> ControllerScreen(modifier = Modifier.padding(innerPadding)) } } }
    }
}

@Composable
fun ControllerScreen(modifier: Modifier = Modifier) {
    val context = LocalContext.current
    val scope = rememberCoroutineScope()
    
    DisposableEffect(Unit) {
        val activity = context as? Activity
        activity?.window?.addFlags(WindowManager.LayoutParams.FLAG_KEEP_SCREEN_ON)
        onDispose { activity?.window?.clearFlags(WindowManager.LayoutParams.FLAG_KEEP_SCREEN_ON) }
    }

    var batteryLevel by remember { mutableStateOf(100) }
    DisposableEffect(Unit) {
        val receiver = object : BroadcastReceiver() { override fun onReceive(context: Context?, intent: Intent?) { val level = intent?.getIntExtra(BatteryManager.EXTRA_LEVEL, -1) ?: -1; val scale = intent?.getIntExtra(BatteryManager.EXTRA_SCALE, -1) ?: -1; if (level != -1 && scale != -1) batteryLevel = (level * 100 / scale.toFloat()).toInt() } }
        context.registerReceiver(receiver, IntentFilter(Intent.ACTION_BATTERY_CHANGED))
        onDispose { context.unregisterReceiver(receiver) }
    }

    val layoutPrefs = remember { context.getSharedPreferences("button_layout_v4_ratio", Context.MODE_PRIVATE) }
    val mainButtonPrefs = remember { context.getSharedPreferences(MAIN_BUTTON_PREFERENCES, Context.MODE_PRIVATE) }

    var connectionStatus by remember { mutableStateOf("搜索中") }
    var isConnected by remember { mutableStateOf(false) }
    val orderedMessageSender = remember(scope) { OrderedMessageSender(scope) }
    val controllerSession = remember(orderedMessageSender) { DiscoveredControllerSession(orderedMessageSender) }
    val discovery = remember { WifiReceiverDiscovery(context.applicationContext) }
    val permissionPrefs = remember { context.getSharedPreferences("lan_permission", Context.MODE_PRIVATE) }
    fun hasLanPermission(): Boolean = canAttemptLanConnection(Build.VERSION.SDK_INT,
        ContextCompat.checkSelfPermission(context, LOCAL_NETWORK_PERMISSION) == PackageManager.PERMISSION_GRANTED)
    var lanAllowed by remember { mutableStateOf(hasLanPermission()) }
    var permissionRequestInFlight by remember { mutableStateOf(false) }
    val permissionLauncher = rememberLauncherForActivityResult(ActivityResultContracts.RequestPermission()) { granted ->
        permissionRequestInFlight = false
        lanAllowed = canAttemptLanConnection(Build.VERSION.SDK_INT, granted)
        connectionStatus = if (lanAllowed) "搜索中" else "需要本地网络权限"
    }
    fun requestLanPermission() {
        if (permissionRequestInFlight) return
        permissionRequestInFlight = true
        permissionPrefs.edit().putBoolean("requested", true).apply()
        permissionLauncher.launch(LOCAL_NETWORK_PERMISSION)
    }
    LaunchedEffect(Unit) {
        if (!lanAllowed) {
            connectionStatus = "需要本地网络权限"
            if (!permissionPrefs.getBoolean("requested", false)) requestLanPermission()
        }
    }
    val lifecycleOwner = androidx.lifecycle.compose.LocalLifecycleOwner.current
    DisposableEffect(lifecycleOwner) {
        val observer = androidx.lifecycle.LifecycleEventObserver { _, event ->
            if (event == androidx.lifecycle.Lifecycle.Event.ON_RESUME) lanAllowed = hasLanPermission()
        }
        lifecycleOwner.lifecycle.addObserver(observer)
        onDispose { lifecycleOwner.lifecycle.removeObserver(observer) }
    }
    DisposableEffect(orderedMessageSender) {
        onDispose { controllerSession.stop(); orderedMessageSender.close() }
    }
    LaunchedEffect(lanAllowed) {
        if (lanAllowed) discovery.run()
        else connectionStatus = "需要本地网络权限"
    }
    LaunchedEffect(lanAllowed, discovery) {
        if (!lanAllowed) return@LaunchedEffect
        // collectLatest waits for the old block's finally to finish before connecting a new target.
        discovery.target.collectLatest { target ->
            connectionStatus = "搜索中"
            if (target == null) return@collectLatest
            runDiscoveredControllerConnection(target, controllerSession,
                onConnected = {
                    isConnected = true
                    connectionStatus = "已连接 · ${target.address.hostAddress}"
                    Log.i("LeftPadDiscovery", "TCP connected source=${target.address.hostAddress} generation=${controllerSession.generation}")
                },
                onDisconnected = {
                    isConnected = false
                    connectionStatus = if (lanAllowed) "搜索中" else "需要本地网络权限"
                },
                onFailure = { error ->
                    if (error != null) Log.w("LeftPadDiscovery", "TCP connect failed", error)
                    discovery.connectionFailed(target)
                })
        }
    }
    LaunchedEffect(orderedMessageSender) {
        for (failure in orderedMessageSender.failures) {
            if (failure.generation == controllerSession.generation) {
                controllerSession.stop()
                isConnected = false
                discovery.target.value?.let { discovery.connectionFailed(it) }
            }
            Log.e("LeftPadSender", "Outgoing sender session ${failure.generation} failed", failure.cause)
        }
    }
    var showPanel by remember { mutableStateOf(false) }
    var isEditMode by remember { mutableStateOf(false) }
    var showAddMenu by remember { mutableStateOf(false) }
    var mainButtonAction by remember {
        mutableStateOf(
            MainButtonAction.fromStoredValue(
                mainButtonPrefs.getString(MAIN_BUTTON_MODE_KEY, MainButtonAction.MOVE.name)
            )
        )
    }
    var mainButtonPressed by remember { mutableStateOf(false) }
    var pressedButtonKey by remember { mutableStateOf<String?>(null) }
    var showActionSelector by remember { mutableStateOf(false) }
    
    val buttonConfigs = remember { mutableStateListOf<ButtonConfig>() }

    fun getDefaultConfigs() = listOf(
        ButtonConfig("triangle", "△", NeonTheme.Triangle, 0.75f, 0.35f, 0.12f, 0.20f),
        ButtonConfig("square", "▢", NeonTheme.Square, 0.63f, 0.50f, 0.12f, 0.20f),
        ButtonConfig("circle", "○", NeonTheme.Circle, 0.87f, 0.50f, 0.12f, 0.20f),
        ButtonConfig("cross", "✖", NeonTheme.Cross, 0.75f, 0.65f, 0.12f, 0.20f),
        ButtonConfig("task_manager", "TM", NeonTheme.TaskMgr, 0.15f, 0.35f, 0.10f, 0.12f),
        ButtonConfig("move", "MOVE", NeonTheme.Move, 0.18f, 0.62f, 0.16f, 0.20f)
    )

    fun saveLayout() {
        layoutPrefs.edit().apply {
            buttonConfigs.forEach { config ->
                putFloat("${config.id}_xRatio", config.xRatio)
                putFloat("${config.id}_yRatio", config.yRatio)
                putFloat("${config.id}_wRatio", config.wRatio)
                putFloat("${config.id}_hRatio", config.hRatio)
                putBoolean("${config.id}_isVisible", config.isVisible)
            }
            commit()
        }
    }

    fun loadLayout() {
        buttonConfigs.clear()
        val defaultList = getDefaultConfigs()
        var hasSaved = false
        defaultList.forEach { if (layoutPrefs.contains("${it.id}_isVisible")) hasSaved = true }

        if (hasSaved) {
            defaultList.forEach { def ->
                buttonConfigs.add(ButtonConfig(
                    id = def.id, label = def.label, color = def.color,
                    xRatio = layoutPrefs.getFloat("${def.id}_xRatio", def.xRatio),
                    yRatio = layoutPrefs.getFloat("${def.id}_yRatio", def.yRatio),
                    wRatio = layoutPrefs.getFloat("${def.id}_wRatio", def.wRatio),
                    hRatio = layoutPrefs.getFloat("${def.id}_hRatio", def.hRatio),
                    isVisible = layoutPrefs.getBoolean("${def.id}_isVisible", def.isVisible)
                ))
            }
        } else {
            buttonConfigs.addAll(defaultList)
        }
    }

    LaunchedEffect(Unit) { loadLayout() }

    fun sendMessage(button: String, action: String, allowWhenUiBlocked: Boolean = false) {
        if (!allowWhenUiBlocked && (isEditMode || showPanel)) return
        if (isConnected) {
            controllerSession.button(button, action)
        }
    }
    fun sendSystemCommand(command: String) {
        if (isEditMode || showPanel) return
        if (isConnected) {
            controllerSession.command(command)
        }
    }

    fun selectMainButtonAction(nextAction: MainButtonAction) {
        if (isEditMode || mainButtonPressed || pressedButtonKey != null) return
        if (nextAction == mainButtonAction) {
            showActionSelector = false
            return
        }

        if (mainButtonAction == MainButtonAction.MOVE && nextAction != MainButtonAction.MOVE) {
            sendMessage(MainButtonAction.MOVE.protocolButtonKey, "stop")
        }

        mainButtonAction = nextAction
        mainButtonPrefs.edit()
            .putString(MAIN_BUTTON_MODE_KEY, nextAction.name)
            .apply()
        showActionSelector = false
    }

    Box(modifier = modifier.fillMaxSize().background(NeonTheme.Background)) {
        BoxWithConstraints(modifier = Modifier.fillMaxSize()) {
            val areaW = maxWidth; val areaH = maxHeight
            buttonConfigs.forEachIndexed { index, config ->
                if (config.isVisible) {
                    val isMainButton = config.id == "move"
                    val displayLabel = if (isMainButton) mainButtonAction.mainLabel else config.label
                    val displayColor = if (isMainButton) mainButtonAction.displayColor else config.color
                    SharpNeonButton(
                        config = config, isEditMode = isEditMode,
                        parentW = areaW, parentH = areaH,
                        displayLabel = displayLabel,
                        displayColor = displayColor,
                        modeSwitchLabel = if (isMainButton) "⋯" else null,
                        modeSwitchEnabled = !isEditMode && !mainButtonPressed && pressedButtonKey == null,
                        onModeSwitch = {
                            if (!isEditMode && !mainButtonPressed && pressedButtonKey == null) {
                                showActionSelector = true
                            }
                        },
                        onUpdate = { updated -> buttonConfigs[index] = updated },
                        onPress = {
                            when {
                                config.id == "task_manager" -> sendSystemCommand("task_manager")
                                isMainButton -> {
                                    val buttonKey = mainButtonAction.protocolButtonKey
                                    pressedButtonKey = buttonKey
                                    mainButtonPressed = true
                                    sendMessage(buttonKey, "down")
                                }
                                else -> sendMessage(config.id, "down")
                            }
                        },
                        onRelease = {
                            when {
                                config.id == "task_manager" -> Unit
                                isMainButton -> {
                                    val buttonKey = pressedButtonKey
                                    pressedButtonKey = null
                                    mainButtonPressed = false
                                    if (buttonKey != null) {
                                        sendMessage(buttonKey, "up", allowWhenUiBlocked = true)
                                    }
                                }
                                else -> sendMessage(config.id, "up", allowWhenUiBlocked = true)
                            }
                        },
                        onDelete = { buttonConfigs[index] = config.copy(isVisible = false); saveLayout() }
                    )
                }
            }
        }

        // 顶部 Header
        Row(modifier = Modifier.align(Alignment.TopStart).padding(20.dp), verticalAlignment = Alignment.CenterVertically) {
            IconButton(onClick = { showPanel = true }, modifier = Modifier.size(32.dp).border(1.dp, NeonTheme.Accent.copy(alpha = 0.5f), CircleShape)) {
                Icon(Icons.Default.Settings, null, tint = NeonTheme.Accent, modifier = Modifier.size(18.dp))
            }
            Spacer(modifier = Modifier.width(16.dp)); Text("DS4 模式", color = Color.Gray, fontSize = 11.sp, fontWeight = FontWeight.Light); Spacer(modifier = Modifier.width(12.dp)); Box(modifier = Modifier.size(6.dp).clip(CircleShape).background(if (isConnected) NeonTheme.Triangle else NeonTheme.Circle).blur(if (isConnected) 4.dp else 0.dp))
        }

        Text(connectionStatus, modifier = Modifier.align(Alignment.TopEnd).padding(24.dp),
            color = if (isConnected) NeonTheme.Triangle else Color.Gray, fontSize = 12.sp)

        // 电池电量
        Column(modifier = Modifier.align(Alignment.BottomStart).padding(24.dp)) {
            Text("电量", color = Color.Gray, fontSize = 9.sp, fontWeight = FontWeight.Bold); Spacer(modifier = Modifier.height(6.dp))
            Row(verticalAlignment = Alignment.CenterVertically) {
                Canvas(modifier = Modifier.size(width = 32.dp, height = 14.dp)) {
                    drawRoundRect(color = NeonTheme.Battery.copy(alpha = 0.5f), size = Size(28.dp.toPx(), 14.dp.toPx()), cornerRadius = CornerRadius(2.dp.toPx()), style = Stroke(width = 1.dp.toPx())); drawRect(color = NeonTheme.Battery.copy(alpha = 0.5f), topLeft = Offset(28.dp.toPx(), 4.dp.toPx()), size = Size(2.dp.toPx(), 6.dp.toPx())); drawRect(color = NeonTheme.Battery, topLeft = Offset(2.dp.toPx(), 2.dp.toPx()), size = Size((24 * (batteryLevel / 100f)).dp.toPx(), 10.dp.toPx()))
                }
                Spacer(modifier = Modifier.width(10.dp)); Text("$batteryLevel%", color = NeonTheme.Battery, fontSize = 13.sp, fontWeight = FontWeight.Medium)
            }
        }

        // 侧滑控制面板
        AnimatedVisibility(visible = showPanel, enter = slideInHorizontally(initialOffsetX = { -it }), exit = slideOutHorizontally(targetOffsetX = { -it })) {
            Box(modifier = Modifier.fillMaxSize().background(Color(0x99000000)).clickable { showPanel = false }) {
                Surface(modifier = Modifier.fillMaxHeight().width(260.dp).clickable(enabled = false) { }, color = NeonTheme.PanelBg, border = BorderStroke(1.dp, NeonTheme.Accent.copy(alpha = 0.3f)), shape = RoundedCornerShape(topEnd = 24.dp, bottomEnd = 24.dp)) {
                    Column(modifier = Modifier.fillMaxSize().padding(24.dp).verticalScroll(rememberScrollState())) {
                        Row(modifier = Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.SpaceBetween, verticalAlignment = Alignment.CenterVertically) { Text("控制台", color = NeonTheme.Accent, fontWeight = FontWeight.Bold, letterSpacing = 2.sp); IconButton(onClick = { showPanel = false }) { Icon(Icons.Default.Close, null, tint = Color.Gray) } }
                        HorizontalDivider(color = NeonTheme.Accent.copy(alpha = 0.2f)); Spacer(modifier = Modifier.height(20.dp)); Text("LeftPad Receiver", color = Color.White, fontSize = 18.sp, fontWeight = FontWeight.Bold); Text("状态: $connectionStatus", fontSize = 11.sp, color = if (isConnected) NeonTheme.Triangle else Color.Gray)
                        if (!lanAllowed) {
                            OutlinedButton(onClick = { requestLanPermission() }, enabled = !permissionRequestInFlight) {
                                Text("允许本地网络访问", color = NeonTheme.Accent)
                            }
                        }
                        Spacer(modifier = Modifier.height(12.dp)); Button(onClick = { if (isEditMode) saveLayout(); isEditMode = !isEditMode }, modifier = Modifier.fillMaxWidth().height(44.dp), colors = ButtonDefaults.buttonColors(containerColor = Color.Transparent), border = BorderStroke(1.dp, if (isEditMode) NeonTheme.Triangle else NeonTheme.Accent), shape = RoundedCornerShape(8.dp)) { Text(if (isEditMode) "保存布局" else "编辑布局", color = if (isEditMode) NeonTheme.Triangle else NeonTheme.Accent) }
                        if (isEditMode) {
                            Spacer(modifier = Modifier.height(16.dp)); Row(modifier = Modifier.fillMaxWidth()) {
                                Box(modifier = Modifier.weight(1f)) {
                                    OutlinedButton(onClick = { showAddMenu = true }, modifier = Modifier.fillMaxWidth().padding(end = 4.dp), border = BorderStroke(1.dp, NeonTheme.Accent)) { Text("添加", fontSize = 11.sp, color = NeonTheme.Accent) }
                                    DropdownMenu(expanded = showAddMenu, onDismissRequest = { showAddMenu = false }, modifier = Modifier.background(NeonTheme.PanelBg)) {
                                        buttonConfigs.filter { !it.isVisible }.forEach { config -> DropdownMenuItem(text = { Text(config.label, color = Color.White) }, onClick = { val index = buttonConfigs.indexOfFirst { it.id == config.id }; buttonConfigs[index] = config.copy(isVisible = true, xRatio = 0.5f, yRatio = 0.5f); showAddMenu = false; saveLayout() }) }
                                    }
                                }
                                OutlinedButton(onClick = { buttonConfigs.clear(); buttonConfigs.addAll(getDefaultConfigs()); saveLayout() }, modifier = Modifier.weight(1f).padding(start = 4.dp), border = BorderStroke(1.dp, Color.Gray)) { Text("重置", fontSize = 11.sp, color = Color.Gray) }
                            }
                        }

                    }
                }
            }
        }

        if (showActionSelector) {
            MainActionSelector(
                currentAction = mainButtonAction,
                enabled = !isEditMode && !mainButtonPressed && pressedButtonKey == null,
                onSelect = ::selectMainButtonAction,
                onDismiss = { showActionSelector = false }
            )
        }
    }
}

@Composable
private fun MainActionSelector(
    currentAction: MainButtonAction,
    enabled: Boolean,
    onSelect: (MainButtonAction) -> Unit,
    onDismiss: () -> Unit
) {
    val actionRows = listOf(
        listOf(
            MainButtonAction.MOVE,
            MainButtonAction.CROSS,
            MainButtonAction.CIRCLE,
            MainButtonAction.SQUARE,
            MainButtonAction.TRIANGLE
        ),
        listOf(MainButtonAction.L1, MainButtonAction.R1),
        listOf(MainButtonAction.L2, MainButtonAction.R2),
        listOf(MainButtonAction.L3, MainButtonAction.R3)
    )

    AlertDialog(
        onDismissRequest = onDismiss,
        title = { Text("Main Action", color = NeonTheme.Accent) },
        text = {
            Column(verticalArrangement = Arrangement.spacedBy(8.dp)) {
                actionRows.forEach { row ->
                    Row(
                        modifier = Modifier.fillMaxWidth(),
                        horizontalArrangement = Arrangement.spacedBy(6.dp)
                    ) {
                        row.forEach { action ->
                            val selected = action == currentAction
                            OutlinedButton(
                                onClick = { onSelect(action) },
                                enabled = enabled,
                                modifier = Modifier.weight(1f),
                                border = BorderStroke(
                                    width = if (selected) 2.dp else 1.dp,
                                    color = action.displayColor.copy(alpha = if (enabled) 0.9f else 0.4f)
                                ),
                                colors = ButtonDefaults.outlinedButtonColors(
                                    containerColor = if (selected) {
                                        action.displayColor.copy(alpha = 0.18f)
                                    } else {
                                        Color.Transparent
                                    },
                                    contentColor = action.displayColor
                                ),
                                contentPadding = PaddingValues(horizontal = 4.dp, vertical = 8.dp)
                            ) {
                                Text(
                                    text = action.mainLabel,
                                    fontSize = if (action == MainButtonAction.MOVE) 10.sp else 13.sp,
                                    fontWeight = FontWeight.Bold,
                                    maxLines = 1
                                )
                            }
                        }
                    }
                }
            }
        },
        confirmButton = {
            TextButton(onClick = onDismiss) {
                Text("Close", color = NeonTheme.Accent)
            }
        },
        containerColor = NeonTheme.PanelBg
    )
}

@Composable
fun SharpNeonButton(
    config: ButtonConfig, isEditMode: Boolean,
    parentW: Dp, parentH: Dp,
    displayLabel: String = config.label,
    displayColor: Color = config.color,
    modeSwitchLabel: String? = null,
    modeSwitchEnabled: Boolean = true,
    onModeSwitch: () -> Unit = {},
    onUpdate: (ButtonConfig) -> Unit,
    onPress: () -> Unit, onRelease: () -> Unit, onDelete: () -> Unit
) {
    val currentConfig by rememberUpdatedState(config)
    val currentOnPress by rememberUpdatedState(onPress)
    val currentOnRelease by rememberUpdatedState(onRelease)
    val btnW = parentW * config.wRatio; val btnH = parentH * config.hRatio
    val posX = parentW * config.xRatio; val posY = parentH * config.yRatio
    
    var isPressed by remember { mutableStateOf(false) }
    
    // 视觉反馈参数：追求极速与利落
    val glowAlpha by animateFloatAsState(if (isPressed) 1.0f else 0.7f, label = "glow")
    val strokeWidth by animateFloatAsState(if (isPressed) 2.5f else 1.2f, label = "stroke")

    Box(
        modifier = Modifier
            .offset { IntOffset(posX.toPx().roundToInt(), posY.toPx().roundToInt()) }
            .size(width = btnW, height = btnH)
    ) {
        Canvas(
            modifier = Modifier.fillMaxSize()
                .pointerInput(isEditMode, config.id) {
                    if (isEditMode) {
                        detectDragGestures { change, dragAmount ->
                            change.consume()
                            val newX = currentConfig.xRatio + dragAmount.x / parentW.toPx()
                            val newY = currentConfig.yRatio + dragAmount.y / parentH.toPx()
                            onUpdate(currentConfig.copy(xRatio = newX, yRatio = newY))
                        }
                    } else {
                        detectTapGestures(onPress = {
                            isPressed = true
                            try {
                                currentOnPress()
                                tryAwaitRelease()
                            } finally {
                                isPressed = false
                                currentOnRelease()
                            }
                        })
                    }
                }
        ) {
            val corner = 12.dp.toPx()
            
            // 1. 深色填充
            drawRoundRect(color = NeonTheme.ButtonInner, size = size, cornerRadius = CornerRadius(corner), style = Fill)
            
            // 2. 克制、贴边的微弱发光 (锐化霓虹感)
            drawRoundRect(
                color = displayColor.copy(alpha = glowAlpha * 0.3f),
                size = Size(size.width + 4.dp.toPx(), size.height + 4.dp.toPx()),
                topLeft = Offset(-2.dp.toPx(), -2.dp.toPx()),
                cornerRadius = CornerRadius(corner + 2.dp.toPx()),
                style = Stroke(width = 4.dp.toPx())
            )

            // 3. 高亮细描边 (核心视觉)
            drawRoundRect(
                color = displayColor.copy(alpha = glowAlpha),
                size = size,
                cornerRadius = CornerRadius(corner),
                style = Stroke(width = strokeWidth.dp.toPx())
            )

            // 4. 高亮居中图标
            val center = Offset(size.width / 2, size.height / 2)
            val iconScale = 0.38f
            val baseSize = Math.min(size.width, size.height) * iconScale
            val activeColor = displayColor.copy(alpha = glowAlpha)

            when (config.id) {
                "triangle" -> {
                    val path = Path().apply {
                        moveTo(center.x, center.y - baseSize * 0.55f)
                        lineTo(center.x - baseSize * 0.5f, center.y + baseSize * 0.35f)
                        lineTo(center.x + baseSize * 0.5f, center.y + baseSize * 0.35f)
                        close()
                    }
                    drawPath(path, activeColor, style = Fill)
                }
                "square" -> {
                    val s = baseSize * 0.85f
                    drawRect(activeColor, topLeft = Offset(center.x - s/2, center.y - s/2), size = Size(s, s), style = Fill)
                }
                "circle" -> {
                    // 圆形按钮中间用一个实心圆点
                    drawCircle(activeColor, radius = baseSize * 0.48f, center = center, style = Fill)
                }
                "cross" -> {
                    val r = baseSize * 0.48f
                    drawLine(activeColor, Offset(center.x - r, center.y - r), Offset(center.x + r, center.y + r), strokeWidth = 3.5.dp.toPx(), cap = StrokeCap.Round)
                    drawLine(activeColor, Offset(center.x + r, center.y - r), Offset(center.x - r, center.y + r), strokeWidth = 3.5.dp.toPx(), cap = StrokeCap.Round)
                }
            }
        }

        if (config.id == "task_manager") {
            Text(text = "TM", color = displayColor.copy(alpha = glowAlpha), fontSize = 15.sp, fontWeight = FontWeight.ExtraBold, modifier = Modifier.align(Alignment.Center))
        }

        if (config.id == "move") {
            Text(
                text = displayLabel,
                color = displayColor.copy(alpha = glowAlpha),
                fontSize = if (displayLabel == "MOVE") 15.sp else 24.sp,
                fontWeight = FontWeight.ExtraBold,
                modifier = Modifier.align(Alignment.Center)
            )
        }

        if (modeSwitchLabel != null) {
            val switchEnabled = !isEditMode && modeSwitchEnabled
            Box(
                modifier = Modifier
                    .align(Alignment.TopEnd)
                    .padding(6.dp)
                    .size(32.dp)
                    .clip(RoundedCornerShape(8.dp))
                    .background(NeonTheme.ButtonInner)
                    .border(1.dp, displayColor.copy(alpha = if (switchEnabled) 0.9f else 0.4f), RoundedCornerShape(8.dp))
                    .clickable(enabled = switchEnabled) { onModeSwitch() },
                contentAlignment = Alignment.Center
            ) {
                Text(
                    text = modeSwitchLabel,
                    color = displayColor.copy(alpha = if (switchEnabled) 1f else 0.4f),
                    fontSize = if (modeSwitchLabel == "MOVE") 8.sp else 13.sp,
                    fontWeight = FontWeight.Bold,
                    textAlign = TextAlign.Center,
                    maxLines = 1
                )
            }
        }

        if (isEditMode) {
            IconButton(onClick = onDelete, modifier = Modifier.align(Alignment.TopStart).size(26.dp).offset(x = (-4).dp, y = (-4).dp).background(Color.Red, CircleShape)) { Icon(Icons.Default.Close, null, tint = Color.White, modifier = Modifier.size(16.dp)) }
            Box(modifier = Modifier.align(Alignment.CenterEnd).size(26.dp).offset(x = 6.dp).background(Color.White, CircleShape).pointerInput(config.id) { 
                detectDragGestures { change, dragAmount -> 
                    change.consume()
                    val newW = (currentConfig.wRatio + dragAmount.x / parentW.toPx()).coerceIn(60.dp / parentW, 0.95f)
                    onUpdate(currentConfig.copy(wRatio = newW)) 
                } 
            }, contentAlignment = Alignment.Center) { Icon(Icons.AutoMirrored.Filled.ArrowForward, null, tint = Color.Black, modifier = Modifier.size(16.dp)) }
            Box(modifier = Modifier.align(Alignment.BottomCenter).size(26.dp).offset(y = 6.dp).background(Color.White, CircleShape).pointerInput(config.id) { 
                detectDragGestures { change, dragAmount -> 
                    change.consume()
                    val newH = (currentConfig.hRatio + dragAmount.y / parentH.toPx()).coerceIn(60.dp / parentH, 0.85f)
                    onUpdate(currentConfig.copy(hRatio = newH))
                } 
            }, contentAlignment = Alignment.Center) { Icon(Icons.Default.ArrowDownward, null, tint = Color.Black, modifier = Modifier.size(16.dp)) }
        }
    }
}
