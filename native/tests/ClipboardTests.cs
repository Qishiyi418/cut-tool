using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

internal static class ClipboardTests
{
    [DllImport("user32.dll")] private static extern bool OpenClipboard(IntPtr owner);
    [DllImport("user32.dll")] private static extern bool CloseClipboard();
    [DllImport("user32.dll")] private static extern IntPtr GetClipboardData(uint format);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern uint RegisterClipboardFormat(string name);
    [DllImport("kernel32.dll")] private static extern IntPtr GlobalLock(IntPtr handle);
    [DllImport("kernel32.dll")] private static extern bool GlobalUnlock(IntPtr handle);
    [DllImport("kernel32.dll")] private static extern UIntPtr GlobalSize(IntPtr handle);
    private static int failures;

    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length == 2 && args[0] == "--write")
        {
            Assembly app = Assembly.LoadFrom(args[1]);
            MethodInfo copy = app.GetType("CutTool.Native.ImageOutput").GetMethod("Copy", BindingFlags.Static | BindingFlags.NonPublic);
            using (var bitmap = new Bitmap(127, 83, PixelFormat.Format32bppArgb))
            {
                for (int y = 0; y < bitmap.Height; y++)
                    for (int x = 0; x < bitmap.Width; x++) bitmap.SetPixel(x, y, ExpectedPixel(x, y));
                return (bool)copy.Invoke(null, new object[] { bitmap }) ? 0 : 1;
            }
        }
        if (args.Length != 1) throw new ArgumentException("Expected application executable path.");
        try
        {
            // A separate STA process writes, disposes its bitmap, and exits.
            // The reader then checks real native formats, not .NET auto-conversion.
            for (int iteration = 0; iteration < 2; iteration++)
            {
                using (Process writer = Process.Start(new ProcessStartInfo
                {
                    FileName = Assembly.GetExecutingAssembly().Location,
                    Arguments = "--write \"" + Path.GetFullPath(args[0]) + "\"",
                    UseShellExecute = false,
                    CreateNoWindow = true
                }))
                {
                    if (!writer.WaitForExit(15000))
                    {
                        writer.Kill();
                        throw new Exception("Clipboard writer timed out.");
                    }
                    Check(writer.ExitCode == 0, "copy succeeds in separate STA process");
                }
                using (Image image = Clipboard.GetImage()) CheckImage(image, "Bitmap survives producer exit");

                byte[] png = ReadNativeBytes(RegisterClipboardFormat("PNG"));
                Check(png != null && png.Length > 8 && png[0] == 137 && png[1] == 80 && png[2] == 78 && png[3] == 71,
                    "native PNG has PNG signature without .NET serialization prefix");
                if (png != null)
                    using (var stream = new MemoryStream(png))
                    using (Image image = Image.FromStream(stream)) CheckImage(image, "native PNG pixels match");

                byte[] dib = ReadNativeBytes(8); // CF_DIB
                Check(dib != null && dib.Length >= 40, "native CF_DIB is available");
                if (dib != null && dib.Length >= 40)
                {
                    Check(BitConverter.ToInt32(dib, 0) == 40 && BitConverter.ToInt32(dib, 4) == 127 &&
                        BitConverter.ToInt32(dib, 8) == 83 && BitConverter.ToInt16(dib, 14) == 24,
                        "DIB header has correct dimensions and 24-bit RGB");
                    using (var stream = new MemoryStream())
                    {
                        var writer = new BinaryWriter(stream);
                        writer.Write((ushort)0x4D42);
                        writer.Write(dib.Length + 14);
                        writer.Write(0);
                        writer.Write(54);
                        writer.Write(dib);
                        writer.Flush();
                        stream.Position = 0;
                        using (Image image = Image.FromStream(stream)) CheckImage(image, "DIB row stride and orientation match");
                    }
                }

                var files = Clipboard.GetFileDropList();
                Check(files.Count == 1 && File.Exists(files[0]), "Explorer file drop points to a persistent PNG");
                if (files.Count == 1 && File.Exists(files[0]))
                {
                    string destination = Path.Combine(Path.GetTempPath(), "cuttool-paste-test-" + Guid.NewGuid().ToString("N") + ".png");
                    try
                    {
                        File.Copy(files[0], destination);
                        using (Image image = Image.FromFile(destination)) CheckImage(image, "file can be pasted/copied after producer exit");
                    }
                    finally { if (File.Exists(destination)) File.Delete(destination); }
                }
            }
        }
        catch (Exception error) { Check(false, error.ToString()); }
        Console.WriteLine(failures == 0 ? "Clipboard tests passed." : failures + " clipboard test(s) failed.");
        return failures == 0 ? 0 : 1;
    }

    private static Color ExpectedPixel(int x, int y)
    {
        return Color.FromArgb(255, x * 2, y * 3, (x + y) % 256);
    }

    private static void CheckImage(Image image, string name)
    {
        bool matches = image != null && image.Width == 127 && image.Height == 83;
        if (matches)
        {
            using (var bitmap = new Bitmap(image))
                for (int y = 0; y < bitmap.Height && matches; y++)
                    for (int x = 0; x < bitmap.Width; x++)
                        if ((bitmap.GetPixel(x, y).ToArgb() & 0xFFFFFF) != (ExpectedPixel(x, y).ToArgb() & 0xFFFFFF))
                        { matches = false; break; }
        }
        Check(matches, name);
    }

    private static byte[] ReadNativeBytes(uint format)
    {
        bool opened = false;
        for (int attempt = 0; attempt < 20 && !opened; attempt++)
        {
            opened = OpenClipboard(IntPtr.Zero);
            if (!opened) Thread.Sleep(50);
        }
        if (!opened) throw new Exception("Cannot open clipboard for verification.");
        try
        {
            IntPtr handle = GetClipboardData(format);
            if (handle == IntPtr.Zero) return null;
            int length = checked((int)GlobalSize(handle).ToUInt64());
            IntPtr pointer = GlobalLock(handle);
            if (pointer == IntPtr.Zero) throw new Exception("Cannot lock clipboard data.");
            try
            {
                var bytes = new byte[length];
                Marshal.Copy(pointer, bytes, 0, length);
                return bytes;
            }
            finally { GlobalUnlock(handle); }
        }
        finally { CloseClipboard(); }
    }

    private static void Check(bool condition, string name)
    {
        Console.WriteLine((condition ? "PASS " : "FAIL ") + name);
        if (!condition) failures++;
    }
}
