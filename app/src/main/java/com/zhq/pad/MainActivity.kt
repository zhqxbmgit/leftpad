package com.zhq.pad

import android.app.Activity
import android.content.BroadcastReceiver
import android.content.Context
import android.content.Intent
import android.content.IntentFilter
import android.os.BatteryManager
import android.os.Bundle
import android.view.WindowManager
import androidx.activity.ComponentActivity
import androidx.activity.compose.setContent
import androidx.activity.enableEdgeToEdge
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
import androidx.core.view.WindowCompat
import androidx.core.view.WindowInsetsCompat
import androidx.core.view.WindowInsetsControllerCompat
import com.zhq.pad.ui.theme.PadTheme
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.delay
import kotlinx.coroutines.launch
import kotlinx.coroutines.withContext
import java.io.PrintWriter
import java.net.InetSocketAddress
import java.net.Socket
import kotlin.math.roundToInt

// --- 赛博霓虹调色板 (视觉强化版) ---
object NeonTheme {
    val Background = Color(0xFF020205) // 极深蓝黑
    val PanelBg = Color(0xEE0B0B0E)
    val Triangle = Color(0xFF00FF41) // 鲜亮绿
    val Square = Color(0xFFFF00FF)   // 电光粉
    val Circle = Color(0xFFFF2A2A)   // 激光红
    val Cross = Color(0xFF00E5FF)    // 极光青
    val Accent = Color(0xFFB15CFF)
    val Battery = Color(0xFF00FF41)
    val TaskMgr = Color(0xFFFFD600)  // 鲜亮黄
}

