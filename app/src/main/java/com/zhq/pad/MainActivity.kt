package com.zhq.pad

import android.app.Activity
import android.content.Context
import android.os.Bundle
import android.view.WindowManager
import androidx.activity.ComponentActivity
import androidx.activity.compose.setContent
import androidx.activity.enableEdgeToEdge
import androidx.compose.animation.AnimatedVisibility
import androidx.compose.animation.slideInHorizontally
import androidx.compose.animation.slideOutHorizontally
import androidx.compose.foundation.background
import androidx.compose.foundation.clickable
import androidx.compose.foundation.gestures.detectDragGestures
import androidx.compose.foundation.gestures.detectTapGestures
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.PaddingValues
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxHeight
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.offset
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.verticalScroll
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.Add
import androidx.compose.material.icons.filled.Close
import androidx.compose.material.icons.filled.Refresh
import androidx.compose.material.icons.filled.Settings
import androidx.compose.material3.Button
import androidx.compose.material3.ButtonDefaults
import androidx.compose.material3.DropdownMenu
import androidx.compose.material3.DropdownMenuItem
import androidx.compose.material3.HorizontalDivider
import androidx.compose.material3.Icon
import androidx.compose.material3.IconButton
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.Scaffold
import androidx.compose.material3.Surface
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.DisposableEffect
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateListOf
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.rememberCoroutineScope
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.input.pointer.pointerInput
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.res.painterResource
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.text.style.TextOverflow
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

// 按钮布局数据结构
data class ButtonConfig(
    val id: String,
    val label: String,
    val color: Color,
    var x: Float,
    var y: Float,
    var size: Float,
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
        setContent {
            PadTheme {
                Scaffold(modifier = Modifier.fillMaxSize()) { innerPadding ->
                    ControllerScreen(modifier = Modifier.padding(innerPadding))
                }
            }
        }
    }
}

