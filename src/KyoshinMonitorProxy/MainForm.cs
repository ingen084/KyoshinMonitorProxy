using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace KyoshinMonitorProxy
{
    public partial class MainForm : Form
    {
        public MainForm()
        {
            InitializeComponent();
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            autoRestartToolStripMenuItem.Checked = true;
        }

        private bool IsServiceLiving { get; set; } = false;
        private HttpClient Client { get; } = new() { Timeout = TimeSpan.FromSeconds(.5) };
        private async void timer1_Tick(object sender, EventArgs e)
        {
            try
            {
                var response = await Client.GetFromJsonAsync("http://127.0.0.100/kmp-status.json", StatusModelContext.Default.StatusModel);
                IsServiceLiving = true;
            }
            catch
            {
                Invoke(() =>
                {
                    IsServiceLiving = false;
                    nowstateLabel.Text = "現在の状態: 起動していないもしくは古いバージョンです";
                });
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
            Process.Start(new ProcessStartInfo("cmd", "/c start https://github.com/ingen084/KyoshinMonitorProxy/releases/latest"));
        }

        private void autoRestartToolStripMenuItem_Click(object sender, EventArgs e)
        {
            autoRestartToolStripMenuItem.Checked = !autoRestartToolStripMenuItem.Checked;
        }
    }
}
