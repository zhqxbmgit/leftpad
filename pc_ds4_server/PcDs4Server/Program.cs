using System;
using System.Windows.Forms;

namespace PcDs4Server
{
    internal static class Program
    {
        [STAThread]
        static int Main(string[] args)
        {
            Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);

            if (args.Contains("--keyboard-smoke-test", StringComparer.OrdinalIgnoreCase))
            {
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                return KeyboardSmokeTest.Run();
            }

            using var coordinator = SingleInstanceCoordinator.Acquire(
                SingleInstanceIdentity.MutexName);
            if (!coordinator.IsPrimaryInstance)
            {
                var client = new SingleInstanceActivationClient(
                    SingleInstanceIdentity.ActivationPipeName);
                SingleInstanceActivationResult activation = client.TryActivateAsync()
                    .GetAwaiter()
                    .GetResult();
                return activation == SingleInstanceActivationResult.Activated ? 0 : 1;
            }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            var service = new Ds4Service();
            var mainForm = new MainForm(service);
            SingleInstanceActivationServer? activationServer = null;
            mainForm.Shown += (_, _) =>
            {
                if (activationServer != null) return;
                activationServer = new SingleInstanceActivationServer(
                    SingleInstanceIdentity.ActivationPipeName,
                    mainForm.RequestExistingInstanceActivationAsync);
                activationServer.Start();
            };

            try
            {
                Application.Run(mainForm);
                return 0;
            }
            finally
            {
                activationServer?.Dispose();
                service.Stop();
            }
        }
    }
}
