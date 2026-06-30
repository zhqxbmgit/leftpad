package com.zhq.pad

import android.content.Context
import android.os.Bundle
import android.util.Log
import androidx.activity.ComponentActivity
import androidx.activity.compose.setContent
import androidx.activity.enableEdgeToEdge
import androidx.compose.animation.AnimatedVisibility
import androidx.compose.foundation.gestures.detectTapGestures
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxHeight
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.verticalScroll
import androidx.compose.material3.Button
import androidx.compose.material3.ButtonDefaults
import androidx.compose.material3.HorizontalDivider
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.Scaffold
import androidx.compose.material3.Surface
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.rememberCoroutineScope
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.input.pointer.pointerInput
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.text.font.FontWeight
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

class MainActivity : ComponentActivity() {
    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        
        // 开启全屏沉浸模式
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
    val sharedPrefs = remember { context.getSharedPreferences("pc_configs", Context.MODE_PRIVATE) }
    
    var selectedPcId by remember { mutableStateOf(sharedPrefs.getString("selected_pc", "A") ?: "A") }
    var pcAName by remember { mutableStateOf(sharedPrefs.getString("pc_a_name", "电脑 A") ?: "电脑 A") }
    var pcAIp by remember { mutableStateOf(sharedPrefs.getString("pc_a_ip", "") ?: "") }
    var pcAPort by remember { mutableStateOf(sharedPrefs.getString("pc_a_port", "8888") ?: "8888") }
    var pcBName by remember { mutableStateOf(sharedPrefs.getString("pc_b_name", "电脑 B") ?: "电脑 B") }
    var pcBIp by remember { mutableStateOf(sharedPrefs.getString("pc_b_ip", "") ?: "") }
    var pcBPort by remember { mutableStateOf(sharedPrefs.getString("pc_b_port", "8888") ?: "8888") }

    var currentName by remember(selectedPcId) { mutableStateOf(if (selectedPcId == "A") pcAName else pcBName) }
    var currentIp by remember(selectedPcId) { mutableStateOf(if (selectedPcId == "A") pcAIp else pcBIp) }
    var currentPort by remember(selectedPcId) { mutableStateOf(if (selectedPcId == "A") pcAPort else pcBPort) }

    var connectionStatus by remember { mutableStateOf("未连接") }
    var isConnected by remember { mutableStateOf(false) }
    var socket: Socket? by remember { mutableStateOf(null) }
    var writer: PrintWriter? by remember { mutableStateOf(null) }
    
    // 断线重连相关状态
    var isUserDisconnected by remember { mutableStateOf(false) }
    var isConnecting by remember { mutableStateOf(false) }
    
    var showSettings by remember { mutableStateOf(false) }

    val scope = rememberCoroutineScope()

    fun saveConfig() {
        sharedPrefs.edit().apply {
            putString("selected_pc", selectedPcId)
            if (selectedPcId == "A") {
                putString("pc_a_name", currentName); putString("pc_a_ip", currentIp); putString("pc_a_port", currentPort)
                pcAName = currentName; pcAIp = currentIp; pcAPort = currentPort
            } else {
                putString("pc_b_name", currentName); putString("pc_b_ip", currentIp); putString("pc_b_port", currentPort)
                pcBName = currentName; pcBIp = currentIp; pcBPort = currentPort
            }
            apply()
        }
    }

    // 修改 disconnect 函数，区分手动和异常
    fun disconnect(isManual: Boolean = true) {
        scope.launch(Dispatchers.IO) {
            try {
                writer?.close()
                socket?.close()
            } catch (e: Exception) {
                Log.e("TCP", "关闭出错: ${e.message}")
            } finally {
                withContext(Dispatchers.Main) {
                    isConnected = false
                    socket = null
                    writer = null
                    if (isManual) {
                        isUserDisconnected = true
                        connectionStatus = "已断开"
                    } else {
                        connectionStatus = "连接已断开，正在尝试重连..."
                    }
                }
            }
        }
    }

