import socket
import json
import signal
import sys
from pynput.keyboard import Key, Controller

# 初始化键盘控制器
keyboard = Controller()

# 按键映射表
BUTTON_MAP = {
    "triangle": 'i',
    "square": 'j',
    "cross": 'k',
    "circle": 'l'
}

# 记录当前按下的按键（状态集合）
pressed_keys = set()

def release_all_keys():
    """安全释放所有已按下的键"""
    if pressed_keys:
        print("\n正在释放所有按键...")
        for key in list(pressed_keys):
            try:
                keyboard.release(key)
                print(f"释放: {key.upper()}")
            except:
                pass
        pressed_keys.clear()

def signal_handler(sig, frame):
    """处理 Ctrl+C 信号"""
    print("\n检测到退出信号...")
    release_all_keys()
    sys.exit(0)

# 注册信号处理
signal.signal(signal.SIGINT, signal_handler)

def get_local_ip():
    """自动获取局域网 IP 地址"""
    try:
        # 创建一个临时 Socket 来探测 local IP
        s = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
        # 即使 IP 不存在也没关系
        s.connect(("8.8.8.8", 80))
        ip = s.getsockname()[0]
        s.close()
        return ip
    except Exception:
        return "127.0.0.1"

def start_server(port=8888):
    local_ip = get_local_ip()
    server_socket = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
    # 允许端口重用，防止重启时报端口占用错误
    server_socket.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 1)

    try:
        server_socket.bind(('0.0.0.0', port))
    except Exception as e:
        print(f"❌ 绑定端口 {port} 失败: {e}")
        return

    server_socket.listen(1)
    print("========================================")
    print(f"🚀 电脑端服务已启动")
    print(f"📍 电脑局域网 IP: {local_ip}")
    print(f"🔌 监听端口: {port}")
    print("========================================")
    print("等待手机连接... (按 Ctrl+C 退出)")

    while True:
        try:
            client_socket, addr = server_socket.accept()
            print(f"✅ 手机已连接！来自: {addr}")

            try:
                while True:
                    data = client_socket.recv(1024)
                    if not data:
                        break

                    try:
                        # 处理可能包含多条 JSON 的情况（TCP 粘包简易处理）
                        raw_data = data.decode('utf-8').strip()
                        if not raw_data:
                            continue

                        # 如果收到多个 JSON，拆分处理
                        for line in raw_data.split('\n'):
                            if not line.strip(): continue
                            message = json.loads(line)
                            button = message.get("button")
                            action = message.get("action")
                            key_to_press = BUTTON_MAP.get(button)

                            if key_to_press:
                                if action == "down":
                                    if key_to_press not in pressed_keys:
                                        print(f"按下: {button} -> {key_to_press.upper()}")
                                        keyboard.press(key_to_press)
                                        pressed_keys.add(key_to_press)
                                elif action == "up":
                                    if key_to_press in pressed_keys:
                                        print(f"松开: {button} -> {key_to_press.upper()}")
                                        keyboard.release(key_to_press)
                                        pressed_keys.remove(key_to_press)
                    except json.JSONDecodeError:
                        print(f"⚠️ 收到格式错误的指令: {data}")
                    except Exception as e:
                        print(f"处理指令时发生错误: {e}")

            except ConnectionResetError:
                print("❌ 手机意外断开连接")
            except Exception as e:
                print(f"连接过程发生错误: {e}")
            finally:
                release_all_keys()
                client_socket.close()
                print("\n等待下一个连接...")

        except Exception as e:
            print(f"服务器运行异常: {e}")
            break

if __name__ == "__main__":
    start_server()
