using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Windows.Forms;

namespace CutTool.Native
{
    internal enum CaptureAction
    {
        None,
        Copy,
        Ocr,
        Translate,
        Save,
        SaveAs
    }

    internal sealed class CaptureOutcome : IDisposable
    {
        internal CaptureAction Action { get; set; }
        internal Bitmap Image { get; set; }

        public void Dispose()
        {
            if (Image != null)
            {
                Image.Dispose();
                Image = null;
            }
        }
    }

    internal sealed class CaptureOverlay : Form
    {
        private enum DragMode { Idle, Creating, Moving, Resizing }

        private const int HandleSize = 8;
        private readonly DesktopSnapshot snapshot;
        private readonly Panel toolbar;
        private readonly ContextMenuStrip moreMenu;
        private Rectangle selection;
        private Rectangle selectionAtDragStart;
        private Point dragStart;
        private DragMode dragMode;
        private string resizeDirection;
        private bool automaticSelection;

        internal CaptureOutcome Outcome { get; private set; }

        internal CaptureOverlay(DesktopSnapshot desktop, Rectangle foregroundBounds)
        {
            snapshot = desktop;
            Outcome = new CaptureOutcome { Action = CaptureAction.None };

            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            Bounds = desktop.Bounds;
            TopMost = true;
            ShowInTaskbar = false;
            KeyPreview = true;
            Cursor = Cursors.Cross;
            BackColor = Color.Black;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);

            Rectangle clipped = Rectangle.Intersect(foregroundBounds, desktop.Bounds);
            if (clipped.Width >= 8 && clipped.Height >= 8)
            {
                selection = new Rectangle(
                    clipped.X - desktop.Bounds.X,
                    clipped.Y - desktop.Bounds.Y,
                    clipped.Width,
                    clipped.Height);
                automaticSelection = true;
            }

            toolbar = BuildToolbar();
            Controls.Add(toolbar);
            moreMenu = BuildMoreMenu();
            UpdateToolbar();
        }

