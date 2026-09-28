using System;
using System.Collections.Specialized;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace CutTool.Native
{
    internal static class ImageOutput
    {
        internal static bool Copy(Bitmap bitmap)
        {
            string clipboardFile = null;
            try
            {
                // Publish real image formats, not just a serialized .NET Bitmap.
                // PNG serves browser/chat editors; 24-bit DIB serves native apps
                // without the ambiguous alpha channel of a 32-bit BI_RGB bitmap.
                using (var png = new MemoryStream())
                using (var bmp = new MemoryStream())
                using (var opaque = new Bitmap(bitmap.Width, bitmap.Height, PixelFormat.Format24bppRgb))
                {
                    bitmap.Save(png, ImageFormat.Png);
                    png.Position = 0;
                    string cacheDirectory = Path.Combine(Path.GetTempPath(), "CutTool", "Clipboard");
                    Directory.CreateDirectory(cacheDirectory);
                    clipboardFile = Path.Combine(cacheDirectory,
                        "截图_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + "_" + Guid.NewGuid().ToString("N") + ".png");
                    File.WriteAllBytes(clipboardFile, png.ToArray());
                    using (Graphics graphics = Graphics.FromImage(opaque))
                    {
                        graphics.Clear(Color.White);
                        graphics.DrawImageUnscaled(bitmap, 0, 0);
                    }
                    opaque.Save(bmp, ImageFormat.Bmp);
                    byte[] bytes = bmp.ToArray();
                    // CF_DIB starts at BITMAPINFOHEADER, after the 14-byte BMP file header.
                    using (var dib = new MemoryStream(bytes, 14, bytes.Length - 14))
                    {
                        var data = new DataObject();
                        data.SetData("PNG", false, png);
                        data.SetData(DataFormats.Dib, false, dib);
                        data.SetData(DataFormats.Bitmap, true, opaque);
                        // Explorer requires CF_HDROP with a real file that outlives this call.
                        data.SetFileDropList(new StringCollection { clipboardFile });
                        // Flush before disposing image/streams, and retry transient locks.
                        Clipboard.SetDataObject(data, true, 10, 100);
                    }
                }
                CleanClipboardCache(Path.GetDirectoryName(clipboardFile));
                return true;
            }
            catch (Exception error)
            {
                if (clipboardFile != null)
                {
                    try { File.Delete(clipboardFile); } catch { }
                }
                MessageBox.Show("复制截图失败：" + error.Message, "CutTool", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }
        }

        private static void CleanClipboardCache(string directory)
        {
            // Keep recent files for delayed pastes and clipboard history. Cleanup
            // is best effort and must never turn a successful copy into a failure.
            try
            {
                DateTime cutoff = DateTime.UtcNow.AddDays(-7);
                foreach (string path in Directory.GetFiles(directory, "截图_*.png"))
                {
                    try
                    {
                        if (File.GetLastWriteTimeUtc(path) < cutoff) File.Delete(path);
                    }
                    catch { }
                }
            }
            catch { }
        }

        internal static string Save(Bitmap bitmap, AppSettings settings, bool choosePath)
        {
            string extension = string.Equals(settings.ImageFormat, "jpg", StringComparison.OrdinalIgnoreCase) ? "jpg" : "png";
            string directory = settings.SaveDirectory;
            if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
                directory = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);

            string fileName = "截图_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + "." + extension;
            string path = Path.Combine(directory, fileName);
            if (choosePath)
            {
                using (var dialog = new SaveFileDialog())
                {
                    dialog.Title = "保存截图";
                    dialog.FileName = fileName;
                    dialog.InitialDirectory = directory;
                    dialog.Filter = "PNG 图片|*.png|JPG 图片|*.jpg;*.jpeg";
                    dialog.FilterIndex = extension == "jpg" ? 2 : 1;
                    if (dialog.ShowDialog() != DialogResult.OK) return null;
                    path = dialog.FileName;
                }
            }

            string targetDirectory = Path.GetDirectoryName(path);
            if (!Directory.Exists(targetDirectory)) Directory.CreateDirectory(targetDirectory);
            if (Path.GetExtension(path).Equals(".jpg", StringComparison.OrdinalIgnoreCase) ||
                Path.GetExtension(path).Equals(".jpeg", StringComparison.OrdinalIgnoreCase))
            {
                ImageCodecInfo codec = ImageCodecInfo.GetImageEncoders().First(item => item.FormatID == ImageFormat.Jpeg.Guid);
                using (var parameters = new EncoderParameters(1))
                {
                    parameters.Param[0] = new EncoderParameter(Encoder.Quality, 92L);
                    bitmap.Save(path, codec, parameters);
                }
            }
            else
            {
                bitmap.Save(path, ImageFormat.Png);
            }
            return path;
        }
    }
}
