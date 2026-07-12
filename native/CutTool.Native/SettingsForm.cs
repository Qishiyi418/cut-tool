using System;
using System.Drawing;
using System.Windows.Forms;

namespace CutTool.Native
{
    internal sealed class SettingsForm : Form
    {
        private readonly TextBox hotkeyBox;
        private readonly TextBox directoryBox;
        private readonly ComboBox formatBox;
        private readonly CheckBox autoCopyBox;
        private readonly CheckBox translateWindowBox;
        private readonly ComboBox languageBox;
        private readonly CheckBox autoLaunchBox;
        private readonly CheckBox trayBox;

        internal AppSettings ResultSettings { get; private set; }

        internal SettingsForm(AppSettings current)
        {
            Text = "CutTool 设置";
            ClientSize = new Size(500, 500);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            Font = new Font("Microsoft YaHei UI", 9F);
            BackColor = Color.FromArgb(247, 248, 249);

            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(18, 14, 18, 14),
                ColumnCount = 1,
                RowCount = 4
            };
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            Controls.Add(root);

            var capture = CreateSection("截图");
            hotkeyBox = new TextBox { ReadOnly = true, Text = current.Hotkey, Width = 190 };
            hotkeyBox.Enter += delegate { hotkeyBox.Text = "按下组合键..."; };
            hotkeyBox.Leave += delegate
            {
                if (hotkeyBox.Text == "按下组合键...") hotkeyBox.Text = current.Hotkey;
            };
            hotkeyBox.KeyDown += RecordHotKey;
            AddRow(capture, "截图快捷键", hotkeyBox);

            directoryBox = new TextBox { ReadOnly = true, Text = current.SaveDirectory, Width = 150 };
            var directoryPanel = new FlowLayoutPanel { AutoSize = true, WrapContents = false, FlowDirection = FlowDirection.LeftToRight };
            directoryPanel.Controls.Add(directoryBox);
            var browse = new Button { Text = "浏览", AutoSize = true };
            browse.Click += BrowseDirectory;
            directoryPanel.Controls.Add(browse);
            AddRow(capture, "默认保存目录", directoryPanel);

            formatBox = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 110 };
            formatBox.Items.AddRange(new object[] { "PNG", "JPG" });
            formatBox.SelectedIndex = string.Equals(current.ImageFormat, "jpg", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
            AddRow(capture, "截图格式", formatBox);
            root.Controls.Add(capture);

            var text = CreateSection("文字与翻译");
            autoCopyBox = new CheckBox { Checked = current.AutoCopy, AutoSize = true };
            AddRow(text, "截图后自动复制", autoCopyBox);
            translateWindowBox = new CheckBox { Checked = current.ShowTranslateWindow, AutoSize = true };
            AddRow(text, "启用文字结果窗口", translateWindowBox);
            languageBox = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 150 };
            languageBox.Items.AddRange(new object[] { "英语 -> 中文", "自动 -> 中文", "中文 -> 英语" });
            string pair = current.TranslateFrom + "|" + current.TranslateTo;
            languageBox.SelectedIndex = pair == "auto|zh-CN" ? 1 : pair == "zh-CN|en" ? 2 : 0;
            AddRow(text, "翻译方向", languageBox);
            root.Controls.Add(text);

            var system = CreateSection("系统");
            autoLaunchBox = new CheckBox { Checked = current.AutoLaunch, AutoSize = true };
            AddRow(system, "开机自动启动", autoLaunchBox);
            trayBox = new CheckBox { Checked = current.ShowTray, AutoSize = true };
            AddRow(system, "显示系统托盘图标", trayBox);
            root.Controls.Add(system);

