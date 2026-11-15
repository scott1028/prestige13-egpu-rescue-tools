using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Threading;
using System.Windows.Forms;

namespace EgpuTray
{
    internal sealed class TrayApp : ApplicationContext
    {
        public const string ExitEventName = @"Global\egpu-tray-exit";

        private const int PollMs = 5000;
        // A replug shows short abnormal states, so only notify when it lasts.
        private const int AbnormalNotifySec = 20;
        private const int BalloonMs = 10000;

        private readonly NotifyIcon notifyIcon;
        private readonly ToolStripMenuItem statusItem;
        private readonly ToolStripMenuItem reEnableItem;
        private readonly System.Windows.Forms.Timer pollTimer;
        private readonly EventWaitHandle exitEvent;
        private readonly Icon grayIcon = CreateDotIcon(Color.Gray);
        private readonly Icon greenIcon = CreateDotIcon(Color.LimeGreen);
        private readonly Icon redIcon = CreateDotIcon(Color.Red);

        private EgpuState state = new EgpuState();
        private bool isDisabledByProfile;
        private DateTime? abnormalSince;
        private bool isAbnormalNotified;
        private bool isErrorNotified;
        private Process rescueProcess;
        private Action balloonAction;

        public TrayApp()
        {
            exitEvent = new EventWaitHandle(false, EventResetMode.ManualReset, ExitEventName);

            statusItem = new ToolStripMenuItem { Enabled = false };
            reEnableItem = new ToolStripMenuItem("重新啟用 GPU（解除 Code 22 停用）", null, HandleReEnableClick);
            var menu = new ContextMenuStrip();
            menu.Items.Add(statusItem);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(reEnableItem);
            menu.Items.Add(new ToolStripMenuItem("執行 egpu-connect 救援", null, HandleRescueClick));
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(new ToolStripMenuItem("結束", null, HandleExitClick));

            notifyIcon = new NotifyIcon { ContextMenuStrip = menu, Icon = grayIcon, Text = "eGPU", Visible = true };
            notifyIcon.BalloonTipClicked += HandleBalloonTipClick;

            pollTimer = new System.Windows.Forms.Timer { Interval = PollMs };
            pollTimer.Tick += HandlePollTimerTick;
            pollTimer.Start();
            RefreshState();
        }

        private void RefreshState()
        {
            try
            {
                state = EgpuDevice.ReadState();
                isDisabledByProfile = EgpuDevice.GetIsDisabledByProfile(state);
                string text = state.Describe();
                notifyIcon.Icon = !state.IsConnected ? grayIcon : state.IsGpuOk ? greenIcon : redIcon;
                notifyIcon.Text = "eGPU：" + text;
                statusItem.Text = "狀態：" + text;
                reEnableItem.Visible = isDisabledByProfile;
                NotifyIfStillAbnormal();
                isErrorNotified = false;
            }
            catch (Exception ex)
            {
                ShowError(ex);
            }
        }

        private void NotifyIfStillAbnormal()
        {
            bool isRescueRunning = rescueProcess != null && !rescueProcess.HasExited;
            if (!state.IsConnected || state.IsGpuOk || isRescueRunning)
            {
                abnormalSince = null;
                isAbnormalNotified = false;
                return;
            }
            if (abnormalSince == null) abnormalSince = DateTime.Now;
            if (isAbnormalNotified || (DateTime.Now - abnormalSince.Value).TotalSeconds < AbnormalNotifySec) return;

            isAbnormalNotified = true;
            if (isDisabledByProfile)
            {
                ShowBalloon("eGPU 被停用了", "GPU 被「Disconnect GPU」停用（Code 22），點這裡重新啟用。", ReEnableGpu);
            }
            else
            {
                ShowBalloon("eGPU 異常", state.Describe() + "。點這裡執行 egpu-connect 救援。", StartRescue);
            }
        }

        private void ShowBalloon(string title, string text, Action onClick)
        {
            balloonAction = onClick;
            notifyIcon.ShowBalloonTip(BalloonMs, title, text, onClick == null ? ToolTipIcon.Info : ToolTipIcon.Warning);
        }

        private void ShowError(Exception ex)
        {
            notifyIcon.Text = "eGPU：檢查失敗";
            if (isErrorNotified) return;
            isErrorNotified = true;
            ShowBalloon("egpu-tray 發生錯誤", ex.Message, null);
        }

        private void StartRescue()
        {
            if (rescueProcess != null && !rescueProcess.HasExited) return;
            string script = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "egpu-connect", "egpu-connect.ps1");
            if (!File.Exists(script)) throw new FileNotFoundException("找不到 egpu-connect.ps1，請先用安裝視窗安裝。", script);
            // Tray runs elevated, so egpu-connect.ps1 skips its own UAC prompt.
            rescueProcess = Process.Start("powershell.exe", "-NoProfile -ExecutionPolicy Bypass -File \"" + script + "\"");
        }

        private void ReEnableGpu()
        {
            if (!isDisabledByProfile) return;
            EgpuDevice.ReEnableGpu(state.GpuInstanceId);
            RefreshState();
            if (isDisabledByProfile || !state.IsGpuOk)
            {
                ShowBalloon("已解除停用", "如果 GPU 還沒恢復，請拔插 eGPU 一次。", null);
            }
        }

        private void RunGuarded(Action action)
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                ShowError(ex);
            }
        }

        private void HandlePollTimerTick(object sender, EventArgs e)
        {
            if (exitEvent.WaitOne(0))
            {
                ExitThread();
                return;
            }
            RefreshState();
        }

        private void HandleBalloonTipClick(object sender, EventArgs e)
        {
            if (balloonAction != null) RunGuarded(balloonAction);
        }

        private void HandleReEnableClick(object sender, EventArgs e)
        {
            RunGuarded(ReEnableGpu);
        }

        private void HandleRescueClick(object sender, EventArgs e)
        {
            RunGuarded(StartRescue);
        }

        private void HandleExitClick(object sender, EventArgs e)
        {
            ExitThread();
        }

        protected override void ExitThreadCore()
        {
            pollTimer.Stop();
            notifyIcon.Visible = false;
            notifyIcon.Dispose();
            exitEvent.Dispose();
            base.ExitThreadCore();
        }

        private static Icon CreateDotIcon(Color color)
        {
            Size size = SystemInformation.SmallIconSize;
            using (var bitmap = new Bitmap(size.Width, size.Height))
            {
                using (var graphics = Graphics.FromImage(bitmap))
                using (var brush = new SolidBrush(color))
                {
                    graphics.SmoothingMode = SmoothingMode.AntiAlias;
                    graphics.FillEllipse(brush, 1, 1, size.Width - 3, size.Height - 3);
                }
                // The icon handle lives for the whole app, so it is never destroyed.
                return Icon.FromHandle(bitmap.GetHicon());
            }
        }
    }
}