    fun connect(ip: String, port: String, isAuto: Boolean = false) {
        if (ip.isEmpty() || isConnecting || isConnected) return
        
        isConnecting = true
        if (!isAuto) {
            connectionStatus = "正在连接..."
            isUserDisconnected = false // 用户主动点击连接，重置手动断开标识
        }
        
        scope.launch(Dispatchers.IO) {
            try {
                val newSocket = Socket()
                newSocket.connect(InetSocketAddress(ip, port.toInt()), 2000) // 超时缩短为 2s 提升重连体验
                val newWriter = PrintWriter(newSocket.getOutputStream(), true)
                withContext(Dispatchers.Main) {
                    socket = newSocket
                    writer = newWriter
                    isConnected = true
                    connectionStatus = "✅ 已连接"
                    isConnecting = false
                    saveConfig()
                }
                launch(Dispatchers.IO) {
                    try {
                        val inputStream = newSocket.getInputStream()
                        while (isConnected) {
                            if (inputStream.read() == -1) break
                        }
                    } catch (e: Exception) {
                        Log.d("TCP", "异常断开: ${e.message}")
                    } finally {
                        disconnect(isManual = false)
                    }
                }
            } catch (e: Exception) {
                withContext(Dispatchers.Main) {
                    isConnecting = false
                    if (!isAuto) {
                        connectionStatus = "❌ 失败: ${e.localizedMessage}"
                    } else if (!isUserDisconnected) {
                        connectionStatus = "正在重连中..."
                    }
                }
            }
        }
    }

    // 自动重连循环逻辑
    LaunchedEffect(isConnected, isUserDisconnected, currentIp, currentPort) {
        if (!isConnected && !isUserDisconnected && currentIp.isNotEmpty()) {
            while (!isConnected && !isUserDisconnected) {
                connect(currentIp, currentPort, isAuto = true)
                delay(3000) // 每 3 秒重连一次
            }
        }
    }

    fun sendMessage(button: String, action: String) {
        if (isConnected && writer != null) {
            scope.launch(Dispatchers.IO) {
                try {
                    val json = "{\"button\":\"$button\",\"action\":\"$action\"}"
                    writer?.println(json)
                    writer?.flush()
                    if (writer?.checkError() == true) disconnect(isManual = false)
                } catch (e: Exception) {
                    disconnect(isManual = false)
                }
            }
        } else {
            connectionStatus = "未连接"
        }
    }

