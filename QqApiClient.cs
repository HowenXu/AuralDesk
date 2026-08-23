using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Runtime.InteropServices;
using System.Text;
using System.ComponentModel;
using System.Text.Json;
using System.Threading.Tasks;

namespace AuralDesk
{
    /// <summary>QQ 专辑信息（搜索结果 / 收藏专辑 / 歌手专辑共用）。</summary>
    public sealed class QqAlbumInfo : INotifyPropertyChanged
    {
        public required string AlbumMid { get; init; }
        public long AlbumId { get; init; }
        public required string Name { get; init; }
        public string Singer { get; init; } = "";
        public string Date { get; init; } = "";
        public string CoverUrl { get; init; } = "";
        public string AlbumType { get; init; } = "";

        private bool _isFav;

        /// <summary>是否已收藏（红心）。</summary>
        public bool IsFav
        {
            get => _isFav;
            set
            {
                if (_isFav != value)
                {
                    _isFav = value;
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsFav)));
                }
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        /// <summary>是否 Live/演唱会专辑（正经发行的大专辑优先展示）。</summary>
        public bool IsLive =>
            AlbumType.Contains("演唱") || AlbumType.Contains("现场") || AlbumType.Contains("实况") ||
            AlbumType.Contains("live", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>QQ 歌手信息（搜索 / 歌手页共用）。</summary>
    public sealed class QqSingerInfo
    {
        public required string SingerMid { get; init; }
        public required string Name { get; init; }
        public string Pic { get; init; } = "";
        public string Sub { get; init; } = "";
        public int SongNum { get; init; }
        public int AlbumNum { get; init; }
    }

    /// <summary>
    /// 基于 DPAPI（当前 Windows 用户）的本地数据加密/解密，无需额外依赖。
    /// </summary>
    internal static class CredentialProtector
    {
        [StructLayout(LayoutKind.Sequential)]
        private struct DataBlob
        {
            public int cbData;
            public IntPtr pbData;
        }

        [DllImport("crypt32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool CryptProtectData(
            ref DataBlob pDataIn, string? szDataDescr, IntPtr pOptionalEntropy,
            IntPtr pvReserved, IntPtr pPromptStruct, uint dwFlags, out DataBlob pDataOut);

        [DllImport("crypt32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool CryptUnprotectData(
            ref DataBlob pDataIn, IntPtr ppszDataDescr, IntPtr pOptionalEntropy,
            IntPtr pvReserved, IntPtr pPromptStruct, uint dwFlags, out DataBlob pDataOut);

        [DllImport("kernel32.dll")]
        private static extern IntPtr LocalFree(IntPtr hMem);

        private const uint CRYPTPROTECT_UI_FORBIDDEN = 0x1;

        public static byte[] Protect(byte[] data)
        {
            var input = new DataBlob { cbData = data.Length, pbData = Marshal.AllocHGlobal(data.Length) };
            try
            {
                Marshal.Copy(data, 0, input.pbData, data.Length);
                if (!CryptProtectData(ref input, null, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero,
                        CRYPTPROTECT_UI_FORBIDDEN, out var output))
                    throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
                try
                {
                    var result = new byte[output.cbData];
                    if (output.cbData > 0)
                        Marshal.Copy(output.pbData, result, 0, output.cbData);
                    return result;
                }
                finally
                {
                    if (output.pbData != IntPtr.Zero)
                        LocalFree(output.pbData);
                }
            }
            finally
            {
                if (input.pbData != IntPtr.Zero)
                    LocalFree(input.pbData);
            }
        }

        public static byte[]? Unprotect(byte[] data)
        {
            if (data == null || data.Length == 0)
                return null;
            var input = new DataBlob { cbData = data.Length, pbData = Marshal.AllocHGlobal(data.Length) };
            try
            {
                Marshal.Copy(data, 0, input.pbData, data.Length);
                if (!CryptUnprotectData(ref input, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero,
                        CRYPTPROTECT_UI_FORBIDDEN, out var output))
                    return null;
                try
                {
                    var result = new byte[output.cbData];
                    if (output.cbData > 0)
                        Marshal.Copy(output.pbData, result, 0, output.cbData);
                    return result;
                }
                finally
                {
                    if (output.pbData != IntPtr.Zero)
                        LocalFree(output.pbData);
                }
            }
            finally
            {
                if (input.pbData != IntPtr.Zero)
                    LocalFree(input.pbData);
            }
        }
    }

    /// <summary>QQ 音乐接口调用失败。</summary>
    public sealed class QqApiException : Exception
    {
        public QqApiException(string message) : base(message) { }
    }

    /// <summary>QQ 音乐歌曲条目（用于列表展示与播放）。</summary>
    public sealed class QqSongItem : INotifyPropertyChanged
    {
        public required string Mid { get; init; }
        public long Id { get; init; }
        public string Title { get; init; } = "";
        public string Singer { get; init; } = "";
        public string Album { get; init; } = "";
        public string AlbumMid { get; init; } = "";
        public string Duration { get; init; } = "";
        public int SongType { get; init; }
        public string MediaMid { get; init; } = "";

        /// <summary>专辑封面 URL（150x150，列表缩略图用）。</summary>
        public string CoverUrl => string.IsNullOrEmpty(AlbumMid)
            ? ""
            : $"https://y.gtimg.cn/music/photo_new/T002R150x150M000{AlbumMid}.jpg";

        private bool _isFav;

        /// <summary>是否已收藏（红心）。</summary>
        public bool IsFav
        {
            get => _isFav;
            set
            {
                if (_isFav != value)
                {
                    _isFav = value;
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsFav)));
                }
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
    }

    /// <summary>QQ 音乐歌单条目。</summary>
    public sealed class QqPlaylistItem
    {
        public required long Id { get; init; }
        public required string Name { get; init; }
        public string Info { get; init; } = "";
    }

    /// <summary>音源直链结果。</summary>
    public sealed class SongUrlResult
    {
        public required string Purl { get; init; }
        public required string Filename { get; init; }
    }

    /// <summary>
    /// QQ 音乐 API 客户端，对接本地 sidecar 的 HTTP 服务。
    /// 登录凭证以 Cookie 透传，401 时自动刷新一次。
    /// </summary>
    public sealed class QqApiClient
    {
        private readonly HttpClient _http;
        private string _credentialCookie = "";
        private long _uin;
        private string? _credentialJson;

        public QqApiClient(string baseUrl)
        {
            // QQ CDN 单连接限速约 200KB/s，放开连接数上限让分片下载真正并行
            System.Net.ServicePointManager.DefaultConnectionLimit = 64;
            // 音频/CDN 请求禁用系统代理（用户代理软件会拖垮 QQ CDN 下载），API 走本机 sidecar 也无需代理
            var handler = new HttpClientHandler { UseProxy = false };
            _http = new HttpClient(handler)
            {
                BaseAddress = new Uri(baseUrl),
                Timeout = TimeSpan.FromMinutes(10)
            };
        }

        public bool IsLoggedIn => _uin > 0 && !string.IsNullOrEmpty(_credentialCookie);
        public long Uin => _uin;

        /// <summary>二维码登录方式：qq / wx。</summary>
        public string QrLoginType { get; set; } = "qq";
        public string? CredentialJson => _credentialJson;

        /// <summary>当前账号加密 UIN（encrypt_uin），用于每日30首等按账号接口。</summary>
        public string? EncryptUin
        {
            get
            {
                if (string.IsNullOrEmpty(_credentialJson))
                    return null;
                try
                {
                    using var doc = JsonDocument.Parse(_credentialJson);
                    return doc.RootElement.TryGetProperty("encrypt_uin", out var v)
                        && v.ValueKind == JsonValueKind.String
                        ? v.GetString()
                        : null;
                }
                catch
                {
                    return null;
                }
            }
        }

        private static string CredentialFile => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "AuralDesk", "qq_credential.json");

        // ---------------- 登录 ----------------

        public async Task<(string Identifier, string ImgDataUrl)?> GetQrCodeAsync()
        {
            using var doc = await GetJsonAsync("/login/qrcode/" + QrLoginType);
            var d = doc.RootElement.GetProperty("data");
            return (d.GetProperty("identifier").GetString()!, d.GetProperty("img").GetString()!);
        }

        public async Task<(int Event, string? CredentialJson)> CheckQrStatusAsync(string identifier)
        {
            using var doc = await GetJsonAsync("/login/qrcode/" + QrLoginType + "/status?identifier=" +
                Uri.EscapeDataString(identifier));
            var d = doc.RootElement.GetProperty("data");
            var ev = d.GetProperty("event").GetInt32();
            string? cred = null;
            if (d.TryGetProperty("credential", out var c) && c.ValueKind == JsonValueKind.Object)
                cred = c.GetRawText();
            return (ev, cred);
        }

        /// <summary>把登录凭证 JSON 转成 Cookie 并记录 uin。成功返回 true。</summary>
        public bool ApplyCredentialJson(string? credentialJson)
        {
            if (string.IsNullOrWhiteSpace(credentialJson))
                return false;
            try
            {
                using var doc = JsonDocument.Parse(credentialJson);
                var r = doc.RootElement;
                if (!r.TryGetProperty("musicid", out var mid) || mid.GetInt64() <= 0)
                    return false;
                _uin = mid.GetInt64();
                var names = new[]
                {
                    "musicid", "musickey", "openid", "refresh_token", "access_token",
                    "expired_at", "unionid", "str_musicid", "refresh_key"
                };
                var parts = names
                    .Where(n => r.TryGetProperty(n, out var v) &&
                                (v.ValueKind == JsonValueKind.String && !string.IsNullOrEmpty(v.GetString()) ||
                                 v.ValueKind == JsonValueKind.Number))
                    .Select(n =>
                    {
                        var v = r.GetProperty(n);
                        var value = v.ValueKind == JsonValueKind.Number
                            ? v.GetRawText()
                            : v.GetString();
                        return $"{n}={value}";
                    });
                _credentialCookie = string.Join("; ", parts);
                _credentialJson = credentialJson;
                SaveCredential();
                return true;
            }
            catch
            {
                return false;
            }
        }

        public void SaveCredential()
        {
            try
            {
                if (string.IsNullOrEmpty(_credentialJson))
                    return;
                var dir = Path.GetDirectoryName(CredentialFile);
                if (!string.IsNullOrEmpty(dir))
                    Directory.CreateDirectory(dir);
                // DPAPI 加密后落盘，仅当前 Windows 用户可解密
                var encrypted = CredentialProtector.Protect(Encoding.UTF8.GetBytes(_credentialJson));
                File.WriteAllBytes(CredentialFile, encrypted);
            }
            catch
            {
                // 持久化失败不阻塞使用
            }
        }

        /// <summary>启动时读取上次保存的凭证并应用。</summary>
        public bool LoadSavedCredential()
        {
            try
            {
                if (!File.Exists(CredentialFile))
                    return false;
                var bytes = File.ReadAllBytes(CredentialFile);
                // 新格式：DPAPI 加密
                var plain = CredentialProtector.Unprotect(bytes);
                if (plain != null && plain.Length > 0)
                    return ApplyCredentialJson(Encoding.UTF8.GetString(plain));
                // 旧格式：明文 JSON（首次启动后自动迁移为加密存储）
                try
                {
                    var legacy = Encoding.UTF8.GetString(bytes);
                    if (legacy.TrimStart().StartsWith("{", StringComparison.Ordinal)
                        && ApplyCredentialJson(legacy))
                    {
                        SaveCredential();
                        return true;
                    }
                }
                catch
                {
                    // 忽略损坏的旧文件
                }
                return false;
            }
            catch
            {
                return false;
            }
        }

        public void ClearCredential()
        {
            _credentialCookie = "";
            _uin = 0;
            _credentialJson = null;
            try
            {
                if (File.Exists(CredentialFile))
                    File.Delete(CredentialFile);
            }
            catch
            {
                // 忽略
            }
        }

        // ---------------- 目录 ----------------

        public async Task<string> GetNicknameAsync()
        {
            try
            {
                using var doc = await GetJsonAsync($"/user/{_uin}/homepage");
                var bi = doc.RootElement.GetProperty("data").GetProperty("base_info");
                return bi.GetProperty("name").GetString() ?? "";
            }
            catch
            {
                return "";
            }
        }

        public async Task<JsonElement> GetFavSongsAsync(int page, int num = 30)
        {
            // sidecar 的 /user/{euin} 需要加密 UIN（纯数字 musicid 会被 QQ 接口拒绝）
            var euin = EncryptUin ?? _uin.ToString();
            using var doc = await GetJsonAsync(
                $"/user/{Uri.EscapeDataString(euin)}/fav/songs?page={page}&num={num}");
            return CloneData(doc);
        }

        /// <summary>收藏歌曲到「我喜欢的音乐」（dirid=201）。</summary>
        public async Task<bool> AddFavSongAsync(long songId, int songType)
        {
            using var doc = await PostJsonAsync("/songlist/add_songs",
                $"{{\"dirid\":201,\"song_id\":[{songId}],\"song_type\":[{songType}],\"tid\":0}}");
            return true;
        }

        /// <summary>从「我喜欢的音乐」取消收藏。</summary>
        public async Task<bool> RemoveFavSongAsync(long songId, int songType)
        {
            using var doc = await PostJsonAsync("/songlist/del_songs",
                $"{{\"dirid\":201,\"song_id\":[{songId}],\"song_type\":[{songType}],\"tid\":0}}");
            return true;
        }

        /// <summary>获取账号收藏的专辑（收藏列表独立于收藏歌曲）。</summary>
        public async Task<JsonElement> GetFavAlbumsAsync(int page, int num = 30)
        {
            var euin = EncryptUin ?? _uin.ToString();
            using var doc = await GetJsonAsync(
                $"/user/{Uri.EscapeDataString(euin)}/fav/albums?page={page}&num={num}");
            return CloneData(doc);
        }

        /// <summary>收藏专辑（进入账号的「收藏的专辑」）。</summary>
        public async Task<bool> AddFavAlbumAsync(long albumId)
        {
            using var doc = await PostJsonAsync("/album/fav", $"{{\"album_id\":[{albumId}]}}");
            return true;
        }

        /// <summary>取消收藏专辑。</summary>
        public async Task<bool> RemoveFavAlbumAsync(long albumId)
        {
            using var doc = await PostJsonAsync("/album/unfav", $"{{\"album_id\":[{albumId}]}}");
            return true;
        }

        public async Task<JsonElement> GetSingerInfoAsync(string mid)
        {
            using var doc = await GetJsonAsync("/singer/" + Uri.EscapeDataString(mid) + "/info");
            return CloneData(doc);
        }

        public async Task<JsonElement> GetSingerDescAsync(string mid)
        {
            using var doc = await GetJsonAsync("/singer/" + Uri.EscapeDataString(mid) + "/desc");
            return CloneData(doc);
        }

        public async Task<JsonElement> GetSingerSongsAsync(string mid, int page, int num = 30)
        {
            using var doc = await GetJsonAsync(
                $"/singer/{Uri.EscapeDataString(mid)}/songs?page={page}&num={num}");
            return CloneData(doc);
        }

        public async Task<JsonElement> GetSingerAlbumsAsync(string mid, int page, int num = 30)
        {
            using var doc = await GetJsonAsync(
                $"/singer/{Uri.EscapeDataString(mid)}/albums?page={page}&num={num}");
            return CloneData(doc);
        }

        public async Task<JsonElement> GetAlbumSongsAsync(string albumMid, int page, int num = 30)
        {
            using var doc = await GetJsonAsync(
                $"/album/{Uri.EscapeDataString(albumMid)}/songs?page={page}&num={num}");
            return CloneData(doc);
        }

        /// <summary>获取账号「每日30首」系统歌单（服务端按登录账号返回个性化内容）。</summary>
        public async Task<JsonElement> GetDaily30Async(string euin)
        {
            using var doc = await GetJsonAsync("/user/" + Uri.EscapeDataString(euin) + "/daily30");
            return CloneData(doc);
        }

        public async Task<JsonElement> GetCreatedSonglistsAsync()
        {
            using var doc = await GetJsonAsync($"/user/{_uin}/created_songlists");
            return CloneData(doc);
        }

        public async Task<JsonElement> GetSonglistDetailAsync(long songlistId, int page, int num = 30)
        {
            using var doc = await GetJsonAsync($"/songlist/{songlistId}/detail?page={page}&num={num}");
            return CloneData(doc);
        }

        public async Task<JsonElement> GetGuessRecommendAsync()
        {
            using var doc = await GetJsonAsync("/recommend/get_guess_recommend");
            return CloneData(doc);
        }

        /// <summary>主页推荐 feed（卡片列表）。</summary>
        public async Task<JsonElement> SearchAsync(string keyword, int page, int num = 30)
        {
            using var doc = await GetJsonAsync(
                "/search/general_search?keyword=" + Uri.EscapeDataString(keyword) + $"&page={page}&num={num}");
            return CloneData(doc);
        }

        /// <summary>获取歌曲歌词（LRC）。返回原文与翻译，无歌词返回 null。</summary>
        public async Task<(string Lyric, string Trans)?> GetLyricAsync(string value)
        {
            using var doc = await GetJsonAsync(
                "/song/" + Uri.EscapeDataString(value) + "/lyric?trans=1&roma=0");
            var d = doc.RootElement.GetProperty("data");
            var lyric = d.TryGetProperty("lyric", out var l) ? l.GetString() ?? "" : "";
            var trans = d.TryGetProperty("trans", out var t) ? t.GetString() ?? "" : "";
            if (string.IsNullOrWhiteSpace(lyric))
                return null;
            return (lyric, trans);
        }

        /// <summary>通过歌曲 MID 查询专辑 MID（列表缺专辑信息时补查封面用），失败返回 null。</summary>
        public async Task<string?> GetAlbumMidAsync(string mid)
        {
            try
            {
                using var doc = await GetJsonAsync("/song/" + Uri.EscapeDataString(mid) + "/detail");
                var d = doc.RootElement.GetProperty("data");
                if (d.TryGetProperty("track", out var track) &&
                    track.TryGetProperty("album", out var album) &&
                    album.TryGetProperty("mid", out var m) && m.ValueKind == JsonValueKind.String)
                {
                    return m.GetString();
                }
                return null;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>通过歌曲 MID 解析数字歌曲 ID（恢复的队列项可能缺 ID），失败返回 0。</summary>
        public async Task<long> ResolveSongIdAsync(string mid)
        {
            try
            {
                using var doc = await GetJsonAsync("/song/" + Uri.EscapeDataString(mid) + "/detail");
                var d = doc.RootElement.GetProperty("data");
                if (d.TryGetProperty("track", out var track) &&
                    track.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.Number)
                    return id.GetInt64();
                if (d.TryGetProperty("id", out var id2) && id2.ValueKind == JsonValueKind.Number)
                    return id2.GetInt64();
            }
            catch
            {
                // 解析失败返回 0
            }
            return 0;
        }

        /// <summary>下载专辑封面（300x300 JPEG），失败返回 null。</summary>
        public async Task<byte[]?> DownloadCoverAsync(string albumMid)
        {
            if (string.IsNullOrWhiteSpace(albumMid))
                return null;
            var url = "https://y.gtimg.cn/music/photo_new/T002R300x300M000" + albumMid + ".jpg";
            try
            {
                using var req = new HttpRequestMessage(HttpMethod.Get, url);
                req.Headers.Referrer = new Uri("https://y.qq.com/");
                using var resp = await _http.SendAsync(req);
                if (!resp.IsSuccessStatusCode)
                    return null;
                return await resp.Content.ReadAsByteArrayAsync();
            }
            catch
            {
                return null;
            }
        }

        // ---------------- 音源 ----------------

        public async Task<SongUrlResult?> GetSongUrlAsync(string mid, int fileType, string mediaMid = "")
        {
            var body = "{\"file_info\":[{\"mid\":\"" + mid + "\",\"song_type\":0" +
                (string.IsNullOrEmpty(mediaMid) ? "" : ",\"media_mid\":\"" + mediaMid + "\"") +
                "}],\"file_type\":" + fileType + "}";
            using var doc = await PostJsonAsync("/song/get_song_urls", body);
            var d = doc.RootElement.GetProperty("data");
            if (!d.TryGetProperty("data", out var list) || list.ValueKind != JsonValueKind.Array || list.GetArrayLength() == 0)
                return null;
            var item = list[0];
            var purl = item.TryGetProperty("purl", out var p) ? p.GetString() ?? "" : "";
            if (string.IsNullOrEmpty(purl))
                return null;
            var filename = item.TryGetProperty("filename", out var f) ? f.GetString() ?? "" : "";
            return new SongUrlResult { Purl = purl, Filename = filename };
        }

        /// <summary>获取 Hi-Res 下载密钥（ekey），用于离线解密 mflac（PcV1Legacy）。</summary>
        public async Task<string?> GetEkeyAsync(string mid, string mediaMid, int songType = 1)
        {
            try
            {
                var q = $"?mid={Uri.EscapeDataString(mid)}&media_mid={Uri.EscapeDataString(mediaMid)}&song_type={songType}";
                using var doc = await PostJsonAsync("/song/get_edown_url" + q, "{}");
                var d = doc.RootElement.GetProperty("data");
                if (d.TryGetProperty("ekey", out var e) && e.ValueKind == JsonValueKind.String)
                {
                    var v = e.GetString() ?? "";
                    return string.IsNullOrEmpty(v) ? null : v;
                }
            }
            catch
            {
                // ekey 获取失败不影响使用（Hi-Res 降级无损）
            }
            return null;
        }

        public async Task<byte[]?> DownloadAsync(string url)
        {
            // 接口返回的是相对路径 purl，需拼上流媒体服务器域名
            if (!url.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                url = "https://dl.stream.qqmusic.qq.com/" + url;
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.Referrer = new Uri("https://y.qq.com/");
            req.Headers.UserAgent.ParseAdd(
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0 Safari/537.36");
            if (!string.IsNullOrEmpty(_credentialCookie))
                req.Headers.TryAddWithoutValidation("Cookie", _credentialCookie);
            using var resp = await _http.SendAsync(req);
            if (!resp.IsSuccessStatusCode)
                return null;
            return await resp.Content.ReadAsByteArrayAsync();
        }

        /// <summary>
        /// 多线程分片下载。QQ CDN 单连接限速约 200KB/s，分片并行可接近跑满带宽。
        /// 失败时自动回退为单连接下载。
        /// </summary>
        /// <param name="progress">进度回调（已下载字节, 总字节），从后台线程触发。</param>
        public async Task<byte[]?> DownloadSegmentedAsync(
            string url, int threads = 10, Action<long, long>? progress = null)
        {
            if (!url.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                url = "https://dl.stream.qqmusic.qq.com/" + url;

            // 探测总大小
            long total;
            try
            {
                using var probe = new HttpRequestMessage(HttpMethod.Get, url);
                if (!string.IsNullOrEmpty(_credentialCookie))
                    probe.Headers.TryAddWithoutValidation("Cookie", _credentialCookie);
                probe.Headers.Referrer = new Uri("https://y.qq.com/");
                using var presp = await _http.SendAsync(probe, HttpCompletionOption.ResponseHeadersRead);
                if (!presp.IsSuccessStatusCode)
                    return null;
                total = presp.Content.Headers.ContentLength ?? 0;
                if (total <= 0 || total > 200L * 1024 * 1024)
                    return await DownloadAsync(url);
            }
            catch
            {
                return await DownloadAsync(url);
            }

            if (total <= 2 * 1024 * 1024)
                return await DownloadAsync(url); // 小文件单连接即可

            progress?.Invoke(0, total);
            var segCount = Math.Min(threads, Math.Max(4, (int)(total / (1024 * 1024)) + 1));
            var segSize = total / segCount;
            var buffers = new byte[segCount][];
            var tasks = new Task[segCount];
            long sharedDone = 0;
            for (var i = 0; i < segCount; i++)
            {
                var idx = i;
                var start = idx * segSize;
                var end = idx == segCount - 1 ? total - 1 : start + segSize - 1;
                tasks[idx] = Task.Run(async () =>
                {
                    using var req = new HttpRequestMessage(HttpMethod.Get, url);
                    if (!string.IsNullOrEmpty(_credentialCookie))
                        req.Headers.TryAddWithoutValidation("Cookie", _credentialCookie);
                    req.Headers.Referrer = new Uri("https://y.qq.com/");
                    req.Headers.Range = new RangeHeaderValue(start, end);
                    using var resp = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead);
                    if (resp.StatusCode != HttpStatusCode.PartialContent)
                        throw new QqApiException("CDN 未支持分片请求");
                    await using var src = await resp.Content.ReadAsStreamAsync();
                    using var dst = new MemoryStream((int)(end - start + 1));
                    var buffer = new byte[64 * 1024];
                    long lastReport = 0;
                    int read;
                    while ((read = await src.ReadAsync(buffer, 0, buffer.Length)) > 0)
                    {
                        await dst.WriteAsync(buffer, 0, read);
                        var done = Interlocked.Add(ref sharedDone, read);
                        if (progress != null && Environment.TickCount64 - lastReport >= 100)
                        {
                            lastReport = Environment.TickCount64;
                            progress(done, total);
                        }
                    }
                    buffers[idx] = dst.ToArray();
                });
            }

            try
            {
                await Task.WhenAll(tasks);
            }
            catch
            {
                return await DownloadAsync(url); // 分片失败回退单连接
            }

            var result = new byte[total];
            var offset = 0L;
            foreach (var buffer in buffers)
            {
                if (buffer == null)
                    return await DownloadAsync(url);
                Buffer.BlockCopy(buffer, 0, result, (int)offset, buffer.Length);
                offset += buffer.Length;
            }
            return result;
        }

        // ---------------- 内部 ----------------

        private async Task<JsonDocument> GetJsonAsync(string path) => await SendAsync("GET", path, null);

        private async Task<JsonDocument> PostJsonAsync(string path, string body) => await SendAsync("POST", path, body);

        private async Task<JsonDocument> SendAsync(string method, string path, string? body)
        {
            for (var attempt = 0; attempt < 2; attempt++)
            {
                using var req = new HttpRequestMessage(new HttpMethod(method), path);
                if (!string.IsNullOrEmpty(_credentialCookie))
                    req.Headers.TryAddWithoutValidation("Cookie", _credentialCookie);
                if (body != null)
                    req.Content = new StringContent(body, Encoding.UTF8, "application/json");

                // sidecar 刚启动时服务可能未就绪：连接类错误重试 3 次（间隔 1s）
                HttpResponseMessage? resp = null;
                for (var connTry = 0; connTry < 3; connTry++)
                {
                    try
                    {
                        resp = await _http.SendAsync(req);
                        break;
                    }
                    catch (HttpRequestException) when (connTry < 2)
                    {
                        await Task.Delay(1000);
                    }
                    catch (HttpRequestException ex)
                    {
                        throw new QqApiException("QQ 音乐服务连接失败：" + ex.Message);
                    }
                }
                if (resp == null)
                    throw new QqApiException("QQ 音乐服务未就绪（连接失败）");
                var text = await resp.Content.ReadAsStringAsync();

                if (resp.StatusCode == HttpStatusCode.Unauthorized && attempt == 0 && IsLoggedIn)
                {
                    if (await TryRefreshAsync())
                        continue;
                    ClearCredential(); // 刷新失败视为登录失效，避免界面显示已登录但请求全部失败
                }

                JsonDocument? doc = null;
                try
                {
                    doc = JsonDocument.Parse(text);
                }
                catch
                {
                    throw new QqApiException($"QQ 音乐接口返回异常数据（HTTP {resp.StatusCode}）");
                }

                if (doc.RootElement.TryGetProperty("code", out var c) && c.GetInt32() == 0)
                    return doc;

                var msg = doc.RootElement.TryGetProperty("msg", out var m)
                    ? m.GetString() ?? ""
                    : $"HTTP {resp.StatusCode}";
                doc.Dispose();
                throw new QqApiException("QQ 音乐接口错误：" + msg);
            }
            throw new QqApiException("QQ 音乐接口请求失败");
        }

        /// <summary>用当前凭证刷新登录态，成功返回 true。</summary>
        public async Task<bool> TryRefreshAsync()
        {
            try
            {
                using var req = new HttpRequestMessage(HttpMethod.Get, "/login/refresh_credential");
                if (!string.IsNullOrEmpty(_credentialCookie))
                    req.Headers.TryAddWithoutValidation("Cookie", _credentialCookie);
                var resp = await _http.SendAsync(req);
                var text = await resp.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(text);
                if (doc.RootElement.TryGetProperty("code", out var c) && c.GetInt32() == 0)
                {
                    var cred = doc.RootElement.GetProperty("data").GetRawText();
                    return ApplyCredentialJson(cred);
                }
            }
            catch
            {
                // 刷新失败视为登录失效
            }
            return false;
        }

        private static JsonElement CloneData(JsonDocument doc) =>
            doc.RootElement.GetProperty("data").Clone();

        // ---------------- 静态解析辅助 ----------------

        /// <summary>
        /// 解析已下载音频的实际格式与采样率。FLAC 解析 STREAMINFO，Ogg 解析 Vorbis 头，
        /// MP3 解析帧头。用于校验 QQ 音质档实际拿到的文件（标准接口无原生 96k，最高 48k/24bit）。
        /// </summary>
        public static string? ParseAudioInfo(byte[] data, string filename)
        {
            if (data == null || data.Length < 32)
                return null;

            // FLAC: "fLaC" + STREAMINFO（大端 64 位：采样率占高 20 位，位深在其后）
            if (data[0] == (byte)'f' && data[1] == (byte)'L' && data[2] == (byte)'a' && data[3] == (byte)'C')
            {
                if (data.Length < 26)
                    return "FLAC";
                var v = ((long)data[18] << 56) | ((long)data[19] << 48) | ((long)data[20] << 40)
                      | ((long)data[21] << 32) | ((long)data[22] << 24) | ((long)data[23] << 16)
                      | ((long)data[24] << 8) | data[25];
                var sampleRate = (int)(v >> 44);
                var channels = (int)(((v >> 41) & 0x7) + 1);
                var bits = (int)(((v >> 36) & 0x1F) + 1);
                return $"FLAC {sampleRate}Hz/{bits}bit/{channels}ch";
            }

            // Ogg: 定位 Vorbis identification header 0x01 "vorbis" version(4) channels(1) sample_rate(4, LE)
            if (data.Length >= 4 && data[0] == (byte)'O' && data[1] == (byte)'g'
                && data[2] == (byte)'g' && data[3] == (byte)'S')
            {
                for (var i = 0; i + 12 < data.Length; i++)
                {
                    if (data[i] == 1 && data[i + 1] == (byte)'v' && data[i + 2] == (byte)'o'
                        && data[i + 3] == (byte)'r' && data[i + 4] == (byte)'b'
                        && data[i + 5] == (byte)'i' && data[i + 6] == (byte)'s')
                    {
                        // Vorbis identification header：i=0x01, i+1..i+7="vorbis",
                        // i+8..i+11=version(LE), i+12=channels, i+13..i+16=sample_rate(LE)
                        var ch = data[i + 12];
                        var sr = BitConverter.ToInt32(data, i + 13);
                        return $"Ogg {sr}Hz/{ch}ch";
                    }
                }
                return "Ogg";
            }

            // MP3: 文件常带 ID3v2 标签，先跳过再线性查找 0xFFE0 帧头
            var frameOffset = 0;
            if (data.Length >= 10 && data[0] == (byte)'I' && data[1] == (byte)'D' && data[2] == (byte)'3')
            {
                var size = ((data[6] & 0x7F) << 21) | ((data[7] & 0x7F) << 14)
                         | ((data[8] & 0x7F) << 7) | (data[9] & 0x7F);
                frameOffset = 10 + size + ((data[5] & 0x10) != 0 ? 10 : 0);
            }
            for (var j = frameOffset; j + 3 < data.Length; j++)
            {
                if (data[j] != 0xFF || (data[j + 1] & 0xE0) != 0xE0)
                    continue;
                var versionIdx = (data[j + 1] >> 3) & 0x3;   // 3=MPEG1, 2=MPEG2, 0=MPEG2.5
                var layerIdx = (data[j + 1] >> 1) & 0x3;     // 1=Layer III
                var brIdx = (data[j + 2] >> 4) & 0xF;
                var srIdx = (data[j + 2] >> 2) & 0x3;
                var isMpeg1 = versionIdx == 3;
                var rates = isMpeg1
                    ? new[] { 44100, 48000, 32000 }
                    : new[] { 22050, 24000, 16000 };
                var kbpsTable = layerIdx == 1
                    ? (isMpeg1
                        ? new[] { 0, 32, 40, 48, 56, 64, 80, 96, 112, 128, 160, 192, 224, 256, 320, 0 }
                        : new[] { 0, 8, 16, 24, 32, 40, 48, 56, 64, 80, 96, 112, 128, 144, 160, 0 })
                    : new[] { 0, 32, 48, 56, 64, 80, 96, 112, 128, 160, 192, 224, 256, 320, 384, 0 };
                var rate = srIdx < 3 ? rates[srIdx] : 0;
                var kbps = brIdx < 16 ? kbpsTable[brIdx] : 0;
                return rate > 0 ? $"MP3 {rate}Hz/{kbps}kbps" : "MP3";
            }
            var dot = filename.LastIndexOf('.');
            var ext = dot >= 0 ? filename[(dot + 1)..].ToLowerInvariant() : "";
            if (ext is "m4a" or "mp4" or "aac")
                return "AAC";
            if (ext is "mp3" or "mp2")
                return "MP3";
            return null;
        }

        public static List<QqSongItem> ParseSongs(JsonElement data)
        {
            var result = new List<QqSongItem>();
            // 收藏/歌单详情：data.songlist[]；搜索：data.song.items[]；猜你喜欢：data.songs[]
            JsonElement list = default;
            if (data.TryGetProperty("songlist", out var sl) && sl.ValueKind == JsonValueKind.Array)
                list = sl;
            else if (data.TryGetProperty("song_list", out var sl2) && sl2.ValueKind == JsonValueKind.Array)
                list = sl2;
            else if (data.TryGetProperty("song", out var sg) &&
                     sg.TryGetProperty("items", out var items) && items.ValueKind == JsonValueKind.Array)
                list = items;
            else if (data.TryGetProperty("songs", out var ss) && ss.ValueKind == JsonValueKind.Array)
                list = ss;

            if (list.ValueKind != JsonValueKind.Array)
                return result;

            foreach (var item in list.EnumerateArray())
            {
                var mid = GetStr(item, "mid");
                if (string.IsNullOrEmpty(mid))
                    continue;
                var singer = "";
                if (item.TryGetProperty("singer", out var singers) && singers.ValueKind == JsonValueKind.Array)
                    singer = string.Join(" / ", singers.EnumerateArray()
                        .Select(s => GetStr(s, "name"))
                        .Where(n => !string.IsNullOrEmpty(n)));
                result.Add(new QqSongItem
                {
                    Mid = mid,
                    Id = GetLong(item, "id"),
                    Title = CleanSearchText(GetStr(item, "name") ?? GetStr(item, "title") ?? "未知歌曲"),
                    Singer = CleanSearchText(singer),
                    Album = item.TryGetProperty("album", out var album)
                        ? CleanSearchText(GetStr(album, "name") ?? "")
                        : "",
                    AlbumMid = item.TryGetProperty("album", out var album2) ? GetStr(album2, "mid") ?? "" : "",
                    Duration = FormatDuration(GetInt(item, "interval")),
                    SongType = GetInt(item, "type"),
                    MediaMid = item.TryGetProperty("file", out var file)
                        ? GetStr(file, "media_mid") ?? ""
                        : ""
                });
            }
            return result;
        }

        public static List<QqPlaylistItem> ParsePlaylists(JsonElement data)
        {
            var result = new List<QqPlaylistItem>();
            if (!data.TryGetProperty("playlists", out var list) || list.ValueKind != JsonValueKind.Array)
                return result;
            foreach (var item in list.EnumerateArray())
            {
                if (!item.TryGetProperty("id", out var id) || id.GetInt64() <= 0)
                    continue;
                var name = GetStr(item, "title") ?? GetStr(item, "name") ?? "未命名歌单";
                var count = GetInt(item, "songnum");
                result.Add(new QqPlaylistItem
                {
                    Id = id.GetInt64(),
                    Name = CleanSearchText(name),
                    Info = count > 0 ? $"{count} 首" : ""
                });
            }
            return result;
        }

        public static bool HasMore(JsonElement data)
        {
            if (data.TryGetProperty("hasmore", out var hm))
                return hm.GetInt32() != 0;
            if (data.TryGetProperty("has_more", out var hm2))
                return hm2.ValueKind == JsonValueKind.True || (hm2.ValueKind == JsonValueKind.Number && hm2.GetInt32() != 0);
            return false;
        }

        /// <summary>
        /// 解析专辑列表，兼容多种返回结构：
        /// 收藏专辑 data.albums[]、搜索 data.album.items[]、歌手专辑 data.albums[]。
        /// </summary>
        public static List<QqAlbumInfo> ParseAlbums(JsonElement data)
        {
            var result = new List<QqAlbumInfo>();
            JsonElement list = default;
            if (data.TryGetProperty("albums", out var a) && a.ValueKind == JsonValueKind.Array)
                list = a;
            else if (data.TryGetProperty("album_list", out var al2) && al2.ValueKind == JsonValueKind.Array)
                list = al2;
            else if (data.TryGetProperty("album", out var al) && al.ValueKind == JsonValueKind.Object &&
                     al.TryGetProperty("items", out var ai) && ai.ValueKind == JsonValueKind.Array)
                list = ai;
            else if (data.TryGetProperty("items", out var it) && it.ValueKind == JsonValueKind.Array)
                list = it;
            else if (data.TryGetProperty("list", out var ls) && ls.ValueKind == JsonValueKind.Array)
                list = ls;

            if (list.ValueKind != JsonValueKind.Array)
                return result;

            foreach (var item in list.EnumerateArray())
            {
                var mid = GetStr(item, "mid") ?? GetStr(item, "album_mid") ?? GetStr(item, "albumMid");
                if (string.IsNullOrEmpty(mid))
                    continue;
                // 作者字段分散：搜索结果 singer/singer_list、歌手专辑 singer_name、
                // 收藏专辑 singers[]。全部兼容。
                var singer = GetStr(item, "singer") ?? GetStr(item, "singer_name") ?? "";
                if (string.IsNullOrEmpty(singer))
                {
                    var singerArr = item.TryGetProperty("singer_list", out var sl)
                        ? sl
                        : item.TryGetProperty("singers", out var sg) ? sg : default;
                    if (singerArr.ValueKind == JsonValueKind.Array)
                        singer = string.Join(" / ", singerArr.EnumerateArray()
                            .Select(s => GetStr(s, "name"))
                            .Where(n => !string.IsNullOrEmpty(n)));
                }
                // 封面：pic 其次；收藏专辑 pmid 是完整 URL；最后按 mid 构造。统一转 https 保证 WPF/系统可加载。
                var pic = GetStr(item, "pic") ?? "";
                if (string.IsNullOrEmpty(pic))
                {
                    var pmid = GetStr(item, "pmid") ?? "";
                    if (pmid.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                        pic = pmid;
                    else if (pmid.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
                        pic = "https://" + pmid.Substring(7);
                }
                if (string.IsNullOrEmpty(pic))
                    pic = $"https://y.gtimg.cn/music/photo_new/T002R300x300M000{mid}.jpg";
                else if (pic.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
                    pic = "https://" + pic.Substring(7);
                result.Add(new QqAlbumInfo
                {
                    AlbumMid = mid,
                    AlbumId = item.TryGetProperty("id", out var idEl) && idEl.ValueKind == JsonValueKind.Number
                        ? idEl.GetInt64()
                        : 0,
                    Name = CleanSearchText(GetStr(item, "name") ?? GetStr(item, "title") ?? "未知专辑"),
                    Singer = CleanSearchText(singer),
                    Date = GetStr(item, "publish_date") ?? GetStr(item, "time_public") ?? "",
                    CoverUrl = pic,
                    AlbumType = GetStr(item, "album_type") ?? ""
                });
            }
            return result;
        }

        /// <summary>解析歌手列表，兼容搜索 data.singer.items[] / 关注歌手 data.singers[]。</summary>
        public static List<QqSingerInfo> ParseSingers(JsonElement data)
        {
            var result = new List<QqSingerInfo>();
            JsonElement list = default;
            if (data.TryGetProperty("singers", out var s) && s.ValueKind == JsonValueKind.Array)
                list = s;
            else if (data.TryGetProperty("singer", out var sg) && sg.ValueKind == JsonValueKind.Object &&
                     sg.TryGetProperty("items", out var si) && si.ValueKind == JsonValueKind.Array)
                list = si;
            else if (data.TryGetProperty("items", out var it) && it.ValueKind == JsonValueKind.Array)
                list = it;

            if (list.ValueKind != JsonValueKind.Array)
                return result;

            foreach (var item in list.EnumerateArray())
            {
                var mid = GetStr(item, "mid") ?? GetStr(item, "singer_mid") ?? GetStr(item, "singer_id") ?? GetStr(item, "id");
                if (string.IsNullOrEmpty(mid))
                    continue;
                result.Add(new QqSingerInfo
                {
                    SingerMid = mid,
                    Name = CleanSearchText(GetStr(item, "name") ?? "未知歌手"),
                    Pic = GetStr(item, "pic") ?? GetStr(item, "singerPic") ?? "",
                    Sub = GetStr(item, "subtitle") ?? "",
                    SongNum = GetInt(item, "song_num") > 0 ? GetInt(item, "song_num") : GetInt(item, "songNum"),
                    AlbumNum = GetInt(item, "album_num") > 0 ? GetInt(item, "album_num") : GetInt(item, "albumNum")
                });
            }
            return result;
        }

        /// <summary>去掉搜索结果中的 &lt;em&gt; 高亮标签。</summary>
        private static string CleanSearchText(string? s) =>
            string.IsNullOrEmpty(s)
                ? ""
                : System.Text.RegularExpressions.Regex.Replace(s, "<.*?>", "").Trim();

        private static string? GetStr(JsonElement el, string name) =>
            el.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String
                ? v.GetString()
                : null;

        private static int GetInt(JsonElement el, string name) =>
            el.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number
                ? v.GetInt32()
                : 0;

        private static long GetLong(JsonElement el, string name) =>
            el.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number
                ? v.GetInt64()
                : 0;

        private static string FormatDuration(int seconds)
        {
            if (seconds <= 0)
                return "";
            return $"{(seconds / 60):00}:{seconds % 60:00}";
        }
    }
}
