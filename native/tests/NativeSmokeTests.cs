using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Text.RegularExpressions;

namespace CutTool.Native
{
    internal static class NativeSmokeTests
    {
        private static int failures;

        private static void Main(string[] args)
        {
            TestHotKeyParsing();
            TestWindowsOcr();
            TestSmallEnglishOcr();
            TestChineseOcr();
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
                Check(text.IndexOf("CUTTOOL", StringComparison.OrdinalIgnoreCase) >= 0, "English OCR recognizes generated text without a Windows English pack");
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

        private static void TestSmallEnglishOcr()
        {
            const string expected = "The quick brown fox jumps over the lazy dog.\nPlease select the text and press Ctrl+C to copy.\nScreenshot translation should preserve every word.";
            foreach (int size in new[] { 11, 14, 18 })
                foreach (bool dark in new[] { false, true })
                {
                    string path = Path.Combine(Path.GetTempPath(), "cuttool-ocr-test-" + Guid.NewGuid().ToString("N") + ".png");
                    string label = size + "px " + (dark ? "dark" : "light");
                    try
                    {
                        using (var bitmap = new Bitmap(700, 120))
                        using (Graphics graphics = Graphics.FromImage(bitmap))
                        using (var font = new Font("Segoe UI", size, FontStyle.Regular, GraphicsUnit.Pixel))
                        using (var brush = new SolidBrush(dark ? Color.FromArgb(225, 225, 225) : Color.FromArgb(30, 30, 30)))
                        {
                            graphics.Clear(dark ? Color.FromArgb(30, 30, 30) : Color.White);
                            graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
                            graphics.DrawString(expected, font, brush, 8, 8);
                            bitmap.Save(path, ImageFormat.Png);
                        }
                        string result = OcrService.RecognizeAsync(path, "auto").Result;
                        string normalizedExpected = Regex.Replace(expected, @"\s+", " ").ToLowerInvariant();
                        string normalizedResult = Regex.Replace(result, @"\s+", " ").ToLowerInvariant();
                        double accuracy = 1.0 - (double)EditDistance(normalizedExpected, normalizedResult) / normalizedExpected.Length;
                        Check(accuracy >= 0.97, "auto English " + label + " character accuracy=" + accuracy.ToString("P1"));
                        Check(result.Split('\n').Length >= 3, "English " + label + " preserves lines");
                        Console.WriteLine("OCR " + label + ": " + normalizedResult);
                    }
                    catch (Exception error) { Check(false, label + ": " + error.GetBaseException().Message); }
                    finally { if (File.Exists(path)) File.Delete(path); }
                }
        }

        private static void TestChineseOcr()
        {
            string path = Path.Combine(Path.GetTempPath(), "cuttool-chinese-test-" + Guid.NewGuid().ToString("N") + ".png");
            try
            {
                using (var bitmap = new Bitmap(800, 100))
                using (Graphics graphics = Graphics.FromImage(bitmap))
                using (var font = new Font("Microsoft YaHei", 26, FontStyle.Regular, GraphicsUnit.Pixel))
                {
                    graphics.Clear(Color.White);
                    graphics.DrawString("截图工具支持中文识别和英文翻译", font, Brushes.Black, 12, 20);
                    bitmap.Save(path, ImageFormat.Png);
                }
                foreach (string language in new[] { "auto", "zh-CN" })
                {
                    string text = Regex.Replace(OcrService.RecognizeAsync(path, language).Result, @"\s+", "");
                    Console.WriteLine("Chinese OCR " + language + ": " + text);
                    Check(text.Contains("截图工具支持中文") && text.Contains("英文翻译"), language + " preserves Chinese OCR routing");
                }
            }
            catch (Exception error) { Check(false, "Chinese OCR: " + error.GetBaseException().Message); }
            finally { if (File.Exists(path)) File.Delete(path); }
        }

        private static int EditDistance(string first, string second)
        {
            var row = new int[second.Length + 1];
            for (int j = 0; j <= second.Length; j++) row[j] = j;
            for (int i = 1; i <= first.Length; i++)
            {
                int previous = row[0];
                row[0] = i;
                for (int j = 1; j <= second.Length; j++)
                {
                    int old = row[j];
                    row[j] = Math.Min(Math.Min(row[j] + 1, row[j - 1] + 1), previous + (first[i - 1] == second[j - 1] ? 0 : 1));
                    previous = old;
                }
            }
            return row[second.Length];
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
