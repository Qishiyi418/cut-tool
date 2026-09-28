using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Tesseract;

internal static class OcrWorker
{
    private static int Main(string[] args)
    {
        Console.OutputEncoding = new UTF8Encoding(false);
        if (args.Length != 1) return 2;
        try
        {
            // This executable is launched only on demand. Exiting releases the
            // model and all native allocations instead of growing the tray app.
            Environment.SetEnvironmentVariable("OMP_THREAD_LIMIT", "1");
            byte[] prepared = Prepare(args[0]);
            using (var engine = new TesseractEngine(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "tessdata"), "eng", EngineMode.LstmOnly))
            using (Pix pixels = Pix.LoadFromMemory(prepared))
            using (Page page = engine.Process(pixels, PageSegMode.Auto))
                Console.Out.Write(page.GetText().Trim());
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine("英文 OCR 失败：" + error.GetBaseException().Message);
            return 1;
        }
    }

    private static byte[] Prepare(string path)
    {
        using (var source = new Bitmap(path))
        {
            // At most four megapixels, including the margin, even for large captures.
            double scale = Math.Min(3.0, Math.Sqrt(3800000.0 / ((double)source.Width * source.Height)));
            scale = Math.Min(scale, 9900.0 / Math.Max(source.Width, source.Height));
            int width = Math.Max(1, (int)Math.Round(source.Width * scale));
            int height = Math.Max(1, (int)Math.Round(source.Height * scale));
            using (var bitmap = new Bitmap(width + 24, height + 24, PixelFormat.Format24bppRgb))
            {
                // Sample the image rather than assuming dark text on a light background.
                int dark = 0, samples = 0;
                for (int y = 0; y < source.Height; y += Math.Max(1, source.Height / 40))
                    for (int x = 0; x < source.Width; x += Math.Max(1, source.Width / 40))
                    {
                        Color color = source.GetPixel(x, y);
                        if ((color.R * 299 + color.G * 587 + color.B * 114) / 1000 < 128) dark++;
                        samples++;
                    }
                bool invert = dark > samples / 2;
                using (Graphics graphics = Graphics.FromImage(bitmap))
                {
                    graphics.Clear(invert ? Color.Black : Color.White);
                    graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    graphics.DrawImage(source, new Rectangle(12, 12, width, height));
                }
                BitmapData data = bitmap.LockBits(new Rectangle(0, 0, bitmap.Width, bitmap.Height), ImageLockMode.ReadWrite, PixelFormat.Format24bppRgb);
                try
                {
                    var row = new byte[data.Stride];
                    for (int y = 0; y < bitmap.Height; y++)
                    {
                        IntPtr address = IntPtr.Add(data.Scan0, y * data.Stride);
                        Marshal.Copy(address, row, 0, row.Length);
                        for (int x = 0; x < bitmap.Width; x++)
                        {
                            int index = x * 3;
                            byte gray = (byte)((row[index + 2] * 299 + row[index + 1] * 587 + row[index] * 114) / 1000);
                            if (invert) gray = (byte)(255 - gray);
                            row[index] = row[index + 1] = row[index + 2] = gray;
                        }
                        Marshal.Copy(row, 0, address, row.Length);
                    }
                }
                finally { bitmap.UnlockBits(data); }
                using (var stream = new MemoryStream())
                {
                    bitmap.Save(stream, System.Drawing.Imaging.ImageFormat.Png);
                    return stream.ToArray();
                }
            }
        }
    }
}
