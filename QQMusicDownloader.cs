using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace AuralDesk
{
    /// <summary>
    /// QQ 音乐主动下载器。
    /// 绕过网页"下载需安装客户端"的限制：带登录 Cookie 调
    /// u.y.qq.com/cgi-bin/musicu.fcg 的 vkey.GetVkeyServer/CgiGetVkey 接口，
    /// 拿真实音频地址（purl），下载后按文件头检测格式、必要时 QMC 解密。
    /// 接口参数参考 copws/qq-music-api（2025.9 确认可用）。
    /// </summary>
    public sealed class QQMusicDownloader
    {
        private readonly HttpClient _http = new();

        /// <summary>下载结果。</summary>
        public sealed class DownloadResult
        {
            public required byte[] Data { get; init; }
            public required string Ext { get; init; }
            public required string Songmid { get; init; }
        }

        /// <summary>
        /// 按 songmid 下载歌曲。
        /// </summary>
        /// <param name="songmid">歌曲 MID。</param>
        /// <param name="quality">m4a / 128 / 320。</param>
        /// <param name="cookieHeader">y.qq.com 的登录 Cookie（分号连接）。</param>
        /// <returns>失败（无权限/网络错误）返回 null。</returns>
        public async Task<DownloadResult?> DownloadAsync(
            string songmid, string quality, string cookieHeader)
        {
            var (prefix, suffix) = quality switch
            {
                "m4a" => ("C400", "m4a"),
                "128" => ("M500", "mp3"),
                _ => ("M800", "mp3")
            };
            var uin = ExtractUin(cookieHeader);
            var filename = $"{prefix}{songmid}{songmid}.{suffix}";
            var body = $@"{{""req_1"":{{""module"":""vkey.GetVkeyServer"",""method"":""CgiGetVkey"",""param"":{{""filename"":[""{filename}""],""guid"":""10000"",""songmid"":[""{songmid}""],""songtype"":[0],""uin"":""{uin}"",""loginflag"":1,""platform"":""20""}}}},""loginUin"":""{uin}"",""comm"":{{""uin"":""{uin}"",""format"":""json"",""ct"":24,""cv"":0}}}}";

            using var req = new HttpRequestMessage(HttpMethod.Post, "https://u.y.qq.com/cgi-bin/musicu.fcg");
            if (!string.IsNullOrEmpty(cookieHeader))
                req.Headers.TryAddWithoutValidation("Cookie", cookieHeader);
            req.Headers.Referrer = new Uri("https://y.qq.com/");
            req.Headers.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0 Safari/537.36");
            req.Content = new StringContent(body, Encoding.UTF8, "application/json");

            using var resp = await _http.SendAsync(req);
            resp.EnsureSuccessStatusCode();
            var json = await resp.Content.ReadAsStringAsync();
            QQMusicDownloader.LogQqDebug($"vkey 响应({json.Length}字符): " +
                (json.Length > 600 ? json[..600] : json));

            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (!root.TryGetProperty("req_1", out var req1)
                || !req1.TryGetProperty("data", out var data))
                return null;

            var sip0 = "https://dl.stream.qqmusic.qq.com/";
            if (data.TryGetProperty("sip", out var sip) && sip.GetArrayLength() > 0)
                sip0 = sip[0].GetString() ?? sip0;

            var purl = "";
            if (data.TryGetProperty("midurlinfo", out var midurl) && midurl.GetArrayLength() > 0)
                purl = midurl[0].GetProperty("purl").GetString() ?? "";

            if (string.IsNullOrEmpty(purl))
            {
                QQMusicDownloader.LogQqDebug("vkey purl 为空（无权限或接口失败）");
                return null; // 无权限（非会员试听/付费歌曲）或接口失败
            }

            var url = sip0 + purl;
            QQMusicDownloader.LogQqDebug($"purl: {url}");
            var bytes = await DownloadFileAsync(url, cookieHeader);
            if (bytes.Length == 0)
            {
                QQMusicDownloader.LogQqDebug("音频下载为空");
                return null;
            }
            QQMusicDownloader.LogQqDebug($"音频下载 {bytes.Length} 字节，头: " +
                string.Join(" ", bytes.Take(16).Select(b => b.ToString("X2"))));

            var ext = QmcDecoder.DetectRealExt(bytes, suffix);
            if (LooksEncrypted(bytes))
            {
                var dec = QmcDecoder.Decrypt(bytes, ext);
                if (dec != null)
                {
                    bytes = dec.Value.data;
                    ext = dec.Value.ext;
                }
            }

            return new DownloadResult { Data = bytes, Ext = ext, Songmid = songmid };
        }

        internal static void LogQqDebug(string message)
        {
            AppLog.Write(message);
        }

        private async Task<byte[]> DownloadFileAsync(string url, string cookieHeader)
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            if (!string.IsNullOrEmpty(cookieHeader))
                req.Headers.TryAddWithoutValidation("Cookie", cookieHeader);
            req.Headers.Referrer = new Uri("https://y.qq.com/");
            req.Headers.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0 Safari/537.36");
            using var resp = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead);
            resp.EnsureSuccessStatusCode();
            await using var stream = await resp.Content.ReadAsStreamAsync();
            using var ms = new MemoryStream();
            await stream.CopyToAsync(ms);
            return ms.ToArray();
        }

        /// <summary>从 Cookie 里提取 uin（去掉开头的 o）。</summary>
        private static string ExtractUin(string cookieHeader)
        {
            if (string.IsNullOrEmpty(cookieHeader)) return "0";
            var m = Regex.Match(cookieHeader, @"(?:^|;\s*)uin=o?(\d+)", RegexOptions.IgnoreCase);
            return m.Success ? m.Groups[1].Value : "0";
        }

        /// <summary>
        /// 若数据是 QMC 加密则解密，否则返回 null。
        /// fallbackExt 用于无头检测时的格式兜底。
        /// </summary>
        internal static (byte[] data, string ext)? MaybeDecrypt(byte[] bytes, string fallbackExt)
        {
            var ext = QmcDecoder.DetectRealExt(bytes, fallbackExt);
            if (LooksEncrypted(bytes))
            {
                var dec = QmcDecoder.Decrypt(bytes, ext);
                // 解密产物必须是可识别的音频头，否则视为误判（如带 ID3 标签的明文 MP3），
                // 回退保留原始数据与原始扩展名，避免把 MP3 标成 .ogg 导致 HQPlayer 播放失败。
                if (dec != null && !LooksEncrypted(dec.Value.data))
                    return (dec.Value.data, dec.Value.ext);
                // 扩展名识别不出加密类型时，自动探测 mflac/mgg 掩码（解决 .ogg/.flac 伪装加密）
                var any = QmcDecoder.TryDecryptAny(bytes);
                if (any != null && !LooksEncrypted(any.Value.data))
                    return any.Value;
            }
            return null;
        }

        /// <summary>粗略判断是否为 QMC 加密数据（不是已知音频头）。</summary>
        internal static bool LooksEncrypted(byte[] data)
        {
            if (data.Length < 16) return false;
            // FLAC / MP3 / OGG / M4A 头
            if (data.Length >= 4 && data[0] == 0x66 && data[1] == 0x4c && data[2] == 0x61 && data[3] == 0x43) return false; // fLaC
            if (data.Length >= 3 && data[0] == 0x49 && data[1] == 0x44 && data[2] == 0x33) return false; // ID3 (MP3)
            if (data.Length >= 2 && data[0] == 0xFF && (data[1] & 0xE0) == 0xE0) return false; // MP3
            if (data.Length >= 4 && data[0] == 0x4F && data[1] == 0x67 && data[2] == 0x67 && data[3] == 0x53) return false; // OggS
            if (data.Length >= 12 && data[4] == 0x66 && data[5] == 0x74 && data[6] == 0x79 && data[7] == 0x70) return false; // ftyp (m4a)
            if (data.Length >= 4 && data[0] == 0x52 && data[1] == 0x49 && data[2] == 0x46 && data[3] == 0x46) return false; // RIFF
            return true;
        }
    }
}
