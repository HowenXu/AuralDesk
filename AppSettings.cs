using System;
using System.IO;
using System.Text.Json;

namespace AuralDesk
{
    /// <summary>轻量 JSON 设置存储（%APPDATA%\AuralDesk\settings.json）。</summary>
    public class AppSettings
    {
        public int SourceIndex { get; set; } = 1;       // QQ音乐
        public int OutputIndex { get; set; } = 0;       // NAA
        public int QqApiQualityIndex { get; set; } = 0; // QQ音质：0=Hi-Res 1=无损 2=普通
        public int QqQualityIndex { get; set; } = 2;    // 网页音质：0=m4a 1=128k 2=320k
        public int BufferOption { get; set; } = 1;      // 0=256MB 1=512MB 2=1GB 3=自定义
        public int CustomBufferMb { get; set; } = 1024;
        public int OutputModeIndex { get; set; } = 0;   // 0=NAA 1=USB升频 2=USB直通 3=DLNA
        public bool HqEnabled { get; set; } = true;
        public string HqAddress { get; set; } = "127.0.0.1";
        public string HqPort { get; set; } = "4321";
        public bool AutoStartHqPlayer { get; set; }      // 启动软件时自动启动 HQPlayer（默认关闭）
        public string? HqPlayerExePath { get; set; }     // HQPlayer 主程序路径（自选，空则探测常见路径）
        public bool RemoteControlEnabled { get; set; }   // 局域网遥控（默认关闭）
        public bool ShowMem { get; set; } = true;
        public bool ShowCpu { get; set; } = true;
        public int ResumeMode { get; set; }             // 0=不保存 1=仅保存歌曲 2=歌曲+进度
        public string? LastSongPath { get; set; }
        public string? LastSongTitle { get; set; }
        public string? LastSongSinger { get; set; }
        public string? LastSongSource { get; set; }
        public double LastSongPosition { get; set; }
        public int LyricOffsetMs { get; set; } = 400;  // 歌词偏移（毫秒，正=歌词延后显示，负=提前）
        public bool HqMissingShown { get; set; }        // HQPlayer 未安装提醒是否已展示过（仅弹一次）
        public bool AutoStart { get; set; }             // 开机自动启动
        public string? CachePath { get; set; }          // 缓存目录；空则默认 Documents\AuralDesk\cache
        public int CacheCountMode { get; set; }         // 0=按大小清理，1=按保留曲数清理
        public int CacheCount { get; set; } = 5;        // 按曲数清理时保留的歌曲数
        public List<SavedQueueItem>? SavedQueue { get; set; }

        public string ResolvedCachePath =>
            string.IsNullOrWhiteSpace(CachePath)
                ? System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                    "AuralDesk", "cache")
                : CachePath.Trim();

        /// <summary>播放队列快照项（用于重启后恢复队列）。</summary>
        public class SavedQueueItem
        {
            public string Title { get; set; } = "";
            public string Singer { get; set; } = "";
            public string Source { get; set; } = "";
            public string Path { get; set; } = "";
            public string QqMid { get; set; } = "";
            public string AlbumMid { get; set; } = "";
            public string QualityName { get; set; } = "";
            public bool IsCurrent { get; set; }
        }

        private static string FilePath =>
            System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "AuralDesk",
                "settings.json");

        public static AppSettings Load()
        {
            try
            {
                if (File.Exists(FilePath))
                {
                    var json = File.ReadAllText(FilePath);
                    var s = JsonSerializer.Deserialize<AppSettings>(json);
                    if (s != null) return s;
                }
            }
            catch
            {
                // 损坏的配置直接重置
            }
            return new AppSettings();
        }

        public void Save()
        {
            try
            {
                var dir = System.IO.Path.GetDirectoryName(FilePath);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
            }
            catch
            {
                // 写失败不阻塞使用
            }
        }
    }
}
