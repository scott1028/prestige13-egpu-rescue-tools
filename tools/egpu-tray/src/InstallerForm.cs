using System;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace EgpuTray
{
    internal sealed class InstallerForm : Form
    {
        private readonly Label statusLabel;
        private readonly Button installButton;
        private readonly Button uninstallButton;

        public InstallerForm()
        {
            Text = "egpu-tray";
            Font = SystemFonts.MessageBoxFont;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;
            Padding = new Padding(12);

            statusLabel = new Label { AutoSize = true, MaximumSize = new Size(Font.Height * 30, 0), Margin = new Padding(0, 0, 0, 12) };
            installButton = new Button { Text = "安裝", AutoSize = true };
            uninstallButton = new Button { Text = "解除安裝", AutoSize = true };
            installButton.Click += HandleInstallClick;
            uninstallButton.Click += HandleUninstallClick;

            var buttons = new FlowLayoutPanel { FlowDirection = FlowDirection.RightToLeft, AutoSize = true, Dock = DockStyle.Fill, Margin = Padding.Empty };
            buttons.Controls.Add(uninstallButton);
            buttons.Controls.Add(installButton);
            var layout = new TableLayoutPanel { ColumnCount = 1, AutoSize = true, Dock = DockStyle.Fill };
            layout.Controls.Add(statusLabel);
            layout.Controls.Add(buttons);
            Controls.Add(layout);

            RefreshStatus();
        }

        private void RefreshStatus()
        {
            bool isInstalled = Installer.GetIsInstalled();
            string egpuText;
            try
            {
                egpuText = EgpuDevice.ReadState().Describe();
            }
            catch (Exception ex)
            {
                egpuText = "讀取失敗：" + ex.Message;
            }
            statusLabel.Text =
                "安裝狀態：" + (isInstalled ? "已安裝" : "未安裝") + "\n" +
                "eGPU：" + egpuText + "\n\n" +
                "安裝：複製到 " + Installer.InstallDir + "，之後每次登入都會自動常駐工作列，不會跳 UAC。\n" +
                "解除安裝：結束工作列程式，移除登入排程和整個安裝資料夾（包含 egpu-connect 的紀錄）。";
            uninstallButton.Enabled = isInstalled;
        }

        private async void HandleInstallClick(object sender, EventArgs e)
        {
            if (!await RunInstallerStep(Installer.Install)) return;
            MessageBox.Show(this, "安裝完成。工作列右下角會出現 eGPU 圖示，可能收在「^」裡。",
                Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private async void HandleUninstallClick(object sender, EventArgs e)
        {
            DialogResult answer = MessageBox.Show(this, "要完整移除 egpu-tray 嗎？安裝資料夾裡的 egpu-connect 紀錄也會一起刪除。",
                Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            if (answer != DialogResult.Yes) return;

            bool isDeleteDeferred = false;
            if (!await RunInstallerStep(() => { isDeleteDeferred = Installer.Uninstall(); })) return;
            MessageBox.Show(this, "已解除安裝。", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
            // The folder is deleted after this window closes.
            if (isDeleteDeferred) Close();
        }

        private async Task<bool> RunInstallerStep(Action step)
        {
            installButton.Enabled = false;
            uninstallButton.Enabled = false;
            UseWaitCursor = true;
            try
            {
                await Task.Run(step);
                return true;
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
                return false;
            }
            finally
            {
                UseWaitCursor = false;
                installButton.Enabled = true;
                RefreshStatus();
            }
        }
    }
}
