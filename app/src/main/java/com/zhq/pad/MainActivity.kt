package com.zhq.pad

import android.content.Context
import android.os.Bundle
import android.util.Log
import androidx.activity.ComponentActivity
import androidx.activity.compose.setContent
import androidx.activity.enableEdgeToEdge
import androidx.compose.foundation.gestures.detectTapGestures
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.material3.Button
import androidx.compose.material3.ButtonDefaults
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
import com.zhq.pad.ui.theme.PadTheme
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.launch
import kotlinx.coroutines.withContext
import java.io.PrintWriter
import java.net.InetSocketAddress
import java.net.Socket

data class PCConfig(
    val id: String, // "A" or "B"
    var name: String,
    var ip: String,
    var port: String
)

class MainActivity : ComponentActivity() {
    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
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
    
    // 加载保存的数据
    var selectedPcId by remember { mutableStateOf(sharedPrefs.getString("selected_pc", "A") ?: "A") }
    
    var pcAName by remember { mutableStateOf(sharedPrefs.getString("pc_a_name", "电脑 A") ?: "电脑 A") }
    var pcAIp by remember { mutableStateOf(sharedPrefs.getString("pc_a_ip", "") ?: "") }
    var pcAPort by remember { mutableStateOf(sharedPrefs.getString("pc_a_port", "8888") ?: "8888") }
    
    var pcBName by remember { mutableStateOf(sharedPrefs.getString("pc_b_name", "电脑 B") ?: "电脑 B") }
    var pcBIp by remember { mutableStateOf(sharedPrefs.getString("pc_b_ip", "") ?: "") }
    var pcBPort by remember { mutableStateOf(sharedPrefs.getString("pc_b_port", "8888") ?: "8888") }

    // 当前显示的 IP/端口/名称（绑定到输入框）
    var currentName by remember(selectedPcId) { mutableStateOf(if (selectedPcId == "A") pcAName else pcBName) }
    var currentIp by remember(selectedPcId) { mutableStateOf(if (selectedPcId == "A") pcAIp else pcBIp) }
    var currentPort by remember(selectedPcId) { mutableStateOf(if (selectedPcId == "A") pcAPort else pcBPort) }

    var connectionStatus by remember { mutableStateOf("未连接") }
    var isConnected by remember { mutableStateOf(false) }
    var socket: Socket? by remember { mutableStateOf(null) }
    var writer: PrintWriter? by remember { mutableStateOf(null) }

    val scope = rememberCoroutineScope()

    // 保存配置到本地
    fun saveConfig() {
        sharedPrefs.edit().apply {
            putString("selected_pc", selectedPcId)
            if (selectedPcId == "A") {
                putString("pc_a_name", currentName)
                putString("pc_a_ip", currentIp)
                putString("pc_a_port", currentPort)
                pcAName = currentName
                pcAIp = currentIp
                pcAPort = currentPort
            } else {
                putString("pc_b_name", currentName)
                putString("pc_b_ip", currentIp)
                putString("pc_b_port", currentPort)
                pcBName = currentName
                pcBIp = currentIp
                pcBPort = currentPort
            }
            apply()
        }
    }

    // 统一断开连接逻辑
    fun disconnect() {
        scope.launch(Dispatchers.IO) {
            try {
                writer?.close()
                socket?.close()
            } catch (e: Exception) {
                Log.e("TCP", "关闭连接出错: ${e.message}")
            } finally {
                withContext(Dispatchers.Main) {
                    isConnected = false
                    socket = null
                    writer = null
                    connectionStatus = "已断开"
                }
            }
        }
    }

    // 建立连接逻辑
    fun connect(ip: String, port: String, isAuto: Boolean = false) {
        if (ip.isEmpty()) {
            connectionStatus = "请填写电脑 IP"
            return
        }
        
        connectionStatus = if (isAuto) "正在自动连接..." else "正在连接..."
        scope.launch(Dispatchers.IO) {
            try {
                val newSocket = Socket()
                newSocket.connect(InetSocketAddress(ip, port.toInt()), 3000)
                val newWriter = PrintWriter(newSocket.getOutputStream(), true)
                
                withContext(Dispatchers.Main) {
                    socket = newSocket
                    writer = newWriter
                    isConnected = true
                    connectionStatus = "✅ 已连接"
                    saveConfig() // 连接成功后保存
                }
                
                launch(Dispatchers.IO) {
                    try {
                        val inputStream = newSocket.getInputStream()
                        while (isConnected) {
                            if (inputStream.read() == -1) break
                        }
                    } catch (e: Exception) {
                        Log.d("TCP", "监听断开: ${e.message}")
                    } finally {
                        disconnect()
                    }
                }
            } catch (e: Exception) {
                withContext(Dispatchers.Main) {
                    connectionStatus = if (isAuto) "自动连接失败" else "❌ 连接失败: ${e.localizedMessage}"
                }
            }
        }
    }

    // 启动时自动连接
    LaunchedEffect(Unit) {
        if (currentIp.isNotEmpty()) {
            connect(currentIp, currentPort, isAuto = true)
        } else {
            connectionStatus = "请填写电脑 IP"
        }
    }