data class ButtonConfig(
    val id: String,
    val label: String,
    val color: Color,
    var x: Float,
    var y: Float,
    var width: Float,
    var height: Float,
    var isVisible: Boolean = true
)

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
    DisposableEffect(Unit) { val activity = context as? Activity; activity?.window?.addFlags(WindowManager.LayoutParams.FLAG_KEEP_SCREEN_ON); onDispose { activity?.window?.clearFlags(WindowManager.LayoutParams.FLAG_KEEP_SCREEN_ON) } }
    var batteryLevel by remember { mutableStateOf(100) }
    DisposableEffect(Unit) { val receiver = object : BroadcastReceiver() { override fun onReceive(context: Context?, intent: Intent?) { val level = intent?.getIntExtra(BatteryManager.EXTRA_LEVEL, -1) ?: -1; val scale = intent?.getIntExtra(BatteryManager.EXTRA_SCALE, -1) ?: -1; if (level != -1 && scale != -1) batteryLevel = (level * 100 / scale.toFloat()).toInt() } }; context.registerReceiver(receiver, IntentFilter(Intent.ACTION_BATTERY_CHANGED)); onDispose { context.unregisterReceiver(receiver) } }
    val configPrefs = remember { context.getSharedPreferences("button_layout_v2", Context.MODE_PRIVATE) }
    val pcPrefs = remember { context.getSharedPreferences("pc_configs", Context.MODE_PRIVATE) }
    var selectedPcId by remember { mutableStateOf(pcPrefs.getString("selected_pc", "A") ?: "A") }
    var pcAName by remember { mutableStateOf(pcPrefs.getString("pc_a_name", "电脑 A") ?: "电脑 A") }
    var pcAIp by remember { mutableStateOf(pcPrefs.getString("pc_a_ip", "") ?: "") }
    var pcAPort by remember { mutableStateOf(pcPrefs.getString("pc_a_port", "8888") ?: "8888") }
    var pcBName by remember { mutableStateOf(pcPrefs.getString("pc_b_name", "电脑 B") ?: "电脑 B") }
    var pcBIp by remember { mutableStateOf(pcPrefs.getString("pc_b_ip", "") ?: "") }
    var pcBPort by remember { mutableStateOf(pcPrefs.getString("pc_b_port", "8888") ?: "8888") }
    var currentName by remember(selectedPcId) { mutableStateOf(if (selectedPcId == "A") pcAName else pcBName) }
    var currentIp by remember(selectedPcId) { mutableStateOf(if (selectedPcId == "A") pcAIp else pcBIp) }
    var currentPort by remember(selectedPcId) { mutableStateOf(if (selectedPcId == "A") pcAPort else pcBPort) }
    var connectionStatus by remember { mutableStateOf("未连接") }
    var isConnected by remember { mutableStateOf(false) }
    var socket: Socket? by remember { mutableStateOf(null) }
    var writer: PrintWriter? by remember { mutableStateOf(null) }
    var isUserDisconnected by remember { mutableStateOf(false) }
    var isConnecting by remember { mutableStateOf(false) }
    var showPanel by remember { mutableStateOf(false) }
    var isEditMode by remember { mutableStateOf(false) }
    var showAddMenu by remember { mutableStateOf(false) }
    val buttonConfigs = remember { mutableStateListOf<ButtonConfig>() }

    fun getDefaultConfigs() = listOf(
        ButtonConfig("triangle", "△", NeonTheme.Triangle, 120f, -80f, 100f, 100f),
        ButtonConfig("square", "▢", NeonTheme.Square, 0f, 20f, 100f, 100f),
        ButtonConfig("circle", "○", NeonTheme.Circle, 240f, 20f, 100f, 100f),
        ButtonConfig("cross", "✖", NeonTheme.Cross, 120f, 120f, 100f, 100f),
        ButtonConfig("task_manager", "TM", NeonTheme.TaskMgr, -250f, -120f, 80f, 60f)
    )

    fun loadLayout() {
        buttonConfigs.clear()
        val defaultList = getDefaultConfigs()
        var hasSavedV2 = false
        defaultList.forEach { def -> if (configPrefs.contains("${def.id}_isVisible")) { hasSavedV2 = true; buttonConfigs.add(ButtonConfig(id = def.id, label = def.label, color = def.color, x = configPrefs.getFloat("${def.id}_x", def.x), y = configPrefs.getFloat("${def.id}_y", def.y), width = configPrefs.getFloat("${def.id}_width", def.width), height = configPrefs.getFloat("${def.id}_height", def.height), isVisible = configPrefs.getBoolean("${def.id}_isVisible", def.isVisible))) } }
        if (!hasSavedV2) { val v1Prefs = context.getSharedPreferences("button_layout", Context.MODE_PRIVATE); defaultList.forEach { def -> if (v1Prefs.contains("${def.id}_isVisible")) { val oldSize = v1Prefs.getFloat("${def.id}_size", def.width); buttonConfigs.add(ButtonConfig(id = def.id, label = def.label, color = def.color, x = v1Prefs.getFloat("${def.id}_x", def.x), y = v1Prefs.getFloat("${def.id}_y", def.y), width = oldSize, height = oldSize, isVisible = v1Prefs.getBoolean("${def.id}_isVisible", def.isVisible))) } else { buttonConfigs.add(def) } } } else { defaultList.forEach { def -> if (buttonConfigs.none { it.id == def.id }) buttonConfigs.add(def) } }
        val tmIdx = buttonConfigs.indexOfFirst { it.id == "task_manager" }; if (tmIdx != -1) buttonConfigs[tmIdx] = buttonConfigs[tmIdx].copy(color = NeonTheme.TaskMgr)
    }

    fun saveLayout() { configPrefs.edit().apply { buttonConfigs.forEach { config -> putFloat("${config.id}_x", config.x); putFloat("${config.id}_y", config.y); putFloat("${config.id}_width", config.width); putFloat("${config.id}_height", config.height); putBoolean("${config.id}_isVisible", config.isVisible) }; apply() } }
    LaunchedEffect(Unit) { loadLayout() }
    fun disconnect(isManual: Boolean = true) { scope.launch(Dispatchers.IO) { try { writer?.close(); socket?.close() } catch (e: Exception) {} finally { withContext(Dispatchers.Main) { isConnected = false; socket = null; writer = null; if (isManual) { isUserDisconnected = true; connectionStatus = "已断开" } else { connectionStatus = "正在重连" } } } } }
    fun connect(ip: String, port: String, isAuto: Boolean = false) { if (ip.isEmpty() || isConnecting || isConnected) return; isConnecting = true; if (!isAuto) { connectionStatus = "连接中..."; isUserDisconnected = false }; scope.launch(Dispatchers.IO) { try { val newSocket = Socket(); newSocket.connect(InetSocketAddress(ip, port.toInt()), 2000); val newWriter = PrintWriter(newSocket.getOutputStream(), true); withContext(Dispatchers.Main) { socket = newSocket; writer = newWriter; isConnected = true; connectionStatus = "已连接"; isConnecting = false; pcPrefs.edit().apply { putString("selected_pc", selectedPcId); if (selectedPcId == "A") { putString("pc_a_name", currentName); putString("pc_a_ip", currentIp); putString("pc_a_port", currentPort) } else { putString("pc_b_name", currentName); putString("pc_b_ip", currentIp); putString("pc_b_port", currentPort) }; apply() } }; launch(Dispatchers.IO) { try { val inputStream = newSocket.getInputStream(); while (isConnected) { if (inputStream.read() == -1) break } } catch (e: Exception) {} finally { disconnect(isManual = false) } } } catch (e: Exception) { withContext(Dispatchers.Main) { isConnecting = false; if (!isAuto) connectionStatus = "连接失败" else if (!isUserDisconnected) connectionStatus = "正在重连" } } } }
    LaunchedEffect(isConnected, isUserDisconnected, currentIp, currentPort) { if (!isConnected && !isUserDisconnected && currentIp.isNotEmpty()) { while (!isConnected && !isUserDisconnected) { connect(currentIp, currentPort, isAuto = true); delay(3000) } } }
    fun sendMessage(button: String, action: String) { if (isEditMode || showPanel) return; if (isConnected && writer != null) { scope.launch(Dispatchers.IO) { try { writer?.println("{\"button\":\"$button\",\"action\":\"$action\"}"); writer?.flush(); if (writer?.checkError() == true) disconnect(isManual = false) } catch (e: Exception) { disconnect(isManual = false) } } } }
    fun sendSystemCommand(command: String) { if (isEditMode || showPanel) return; if (isConnected && writer != null) { scope.launch(Dispatchers.IO) { try { writer?.println("{\"command\":\"$command\"}"); writer?.flush(); if (writer?.checkError() == true) disconnect(isManual = false) } catch (e: Exception) { disconnect(isManual = false) } } } }

    Box(modifier = modifier.fillMaxSize().background(NeonTheme.Background)) {
        BoxWithConstraints(modifier = Modifier.fillMaxSize(), contentAlignment = Alignment.Center) {
            val maxAllowedWidth = maxWidth * 0.9f; val maxAllowedHeight = maxHeight * 0.8f
            buttonConfigs.forEachIndexed { index, config -> if (config.isVisible) { 
                EnhancedNeonButton(config = config, isEditMode = isEditMode, maxWidthLimit = maxAllowedWidth, maxHeightLimit = maxAllowedHeight, onUpdate = { updated -> buttonConfigs[index] = updated }, onPress = { if (config.id == "task_manager") sendSystemCommand("task_manager") else sendMessage(config.id, "down") }, onRelease = { if (config.id != "task_manager") sendMessage(config.id, "up") }, onDelete = { buttonConfigs[index] = config.copy(isVisible = false) }) 
            } }
        }
        Row(modifier = Modifier.align(Alignment.TopStart).padding(20.dp), verticalAlignment = Alignment.CenterVertically) { IconButton(onClick = { showPanel = true }, modifier = Modifier.size(32.dp).border(1.dp, NeonTheme.Accent.copy(alpha = 0.5f), CircleShape)) { Icon(Icons.Default.Settings, null, tint = NeonTheme.Accent, modifier = Modifier.size(18.dp)) }; Spacer(modifier = Modifier.width(16.dp)); Text("DS4 模式", color = Color.Gray, fontSize = 11.sp, fontWeight = FontWeight.Light); Spacer(modifier = Modifier.width(12.dp)); Box(modifier = Modifier.size(6.dp).clip(CircleShape).background(if (isConnected) NeonTheme.Triangle else NeonTheme.Circle).blur(if (isConnected) 4.dp else 0.dp)) }
        Column(modifier = Modifier.align(Alignment.BottomStart).padding(24.dp)) { Text("电量", color = Color.Gray, fontSize = 9.sp, fontWeight = FontWeight.Bold); Spacer(modifier = Modifier.height(6.dp)); Row(verticalAlignment = Alignment.CenterVertically) { Canvas(modifier = Modifier.size(width = 32.dp, height = 14.dp)) { drawRoundRect(color = NeonTheme.Battery.copy(alpha = 0.5f), size = Size(28.dp.toPx(), 14.dp.toPx()), cornerRadius = CornerRadius(2.dp.toPx()), style = Stroke(width = 1.dp.toPx())); drawRect(color = NeonTheme.Battery.copy(alpha = 0.5f), topLeft = Offset(28.dp.toPx(), 4.dp.toPx()), size = Size(2.dp.toPx(), 6.dp.toPx())); drawRect(color = NeonTheme.Battery, topLeft = Offset(2.dp.toPx(), 2.dp.toPx()), size = Size((24 * (batteryLevel / 100f)).dp.toPx(), 10.dp.toPx())) }; Spacer(modifier = Modifier.width(10.dp)); Text("$batteryLevel%", color = NeonTheme.Battery, fontSize = 13.sp, fontWeight = FontWeight.Medium) }; Spacer(modifier = Modifier.height(16.dp)); Text("CYBER INPUT SYSTEM", color = NeonTheme.Accent.copy(alpha = 0.6f), fontSize = 9.sp, letterSpacing = 1.sp) }
        AnimatedVisibility(visible = showPanel, enter = slideInHorizontally(initialOffsetX = { -it }), exit = slideOutHorizontally(targetOffsetX = { -it })) { Box(modifier = Modifier.fillMaxSize().background(Color(0x99000000)).clickable { showPanel = false }) { Surface(modifier = Modifier.fillMaxHeight().width(260.dp).clickable(enabled = false) { }, color = NeonTheme.PanelBg, border = BorderStroke(1.dp, NeonTheme.Accent.copy(alpha = 0.3f)), shape = RoundedCornerShape(topEnd = 24.dp, bottomEnd = 24.dp)) { Column(modifier = Modifier.fillMaxSize().padding(24.dp).verticalScroll(rememberScrollState())) { Row(modifier = Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.SpaceBetween, verticalAlignment = Alignment.CenterVertically) { Text("控制台", color = NeonTheme.Accent, fontWeight = FontWeight.Bold, letterSpacing = 2.sp); IconButton(onClick = { showPanel = false }) { Icon(Icons.Default.Close, null, tint = Color.Gray) } }; HorizontalDivider(color = NeonTheme.Accent.copy(alpha = 0.2f)); Spacer(modifier = Modifier.height(20.dp)); Text(currentName, color = Color.White, fontSize = 18.sp, fontWeight = FontWeight.Bold); Text("状态: $connectionStatus", fontSize = 11.sp, color = if (isConnected) NeonTheme.Triangle else Color.Gray); Spacer(modifier = Modifier.height(24.dp)); Button(onClick = { if (!isConnected) connect(currentIp, currentPort) else disconnect(isManual = true) }, modifier = Modifier.fillMaxWidth().height(44.dp), colors = ButtonDefaults.buttonColors(containerColor = Color.Transparent), border = BorderStroke(1.dp, if (isConnected) NeonTheme.Circle else NeonTheme.Triangle), shape = RoundedCornerShape(8.dp)) { Text(if (isConnected) "断开连接" else "建立连接", color = if (isConnected) NeonTheme.Circle else NeonTheme.Triangle) }; Spacer(modifier = Modifier.height(12.dp)); Button(onClick = { if (isEditMode) { saveLayout() }; isEditMode = !isEditMode }, modifier = Modifier.fillMaxWidth().height(44.dp), colors = ButtonDefaults.buttonColors(containerColor = Color.Transparent), border = BorderStroke(1.dp, if (isEditMode) NeonTheme.Triangle else NeonTheme.Accent), shape = RoundedCornerShape(8.dp)) { Text(if (isEditMode) "保存布局" else "编辑布局", color = if (isEditMode) NeonTheme.Triangle else NeonTheme.Accent) }; if (isEditMode) { Spacer(modifier = Modifier.height(16.dp)); Row(modifier = Modifier.fillMaxWidth()) { Box(modifier = Modifier.weight(1f)) { OutlinedButton(onClick = { showAddMenu = true }, modifier = Modifier.fillMaxWidth().padding(end = 4.dp), border = BorderStroke(1.dp, NeonTheme.Accent)) { Text("添加", fontSize = 11.sp, color = NeonTheme.Accent) }; DropdownMenu(expanded = showAddMenu, onDismissRequest = { showAddMenu = false }, modifier = Modifier.background(NeonTheme.PanelBg)) { buttonConfigs.filter { !it.isVisible }.forEach { config -> DropdownMenuItem(text = { Text(config.label, color = Color.White) }, onClick = { val index = buttonConfigs.indexOfFirst { it.id == config.id }; buttonConfigs[index] = config.copy(isVisible = true, x = 0f, y = 0f); showAddMenu = false }) } } }; OutlinedButton(onClick = { buttonConfigs.clear(); buttonConfigs.addAll(getDefaultConfigs()); saveLayout() }, modifier = Modifier.weight(1f).padding(start = 4.dp), border = BorderStroke(1.dp, Color.Gray)) { Text("重置", fontSize = 11.sp, color = Color.Gray) } } }; Spacer(modifier = Modifier.height(32.dp)); Text("目标配置", style = MaterialTheme.typography.labelSmall, color = Color.Gray); Spacer(modifier = Modifier.height(12.dp)); Row(modifier = Modifier.fillMaxWidth()) { Button(onClick = { if (selectedPcId != "A") { disconnect(); selectedPcId = "A" } }, modifier = Modifier.weight(1f).height(32.dp).padding(end = 4.dp), colors = ButtonDefaults.buttonColors(containerColor = if (selectedPcId == "A") NeonTheme.Accent.copy(alpha = 0.2f) else Color.Transparent), border = BorderStroke(1.dp, if (selectedPcId == "A") NeonTheme.Accent else Color.DarkGray)) { Text("电脑 A", color = if (selectedPcId == "A") NeonTheme.Accent else Color.Gray) }; Button(onClick = { if (selectedPcId != "B") { disconnect(); selectedPcId = "B" } }, modifier = Modifier.weight(1f).height(32.dp).padding(start = 4.dp), colors = ButtonDefaults.buttonColors(containerColor = if (selectedPcId == "B") NeonTheme.Accent.copy(alpha = 0.2f) else Color.Transparent), border = BorderStroke(1.dp, if (selectedPcId == "B") NeonTheme.Accent else Color.DarkGray)) { Text("电脑 B", color = if (selectedPcId == "B") NeonTheme.Accent else Color.Gray) } }; Spacer(modifier = Modifier.height(12.dp)); OutlinedTextField(value = currentName, onValueChange = { currentName = it }, label = { Text("名称") }, modifier = Modifier.fillMaxWidth(), enabled = !isConnected, singleLine = true, colors = OutlinedTextFieldDefaults.colors(unfocusedTextColor = Color.White, focusedTextColor = Color.White, unfocusedBorderColor = Color.DarkGray, focusedBorderColor = NeonTheme.Accent, unfocusedLabelColor = Color.Gray, focusedLabelColor = NeonTheme.Accent), shape = RoundedCornerShape(8.dp)); OutlinedTextField(value = currentIp, onValueChange = { currentIp = it }, label = { Text("IP 地址") }, modifier = Modifier.fillMaxWidth(), enabled = !isConnected, singleLine = true, colors = OutlinedTextFieldDefaults.colors(unfocusedTextColor = Color.White, focusedTextColor = Color.White, unfocusedBorderColor = Color.DarkGray, focusedBorderColor = NeonTheme.Accent, unfocusedLabelColor = Color.Gray, focusedLabelColor = NeonTheme.Accent), shape = RoundedCornerShape(8.dp)); OutlinedTextField(value = currentPort, onValueChange = { currentPort = it }, label = { Text("端口") }, modifier = Modifier.fillMaxWidth(), enabled = !isConnected, singleLine = true, colors = OutlinedTextFieldDefaults.colors(unfocusedTextColor = Color.White, focusedTextColor = Color.White, unfocusedBorderColor = Color.DarkGray, focusedBorderColor = NeonTheme.Accent, unfocusedLabelColor = Color.Gray, focusedLabelColor = NeonTheme.Accent), shape = RoundedCornerShape(8.dp)) } } } }
    }
}

