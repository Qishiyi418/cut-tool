using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace CutTool.Native
{
    internal sealed class ResultForm : Form
    {
        private readonly TextMode mode;
        private readonly Bitmap image;
        private readonly AppSettings settings;
        private readonly Label status;
        private readonly Label sourceLabel;
        private readonly TextBox sourceBox;
        private readonly TextBox resultBox;
        private readonly Button retryButton;
        private readonly Button pinButton;
        private bool processing;

        internal ResultForm(TextMode textMode, Bitmap retryImage, AppSettings appSettings)
        {
            mode = textMode;
            image = retryImage;
            settings = appSettings;
            Text = mode == TextMode.Translate ? "翻译" : "文字识别";
            ClientSize = new Size(560, mode == TextMode.Translate ? 390 : 300);
            MinimumSize = new Size(440, 260);
            StartPosition = FormStartPosition.CenterScreen;
            TopMost = true;
            Font = new Font("Microsoft YaHei UI", 9F);

            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(12),
                ColumnCount = 1,
                RowCount = mode == TextMode.Translate ? 6 : 4
            };
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            if (mode == TextMode.Translate)
            {
                root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                root.RowStyles.Add(new RowStyle(SizeType.Percent, 35F));
            }
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 65F));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            Controls.Add(root);

            status = new Label
            {
                Text = "等待处理...",
                AutoSize = true,
                ForeColor = Color.FromArgb(90, 98, 106),
                Padding = new Padding(0, 0, 0, 8)
            };
            root.Controls.Add(status);

            sourceLabel = new Label { Text = "原文", AutoSize = true, Visible = mode == TextMode.Translate };
            sourceBox = CreateTextBox(true);
            sourceBox.Visible = mode == TextMode.Translate;
            if (mode == TextMode.Translate)
            {
                root.Controls.Add(sourceLabel);
                root.Controls.Add(sourceBox);
            }

            root.Controls.Add(new Label { Text = mode == TextMode.Translate ? "译文" : "识别结果", AutoSize = true });
            resultBox = CreateTextBox(false);
            root.Controls.Add(resultBox);

            var footer = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                AutoSize = true,
                FlowDirection = FlowDirection.RightToLeft,
                WrapContents = false,
                Padding = new Padding(0, 8, 0, 0)
            };
            var copyButton = new Button { Text = mode == TextMode.Translate ? "复制译文" : "复制原文", AutoSize = true, Height = 32 };
            copyButton.Click += delegate { CopyResult(); };
            retryButton = new Button { Text = "重试", AutoSize = true, Height = 32 };
            retryButton.Click += delegate { BeginProcessing(); };
            pinButton = new Button { Text = "取消置顶", AutoSize = true, Height = 32 };
            pinButton.Click += delegate
            {
                TopMost = !TopMost;
                pinButton.Text = TopMost ? "取消置顶" : "置顶";
            };
            footer.Controls.Add(copyButton);
            footer.Controls.Add(retryButton);
            footer.Controls.Add(pinButton);
            root.Controls.Add(footer);
            FormClosed += delegate { image.Dispose(); };
        }

        private TextBox CreateTextBox(bool readOnly)
        {
            return new TextBox
            {
                Multiline = true,
                ReadOnly = readOnly,
                Dock = DockStyle.Fill,
                ScrollBars = ScrollBars.Vertical,
                BackColor = readOnly ? Color.FromArgb(247, 248, 249) : SystemColors.Window
            };
        }

        internal async void BeginProcessing()
        {
            if (processing || IsDisposed) return;
            processing = true;
            retryButton.Enabled = false;
            resultBox.ReadOnly = true;
            resultBox.Text = string.Empty;
            sourceBox.Text = string.Empty;
            status.ForeColor = Color.FromArgb(90, 98, 106);
            status.Text = "正在识别文字...";

            string temporary = Path.Combine(Path.GetTempPath(), "cuttool-ocr-" + Guid.NewGuid().ToString("N") + ".png");
            try
            {
                image.Save(temporary, ImageFormat.Png);
                string source = await OcrService.RecognizeAsync(temporary, settings.TranslateFrom);
                if (IsDisposed) return;
                if (string.IsNullOrWhiteSpace(source)) throw new InvalidOperationException("未识别到文字，请确认截图区域包含清晰文本。");

                if (mode == TextMode.Ocr)
                {
                    resultBox.Text = source;
                    try
                    {
                        Clipboard.SetText(source);
                        status.Text = "识别完成，已复制原文";
                    }
                    catch { status.Text = "识别完成"; }
                }
                else
                {
                    sourceBox.Text = source;
                    status.Text = "正在翻译...";
                    TranslationResult translated = await TranslationService.TranslateAsync(source, settings.TranslateFrom, settings.TranslateTo);
                    if (IsDisposed) return;
                    resultBox.Text = translated.Text;
                    status.Text = "翻译完成 · " + translated.Provider;
                }
            }
            catch (Exception error)
            {
                if (!IsDisposed)
                {
                    status.ForeColor = Color.FromArgb(190, 48, 48);
                    status.Text = error.Message;
                }
            }
            finally
            {
                try { if (File.Exists(temporary)) File.Delete(temporary); } catch { }
                if (!IsDisposed)
                {
                    processing = false;
                    retryButton.Enabled = true;
                    resultBox.ReadOnly = false;
                }
            }
        }

        private void CopyResult()
        {
            if (string.IsNullOrEmpty(resultBox.Text)) return;
            try
            {
                Clipboard.SetText(resultBox.Text);
                status.ForeColor = Color.FromArgb(42, 132, 98);
                status.Text = mode == TextMode.Translate ? "已复制译文" : "已复制原文";
            }
            catch (Exception error)
            {
                status.ForeColor = Color.FromArgb(190, 48, 48);
                status.Text = "复制失败：" + error.Message;
            }
        }
    }
}
