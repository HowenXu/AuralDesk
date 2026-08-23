using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace AuralDesk
{
    /// <summary>播放队列中的一条曲目。</summary>
    public sealed class QueueTrack : INotifyPropertyChanged
    {
        private bool _isCurrent;

        public required string Title { get; init; }
        public string Singer { get; init; } = "";

        private string _source = "";

        /// <summary>来源/音质描述（下载完成后更新，带属性通知）。</summary>
        public string Source
        {
            get => _source;
            set
            {
                if (_source != value)
                {
                    _source = value;
                    OnPropertyChanged();
                }
            }
        }

        /// <summary>本地文件路径；QQ 流媒体项在下载完成前为空。</summary>
        public string Path { get; set; } = "";

        /// <summary>QQ 歌曲 mid，非空表示该队列项来自 QQ 流媒体（需要下载后才能播放）。</summary>
        public string QqMid { get; init; } = "";

        private bool _isFav;

        /// <summary>是否已收藏（红心，仅 QQ 歌曲有效）。</summary>
        public bool IsFav
        {
            get => _isFav;
            set
            {
                if (_isFav != value)
                {
                    _isFav = value;
                    OnPropertyChanged();
                }
            }
        }

        /// <summary>QQ 专辑 mid（用于封面）。</summary>
        public string AlbumMid { get; init; } = "";

        private string _qualityName = "";

        /// <summary>实际下载到的音质描述（如 Hi-Res · FLAC 48kHz/24bit）。</summary>
        public string QualityName
        {
            get => _qualityName;
            set
            {
                if (_qualityName != value)
                {
                    _qualityName = value;
                    OnPropertyChanged();
                }
            }
        }

        /// <summary>来源 QQ 歌曲对象（用于歌词/封面加载）。</summary>
        public QqSongItem? QqSong { get; init; }

        /// <summary>专辑封面 URL（150x150，播放列表缩略图用）。</summary>
        public string CoverUrl => string.IsNullOrEmpty(AlbumMid)
            ? ""
            : $"https://y.gtimg.cn/music/photo_new/T002R150x150M000{AlbumMid}.jpg";

        /// <summary>是否当前正在播放。</summary>
        public bool IsCurrent
        {
            get => _isCurrent;
            set
            {
                if (_isCurrent != value)
                {
                    _isCurrent = value;
                    OnPropertyChanged();
                }
            }
        }

        private string _cacheState = "";

        /// <summary>缓存状态（下载完成后显示「已缓存待播放」，用于队列中已预下载的下一首）。</summary>
        public string CacheState
        {
            get => _cacheState;
            set
            {
                if (_cacheState != value)
                {
                    _cacheState = value;
                    OnPropertyChanged();
                }
            }
        }

        private double _downloadProgress;

        /// <summary>下载进度（0~1），用于播放队列行内进度条。</summary>
        public double DownloadProgress
        {
            get => _downloadProgress;
            set
            {
                if (Math.Abs(_downloadProgress - value) > 0.001)
                {
                    _downloadProgress = value;
                    OnPropertyChanged();
                }
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
