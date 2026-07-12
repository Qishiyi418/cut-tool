using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Windows.Forms;

namespace CutTool.Native
{
    internal sealed class DesktopSnapshot : IDisposable
    {
        internal Rectangle Bounds { get; private set; }
        internal Bitmap Bitmap { get; private set; }

        internal DesktopSnapshot(Rectangle bounds, Bitmap bitmap)
        {
            Bounds = bounds;
            Bitmap = bitmap;
        }

        public void Dispose()
        {
            if (Bitmap != null)
            {
                Bitmap.Dispose();
                Bitmap = null;
            }
        }
    }

    internal static class ScreenCapture
    {
        internal static DesktopSnapshot CaptureDesktop()
        {
            Rectangle bounds = SystemInformation.VirtualScreen;
            if (bounds.Width < 1 || bounds.Height < 1)
                throw new InvalidOperationException("无法获取桌面范围。");

            var bitmap = new Bitmap(bounds.Width, bounds.Height, PixelFormat.Format32bppPArgb);
            try
            {
                using (Graphics graphics = Graphics.FromImage(bitmap))
                {
                    graphics.CopyFromScreen(bounds.Location, Point.Empty, bounds.Size, CopyPixelOperation.SourceCopy);
                }
                return new DesktopSnapshot(bounds, bitmap);
            }
            catch
            {
                bitmap.Dispose();
                throw;
            }
        }
    }
}
