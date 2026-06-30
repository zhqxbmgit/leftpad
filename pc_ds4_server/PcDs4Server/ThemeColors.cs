using System.Drawing;

namespace PcDs4Server
{
    public static class ThemeColors
    {
        public static readonly Color Background = Color.FromArgb(9, 11, 42);      // 深蓝黑
        public static readonly Color Sidebar = Color.FromArgb(17, 19, 58);        // 侧边栏
        public static readonly Color ContentPanel = Color.FromArgb(26, 21, 69);   // 主面板
        public static readonly Color AccentPurple = Color.FromArgb(177, 92, 255); // 高亮紫
        public static readonly Color BorderPurple = Color.FromArgb(185, 140, 255);// 边框紫
        public static readonly Color TextMain = Color.White;                      // 主文字
        public static readonly Color TextSecondary = Color.FromArgb(201, 198, 232);// 次要文字
        public static readonly Color Success = Color.FromArgb(61, 255, 154);      // 成功绿
        public static readonly Color Warning = Color.FromArgb(255, 209, 102);     // 警告黄
        public static readonly Color Error = Color.FromArgb(255, 77, 109);        // 错误红
        public static readonly Color ControlDark = Color.FromArgb(42, 45, 90);    // 控件暗色
    }
}