@Composable
fun ControllerScreen(modifier: Modifier = Modifier) {
    val context = LocalContext.current
    
    DisposableEffect(Unit) {
        val activity = context as? Activity
        activity?.window?.addFlags(WindowManager.LayoutParams.FLAG_KEEP_SCREEN_ON)
        onDispose {
            activity?.window?.clearFlags(WindowManager.LayoutParams.FLAG_KEEP_SCREEN_ON)
        }
    }

    val configPrefs = remember { context.getSharedPreferences("button_layout", Context.MODE_PRIVATE) }
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
    val scope = rememberCoroutineScope()

    fun getDefaultConfigs() = listOf(
        ButtonConfig("triangle", "△", Color(0xFF4CAF50), 100f, -60f, 90f),
        ButtonConfig("square", "▢", Color(0xFFE91E63), 0f, 40f, 90f),
        ButtonConfig("circle", "○", Color(0xFFF44336), 200f, 40f, 90f),
        ButtonConfig("cross", "✖", Color(0xFF2196F3), 100f, 140f, 90f),
        // 第十二阶段：新增“任务管理器”按钮，默认位置偏上
        ButtonConfig("task_manager", "TM", Color(0xFF607D8B), -180f, -100f, 60f, isVisible = true)
    )

    fun loadLayout() {
        buttonConfigs.clear()
        val defaultList = getDefaultConfigs()
        var hasSaved = false
        
        defaultList.forEach { defaultConfig ->
            val id = defaultConfig.id
            if (configPrefs.contains("${id}_isVisible")) {
                hasSaved = true
                buttonConfigs.add(ButtonConfig(
                    id = id,
                    label = defaultConfig.label,
                    color = defaultConfig.color,
                    x = configPrefs.getFloat("${id}_x", defaultConfig.x),
                    y = configPrefs.getFloat("${id}_y", defaultConfig.y),
                    size = configPrefs.getFloat("${id}_size", defaultConfig.size),
                    isVisible = configPrefs.getBoolean("${id}_isVisible", defaultConfig.isVisible)
                ))
            }
        }
        if (!hasSaved) buttonConfigs.addAll(defaultList)
        else {
            // 补全由于升级新增的按钮（如 TM）
            defaultList.forEach { def ->
                if (buttonConfigs.none { it.id == def.id }) {
                    buttonConfigs.add(def)
                }
            }
        }
    }

    fun saveLayout() {
        configPrefs.edit().apply {
            buttonConfigs.forEach { config ->
                putFloat("${config.id}_x", config.x); putFloat("${config.id}_y", config.y)
                putFloat("${config.id}_size", config.size); putBoolean("${config.id}_isVisible", config.isVisible)
            }
            apply()
        }
    }

    LaunchedEffect(Unit) { loadLayout() }

    fun disconnect(isManual: Boolean = true) {
        scope.launch(Dispatchers.IO) {
            try { writer?.close(); socket?.close() } catch (e: Exception) {} finally {
                withContext(Dispatchers.Main) {
                    isConnected = false; socket = null; writer = null
                    if (isManual) { isUserDisconnected = true; connectionStatus = "已断开" }
                    else { connectionStatus = "重连中..." }
                }
            }
        }
    }

    fun connect(ip: String, port: String, isAuto: Boolean = false) {
        if (ip.isEmpty() || isConnecting || isConnected) return
        isConnecting = true
        if (!isAuto) { connectionStatus = "连接中..."; isUserDisconnected = false }
        scope.launch(Dispatchers.IO) {
            try {
                val newSocket = Socket()
                newSocket.connect(InetSocketAddress(ip, port.toInt()), 2000)
                val newWriter = PrintWriter(newSocket.getOutputStream(), true)
                withContext(Dispatchers.Main) {
                    socket = newSocket; writer = newWriter; isConnected = true; connectionStatus = "✅ 已连接"; isConnecting = false
                    pcPrefs.edit().apply {
                        putString("selected_pc", selectedPcId)
                        if (selectedPcId == "A") { putString("pc_a_name", currentName); putString("pc_a_ip", currentIp); putString("pc_a_port", currentPort) }
                        else { putString("pc_b_name", currentName); putString("pc_b_ip", currentIp); putString("pc_b_port", currentPort) }
                        apply()
                    }
                }
                launch(Dispatchers.IO) {
                    try {
                        val inputStream = newSocket.getInputStream()
                        while (isConnected) { if (inputStream.read() == -1) break }
                    } catch (e: Exception) {} finally { disconnect(isManual = false) }
                }
            } catch (e: Exception) {
                withContext(Dispatchers.Main) {
                    isConnecting = false
                    if (!isAuto) connectionStatus = "❌ 失败: ${e.localizedMessage}"
                    else if (!isUserDisconnected) connectionStatus = "重连中..."
                }
            }
        }
    }

    LaunchedEffect(isConnected, isUserDisconnected, currentIp, currentPort) {
        if (!isConnected && !isUserDisconnected && currentIp.isNotEmpty()) {
            while (!isConnected && !isUserDisconnected) { connect(currentIp, currentPort, isAuto = true); delay(3000) }
        }
    }

    // 发送手柄按键 JSON
    fun sendMessage(button: String, action: String) {
        if (isEditMode || showPanel) return
        if (isConnected && writer != null) {
            scope.launch(Dispatchers.IO) {
                try {
                    writer?.println("{\"button\":\"$button\",\"action\":\"$action\"}")
                    writer?.flush()
                    if (writer?.checkError() == true) disconnect(isManual = false)
                } catch (e: Exception) { disconnect(isManual = false) }
            }
        }
    }

    // 第十二阶段：发送系统命令 JSON
    fun sendSystemCommand(command: String) {
        if (isEditMode || showPanel) return
        if (isConnected && writer != null) {
            scope.launch(Dispatchers.IO) {
                try {
                    writer?.println("{\"command\":\"$command\"}")
                    writer?.flush()
                    if (writer?.checkError() == true) disconnect(isManual = false)
                } catch (e: Exception) { disconnect(isManual = false) }
            }
        } else {
            connectionStatus = "未连接"
        }
    }

    Box(modifier = modifier.fillMaxSize().background(Color(0xFF121212))) {
        Box(modifier = Modifier.fillMaxSize(), contentAlignment = Alignment.Center) {
            buttonConfigs.forEachIndexed { index, config ->
                if (config.isVisible) {
                    DraggablePadButton(
                        config = config, isEditMode = isEditMode,
                        onUpdate = { updated -> buttonConfigs[index] = updated },
                        onPress = { 
                            if (config.id == "task_manager") sendSystemCommand("task_manager")
                            else sendMessage(config.id, "down") 
                        },
                        onRelease = { 
                            if (config.id != "task_manager") sendMessage(config.id, "up") 
                        },
                        onDelete = { buttonConfigs[index] = config.copy(isVisible = false) }
                    )
                }
            }
        }

        Row(
            modifier = Modifier.align(Alignment.TopStart).padding(12.dp),
            verticalAlignment = Alignment.CenterVertically
        ) {
            IconButton(
                onClick = { showPanel = true },
                modifier = Modifier.size(36.dp).background(Color(0x88000000), CircleShape)
            ) {
                Icon(Icons.Default.Settings, contentDescription = "Settings", tint = Color.White, modifier = Modifier.size(20.dp))
            }
            Spacer(modifier = Modifier.width(8.dp))
            Box(
                modifier = Modifier.size(10.dp).clip(CircleShape)
                    .background(when {
                        isConnected -> Color(0xFF4CAF50)
                        connectionStatus.contains("重连") -> Color(0xFFFFEB3B)
                        else -> Color(0xFFF44336)
                    })
            )
            if (isEditMode) {
                Spacer(modifier = Modifier.width(8.dp))
                Text("编辑模式", color = Color.White, fontSize = 12.sp, fontWeight = FontWeight.Bold)
            }
        }

        AnimatedVisibility(
            visible = showPanel,
            enter = slideInHorizontally(initialOffsetX = { -it }),
            exit = slideOutHorizontally(targetOffsetX = { -it })
        ) {
            Box(modifier = Modifier.fillMaxSize().background(Color(0x66000000)).clickable { showPanel = false }) {
                Surface(
                    modifier = Modifier.fillMaxHeight().width(240.dp).clickable(enabled = false) { },
                    color = MaterialTheme.colorScheme.surface,
                    tonalElevation = 8.dp,
                    shape = RoundedCornerShape(topEnd = 16.dp, bottomEnd = 16.dp)
                ) {
                    Column(modifier = Modifier.fillMaxSize().padding(16.dp).verticalScroll(rememberScrollState()), horizontalAlignment = Alignment.Start) {
                        Row(modifier = Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.SpaceBetween, verticalAlignment = Alignment.CenterVertically) {
                            Text("控制面板", style = MaterialTheme.typography.titleMedium, fontWeight = FontWeight.Bold)
                            IconButton(onClick = { showPanel = false }) { Icon(Icons.Default.Close, contentDescription = "Close") }
                        }
                        HorizontalDivider()
                        Spacer(modifier = Modifier.height(12.dp))
                        Text(text = currentName, fontSize = 16.sp, fontWeight = FontWeight.Bold, color = MaterialTheme.colorScheme.primary, maxLines = 1, overflow = TextOverflow.Ellipsis)
                        Text(text = "状态: $connectionStatus", fontSize = 12.sp, color = if (isConnected) Color(0xFF4CAF50) else Color.Gray)
                        Spacer(modifier = Modifier.height(16.dp))
                        Button(onClick = { if (!isConnected) connect(currentIp, currentPort) else disconnect(isManual = true) }, modifier = Modifier.fillMaxWidth().height(40.dp)) { Text(if (isConnected) "断开连接" else "开始连接") }
                        Spacer(modifier = Modifier.height(8.dp))
                        Button(onClick = { if (isEditMode) { saveLayout(); connectionStatus = "布局已保存" }; isEditMode = !isEditMode }, modifier = Modifier.fillMaxWidth().height(40.dp), colors = ButtonDefaults.buttonColors(containerColor = if (isEditMode) Color(0xFF4CAF50) else MaterialTheme.colorScheme.secondary)) { Text(if (isEditMode) "完成布局编辑" else "进入布局编辑") }
                        if (isEditMode) {
                            Spacer(modifier = Modifier.height(12.dp))
                            Row(modifier = Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.SpaceBetween) {
                                Box(modifier = Modifier.weight(1f)) {
                                    Button(onClick = { showAddMenu = true }, modifier = Modifier.fillMaxWidth().padding(end = 4.dp)) {
                                        Icon(Icons.Default.Add, null, modifier = Modifier.size(14.dp))
                                        Text("添加", fontSize = 11.sp)
                                    }
                                    DropdownMenu(expanded = showAddMenu, onDismissRequest = { showAddMenu = false }) {
                                        buttonConfigs.filter { !it.isVisible }.forEach { config ->
                                            DropdownMenuItem(text = { Text(config.label) }, onClick = {
                                                val index = buttonConfigs.indexOfFirst { it.id == config.id }
                                                buttonConfigs[index] = config.copy(isVisible = true, x = 0f, y = 0f)
                                                showAddMenu = false
                                            })
                                        }
                                    }
                                }
                                Button(onClick = { buttonConfigs.clear(); buttonConfigs.addAll(getDefaultConfigs()); saveLayout() }, modifier = Modifier.weight(1f).padding(start = 4.dp), colors = ButtonDefaults.buttonColors(containerColor = Color.DarkGray)) {
                                    Icon(Icons.Default.Refresh, null, modifier = Modifier.size(14.dp))
                                    Text("重置", fontSize = 11.sp)
                                }
                            }
                        }
                        Spacer(modifier = Modifier.height(24.dp))
                        Text("电脑配置", style = MaterialTheme.typography.labelLarge, color = Color.Gray)
                        HorizontalDivider()
                        Spacer(modifier = Modifier.height(8.dp))
                        Row(modifier = Modifier.fillMaxWidth()) {
                            Button(onClick = { if (selectedPcId != "A") { disconnect(); selectedPcId = "A" } }, modifier = Modifier.weight(1f).height(32.dp).padding(end = 4.dp), contentPadding = PaddingValues(0.dp), colors = ButtonDefaults.buttonColors(containerColor = if (selectedPcId == "A") MaterialTheme.colorScheme.primary else Color.Gray)) { Text("电脑 A", fontSize = 11.sp) }
                            Button(onClick = { if (selectedPcId != "B") { disconnect(); selectedPcId = "B" } }, modifier = Modifier.weight(1f).height(32.dp).padding(start = 4.dp), contentPadding = PaddingValues(0.dp), colors = ButtonDefaults.buttonColors(containerColor = if (selectedPcId == "B") MaterialTheme.colorScheme.primary else Color.Gray)) { Text("电脑 B", fontSize = 11.sp) }
                        }
                        Spacer(modifier = Modifier.height(8.dp))
                        OutlinedTextField(value = currentName, onValueChange = { currentName = it }, label = { Text("名称") }, modifier = Modifier.fillMaxWidth(), enabled = !isConnected, singleLine = true)
                        Spacer(modifier = Modifier.height(4.dp))
                        OutlinedTextField(value = currentIp, onValueChange = { currentIp = it }, label = { Text("IP 地址") }, modifier = Modifier.fillMaxWidth(), enabled = !isConnected, singleLine = true)
                        Spacer(modifier = Modifier.height(4.dp))
                        OutlinedTextField(value = currentPort, onValueChange = { currentPort = it }, label = { Text("端口") }, modifier = Modifier.fillMaxWidth(), enabled = !isConnected, singleLine = true)
                    }
                }
            }
        }
    }
}

