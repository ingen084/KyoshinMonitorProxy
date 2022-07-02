using System.ComponentModel;
using System.Data;
using System.Diagnostics;
using System.Reflection;

namespace KyoshinMonitorProxy
{
    public partial class MainForm : Form
    {
        private bool IsServiceLiving { get; set; } = false;
        private bool IsServiceStopping { get; set; }
        private int FailCount { get; set; }
        private HttpClient Client { get; } = new() { Timeout = TimeSpan.FromSeconds(1) };
        System.Threading.Timer UpdateTimer { get; }

        public MainForm()
        {
            InitializeComponent();
            UpdateTimer = new System.Threading.Timer(async _ =>
            {
                string ToHumanReadableBytes(ulong bytes)
                {
                    if (bytes <= 1024)
                        return bytes + "B";
                    else if (bytes / 1024 <= 1024)
                        return $"{bytes / 1024.0:0.00}KB";
                    else if (bytes / 1024 / 1024 <= 1024)
                        return $"{bytes / 1024.0 / 1024.0:0.00}MB";
                    else if (bytes / 1024 / 1024 / 1024 <= 1024)
                        return $"{bytes / 1024.0 / 1024.0 / 1024.0:0.00}GB";
                    else
                        return $"{bytes / 1024.0 / 1024.0 / 1024.0 / 1024.0:0.00}TB";
                }
                try
                {
                    var response = await Client.GetFromJsonAsync("http://127.0.0.100/kmp-status.json", StatusModelContext.Default.StatusModel);
                    IsServiceLiving = true;
                    FailCount = 0;
                    Invoke(() =>
                    {
                        nowstateLabel.Text = "現在の状態: 起動中";
                        label1.Text = $@"全体のリクエスト: {response?.RequestCount / 60.0:0.00}req/s
削減したリクエスト: {response?.HitCacheCount / 60.0:0.00}req/s
強震モニタへのリクエスト: {response?.MissCacheCount / 60.0:0.00}req/s
メモリ: {ToHumanReadableBytes(response?.UsedMemoryBytes ?? 0)}
削減した通信量: {ToHumanReadableBytes(response?.SavedBytes ?? 0)}";
                    });
                }
                catch
                {
                    if (!IsServiceStopping && FailCount++ >= 3 && Invoke(() => autoRestartToolStripMenuItem.Checked))
                    {
                        await StopServiceAsync();
                        await StartServiceAsync();
                    }
                    IsServiceLiving = false;
                    Invoke(() =>
                    {
                        nowstateLabel.Text = "現在の状態: 未起動 or 旧バージョン";
                        label1.Text = "-";
                    });
                }
                UpdateTimer?.Change(TimeSpan.FromSeconds(1), Timeout.InfiniteTimeSpan);
            }, null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        }


        protected override async void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            versionLabel.Text = "v" + Assembly.GetExecutingAssembly()?.GetName().Version?.ToString() ?? "謎";
            autoRestartToolStripMenuItem.Checked = true;
            Client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "KyoshinMonitorProxy");
            await CheckUpdateAsync();
            UpdateTimer.Change(TimeSpan.FromSeconds(1), Timeout.InfiniteTimeSpan);
        }

        private async void timer2_Tick(object sender, EventArgs e)
        {
            await CheckUpdateAsync();
        }

        private async Task CheckUpdateAsync()
        {
            try
            {
                var currentVersion = Assembly.GetExecutingAssembly()?.GetName().Version;
                var releases = (await Client.GetFromJsonAsync("https://api.github.com/repos/ingen084/KyoshinMonitorProxy/releases", StatusModelContext.Default.GitHubReleaseArray))
                    ?.Where(r => !r.Draft && Version.TryParse(r.TagName.Replace("v", ""), out var v) && v > currentVersion)
                        .OrderByDescending(r => Version.TryParse(r.TagName.Replace("v", ""), out var v) ? v : new Version());
                Invoke(() =>
                {
                    if (releases?.Any() ?? false)
                    {
                        notifyIcon1.BalloonTipText = "KyoshinMonitorPrioxy の更新があります。";
                        notifyIcon1.ShowBalloonTip(3000);
                    }

                    linkLabel1.Visible = releases?.Any() ?? false;
                });
            }
            catch
            {
                Invoke(() => linkLabel1.Visible = false);
            }
        }

