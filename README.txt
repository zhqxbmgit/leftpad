LeftPad — Android LAN controller / Windows DS4 Receiver
=====================================================

当前接收器：pc_ds4_server/PcDs4Server（PcDs4Server.exe）。
默认 Direct DS4 输出使用 ViGEmBus 创建虚拟 DualShock 4；手机未连接时
虚拟 DS4 仍保留。现有 Keyboard 模式及映射仍可在 Windows 端选择。
pc_server.py 是旧接收器，不是当前生产连接路径。

连接
----
1. Windows 启动 LeftPad Receiver，确认 TCP listener 和输出初始化成功。
2. 手机连接同一局域网的 Wi-Fi，打开 LeftPad。
3. App 显示“搜索中”，自动发现并连接，随后显示“已连接 · 实际 IPv4”。
   无需填写 IP、端口或选择 PC A/B；没有固定 IP 回退、云服务或账号依赖。
4. Android 17 首次使用需要允许“本地网络”权限；未授权时不会启动 discovery。
   拒绝后不循环弹窗，可通过控制台中的权限按钮或系统设置重新授权。
   Android 16 及以下不请求该权限。

两台 PC 的正常使用场景是处于不同地点、不同 LAN。Searching 时首个合法
OFFER 获选；已有目标时其他 receiverId 不抢占。当前目标失效后重新发现。
DHCP 地址变化、App 重启和 Receiver 重启不需要修改手机配置。

网络端口
--------
TCP 8888 = controller transport（现有逐行消息格式，TCP_NODELAY 保持启用）。
UDP 8889 = LeftPad Receiver Discovery v1。
与 rightpad 的 UDP 50000 / 50001 不冲突。

Windows 防火墙按需允许专用网络入站 TCP 8888 和 UDP 8889。
Discovery 独立规则名：LeftPad Receiver Discovery UDP 8889；Inbound / Allow /
UDP / LocalPort 8889 / Private only。不开放 Public，不修改 rightpad 规则。
如果已有 TCP 8888 规则，不需要重建。

Windows Receiver 的“设置 → 基础 → 接收器界面”提供 Start with Windows
（开机启动）开关。开启后，Receiver 会在当前 Windows 用户登录时自动启动；
关闭开关只会删除 LeftPad 自己的当前用户启动项。
若仍搜索中，检查 Wi-Fi 与 PC 是否在同一 LAN、Receiver 是否启动，以及
防火墙或路由器的客户端隔离是否阻止广播/连接。

Discovery v1（全部多字节整数为 Little Endian）
----------------------------------------------
DISCOVER 恰好 16 bytes：
  0..3   ASCII LPAD
  4      version = 1
  5      type = 1
  6..7   reserved = 0
  8..15  uint64 nonce

OFFER 恰好 40 bytes：
  0..3   ASCII LPAD
  4      version = 1
  5      type = 2
  6..7   reserved = 0
  8..15  uint64 nonce（echo DISCOVER）
  16..31 receiverId（16 opaque bytes，不做 GUID 字节序转换）
  32..33 uint16 controllerPort = 8888
  34     controllerProtocolVersion = 1
  35     reserved = 0
  36..39 uint32 capabilities = 0

严格检查长度、magic、版本、type、reserved、nonce、端口、控制协议版本及
v1 capabilities；未知/非法包忽略并低频累计诊断。
OFFER 不含 IP 或 hostname。Android 仅将收到的 UDP datagram source IPv4
作为 authoritative address；不会使用 Windows 默认路由、首块网卡、VPN/TUN
地址选择逻辑来填充 payload。

每台 Windows 的随机 receiverId 存放于：
  %LocalAppData%\leftpad\receiver-id
文件为 32 hex chars，原子创建/替换，重启复用；损坏时输出诊断并重建。
它不是 secret，不使用 MAC、hostname、SID 或用户名。

Android 通过 NetworkCallback 跟踪非 VPN 的 Wi-Fi Network，使用
Network.bindSocket 仅绑定独立 discovery DatagramSocket，不改变 App 整体路由。
每轮向 255.255.255.255:8889 和由该 Network 的 IPv4 LinkAddress/prefixLength
计算出的 directed broadcast 发送探测，去重、忽略 IPv6；/31、/32 无广播主机。
Searching 初始节奏为 0 / 250 / 500 / 1000 ms，之后每 1000 ms 一次；
有目标时约每 1000 ms 探测。2500 ms 没有当前 receiverId 的合法 OFFER 则清除
 target、断开 TCP、取消 sender session 并重新 Searching。
此计时仅用于 discovery target liveness，不是 controller input heartbeat/fail-safe。
TCP 连接失败后清除 target，至少等待 1000 ms 再获选，避免紧密重试。

目标切换先取消旧 OrderedMessageSender session 和 queue，关闭旧 socket/writer，
再建立新连接；不会重放已按住的按钮，也不会把旧手指释放事件发送给新 PC。
普通按钮路径仍只构造 OutgoingMessage 并交给 OrderedMessageSender.tryEnqueue。
按钮布局、布局编辑、电量、DS4 模式和 Task Manager 按钮保留。

验证
----
Android: gradlew.bat test :app:assembleDebug :app:lintDebug
Windows: dotnet restore / build Debug / build Release / test
项目: pc_ds4_server/PcDs4Server.Tests/PcDs4Server.Tests.csproj
DiscoveryTests 包含协议 golden vector、identity、UDP responder 和 TCP readiness；
Android DiscoveryTest 包含广播前缀、选择/超时/退避及 A -> stale -> B 队列隔离。
第二地点的真实 PC / LAN 需要在现场验证；确定性测试不替代双地点实机验收。