        protected override bool ShowWithoutActivation { get { return false; } }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            Activate();
            Focus();
        }

        private Panel BuildToolbar()
        {
            var panel = new FlowLayoutPanel
            {
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                Padding = new Padding(5),
                BackColor = Color.FromArgb(28, 32, 36),
                Visible = !selection.IsEmpty
            };
            panel.Controls.Add(CreateButton("复制", delegate { Finish(CaptureAction.Copy); }));
            panel.Controls.Add(CreateButton("识别文字", delegate { Finish(CaptureAction.Ocr); }));
            panel.Controls.Add(CreateButton("翻译", delegate { Finish(CaptureAction.Translate); }));
            panel.Controls.Add(CreateButton("保存", delegate { Finish(CaptureAction.Save); }));
            Button more = CreateButton("更多", delegate
            {
                moreMenu.Show(panel, new Point(panel.Width - moreMenu.Width, panel.Height));
            });
            panel.Controls.Add(more);
            return panel;
        }

        private Button CreateButton(string text, EventHandler click)
        {
            var button = new Button
            {
                AutoSize = true,
                Height = 30,
                Margin = new Padding(2),
                Padding = new Padding(8, 0, 8, 0),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(42, 48, 54),
                ForeColor = Color.White,
                Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Regular, GraphicsUnit.Point)
            };
            button.FlatAppearance.BorderColor = Color.FromArgb(74, 82, 90);
            button.Text = text;
            button.Click += click;
            return button;
        }

        private ContextMenuStrip BuildMoreMenu()
        {
            var menu = new ContextMenuStrip();
            menu.Items.Add("另存为", null, delegate { Finish(CaptureAction.SaveAs); });
            menu.Items.Add("重新框选", null, delegate
            {
                selection = Rectangle.Empty;
                automaticSelection = false;
                toolbar.Visible = false;
                Invalidate();
            });
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("取消", null, delegate { Close(); });
            return menu;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.DrawImageUnscaled(snapshot.Bitmap, Point.Empty);
            using (var mask = new SolidBrush(Color.FromArgb(145, 0, 0, 0)))
                e.Graphics.FillRectangle(mask, ClientRectangle);

            if (!HasSelection) return;

            e.Graphics.DrawImage(snapshot.Bitmap, selection, selection, GraphicsUnit.Pixel);
            using (var border = new Pen(Color.FromArgb(64, 222, 178), 2F))
                e.Graphics.DrawRectangle(border, selection.X, selection.Y, selection.Width, selection.Height);
            DrawHandles(e.Graphics);
            DrawSizeLabel(e.Graphics);
        }

        private void DrawHandles(Graphics graphics)
        {
            using (var brush = new SolidBrush(Color.White))
            using (var pen = new Pen(Color.FromArgb(20, 120, 96)))
            {
                foreach (Rectangle handle in HandleRectangles())
                {
                    graphics.FillRectangle(brush, handle);
                    graphics.DrawRectangle(pen, handle);
                }
            }
        }

        private void DrawSizeLabel(Graphics graphics)
        {
            string text = selection.Width + " x " + selection.Height;
            using (var font = new Font("Segoe UI", 9F))
            {
                SizeF size = graphics.MeasureString(text, font);
                int y = selection.Y > 28 ? selection.Y - 25 : selection.Y + 6;
                var box = new Rectangle(selection.X, y, (int)Math.Ceiling(size.Width) + 12, 21);
                using (var background = new SolidBrush(Color.FromArgb(220, 20, 24, 28)))
                    graphics.FillRectangle(background, box);
                using (var foreground = new SolidBrush(Color.White))
                    graphics.DrawString(text, font, foreground, box.X + 6, box.Y + 2);
            }
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left) return;

            dragStart = e.Location;
            selectionAtDragStart = selection;
            resizeDirection = HitHandle(e.Location);
            toolbar.Visible = false;

            if (resizeDirection != null && HasSelection && !automaticSelection)
            {
                dragMode = DragMode.Resizing;
            }
            else if (HasSelection && selection.Contains(e.Location) && !automaticSelection)
            {
                dragMode = DragMode.Moving;
            }
            else
            {
                automaticSelection = false;
                selection = new Rectangle(e.X, e.Y, 0, 0);
                dragMode = DragMode.Creating;
            }
            Capture = true;
            Invalidate();
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (dragMode == DragMode.Idle)
            {
                string handle = HitHandle(e.Location);
                Cursor = CursorForHandle(handle, HasSelection && selection.Contains(e.Location));
                return;
            }

            int dx = e.X - dragStart.X;
            int dy = e.Y - dragStart.Y;
            if (dragMode == DragMode.Creating)
            {
                selection = Normalize(dragStart, e.Location);
            }
            else if (dragMode == DragMode.Moving)
            {
                selection = Clamp(new Rectangle(
                    selectionAtDragStart.X + dx,
                    selectionAtDragStart.Y + dy,
                    selectionAtDragStart.Width,
                    selectionAtDragStart.Height));
            }
            else if (dragMode == DragMode.Resizing)
            {
                selection = ResizeSelection(selectionAtDragStart, resizeDirection, dx, dy);
            }
            Invalidate();
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (e.Button != MouseButtons.Left || dragMode == DragMode.Idle) return;
            Capture = false;
            dragMode = DragMode.Idle;
            if (selection.Width < 4 || selection.Height < 4) selection = Rectangle.Empty;
            UpdateToolbar();
            Invalidate();
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.KeyCode == Keys.Escape)
            {
                Close();
                return;
            }
            if (e.KeyCode == Keys.Enter && HasSelection)
            {
                Finish(CaptureAction.Copy);
                return;
            }
            if (!HasSelection) return;

            int step = e.Shift ? 10 : 1;
            int dx = 0;
            int dy = 0;
            if (e.KeyCode == Keys.Left) dx = -step;
            else if (e.KeyCode == Keys.Right) dx = step;
            else if (e.KeyCode == Keys.Up) dy = -step;
            else if (e.KeyCode == Keys.Down) dy = step;
            else return;
            e.Handled = true;
            automaticSelection = false;
            selection = Clamp(new Rectangle(selection.X + dx, selection.Y + dy, selection.Width, selection.Height));
            UpdateToolbar();
            Invalidate();
        }

        private void Finish(CaptureAction action)
        {
            if (!HasSelection) return;
            Outcome.Action = action;
            Outcome.Image = snapshot.Bitmap.Clone(selection, PixelFormat.Format32bppArgb);
            Close();
        }

        private bool HasSelection
        {
            get { return selection.Width > 0 && selection.Height > 0; }
        }

        private void UpdateToolbar()
        {
            toolbar.Visible = HasSelection;
            if (!HasSelection) return;
            toolbar.PerformLayout();
            int x = selection.Right - toolbar.Width;
            if (x < 8) x = 8;
            if (x + toolbar.Width > ClientSize.Width - 8) x = ClientSize.Width - toolbar.Width - 8;
            int y = selection.Bottom + 10;
            if (y + toolbar.Height > ClientSize.Height - 8) y = selection.Y - toolbar.Height - 10;
            if (y < 8) y = Math.Max(8, selection.Bottom - toolbar.Height - 10);
            toolbar.Location = new Point(x, y);
            toolbar.BringToFront();
        }

        private Rectangle Normalize(Point first, Point second)
        {
            int x = Math.Min(first.X, second.X);
            int y = Math.Min(first.Y, second.Y);
            return Clamp(new Rectangle(x, y, Math.Abs(second.X - first.X), Math.Abs(second.Y - first.Y)));
        }

        private Rectangle Clamp(Rectangle rectangle)
        {
            int width = Math.Min(Math.Max(0, rectangle.Width), ClientSize.Width);
            int height = Math.Min(Math.Max(0, rectangle.Height), ClientSize.Height);
            int x = Math.Max(0, Math.Min(rectangle.X, ClientSize.Width - width));
            int y = Math.Max(0, Math.Min(rectangle.Y, ClientSize.Height - height));
            return new Rectangle(x, y, width, height);
        }

        private Rectangle ResizeSelection(Rectangle original, string direction, int dx, int dy)
        {
            int left = original.Left;
            int top = original.Top;
            int right = original.Right;
            int bottom = original.Bottom;
            if (direction.IndexOf('w') >= 0) left += dx;
            if (direction.IndexOf('e') >= 0) right += dx;
            if (direction.IndexOf('n') >= 0) top += dy;
            if (direction.IndexOf('s') >= 0) bottom += dy;
            if (right < left) { int swap = left; left = right; right = swap; }
            if (bottom < top) { int swap = top; top = bottom; bottom = swap; }
            return Clamp(Rectangle.FromLTRB(left, top, right, bottom));
        }

        private Rectangle[] HandleRectangles()
        {
            int half = HandleSize / 2;
            int centerX = selection.Left + selection.Width / 2;
            int centerY = selection.Top + selection.Height / 2;
            return new[]
            {
                new Rectangle(selection.Left - half, selection.Top - half, HandleSize, HandleSize),
                new Rectangle(centerX - half, selection.Top - half, HandleSize, HandleSize),
                new Rectangle(selection.Right - half, selection.Top - half, HandleSize, HandleSize),
                new Rectangle(selection.Right - half, centerY - half, HandleSize, HandleSize),
                new Rectangle(selection.Right - half, selection.Bottom - half, HandleSize, HandleSize),
                new Rectangle(centerX - half, selection.Bottom - half, HandleSize, HandleSize),
                new Rectangle(selection.Left - half, selection.Bottom - half, HandleSize, HandleSize),
                new Rectangle(selection.Left - half, centerY - half, HandleSize, HandleSize)
            };
        }

        private string HitHandle(Point point)
        {
            if (!HasSelection) return null;
            string[] directions = { "nw", "n", "ne", "e", "se", "s", "sw", "w" };
            Rectangle[] rectangles = HandleRectangles();
            for (int index = 0; index < rectangles.Length; index++)
                if (rectangles[index].Contains(point)) return directions[index];
            return null;
        }

        private Cursor CursorForHandle(string direction, bool inside)
        {
            if (direction == "n" || direction == "s") return Cursors.SizeNS;
            if (direction == "e" || direction == "w") return Cursors.SizeWE;
            if (direction == "nw" || direction == "se") return Cursors.SizeNWSE;
            if (direction == "ne" || direction == "sw") return Cursors.SizeNESW;
            return inside && !automaticSelection ? Cursors.SizeAll : Cursors.Cross;
        }
    }
}
