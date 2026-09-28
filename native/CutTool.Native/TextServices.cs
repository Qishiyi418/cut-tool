using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Web;
using System.Web.Script.Serialization;

namespace CutTool.Native
{
    internal enum TextMode { Ocr, Translate }

    internal static class OcrService
    {
        private static readonly SemaphoreSlim Gate = new SemaphoreSlim(1, 1);

        internal static Task<string> RecognizeAsync(string imagePath, string sourceLanguage)
        {
            return Task.Run(delegate
            {
                Gate.Wait();
                try
                {
                    if (sourceLanguage == "zh-CN") return RecognizeWindows(imagePath, "zh-CN");
                    if (sourceLanguage == "en" || sourceLanguage == "en-US") return RecognizeEnglish(imagePath);

                    // Windows' profile language is not language detection. On a
                    // Chinese-only system it badly misreads small English text.
                    string local = string.Empty;
                    try { local = RecognizeWindows(imagePath, string.Empty); }
                    catch { /* English OCR remains available without Windows language packs. */ }
                    int han = Regex.Matches(local, @"[\u3400-\u9fff]").Count;
                    int letters = Regex.Matches(local, @"[A-Za-z\u3400-\u9fff]").Count;
                    if (han >= 2 && han >= letters * 0.2) return local;
                    return RecognizeEnglish(imagePath);
                }
                finally { Gate.Release(); }
            });
        }

        private static string RecognizeEnglish(string imagePath)
        {
            string executable = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "tools", "ocr", "CutTool.Ocr.exe");
            if (!File.Exists(executable)) throw new FileNotFoundException("缺少英文 OCR 组件，请重新安装完整版本。", executable);
            return RunProcess(executable, Quote(imagePath));
        }

        private static string RecognizeWindows(string imagePath, string language)
        {
            string script = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "tools", "windows-ocr.ps1");
            if (!File.Exists(script)) throw new FileNotFoundException("找不到 Windows OCR 脚本。", script);