            var footer = new FlowLayoutPanel
            {
                Dock = DockStyle.Bottom,
                AutoSize = true,
                FlowDirection = FlowDirection.RightToLeft,
                WrapContents = false,
                Padding = new Padding(0, 12, 0, 0)
            };
            var save = new Button { Text = "保存", Width = 84, Height = 32 };
            save.Click += SaveSettings;
            var cancel = new Button { Text = "取消", Width = 84, Height = 32, DialogResult = DialogResult.Cancel };
            footer.Controls.Add(save);
            footer.Controls.Add(cancel);
            root.Controls.Add(footer);
            AcceptButton = save;
            CancelButton = cancel;
        }

        private TableLayoutPanel CreateSection(string title)
        {
            var section = new TableLayoutPanel
            {
                AutoSize = true,
                Dock = DockStyle.Top,
                ColumnCount = 2,
                Padding = new Padding(0, 0, 0, 10),
                Margin = new Padding(0)
            };
            section.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            section.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            var heading = new Label
            {
                Text = title,
                AutoSize = true,
                Dock = DockStyle.Fill,
                Font = new Font(Font, FontStyle.Bold),
                ForeColor = Color.FromArgb(75, 82, 90),
                Padding = new Padding(0, 4, 0, 6)
            };
            section.Controls.Add(heading, 0, 0);
            section.SetColumnSpan(heading, 2);
            return section;
        }

        private void AddRow(TableLayoutPanel section, string label, Control control)
        {
            int row = section.RowCount++;
            section.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            var caption = new Label
            {
                Text = label,
                AutoSize = true,
                Anchor = AnchorStyles.Left,
                Padding = new Padding(0, 8, 12, 8)
            };
            control.Anchor = AnchorStyles.Right;
            control.Margin = new Padding(3, 4, 0, 4);
            section.Controls.Add(caption, 0, row);
            section.Controls.Add(control, 1, row);
        }

        private void RecordHotKey(object sender, KeyEventArgs e)
        {
            e.SuppressKeyPress = true;
            e.Handled = true;
            if (e.KeyCode == Keys.ControlKey || e.KeyCode == Keys.ShiftKey || e.KeyCode == Keys.Menu || e.KeyCode == Keys.LWin || e.KeyCode == Keys.RWin)
                return;

            string keyName = null;
            if (e.KeyCode >= Keys.A && e.KeyCode <= Keys.Z) keyName = e.KeyCode.ToString();
            else if (e.KeyCode >= Keys.D0 && e.KeyCode <= Keys.D9) keyName = ((int)e.KeyCode - (int)Keys.D0).ToString();
            else if (e.KeyCode >= Keys.F1 && e.KeyCode <= Keys.F24) keyName = e.KeyCode.ToString();
            else if (e.KeyCode == Keys.Space) keyName = "Space";
            else if (e.KeyCode == Keys.Left || e.KeyCode == Keys.Right || e.KeyCode == Keys.Up || e.KeyCode == Keys.Down) keyName = e.KeyCode.ToString();
            else if (e.KeyCode == Keys.PrintScreen) keyName = "PrintScreen";
            if (keyName == null) return;

            string value = string.Empty;
            if (e.Control) value += "Ctrl+";
            if (e.Alt) value += "Alt+";
            if (e.Shift) value += "Shift+";
            bool windowsKey = (NativeMethods.GetAsyncKeyState((int)Keys.LWin) & 0x8000) != 0 ||
                              (NativeMethods.GetAsyncKeyState((int)Keys.RWin) & 0x8000) != 0;
            if (windowsKey) value += "Super+";
            if (value.Length == 0 && e.KeyCode < Keys.F1) return;
            hotkeyBox.Text = value + keyName;
            SelectNextControl(hotkeyBox, true, true, true, true);
        }

        private void BrowseDirectory(object sender, EventArgs e)
        {
            using (var dialog = new FolderBrowserDialog())
            {
                dialog.Description = "选择默认截图保存目录";
                dialog.SelectedPath = directoryBox.Text;
                if (dialog.ShowDialog(this) == DialogResult.OK) directoryBox.Text = dialog.SelectedPath;
            }
        }

        private void SaveSettings(object sender, EventArgs e)
        {
            uint modifiers;
            uint key;
            if (!HotKeyParser.TryParse(hotkeyBox.Text, out modifiers, out key))
            {
                MessageBox.Show(this, "请输入有效的快捷键组合。", "CutTool", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                hotkeyBox.Focus();
                return;
            }

            string from = languageBox.SelectedIndex == 1 ? "auto" : languageBox.SelectedIndex == 2 ? "zh-CN" : "en";
            string to = languageBox.SelectedIndex == 2 ? "en" : "zh-CN";
            ResultSettings = new AppSettings
            {
                Hotkey = hotkeyBox.Text,
                SaveDirectory = directoryBox.Text,
                ImageFormat = formatBox.SelectedIndex == 1 ? "jpg" : "png",
                AutoCopy = autoCopyBox.Checked,
                ShowTranslateWindow = translateWindowBox.Checked,
                TranslateFrom = from,
                TranslateTo = to,
                AutoLaunch = autoLaunchBox.Checked,
                ShowTray = trayBox.Checked
            };
            DialogResult = DialogResult.OK;
            Close();
        }
    }
}
