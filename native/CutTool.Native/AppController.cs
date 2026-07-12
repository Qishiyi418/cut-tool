using Microsoft.Win32;
using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Windows.Forms;

namespace CutTool.Native
{
    internal sealed class AppController : ApplicationContext
    {
        private readonly EventWaitHandle wakeEvent;
        private readonly RegisteredWaitHandle wakeRegistration;
        private readonly SettingsStore store;
        private readonly HotKeyWindow hotKeyWindow;
        private NotifyIcon trayIcon;
        private AppSettings settings;
        private bool captureActive;
        private bool disposed;

        internal AppController(EventWaitHandle wake, bool probeMode)
        {
            wakeEvent = wake;
            store = new SettingsStore();
            settings = store.Load();
            hotKeyWindow = new HotKeyWindow();
            hotKeyWindow.CaptureRequested += delegate { StartCapture(); };
            wakeRegistration = ThreadPool.RegisterWaitForSingleObject(
                wakeEvent,
                delegate { hotKeyWindow.PostCapture(); },
                null,
                Timeout.Infinite,
                false);

            if (probeMode)
            {
                bool showTray = settings.ShowTray;
                settings.ShowTray = false;
                BuildTray();
                settings.ShowTray = showTray;
            }
            else BuildTray();
            if (!probeMode) ApplyAutoLaunch();
            if (!probeMode && !hotKeyWindow.Register(settings.Hotkey))
            {
                MessageBox.Show(
                    "无法注册快捷键 " + settings.Hotkey + "，它可能已被其他程序占用。",
                    "CutTool",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
            }
            if (!probeMode && !settings.ShowTray) ShowSettings();
        }

        private void BuildTray()
        {
            if (trayIcon == null)
            {
                trayIcon = new NotifyIcon();
                trayIcon.Icon = CreateTrayIcon();
                trayIcon.Text = "CutTool";
                trayIcon.DoubleClick += delegate { StartCapture(); };
            }

            var menu = new ContextMenuStrip();
            menu.Items.Add("截图 (" + settings.Hotkey + ")", null, delegate { StartCapture(); });
            menu.Items.Add("设置", null, delegate { ShowSettings(); });
            menu.Items.Add("打开保存目录", null, delegate { OpenSaveDirectory(); });
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("退出", null, delegate { ExitThread(); });
            if (trayIcon.ContextMenuStrip != null) trayIcon.ContextMenuStrip.Dispose();
            trayIcon.ContextMenuStrip = menu;
            trayIcon.Visible = settings.ShowTray;
        }

        private Icon CreateTrayIcon()
        {
            using (var bitmap = new Bitmap(32, 32))
            using (Graphics graphics = Graphics.FromImage(bitmap))
            {
                graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                graphics.Clear(Color.Transparent);
                using (var background = new SolidBrush(Color.FromArgb(64, 222, 178)))
                    graphics.FillRoundedRectangle(background, new Rectangle(1, 1, 30, 30), 7);
                using (var pen = new Pen(Color.FromArgb(18, 35, 31), 2.5F))
                    graphics.DrawRectangle(pen, 8, 8, 16, 16);
                IntPtr handle = bitmap.GetHicon();
                try { return (Icon)Icon.FromHandle(handle).Clone(); }
                finally { NativeMethods.DestroyIcon(handle); }
            }
        }

        private void StartCapture()
        {
            if (captureActive) return;
            captureActive = true;
            try
            {
                Rectangle foreground = NativeMethods.GetForegroundWindowBounds();
                using (DesktopSnapshot desktop = ScreenCapture.CaptureDesktop())
                using (var overlay = new CaptureOverlay(desktop, foreground))
                {
                    overlay.ShowDialog();
                    using (CaptureOutcome outcome = overlay.Outcome)
                    {
                        ProcessOutcome(outcome);
                    }
                }
            }
            catch (Exception error)
            {
                MessageBox.Show("截图失败：" + error.Message, "CutTool", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                captureActive = false;
            }
        }

        private void ProcessOutcome(CaptureOutcome outcome)
        {
            if (outcome == null || outcome.Action == CaptureAction.None || outcome.Image == null) return;

            if ((settings.AutoCopy && outcome.Action != CaptureAction.Ocr) || outcome.Action == CaptureAction.Copy)
                ImageOutput.Copy(outcome.Image);

            if (outcome.Action == CaptureAction.Save || outcome.Action == CaptureAction.SaveAs)
            {
                try { ImageOutput.Save(outcome.Image, settings, outcome.Action == CaptureAction.SaveAs); }
                catch (Exception error)
                {
                    MessageBox.Show("保存截图失败：" + error.Message, "CutTool", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
            else if (outcome.Action == CaptureAction.Ocr || outcome.Action == CaptureAction.Translate)
            {
                var image = new Bitmap(outcome.Image);
                var result = new ResultForm(
                    outcome.Action == CaptureAction.Translate ? TextMode.Translate : TextMode.Ocr,
                    image,
                    settings.Clone());
                result.Show();
                result.BeginProcessing();
            }
        }

        private void ShowSettings()
        {
            using (var form = new SettingsForm(settings.Clone()))
            {
                if (form.ShowDialog() != DialogResult.OK || form.ResultSettings == null) return;
                AppSettings previous = settings;
                settings = form.ResultSettings;
                if (!hotKeyWindow.Register(settings.Hotkey))
                {
                    MessageBox.Show("快捷键注册失败，已恢复原来的快捷键。", "CutTool", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    settings.Hotkey = previous.Hotkey;
                    hotKeyWindow.Register(settings.Hotkey);
                }
                store.Save(settings);
                BuildTray();
                ApplyAutoLaunch();
            }
        }

        private void OpenSaveDirectory()
        {
            string directory = settings.SaveDirectory;
            if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
                directory = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
            Process.Start("explorer.exe", directory);
        }

        private void ApplyAutoLaunch()
        {
            try
            {
                using (RegistryKey run = Registry.CurrentUser.OpenSubKey("Software\\Microsoft\\Windows\\CurrentVersion\\Run", true))
                {
                    if (settings.AutoLaunch) run.SetValue("CutTool", "\"" + Application.ExecutablePath + "\"");
                    else run.DeleteValue("CutTool", false);
                }
            }
            catch { }
        }

        protected override void ExitThreadCore()
        {
            if (trayIcon != null) trayIcon.Visible = false;
            base.ExitThreadCore();
        }

        protected override void Dispose(bool disposing)
        {
            if (!disposed && disposing)
            {
                disposed = true;
                if (wakeRegistration != null) wakeRegistration.Unregister(null);
                if (hotKeyWindow != null) hotKeyWindow.Dispose();
                if (trayIcon != null)
                {
                    trayIcon.Visible = false;
                    if (trayIcon.ContextMenuStrip != null) trayIcon.ContextMenuStrip.Dispose();
                    trayIcon.Icon.Dispose();
                    trayIcon.Dispose();
                }
            }
            base.Dispose(disposing);
        }
    }

    internal static class GraphicsExtensions
    {
        internal static void FillRoundedRectangle(this Graphics graphics, Brush brush, Rectangle bounds, int radius)
        {
            int diameter = radius * 2;
            using (var path = new System.Drawing.Drawing2D.GraphicsPath())
            {
                path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
                path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270, 90);
                path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
                path.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90, 90);
                path.CloseFigure();
                graphics.FillPath(brush, path);
            }
        }
    }
}