        protected override void OnSizeChanged(EventArgs e)
        {
            base.OnSizeChanged(e);

            if (WindowState == FormWindowState.Minimized)
            {
                Hide();
                notifyIcon1.BalloonTipText = "タスクバーに最小化しました。";
                notifyIcon1.ShowBalloonTip(3000);
            }
        }

        protected override void OnClosing(CancelEventArgs e)
        {
            if (IsServiceLiving)
            {
                e.Cancel = true;
                Hide();
                notifyIcon1.BalloonTipText = "タスクバーに最小化しました。";
                notifyIcon1.ShowBalloonTip(3000);
            }
            base.OnClosing(e);
        }

        private void openToolStripMenuItem_Click(object sender, EventArgs e)
        {
            Show();
            WindowState = FormWindowState.Normal;
        }

        private void exitToolStripMenuItem_Click(object sender, EventArgs e)
        {
            Application.Exit();
        }

        private void linkLabel1_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
        {
            Process.Start(new ProcessStartInfo("cmd", "/c start https://github.com/ingen084/KyoshinMonitorProxy/releases/latest") { CreateNoWindow = true });
        }

        private void autoRestartToolStripMenuItem_Click(object sender, EventArgs e)
        {
            autoRestartToolStripMenuItem.Checked = !autoRestartToolStripMenuItem.Checked;
        }

        private async void installButton_Click(object sender, EventArgs e)
        {
            foreach (var b in Controls.OfType<Button>())
                b.Enabled = false;
            try
            {
                var p1 = Process.Start(new ProcessStartInfo("sc.exe", $"create \"KyoshinMonitorProxy\" start=auto binpath=\"{Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "KyoshinMonitorProxy.exe")} --service\" displayname=\"強震モニタ プロキシサービス\"") { CreateNoWindow = true });
                if (p1 != null)
                    await p1.WaitForExitAsync();
                await StartServiceAsync();
            }
            finally
            {
                Invoke(() =>
                {
                    foreach (var b in Controls.OfType<Button>())
                        b.Enabled = true;
                });
            }
        }

        private async void uninstallButton_Click(object sender, EventArgs e)
        {
            foreach (var b in Controls.OfType<Button>())
                b.Enabled = false;
            try
            {
                IsServiceLiving = false;
                await StopServiceAsync();
                var p1 = Process.Start(new ProcessStartInfo("sc.exe", $"delete \"KyoshinMonitorProxy\"") { CreateNoWindow = true });
                if (p1 != null)
                    await p1.WaitForExitAsync();
                CertManager.RemoveCert(await Settings.LoadAsync());
            }
            finally
            {
                Invoke(() =>
                {
                    foreach (var b in Controls.OfType<Button>())
                        b.Enabled = true;
                });
            }
        }

        private async void startButton_Click(object sender, EventArgs e)
        {
            foreach (var b in Controls.OfType<Button>())
                b.Enabled = false;
            try
            {
                await StartServiceAsync();
            }
            finally
            {
                Invoke(() =>
                {
                    foreach (var b in Controls.OfType<Button>())
                        b.Enabled = true;
                });
            }
        }

        private async void stopButton_Click(object sender, EventArgs e)
        {
            foreach (var b in Controls.OfType<Button>())
                b.Enabled = false;
            try
            {
                IsServiceLiving = false;
                await StopServiceAsync();
            }
            finally
            {
                Invoke(() =>
                {
                    foreach (var b in Controls.OfType<Button>())
                        b.Enabled = true;
                });
            }
        }

        private void notifyIcon1_MouseDoubleClick(object sender, MouseEventArgs e)
        {
            Show();
            WindowState = FormWindowState.Normal;
        }

        private async Task StartServiceAsync()
        {
            IsServiceStopping = false;
            var p2 = Process.Start(new ProcessStartInfo("sc.exe", $"start \"KyoshinMonitorProxy\"") { CreateNoWindow = true });
            if (p2 != null)
                await p2.WaitForExitAsync();
        }
        private async Task StopServiceAsync()
        {
            IsServiceStopping = true;
            var p2 = Process.Start(new ProcessStartInfo("sc.exe", $"stop \"KyoshinMonitorProxy\"") { CreateNoWindow = true });
            if (p2 != null)
                await p2.WaitForExitAsync();
        }
    }
}