@Composable
fun DraggablePadButton(
    config: ButtonConfig, isEditMode: Boolean, onUpdate: (ButtonConfig) -> Unit,
    onPress: () -> Unit, onRelease: () -> Unit, onDelete: () -> Unit
) {
    var offsetX by remember(config.id) { mutableStateOf(config.x.dp) }
    var offsetY by remember(config.id) { mutableStateOf(config.y.dp) }
    var currentSize by remember(config.id) { mutableStateOf(config.size.dp) }

    Box(
        modifier = Modifier
            .offset { IntOffset(offsetX.toPx().roundToInt(), offsetY.toPx().roundToInt()) }
            .size(currentSize)
    ) {
        Surface(
            modifier = Modifier
                .fillMaxSize()
                .pointerInput(isEditMode, config.id) {
                    if (isEditMode) {
                        detectDragGestures { change, dragAmount ->
                            change.consume()
                            offsetX += dragAmount.x.toDp()
                            offsetY += dragAmount.y.toDp()
                            onUpdate(config.copy(x = offsetX.value, y = offsetY.value, size = currentSize.value))
                        }
                    } else {
                        detectTapGestures(onPress = { onPress(); tryAwaitRelease(); onRelease() })
                    }
                },
            shape = CircleShape, color = config.color, shadowElevation = if (isEditMode) 0.dp else 8.dp
        ) {
            Box(contentAlignment = Alignment.Center) {
                Text(
                    text = config.label, 
                    fontSize = (currentSize.value * if (config.id == "task_manager") 0.3 else 0.4).sp, 
                    fontWeight = FontWeight.Bold, 
                    color = Color.White,
                    textAlign = TextAlign.Center
                )
            }
        }

        if (isEditMode) {
            IconButton(
                onClick = onDelete,
                modifier = Modifier.align(Alignment.TopStart).size(24.dp).offset(x = (-4).dp, y = (-4).dp).background(Color.Red, CircleShape)
            ) { Icon(Icons.Default.Close, contentDescription = "Delete", tint = Color.White, modifier = Modifier.size(14.dp)) }

            Box(
                modifier = Modifier
                    .align(Alignment.BottomEnd).size(24.dp).offset(x = 4.dp, y = 4.dp).background(Color.White, CircleShape)
                    .pointerInput(config.id) {
                        detectDragGestures { change, dragAmount ->
                            change.consume()
                            val deltaDp = (dragAmount.x.toDp() + dragAmount.y.toDp()) / 2f
                            val newSize = (currentSize + deltaDp).coerceIn(60.dp, 320.dp)
                            currentSize = newSize
                            onUpdate(config.copy(size = currentSize.value))
                        }
                    },
                contentAlignment = Alignment.Center
            ) {
                Icon(painter = painterResource(android.R.drawable.ic_menu_directions), contentDescription = "Resize", tint = Color.Black, modifier = Modifier.size(16.dp))
            }
        }
    }
}
