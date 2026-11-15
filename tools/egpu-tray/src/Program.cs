using System;
using System.Diagnostics;
using System.Threading;
using System.Windows.Forms;

namespace EgpuTray
{
    internal static class Program
    {
        [STAThread]
        private static void Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            if (args.Length > 0 && args[0] == "--tray")
            {
                bool isFirstInstance;
                using (new Mutex(true, @"Global\egpu-tray", out isFirstInstance))
                {
                    if (isFirstInstance) Application.Run(new TrayApp());
                }
                return;
            }

            Application.Run(new InstallerForm());
        }

        internal static int RunHidden(string fileName, string arguments)
        {
            var info = new ProcessStartInfo(fileName, arguments) { UseShellExecute = false, CreateNoWindow = true };
            using (var process = Process.Start(info))
            {
                process.WaitForExit();
                return process.ExitCode;
            }
        }
    }
}
