using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Security;
using System.Security.Principal;
using System.Text;
using System.Threading;

namespace EgpuTray
{
    internal static class Installer
    {
        private const string TaskName = "egpu-tray";
        private const string ExeName = "egpu-tray.exe";
        private const string RescueResourceName = "egpu-connect.ps1";
        private const int TrayExitWaitMs = 10000;

        // {0} = user, {1} = exe path. Starts the tray elevated at logon without a UAC prompt.
        private const string TaskXmlTemplate = @"<?xml version=""1.0"" encoding=""UTF-16""?>
<Task version=""1.2"" xmlns=""http://schemas.microsoft.com/windows/2004/02/mit/task"">
  <RegistrationInfo>
    <Description>eGPU status and rescue in the taskbar (prestige13-egpu-rescue-tools)</Description>
  </RegistrationInfo>
  <Triggers>
    <LogonTrigger>
      <Enabled>true</Enabled>
      <UserId>{0}</UserId>
      <Delay>PT15S</Delay>
    </LogonTrigger>
  </Triggers>
  <Principals>
    <Principal id=""Author"">
      <UserId>{0}</UserId>
      <LogonType>InteractiveToken</LogonType>
      <RunLevel>HighestAvailable</RunLevel>
    </Principal>
  </Principals>
  <Settings>
    <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>
    <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>
    <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>
    <ExecutionTimeLimit>PT0S</ExecutionTimeLimit>
  </Settings>
  <Actions Context=""Author"">
    <Exec>
      <Command>{1}</Command>
      <Arguments>--tray</Arguments>
    </Exec>
  </Actions>
</Task>";

        public static readonly string InstallDir =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "egpu-tray");

        public static bool GetIsInstalled()
        {
            return Directory.Exists(InstallDir) || Program.RunHidden("schtasks.exe", "/Query /TN " + TaskName) == 0;
        }

        public static void Install()
        {
            StopTray();
            string exePath = Path.Combine(InstallDir, ExeName);
            string rescueDir = Path.Combine(InstallDir, "egpu-connect");
            Directory.CreateDirectory(rescueDir);

            Assembly self = Assembly.GetExecutingAssembly();
            if (!GetIsSamePath(self.Location, exePath)) File.Copy(self.Location, exePath, true);
            using (Stream resource = self.GetManifestResourceStream(RescueResourceName))
            using (FileStream file = File.Create(Path.Combine(rescueDir, RescueResourceName)))
            {
                resource.CopyTo(file);
            }

            RegisterTask(exePath);
            RunChecked("schtasks.exe", "/Run /TN " + TaskName);
        }

        // Returns true when the folder is deleted only after this process exits.
        public static bool Uninstall()
        {
            StopTray();
            // Exit code is non-zero when the task does not exist; that is fine.
            Program.RunHidden("schtasks.exe", "/Delete /TN " + TaskName + " /F");
            if (!Directory.Exists(InstallDir)) return false;

            string selfDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            if (!GetIsSamePath(selfDir, InstallDir))
            {
                Directory.Delete(InstallDir, true);
                return false;
            }
            // This exe lives in the folder, so delete it after this process exits.
            var info = new ProcessStartInfo("cmd.exe", "/c ping -n 4 127.0.0.1 >nul & rmdir /s /q \"" + InstallDir + "\"")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = Path.GetTempPath()
            };
            Process.Start(info).Dispose();
            return true;
        }

        private static void RegisterTask(string exePath)
        {
            string xml = string.Format(TaskXmlTemplate,
                SecurityElement.Escape(WindowsIdentity.GetCurrent().Name), SecurityElement.Escape(exePath));
            string xmlPath = Path.Combine(Path.GetTempPath(), "egpu-tray-task.xml");
            // schtasks expects UTF-16 task XML.
            File.WriteAllText(xmlPath, xml, Encoding.Unicode);
            try
            {
                RunChecked("schtasks.exe", "/Create /TN " + TaskName + " /XML \"" + xmlPath + "\" /F");
            }
            finally
            {
                File.Delete(xmlPath);
            }
        }

        private static void StopTray()
        {
            EventWaitHandle exitEvent;
            if (!EventWaitHandle.TryOpenExisting(TrayApp.ExitEventName, out exitEvent)) return;
            using (exitEvent)
            {
                exitEvent.Set();
                int selfId = Process.GetCurrentProcess().Id;
                foreach (Process process in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(ExeName)))
                {
                    using (process)
                    {
                        if (process.Id == selfId) continue;
                        if (!process.WaitForExit(TrayExitWaitMs)) process.Kill();
                    }
                }
                // Reset so the next tray does not exit right after it starts.
                exitEvent.Reset();
            }
        }

        private static void RunChecked(string fileName, string arguments)
        {
            int exitCode = Program.RunHidden(fileName, arguments);
            if (exitCode != 0)
            {
                throw new InvalidOperationException(fileName + " " + arguments + " 失敗（結束代碼 " + exitCode + "）");
            }
        }

        private static bool GetIsSamePath(string pathA, string pathB)
        {
            return string.Equals(Path.GetFullPath(pathA).TrimEnd('\\'), Path.GetFullPath(pathB).TrimEnd('\\'),
                StringComparison.OrdinalIgnoreCase);
        }
    }
}