    Row(
        modifier = modifier.fillMaxSize().padding(16.dp),
        verticalAlignment = Alignment.CenterVertically
    ) {
        Column(
            modifier = Modifier.weight(0.4f).fillMaxHeight().verticalScroll(rememberScrollState()),
            horizontalAlignment = Alignment.Start
        ) {
            Text(text = currentName, fontSize = 20.sp, fontWeight = FontWeight.Bold, color = MaterialTheme.colorScheme.primary)
            Text(
                text = "状态: $connectionStatus",
                fontSize = 13.sp,
                color = if (isConnected) Color(0xFF4CAF50) else Color.Gray,
                lineHeight = 16.sp
            )
            
            Spacer(modifier = Modifier.height(12.dp))
            
            Row(modifier = Modifier.fillMaxWidth()) {
                Button(
                    onClick = { if (!isConnected) connect(currentIp, currentPort) else disconnect(isManual = true) },
                    modifier = Modifier.weight(1f)
                ) { Text(if (isConnected) "断开" else "连接") }
                Spacer(modifier = Modifier.width(8.dp))
                Button(
                    onClick = { showSettings = !showSettings },
                    modifier = Modifier.weight(0.8f),
                    colors = ButtonDefaults.buttonColors(containerColor = MaterialTheme.colorScheme.secondary)
                ) { Text(if (showSettings) "收起" else "设置") }
            }

            AnimatedVisibility(visible = showSettings) {
                Column(modifier = Modifier.padding(top = 16.dp)) {
                    HorizontalDivider()
                    Spacer(modifier = Modifier.height(12.dp))
                    Row(modifier = Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.SpaceBetween) {
                        Button(
                            onClick = { if (selectedPcId != "A") { disconnect(isManual = true); selectedPcId = "A"; isUserDisconnected = false } },
                            modifier = Modifier.weight(1f).padding(end = 4.dp),
                            colors = ButtonDefaults.buttonColors(containerColor = if (selectedPcId == "A") MaterialTheme.colorScheme.primary else Color.Gray)
                        ) { Text("电脑 A", fontSize = 12.sp) }
                        Button(
                            onClick = { if (selectedPcId != "B") { disconnect(isManual = true); selectedPcId = "B"; isUserDisconnected = false } },
                            modifier = Modifier.weight(1f).padding(start = 4.dp),
                            colors = ButtonDefaults.buttonColors(containerColor = if (selectedPcId == "B") MaterialTheme.colorScheme.primary else Color.Gray)
                        ) { Text("电脑 B", fontSize = 12.sp) }
                    }
                    Spacer(modifier = Modifier.height(8.dp))
                    OutlinedTextField(value = currentName, onValueChange = { currentName = it }, label = { Text("名称") }, modifier = Modifier.fillMaxWidth(), enabled = !isConnected)
                    Spacer(modifier = Modifier.height(4.dp))
                    OutlinedTextField(value = currentIp, onValueChange = { currentIp = it }, label = { Text("IP 地址") }, modifier = Modifier.fillMaxWidth(), enabled = !isConnected)
                    Spacer(modifier = Modifier.height(4.dp))
                    OutlinedTextField(value = currentPort, onValueChange = { currentPort = it }, label = { Text("端口") }, modifier = Modifier.fillMaxWidth(), enabled = !isConnected)
                }
            }
        }

        Spacer(modifier = Modifier.width(32.dp))

        Box(modifier = Modifier.weight(0.6f).fillMaxHeight(), contentAlignment = Alignment.Center) {
            val buttonSize = 90.dp
            val offset = 100.dp
            PadButton(label = "△", color = Color(0xFF4CAF50), modifier = Modifier.align(Alignment.Center).padding(bottom = offset * 2), size = buttonSize, onDown = { sendMessage("triangle", "down") }, onUp = { sendMessage("triangle", "up") })
            PadButton(label = "▢", color = Color(0xFFE91E63), modifier = Modifier.align(Alignment.Center).padding(end = offset * 2), size = buttonSize, onDown = { sendMessage("square", "down") }, onUp = { sendMessage("square", "up") })
            PadButton(label = "○", color = Color(0xFFF44336), modifier = Modifier.align(Alignment.Center).padding(start = offset * 2), size = buttonSize, onDown = { sendMessage("circle", "down") }, onUp = { sendMessage("circle", "up") })
            PadButton(label = "✖", color = Color(0xFF2196F3), modifier = Modifier.align(Alignment.Center).padding(top = offset * 2), size = buttonSize, onDown = { sendMessage("cross", "down") }, onUp = { sendMessage("cross", "up") })
        }
    }
}

@Composable
fun PadButton(label: String, color: Color, modifier: Modifier = Modifier, size: androidx.compose.ui.unit.Dp = 80.dp, onDown: () -> Unit, onUp: () -> Unit) {
    Surface(modifier = modifier.size(size).pointerInput(Unit) { detectTapGestures(onPress = { onDown(); tryAwaitRelease(); onUp() }) }, shape = CircleShape, color = color, shadowElevation = 8.dp) {
        Box(contentAlignment = Alignment.Center) { Text(text = label, fontSize = (size.value * 0.4).sp, fontWeight = FontWeight.Bold, color = Color.White) }
    }
}
