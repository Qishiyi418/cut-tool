using System;
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
            try
            {
                using (var clone = new Bitmap(bitmap))
                    Clipboard.SetDataObject(clone, true);
                return true;
            }
            catch (Exception error)
            {
                MessageBox.Show("复制截图失败：" + error.Message, "CutTool", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }
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
