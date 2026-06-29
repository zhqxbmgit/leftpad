package com.zhq.pad

import android.os.Bundle
import android.util.Log
import androidx.activity.ComponentActivity
import androidx.activity.compose.setContent
import androidx.activity.enableEdgeToEdge
import androidx.compose.foundation.gestures.detectTapGestures
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.material3.Button
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.Scaffold
import androidx.compose.material3.Surface
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.rememberCoroutineScope
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.input.pointer.pointerInput
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
    var ipAddress by remember { mutableStateOf("192.168.1.5") }
    var port by remember { mutableStateOf("8888") }
    var connectionStatus by remember { mutableStateOf("未连接") }
    var isConnected by remember { mutableStateOf(false) }
    var socket: Socket? by remember { mutableStateOf(null) }
    var writer: PrintWriter? by remember { mutableStateOf(null) }

    val scope = rememberCoroutineScope()

    // 统一断开连接的逻辑
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

    // 发送消息的函数
    fun sendMessage(button: String, action: String) {
        if (isConnected && writer != null) {
            scope.launch(Dispatchers.IO) {
                try {
                    val json = "{\"button\":\"$button\",\"action\":\"$action\"}"
                    writer?.println(json)
                    writer?.flush()
                    // 检查是否发生错误（如连接断开）
                    if (writer?.checkError() == true) {
                        Log.e("TCP", "检测到 Writer 错误，可能已断连")
                        disconnect()
                    }
                } catch (e: Exception) {
                    Log.e("TCP", "发送失败: ${e.message}")
                    disconnect()
                }
            }
        }
    }

    Column(
        modifier = modifier
            .fillMaxSize()
            .padding(16.dp),
        horizontalAlignment = Alignment.CenterHorizontally
    ) {
        // 1. 输入区域
        OutlinedTextField(
            value = ipAddress,
            onValueChange = { ipAddress = it },
            label = { Text("电脑 IP 地址") },
            modifier = Modifier.fillMaxWidth(),
            enabled = !isConnected
        )
        Spacer(modifier = Modifier.height(8.dp))
        OutlinedTextField(
            value = port,
            onValueChange = { port = it },
            label = { Text("端口号") },
            modifier = Modifier.fillMaxWidth(),
            enabled = !isConnected
        )
        Spacer(modifier = Modifier.height(16.dp))

        // 2. 连接按钮和状态
        Button(
            onClick = {
                if (!isConnected) {
                    connectionStatus = "正在连接..."
                    scope.launch(Dispatchers.IO) {
                        try {
                            val newSocket = Socket()
                            // 设置连接超时为 3秒
                            newSocket.connect(InetSocketAddress(ipAddress, port.toInt()), 3000)
                            val newWriter = PrintWriter(newSocket.getOutputStream(), true)
                            
                            withContext(Dispatchers.Main) {
                                socket = newSocket
                                writer = newWriter
                                isConnected = true
                                connectionStatus = "✅ 已连接"
                            }
                            
                            // 启动监听线程，检测服务器是否主动断开
                            launch(Dispatchers.IO) {
                                try {
                                    val inputStream = newSocket.getInputStream()
                                    while (isConnected) {
                                        if (inputStream.read() == -1) break // 服务器断开
                                    }
                                } catch (e: Exception) {
                                    Log.d("TCP", "监听断开: ${e.message}")
                                } finally {
                                    disconnect()
                                }
                            }

                        } catch (e: Exception) {
                            withContext(Dispatchers.Main) {
                                connectionStatus = "❌ 连接失败: ${e.localizedMessage ?: "未知错误"}"
                            }
                        }
                    }
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

        // 3. 四个面部按钮 (○, ✖, △, ▢)
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