@Composable
fun EnhancedNeonButton(
    config: ButtonConfig, isEditMode: Boolean,
    maxWidthLimit: Dp, maxHeightLimit: Dp,
    onUpdate: (ButtonConfig) -> Unit,
    onPress: () -> Unit, onRelease: () -> Unit, onDelete: () -> Unit
) {
    var offsetX by remember(config.id) { mutableStateOf(config.x.dp) }
    var offsetY by remember(config.id) { mutableStateOf(config.y.dp) }
    var currentWidth by remember(config.id) { mutableStateOf(config.width.dp) }
    var currentHeight by remember(config.id) { mutableStateOf(config.height.dp) }
    var isPressed by remember { mutableStateOf(false) }

    // 状态动画：发光与描边增强
    val glowAlpha by animateFloatAsState(if (isPressed) 1.0f else 0.55f, label = "glow")
    val strokeWidth by animateFloatAsState(if (isPressed) 5.5f else 2.5f, label = "stroke")
    val symbolAlpha by animateFloatAsState(if (isPressed) 1.0f else 0.75f, label = "symbol")
    val scale by animateFloatAsState(if (isPressed) 1.06f else 1f, label = "scale")

    Box(
        modifier = Modifier
            .offset { IntOffset(offsetX.toPx().roundToInt(), offsetY.toPx().roundToInt()) }
            .size(width = currentWidth, height = currentHeight)
            .scale(scale)
    ) {
        Canvas(
            modifier = Modifier
                .fillMaxSize()
                .pointerInput(isEditMode, config.id) {
                    if (isEditMode) {
                        detectDragGestures { change, dragAmount ->
                            change.consume()
                            offsetX += dragAmount.x.toDp(); offsetY += dragAmount.y.toDp()
                            onUpdate(config.copy(x = offsetX.value, y = offsetY.value))
                        }
                    } else {
                        detectTapGestures(onPress = { isPressed = true; onPress(); tryAwaitRelease(); isPressed = false; onRelease() })
                    }
                }
        ) {
            val corner = 14.dp.toPx()
            
            // 1. 最外层扩散光晕 (柔和大气感)
            drawRoundRect(
                color = config.color.copy(alpha = glowAlpha * 0.12f),
                size = Size(size.width + 24.dp.toPx(), size.height + 24.dp.toPx()),
                topLeft = Offset(-12.dp.toPx(), -12.dp.toPx()),
                cornerRadius = CornerRadius(corner + 12.dp.toPx()),
                style = Stroke(width = 16.dp.toPx())
            )

            // 2. 中层核心光晕 (霓虹质感)
            drawRoundRect(
                color = config.color.copy(alpha = glowAlpha * 0.25f),
                size = Size(size.width + 10.dp.toPx(), size.height + 10.dp.toPx()),
                topLeft = Offset(-5.dp.toPx(), -5.dp.toPx()),
                cornerRadius = CornerRadius(corner + 5.dp.toPx()),
                style = Stroke(width = 8.dp.toPx())
            )

            // 3. 核心高亮描边 (绝对可见性)
            drawRoundRect(
                color = config.color.copy(alpha = glowAlpha),
                size = size,
                cornerRadius = CornerRadius(corner),
                style = Stroke(width = strokeWidth.dp.toPx())
            )

            // 4. 符号绘制 (增强亮度与轻微光晕)
            val center = Offset(size.width / 2, size.height / 2)
            val iconSize = Math.min(size.width, size.height) * 0.42f
            val symColor = config.color.copy(alpha = symbolAlpha)
            val symStroke = 3.dp.toPx()

            when (config.id) {
                "triangle" -> {
                    val path = Path().apply {
                        moveTo(center.x, center.y - iconSize * 0.55f)
                        lineTo(center.x - iconSize * 0.5f, center.y + iconSize * 0.35f)
                        lineTo(center.x + iconSize * 0.5f, center.y + iconSize * 0.35f)
                        close()
                    }
                    drawPath(path, symColor, style = Stroke(width = symStroke, cap = StrokeCap.Round))
                }
                "square" -> {
                    drawRect(symColor, topLeft = Offset(center.x - iconSize * 0.42f, center.y - iconSize * 0.42f), size = Size(iconSize * 0.84f, iconSize * 0.84f), style = Stroke(width = symStroke, cap = StrokeCap.Round))
                }
                "circle" -> {
                    drawCircle(symColor, radius = iconSize * 0.48f, center = center, style = Stroke(width = symStroke))
                }
                "cross" -> {
                    val r = iconSize * 0.42f
                    drawLine(symColor, Offset(center.x - r, center.y - r), Offset(center.x + r, center.y + r), strokeWidth = symStroke + 1.dp.toPx(), cap = StrokeCap.Round)
                    drawLine(symColor, Offset(center.x + r, center.y - r), Offset(center.x - r, center.y + r), strokeWidth = symStroke + 1.dp.toPx(), cap = StrokeCap.Round)
                }
                "task_manager" -> {
                    // TM 按钮背景也增强一点
                    drawRoundRect(color = config.color.copy(alpha = glowAlpha * 0.15f), size = size, cornerRadius = CornerRadius(corner))
                }
            }
        }

        if (config.id == "task_manager") {
            Text(text = "TM", color = config.color.copy(alpha = glowAlpha), fontSize = 15.sp, fontWeight = FontWeight.ExtraBold, modifier = Modifier.align(Alignment.Center))
        }

        if (isEditMode) {
            IconButton(onClick = onDelete, modifier = Modifier.align(Alignment.TopStart).size(26.dp).offset(x = (-4).dp, y = (-4).dp).background(Color.Red, CircleShape)) { Icon(Icons.Default.Close, null, tint = Color.White, modifier = Modifier.size(16.dp)) }
            Box(modifier = Modifier.align(Alignment.CenterEnd).size(26.dp).offset(x = 6.dp).background(Color.White, CircleShape).pointerInput(config.id) { detectDragGestures { change, dragAmount -> change.consume(); currentWidth = (currentWidth + dragAmount.x.toDp()).coerceIn(60.dp, maxWidthLimit); onUpdate(config.copy(width = currentWidth.value)) } }, contentAlignment = Alignment.Center) { Icon(Icons.AutoMirrored.Filled.ArrowForward, null, tint = Color.Black, modifier = Modifier.size(16.dp)) }
            Box(modifier = Modifier.align(Alignment.BottomCenter).size(26.dp).offset(y = 6.dp).background(Color.White, CircleShape).pointerInput(config.id) { detectDragGestures { change, dragAmount -> change.consume(); currentHeight = (currentHeight + dragAmount.y.toDp()).coerceIn(60.dp, maxHeightLimit); onUpdate(config.copy(height = currentHeight.value)) } }, contentAlignment = Alignment.Center) { Icon(Icons.Default.ArrowDownward, null, tint = Color.Black, modifier = Modifier.size(16.dp)) }
        }
    }
}
