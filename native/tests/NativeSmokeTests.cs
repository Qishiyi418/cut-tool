using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;

namespace CutTool.Native
{
    internal static class NativeSmokeTests
    {
        private static int failures;

        private static void Main(string[] args)
        {
            TestHotKeyParsing();
            TestWindowsOcr();
            if (Array.Exists(args, delegate(string value) { return value == "--network"; }))
                TestTranslation();

            if (failures > 0)
            {
                Console.Error.WriteLine(failures + " native smoke test(s) failed.");
                Environment.Exit(1);
            }
            Console.WriteLine("Native smoke tests passed.");
        }

        private static void TestHotKeyParsing()
        {
            uint modifiers;
            uint key;
            Check(HotKeyParser.TryParse("Ctrl+Shift+A", out modifiers, out key), "Ctrl+Shift+A parses");
            Check(modifiers == (NativeMethods.MOD_CONTROL | NativeMethods.MOD_SHIFT), "hotkey modifiers match");
            Check(key == (uint)System.Windows.Forms.Keys.A, "hotkey key matches");
            Check(!HotKeyParser.TryParse("A", out modifiers, out key), "unmodified letter is rejected");
            Check(HotKeyParser.TryParse("F12", out modifiers, out key), "standalone function key parses");
        }

        private static void TestWindowsOcr()
        {
            string imagePath = Path.Combine(Path.GetTempPath(), "cuttool-native-ocr-test.png");
            try
            {
                using (var bitmap = new Bitmap(900, 180))
                using (Graphics graphics = Graphics.FromImage(bitmap))
                using (var font = new Font("Arial", 42F, FontStyle.Bold))
                {
                    graphics.Clear(Color.White);
                    graphics.DrawString("CUTTOOL MEMORY TEST 2026", font, Brushes.Black, 20F, 45F);
                    bitmap.Save(imagePath, ImageFormat.Png);
                }
                string text = OcrService.RecognizeAsync(imagePath, "en").Result;
                Check(text.IndexOf("CUTTOOL", StringComparison.OrdinalIgnoreCase) >= 0, "Windows OCR recognizes generated text");
            }
            catch (Exception error)
            {
                Check(false, "Windows OCR: " + error.GetBaseException().Message);
            }
            finally
            {
                try { File.Delete(imagePath); } catch { }
            }
        }

        private static void TestTranslation()
        {
            try
            {
                TranslationResult result = TranslationService.TranslateAsync("memory", "en", "zh-CN").Result;
                Check(result != null && !string.IsNullOrWhiteSpace(result.Text), "translation provider returns text");
                Console.WriteLine("Translation provider: " + result.Provider + ", result: " + result.Text);
            }
            catch (Exception error)
            {
                Check(false, "translation: " + error.GetBaseException().Message);
            }
        }

        private static void Check(bool condition, string name)
        {
            if (condition) Console.WriteLine("PASS " + name);
            else
            {
                failures++;
                Console.Error.WriteLine("FAIL " + name);
            }
        }
    }
}