    // 发送消息
    fun sendMessage(button: String, action: String) {
        if (isConnected && writer != null) {
            scope.launch(Dispatchers.IO) {
                try {
                    val json = "{\"button\":\"$button\",\"action\":\"$action\"}"
                    writer?.println(json)
                    writer?.flush()
                    if (writer?.checkError() == true) {
                        disconnect()
                    }
                } catch (e: Exception) {
                    disconnect()
                }
            }
        } else {
            connectionStatus = "未连接"
        }
    }

    Column(
        modifier = modifier
            .fillMaxSize()
            .padding(16.dp),
        horizontalAlignment = Alignment.CenterHorizontally
    ) {
        // 1. 电脑选择 A / B
        Row(
            modifier = Modifier.fillMaxWidth(),
            horizontalArrangement = Arrangement.SpaceEvenly
        ) {
            Button(
                onClick = {
                    if (selectedPcId != "A") {
                        disconnect()
                        selectedPcId = "A"
                        // UI 会通过 remember(selectedPcId) 自动更新 currentIp 等
                    }
                },
                colors = ButtonDefaults.buttonColors(
                    containerColor = if (selectedPcId == "A") MaterialTheme.colorScheme.primary else Color.Gray
                ),
                modifier = Modifier.weight(1f).padding(horizontal = 4.dp)
            ) {
                Text("电脑 A")
            }
            Button(
                onClick = {
                    if (selectedPcId != "B") {
                        disconnect()
                        selectedPcId = "B"
                    }
                },
                colors = ButtonDefaults.buttonColors(
                    containerColor = if (selectedPcId == "B") MaterialTheme.colorScheme.primary else Color.Gray
                ),
                modifier = Modifier.weight(1f).padding(horizontal = 4.dp)
            ) {
                Text("电脑 B")
            }
        }

        Spacer(modifier = Modifier.height(16.dp))

        // 2. 配置输入区域
        OutlinedTextField(
            value = currentName,
            onValueChange = { currentName = it },
            label = { Text("电脑名称") },
            modifier = Modifier.fillMaxWidth(),
            enabled = !isConnected
        )
        Spacer(modifier = Modifier.height(8.dp))
        OutlinedTextField(
            value = currentIp,
            onValueChange = { currentIp = it },
            label = { Text("IP 地址") },
            modifier = Modifier.fillMaxWidth(),
            enabled = !isConnected
        )
        Spacer(modifier = Modifier.height(8.dp))
        OutlinedTextField(
            value = currentPort,
            onValueChange = { currentPort = it },
            label = { Text("端口号") },
            modifier = Modifier.fillMaxWidth(),
            enabled = !isConnected
        )

        Spacer(modifier = Modifier.height(16.dp))

        // 3. 连接控制
        Button(
            onClick = {
                if (!isConnected) {
                    connect(currentIp, currentPort)
                } else {
                    disconnect()
                }
            },
            modifier = Modifier.fillMaxWidth()
        ) {
            Text(if (isConnected) "断开连接" else "连接电脑")
        }
        
        Text(
            text = "状态: $connectionStatus", 
            modifier = Modifier.padding(vertical = 8.dp),
            style = MaterialTheme.typography.bodyMedium,
            color = if (isConnected) Color(0xFF4CAF50) else Color.Gray
        )

        Spacer(modifier = Modifier.weight(1f))

        // 4. 四个面部按钮
        Box(modifier = Modifier.size(280.dp), contentAlignment = Alignment.Center) {
            PadButton(
                label = "△",
                color = Color(0xFF4CAF50),
                modifier = Modifier.align(Alignment.TopCenter),
                onDown = { sendMessage("triangle", "down") },
                onUp = { sendMessage("triangle", "up") }
            )
            PadButton(
                label = "▢",
                color = Color(0xFFE91E63),
                modifier = Modifier.align(Alignment.CenterStart),
                onDown = { sendMessage("square", "down") },
                onUp = { sendMessage("square", "up") }
            )
            PadButton(
                label = "○",
                color = Color(0xFFF44336),
                modifier = Modifier.align(Alignment.CenterEnd),
                onDown = { sendMessage("circle", "down") },
                onUp = { sendMessage("circle", "up") }
            )
            PadButton(
                label = "✖",
                color = Color(0xFF2196F3),
                modifier = Modifier.align(Alignment.BottomCenter),
                onDown = { sendMessage("cross", "down") },
                onUp = { sendMessage("cross", "up") }
            )
        }

        Spacer(modifier = Modifier.weight(1f))
    }
}

@Composable
fun PadButton(
    label: String,
    color: Color,
    modifier: Modifier = Modifier,
    onDown: () -> Unit,
    onUp: () -> Unit
) {
    Surface(
        modifier = modifier
            .size(80.dp)
            .pointerInput(Unit) {
                detectTapGestures(
                    onPress = {
                        onDown()
                        tryAwaitRelease()
                        onUp()
                    }
                )
            },
        shape = CircleShape,
        color = color,
        shadowElevation = 8.dp
    ) {
        Box(contentAlignment = Alignment.Center) {
            Text(
                text = label,
                fontSize = 32.sp,
                fontWeight = FontWeight.Bold,
                color = Color.White
            )
        }
    }
}
