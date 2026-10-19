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
        private delegate Task<string> TranslateMethod(HttpClient client, string text, string from, string to);

        private sealed class Provider
        {
            internal string Name;
            internal int ByteLimit;
            internal bool Direct;
            internal TranslateMethod Translate;
            internal DateTime CoolUntil;
        }

        private sealed class ProviderException : Exception
        {
            internal ProviderException(string message, bool throttled) : base(message) { Throttled = throttled; }
            internal bool Throttled { get; private set; }
        }

        private sealed class BingSession
        {
            internal string Ig;
            internal string Iid;
            internal string Key;
            internal string Token;
            internal DateTime Expires;
        }

        private const string UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/130.0.0.0 Safari/537.36";
        private static readonly Uri BingHome = new Uri("https://cn.bing.com/translator");
        private static readonly object Sync = new object();
        // Bing's page token belongs to the session cookies that came with it.
        private static readonly CookieContainer Cookies = new CookieContainer();
        private static readonly Provider[] Providers;
        private static Provider preferred;
        private static BingSession bingSession;

        static TranslationService()
        {
            // csc does not stamp a TargetFramework attribute, so the runtime uses
            // .NET 4.0 defaults (SSL 3 / TLS 1.0). Bing and MyMemory refuse those
            // handshakes, which used to leave the rate-limited Google endpoint as
            // the only working service.
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            Providers = new[]
            {
                // cn.bing.com is reachable from mainland China with or without the proxy.
                new Provider { Name = "Bing", ByteLimit = 1500, Translate = TranslateWithBing },
                new Provider { Name = "Google", ByteLimit = 1800, Translate = TranslateWithGoogle },
                // A different Google front end with a separate rate limit.
                new Provider { Name = "Google 备用", ByteLimit = 4000, Translate = TranslateWithGoogleDictionary },
                new Provider { Name = "MyMemory", ByteLimit = 480, Translate = TranslateWithMyMemory },
                // Covers a system proxy that is still configured after the proxy app exits.
                new Provider { Name = "Bing 直连", ByteLimit = 1500, Direct = true, Translate = TranslateWithBing }
            };
        }

        internal static async Task<TranslationResult> TranslateAsync(string text, string from, string to)
        {
            text = (text ?? string.Empty).Replace("\r\n", "\n").Trim();
            if (text.Length == 0) throw new InvalidOperationException("没有可翻译的文字。");

            var errors = new List<string>();
            foreach (Provider provider in GetAttemptOrder())
            {
                try
                {
                    string translated = await TranslateInPieces(provider, text, from, to);
                    lock (Sync)
                    {
                        provider.CoolUntil = DateTime.MinValue;
                        preferred = provider;
                    }
                    return new TranslationResult { Text = translated, Provider = provider.Name };
                }
                catch (Exception error)
                {
                    var failure = error as ProviderException;
                    lock (Sync)
                    {
                        // Skip a failing service for a while so a rate limit or a
                        // broken route does not delay every later translation.
                        provider.CoolUntil = DateTime.UtcNow.AddMinutes(failure != null && failure.Throttled ? 10 : 1);
                        if (preferred == provider) preferred = null;
                    }
                    errors.Add(provider.Name + ": " + GetErrorDetail(error));
                }
            }
            throw new InvalidOperationException("所有翻译服务均不可用。请检查网络和代理连接。" + Environment.NewLine + string.Join(Environment.NewLine, errors.ToArray()));
        }

        // Tries every service once without changing the fallback order.
        internal static async Task<List<string>> ProbeAsync(string text, string from, string to)
        {
            var report = new List<string>();
            foreach (Provider provider in Providers)
            {
                if (provider.Direct && !SystemProxyApplies()) continue;
                try { report.Add("OK   " + provider.Name + ": " + await TranslateInPieces(provider, text, from, to)); }
                catch (Exception error) { report.Add("FAIL " + provider.Name + ": " + GetErrorDetail(error)); }
            }
            return report;
        }

        private static List<Provider> GetAttemptOrder()
        {
            bool proxied = SystemProxyApplies();
            lock (Sync)
            {
                DateTime now = DateTime.UtcNow;
                var ready = new List<Provider>();
                var cooling = new List<Provider>();
                if (preferred != null && preferred.CoolUntil <= now) ready.Add(preferred);
                foreach (Provider provider in Providers)
                {
                    if (ready.Contains(provider) || (provider.Direct && !proxied)) continue;
                    (provider.CoolUntil <= now ? ready : cooling).Add(provider);
                }
                // Services that failed recently remain a last resort.
                ready.AddRange(cooling);
                return ready;
            }
        }

        private static bool SystemProxyApplies()
        {
            try
            {
                IWebProxy proxy = WebRequest.DefaultWebProxy;
                Uri route = proxy == null ? null : proxy.GetProxy(BingHome);
                return route != null && route != BingHome;
            }
            catch { return false; }
        }

        private static async Task<string> TranslateInPieces(Provider provider, string text, string from, string to)
        {
            using (HttpClient client = CreateClient(provider.Direct))
            {
                var builder = new StringBuilder();
                foreach (string piece in SplitText(text, provider.ByteLimit))
                {
                    // Services trim whitespace; keep the line breaks between pieces.
                    string core = piece.Trim();
                    if (core.Length == 0)
                    {
                        builder.Append(piece);
                        continue;
                    }
                    int lead = piece.Length - piece.TrimStart().Length;
                    builder.Append(piece, 0, lead);
                    builder.Append(await provider.Translate(client, core, from, to));
                    builder.Append(piece, lead + core.Length, piece.Length - lead - core.Length);
                }
                return builder.ToString().Trim();
            }
        }

        // Splits text into pieces of at most byteLimit UTF-8 bytes, preferring
        // line breaks, then sentence ends, then spaces. The pieces concatenate
        // back to the original text.
        internal static List<string> SplitText(string text, int byteLimit)
        {
            var pieces = new List<string>();
            int start = 0;
            while (start < text.Length)
            {
                int end = start;
                int bytes = 0;
                while (end < text.Length)
                {
                    char current = text[end];
                    int size = current < 0x80 ? 1 : current < 0x800 || char.IsSurrogate(current) ? 2 : 3;
                    if (bytes + size > byteLimit) break;
                    bytes += size;
                    end++;
                }
                if (end == start) end++;
                if (end < text.Length) end = FindBreak(text, start, end);
                pieces.Add(text.Substring(start, end - start));
                start = end;
            }
            return pieces;
        }

        private static int FindBreak(string text, int start, int end)
        {
            int cut = text.LastIndexOf('\n', end - 1, end - start);
            if (cut <= start) cut = text.LastIndexOfAny(new[] { '.', '!', '?', ';', '。', '！', '？', '；' }, end - 1, end - start);
            if (cut <= start) cut = text.LastIndexOf(' ', end - 1, end - start);
            if (cut > start) return cut + 1;
            if (end - start > 1 && char.IsHighSurrogate(text[end - 1])) return end - 1;
            return end;
        }

        private static string GetErrorDetail(Exception error)
        {
            if (error is ProviderException) return error.Message;
            if (error is TaskCanceledException) return "连接超时";
            // HttpClient often reports only "Error sending request" on the
            // surface; the inner exception identifies TLS or proxy failures.
            Exception detail = error;
            while (detail.InnerException != null) detail = detail.InnerException;
            return detail.Message;
        }

        private static HttpClient CreateClient(bool direct)
        {
            var handler = new HttpClientHandler
            {
                AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
                AllowAutoRedirect = true,
                CookieContainer = Cookies,
                UseProxy = !direct
            };
            var client = new HttpClient(handler);
            client.Timeout = TimeSpan.FromSeconds(12);
            client.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgent);
            return client;
        }

        private static Task<string> GetAsync(HttpClient client, string address)
        {
            return SendAsync(client, new HttpRequestMessage(HttpMethod.Get, address));
        }

        private static Task<string> PostAsync(HttpClient client, string address, HttpContent content)
        {
            return SendAsync(client, new HttpRequestMessage(HttpMethod.Post, address) { Content = content });
        }

        private static async Task<string> SendAsync(HttpClient client, HttpRequestMessage request)
        {
            using (request)
            using (HttpResponseMessage response = await client.SendAsync(request))
            {
                int status = (int)response.StatusCode;
                if (status == 429) throw new ProviderException("请求过于频繁 (429)，10 分钟内优先使用其他服务", true);
                if (!response.IsSuccessStatusCode) throw new ProviderException("HTTP " + status + " " + response.ReasonPhrase, false);
                return await response.Content.ReadAsStringAsync();
            }
        }

        private static async Task<string> TranslateWithBing(HttpClient client, string text, string from, string to)
        {
            for (int attempt = 0; ; attempt++)
            {
                BingSession session = await GetBingSession(client, attempt > 0);
                var form = new FormUrlEncodedContent(new[]
                {
                    new KeyValuePair<string, string>("fromLang", from == "auto" ? "auto-detect" : NormalizeBingLanguage(from)),
                    new KeyValuePair<string, string>("to", NormalizeBingLanguage(to)),
                    new KeyValuePair<string, string>("text", text),
                    new KeyValuePair<string, string>("token", session.Token),
                    new KeyValuePair<string, string>("key", session.Key)
                });
                string address = "https://cn.bing.com/ttranslatev3?isVertical=1&IG=" + Uri.EscapeDataString(session.Ig) +
                                 "&IID=" + Uri.EscapeDataString(session.Iid);
                string json = string.Empty;
                object root = null;
                try
                {
                    json = await PostAsync(client, address, form);
                    root = new JavaScriptSerializer().DeserializeObject(json);
                }
                catch (ProviderException failure)
                {
                    if (failure.Throttled || attempt > 0) throw;
                }
                catch (ArgumentException) { /* An HTML error page; refresh the token below. */ }
                object[] array = root as object[];
                if (array != null && array.Length > 0)
                {
                    var first = array[0] as Dictionary<string, object>;
                    object translationsValue;
                    if (first == null || !first.TryGetValue("translations", out translationsValue)) throw new ProviderException("没有翻译结果", false);
                    object[] translations = translationsValue as object[];
                    var translated = translations != null && translations.Length > 0 ? translations[0] as Dictionary<string, object> : null;
                    object value;
                    if (translated == null || !translated.TryGetValue("text", out value)) throw new ProviderException("没有翻译结果", false);
                    return Convert.ToString(value);
                }

                // An expired token or a captcha challenge arrives as an error
                // status or an object instead of a result array.
                lock (Sync) bingSession = null;
                var challenge = root as Dictionary<string, object>;
                if (challenge != null && challenge.ContainsKey("ShowCaptcha")) throw new ProviderException("触发了 Bing 验证码，暂时改用其他服务", true);
                if (attempt > 0) throw new ProviderException("返回格式异常：" + (json.Length > 120 ? json.Substring(0, 120) : json), false);
            }
        }

        private static async Task<BingSession> GetBingSession(HttpClient client, bool refresh)
        {
            lock (Sync)
            {
                if (!refresh && bingSession != null && bingSession.Expires > DateTime.UtcNow) return bingSession;
            }

            // The translator page is about 600 KB, so reuse its token until it expires.
            string page = await GetAsync(client, BingHome.AbsoluteUri);
            Match ig = Regex.Match(page, "IG:\"([^\"]+)\"");
            Match helper = Regex.Match(page, @"params_AbusePreventionHelper\s*=\s*\[([^\]]+)\]");
            if (!ig.Success || !helper.Success) throw new ProviderException("参数解析失败", false);
            string[] parts = helper.Groups[1].Value.Split(',');
            if (parts.Length < 2) throw new ProviderException("令牌解析失败", false);
            Match iid = Regex.Match(page, "data-iid=\"(translator\\.[0-9]+)\"");
            long lifetime;
            if (parts.Length < 3 || !long.TryParse(parts[2].Trim(), out lifetime) || lifetime <= 0) lifetime = 3600000;
            var session = new BingSession
            {
                Ig = ig.Groups[1].Value,
                Iid = iid.Success ? iid.Groups[1].Value : "translator.5028",
                Key = parts[0].Trim().Trim('\"'),
                Token = parts[1].Trim().Trim('\"'),
                Expires = DateTime.UtcNow.AddMilliseconds(Math.Min(lifetime, 3600000) * 0.8)
            };
            lock (Sync) bingSession = session;
            return session;
        }

        private static async Task<string> TranslateWithGoogle(HttpClient client, string text, string from, string to)
        {
            string address = "https://translate.googleapis.com/translate_a/single?client=gtx&sl=" +
                             Uri.EscapeDataString(from) + "&tl=" + Uri.EscapeDataString(to) +
                             "&dt=t&q=" + Uri.EscapeDataString(text);
            object[] root = new JavaScriptSerializer().DeserializeObject(await GetAsync(client, address)) as object[];
            object[] segments = root != null && root.Length > 0 ? root[0] as object[] : null;
            if (segments == null) throw new ProviderException("返回格式异常", false);
            var builder = new StringBuilder();
            foreach (object segmentValue in segments)
            {
                object[] segment = segmentValue as object[];
                if (segment != null && segment.Length > 0) builder.Append(Convert.ToString(segment[0]));
            }
            if (builder.Length == 0) throw new ProviderException("没有翻译结果", false);
            return builder.ToString();
        }

        private static async Task<string> TranslateWithGoogleDictionary(HttpClient client, string text, string from, string to)
        {
            string address = "https://clients5.google.com/translate_a/t?client=dict-chrome-ex&sl=" +
                             Uri.EscapeDataString(from) + "&tl=" + Uri.EscapeDataString(to);
            var form = new FormUrlEncodedContent(new[] { new KeyValuePair<string, string>("q", text) });
            object[] root = new JavaScriptSerializer().DeserializeObject(await PostAsync(client, address, form)) as object[];
            // ["text"] for a fixed source language, [["text","en"]] for auto.
            object first = root != null && root.Length > 0 ? root[0] : null;
            object[] pair = first as object[];
            string result = Convert.ToString(pair != null && pair.Length > 0 ? pair[0] : first);
            if (string.IsNullOrWhiteSpace(result)) throw new ProviderException("没有翻译结果", false);
            return result;
        }

        private static async Task<string> TranslateWithMyMemory(HttpClient client, string text, string from, string to)
        {
            // MyMemory has no automatic source language.
            string source = from != "auto" ? from : Regex.Matches(text, @"[㐀-鿿]").Count * 5 >= Regex.Matches(text, @"[A-Za-z㐀-鿿]").Count ? "zh-CN" : "en";
            if (source == to) throw new ProviderException("原文已是目标语言", false);
            string address = "https://api.mymemory.translated.net/get?q=" + Uri.EscapeDataString(text) +
                             "&langpair=" + Uri.EscapeDataString(source + "|" + to);
            var root = new JavaScriptSerializer().DeserializeObject(await GetAsync(client, address)) as Dictionary<string, object>;
            object quota;
            if (root != null && root.TryGetValue("quotaFinished", out quota) && true.Equals(quota))
                throw new ProviderException("今日免费额度已用尽", true);
            object responseStatus;
            if (root == null || !root.TryGetValue("responseStatus", out responseStatus) || Convert.ToString(responseStatus) != "200")
                throw new ProviderException("服务拒绝请求", false);
            object dataValue;
            var data = root.TryGetValue("responseData", out dataValue) ? dataValue as Dictionary<string, object> : null;
            object textValue;
            if (data == null || !data.TryGetValue("translatedText", out textValue)) throw new ProviderException("没有翻译结果", false);
            return HttpUtility.HtmlDecode(Convert.ToString(textValue));
        }

        private static string NormalizeBingLanguage(string language)
        {
            if (language == "zh-CN") return "zh-Hans";
            if (language == "zh-TW") return "zh-Hant";
            return language;
        }
    }

    internal static class TextLayout
    {
        // Windows edit controls only break lines at CRLF; OCR and the
        // translation services return bare LF.
        internal static string ForDisplay(string text)
        {
            return Regex.Replace(text ?? string.Empty, "\r\n|\r|\n", "\r\n");
        }

        // OCR keeps the visual wraps of the captured paragraph, and services
        // translate each wrapped fragment on its own. Rejoin a line with the
        // next when it fills the text width and the sentence clearly continues.
        internal static string JoinWrappedLines(string text)
        {
            string[] lines = Regex.Split((text ?? string.Empty).Trim(), "\r\n|\r|\n");
            int width = 0;
            foreach (string line in lines) width = Math.Max(width, line.Trim().Length);

            var builder = new StringBuilder();
            string previous = null;
            foreach (string raw in lines)
            {
                string line = raw.Trim();
                if (previous != null)
                {
                    string joint = Joint(previous, line, width);
                    builder.Append(joint ?? "\n");
                }
                builder.Append(line);
                previous = line;
            }
            return builder.ToString();
        }

        private static string Joint(string previous, string next, int width)
        {
            if (previous.Length == 0 || next.Length == 0 || previous.Length * 10 < width * 7) return null;
            char last = previous[previous.Length - 1];
            char first = next[0];
            if (IsHan(first) && previous.Length >= 10 && (IsHan(last) || last == '，' || last == '、')) return string.Empty;
            if (previous.Length < 20 || !char.IsLower(first)) return null;
            if (char.IsLetterOrDigit(last) || last == ',') return " ";
            if (last == '-' && previous.Length > 1 && char.IsLetter(previous[previous.Length - 2])) return string.Empty;
            return null;
        }

        private static bool IsHan(char value)
        {
            return value >= '㐀' && value <= '鿿';
        }
    }
}
