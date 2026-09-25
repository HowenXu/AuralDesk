using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace AuralDesk
{
    /// <summary>foobar2000 播放状态（来自 beefweb 插件的 /api/player）。</summary>
    public class FoobarStatus
    {
        public string State { get; init; } = "";
        public double Position { get; init; }
        public double Duration { get; init; }
        public bool IsPlaying => State == "playing";
        public bool IsPaused => State == "paused";
    }

    /// <summary>foobar2000 的输出设备（含 WASAPI 独占变体）。</summary>
    public class FoobarOutputDevice
    {
        public string Id { get; init; } = "";
        public string Name { get; init; } = "";
    }

    public enum FoobarPlayResult
    {
        Ok,
        /// <summary>歌曲文件不在 beefweb 的 musicDirs 白名单里，配置已写但需重启 foobar 才生效。</summary>
        RestartRequired,
        Failed
    }

    /// <summary>
    /// foobar2000 控制器：走 beefweb 插件（foo_beefweb）的 HTTP API，
    /// 由 foobar2000 负责实际的音频输出（含 WASAPI 独占），本程序不再实现独占输出。
    /// </summary>
    public class FoobarClient
    {
        public const int DefaultPort = 8880;
        /// <summary>beefweb 的默认播放列表，恒定存在。</summary>
        private const string MainPlaylistId = "p1";

        private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(6) };

        private string host = "127.0.0.1";
        private int port = DefaultPort;

        public bool Connected { get; private set; }

        public string BaseUrl => $"http://{host}:{port}";

        public void UpdateEndpoint(string address, int listenPort)
        {
            host = string.IsNullOrWhiteSpace(address) ? "127.0.0.1" : address.Trim();
            port = listenPort > 0 ? listenPort : DefaultPort;
        }

        /// <summary>beefweb 配置文件位置（插件只在其启动时读取一次）。</summary>
        public static string ConfigPath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "foobar2000-v2", "beefweb", "config.json");

        /// <summary>探测 foobar2000 + beefweb 是否可用：能读到播放状态即算可用。</summary>
        public bool Probe()
        {
            var status = GetStatus();
            Connected = status != null;
            return Connected;
        }

        public FoobarStatus? GetStatus()
        {
            var json = GetJson("/api/player");
            var player = json?["player"];
            if (player == null) return null;
            var active = player["activeItem"];
            return new FoobarStatus
            {
                State = player["playbackState"]?.GetValue<string>() ?? "",
                Position = active?["position"]?.GetValue<double>() ?? 0,
                Duration = active?["duration"]?.GetValue<double>() ?? 0
            };
        }

        /// <summary>
        /// 把文件交给 foobar2000 播放：替换默认播放列表为该文件并立即开始播放。
        /// 用「替换成单曲」而不是追加，避免 foobar 自己的播放列表越堆越长、和本程序队列错位。
        /// </summary>
        public FoobarPlayResult PlayFile(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return FoobarPlayResult.Failed;

            // beefweb 只允许播放 musicDirs 白名单下的文件，先确保当前缓存目录在名单里
            EnsureMusicDir(Path.GetDirectoryName(path)!);

            var body = new JsonObject
            {
                ["items"] = new JsonArray { path },
                ["replace"] = true,
                ["play"] = true
            };
            var code = PostJson($"/api/playlists/{MainPlaylistId}/items/add", body.ToJsonString());
            if (code == System.Net.HttpStatusCode.NoContent || code == System.Net.HttpStatusCode.Accepted)
            {
                Connected = true;
                return FoobarPlayResult.Ok;
            }
            // 403 只会来自「item is not under allowed path」：白名单已写入，等 foobar 重启
            if (code == System.Net.HttpStatusCode.Forbidden) return FoobarPlayResult.RestartRequired;
            return FoobarPlayResult.Failed;
        }

        public bool Pause() => PostOk("/api/player/pause");

        public bool Resume() => PostOk("/api/player/play");

        public bool PlayPause() => PostOk("/api/player/play-pause");

        public bool Stop() => PostOk("/api/player/stop");

        /// <summary>列出 foobar2000 的全部输出设备，包含 [exclusive] 独占变体。</summary>
        public List<FoobarOutputDevice> GetOutputDevices()
        {
            var result = new List<FoobarOutputDevice>();
            var json = GetJson("/api/outputs");
            var types = json?["outputs"]?["types"] as JsonArray;
            if (types == null) return result;
            foreach (var type in types)
            {
                if (type?["devices"] is not JsonArray devices) continue;
                foreach (var device in devices)
                {
                    var id = device?["id"]?.GetValue<string>();
                    if (string.IsNullOrEmpty(id)) continue;
                    result.Add(new FoobarOutputDevice
                    {
                        Id = id,
                        Name = device?["name"]?.GetValue<string>() ?? id
                    });
                }
            }
            return result;
        }

        /// <summary>切换 foobar2000 的输出设备（独占变体即传入名字带 [exclusive] 的那一项）。</summary>
        public bool SetOutputDevice(string deviceId)
        {
            if (string.IsNullOrWhiteSpace(deviceId)) return false;
            var body = new JsonObject { ["deviceId"] = deviceId };
            return PostOk("/api/outputs/active", body.ToJsonString());
        }

        /// <summary>
        /// 确保目录在 beefweb 的 musicDirs 白名单中，否则添加歌曲会 403。
        /// 返回 true 表示配置文件被修改（需重启 foobar2000 才生效）。
        /// </summary>
        public static bool EnsureMusicDir(string dir)
        {
            if (string.IsNullOrWhiteSpace(dir)) return false;
            var full = Path.GetFullPath(dir).TrimEnd('\\');
            try
            {
                JsonObject root;
                var path = ConfigPath;
                if (File.Exists(path))
                {
                    root = JsonNode.Parse(File.ReadAllText(path, Encoding.UTF8)) as JsonObject ?? new JsonObject();
                }
                else
                {
                    root = new JsonObject();
                    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                }

                if (root["musicDirs"] is not JsonArray dirs)
                {
                    dirs = new JsonArray();
                    root["musicDirs"] = dirs;
                }
                foreach (var item in dirs)
                {
                    var existing = item?.GetValue<string>();
                    if (string.IsNullOrEmpty(existing)) continue;
                    if (string.Equals(Path.GetFullPath(existing).TrimEnd('\\'), full,
                                      StringComparison.OrdinalIgnoreCase))
                    {
                        return false; // 已在白名单里
                    }
                }
                dirs.Add(full);
                root["port"] ??= DefaultPort;
                File.WriteAllText(path, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }),
                                  new UTF8Encoding(false));
                return true;
            }
            catch
            {
                return false;
            }
        }

        // ------------------------------------------------------------------

        private bool PostOk(string route, string? body = null)
        {
            var code = PostJson(route, body);
            var ok = code == System.Net.HttpStatusCode.NoContent ||
                     code == System.Net.HttpStatusCode.OK ||
                     code == System.Net.HttpStatusCode.Accepted;
            if (ok) Connected = true;
            return ok;
        }

        private System.Net.HttpStatusCode PostJson(string route, string? body)
        {
            try
            {
                using var content = body == null
                    ? null
                    : new StringContent(body, Encoding.UTF8, "application/json");
                using var resp = Http.PostAsync(BaseUrl + route, content).GetAwaiter().GetResult();
                return resp.StatusCode;
            }
            catch
            {
                Connected = false;
                return 0;
            }
        }

        private JsonNode? GetJson(string route)
        {
            try
            {
                using var resp = Http.GetAsync(BaseUrl + route).GetAwaiter().GetResult();
                if (!resp.IsSuccessStatusCode) return null;
                var text = resp.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                return JsonNode.Parse(text);
            }
            catch
            {
                return null;
            }
        }
    }
}
