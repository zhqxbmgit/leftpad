using System;
using System.Threading;
using System.Windows.Forms;

namespace PcDs4Server
{
    internal static class Program
    {
        private static Mutex? _mutex;

        [STAThread]
        static void Main()
        {
            // 单实例运行控制
            const string mutexName = "Global\\LeftPadDs4Receiver_Mutex";
            _mutex = new Mutex(true, mutexName, out bool createdNew);

            if (!createdNew)
            {
                MessageBox.Show("接收端程序已在运行中。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            var service = new Ds4Service();
            var mainForm = new MainForm(service);

            try
            {
                Application.Run(mainForm);
            }
            finally
            {
                service.Stop();
                _mutex.ReleaseMutex();
            }
        }
    }
}