            string powershell = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.System),
                "WindowsPowerShell",
                "v1.0",
                "powershell.exe");
            return RunProcess(powershell,
                "-NoProfile -NonInteractive -ExecutionPolicy Bypass -File " + Quote(script) +
                " -ImagePath " + Quote(imagePath) + (string.IsNullOrEmpty(language) ? string.Empty : " -Language " + Quote(language)));
        }

        private static string RunProcess(string executable, string arguments)
        {
            var start = new ProcessStartInfo
            {
                FileName = executable,
                Arguments = arguments,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };

            using (Process process = Process.Start(start))
            {
                Task<string> output = process.StandardOutput.ReadToEndAsync();
                Task<string> error = process.StandardError.ReadToEndAsync();
                if (!process.WaitForExit(30000))
                {
                    try { process.Kill(); } catch { }
                    throw new TimeoutException("OCR 识别超时。");
                }
                if (process.ExitCode != 0)
                    throw new InvalidOperationException(string.IsNullOrWhiteSpace(error.Result) ? "OCR 执行失败。" : error.Result.Trim());
                return output.Result.Trim();
            }
        }

        private static string Quote(string value)
        {
            return "\"" + (value ?? string.Empty).Replace("\"", "\\\"") + "\"";
        }
    }

    internal sealed class TranslationResult
    {
        internal string Text { get; set; }
        internal string Provider { get; set; }
    }

    internal static class TranslationService
    {
        internal static async Task<TranslationResult> TranslateAsync(string text, string from, string to)
        {
            var errors = new List<string>();
            try { return await TranslateWithBing(text, from, to); }
            catch (Exception error) { errors.Add("Bing: " + error.Message); }
            try { return await TranslateWithMyMemory(text, from, to); }
            catch (Exception error) { errors.Add("MyMemory: " + error.Message); }
            try { return await TranslateWithGoogle(text, from, to); }
            catch (Exception error) { errors.Add("Google: " + error.Message); }
            throw new InvalidOperationException("所有翻译服务均不可用。" + string.Join("；", errors.ToArray()));
        }

        private static HttpClient CreateClient(int timeoutSeconds)
        {
            var handler = new HttpClientHandler
            {
                AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
                AllowAutoRedirect = true,
                UseCookies = true
            };
            var client = new HttpClient(handler);
            client.Timeout = TimeSpan.FromSeconds(timeoutSeconds);
            client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) CutTool/2.0");
            return client;
        }

        private static async Task<TranslationResult> TranslateWithBing(string text, string from, string to)
        {
            using (HttpClient client = CreateClient(8))
            {
                string page = await client.GetStringAsync("https://cn.bing.com/translator");
                Match ig = Regex.Match(page, "IG:\"([^\"]+)\"");
                Match helper = Regex.Match(page, "params_AbusePreventionHelper\\s*=\\s*\\[([^\\]]+)\\]");
                if (!ig.Success || !helper.Success) throw new InvalidOperationException("参数解析失败");
                string[] parts = helper.Groups[1].Value.Split(',');
                if (parts.Length < 2) throw new InvalidOperationException("令牌解析失败");
                string key = parts[0].Trim().Trim('\"');
                string token = parts[1].Trim().Trim('\"');
                string source = from == "auto" ? "auto-detect" : NormalizeBingLanguage(from);
                string target = NormalizeBingLanguage(to);
                var form = new FormUrlEncodedContent(new[]
                {
                    new KeyValuePair<string, string>("fromLang", source),
                    new KeyValuePair<string, string>("to", target),
                    new KeyValuePair<string, string>("text", text),
                    new KeyValuePair<string, string>("token", token),
                    new KeyValuePair<string, string>("key", key)
                });
                string address = "https://cn.bing.com/ttranslatev3?isVertical=1&IG=" + Uri.EscapeDataString(ig.Groups[1].Value) + "&IID=translator.5028";
                HttpResponseMessage response = await client.PostAsync(address, form);
                response.EnsureSuccessStatusCode();
                string json = await response.Content.ReadAsStringAsync();
                object root = new JavaScriptSerializer().DeserializeObject(json);
                object[] array = root as object[];
                if (array == null || array.Length == 0) throw new InvalidOperationException("返回格式异常");
                var first = array[0] as Dictionary<string, object>;
                object translationsValue;
                if (first == null || !first.TryGetValue("translations", out translationsValue)) throw new InvalidOperationException("没有翻译结果");
                object[] translations = translationsValue as object[];
                var translated = translations != null && translations.Length > 0 ? translations[0] as Dictionary<string, object> : null;
                object value;
                if (translated == null || !translated.TryGetValue("text", out value)) throw new InvalidOperationException("没有翻译结果");
                return new TranslationResult { Text = Convert.ToString(value), Provider = "Bing" };
            }
        }

        private static async Task<TranslationResult> TranslateWithMyMemory(string text, string from, string to)
        {
            string source = from == "auto" ? "en" : from;
            // Do not silently lose the rest of a paragraph at the provider's byte limit.
            if (Encoding.UTF8.GetByteCount(text) > 500) throw new InvalidOperationException("文本超过 MyMemory 限制，尝试下一服务");
            string query = text;
            string address = "https://api.mymemory.translated.net/get?q=" + Uri.EscapeDataString(query) +
                             "&langpair=" + Uri.EscapeDataString(source + "|" + to);
            using (HttpClient client = CreateClient(7))
            {
                string json = await client.GetStringAsync(address);
                var root = new JavaScriptSerializer().DeserializeObject(json) as Dictionary<string, object>;
                object responseStatus;
                if (root == null || !root.TryGetValue("responseStatus", out responseStatus) || Convert.ToInt32(responseStatus) != 200)
                    throw new InvalidOperationException("服务拒绝请求或额度已用尽");
                object dataValue;
                var data = root != null && root.TryGetValue("responseData", out dataValue) ? dataValue as Dictionary<string, object> : null;
                object textValue;
                if (data == null || !data.TryGetValue("translatedText", out textValue)) throw new InvalidOperationException("没有翻译结果");
                return new TranslationResult { Text = HttpUtility.HtmlDecode(Convert.ToString(textValue)), Provider = "MyMemory" };
            }
        }

        private static async Task<TranslationResult> TranslateWithGoogle(string text, string from, string to)
        {
            string address = "https://translate.googleapis.com/translate_a/single?client=gtx&sl=" +
                             Uri.EscapeDataString(from) + "&tl=" + Uri.EscapeDataString(to) +
                             "&dt=t&q=" + Uri.EscapeDataString(text);
            using (HttpClient client = CreateClient(5))
            {
                string json = await client.GetStringAsync(address);
                object[] root = new JavaScriptSerializer().DeserializeObject(json) as object[];
                object[] segments = root != null && root.Length > 0 ? root[0] as object[] : null;
                if (segments == null) throw new InvalidOperationException("返回格式异常");
                var builder = new StringBuilder();
                foreach (object segmentValue in segments)
                {
                    object[] segment = segmentValue as object[];
                    if (segment != null && segment.Length > 0) builder.Append(Convert.ToString(segment[0]));
                }
                if (builder.Length == 0) throw new InvalidOperationException("没有翻译结果");
                return new TranslationResult { Text = builder.ToString(), Provider = "Google" };
            }
        }

        private static string NormalizeBingLanguage(string language)
        {
            if (language == "zh-CN") return "zh-Hans";
            if (language == "zh-TW") return "zh-Hant";
            return language;
        }
    }
}
