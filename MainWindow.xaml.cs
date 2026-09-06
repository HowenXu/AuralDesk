using System;
using System.Diagnostics;
using System.Globalization;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using System.Net.Http;
using System.Text.Json;
using System.Collections.ObjectModel;
using System.Runtime.InteropServices;
using Microsoft.Web.WebView2.Core;
using NAudio.Wave;

namespace AuralDesk
{
    public partial class MainWindow : Window
    {
        private readonly AppSettings settings = AppSettings.Load();
        private bool loaded;
        private bool isPlaying = true;
        private bool hqOn = true;
        private bool hqIsPlaying;
        private bool hqPolling;
        private int hqPrevState = -1;
        private int hqFailCount;
        private DateTime lastHqPoll = DateTime.MinValue;
        private double lastHqPosition = -1;
        private DateTime lastHqMoveTime = DateTime.MinValue;
        private DateTime hqPlayRequestTime = DateTime.MinValue;
        private readonly HqPlayerClient hqPlayer;
        private readonly DispatcherTimer statsTimer;
        private ulong lastIdleTicks;
        private ulong lastKernelTicks;
        private ulong lastUserTicks;
        private bool cpuBaselineReady;
        private bool isFullScreen;
        private WindowState prevWindowState;
        private WindowStyle prevWindowStyle;
        private ResizeMode prevResizeMode;
        private Rect prevBounds;
        private DispatcherTimer? playTimer;
        private TimeSpan playbackPos;
        private double totalDurationSeconds = 183;
        private readonly List<TimeSpan> lyricTimes = new();
        private readonly List<TextBlock> lyricLines = new();
        private readonly List<Color> lyricOriginalColors = new();
        private readonly List<TextBlock> lyricTrans = new();
        private readonly List<Color> lyricTransOriginalColors = new();
        private readonly List<(TimeSpan time, string main, string trans)> lyricEntries = new();
        private int activeLyricIndex = -1;
        private bool lyricFadeReady;
        private readonly SystemAudioPlayer systemPlayer = new();
        private readonly RemoteControlServer remoteControl = new();
        private DateTime lastResumeSave = DateTime.MinValue;
        private readonly HttpClient httpClient = new();
        private string? lastCapturedUrl;
        private DateTime lastCapturedTime = DateTime.MinValue;
        private int capturedSongCount;
        private readonly QQMusicDownloader qqDownloader = new();
        private readonly QqApiSidecar qqSidecar = new();
        private readonly QqApiClient qqApi = new("http://127.0.0.1:8123");
        private readonly ObservableCollection<QqSongItem> qqSongs = new();
        private readonly ObservableCollection<QqPlaylistItem> qqPlaylists = new();
        private string qqTab = "fav";
        private bool qqViewEnteredOnce;
        private List<QqSongItem> qqSongsFull = new();
        private readonly ObservableCollection<QqHomeCard> qqHomeCards = new();
        private int qqPage = 1;
        private bool qqHasMore;
        private bool qqLoading;
        private bool qrPolling;
        private bool radarHasMore;
        private bool radarLoading;
        private readonly HashSet<string> qqShownMids = new();
        private List<string> cachedMidsList = new();
        private DateTime cachedMidsTime = DateTime.MinValue;
        private readonly HashSet<string> favMidSet = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> favAlbumMidSet = new(StringComparer.OrdinalIgnoreCase);
        private QqSongItem? qqDownloadingSong;
        private long currentPlaylistId;
        private string currentPlaylistName = "";
        private string currentAlbumMid = "";
        private string currentSingerMid = "";
        private string currentSingerName = "";
        private string currentSingerTab = "songs";
        private string currentSearchTab = "song";
        private string qqBackTarget = "home";
        private bool qqSearchLoading;
        private int qqSingerPage = 1;
        private int qqSingerAlbumPage = 1;
        private bool qqSingerAlbumsAllLoaded;
        private bool qqSingerLoading;
        private readonly ObservableCollection<QqAlbumInfo> qqAlbums = new();
        private readonly ObservableCollection<QqAlbumInfo> qqSearchAlbums = new();
        private readonly ObservableCollection<QqSingerInfo> qqSearchSingers = new();
        private readonly ObservableCollection<QqSongItem> qqSearchSongs = new();
        private readonly ObservableCollection<QqSongItem> qqSingerSongs = new();
        private readonly ObservableCollection<QqAlbumInfo> qqSingerAlbums = new();
        private readonly ObservableCollection<QueueTrack> queueTracks = new();
        private readonly List<QqSongInfo> qqSongCache = new();
        private readonly object qqSongLock = new();
        private readonly Dictionary<string, double> qqDownloadProgress = new();
        /// <summary>主页推荐卡片。</summary>
        public sealed class QqHomeCard
        {
            public required string Title { get; init; }
            public string CoverUrl { get; init; } = "";
            public string Sub { get; init; } = "";
            public string Icon { get; init; } = "";
            public string Type { get; init; } = "";   // songlist / newsong
            public string TargetId { get; init; } = "";
        }

        private sealed class QqSongInfo
        {
            public string Songmid { get; set; } = "";
            public string Songname { get; set; } = "";
            public string Singer { get; set; } = "";
        }

        private static string QqInjectScript
        {
            get
            {
                try
                {
                    var path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "qq_inject.js");
                    return File.Exists(path) ? File.ReadAllText(path) : "";
                }
                catch
                {
                    return "";
                }
            }
        }
        private bool scrollAnimating;
        private double scrollStart;
        private double scrollDelta;
        private DateTime scrollStartTime;
        private Border? topSpacer;
        private Border? bottomSpacer;
        private const double MainFont = 15;
        private const double MainFontActive = 18;
        private const double TransFont = 12.5;
        private const double TransFontActive = 14;

        public MainWindow()
        {
            InitializeComponent();
            hqPlayer = new HqPlayerClient(
                string.IsNullOrWhiteSpace(settings.HqAddress) ? "127.0.0.1" : settings.HqAddress.Trim(),
                NormalizeHqPort(settings.HqPort));
            QueueList.ItemsSource = queueTracks;
            ShowView("stream");
            ApplySettings();
            loaded = true;
            ApplyRemoteControl();
            Loaded += (_, _) => Dispatcher.BeginInvoke(new Action(CheckForUpdatesOnStart));
            statsTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
            statsTimer.Tick += UpdateStats;
            statsTimer.Start();
            InitLyrics();
            systemPlayer.PlaybackStopped += () => Dispatcher.BeginInvoke(new Action(async () =>
            {
                // 切歌/手动停止时 Stop() 也会触发本事件，用时间窗区分自然播完
                if ((DateTime.UtcNow - lastPlayStart).TotalMilliseconds < 500) return;
                SyncPlayIcon();
                await PlayNextQueueAsync();
            }));
            SetPlaying(false);
            RestoreQueue();
            SetHqConnected(false);
            _ = TryConnectHqAsync();
            Loaded += OnFirstLoaded;
            QqHomeList.ItemsSource = qqHomeCards;
            QqSongList.ItemsSource = qqSongs;
            QqPlaylistList.ItemsSource = qqPlaylists;
            QqAlbumList.ItemsSource = qqAlbums;
            QqSearchSongList.ItemsSource = qqSearchSongs;
            QqSearchAlbumList.ItemsSource = qqSearchAlbums;
            QqSearchSingerList.ItemsSource = qqSearchSingers;
            QqSingerSongList.ItemsSource = qqSingerSongs;
            QqSingerAlbumList.ItemsSource = qqSingerAlbums;
            if (qqApi.LoadSavedCredential())
                _ = RestoreLoginStateAsync();
        }

        private void OnFirstLoaded(object? sender, RoutedEventArgs e)
        {
            Loaded -= OnFirstLoaded;
            // 等主界面稳定后再检测 HQPlayer，未安装则提醒一次
            _ = Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
            {
                TryAutoStartHqPlayer();
                // 带 --show-hq-dialog 启动参数可强制预览弹窗（调试/效果确认用）
                var forcePreview = Environment.GetCommandLineArgs()
                    .Contains("--show-hq-dialog", StringComparer.OrdinalIgnoreCase);
                if (!forcePreview && (HqInstallationDetector.IsInstalled() || settings.HqMissingShown))
                    return;
                var dlg = new HqMissingDialog { Owner = this };
                dlg.ShowDialog();
                // 正式提醒只弹一次；--show-hq-dialog 预览不写标记，方便反复查看效果
                if (!forcePreview)
                {
                    settings.HqMissingShown = true;
                    SaveSettings();
                }
            }));
        }

        /// <summary>设置开启时，若 HQPlayer 未在运行则按自选路径（或常见路径）拉起。</summary>
        private void TryAutoStartHqPlayer()
        {
            try
            {
                if (!settings.AutoStartHqPlayer)
                    return;
                if (IsHqPlayerRunning())
                    return;
                var exe = ResolveHqPlayerExe();
                if (string.IsNullOrEmpty(exe) || !File.Exists(exe))
                    return;
                Process.Start(new ProcessStartInfo(exe) { UseShellExecute = true });
                LogQqDebug("已自动启动 HQPlayer: " + exe);
            }
            catch (Exception ex)
            {
                LogQqDebug("自动启动 HQPlayer 失败: " + ex.Message);
            }
        }

        private static bool IsHqPlayerRunning()
        {
            foreach (var name in new[] { "HQPlayer6Desktop", "HQPlayer5Desktop", "HQPlayer" })
            {
                if (Process.GetProcessesByName(name).Length > 0)
                    return true;
            }
            return false;
        }

        private string? ResolveHqPlayerExe()
        {
            if (!string.IsNullOrWhiteSpace(settings.HqPlayerExePath) &&
                File.Exists(settings.HqPlayerExePath))
            {
                return settings.HqPlayerExePath.Trim();
            }
            return HqInstallationDetector.LocateExe();
        }

        private static int NormalizeHqPort(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return 4321;
            if (raw.Trim() == "10020") return 4321; // 旧版本默认端口迁移
            return int.TryParse(raw.Trim(), out var n) && n > 0 ? n : 4321;
        }

        private string HqHost =>
            string.IsNullOrWhiteSpace(HqAddressBox?.Text)
                ? settings.HqAddress
                : HqAddressBox.Text.Trim();

        private int HqPort => NormalizeHqPort(HqPortBox?.Text ?? settings.HqPort);

        /// <summary>当前输出是否走 HQPlayer（升频开关打开且输出选择 NAA）。</summary>
        private bool UseHqOutput => hqOn && OutputCombo?.SelectedIndex == 1;

        private static string FormatRate(long hz)
        {
            if (hz >= 1000000) return (hz / 1000000.0).ToString("0.##") + "MHz";
            if (hz >= 1000) return (hz / 1000.0).ToString("0.#") + "kHz";
            return hz + "Hz";
        }

        private void SetHqConnected(bool connected, long activeRate = 0, string activeMode = "")
        {
            if (HqStatusDot == null || HqStatusText == null || NaaStatusText == null) return;
            if (connected)
            {
                HqStatusDot.Fill = (Brush)FindResource("OkBrush");
                HqStatusText.Text = activeRate > 0
                    ? "已连接 · 升频 " + HqModeLabel(activeMode) + " " + FormatRate(activeRate)
                    : "已连接 · 升频";
                NaaStatusText.Text = "NAA · 已连接";
            }
            else
            {
                HqStatusDot.Fill = (Brush)FindResource("TextDimBrush");
                HqStatusText.Text = "未连接";
                NaaStatusText.Text = "NAA · 未连接";
            }
            // 底部 NAA 芯片只在选择了 NAA 输出时出现
            NaaStatusText.Visibility = UseHqOutput ? Visibility.Visible : Visibility.Collapsed;
        }

        private static string HqModeLabel(string activeMode)
        {
            if (string.IsNullOrEmpty(activeMode)) return "";
            if (activeMode.Contains("DSD", StringComparison.OrdinalIgnoreCase)) return "DSD";
            if (activeMode.Contains("PCM", StringComparison.OrdinalIgnoreCase)) return "PCM";
            if (activeMode.Contains("source", StringComparison.OrdinalIgnoreCase)) return "";
            return activeMode.Trim();
        }

        private async Task TryConnectHqAsync()
        {
            if (!hqOn) return;
            // HQPlayer 偶发短暂不响应，启动时最多重试 3 次
            for (var attempt = 0; attempt < 3; attempt++)
            {
                var ok = await Task.Run(() => hqPlayer.GetStatus() != null);
                if (ok)
                {
                    hqPrevState = -1;
                    hqFailCount = 0;
                    LogQqDebug($"HQPlayer 已连接 {HqHost}:{HqPort}");
                    SetHqConnected(true);
                    return;
                }
                await Task.Delay(1500);
            }
            LogQqDebug($"HQPlayer 连接失败 {HqHost}:{HqPort}");
            SetHqConnected(false);
        }

        /// <summary>停止当前输出（系统输出与 HQPlayer 一起停，切换音源/切歌时调用）。</summary>
        private void StopOutput()
        {
            try { systemPlayer.Stop(); } catch { }
            hqPrevState = -1;
            hqIsPlaying = false;
            if (hqPlayer.Connected)
            {
                try { hqPlayer.Stop(); } catch { }
                try { hqPlayer.ClearPlaylist(); } catch { }
            }
        }

        // ------------------------------------------------------------------
        // 播放队列：编辑操作
        // ------------------------------------------------------------------

        private void QueueUp_Click(object sender, RoutedEventArgs e)
        {
            var items = QueueList.SelectedItems.Cast<QueueTrack>()
                .OrderBy(x => queueTracks.IndexOf(x)).ToList();
            if (items.Count == 0) return;
            // 从最靠前的开始逐个上移
            foreach (var item in items)
            {
                int idx = queueTracks.IndexOf(item);
                if (idx <= 0) continue;
                queueTracks.Move(idx, idx - 1);
            }
            RestoreQueueSelection(items);
        }

        private void QueueDown_Click(object sender, RoutedEventArgs e)
        {
            var items = QueueList.SelectedItems.Cast<QueueTrack>()
                .OrderByDescending(x => queueTracks.IndexOf(x)).ToList();
            if (items.Count == 0) return;
            foreach (var item in items)
            {
                int idx = queueTracks.IndexOf(item);
                if (idx >= queueTracks.Count - 1) continue;
                queueTracks.Move(idx, idx + 1);
            }
            RestoreQueueSelection(items);
        }

        private void QueueDelete_Click(object sender, RoutedEventArgs e)
        {
            var items = QueueList.SelectedItems.Cast<QueueTrack>().ToList();
            if (items.Count == 0) return;
            bool removedCurrent = items.Any(x => x.IsCurrent);
            foreach (var item in items)
            {
                queueTracks.Remove(item);
            }
            if (removedCurrent)
            {
                StopPlaybackAndReset();
            }
            UpdateQueueEmptyHint();
        }

        /// <summary>停止播放并让界面回到"未在播放"状态。</summary>
        private void StopPlaybackAndReset()
        {
            lastPlayStart = DateTime.UtcNow;
            StopOutput();
            isPlaying = false;
            hqIsPlaying = false;
            hqPrevState = -1;
            SyncPlayIcon();
            UpdateNowPlayingInfo("未在播放", "", "");
            ShowNoLyrics();
            settings.LastSongPath = null;
            settings.LastSongTitle = null;
            settings.LastSongSinger = null;
            settings.LastSongSource = null;
            settings.LastSongPosition = 0;
            SaveSettings();
            SetStatus("已停止播放");
        }

        private void QueueSelectAll_Click(object sender, RoutedEventArgs e)
        {
            if (QueueSelectAll.IsChecked == true)
            {
                // 全选需要 Extended 模式：未开启选择模式时自动进入（并同步选择模式开关）
                if (QueueList.SelectionMode != SelectionMode.Extended)
                {
                    QueueSelectModeBtn.IsChecked = true;
                    queueSelectMode = true;
                    QueueList.SelectionMode = SelectionMode.Extended;
                }
                QueueList.SelectAll();
            }
            else
            {
                QueueList.UnselectAll();
            }
        }

        private void RestoreQueueSelection(List<QueueTrack> items)
        {
            QueueList.SelectedItems.Clear();
            foreach (var item in items)
            {
                if (queueTracks.Contains(item))
                {
                    QueueList.SelectedItems.Add(item);
                }
            }
            UpdateQueueEmptyHint();
        }

        private void UpdateQueueEmptyHint()
        {
            if (QueueEmptyHint != null)
            {
                QueueEmptyHint.Text = queueTracks.Count == 0 ? "（空）" : $"共 {queueTracks.Count} 首";
            if (loaded) SaveQueueSnapshot();
            }
        }

        private void AddToQueue(QueueTrack track)
        {
            // 同路径已在队列：移到队尾并标为当前
            var existing = queueTracks.FirstOrDefault(x => x.Path == track.Path);
            if (existing != null)
            {
                foreach (var t in queueTracks) t.IsCurrent = false;
                existing.IsCurrent = true;
                queueTracks.Move(queueTracks.IndexOf(existing), queueTracks.Count - 1);
            }
            else
            {
                foreach (var t in queueTracks) t.IsCurrent = false;
                track.IsCurrent = true;
                queueTracks.Add(track);
            }
            UpdateQueueEmptyHint();
            QueueList.ScrollIntoView(queueTracks[^1]);
        }

        /// <summary>把当前播放队列序列化到设置（重启后恢复用）。</summary>
        private void SaveQueueSnapshot(bool force = false)
        {
            try
            {
                var now = DateTime.UtcNow;
                if (!force && (now - lastQueueSave).TotalMilliseconds < 1500) return;
                lastQueueSave = now;
                settings.SavedQueue = queueTracks.Select(t => new AppSettings.SavedQueueItem
                {
                    Title = t.Title,
                    Singer = t.Singer,
                    Source = t.Source,
                    Path = t.Path,
                    QqMid = t.QqMid,
                    AlbumMid = t.AlbumMid,
                    QualityName = t.QualityName,
                    IsCurrent = t.IsCurrent
                }).ToList();
                SaveSettings();
            }
            catch
            {
                // 忽略保存失败
            }
        }

        /// <summary>启动时恢复上次播放队列；无快照时回退到单曲恢复。</summary>
        private void RestoreQueue()
        {
            try
            {
                if (settings.SavedQueue == null || settings.SavedQueue.Count == 0)
                {
                    TryResumeLastSong();
                    return;
                }
                queueTracks.Clear();
                currentQueueIndex = -1;
                foreach (var item in settings.SavedQueue)
                {
                    var track = new QueueTrack
                    {
                        Title = item.Title,
                        Singer = item.Singer,
                        Source = item.Source,
                        Path = item.Path,
                        QqMid = item.QqMid,
                        AlbumMid = item.AlbumMid,
                        QqSong = string.IsNullOrEmpty(item.QqMid)
                            ? null
                            : new QqSongItem
                            {
                                Mid = item.QqMid,
                                Title = item.Title,
                                Singer = item.Singer,
                                AlbumMid = item.AlbumMid
                            },
                        IsCurrent = item.IsCurrent
                    };
                    track.QualityName = item.QualityName;
                    queueTracks.Add(track);
                    if (item.IsCurrent)
                        currentQueueIndex = queueTracks.Count - 1;
                }
                UpdateQueueEmptyHint();

                if (currentQueueIndex >= 0 && currentQueueIndex < queueTracks.Count)
                {
                    var cur = queueTracks[currentQueueIndex];
                    if (settings.ResumeMode >= 2 && !string.IsNullOrEmpty(cur.Path) && File.Exists(cur.Path))
                    {
                        // 缓存文件仍在：直接恢复播放
                        if (!UseHqOutput) systemPlayer.Load(cur.Path);
                        isPlaying = false;
                        SyncPlayIcon();
                        UpdateNowPlayingInfo(cur.Title, cur.Singer, cur.Source);
                        ShowNoLyrics();
                        if (cur.QqSong != null)
                            _ = LoadQqSongMetaAsync(cur.QqSong);
                        if (!UseHqOutput && settings.LastSongPosition > 0)
                            systemPlayer.SeekTo(TimeSpan.FromSeconds(settings.LastSongPosition));
                        SetStatus($"已恢复播放队列（{queueTracks.Count} 首）");
                    }
                    else if (settings.ResumeMode >= 1)
                    {
                        // 恢复显示上次歌曲（未加载时点击播放自动下载）
                        SyncPlayIcon();
                        UpdateNowPlayingInfo(cur.Title, cur.Singer, cur.Source);
                        ShowNoLyrics();
                        if (cur.QqSong != null)
                            _ = LoadQqSongMetaAsync(cur.QqSong);
                        if (!UseHqOutput && !string.IsNullOrEmpty(cur.Path) && File.Exists(cur.Path))
                            systemPlayer.Load(cur.Path);
                        SetStatus($"播放队列已恢复（{queueTracks.Count} 首），点击播放");
                    }
                }
            }
            catch (Exception ex)
            {
                LogQqDebug("恢复队列失败: " + ex.Message);
            }
        }
        private void TryResumeLastSong()
        {
            try
            {
                if (settings.ResumeMode <= 0) return;
                if (string.IsNullOrEmpty(settings.LastSongPath)) return;
                if (!File.Exists(settings.LastSongPath)) return;

                if (!UseHqOutput) systemPlayer.Load(settings.LastSongPath);
                isPlaying = false;
                SyncPlayIcon();
                var title = settings.LastSongTitle ?? Path.GetFileNameWithoutExtension(settings.LastSongPath);
                UpdateNowPlayingInfo(title,
                    settings.LastSongSinger ?? "",
                    settings.LastSongSource ?? "本地文件");
                ShowNoLyrics();
                AddToQueue(new QueueTrack
                {
                    Title = title,
                    Singer = settings.LastSongSinger ?? "",
                    Source = settings.LastSongSource ?? "本地文件",
                    Path = settings.LastSongPath,
                    IsCurrent = true
                });

                if (!UseHqOutput && settings.ResumeMode >= 2 && settings.LastSongPosition > 0)
                {
                    systemPlayer.SeekTo(TimeSpan.FromSeconds(settings.LastSongPosition));
                }
                SetStatus("已恢复上次播放（点击播放继续）");
            }
            catch
            {
                // 文件损坏/不可读则忽略，保持"未在播放"
            }
        }

        // ------------------------------------------------------------------
        // 导航
        // ------------------------------------------------------------------

        private void Nav_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn)
            {
                ShowView((string)btn.Tag);
            }
        }

        private void ShowView(string tag)
        {
            bool stream = tag == "stream";
            bool qqSource = stream && IsQqSource();
            QueuePanel.Visibility = tag == "queue" ? Visibility.Visible : Visibility.Collapsed;
            QqPanel.Visibility = qqSource ? Visibility.Visible : Visibility.Collapsed;
            StreamPanel.Visibility = stream && !qqSource ? Visibility.Visible : Visibility.Collapsed;
            SettingsPanel.Visibility = tag == "settings" ? Visibility.Visible : Visibility.Collapsed;
            ViewTitle.Text = tag switch
            {
                "stream" => qqSource ? "QQ音乐" : "流媒体",
                "settings" => "设置",
                _ => "播放队列"
            };
            if (stream)
            {
                if (qqSource)
                    _ = EnterQqViewAsync();
                else
                    _ = EnsureBrowserAsync();
            }
            UpdateNavHighlight(tag);
            UpdateJumpButtons();
        }

        private bool IsQqSource() =>
            SourceCombo.SelectedItem is ComboBoxItem item && item.Tag is string tag && tag == "https://y.qq.com";

        private void UpdateNavHighlight(string tag)
        {
            foreach (Button btn in new[] { NavQueue, NavStream, NavSettings })
            {
                bool selected = (string)btn.Tag == tag;
                btn.Foreground = selected
                    ? (Brush)FindResource("AccentBrush")
                    : (Brush)FindResource("TextSecondaryBrush");
                btn.FontWeight = selected ? FontWeights.SemiBold : FontWeights.Normal;
                btn.Background = selected
                    ? (Brush)FindResource("AccentDimBrush")
                    : Brushes.Transparent;
            }
        }

        // ------------------------------------------------------------------
        // 设置：加载 / 保存
        // ------------------------------------------------------------------

        private void ApplySettings()
        {
            if (settings.SourceIndex >= 0 && settings.SourceIndex < SourceCombo.Items.Count)
            {
                SourceCombo.SelectedIndex = settings.SourceIndex;
            }
            if (settings.OutputIndex >= 0 && settings.OutputIndex < OutputCombo.Items.Count)
            {
                OutputCombo.SelectedIndex = settings.OutputIndex;
            }

            switch (settings.BufferOption)
            {
                case 0: Buf256.IsChecked = true; break;
                case 2: Buf1G.IsChecked = true; break;
                case 3: BufCustom.IsChecked = true; break;
                default: Buf512.IsChecked = true; break;
            }
            BufCustomValue.Text = settings.CustomBufferMb.ToString();
            BufCount.IsChecked = settings.CacheCountMode == 1;
            BufCountValue.Text = settings.CacheCount.ToString();
            CachePathBox.Text = settings.ResolvedCachePath;
            PreCacheBox.Text = settings.PrecacheCount.ToString();
            AutoStartCheck.IsChecked = settings.AutoStart;
            LyricOffsetBox.Text = settings.LyricOffsetMs.ToString();

            hqOn = settings.HqEnabled;
            HqToggle.Content = hqOn ? "升频：开" : "升频：关";
            HqToggle.Background = hqOn
                ? (Brush)FindResource("OkBrush")
                : (Brush)FindResource("TextDimBrush");
            if (settings.HqPort.Trim() == "10020") settings.HqPort = "4321"; // 旧默认端口迁移
            HqAddressBox.Text = settings.HqAddress;
            HqPortBox.Text = settings.HqPort;
            HqAutoStartCheck.IsChecked = settings.AutoStartHqPlayer;
            HqExePathBox.Text = settings.HqPlayerExePath ?? "";
            RemoteCtrlCheck.IsChecked = settings.RemoteControlEnabled;
            if (settings.Language == 1) LangZh.IsChecked = true;
            else if (settings.Language == 2) LangEn.IsChecked = true;
            else LangAuto.IsChecked = true;
            UpdateIntervalCombo.SelectedIndex = Math.Clamp(settings.UpdateCheckInterval, 0, 4);

            ShowMemCheck.IsChecked = settings.ShowMem;
            ShowCpuCheck.IsChecked = settings.ShowCpu;
            UpdateStatsVisibility();

            switch (settings.ResumeMode)
            {
                case 0: ResumeOff.IsChecked = true; break;
                case 1: ResumeSong.IsChecked = true; break;
                default: ResumeFull.IsChecked = true; break;
            }
            if (settings.QqApiQualityIndex >= 0 && settings.QqApiQualityIndex < QqApiQualityCombo.Items.Count)
            {
                QqApiQualityCombo.SelectedIndex = settings.QqApiQualityIndex;
            }
            if (settings.QqQualityIndex >= 0 && settings.QqQualityIndex < QqQualityCombo.Items.Count)
            {
                QqQualityCombo.SelectedIndex = settings.QqQualityIndex;
            }
        }

        private void SaveSettings()
        {
            settings.SourceIndex = SourceCombo.SelectedIndex;
            settings.OutputIndex = OutputCombo.SelectedIndex;
            settings.BufferOption = Buf256.IsChecked == true ? 0
                : Buf1G.IsChecked == true ? 2
                : BufCustom.IsChecked == true ? 3 : 1;
            settings.CustomBufferMb = int.TryParse(BufCustomValue.Text, out var mb) ? mb : settings.CustomBufferMb;
            settings.CacheCountMode = BufCount.IsChecked == true ? 1 : 0;
            settings.CacheCount = int.TryParse(BufCountValue.Text, out var cnt) && cnt > 0 ? cnt : 5;
            settings.CachePath = CachePathBox.Text.Trim();
            settings.AutoStart = AutoStartCheck.IsChecked == true;
            settings.LyricOffsetMs = int.TryParse(LyricOffsetBox.Text, out var lo) ? lo : 400;
            settings.HqEnabled = hqOn;
            settings.HqAddress = HqAddressBox.Text.Trim();
            settings.HqPort = HqPortBox.Text.Trim();
            settings.AutoStartHqPlayer = HqAutoStartCheck.IsChecked == true;
            settings.HqPlayerExePath = HqExePathBox.Text.Trim();
            settings.RemoteControlEnabled = RemoteCtrlCheck.IsChecked == true;
            settings.ShowMem = ShowMemCheck.IsChecked == true;
            settings.ShowCpu = ShowCpuCheck.IsChecked == true;
            settings.ResumeMode = ResumeOff.IsChecked == true ? 0
                : ResumeSong.IsChecked == true ? 1 : 2;
            settings.QqApiQualityIndex = QqApiQualityCombo.SelectedIndex;
            settings.QqQualityIndex = QqQualityCombo.SelectedIndex;
            settings.Save();
        }

        private void ResumeMode_Changed(object sender, RoutedEventArgs e)
        {
            if (loaded) SaveSettings();
        }

        private void SourceCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!IsLoaded) return; // InitializeComponent 期间的默认选中不处理
            if (loaded) SaveSettings();
            if (SourceCombo.SelectedItem is ComboBoxItem item && item.Tag is string tag)
            {
                if (tag == "queue")
                {
                    ShowView("queue");
                }
                else
                {
                    ShowView("stream");
                }
            }
        }

        private void OutputCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!loaded) return;
            StopOutput();
            if (UseHqOutput) _ = TryConnectHqAsync();
            SaveSettings();
        }

        private void QqQualityCombo_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (loaded) SaveSettings();
        }

        private void BufferOption_Changed(object sender, RoutedEventArgs e)
        {
            if (loaded) SaveSettings();
        }

        private void BufCustom_Changed(object sender, TextChangedEventArgs e)
        {
            if (loaded) SaveSettings();
        }

        private void BufCount_Changed(object sender, TextChangedEventArgs e)
        {
            if (loaded) SaveSettings();
        }

        /// <summary>开机自启：写/删 HKCU 注册表 Run 项（优先 Launcher，缺失时用主程序）。</summary>
        private void AutoStart_Changed(object sender, RoutedEventArgs e)
        {
            try
            {
                using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                    @"Software\Microsoft\Windows\CurrentVersion\Run", writable: true);
                if (AutoStartCheck.IsChecked == true)
                {
                    var launcher = Path.Combine(AppContext.BaseDirectory, "AuralDesk.Launcher.exe");
                    var exe = File.Exists(launcher)
                        ? launcher
                        : Path.Combine(AppContext.BaseDirectory, "AuralDesk.exe");
                    key?.SetValue("AuralDesk", "\"" + exe + "\"");
                }
                else
                {
                    key?.DeleteValue("AuralDesk", throwOnMissingValue: false);
                }
            }
            catch
            {
                // 注册表写入失败不阻塞
            }
            if (loaded) SaveSettings();
        }

        private void HqFields_Changed(object sender, TextChangedEventArgs e)
        {
            if (!loaded) return;
            hqPlayer.UpdateEndpoint(HqHost, HqPort);
            if (hqOn) _ = TryConnectHqAsync();
            SaveSettings();
        }

        private void HqAutoStart_Changed(object sender, RoutedEventArgs e)
        {
            if (loaded) SaveSettings();
        }

        private void HqExePath_Changed(object sender, TextChangedEventArgs e)
        {
            if (loaded) SaveSettings();
        }

        private void HqLocate_Click(object sender, RoutedEventArgs e)
        {
            var exe = HqInstallationDetector.LocateExe();
            if (!string.IsNullOrEmpty(exe))
            {
                HqExePathBox.Text = exe;
                SaveSettings();
                SetStatus("已自动定位 HQPlayer：" + exe);
            }
            else
            {
                SetStatus("未自动定位到 HQPlayer，请手动选择，或先安装 HQPlayer");
            }
        }

        private void HqExeBrowse_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Title = "选择 HQPlayer 主程序",
                Filter = "可执行程序 (*.exe)|*.exe",
                CheckFileExists = true
            };
            if (!string.IsNullOrWhiteSpace(HqExePathBox.Text))
                dlg.InitialDirectory = Path.GetDirectoryName(HqExePathBox.Text.Trim());
            if (dlg.ShowDialog(this) == true)
            {
                HqExePathBox.Text = dlg.FileName;
                SaveSettings();
            }
        }

        // ------------------------------------------------------------------
        // 局域网遥控
        // ------------------------------------------------------------------

        private void RemoteCtrl_Changed(object sender, RoutedEventArgs e)
        {
            if (!loaded) return;
            ApplyRemoteControl();
            SaveSettings();
        }

        private void ApplyRemoteControl()
        {
            if (RemoteCtrlCheck.IsChecked == true)
            {
                remoteControl.Start(
                    RemoteGetStatus, RemoteControlAction, RemoteSeek,
                    RemoteGetQueue, RemoteGetLyric, RemoteQqState, RemoteQqAction);
                LogQqDebug("局域网遥控已开启");
            }
            else
            {
                remoteControl.Stop();
                LogQqDebug("局域网遥控已关闭");
            }
            UpdateRemoteStatusText();
        }

        private void UpdateRemoteStatusText()
        {
            if (RemoteCtrlAddress == null) return;
            if (RemoteCtrlCheck.IsChecked == true)
            {
                var ip = GetLocalIpAddress();
                RemoteCtrlAddress.Text = string.IsNullOrEmpty(ip)
                    ? $"http://<局域网IP>:{RemoteControlServer.HttpPort}（未检测到局域网地址）"
                    : $"http://{ip}:{RemoteControlServer.HttpPort}";
            }
            else
            {
                RemoteCtrlAddress.Text = "未开启";
            }
        }

        private static string? GetLocalIpAddress()
        {
            try
            {
                foreach (var ni in System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (ni.OperationalStatus != System.Net.NetworkInformation.OperationalStatus.Up)
                        continue;
                    if (ni.NetworkInterfaceType == System.Net.NetworkInformation.NetworkInterfaceType.Loopback)
                        continue;
                    foreach (var ua in ni.GetIPProperties().UnicastAddresses)
                    {
                        if (ua.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                        {
                            var ip = ua.Address.ToString();
                            if (!ip.StartsWith("169.254", StringComparison.Ordinal))
                                return ip;
                        }
                    }
                }
            }
            catch { }
            return null;
        }

        private RemoteSongInfo? RemoteGetStatus()
        {
            return Dispatcher.Invoke(() =>
            {
                var cur = currentQueueIndex >= 0 && currentQueueIndex < queueTracks.Count
                    ? queueTracks[currentQueueIndex]
                    : null;
                double pos, len;
                if (UseHqOutput)
                {
                    pos = playbackPos.TotalSeconds;
                    len = totalDurationSeconds;
                }
                else if (systemPlayer.HasFile)
                {
                    pos = systemPlayer.Position.TotalSeconds;
                    len = systemPlayer.Length.TotalSeconds;
                }
                else
                {
                    pos = playbackPos.TotalSeconds;
                    len = totalDurationSeconds;
                }
                return new RemoteSongInfo
                {
                    Title = cur?.Title ?? "未在播放",
                    Singer = cur?.Singer ?? "",
                    Source = cur?.Source ?? "",
                    Mid = cur?.QqMid ?? "",
                    Position = pos,
                    Length = len,
                    Playing = NowPlaying,
                    Lang = Lang.IsEnglish ? "en" : "zh"
                };
            });
        }

        private void RemoteControlAction(string action)
        {
            _ = Dispatcher.BeginInvoke(new Action(() =>
            {
                switch (action)
                {
                    case "toggle":
                        Play_Click(null!, new RoutedEventArgs());
                        break;
                    case "next":
                        Next_Click(null!, new RoutedEventArgs());
                        break;
                    case "prev":
                        Prev_Click(null!, new RoutedEventArgs());
                        break;
                }
            }));
        }

        private void RemoteSeek(double position)
        {
            _ = Dispatcher.BeginInvoke(new Action(() =>
            {
                if (UseHqOutput)
                {
                    try { hqPlayer.Seek(position); } catch { }
                }
                else if (systemPlayer.HasFile)
                {
                    systemPlayer.SeekTo(TimeSpan.FromSeconds(position));
                }
            }));
        }

        private List<RemoteQueueEntry> RemoteGetQueue()
        {
            return Dispatcher.Invoke(() =>
            {
                var list = new List<RemoteQueueEntry>();
                for (var i = 0; i < queueTracks.Count; i++)
                {
                    list.Add(new RemoteQueueEntry
                    {
                        Title = queueTracks[i].Title,
                        Singer = queueTracks[i].Singer,
                        IsCurrent = i == currentQueueIndex
                    });
                }
                return list;
            });
        }

        private RemoteLyricData? RemoteGetLyric()
        {
            return Dispatcher.Invoke(() =>
            {
                if (lyricEntries.Count == 0)
                    return null;
                var data = new RemoteLyricData();
                foreach (var (time, main, trans) in lyricEntries)
                {
                    data.Lines.Add(new RemoteLyricLine
                    {
                        Time = time.TotalSeconds,
                        Main = main,
                        Trans = trans
                    });
                }
                data.ActiveIndex = activeLyricIndex;
                return data;
            });
        }

        /// <summary>手机端远控：流媒体 + 播放队列完整状态镜像（JSON 字符串）。</summary>
        private string RemoteQqState(int qstart, int qcount)
        {
            return Dispatcher.Invoke(() =>
            {
                string view = QqSearchOverlay?.Visibility == Visibility.Visible ? "searchOnline"
                    : QqSingerView?.Visibility == Visibility.Visible ? "singer"
                    : QqAlbumScroller?.Visibility == Visibility.Visible ? "albums"
                    : QqHomeScroller?.Visibility == Visibility.Visible ? "home"
                    : QqPlaylistScroller?.Visibility == Visibility.Visible ? "playlists"
                    : QqSongScroller?.Visibility == Visibility.Visible ? "songs"
                    : "home";
                // 缓存标记：扫目录有 IO 开销，结果缓存 3 秒避免每次轮询阻塞 UI
                var cachedMids = GetCachedMids();
                var songs = qqSongs.Select(s => new
                {
                    mid = s.Mid,
                    title = s.Title,
                    singer = s.Singer,
                    duration = s.Duration,
                    albumMid = s.AlbumMid,
                    cached = cachedMids.Contains(s.Mid),
                    isFav = s.IsFav,
                    download = GetQqDownloadProgress(s.Mid)
                });
                var playlists = qqPlaylists.Select(p => new { id = p.Id, name = p.Name, info = p.Info });
                var homeCards = qqHomeCards.Select(c => new { type = c.Type, title = c.Title, sub = c.Sub });
                var searchSongs = qqSearchSongs.Select(s => new
                {
                    mid = s.Mid, title = s.Title, singer = s.Singer, duration = s.Duration, isFav = s.IsFav
                });
                var searchAlbums = qqSearchAlbums.Select(a => new
                {
                    albumMid = a.AlbumMid, name = a.Name, singer = a.Singer, date = a.Date,
                    coverUrl = a.CoverUrl, isFav = a.IsFav
                });
                var searchSingers = qqSearchSingers.Select(s => new
                {
                    singerMid = s.SingerMid, name = s.Name, pic = s.Pic,
                    songNum = s.SongNum, albumNum = s.AlbumNum
                });
                var singerSongs = qqSingerSongs.Select(s => new
                {
                    mid = s.Mid, title = s.Title, singer = s.Singer, duration = s.Duration, album = s.Album,
                    isFav = s.IsFav
                });
                var singerAlbums = qqSingerAlbums.Select(a => new
                {
                    albumMid = a.AlbumMid, name = a.Name, date = a.Date, coverUrl = a.CoverUrl, isFav = a.IsFav
                });
                var favAlbums = qqAlbums.Select(a => new
                {
                    albumMid = a.AlbumMid, name = a.Name, singer = a.Singer, date = a.Date,
                    coverUrl = a.CoverUrl, isFav = a.IsFav
                });
                var queue = new List<object>();
                for (var i = qstart; i < queueTracks.Count && i < qstart + qcount; i++)
                {
                    queue.Add(new
                    {
                        idx = i,
                        mid = queueTracks[i].QqMid,
                        title = queueTracks[i].Title,
                        singer = queueTracks[i].Singer,
                        isCurrent = i == currentQueueIndex,
                        isFav = queueTracks[i].IsFav,
                        cache = queueTracks[i].CacheState,
                        cached = !string.IsNullOrEmpty(queueTracks[i].QqMid) &&
                                 cachedMids.Contains(queueTracks[i].QqMid)
                    });
                }
                var cur = currentQueueIndex >= 0 && currentQueueIndex < queueTracks.Count
                    ? queueTracks[currentQueueIndex]
                    : null;
                double pos, len;
                if (UseHqOutput)
                {
                    pos = playbackPos.TotalSeconds;
                    len = totalDurationSeconds;
                }
                else if (systemPlayer.HasFile)
                {
                    pos = systemPlayer.Position.TotalSeconds;
                    len = systemPlayer.Length.TotalSeconds;
                }
                else
                {
                    pos = playbackPos.TotalSeconds;
                    len = totalDurationSeconds;
                }
                return JsonSerializer.Serialize(new
                {
                    view,
                    qqTab,
                    listTitle = QqListTitle?.Text ?? "",
                    songs,
                    playlists,
                    homeCards,
                    searchSongs,
                    searchAlbums,
                    searchSingers,
                    searchTab = currentSearchTab,
                    singerName = QqSingerName?.Text ?? "",
                    singerDesc = QqSingerDescText?.Text ?? "",
                    singerTab = currentSingerTab,
                    singerSongs,
                    singerAlbums,
                    favAlbums,
                    queue,
                    currentIndex = currentQueueIndex,
                    playing = NowPlaying,
                    curTitle = cur?.Title ?? Lang.T("notPlaying"),
                    curSinger = cur?.Singer ?? "",
                    curSource = cur?.Source ?? "",
                    position = pos,
                    length = len,
                    queueTotal = queueTracks.Count,
                    queueMore = qstart + qcount < queueTracks.Count,
                    lang = Lang.IsEnglish ? "en" : "zh"
                });
            });
        }

        private HashSet<string> GetCachedMids()
        {
            if ((DateTime.UtcNow - cachedMidsTime).TotalSeconds > 3 || cachedMidsList.Count == 0)
            {
                var list = new List<string>();
                try
                {
                    foreach (var f in Directory.GetFiles(CacheDir))
                    {
                        var name = Path.GetFileName(f);
                        var idx = name.IndexOf('_');
                        if (idx > 0) list.Add(name[..idx]);
                    }
                }
                catch { }
                cachedMidsList = list;
                cachedMidsTime = DateTime.UtcNow;
            }
            return new HashSet<string>(cachedMidsList, StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>当前歌曲下载进度（0~1），未在下载返回 -1。</summary>
        private double GetQqDownloadProgress(string mid)
        {
            lock (qqDownloadProgress)
                return qqDownloadProgress.TryGetValue(mid, out var p) ? p : -1;
        }

        /// <summary>手机端远控动作分发（在主线程执行）。</summary>
        private void RemoteQqAction(string json)
        {
            _ = Dispatcher.BeginInvoke(new Action(() =>
            {
                try
                {
                    using var doc = JsonDocument.Parse(json);
                    var r = doc.RootElement;
                    var action = r.TryGetProperty("action", out var a) ? a.GetString() ?? "" : "";
                    LogQqDebug("远控动作: " + json);
                    switch (action)
                    {
                        case "home":
                            _ = ShowQqHomeAsync();
                            break;
                        case "playlists":
                            _ = LoadQqPlaylistsAsync();
                            break;
                        case "openPlaylist":
                            if (r.TryGetProperty("id", out var pid) && long.TryParse(pid.GetString(), out var plId))
                            {
                                qqTab = "playlists";
                                currentPlaylistId = plId;
                                currentPlaylistName = r.TryGetProperty("name", out var pn) ? pn.GetString() ?? "歌单" : "歌单";
                                QqHomeScroller.Visibility = Visibility.Collapsed;
                                QqPlaylistScroller.Visibility = Visibility.Collapsed;
                                QqSongScroller.Visibility = Visibility.Visible;
                                QqBackToPlaylists.Visibility = Visibility.Visible;
                                QqEmptyHint.Visibility = Visibility.Collapsed;
                                qqSongs.Clear();
                                _ = LoadQqPlaylistSongsAsync(reset: true);
                            }
                            break;
                        case "fav":
                            qqTab = "fav";
                            QqHomeScroller.Visibility = Visibility.Collapsed;
                            QqPlaylistScroller.Visibility = Visibility.Collapsed;
                            QqSongScroller.Visibility = Visibility.Visible;
                            QqBackToPlaylists.Visibility = Visibility.Collapsed;
                            qqSongs.Clear();
                            qqShownMids.Clear();
                            QqEmptyHint.Visibility = Visibility.Collapsed;
                            _ = LoadQqFavAsync(reset: true);
                            break;
                        case "daily30":
                            QqHomeScroller.Visibility = Visibility.Collapsed;
                            QqPlaylistScroller.Visibility = Visibility.Collapsed;
                            QqSongScroller.Visibility = Visibility.Visible;
                            QqBackToPlaylists.Visibility = Visibility.Collapsed;
                            QqEmptyHint.Visibility = Visibility.Collapsed;
                            _ = LoadDaily30Async();
                            break;
                        case "radar":
                            QqHomeScroller.Visibility = Visibility.Collapsed;
                            QqPlaylistScroller.Visibility = Visibility.Collapsed;
                            QqSongScroller.Visibility = Visibility.Visible;
                            QqBackToPlaylists.Visibility = Visibility.Collapsed;
                            QqEmptyHint.Visibility = Visibility.Collapsed;
                            _ = EnterRadarModeAsync();
                            break;
                        case "search":
                            if (r.TryGetProperty("keyword", out var kw))
                            {
                                QqSearchBox.Text = kw.GetString() ?? "";
                                _ = DoQqSearchAsync(reset: true);
                            }
                            break;
                        case "searchOnline":
                            if (r.TryGetProperty("keyword", out var kw2))
                            {
                                qqBackTarget = "home"; // 手机端从主页发起搜索，返回=主页
                                QqOnlineSearchBox.Text = kw2.GetString() ?? "";
                                QqSearchOverlay.Visibility = Visibility.Visible;
                                ShowView("stream"); // 确保电脑端切到流媒体页显示搜索弹窗
                                _ = DoQqOnlineSearchAsync();
                            }
                            break;
                        case "searchTab":
                            if (r.TryGetProperty("tab", out var stEl))
                                ShowQqSearchTab(stEl.GetString() ?? "song");
                            break;
                        case "openAlbum":
                            if (r.TryGetProperty("albumMid", out var amEl))
                            {
                                var album = new QqAlbumInfo
                                {
                                    AlbumMid = amEl.GetString() ?? "",
                                    Name = r.TryGetProperty("name", out var anEl) ? anEl.GetString() ?? "专辑" : "专辑"
                                };
                                if (album.AlbumMid.Length > 0)
                                    _ = OpenAlbumAsync(album);
                            }
                            break;
                        case "openSinger":
                            if (r.TryGetProperty("singerMid", out var smEl))
                            {
                                var singer = new QqSingerInfo
                                {
                                    SingerMid = smEl.GetString() ?? "",
                                    Name = r.TryGetProperty("name", out var snEl) ? snEl.GetString() ?? "歌手" : "歌手"
                                };
                                if (singer.SingerMid.Length > 0)
                                    _ = OpenSingerAsync(singer);
                            }
                            break;
                        case "singerTab":
                            if (r.TryGetProperty("tab", out var sgtEl) && currentSingerMid.Length > 0)
                            {
                                var tab = sgtEl.GetString() ?? "songs";
                                currentSingerTab = tab;
                                QqSingerFilterBox.Text = "";
                                QqSingerSongList.Visibility = tab == "songs" ? Visibility.Visible : Visibility.Collapsed;
                                QqSingerAlbumList.Visibility = tab == "albums" ? Visibility.Visible : Visibility.Collapsed;
                                if (tab == "songs" && qqSingerSongs.Count == 0)
                                    _ = LoadSingerSongsAsync(reset: true);
                                else if (tab == "albums" && qqSingerAlbums.Count == 0)
                                    _ = LoadSingerAlbumsAsync(reset: true);
                            }
                            break;
                        case "favAlbums":
                            QqHomeScroller.Visibility = Visibility.Collapsed;
                            QqPlaylistScroller.Visibility = Visibility.Collapsed;
                            QqSongScroller.Visibility = Visibility.Collapsed;
                            QqSingerView.Visibility = Visibility.Collapsed;
                            QqSearchOverlay.Visibility = Visibility.Collapsed;
                            QqAlbumScroller.Visibility = Visibility.Visible;
                            QqListTitle.Text = "收藏的专辑";
                            _ = LoadQqFavAlbumsAsync();
                            break;
                        case "loadMore":
                            _ = LoadQqNextPageAsync();
                            break;
                        case "random":
                            RandomPlay_Click(null!, new RoutedEventArgs());
                            break;
                        case "play":
                            if (r.TryGetProperty("mid", out var midEl))
                            {
                                var mid = midEl.GetString() ?? "";
                                var song = qqSongs.FirstOrDefault(s => s.Mid == mid);
                                if (song == null)
                                    song = qqSearchSongs.FirstOrDefault(s => s.Mid == mid);
                                if (song == null)
                                    song = qqSingerSongs.FirstOrDefault(s => s.Mid == mid);
                                if (song != null)
                                {
                                    if (qqSingerSongs.Contains(song))
                                        _ = PlayQqSongWithPlaylistAsync(song, qqSingerSongs.ToList());
                                    else if (qqSearchSongs.Contains(song))
                                        _ = PlayQqSongWithPlaylistAsync(song, qqSearchSongs.ToList());
                                    else
                                        _ = PlayQqSongWithPlaylistAsync(song);
                                }
                            }
                            break;
                        case "toggleFav":
                            if (r.TryGetProperty("mid", out var favMidEl))
                            {
                                var favMid = favMidEl.GetString() ?? "";
                                var favSong = qqSongs.FirstOrDefault(s => s.Mid == favMid);
                                if (favSong == null)
                                    favSong = qqSearchSongs.FirstOrDefault(s => s.Mid == favMid);
                                if (favSong == null)
                                    favSong = qqSingerSongs.FirstOrDefault(s => s.Mid == favMid);
                                if (favSong != null)
                                    _ = ToggleFavAsync(favSong);
                                else
                                {
                                    var favTrack = queueTracks.FirstOrDefault(t => t.QqMid == favMid);
                                    if (favTrack != null)
                                        _ = ToggleQueueFavAsync(favTrack);
                                }
                            }
                            break;
                        case "toggleFavAlbum":
                            if (r.TryGetProperty("albumMid", out var favAlEl))
                            {
                                var favAlMid = favAlEl.GetString() ?? "";
                                var favAlbum = qqAlbums.FirstOrDefault(a => a.AlbumMid == favAlMid);
                                if (favAlbum == null)
                                    favAlbum = qqSearchAlbums.FirstOrDefault(a => a.AlbumMid == favAlMid);
                                if (favAlbum == null)
                                    favAlbum = qqSingerAlbums.FirstOrDefault(a => a.AlbumMid == favAlMid);
                                if (favAlbum != null)
                                    _ = ToggleFavAlbumAsync(favAlbum);
                            }
                            break;
                        case "queueUp":
                        case "queueDown":
                            if (r.TryGetProperty("index", out var idxEl) && int.TryParse(idxEl.GetString(), out var idx))
                            {
                                if (action == "queueUp" && idx > 0 && idx < queueTracks.Count)
                                    queueTracks.Move(idx, idx - 1);
                                else if (action == "queueDown" && idx >= 0 && idx < queueTracks.Count - 1)
                                    queueTracks.Move(idx, idx + 1);
                            }
                            break;
                        case "queueDelete":
                            if (r.TryGetProperty("index", out var dIdx) && int.TryParse(dIdx.GetString(), out var delIdx)
                                && delIdx >= 0 && delIdx < queueTracks.Count)
                            {
                                queueTracks.RemoveAt(delIdx);
                                if (currentQueueIndex >= delIdx && currentQueueIndex > 0)
                                    currentQueueIndex--;
                            }
                            break;
                        case "queuePlay":
                            if (r.TryGetProperty("index", out var qIdx) && int.TryParse(qIdx.GetString(), out var playIdx)
                                && playIdx >= 0 && playIdx < queueTracks.Count)
                            {
                                currentQueueIndex = playIdx;
                                _ = PlayCurrentQueueAsync();
                            }
                            break;
                        case "back":
                            QqBackBtn_Click(null!, new RoutedEventArgs());
                            break;
                    }
                }
                catch (Exception ex)
                {
                    LogQqDebug("远控动作执行异常: " + ex.Message);
                }
            }));
        }

        private void StatsCheck_Changed(object sender, RoutedEventArgs e)
        {
            UpdateStatsVisibility();
            if (loaded) SaveSettings();
        }

        private void UpdateStatsVisibility()
        {
            bool showMem = ShowMemCheck.IsChecked == true;
            bool showCpu = ShowCpuCheck.IsChecked == true;
            MemRow.Visibility = showMem ? Visibility.Visible : Visibility.Collapsed;
            CpuRow.Visibility = showCpu ? Visibility.Visible : Visibility.Collapsed;
            SystemStatsCard.Visibility = (showMem || showCpu)
                ? Visibility.Visible
                : Visibility.Collapsed;
        }

        // ------------------------------------------------------------------
        // 播放控制
        // ------------------------------------------------------------------

        private async void Play_Click(object sender, RoutedEventArgs e)
        {
            if (UseHqOutput)
            {
                await HqPlayToggleAsync();
                return;
            }
            if (systemPlayer.HasFile)
            {
                systemPlayer.Toggle();
                SyncPlayIcon();
            }
            else if (currentQueueIndex >= 0 && currentQueueIndex < queueTracks.Count)
            {
                // 恢复的队列/尚未加载的当前项：点击播放走队列（QQ 歌会自动下载后播放）
                _ = PlayCurrentQueueAsync();
            }
            else
            {
                SetPlaying(!isPlaying);
            }
        }

        private async Task HqPlayToggleAsync()
        {
            var status = await Task.Run(() => hqPlayer.GetStatus());
            if (status != null && status.State == 2)
            {
                hqPlayer.Pause();
                hqIsPlaying = false;
            }
            else if (status != null)
            {
                var ok = await Task.Run(() => hqPlayer.Play());
                if (ok)
                {
                    hqIsPlaying = true;
                    lastHqPoll = DateTime.MinValue;
                }
                else if (currentQueueIndex >= 0 && currentQueueIndex < queueTracks.Count)
                {
                    _ = PlayCurrentQueueAsync();
                }
            }
            else if (currentQueueIndex >= 0 && currentQueueIndex < queueTracks.Count)
            {
                _ = PlayCurrentQueueAsync();
            }
            SyncPlayIcon();
        }

        private void OverlayPlay_Click(object sender, RoutedEventArgs e)
        {
            Play_Click(sender, e);
        }

        private void SyncPlayIcon()
        {
            bool playing = NowPlaying;
            PlayBtn.Content = playing ? "\uE769" : "\uE768";
            OverlayPlayBtn.Content = playing ? "\uE769" : "\uE768";
        }

        private bool NowPlaying
        {
            get
            {
                if (UseHqOutput) return hqIsPlaying;
                return systemPlayer.HasFile ? systemPlayer.IsPlaying : isPlaying;
            }
        }

        private void SetPlaying(bool playing)
        {
            if (playing && activeLyricIndex < 0)
            {
                ResetPlayback();
            }
            isPlaying = playing;
            PlayBtn.Content = isPlaying ? "\uE769" : "\uE768";     // pause / play
            OverlayPlayBtn.Content = isPlaying ? "\uE769" : "\uE768";
            UpdateJumpButtons();
        }

        private async void Prev_Click(object sender, RoutedEventArgs e)
        {
            if (currentQueueIndex <= 0) return;
            // 上一首：切到前一项，缓存可能已清理，播放时自动重新下载
            lastPlayStart = DateTime.UtcNow;
            StopOutput();
            currentQueueIndex--;
            await PlayCurrentQueueAsync();
        }
        private async void Next_Click(object sender, RoutedEventArgs e)
        {
            if (queueTracks.Count == 0 || currentQueueIndex >= queueTracks.Count - 1)
            {
                if (playlistAutoExtend) await ExtendQueueAsync();
                if (currentQueueIndex >= queueTracks.Count - 1) return;
            }
            lastPlayStart = DateTime.UtcNow;
            StopOutput();
            await PlayNextQueueAsync();
        }
        private void HqToggle_Click(object sender, RoutedEventArgs e)
        {
            hqOn = !hqOn;
            HqToggle.Content = hqOn ? "升频：开" : "升频：关";
            HqToggle.Background = hqOn
                ? (Brush)FindResource("OkBrush")
                : (Brush)FindResource("TextDimBrush");
            if (!loaded) return;
            if (hqOn)
            {
                _ = TryConnectHqAsync();
            }
            else
            {
                hqPlayer.Disconnect();
                SetHqConnected(false);
            }
            SaveSettings();
        }

        // ------------------------------------------------------------------
        // 正在播放大窗口（点封面上滑）
        // ------------------------------------------------------------------

        private void OpenNowPlaying_Click(object sender, RoutedEventArgs e)
        {
            // WebView2 是独立 HWND（airspace），永远盖在 WPF 元素上面，
            // 打开覆盖层时先隐藏浏览器，避免网页挡住放大歌词页。
            Browser.Visibility = Visibility.Collapsed;
            NowPlayingOverlay.Visibility = Visibility.Visible;
            var trans = new TranslateTransform { Y = ActualHeight };
            NowPlayingOverlay.RenderTransform = trans;
            var anim = new DoubleAnimation(ActualHeight, 0, TimeSpan.FromMilliseconds(280))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            trans.BeginAnimation(TranslateTransform.YProperty, anim);
            // 淡入：内容与歌词
            NowPlayingOverlay.BeginAnimation(UIElement.OpacityProperty, null);
            NowPlayingOverlay.Opacity = 0;
            var fade = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(300))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            NowPlayingOverlay.BeginAnimation(UIElement.OpacityProperty, fade);
            if (activeLyricIndex < 0)
            {
                SetActiveLyric(0, false);
            }
            // 等布局完成后再滚动到当前句（此时视口高度才有效），避免第一句贴在顶部
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
            {
                UpdateLyricSpacers();
                if (activeLyricIndex >= 0 && activeLyricIndex < lyricLines.Count)
                {
                    ScrollLyricToCenter(lyricLines[activeLyricIndex]);
                }
            }));
        }

        private void CloseNowPlaying_Click(object sender, RoutedEventArgs e)
        {
            var trans = NowPlayingOverlay.RenderTransform as TranslateTransform ?? new TranslateTransform();
            NowPlayingOverlay.RenderTransform = trans;
            var anim = new DoubleAnimation(0, ActualHeight, TimeSpan.FromMilliseconds(240))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
            };
            anim.Completed += (s, a) =>
            {
                NowPlayingOverlay.Visibility = Visibility.Collapsed;
                // 恢复浏览器显示
                if (StreamPanel.Visibility == Visibility.Visible)
                {
                    Browser.Visibility = Visibility.Visible;
                }
            };
            trans.BeginAnimation(TranslateTransform.YProperty, anim);
            var fadeOut = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(220))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
            };
            NowPlayingOverlay.BeginAnimation(UIElement.OpacityProperty, fadeOut);
        }

        // ------------------------------------------------------------------
        // 流媒体 / 内嵌浏览器
        // ------------------------------------------------------------------

        private async Task EnsureBrowserAsync()
        {
            try
            {
                await Browser.EnsureCoreWebView2Async();
                Browser.CoreWebView2.WebResourceResponseReceived += OnWebResourceResponseReceived;
                Browser.CoreWebView2.WebMessageReceived += OnWebMessageReceived;
                try
                {
                    var js = QqInjectScript;
                    if (!string.IsNullOrEmpty(js))
                    {
                        await Browser.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(js);
                        // 对已加载的当前页面立即注入一次
                        try { await Browser.CoreWebView2.ExecuteScriptAsync(js); } catch { }
                    }
                }
                catch
                {
                    // 注入失败不阻塞浏览器
                }
                if (Browser.Source == null &&
                    Uri.TryCreate(UrlBox.Text.Trim(), UriKind.Absolute, out var uri))
                {
                    Browser.Source = uri;
                }
            }
            catch (Exception ex)
            {
                UrlBox.ToolTip = "浏览器初始化失败：" + ex.Message;
            }
        }

        private void OnWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
        {
            try
            {
                var msg = e.TryGetWebMessageAsString();
                if (string.IsNullOrEmpty(msg)) return;
                using var doc = System.Text.Json.JsonDocument.Parse(msg);
                if (!doc.RootElement.TryGetProperty("type", out var typeEl)
                    || typeEl.GetString() != "qq-download")
                    return;
                // 只处理 QQ 域名页面发来的下载请求（自定义 URL 等其他站点不能触发下载）
                var sourceHost = Browser.Source?.Host ?? "";
                if (!sourceHost.EndsWith("qq.com", StringComparison.OrdinalIgnoreCase))
                    return;
                var songmid = doc.RootElement.TryGetProperty("songmid", out var midEl)
                    ? midEl.GetString() : null;
                if (string.IsNullOrEmpty(songmid)) return;
                var songname = doc.RootElement.TryGetProperty("songname", out var nameEl)
                    ? nameEl.GetString() : "";
                var singer = doc.RootElement.TryGetProperty("singer", out var singerEl)
                    ? singerEl.GetString() : "";
                LogQqDebug($"收到下载请求: {songmid} / {songname} / {singer}");
                _ = HandleQqDownloadAsync(songmid, songname ?? "", singer ?? "");
            }
            catch
            {
                // 非 JSON / 解析失败忽略
            }
        }

        private async Task HandleQqDownloadAsync(string songmid, string songname, string singer)
        {
            try
            {
                LogQqDebug($"开始下载: {songmid} / {songname} / {singer}");
                SetStatus("QQ 下载中..." + (string.IsNullOrEmpty(songname) ? "" : $"（{songname}）"));
                string cookieHeader = "";
                if (Browser.CoreWebView2 != null)
                {
                    var cookies = await Browser.CoreWebView2.CookieManager
                        .GetCookiesAsync("https://y.qq.com/");
                    cookieHeader = string.Join("; ", cookies.Select(c => $"{c.Name}={c.Value}"));
                }
                LogQqDebug($"Cookie 长度: {cookieHeader.Length}（内容不写入日志）");
                var quality = QqQualityCombo?.SelectedIndex switch
                {
                    0 => "m4a",
                    1 => "128",
                    _ => "320"
                };
                LogQqDebug($"下载音质: {quality}");
                var result = await qqDownloader.DownloadAsync(songmid, quality, cookieHeader);
                if (result == null)
                {
                    LogQqDebug($"下载失败: 无权限或接口返回为空 ({songmid})");
                    SetStatus("下载失败：无权限或接口返回为空（可能需 VIP/付费）");
                    return;
                }
                var dir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                    "AuralDesk", "downloads");
                Directory.CreateDirectory(dir);
                capturedSongCount++;
                var safeName = string.IsNullOrEmpty(songname)
                    ? $"QQ_{DateTime.Now:yyyyMMdd_HHmmss}_{capturedSongCount}"
                    : SanitizeFileName(songname);
                var file = Path.Combine(dir, $"{safeName}.{result.Ext}");
                await File.WriteAllBytesAsync(file, result.Data);
                LogQqDebug($"已保存: {file} ({result.Data.Length} 字节)");
                SetStatus($"已保存：{file}（{result.Data.Length / 1024 / 1024}MB）");

                if (UseHqOutput)
                {
                    if (!await Task.Run(() => hqPlayer.PlayFile(file)))
                    {
                        SetStatus("HQPlayer 播放失败：请确认 HQPlayer 已启动、输出为 NAA");
                        return;
                    }
                    hqIsPlaying = true;
                    hqPrevState = -1;
                    lastHqPoll = DateTime.MinValue;
                    hqPlayRequestTime = DateTime.UtcNow;
                    isPlaying = true;
                    SyncPlayIcon();
                    var title = string.IsNullOrEmpty(songname)
                        ? Path.GetFileNameWithoutExtension(file)
                        : songname;
                    UpdateNowPlayingInfo(title, singer, "QQ音乐 · " + quality.ToUpperInvariant());
                    ShowNoLyrics();
                    SetStatus($"HQPlayer 播放：{Path.GetFileName(file)}");
                    AddToQueue(new QueueTrack
                    {
                        Title = title,
                        Singer = singer,
                        Source = "QQ音乐 · " + quality.ToUpperInvariant(),
                        Path = file,
                        IsCurrent = true
                    });

                    if (settings.ResumeMode > 0)
                    {
                        settings.LastSongPath = file;
                        settings.LastSongTitle = title;
                        settings.LastSongSinger = singer;
                        settings.LastSongSource = "QQ音乐 · " + quality.ToUpperInvariant();
                        settings.LastSongPosition = 0;
                        SaveSettings();
                    }
                }
                else if (OutputCombo.SelectedIndex == 0)
                {
                    systemPlayer.Play(file);
                    isPlaying = true;
                    SyncPlayIcon();
                    var title = string.IsNullOrEmpty(songname)
                        ? Path.GetFileNameWithoutExtension(file)
                        : songname;
                    UpdateNowPlayingInfo(title, singer, "QQ音乐 · " + quality.ToUpperInvariant());
                    ShowNoLyrics();
                    SetStatus($"系统输出播放：{Path.GetFileName(file)}");
                    AddToQueue(new QueueTrack
                    {
                        Title = title,
                        Singer = singer,
                        Source = "QQ音乐 · " + quality.ToUpperInvariant(),
                        Path = file,
                        IsCurrent = true
                    });

                    if (settings.ResumeMode > 0)
                    {
                        settings.LastSongPath = file;
                        settings.LastSongTitle = title;
                        settings.LastSongSinger = singer;
                        settings.LastSongSource = "QQ音乐 · " + quality.ToUpperInvariant();
                        settings.LastSongPosition = 0;
                        SaveSettings();
                    }
                }
            }
            catch (Exception ex)
            {
                LogQqDebug("下载异常: " + ex);
                SetStatus("下载失败：" + ex.Message);
            }
        }

        private static string SanitizeFileName(string name)
        {
            var invalid = Path.GetInvalidFileNameChars();
            var sb = new StringBuilder();
            foreach (var ch in name)
            {
                sb.Append(invalid.Contains(ch) ? '_' : ch);
            }
            var s = sb.ToString().Trim();
            return string.IsNullOrEmpty(s) ? "QQ_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") : s;
        }

        private void ShowNoLyrics()
        {
            LyricList.Children.Clear();
            lyricEntries.Clear();
            lyricTimes.Clear();
            lyricLines.Clear();
            lyricTrans.Clear();
            lyricOriginalColors.Clear();
            lyricTransOriginalColors.Clear();
            activeLyricIndex = -1;
            var tb = new TextBlock
            {
                Text = "暂无歌词",
                FontSize = 15,
                Foreground = (Brush)FindResource("TextDimBrush"),
                HorizontalAlignment = HorizontalAlignment.Left,
                Margin = new Thickness(0, 24, 0, 0)
            };
            LyricList.Children.Add(tb);
        }

        private void UpdateNowPlayingInfo(string title, string singer, string source)
        {
            if (BottomTitleText != null) BottomTitleText.Text = title;
            if (BottomSingerText != null) BottomSingerText.Text = singer;
            if (BottomSourceText != null) BottomSourceText.Text = source;
            if (OverlayTitleText != null) OverlayTitleText.Text = title;
            if (OverlaySingerText != null) OverlaySingerText.Text = singer;
            if (OverlaySourceText != null) OverlaySourceText.Text = source;
        }

        private static void LogQqDebug(string message)
        {
            AppLog.Write(message);
        }

        private async void UrlOpen_Click(object sender, RoutedEventArgs e)
        {
            await NavigateAsync(UrlBox.Text.Trim());
        }

        private async void UrlBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                await NavigateAsync(UrlBox.Text.Trim());
            }
        }

        private async Task NavigateAsync(string url)
        {
            if (string.IsNullOrWhiteSpace(url)) return;
            if (!url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
                !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                url = "https://" + url;
            }
            try
            {
                await Browser.EnsureCoreWebView2Async();
                Browser.Source = new Uri(url);
                UrlBox.Text = url;
            }
            catch (Exception ex)
            {
                UrlBox.ToolTip = "打开失败：" + ex.Message;
            }
        }

        private async void UrlBack_Click(object sender, RoutedEventArgs e)
        {
            await Browser.EnsureCoreWebView2Async();
            if (Browser.CoreWebView2?.CanGoBack == true)
            {
                Browser.CoreWebView2.GoBack();
            }
        }

        private async void UrlForward_Click(object sender, RoutedEventArgs e)
        {
            await Browser.EnsureCoreWebView2Async();
            if (Browser.CoreWebView2?.CanGoForward == true)
            {
                Browser.CoreWebView2.GoForward();
            }
        }


        // ------------------------------------------------------------------
        // QQ 音乐音源捕获 / 解密 / 下载 / 播放
        // ------------------------------------------------------------------

        private void OnWebResourceResponseReceived(object? sender, CoreWebView2WebResourceResponseReceivedEventArgs e)
        {
            var uri = e.Request.Uri;
            // 拦截 QQ 音乐核心接口 musicu.fcg：歌单/搜索/播放信息都走它，
            // 从响应 JSON 提取歌曲列表（songmid/songname/singer），供浮动面板下载。
            bool isQqApi = uri.Contains("musics.fcg", StringComparison.OrdinalIgnoreCase)
                || uri.Contains("cgi-bin/musicu.fcg", StringComparison.OrdinalIgnoreCase)
                || uri.Contains("fcg_ucc_getcdinfo", StringComparison.OrdinalIgnoreCase)
                || uri.Contains("fcg_music_express", StringComparison.OrdinalIgnoreCase)
                || uri.Contains("fcg_getplayinfo", StringComparison.OrdinalIgnoreCase);
            if (isQqApi)
            {
                LogQqDebug("musicu.fcg URL: " + uri);
                _ = ParseMusicuResponseAsync(e);
                return;
            }
            if (!IsCapturableAudioUrl(uri)) return;
            // 去重：同一地址短时间内只处理一次（播放器可能发起多个分段请求）
            if (uri == lastCapturedUrl && (DateTime.UtcNow - lastCapturedTime).TotalSeconds < 15) return;
            lastCapturedUrl = uri;
            lastCapturedTime = DateTime.UtcNow;
            _ = HandleCapturedAudioAsync(uri);
        }

        private async Task ParseMusicuResponseAsync(CoreWebView2WebResourceResponseReceivedEventArgs e)
        {
            try
            {
                var stream = await e.Response.GetContentAsync();
                if (stream == null) return;
                using var reader = new StreamReader(stream, Encoding.UTF8);
                var text = await reader.ReadToEndAsync();
                if (string.IsNullOrWhiteSpace(text)) return;
                LogQqDebug("musicu.fcg 响应头: " + (text.Length > 300 ? text[..300] : text));

                using var doc = JsonDocument.Parse(text);
                var found = new List<QqSongInfo>();
                CollectSongs(doc.RootElement, found);
                LogQqDebug($"musicu.fcg 响应 {text.Length} 字符，解析出 {found.Count} 首");
                if (found.Count == 0) return;

                lock (qqSongLock)
                {
                    foreach (var s in found)
                    {
                        if (!qqSongCache.Any(x => x.Songmid == s.Songmid))
                            qqSongCache.Add(s);
                    }
                    if (qqSongCache.Count > 200) qqSongCache.RemoveRange(0, qqSongCache.Count - 200);
                }

                // 推送最新歌曲列表给页面浮动面板
                var list = qqSongCache.Take(50).Select(s => new
                {
                    mid = s.Songmid,
                    name = s.Songname,
                    singer = s.Singer
                }).ToList();
                var json = JsonSerializer.Serialize(list);
                if (Browser.CoreWebView2 != null)
                {
                    await Browser.CoreWebView2.ExecuteScriptAsync($"window.adSetSongs && window.adSetSongs({json});");
                }
                _ = Dispatcher.BeginInvoke(() =>
                    SetStatus($"已识别 {found.Count} 首歌曲（共 {qqSongCache.Count} 首缓存）"));
            }
            catch (Exception ex)
            {
                LogQqDebug("musicu.fcg 解析失败: " + ex.Message);
            }
        }

        private static void CollectSongs(JsonElement el, List<QqSongInfo> list)
        {
            switch (el.ValueKind)
            {
                case JsonValueKind.Object:
                    if (el.TryGetProperty("songmid", out var mid) && mid.ValueKind == JsonValueKind.String)
                    {
                        var songmid = mid.GetString();
                        if (!string.IsNullOrEmpty(songmid) && songmid.Length >= 8)
                        {
                            var name = "";
                            if (el.TryGetProperty("songname", out var nameEl) && nameEl.ValueKind == JsonValueKind.String)
                                name = nameEl.GetString() ?? "";
                            var singer = "";
                            if (el.TryGetProperty("singer", out var singerEl) && singerEl.ValueKind == JsonValueKind.Array)
                            {
                                var parts = new List<string>();
                                foreach (var s in singerEl.EnumerateArray())
                                {
                                    if (s.ValueKind == JsonValueKind.Object && s.TryGetProperty("name", out var n)
                                        && n.ValueKind == JsonValueKind.String)
                                        parts.Add(n.GetString() ?? "");
                                }
                                singer = string.Join(" & ", parts);
                            }
                            list.Add(new QqSongInfo { Songmid = songmid, Songname = name, Singer = singer });
                            return;
                        }
                    }
                    foreach (var prop in el.EnumerateObject())
                        CollectSongs(prop.Value, list);
                    break;
                case JsonValueKind.Array:
                    foreach (var item in el.EnumerateArray())
                        CollectSongs(item, list);
                    break;
            }
        }

        private static bool IsCapturableAudioUrl(string uri)
        {
            if (!Uri.TryCreate(uri, UriKind.Absolute, out var u)) return false;
            var host = u.Host.ToLowerInvariant();
            bool qqHost = host.EndsWith("stream.qqmusic.qq.com")
                || host.EndsWith("dl.stream.qqmusic.qq.com")
                || host.EndsWith("music.tc.qq.com")
                || host.Contains("qqmusic.qq.com");
            if (!qqHost) return false;
            var path = u.AbsolutePath.ToLowerInvariant();
            return path.EndsWith(".m4a") || path.EndsWith(".mp3") || path.EndsWith(".flac")
                || path.EndsWith(".ogg") || path.EndsWith(".mflac") || path.EndsWith(".mgg")
                || path.EndsWith(".qmc0") || path.EndsWith(".qmc3");
        }

        private async Task HandleCapturedAudioAsync(string uri)
        {
            try
            {
                SetStatus("捕获到 QQ 音乐音源，开始下载…");
                byte[] bytes = await DownloadAudioAsync(uri);
                if (bytes.Length == 0)
                {
                    SetStatus("下载失败：响应为空");
                    return;
                }
                string ext = DetectUrlExt(uri);
                byte[] outData = bytes;
                string outExt = ext;

                if (IsQmcExt(ext))
                {
                    var dec = QmcDecoder.Decrypt(bytes, ext);
                    if (dec == null)
                    {
                        SetStatus("解密失败：无法检测掩码（可能是新加密格式）");
                        return;
                    }
                    outData = dec.Value.data;
                    outExt = dec.Value.ext;
                }
                else
                {
                    outExt = QmcDecoder.DetectRealExt(bytes, outExt);
                }

                var dir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                    "AuralDesk", "downloads");
                Directory.CreateDirectory(dir);
                capturedSongCount++;
                var file = Path.Combine(dir, $"QQ_{DateTime.Now:yyyyMMdd_HHmmss}_{capturedSongCount}.{outExt}");
                await File.WriteAllBytesAsync(file, outData);
                SetStatus($"已保存：{file}（{(outData.Length + 512) / 1024 / 1024}MB）");

                if (UseHqOutput)
                {
                    if (await Task.Run(() => hqPlayer.PlayFile(file)))
                    {
                        hqIsPlaying = true;
                        hqPrevState = -1;
                        lastHqPoll = DateTime.MinValue;
                        hqPlayRequestTime = DateTime.UtcNow;
                        isPlaying = true;
                        SyncPlayIcon();
                        SetStatus($"HQPlayer 播放：{Path.GetFileName(file)}");
                    }
                    else
                    {
                        SetStatus("HQPlayer 播放失败：请确认 HQPlayer 已启动、输出为 NAA");
                    }
                }
                else if (OutputCombo.SelectedIndex == 0)
                {
                    systemPlayer.Play(file);
                    SyncPlayIcon();
                    SetStatus($"系统输出播放：{Path.GetFileName(file)}");
                }
            }
            catch (Exception ex)
            {
                SetStatus("下载失败：" + ex.Message);
            }
        }

        private async Task<byte[]> DownloadAudioAsync(string uri)
        {
            if (Browser.CoreWebView2 != null)
            {
                var cookies = await Browser.CoreWebView2.CookieManager.GetCookiesAsync(uri);
                var cookieHeader = string.Join("; ", cookies.Select(c => $"{c.Name}={c.Value}"));
                using var req = new HttpRequestMessage(HttpMethod.Get, uri);
                if (!string.IsNullOrEmpty(cookieHeader))
                {
                    req.Headers.TryAddWithoutValidation("Cookie", cookieHeader);
                }
                req.Headers.Referrer = new Uri("https://y.qq.com/");
                req.Headers.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0 Safari/537.36");
                using var resp = await httpClient.SendAsync(req, HttpCompletionOption.ResponseHeadersRead);
                resp.EnsureSuccessStatusCode();
                return await resp.Content.ReadAsByteArrayAsync();
            }
            return await httpClient.GetByteArrayAsync(uri);
        }

        private static bool IsQmcExt(string ext) => ext switch
        {
            "mflac" or "mgg" or "qmc0" or "qmc3" or "qmcogg" or "qmcflac" or "bkcmp3" or "bkcflac" or "tkm" => true,
            _ => false
        };

        private static string DetectUrlExt(string uri)
        {
            if (!Uri.TryCreate(uri, UriKind.Absolute, out var u)) return "bin";
            var p = u.AbsolutePath;
            var dot = p.LastIndexOf('.');
            if (dot >= 0 && dot < p.Length - 1)
            {
                var e = p[(dot + 1)..].ToLowerInvariant();
                if (e.Length <= 5) return e;
            }
            return "bin";
        }

        private void SetStatus(string message)
        {
            if (StatusText != null)
            {
                StatusText.Text = message;
                StatusText.ToolTip = message;
            }
        }
        private async void UrlRefresh_Click(object sender, RoutedEventArgs e)
        {
            await Browser.EnsureCoreWebView2Async();
            Browser.CoreWebView2?.Reload();
        }

        // ------------------------------------------------------------------
        // 系统资源统计
        // ------------------------------------------------------------------

        private void UpdateStats(object? sender, EventArgs e)
        {
            try
            {
                var mse = new MemoryStatusEx();
                if (GlobalMemoryStatusEx(mse) && mse.ullTotalPhys > 0)
                {
                    long usedMb = (long)((mse.ullTotalPhys - mse.ullAvailPhys) / 1024 / 1024);
                    long totalMb = (long)(mse.ullTotalPhys / 1024 / 1024);
                    MemValue.Text = $"{usedMb:N0} / {totalMb:N0} MB";
                }

                if (GetSystemTimes(out var idle, out var kernel, out var user))
                {
                    ulong idleNow = ToTicks(idle);
                    ulong kernelNow = ToTicks(kernel);
                    ulong userNow = ToTicks(user);
                    if (!cpuBaselineReady)
                    {
                        // 首个样本只建立基线
                        lastIdleTicks = idleNow;
                        lastKernelTicks = kernelNow;
                        lastUserTicks = userNow;
                        cpuBaselineReady = true;
                        return;
                    }
                    ulong idleDelta = idleNow - lastIdleTicks;
                    ulong totalDelta = (kernelNow - lastKernelTicks) + (userNow - lastUserTicks);
                    lastIdleTicks = idleNow;
                    lastKernelTicks = kernelNow;
                    lastUserTicks = userNow;
                    if (totalDelta > 0)
                    {
                        // kernel 时间包含 idle，忙 = (kernel+user) - idle
                        double pct = (double)(totalDelta - idleDelta) / totalDelta * 100.0;
                        CpuValue.Text = $"{pct:F1}%";
                    }
                }
            }
            catch
            {
                // 统计失败不影响主流程
            }
        }

        private static ulong ToTicks(SystemTimes t) =>
            ((ulong)t.dwHighDateTime << 32) | t.dwLowDateTime;

        [StructLayout(LayoutKind.Sequential)]
        private struct SystemTimes
        {
            public uint dwLowDateTime;
            public uint dwHighDateTime;
        }

        [DllImport("kernel32.dll")]
        private static extern bool GetSystemTimes(
            out SystemTimes idleTime, out SystemTimes kernelTime, out SystemTimes userTime);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        private sealed class MemoryStatusEx
        {
            public uint dwLength;
            public uint dwMemoryLoad;
            public ulong ullTotalPhys;
            public ulong ullAvailPhys;
            public ulong ullTotalPageFile;
            public ulong ullAvailPageFile;
            public ulong ullTotalVirtual;
            public ulong ullAvailVirtual;
            public ulong ullAvailExtendedVirtual;

            public MemoryStatusEx()
            {
                dwLength = (uint)Marshal.SizeOf(typeof(MemoryStatusEx));
            }
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GlobalMemoryStatusEx([In, Out] MemoryStatusEx lpBuffer);
        // ------------------------------------------------------------------
        // 全屏模式
        // ------------------------------------------------------------------

        private void FullScreen_Click(object sender, RoutedEventArgs e)
        {
            ToggleFullScreen();
        }

        private void ToggleFullScreen()
        {
            if (!isFullScreen)
            {
                prevWindowState = WindowState;
                prevWindowStyle = WindowStyle;
                prevResizeMode = ResizeMode;
                prevBounds = new Rect(Left, Top, Width, Height);
                WindowStyle = WindowStyle.None;
                ResizeMode = ResizeMode.NoResize;
                WindowState = WindowState.Maximized;
                isFullScreen = true;
                FullScreenBtn.Content = "\uE73F";
                FullScreenBtn.ToolTip = "退出全屏";
            }
            else
            {
                if (prevWindowState == WindowState.Maximized)
                {
                    WindowStyle = prevWindowStyle;
                    ResizeMode = prevResizeMode;
                    WindowState = WindowState.Maximized;
                }
                else
                {
                    WindowStyle = prevWindowStyle;
                    ResizeMode = prevResizeMode;
                    WindowState = WindowState.Normal;
                    Left = prevBounds.Left;
                    Top = prevBounds.Top;
                    Width = prevBounds.Width;
                    Height = prevBounds.Height;
                }
                isFullScreen = false;
                FullScreenBtn.Content = "\uE740";
                FullScreenBtn.ToolTip = "全屏";
            }
        }

        private void Window_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.F11)
            {
                ToggleFullScreen();
                e.Handled = true;
            }
            else if (e.Key == Key.Escape && isFullScreen)
            {
                ToggleFullScreen();
                e.Handled = true;
            }
        }

        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            try
            {
                if (settings.ResumeMode >= 2)
                {
                    settings.LastSongPosition = UseHqOutput
                        ? playbackPos.TotalSeconds
                        : (systemPlayer.HasFile ? systemPlayer.Position.TotalSeconds : playbackPos.TotalSeconds);
                    SaveSettings();
                    SaveQueueSnapshot(force: true); // 关闭前强制保存完整队列
                }
            }
            catch
            {
                // 忽略保存失败
            }
            // 关闭时停止 HQPlayer 播放并清空其播放列表（后台执行，最多等 3 秒）
            try
            {
                var stopTask = Task.Run(() =>
                {
                    try { if (hqPlayer.Connected) hqPlayer.Stop(); } catch { }
                    try { if (hqPlayer.Connected) hqPlayer.ClearPlaylist(); } catch { }
                });
                stopTask.Wait(TimeSpan.FromSeconds(3));
            }
            catch
            {
                // 关闭不因 HQPlayer 阻塞
            }
            remoteControl.Stop();
            qqSidecar.Stop();
            base.OnClosing(e);
        }

        // ------------------------------------------------------------------
        // QQ 音乐（本地 sidecar API）
        // ------------------------------------------------------------------

        private async Task EnterQqViewAsync()
        {
            if (!qqSidecar.Available)
            {
                SetQqStatus(false, qqSidecar.Error ?? "QQ 音乐组件不可用");
                return;
            }
            SetQqStatus(false, "QQ 音乐组件启动中…");
            if (!await qqSidecar.EnsureStartedAsync())
            {
                SetQqStatus(false, qqSidecar.Error ?? "QQ 音乐组件启动失败");
                return;
            }
            if (qqApi.IsLoggedIn)
            {
                SetQqLoggedIn();
                // 每次启动首次进入流媒体都回到首页（避免停留在上次的收藏/歌单列表）
                if (!qqViewEnteredOnce || qqSongs.Count == 0)
                {
                    qqViewEnteredOnce = true;
                    await ShowQqHomeAsync();
                }
            }
            else
            {
                SetQqLoggedOut();
                QqEmptyHint.Text = "未登录，登录后即可查看收藏、歌单与推荐";
                QqEmptyHint.Visibility = Visibility.Visible;
            }
        }

        private void SetQqStatus(bool ok, string text)
        {
            QqStatusDot.Fill = ok
                ? (Brush)FindResource("OkBrush")
                : (Brush)FindResource("WarnBrush");
            QqStatusText.Text = text;
        }

        private void SetQqLoggedIn()
        {
            QqLoginBtn.Visibility = Visibility.Collapsed;
            QqLogoutBtn.Visibility = Visibility.Visible;
            SetQqStatus(true, $"已登录（{qqApi.Uin}）");
            _ = LoadQqNicknameAsync();
            _ = RefreshFavSetAsync();
            _ = RefreshFavAlbumsSetAsync();
        }

        private void SetQqLoggedOut()
        {
            QqLoginBtn.Visibility = Visibility.Visible;
            QqLogoutBtn.Visibility = Visibility.Collapsed;
            SetQqStatus(false, "未登录，点击右上角扫码登录");
        }

        private async Task LoadQqNicknameAsync()
        {
            try
            {
                var name = await qqApi.GetNicknameAsync();
                if (!string.IsNullOrEmpty(name))
                {
                    SetQqStatus(true, $"已登录：{name}");
                    QqStatusText.ToolTip = $"uin: {qqApi.Uin}";
                }
            }
            catch
            {
                // 昵称拿不到不影响使用
            }
        }

        private async Task RestoreLoginStateAsync()
        {
            if (!qqSidecar.Available)
                return;
            await qqSidecar.EnsureStartedAsync();
            await qqApi.TryRefreshAsync();
            if (qqApi.IsLoggedIn)
                SetQqLoggedIn();
        }

        private async void QqLogin_Click(object sender, RoutedEventArgs e)
        {
            ApplyQrLoginType();
            if (!qqSidecar.Available)
            {
                SetQqStatus(false, qqSidecar.Error ?? "QQ 音乐组件不可用");
                return;
            }
            if (!await qqSidecar.EnsureStartedAsync())
            {
                SetQqStatus(false, qqSidecar.Error ?? "启动失败");
                return;
            }
            try
            {
                var qr = await qqApi.GetQrCodeAsync();
                if (qr == null)
                {
                    SetQqStatus(false, "获取二维码失败");
                    return;
                }
                var (identifier, imgDataUrl) = qr.Value;
                var comma = imgDataUrl.IndexOf(',');
                if (comma < 0)
                {
                    SetQqStatus(false, "二维码数据异常");
                    return;
                }
                var base64 = imgDataUrl[(comma + 1)..];
                using var ms = new MemoryStream(Convert.FromBase64String(base64));
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.StreamSource = ms;
                bmp.EndInit();
                bmp.Freeze();
                QrImage.Source = bmp;
                QrStatusText.Text = qqApi.QrLoginType == "wx" ? "请用微信扫码" : "请用 QQ 手机版扫码";
                QrOverlay.Visibility = Visibility.Visible;
                _ = PollQrAsync(identifier);
            }
            catch (Exception ex)
            {
                SetQqStatus(false, "获取二维码失败：" + ex.Message);
            }
        }

        /// <summary>应用当前二维码登录方式（QQ/微信）并更新弹窗标题。</summary>
        private void ApplyQrLoginType()
        {
            qqApi.QrLoginType = QrTypeWx.IsChecked == true ? "wx" : "qq";
            QrTitleText.Text = qqApi.QrLoginType == "wx" ? "微信扫码登录" : "QQ 扫码登录";
        }

        /// <summary>登录弹窗内切换 QQ/微信：若弹窗已打开则立即重新获取对应二维码。</summary>
        private async void QrType_Changed(object sender, RoutedEventArgs e)
        {
            if (!loaded) return;
            ApplyQrLoginType();
            if (QrOverlay.Visibility != Visibility.Visible)
                return;
            QrStatusText.Text = "正在获取二维码…";
            try
            {
                var qr = await qqApi.GetQrCodeAsync();
                if (qr == null)
                {
                    QrStatusText.Text = "获取二维码失败";
                    return;
                }
                var (identifier, imgDataUrl) = qr.Value;
                var comma = imgDataUrl.IndexOf(',');
                if (comma < 0)
                {
                    QrStatusText.Text = "二维码数据异常";
                    return;
                }
                var base64 = imgDataUrl[(comma + 1)..];
                using var ms = new MemoryStream(Convert.FromBase64String(base64));
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.StreamSource = ms;
                bmp.EndInit();
                bmp.Freeze();
                QrImage.Source = bmp;
                QrStatusText.Text = qqApi.QrLoginType == "wx" ? "请用微信扫码" : "请用 QQ 手机版扫码";
                _ = PollQrAsync(identifier);
            }
            catch (Exception ex)
            {
                QrStatusText.Text = "获取二维码失败：" + ex.Message;
            }
        }

        private async Task PollQrAsync(string identifier)
        {
            if (qrPolling)
                return;
            qrPolling = true;
            try
            {
                while (QrOverlay.Visibility == Visibility.Visible)
                {
                    await Task.Delay(2000);
                    try
                    {
                        var (ev, cred) = await qqApi.CheckQrStatusAsync(identifier);
                        switch (ev)
                        {
                            case 1:
                                QrStatusText.Text = "等待扫码…";
                                break;
                            case 2:
                                QrStatusText.Text = "已扫码，请在手机上确认";
                                break;
                            case 3:
                                QrStatusText.Text = "二维码已过期，请取消后重新获取";
                                break;
                            case 4:
                                QrStatusText.Text = "已拒绝，请取消后重新获取";
                                break;
                            case 0:
                                QrStatusText.Text = "登录成功！";
                                if (cred != null && qqApi.ApplyCredentialJson(cred))
                                {
                                    QrOverlay.Visibility = Visibility.Collapsed;
                                    SetQqLoggedIn();
                                    await LoadQqNicknameAsync();
                                    if (qqSongs.Count == 0)
                                        await LoadQqTabAsync(reset: true);
                                    return;
                                }
                                QrStatusText.Text = "登录失败：凭证解析异常";
                                break;
                        }
                    }
                    catch (Exception ex)
                    {
                        QrStatusText.Text = "轮询异常：" + ex.Message;
                    }
                }
            }
            finally
            {
                qrPolling = false;
            }
        }

        private void QrCancel_Click(object sender, RoutedEventArgs e)
        {
            QrOverlay.Visibility = Visibility.Collapsed;
        }

        private void QqLogout_Click(object sender, RoutedEventArgs e)
        {
            qqApi.ClearCredential();
            qqSongs.Clear();
            qqShownMids.Clear();
            qqPlaylists.Clear();
            QqEmptyHint.Visibility = Visibility.Collapsed;
            SetQqLoggedOut();
        }

        private async void QqRefresh_Click(object sender, RoutedEventArgs e)
        {
            if (!qqSidecar.Available)
            {
                SetQqStatus(false, qqSidecar.Error ?? "组件不可用");
                return;
            }
            if (!await qqSidecar.EnsureStartedAsync())
            {
                SetQqStatus(false, qqSidecar.Error ?? "启动失败");
                return;
            }
            if (qqApi.IsLoggedIn)
            {
                await qqApi.TryRefreshAsync();
                SetQqLoggedIn();
                qqSongs.Clear();
                await LoadQqTabAsync(reset: true);
            }
            else
            {
                SetQqLoggedOut();
            }
        }

        private async Task ShowQqHomeAsync()
        {
            if (qqHomeCards.Count == 0)
                await LoadQqHomeAsync();
            QqHomeScroller.Visibility = Visibility.Visible;
            QqSongScroller.Visibility = Visibility.Collapsed;
            QqPlaylistScroller.Visibility = Visibility.Collapsed;
            QqListTitle.Text = "主页";
            QqListCountText.Text = "";
            QqHomeBtn.Visibility = Visibility.Collapsed;
            UpdateRandomPlayVisibility();
        }

        private Task LoadQqHomeAsync()
        {
            qqHomeCards.Clear();
            qqHomeCards.Add(new QqHomeCard
            {
                Icon = "\uE8B7",
                Title = "猜你喜欢·沉浸刷歌",
                Sub = "无限推荐，刷不完的歌",
                Type = "infinite"
            });
            qqHomeCards.Add(new QqHomeCard
            {
                Icon = "\uE81C",
                Title = "每日30首",
                Sub = "为你甄选今日 30 首",
                Type = "daily30"
            });
            qqHomeCards.Add(new QqHomeCard
            {
                Icon = "\uE734",
                Title = "我的收藏",
                Sub = "收藏的歌曲",
                Type = "fav"
            });
            qqHomeCards.Add(new QqHomeCard
            {
                Icon = "\uE8B9",
                Title = "收藏的专辑",
                Sub = "收藏列表中的专辑",
                Type = "favalbums"
            });
            qqHomeCards.Add(new QqHomeCard
            {
                Icon = "\uE8A1",
                Title = "账户下的歌单",
                Sub = "创建与收藏的歌单",
                Type = "songlists"
            });
            return Task.CompletedTask;
        }

        private void QqHome_Click(object sender, RoutedEventArgs e)
        {
            _ = ShowQqHomeAsync();
        }

        /// <summary>常驻返回：回到进入当前二级页面之前的视图。</summary>
        private void QqBackBtn_Click(object sender, RoutedEventArgs e)
        {
            QqSearchOverlay.Visibility = Visibility.Collapsed;
            QqSingerView.Visibility = Visibility.Collapsed;
            QqSongScroller.Visibility = Visibility.Collapsed;
            QqPlaylistScroller.Visibility = Visibility.Collapsed;
            QqAlbumScroller.Visibility = Visibility.Collapsed;
            QqHomeScroller.Visibility = Visibility.Collapsed;
            switch (qqBackTarget)
            {
                case "playlists":
                    QqPlaylistScroller.Visibility = Visibility.Visible;
                    qqBackTarget = "home"; // 歌单列表的上一级是主页
                    _ = LoadQqPlaylistsAsync();
                    break;
                case "favalbums":
                    QqAlbumScroller.Visibility = Visibility.Visible;
                    qqBackTarget = "home"; // 收藏专辑列表的上一级是主页
                    _ = LoadQqFavAlbumsAsync();
                    break;
                case "singer":
                    QqSingerView.Visibility = Visibility.Visible;
                    qqBackTarget = "search"; // 歌手页的上一级是搜索
                    if (currentSingerTab == "songs")
                        _ = LoadSingerSongsAsync(reset: true);
                    else
                        _ = LoadSingerAlbumsAsync(reset: true);
                    break;
                case "search":
                    QqSearchOverlay.Visibility = Visibility.Visible;
                    qqBackTarget = "home"; // 搜索的上一级是主页；再返回时退出搜索
                    break;
                default:
                    _ = ShowQqHomeAsync();
                    break;
            }
            UpdateRandomPlayVisibility();
        }

        private async void QqHomeCard_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button btn || btn.Tag is not QqHomeCard card) return;
            qqBackTarget = "home";
            QqHomeScroller.Visibility = Visibility.Collapsed;
            QqHomeBtn.Visibility = Visibility.Visible;
            switch (card.Type)
            {
                case "infinite":
                    QqPlaylistScroller.Visibility = Visibility.Collapsed;
                    QqSongScroller.Visibility = Visibility.Visible;
                    QqBackToPlaylists.Visibility = Visibility.Collapsed;
                    await EnterRadarModeAsync();
                    break;
                case "daily30":
                    QqPlaylistScroller.Visibility = Visibility.Collapsed;
                    QqSongScroller.Visibility = Visibility.Visible;
                    QqBackToPlaylists.Visibility = Visibility.Collapsed;
                    await LoadDaily30Async();
                    break;
                case "fav":
                    QqPlaylistScroller.Visibility = Visibility.Collapsed;
                    QqSongScroller.Visibility = Visibility.Visible;
                    QqBackToPlaylists.Visibility = Visibility.Collapsed;
                    qqTab = "fav";
                    qqSongs.Clear();
                    qqShownMids.Clear();
                    QqEmptyHint.Visibility = Visibility.Collapsed;
                    await LoadQqFavAsync(reset: true);
                    break;
                case "favalbums":
                    QqPlaylistScroller.Visibility = Visibility.Collapsed;
                    QqSongScroller.Visibility = Visibility.Collapsed;
                    QqAlbumScroller.Visibility = Visibility.Visible;
                    QqBackToPlaylists.Visibility = Visibility.Collapsed;
                    QqListTitle.Text = "收藏的专辑";
                    QqListCountText.Text = "";
                    await LoadQqFavAlbumsAsync();
                    break;
                case "songlists":
                    qqTab = "playlists";
                    QqSongScroller.Visibility = Visibility.Collapsed;
                    qqSongs.Clear();
                    QqEmptyHint.Visibility = Visibility.Collapsed;
                    await LoadQqPlaylistsAsync();
                    break;
            }
            UpdateRandomPlayVisibility();
        }

        // ---------------- 猜你喜欢·沉浸刷歌 / 每日30首 ----------------

        /// <summary>进入沉浸刷歌模式：清空列表，循环拉取猜你喜欢持续加载。</summary>
        private async Task EnterRadarModeAsync()
        {
            qqTab = "radar";
            radarHasMore = true;
            radarLoading = false;
            qqSongs.Clear();
            qqShownMids.Clear();
            QqEmptyHint.Visibility = Visibility.Collapsed;
            QqListTitle.Text = "猜你喜欢·沉浸刷歌";
            QqListCountText.Text = "";
            await LoadMoreRadarAsync();
        }

        /// <summary>拉取猜你喜欢下一批（账户个性化，每批 5 首）并追加到歌曲列表。</summary>
        private async Task LoadMoreRadarAsync()
        {
            if (radarLoading) return;
            radarLoading = true;
            try
            {
                var data = await qqApi.GetGuessRecommendAsync();
                var songs = QqApiClient.ParseSongs(data);
                var added = 0;
                foreach (var song in songs)
                {
                    if (!qqShownMids.Add(song.Mid)) continue;
                    qqSongs.Add(song);
                    added++;
                }
                QqListCountText.Text = $"已加载 {qqSongs.Count} 首";
                QqFilterBox.Text = "";
                SyncQqSongsFull();
                radarHasMore = true; // 猜你喜欢无限供应，始终可续
                LogQqDebug($"猜你喜欢沉浸刷歌：新增 {added} 首，共 {qqSongs.Count} 首");
            }
            catch (Exception ex)
            {
                SetQqStatus(false, "加载沉浸刷歌失败：" + ex.Message);
            }
            finally
            {
                radarLoading = false;
            }
        }

        /// <summary>加载每日 30 首：调用账号专属系统歌单接口（dirid=202），任何登录账号通用。</summary>
        private async Task LoadDaily30Async()
        {
            qqTab = "daily30";
            qqSongs.Clear();
            qqShownMids.Clear();
            QqEmptyHint.Visibility = Visibility.Collapsed;
            QqListTitle.Text = "每日30首";
            QqListCountText.Text = "正在加载…";
            try
            {
                var euin = qqApi.EncryptUin;
                if (string.IsNullOrEmpty(euin))
                {
                    SetQqStatus(false, "未登录，无法获取每日30首");
                    QqListCountText.Text = "0 首";
                    return;
                }
                var data = await qqApi.GetDaily30Async(euin);
                foreach (var song in QqApiClient.ParseSongs(data))
                {
                    qqSongs.Add(song);
                }
                QqFilterBox.Text = "";
                SyncQqSongsFull();
            }
            catch (Exception ex)
            {
                SetQqStatus(false, "加载每日30首失败：" + ex.Message);
            }
            QqListCountText.Text = $"{qqSongs.Count} 首";
            if (qqSongs.Count == 0)
            {
                QqEmptyHint.Text = "暂无推荐";
                QqEmptyHint.Visibility = Visibility.Visible;
            }
        }

        /// <summary>随机播放按钮：仅打开歌曲列表时可见；按下即随机打乱当前列表后播放。</summary>
        private async void RandomPlay_Click(object sender, RoutedEventArgs e)
        {
            if (qqSongs.Count == 0) return;
            lastPlayStart = DateTime.UtcNow;
            StopOutput();
            List<QqSongItem> list;
            if (qqTab == "radar" || qqTab == "daily30")
            {
                // 动态/每日列表即全量，直接打乱当前已加载列表
                list = qqSongs.ToList();
            }
            else
            {
                SetStatus("正在加载整个歌单…");
                list = await LoadEntireQqListAsync();
                if (list.Count == 0)
                {
                    SetStatus("歌单为空");
                    return;
                }
            }
            Shuffle(list);
            queueTracks.Clear();
            foreach (var s in list)
                queueTracks.Add(MakeQqTrack(s));
            ApplyFavStateToLists();
            currentQueueIndex = 0;
            UpdateQueueEmptyHint();
            await PlayCurrentQueueAsync();
            SetStatus($"随机播放（{queueTracks.Count} 首）");
        }

        /// <summary>随机播放按钮仅在歌曲列表视图下显示。</summary>
        private void UpdateRandomPlayVisibility()
        {
            RandomPlayBtn.Visibility = QqSongScroller.Visibility == Visibility.Visible
                ? Visibility.Visible
                : Visibility.Collapsed;
            // 无限推荐是持续推荐流，随机打乱没有意义，禁用
            RandomPlayBtn.IsEnabled = qqTab != "radar";
        }

        // ---------------- URL 打开 ----------------

        private void QqUrl_Click(object sender, RoutedEventArgs e)
        {
            QqUrlBox.Text = "";
            QqUrlOverlay.Visibility = Visibility.Visible;
            QqUrlBox.Focus();
        }

        private void QqUrlClose_Click(object sender, RoutedEventArgs e)
        {
            QqUrlOverlay.Visibility = Visibility.Collapsed;
        }

        private void QqUrlBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
                _ = OpenQqUrlAsync();
        }

        private async void QqUrlGo_Click(object sender, RoutedEventArgs e)
        {
            await OpenQqUrlAsync();
        }

        private async Task OpenQqUrlAsync()
        {
            var url = QqUrlBox.Text.Trim();
            if (string.IsNullOrEmpty(url)) return;
            QqUrlOverlay.Visibility = Visibility.Collapsed;

            // 短链/分享链接：跟随跳转拿最终地址
            if (url.Contains("fcgi-bin/u?") || url.Contains("c6.y.qq.com") || url.Contains("u.y.qq.com"))
            {
                try
                {
                    using var hc = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
                    hc.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0");
                    using var resp = await hc.GetAsync(url);
                    url = resp.RequestMessage?.RequestUri?.ToString() ?? url;
                }
                catch { }
            }

            var mPlaylist = Regex.Match(url, @"(?:playlist|songlist)[/=](\d+)");
            if (mPlaylist.Success && long.TryParse(mPlaylist.Groups[1].Value, out var pid))
            {
                qqBackTarget = "home";
                qqTab = "playlists";
                currentPlaylistId = pid;
                currentPlaylistName = "链接歌单";
                qqSongs.Clear();
                QqHomeScroller.Visibility = Visibility.Collapsed;
                QqSongScroller.Visibility = Visibility.Visible;
                QqHomeBtn.Visibility = Visibility.Visible;
                QqEmptyHint.Visibility = Visibility.Collapsed;
                await LoadQqPlaylistSongsAsync(reset: true);
                UpdateRandomPlayVisibility();
                return;
            }

            var mSong = Regex.Match(url, @"(?:songDetail|song)[/=](\w+)");
            if (mSong.Success)
            {
                var mid = mSong.Groups[1].Value;
                var data = await qqApi.SearchAsync(mid, 1, 10);
                var songs = QqApiClient.ParseSongs(data);
                var hit = songs.FirstOrDefault(s => s.Mid == mid) ?? songs.FirstOrDefault();
                if (hit != null)
                {
                    qqSongs.Clear();
                    qqSongs.Add(hit);
                    qqPage = 1;
                    qqHasMore = false;
                    QqHomeScroller.Visibility = Visibility.Collapsed;
                    QqSongScroller.Visibility = Visibility.Visible;
                    QqHomeBtn.Visibility = Visibility.Visible;
                    QqListTitle.Text = hit.Title;
                    QqListCountText.Text = "1 首";
                    UpdateRandomPlayVisibility();
                }
                return;
            }

            if (long.TryParse(url, out var pid2))
            {
                qqTab = "playlists";
                currentPlaylistId = pid2;
                currentPlaylistName = "链接歌单";
                qqSongs.Clear();
                QqHomeScroller.Visibility = Visibility.Collapsed;
                QqSongScroller.Visibility = Visibility.Visible;
                QqHomeBtn.Visibility = Visibility.Visible;
                QqEmptyHint.Visibility = Visibility.Collapsed;
                await LoadQqPlaylistSongsAsync(reset: true);
                UpdateRandomPlayVisibility();
                return;
            }
            SetStatus("无法识别的链接");
        }
        private async void QqTab_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button btn || btn.Tag is not string tag)
                return;
            qqTab = tag;
            currentPlaylistId = 0;
            QqBackToPlaylists.Visibility = Visibility.Collapsed;
            QqSearchRow.Visibility = tag == "search" ? Visibility.Visible : Visibility.Collapsed;
            QqPlaylistScroller.Visibility = Visibility.Collapsed;
            QqSongScroller.Visibility = Visibility.Visible;
            qqSongs.Clear();
            qqShownMids.Clear();
            QqEmptyHint.Visibility = Visibility.Collapsed;
            await LoadQqTabAsync(reset: true);
        }

        private async Task LoadQqTabAsync(bool reset)
        {
            if (reset)
                qqShownMids.Clear();
            if (!qqApi.IsLoggedIn && qqTab != "search")
            {
                QqEmptyHint.Text = "未登录，请先扫码登录";
                QqEmptyHint.Visibility = Visibility.Visible;
                SetQqLoggedOut();
                return;
            }
            QqEmptyHint.Visibility = Visibility.Collapsed;
            switch (qqTab)
            {
                case "fav":
                    await LoadQqFavAsync(reset);
                    break;
                case "playlists":
                    await LoadQqPlaylistsAsync();
                    break;
            }
            UpdateRandomPlayVisibility();
        }

        private async Task LoadQqFavAsync(bool reset)
        {
            if (qqLoading)
                return;
            qqLoading = true;
            try
            {
                var page = reset ? 1 : qqPage + 1;
                var data = await qqApi.GetFavSongsAsync(page, 30);
                var songs = QqApiClient.ParseSongs(data);
                UpdateQqListCount(data);
                if (reset)
                    qqSongs.Clear();
                foreach (var song in songs)
                    qqSongs.Add(song);
                qqPage = page;
                qqHasMore = QqApiClient.HasMore(data);
                QqListTitle.Text = "我的收藏";
                QqFilterBox.Text = "";
                SyncQqSongsFull();
                if (qqSongs.Count == 0)
                {
                    QqEmptyHint.Text = "收藏列表为空";
                    QqEmptyHint.Visibility = Visibility.Visible;
                }
            }
            catch (Exception ex)
            {
                SetQqStatus(false, "加载收藏失败：" + ex.Message);
            }
            finally
            {
                qqLoading = false;
            }
        }

        private async Task LoadQqPlaylistsAsync()
        {
            try
            {
                var data = await qqApi.GetCreatedSonglistsAsync();
                var list = QqApiClient.ParsePlaylists(data);
                qqPlaylists.Clear();
                foreach (var playlist in list)
                    qqPlaylists.Add(playlist);
                QqListTitle.Text = "我的歌单";
                QqPlaylistScroller.Visibility = Visibility.Visible;
                QqSongScroller.Visibility = Visibility.Collapsed;
                if (list.Count == 0)
                {
                    QqEmptyHint.Text = "还没有创建过歌单";
                    QqEmptyHint.Visibility = Visibility.Visible;
                }
                UpdateRandomPlayVisibility();
            }
            catch (Exception ex)
            {
                SetQqStatus(false, "加载歌单失败：" + ex.Message);
            }
        }

        private async void QqPlaylist_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button btn || btn.Tag is not QqPlaylistItem playlist)
                return;
            qqBackTarget = "playlists";
            // 必须同步 qqTab，否则后台"完整加载整个歌单"会按残留的 tab 拉错歌单
            qqTab = "playlists";
            currentPlaylistId = playlist.Id;
            currentPlaylistName = playlist.Name;
            QqPlaylistScroller.Visibility = Visibility.Collapsed;
            QqSongScroller.Visibility = Visibility.Visible;
            QqBackToPlaylists.Visibility = Visibility.Visible;
            qqSongs.Clear();
            QqEmptyHint.Visibility = Visibility.Collapsed;
            await LoadQqPlaylistSongsAsync(reset: true);
            UpdateRandomPlayVisibility();
        }

        private async Task LoadQqPlaylistSongsAsync(bool reset)
        {
            if (currentPlaylistId <= 0 || qqLoading)
                return;
            qqLoading = true;
            try
            {
                var page = reset ? 1 : qqPage + 1;
                var data = await qqApi.GetSonglistDetailAsync(currentPlaylistId, page, 30);
                var songs = QqApiClient.ParseSongs(data);
                UpdateQqListCount(data);
                if (reset)
                    qqSongs.Clear();
                foreach (var song in songs)
                    qqSongs.Add(song);
                qqPage = page;
                qqHasMore = QqApiClient.HasMore(data);
                QqListTitle.Text = currentPlaylistName;
                QqFilterBox.Text = "";
                SyncQqSongsFull();
                if (qqSongs.Count == 0)
                {
                    QqEmptyHint.Text = "歌单为空";
                    QqEmptyHint.Visibility = Visibility.Visible;
                }
            }
            catch (Exception ex)
            {
                SetQqStatus(false, "加载歌单歌曲失败：" + ex.Message);
            }
            finally
            {
                qqLoading = false;
            }
        }

        private void QqBackToPlaylists_Click(object sender, RoutedEventArgs e)
        {
            currentPlaylistId = 0;
            QqBackToPlaylists.Visibility = Visibility.Collapsed;
            QqSongScroller.Visibility = Visibility.Collapsed;
            QqPlaylistScroller.Visibility = Visibility.Visible;
            _ = LoadQqPlaylistsAsync();
        }

        private void QqSearchBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
                _ = DoQqSearchAsync(reset: true);
        }

        private async void QqSearchBtn_Click(object sender, RoutedEventArgs e)
        {
            await DoQqSearchAsync(reset: true);
        }

        private async Task DoQqSearchAsync(bool reset)
        {
            var keyword = QqSearchBox.Text.Trim();
            if (string.IsNullOrEmpty(keyword))
                return;
            if (qqLoading)
                return;
            qqLoading = true;
            try
            {
                var page = reset ? 1 : qqPage + 1;
                var data = await qqApi.SearchAsync(keyword, page, 30);
                var songs = QqApiClient.ParseSongs(data);
                UpdateQqListCount(data);
                if (reset)
                    qqSongs.Clear();
                foreach (var song in songs)
                    qqSongs.Add(song);
                qqPage = page;
                qqHasMore = songs.Count > 0;
                QqListTitle.Text = $"搜索：{keyword}";
                QqFilterBox.Text = "";
                SyncQqSongsFull();
                if (qqSongs.Count == 0)
                {
                    QqEmptyHint.Text = "没有找到相关歌曲";
                    QqEmptyHint.Visibility = Visibility.Visible;
                }
            }
            catch (Exception ex)
            {
                SetQqStatus(false, "搜索失败：" + ex.Message);
            }
            finally
            {
                qqLoading = false;
            }
        }


        private void QqSongScroller_ScrollChanged(object sender, ScrollChangedEventArgs e)
        {
            if (e.VerticalOffset + e.ViewportHeight >= e.ExtentHeight - 200)
                _ = LoadQqNextPageAsync();
        }

        /// <summary>歌单内搜索：记录全量列表并应用当前关键字过滤。</summary>
        private void SyncQqSongsFull()
        {
            qqSongsFull = qqSongs.ToList();
            ApplyQqFilter();
            ApplyFavStateToLists();
        }

        /// <summary>拉取「我喜欢的音乐」列表，构建收藏 mid 集合并刷新各列表红心。</summary>
        private async Task RefreshFavSetAsync()
        {
            if (!qqApi.IsLoggedIn)
                return;
            try
            {
                var mids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                for (var page = 1; page <= 8; page++)
                {
                    var data = await qqApi.GetFavSongsAsync(page, 100);
                    var list = QqApiClient.ParseSongs(data);
                    foreach (var s in list)
                        mids.Add(s.Mid);
                    if (list.Count < 100)
                        break;
                }
                favMidSet.Clear();
                foreach (var m in mids)
                    favMidSet.Add(m);
                await Dispatcher.BeginInvoke(DispatcherPriority.Normal, new Action(ApplyFavStateToLists));
            }
            catch
            {
                // 收藏状态获取失败不影响使用
            }
        }

        /// <summary>按收藏集合刷新各列表歌曲的红心状态。</summary>
        private void ApplyFavStateToLists()
        {
            foreach (var s in qqSongs) s.IsFav = favMidSet.Contains(s.Mid);
            foreach (var s in qqSingerSongs) s.IsFav = favMidSet.Contains(s.Mid);
            foreach (var s in qqSearchSongs) s.IsFav = favMidSet.Contains(s.Mid);
            foreach (var t in queueTracks) t.IsFav = !string.IsNullOrEmpty(t.QqMid) && favMidSet.Contains(t.QqMid);
        }

        /// <summary>歌曲行红心按钮：收藏 / 取消收藏（我的收藏歌单 dirid=201）。</summary>
        /// <summary>红心按下即 handled：阻止外层行按钮进入按下状态（避免点击收藏却触发行播放）。</summary>
        private void QqSongFav_PreviewDown(object sender, MouseButtonEventArgs e) => e.Handled = true;

        /// <summary>歌曲行红心：收藏 / 取消收藏（我的收藏歌单 dirid=201）。</summary>
        private async void QqSongFav_MouseUp(object sender, MouseButtonEventArgs e)
        {
            e.Handled = true;
            if (sender is not System.Windows.Shapes.Path p || p.Tag is not QqSongItem song)
                return;
            await ToggleFavAsync(song);
        }

        private async Task ToggleFavAsync(QqSongItem song)
        {
            if (song.Id <= 0)
            {
                SetStatus("该歌曲缺少 ID，无法收藏");
                LogQqDebug("收藏失败: 歌曲缺少 ID " + song.Mid);
                return;
            }
            var target = !song.IsFav;
            try
            {
                if (target)
                    await qqApi.AddFavSongAsync(song.Id, song.SongType);
                else
                    await qqApi.RemoveFavSongAsync(song.Id, song.SongType);
                  song.IsFav = target;
                  if (target) favMidSet.Add(song.Mid); else favMidSet.Remove(song.Mid);
                  ApplyFavStateToLists();
                  SetStatus(target ? $"已收藏：{song.Title}" : $"已取消收藏：{song.Title}");
                  LogQqDebug($"收藏操作: {song.Title} id={song.Id} type={song.SongType} -> {target}");
              }
              catch (Exception ex)
              {
                  SetStatus("收藏操作失败：" + ex.Message);
                  LogQqDebug("收藏操作失败: " + ex);
              }
          }

        /// <summary>播放队列红心：按队列项收藏 / 取消收藏。</summary>
        private async Task ToggleQueueFavAsync(QueueTrack track)
        {
            var song = track.QqSong;
            if (song == null)
                song = qqSongs.FirstOrDefault(s => s.Mid == track.QqMid)
                    ?? qqSearchSongs.FirstOrDefault(s => s.Mid == track.QqMid)
                    ?? qqSingerSongs.FirstOrDefault(s => s.Mid == track.QqMid);
            if (song != null && song.Id > 0)
            {
                await ToggleFavAsync(song);
                return;
            }
            if (string.IsNullOrEmpty(track.QqMid))
                return;
            // 恢复的队列项可能缺少歌曲 ID，先从详情接口解析
            var songId = song?.Id > 0 ? song.Id : await qqApi.ResolveSongIdAsync(track.QqMid);
            if (songId <= 0)
            {
                SetStatus("该歌曲缺少 ID，无法收藏");
                LogQqDebug("收藏失败: 歌曲缺少 ID " + track.QqMid);
                return;
            }
            var target = !track.IsFav;
            try
            {
                if (target)
                    await qqApi.AddFavSongAsync(songId, 0);
                else
                    await qqApi.RemoveFavSongAsync(songId, 0);
                track.IsFav = target;
                if (target) favMidSet.Add(track.QqMid); else favMidSet.Remove(track.QqMid);
                ApplyFavStateToLists();
                SetStatus(target ? $"已收藏：{track.Title}" : $"已取消收藏：{track.Title}");
                LogQqDebug($"收藏操作(队列): {track.Title} id={songId} -> {target}");
            }
            catch (Exception ex)
            {
                SetStatus("收藏操作失败：" + ex.Message);
                LogQqDebug("收藏操作失败: " + ex);
            }
        }

        /// <summary>拉取「收藏的专辑」列表，构建收藏专辑 mid 集合。</summary>
        private async Task RefreshFavAlbumsSetAsync()
        {
            if (!qqApi.IsLoggedIn)
                return;
            try
            {
                var mids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var collected = 0;
                for (var page = 1; page <= 8; page++)
                {
                    var data = await qqApi.GetFavAlbumsAsync(page, 50);
                    var list = QqApiClient.ParseAlbums(data);
                    foreach (var a in list)
                        mids.Add(a.AlbumMid);
                    collected += list.Count;
                    var total = GetQqTotal(data);
                    if (total > 0 && collected >= total)
                        break;
                    if (list.Count == 0)
                        break;
                }
                favAlbumMidSet.Clear();
                foreach (var m in mids)
                    favAlbumMidSet.Add(m);
                await Dispatcher.BeginInvoke(DispatcherPriority.Normal, new Action(ApplyFavStateToAlbums));
            }
            catch
            {
                // 专辑收藏状态获取失败不影响使用
            }
        }

        /// <summary>按收藏专辑集合刷新各列表专辑的红心状态。</summary>
        private void ApplyFavStateToAlbums()
        {
            foreach (var a in qqAlbums) a.IsFav = favAlbumMidSet.Contains(a.AlbumMid);
            foreach (var a in qqSearchAlbums) a.IsFav = favAlbumMidSet.Contains(a.AlbumMid);
            foreach (var a in qqSingerAlbums) a.IsFav = favAlbumMidSet.Contains(a.AlbumMid);
        }

        /// <summary>专辑行红心：收藏 / 取消收藏（收藏的专辑）。</summary>
        private async Task ToggleFavAlbumAsync(QqAlbumInfo album)
        {
            if (album.AlbumId <= 0)
            {
                SetStatus("该专辑缺少 ID，无法收藏");
                LogQqDebug("收藏专辑失败: 缺少专辑 ID " + album.AlbumMid);
                return;
            }
            var target = !album.IsFav;
            try
            {
                if (target)
                    await qqApi.AddFavAlbumAsync(album.AlbumId);
                else
                    await qqApi.RemoveFavAlbumAsync(album.AlbumId);
                album.IsFav = target;
                if (target) favAlbumMidSet.Add(album.AlbumMid); else favAlbumMidSet.Remove(album.AlbumMid);
                ApplyFavStateToAlbums();
                SetStatus(target ? $"已收藏专辑：{album.Name}" : $"已取消收藏专辑：{album.Name}");
                LogQqDebug($"收藏专辑操作: {album.Name} id={album.AlbumId} -> {target}");
            }
            catch (Exception ex)
            {
                SetStatus("收藏专辑操作失败：" + ex.Message);
                LogQqDebug("收藏专辑操作失败: " + ex);
            }
        }

        private void QqAlbumFav_PreviewDown(object sender, MouseButtonEventArgs e) => e.Handled = true;

        private async void QqAlbumFav_MouseUp(object sender, MouseButtonEventArgs e)
        {
            e.Handled = true;
            if (sender is not System.Windows.Shapes.Path p || p.Tag is not QqAlbumInfo album)
                return;
            await ToggleFavAlbumAsync(album);
        }

        private void QueueFav_PreviewDown(object sender, MouseButtonEventArgs e) => e.Handled = true;

        private async void QueueFav_MouseUp(object sender, MouseButtonEventArgs e)
        {
            e.Handled = true;
            if (sender is not System.Windows.Shapes.Path p || p.Tag is not QueueTrack track)
                return;
            await ToggleQueueFavAsync(track);
        }

        private void ApplyQqFilter()
        {
            if (QqFilterBox == null) return;
            var kw = QqFilterBox.Text.Trim();
            if (string.IsNullOrEmpty(kw)) return;
            qqSongs.Clear();
            foreach (var s in qqSongsFull)
            {
                if (s.Title.Contains(kw, StringComparison.OrdinalIgnoreCase) ||
                    s.Singer.Contains(kw, StringComparison.OrdinalIgnoreCase))
                {
                    qqSongs.Add(s);
                }
            }
        }

        private void QqFilter_Changed(object sender, TextChangedEventArgs e)
        {
            if (!loaded) return;
            if (string.IsNullOrEmpty(QqFilterBox.Text.Trim()))
            {
                if (qqSongs.Count != qqSongsFull.Count)
                {
                    qqSongs.Clear();
                    foreach (var s in qqSongsFull) qqSongs.Add(s);
                }
            }
            else
            {
                ApplyQqFilter();
            }
        }

        // ---------------- 在线搜索（单曲/专辑/歌手） ----------------

        private List<QqSongItem> qqSingerSongsFull = new();
        private List<QqAlbumInfo> qqSingerAlbumsFull = new();

        private void QqOnlineSearch_Click(object sender, RoutedEventArgs e)
        {
            QqSingerView.Visibility = Visibility.Collapsed;
            QqSearchOverlay.Visibility = Visibility.Visible;
            QqOnlineSearchBox.Focus();
        }

        private void QqSearchClose_Click(object sender, RoutedEventArgs e)
        {
            QqSearchOverlay.Visibility = Visibility.Collapsed;
        }

        private void QqOnlineSearchBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
                _ = DoQqOnlineSearchAsync();
        }

        private async void QqOnlineSearchGo_Click(object sender, RoutedEventArgs e)
        {
            await DoQqOnlineSearchAsync();
        }

        private async Task DoQqOnlineSearchAsync()
        {
            var keyword = QqOnlineSearchBox.Text.Trim();
            if (string.IsNullOrEmpty(keyword) || qqSearchLoading)
                return;
            qqSearchLoading = true;
            try
            {
                var data = await qqApi.SearchAsync(keyword, 1, 30);
                qqSearchSongs.Clear();
                foreach (var s in QqApiClient.ParseSongs(data)) qqSearchSongs.Add(s);
                qqSearchAlbums.Clear();
                foreach (var a in QqApiClient.ParseAlbums(data)) qqSearchAlbums.Add(a);
                qqSearchSingers.Clear();
                foreach (var sg in QqApiClient.ParseSingers(data)) qqSearchSingers.Add(sg);
                ShowQqSearchTab(currentSearchTab);
                ApplyFavStateToLists();
                ApplyFavStateToAlbums();
                SetQqStatus(true, $"搜索「{keyword}」完成");
            }
            catch (Exception ex)
            {
                SetQqStatus(false, "在线搜索失败：" + ex.Message);
            }
            finally
            {
                qqSearchLoading = false;
            }
        }

        private void QqSearchTab_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string tag)
                ShowQqSearchTab(tag);
        }

        private void ShowQqSearchTab(string tab)
        {
            currentSearchTab = tab;
            QqSearchSongList.Visibility = tab == "song" ? Visibility.Visible : Visibility.Collapsed;
            QqSearchAlbumList.Visibility = tab == "album" ? Visibility.Visible : Visibility.Collapsed;
            QqSearchSingerList.Visibility = tab == "singer" ? Visibility.Visible : Visibility.Collapsed;
        }

        private async void QqSearchSong_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is QqSongItem song)
            {
                // 播放后保留搜索页，由用户自行关闭；整个搜索单曲列表入队
                await PlayQqSongWithPlaylistAsync(song, qqSearchSongs.ToList());
            }
        }

        private async void QqSearchAlbum_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is QqAlbumInfo album)
            {
                qqBackTarget = "search";
                await OpenAlbumAsync(album);
            }
        }

        private async void QqSearchSinger_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is QqSingerInfo singer)
                await OpenSingerAsync(singer);
        }

        // ---------------- 歌手页 ----------------

        private async Task OpenSingerAsync(QqSingerInfo singer)
        {
            qqBackTarget = "search";
            currentSingerMid = singer.SingerMid;
            currentSingerName = singer.Name;
            QqSearchOverlay.Visibility = Visibility.Collapsed;
            QqSingerView.Visibility = Visibility.Visible;
            QqHomeScroller.Visibility = Visibility.Collapsed;
            QqPlaylistScroller.Visibility = Visibility.Collapsed;
            QqAlbumScroller.Visibility = Visibility.Collapsed;
            QqSongScroller.Visibility = Visibility.Collapsed;
            QqSingerAvatar.Source = null;
            QqSingerName.Text = singer.Name;
            QqSingerDescText.Text = "";
            QqSingerFilterBox.Text = "";
            currentSingerTab = "songs";
            _ = LoadSingerInfoAsync();
            await LoadSingerSongsAsync(reset: true);
        }

        private void QqSingerBack_Click(object sender, RoutedEventArgs e)
        {
            QqSingerView.Visibility = Visibility.Collapsed;
            QqSearchOverlay.Visibility = Visibility.Visible;
        }

        private async Task LoadSingerInfoAsync()
        {
            try
            {
                var info = await qqApi.GetSingerInfoAsync(currentSingerMid);
                var desc = await qqApi.GetSingerDescAsync(currentSingerMid);
                var name = TryGetString(info, "name") ?? currentSingerName;
                var d = TryGetString(desc, "desc") ?? TryGetString(desc, "description") ?? "";
                if (string.IsNullOrEmpty(d))
                    d = TryGetString(info, "desc") ?? "";
                // 歌手介绍在 desc 的 singer_list[0].ex_info.desc；头像在 info 的 base_info.avatar
                if (string.IsNullOrEmpty(d) &&
                    desc.TryGetProperty("singer_list", out var sl) && sl.ValueKind == JsonValueKind.Array &&
                    sl.GetArrayLength() > 0)
                {
                    var s0 = sl[0];
                    if (s0.TryGetProperty("ex_info", out var ex) &&
                        ex.TryGetProperty("desc", out var dv) && dv.ValueKind == JsonValueKind.String)
                        d = dv.GetString() ?? "";
                }
                string avatar = "";
                if (info.TryGetProperty("base_info", out var bi) &&
                    bi.TryGetProperty("avatar", out var av) && av.ValueKind == JsonValueKind.String)
                    avatar = av.GetString() ?? "";
                if (avatar.StartsWith("//", StringComparison.Ordinal))
                    avatar = "https:" + avatar;
                Dispatcher.Invoke(() =>
                {
                    QqSingerName.Text = name;
                    QqSingerDescText.Text = d;
                    if (!string.IsNullOrEmpty(avatar))
                        QqSingerAvatar.Source = new System.Windows.Media.Imaging.BitmapImage(new Uri(avatar));
                });
            }
            catch
            {
                // 信息/介绍拿不到不影响浏览
            }
        }

        private async void QqSingerTab_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button btn || btn.Tag is not string tag)
                return;
            currentSingerTab = tag;
            QqSingerFilterBox.Text = "";
            QqSingerSongList.Visibility = tag == "songs" ? Visibility.Visible : Visibility.Collapsed;
            QqSingerAlbumList.Visibility = tag == "albums" ? Visibility.Visible : Visibility.Collapsed;
            if (tag == "songs" && qqSingerSongs.Count == 0)
                await LoadSingerSongsAsync(reset: true);
            else if (tag == "albums" && qqSingerAlbums.Count == 0)
                await LoadSingerAlbumsAsync(reset: true);
        }

        private async Task LoadSingerSongsAsync(bool reset)
        {
            if (qqSingerLoading) return;
            qqSingerLoading = true;
            try
            {
                var page = reset ? 1 : qqSingerPage + 1;
                var data = await qqApi.GetSingerSongsAsync(currentSingerMid, page, 30);
                var songs = QqApiClient.ParseSongs(data);
                if (reset) qqSingerSongs.Clear();
                foreach (var s in songs) qqSingerSongs.Add(s);
                qqSingerPage = page;
                qqSingerSongsFull = qqSingerSongs.ToList();
                ApplySingerFilter();
                ApplyFavStateToLists();
            }
            catch (Exception ex)
            {
                SetQqStatus(false, "加载歌手歌曲失败：" + ex.Message);
            }
            finally
            {
                qqSingerLoading = false;
            }
        }

        private async Task LoadSingerAlbumsAsync(bool reset)
        {
            if (qqSingerLoading) return;
            qqSingerLoading = true;
            try
            {
                // 歌手专辑数量有限（一般几十张），全量拉取后统一排序：
                // 正经发行专辑（非 Live）优先，同组内按发行时间从新到旧。
                if (reset)
                {
                    qqSingerAlbumPage = 1;
                    qqSingerAlbumsAllLoaded = false;
                }
                var albums = new List<QqAlbumInfo>();
                for (var p = qqSingerAlbumPage; !qqSingerAlbumsAllLoaded; p++)
                {
                    var data = await qqApi.GetSingerAlbumsAsync(currentSingerMid, p, 30);
                    var batch = QqApiClient.ParseAlbums(data);
                    if (batch.Count == 0)
                    {
                        qqSingerAlbumsAllLoaded = true;
                        break;
                    }
                    albums.AddRange(batch);
                    qqSingerAlbumPage = p;
                    var total = GetQqTotal(data);
                    if (total > 0 && albums.Count >= total)
                    {
                        qqSingerAlbumsAllLoaded = true;
                        break;
                    }
                }
                albums.Sort((x, y) =>
                {
                    var lx = x.IsLive;
                    var ly = y.IsLive;
                    if (lx != ly) return lx ? 1 : -1;
                    return string.Compare(y.Date, x.Date, StringComparison.Ordinal);
                });
                if (reset)
                    qqSingerAlbums.Clear();
                foreach (var a in albums) qqSingerAlbums.Add(a);
                qqSingerAlbumsFull = qqSingerAlbums.ToList();
                ApplyFavStateToAlbums();
                ApplySingerFilter();
            }
            catch (Exception ex)
            {
                SetQqStatus(false, "加载歌手专辑失败：" + ex.Message);
            }
            finally
            {
                qqSingerLoading = false;
            }
        }

        /// <summary>歌手页列表滚到底时加载下一页（歌曲/专辑分别翻页）。</summary>
        private void QqSingerScroller_ScrollChanged(object sender, ScrollChangedEventArgs e)
        {
            if (e.VerticalOffset + e.ViewportHeight >= e.ExtentHeight - 120)
            {
                if (currentSingerTab == "songs")
                    _ = LoadSingerSongsAsync(reset: false);
                else
                    _ = LoadSingerAlbumsAsync(reset: false);
            }
        }

        private void QqSingerFilter_Changed(object sender, TextChangedEventArgs e)
        {
            if (!loaded) return;
            ApplySingerFilter();
        }

        private void ApplySingerFilter()
        {
            var kw = QqSingerFilterBox.Text.Trim();
            if (currentSingerTab == "songs")
            {
                qqSingerSongs.Clear();
                foreach (var s in qqSingerSongsFull)
                {
                    if (string.IsNullOrEmpty(kw) ||
                        s.Title.Contains(kw, StringComparison.OrdinalIgnoreCase) ||
                        s.Singer.Contains(kw, StringComparison.OrdinalIgnoreCase))
                        qqSingerSongs.Add(s);
                }
            }
            else
            {
                qqSingerAlbums.Clear();
                foreach (var a in qqSingerAlbumsFull)
                {
                    if (string.IsNullOrEmpty(kw) ||
                        a.Name.Contains(kw, StringComparison.OrdinalIgnoreCase) ||
                        a.Singer.Contains(kw, StringComparison.OrdinalIgnoreCase))
                        qqSingerAlbums.Add(a);
                }
            }
        }

        private async void QqSingerSong_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is QqSongItem song)
            {
                // 播放当前页面歌曲时把整个歌手歌曲列表加入播放队列（缓冲列表，不全量重建）
                await PlayQqSongWithPlaylistAsync(song, qqSingerSongs.ToList());
            }
        }

        private async void QqSingerAlbum_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is QqAlbumInfo album)
            {
                qqBackTarget = "singer";
                await OpenAlbumAsync(album);
            }
        }

        // ---------------- 专辑 ----------------

        private async void QqAlbum_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is QqAlbumInfo album)
            {
                qqBackTarget = "favalbums";
                await OpenAlbumAsync(album);
            }
        }

        private async Task OpenAlbumAsync(QqAlbumInfo album)
        {
            currentAlbumMid = album.AlbumMid;
            currentPlaylistName = album.Name;
            qqTab = "album";
            QqSearchOverlay.Visibility = Visibility.Collapsed;
            QqSingerView.Visibility = Visibility.Collapsed;
            QqHomeScroller.Visibility = Visibility.Collapsed;
            QqPlaylistScroller.Visibility = Visibility.Collapsed;
            QqAlbumScroller.Visibility = Visibility.Collapsed;
            QqSongScroller.Visibility = Visibility.Visible;
            QqBackToPlaylists.Visibility = Visibility.Collapsed;
            QqHomeBtn.Visibility = Visibility.Visible;
            QqEmptyHint.Visibility = Visibility.Collapsed;
            await LoadQqAlbumSongsAsync(reset: true);
            UpdateRandomPlayVisibility();
        }

        private async Task LoadQqAlbumSongsAsync(bool reset)
        {
            try
            {
                // GetAlbumSongList 接口返回倒序：一次性拉取全部曲目后整体反转，恢复专辑正序
                var all = new List<QqSongItem>();
                for (var p = 1; p <= 50; p++)
                {
                    var data = await qqApi.GetAlbumSongsAsync(currentAlbumMid, p, 100);
                    var batch = QqApiClient.ParseSongs(data);
                    if (batch.Count == 0)
                        break;
                    all.AddRange(batch);
                    var total = GetQqTotal(data);
                    if (total > 0 && all.Count >= total)
                        break;
                    if (batch.Count < 100)
                        break;
                }
                all.Reverse();
                qqSongs.Clear();
                qqShownMids.Clear();
                foreach (var song in all)
                    qqSongs.Add(song);
                qqPage = 1;
                qqHasMore = false;
                QqListTitle.Text = currentPlaylistName;
                QqFilterBox.Text = "";
                SyncQqSongsFull();
                QqListCountText.Text = $"{qqSongs.Count} 首";
                if (qqSongs.Count == 0)
                {
                    QqEmptyHint.Text = "专辑为空";
                    QqEmptyHint.Visibility = Visibility.Visible;
                }
            }
            catch (Exception ex)
            {
                SetQqStatus(false, "加载专辑失败：" + ex.Message);
            }
        }

        private async Task LoadQqFavAlbumsAsync()
        {
            try
            {
                var data = await qqApi.GetFavAlbumsAsync(1, 30);
                LogQqDebug("收藏专辑返回: " + data.ToString().Length + " 字符, 前 600: " +
                           data.ToString().Substring(0, Math.Min(600, data.ToString().Length)));
                qqAlbums.Clear();
                var parsed = QqApiClient.ParseAlbums(data);
                LogQqDebug("收藏专辑解析: " + parsed.Count + " 张; 首张 Singer=[" +
                           (parsed.Count > 0 ? parsed[0].Singer : "空") + "]");
                foreach (var a in parsed)
                    qqAlbums.Add(a);
                ApplyFavStateToAlbums();
                QqListCountText.Text = $"{qqAlbums.Count} 张专辑";
            }
            catch (Exception ex)
            {
                LogQqDebug("加载收藏专辑失败: " + ex.Message);
                SetQqStatus(false, "加载收藏专辑失败：" + ex.Message);
            }
        }

        private static string? TryGetString(JsonElement el, string name) =>
            el.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String
                ? v.GetString()
                : null;

        private Task LoadQqNextPageAsync() => qqTab switch
        {
            "fav" => LoadQqFavAsync(reset: false),
            "playlists" when currentPlaylistId > 0 => LoadQqPlaylistSongsAsync(reset: false),
            "album" when currentAlbumMid.Length > 0 => Task.CompletedTask, // 专辑曲目已全量加载
            "search" => DoQqSearchAsync(reset: false),
            "radar" => LoadMoreRadarAsync(),
            _ => Task.CompletedTask
        };



        private int currentQueueIndex = -1;
        private int playGeneration;   // 播放会话代际号：切歌后使旧的下载/播放回调失效
        private bool cacheBusy;
        private DateTime lastPlayStart = DateTime.MinValue;
        private bool playlistAutoExtend;
        private int qqListTotal;
        private readonly Random shuffleRng = new();
        private bool queueSelectMode;
        private DateTime lastQueueSave = DateTime.MinValue;

        private string CacheDir => settings.ResolvedCachePath;

        private async void QqSong_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is QqSongItem song)
                await PlayQqSongWithPlaylistAsync(song);
        }

        /// <summary>列表封面加载失败（无专辑图/404）时隐藏图片，露出占位音符图标。</summary>
        private void QqCover_ImageFailed(object sender, ExceptionRoutedEventArgs e)
        {
            if (sender is Image img) img.Visibility = Visibility.Collapsed;
        }

        /// <summary>歌手头像加载失败时隐藏，露出占位头像图标。</summary>
        private void QqAvatar_ImageFailed(object sender, ExceptionRoutedEventArgs e)
        {
            if (sender is Image img) img.Visibility = Visibility.Collapsed;
        }

        /// <summary>单击播放：以点击的歌为首入队已加载列表（动态/每日列表即全量），随后播放并预缓存下一首。</summary>
        /// <summary>
        /// 单击播放。pageList 非空时（歌手页/搜索弹窗）以点击歌为首、把整个页面列表入队
        /// （列表缓冲：只入队当前已加载的页面歌曲，不做全量重建）。
        /// </summary>
        private async Task PlayQqSongWithPlaylistAsync(QqSongItem song, IReadOnlyList<QqSongItem>? pageList = null)
        {
            lastPlayStart = DateTime.UtcNow;
            StopOutput();

            queueTracks.Clear();
            if (pageList != null)
            {
                var ordered = new List<QqSongItem> { song };
                foreach (var s in pageList)
                    if (s.Mid != song.Mid)
                        ordered.Add(s);
                foreach (var s in ordered)
                    queueTracks.Add(MakeQqTrack(s));
                currentQueueIndex = 0;
            }
            else if (qqTab == "radar" || qqTab == "daily30")
            {
                // 动态/每日列表即全量：点击的歌放队首，其余按列表顺序跟随
                var list = new List<QqSongItem>(qqSongs);
                var hitIdx = list.FindIndex(s => s.Mid == song.Mid);
                if (hitIdx > 0)
                {
                    list.RemoveAt(hitIdx);
                    list.Insert(0, song);
                }
                else if (hitIdx < 0)
                {
                    list.Insert(0, song);
                }
                foreach (var s in list)
                    queueTracks.Add(MakeQqTrack(s));
                currentQueueIndex = 0;
            }
            else
            {
                // 先用已加载列表建立临时队列，立即开始播放点击的歌（不等全量）
                // 保持列表排序：直接加载整个歌单从头构建队列，播放定位到点击的歌
                SetStatus("正在加载整个歌单…");
                var full = await LoadEntireQqListAsync();
                if (full.Count == 0)
                {
                    SetStatus("歌单为空");
                    return;
                }
                var hitIdx = full.FindIndex(s => s.Mid == song.Mid);
                foreach (var s in full)
                    queueTracks.Add(MakeQqTrack(s));
                currentQueueIndex = hitIdx >= 0 ? hitIdx : 0;
            }
            ApplyFavStateToLists();
            UpdateQueueEmptyHint();
            await PlayCurrentQueueAsync();

        }

        /// <summary>后台拉取整个歌单，按顺序/随机重建播放队列，当前播放的歌（按 Mid）保持队首。</summary>

        private static QueueTrack MakeQqTrack(QqSongItem s) => new()
        {
            Title = s.Title,
            Singer = s.Singer,
            Source = "QQ音乐",
            QqMid = s.Mid,
            AlbumMid = s.AlbumMid,
            QqSong = s
        };

        /// <summary>播放队列当前项；QQ 项未下载时先下载到缓存目录。</summary>
        private async Task PlayCurrentQueueAsync()
        {
            var gen = ++playGeneration; // 本代播放目标；后续切歌会递增，使本代过期的下载/播放回调失效
            if (currentQueueIndex < 0 || currentQueueIndex >= queueTracks.Count)
            {
                SetPlaying(false);
                return;
            }
            var track = queueTracks[currentQueueIndex];
            foreach (var t in queueTracks) t.IsCurrent = false;
            track.IsCurrent = true;
            UpdateQueueEmptyHint();
            QueueList.ScrollIntoView(track);

            // 缓存文件已被清理时重新下载
            if (!string.IsNullOrEmpty(track.Path) && !File.Exists(track.Path))
                track.Path = "";

            if (string.IsNullOrEmpty(track.Path) && !string.IsNullOrEmpty(track.QqMid))
            {
                // 先查缓存：文件名带 mid 前缀，命中则直接播放
                var cachedFile = FindCachedFile(track.QqMid);
                if (cachedFile != null)
                {
                    track.Path = cachedFile;
                    ApplyCachedInfo(track, cachedFile);
                }
                else
                {
                    // 切歌/起播时若无本地文件：暂停播放态并提示加载中，下载完成后再播放
                    isPlaying = false;
                    SyncPlayIcon();
                    UpdateNowPlayingInfo(track.Title, track.Singer, track.Source);
                    SetStatus($"正在加载：{track.Title}…");
                    var result = await DownloadQqSongAsync(track.QqSong!, track);
                    if (gen != playGeneration) return; // 用户已切到其它曲目，丢弃本次下载结果
                    if (result == null)
                    {
                        SetStatus($"下载失败，跳过：{track.Title}");
                        currentQueueIndex++;
                        await PlayCurrentQueueAsync();
                        return;
                    }
                    // 下载期间队列可能被后台全量重建，用 Mid 定位当前项
                    if (gen != playGeneration) return;
                    var target = queueTracks.FirstOrDefault(t => !string.IsNullOrEmpty(t.QqMid) && t.QqMid == track.QqMid) ?? track;
                    track = target;
                    track.Path = result.Value.File;
                    track.QualityName = result.Value.QualityName;
                    track.Source = "QQ音乐 · " + track.QualityName;
                    track.CacheState = "";
                    EnforceCacheLimit();
                }
            }

            if (string.IsNullOrEmpty(track.Path))
            {
                currentQueueIndex++;
                await PlayCurrentQueueAsync();
                return;
            }

            track.DownloadProgress = 0;
            // 播放前同步给相邻已缓存项打标（不依赖下载锁，保证上一首/下一首立即显示已缓存）
            MarkCachedIfReady(currentQueueIndex + 1);
            MarkCachedIfReady(currentQueueIndex - 1);
            await PlayQueueTrackAsync(track);
            UpdateJumpButtons();
            _ = CacheAdjacentTracksAsync();

            // 无限推荐：播放接近队列末尾时提前拉取下一页，播完即可无缝续播
            if (qqTab == "radar" && radarHasMore && !radarLoading
                && currentQueueIndex >= queueTracks.Count - 3)
            {
                _ = LoadMoreRadarAsync();
            }
        }

        /// <summary>播放结束自动切下一首，并删除上一首缓存文件。</summary>
        private async Task PlayNextQueueAsync()
        {
            if (currentQueueIndex >= 0 && currentQueueIndex < queueTracks.Count)
                // 切走后不再标记为待播放，缓存文件保留在缓冲区内由 EnforceCacheLimit 按上限清理
                queueTracks[currentQueueIndex].CacheState = "";
            currentQueueIndex++;
            if (currentQueueIndex >= queueTracks.Count)
            {
                // 队列播完：顺序模式下若列表还有下一页则续载后继续
                if (playlistAutoExtend || qqTab == "radar")
                {
                    await ExtendQueueAsync();
                    if (currentQueueIndex < queueTracks.Count)
                    {
                        await PlayCurrentQueueAsync();
                        return;
                    }
                }
                SetPlaying(false);
                SetStatus("队列播放完毕");
                return;
            }
            await PlayCurrentQueueAsync();
        }

        /// <summary>队列播到末尾时，加载当前 QQ 列表下一页并追加到队列（顺序模式续载）。</summary>
        private async Task ExtendQueueAsync()
        {
            try
            {
                // 追加起点用队列长度而非列表长度：无限推荐的预载只填了列表没进队列，
                // 用列表长度做起点会跳过预载的歌曲
                var oldCount = queueTracks.Count;
                if (qqTab == "radar")
                {
                    // 无限推荐：等当前在途请求完成（已预载的歌曲已在列表里），再拉下一页
                    while (radarLoading) await Task.Delay(150);
                    await LoadMoreRadarAsync();
                }
                else
                {
                    await LoadQqNextPageAsync();
                }
                if (qqSongs.Count > oldCount)
                {
                    for (var i = oldCount; i < qqSongs.Count; i++)
                        queueTracks.Add(MakeQqTrack(qqSongs[i]));
                    playlistAutoExtend = qqTab == "radar" ? radarHasMore : qqHasMore;
                    LogQqDebug($"队列续载 {qqSongs.Count - oldCount} 首，共 {queueTracks.Count} 首");
                }
                else
                {
                    playlistAutoExtend = false;
                }
            }
            catch (Exception ex)
            {
                LogQqDebug("队列续载异常: " + ex.Message);
                playlistAutoExtend = false;
            }
        }

        private async Task PlayQueueTrackAsync(QueueTrack track)
        {
            try
            {
                if (!IsPlayableAudioFile(track.Path) ||
                    (UseHqOutput && !IsHqPlayableAudioFile(track.Path)))
                {
                    SetStatus($"文件格式异常，跳过：{Path.GetFileName(track.Path)}");
                    LogQqDebug("非音频文件，跳过: " + track.Path);
                    _ = PlayNextQueueAsync();
                    return;
                }
                if (UseHqOutput)
                {
                    if (!await Task.Run(() => hqPlayer.PlayFile(track.Path)))
                    {
                        SetStatus("HQPlayer 播放失败：请确认 HQPlayer 已启动、输出为 NAA");
                        LogQqDebug("HQPlayer PlayFile 失败: " + track.Path);
                        _ = PlayNextQueueAsync();
                        return;
                    }
                    hqIsPlaying = true;
                    hqPrevState = -1;
                    lastHqPoll = DateTime.MinValue;
                    hqPlayRequestTime = DateTime.UtcNow;
                }
                else
                {
                    systemPlayer.Play(track.Path);
                }
                isPlaying = true;
                SyncPlayIcon();
                if (track.QqSong != null)
                {
                    UpdateNowPlayingInfo(track.QqSong.Title, track.QqSong.Singer, track.Source);
                    ShowNoLyrics();
                    ClearQqCover();
                    _ = LoadQqSongMetaAsync(track.QqSong);
                }
                else
                {
                    UpdateNowPlayingInfo(track.Title, track.Singer, track.Source);
                }
                SetStatus(UseHqOutput
                    ? $"HQPlayer 播放：{Path.GetFileName(track.Path)}"
                    : $"播放：{Path.GetFileName(track.Path)}");
            }
            catch (Exception ex)
            {
                LogQqDebug("播放异常: " + ex);
                _ = PlayNextQueueAsync();
            }
        }

        /// <summary>校验文件头是否为可播放音频（排除 QMC 加密残留等非法文件）。</summary>
        private static bool IsPlayableAudioFile(string path)
        {
            try
            {
                using var fs = File.OpenRead(path);
                  var head = new byte[16];
                  var n = fs.Read(head, 0, head.Length);
                  if (n < 4) return false;
                  if (head[0] == 0x66 && head[1] == 0x4C && head[2] == 0x61 && head[3] == 0x43) return true; // fLaC
                  if (head[0] == 0x49 && head[1] == 0x44 && head[2] == 0x33) return true; // ID3 (MP3)
                  if (head[0] == 0xFF && (head[1] & 0xE0) == 0xE0) return true; // MP3
                  if (head[0] == 0x4F && head[1] == 0x67 && head[2] == 0x67 && head[3] == 0x53) return true; // OggS
                if (n >= 12 && head[4] == 0x66 && head[5] == 0x74 && head[6] == 0x79 && head[7] == 0x70) return true; // ftyp (m4a)
                if (head[0] == 0x52 && head[1] == 0x49 && head[2] == 0x46 && head[3] == 0x46) return true; // RIFF (wav/aiff)
                if (head[0] == 0x44 && head[1] == 0x53 && head[2] == 0x44 && head[3] == 0x20) return true; // DSD (dsf)
                if (head[0] == 0x46 && head[1] == 0x52 && head[2] == 0x4D && head[3] == 0x38) return true; // FRM8 (dff)
                return false;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>HQPlayer 不支持的容器（Ogg/Vorbis、M4A/AAC）在 NAA 输出时直接跳过。</summary>
        private static bool IsHqPlayableAudioFile(string path)
        {
            try
            {
                using var fs = File.OpenRead(path);
                  var head = new byte[16];
                  var n = fs.Read(head, 0, head.Length);
                  if (n < 4) return false;
                  if (head[0] == 0x66 && head[1] == 0x4C && head[2] == 0x61 && head[3] == 0x43) return true; // fLaC
                  if (head[0] == 0x49 && head[1] == 0x44 && head[2] == 0x33) return true; // ID3 (MP3)
                  if (head[0] == 0xFF && (head[1] & 0xE0) == 0xE0) return true; // MP3
                  if (head[0] == 0x52 && head[1] == 0x49 && head[2] == 0x46 && head[3] == 0x46) return true; // RIFF (wav/aiff)
                if (head[0] == 0x44 && head[1] == 0x53 && head[2] == 0x44 && head[3] == 0x20) return true; // DSD (dsf)
                if (head[0] == 0x46 && head[1] == 0x52 && head[2] == 0x4D && head[3] == 0x38) return true; // FRM8 (dff)
                return false;
            }
            catch
            {
                return false;
              }
          }

          /// <summary>检测文件内容是否为 MP3（带或不带 ID3 标签）。</summary>
          private static bool IsMp3Content(string path)
          {
              try
              {
                  using var fs = File.OpenRead(path);
                  var head = new byte[4];
                  var n = fs.Read(head, 0, head.Length);
                  if (n < 2) return false;
                  if (head[0] == 0x49 && head[1] == 0x44 && head[2] == 0x33) return true; // ID3
                  return head[0] == 0xFF && (head[1] & 0xE0) == 0xE0; // MP3 帧头
              }
              catch
              {
                  return false;
              }
          }

          /// <summary>缓存相邻歌曲：先快速标记已缓存的上一首/下一首，再补下载缺失的（先下一首，再上一首）。</summary>
        private async Task CacheAdjacentTracksAsync()
        {
            if (cacheBusy) return;
            cacheBusy = true;
            try
            {
                var baseIdx = currentQueueIndex;
                var ahead = Math.Max(1, settings.PrecacheCount); // 提前缓存的下一首数量
                // 先快速打标：接下来 ahead 首 + 上一首只要有缓存文件就显示「已缓存」
                for (var k = 1; k <= ahead; k++) MarkCachedIfReady(baseIdx + k);
                MarkCachedIfReady(baseIdx - 1);
                // 补下载缺失的：按顺序先缓存 ahead 首下一首（串行避免抢占带宽），再缓存上一首；
                // 期间用户切歌则中止（新的一次 CacheAdjacentTracksAsync 会重新规划）
                for (var k = 1; k <= ahead; k++)
                {
                    await CacheOneAsync(baseIdx + k);
                    if (currentQueueIndex != baseIdx) return;
                }
                if (currentQueueIndex == baseIdx)
                    await CacheOneAsync(baseIdx - 1);
            }
            finally
            {
                cacheBusy = false;
            }
        }

        /// <summary>相邻项若已有缓存文件则直接打上「已缓存」标记。</summary>
        private void MarkCachedIfReady(int idx)
        {
            if (idx < 0 || idx >= queueTracks.Count) return;
            var t = queueTracks[idx];
            if (string.IsNullOrEmpty(t.QqMid) || idx == currentQueueIndex) return;
            if (!string.IsNullOrEmpty(t.Path) && File.Exists(t.Path))
            {
                t.CacheState = "已缓存";
            }
            else
            {
                var cf = FindCachedFile(t.QqMid);
                if (cf != null)
                {
                    t.Path = cf;
                    ApplyCachedInfo(t, cf);
                    t.CacheState = "已缓存";
                }
            }
        }

        private async Task CacheOneAsync(int idx)
        {
            if (idx < 0 || idx >= queueTracks.Count) return;
            var track = queueTracks[idx];
            if (string.IsNullOrEmpty(track.QqMid)) return;
            if (idx == currentQueueIndex) return; // 当前播放项不标缓存

            // 已有有效文件：标记为已缓存（供回退），不重复下载
            if (!string.IsNullOrEmpty(track.Path) && File.Exists(track.Path))
            {
                track.CacheState = "已缓存";
                return;
            }

            // 命中缓存文件（文件名带 mid 前缀）直接标记，不重复下载
            var cachedFile = FindCachedFile(track.QqMid);
            if (cachedFile != null)
            {
                var t0 = queueTracks.FirstOrDefault(t => !string.IsNullOrEmpty(t.QqMid) && t.QqMid == track.QqMid) ?? track;
                t0.Path = cachedFile;
                ApplyCachedInfo(t0, cachedFile);
                t0.CacheState = "已缓存";
                return;
            }

            LogQqDebug($"预缓存: {track.Title}");
            var result = await DownloadQqSongAsync(track.QqSong!, track);
            if (result != null)
            {
                var target = queueTracks.FirstOrDefault(t => !string.IsNullOrEmpty(t.QqMid) && t.QqMid == track.QqMid) ?? track;
                var curIdx = queueTracks.IndexOf(target);
                // 下载期间播放可能已推进：该歌不再位于当前/相邻位置则视为过时，删除文件
                var span = Math.Max(1, settings.PrecacheCount) + 1;
                var near = curIdx == currentQueueIndex || Math.Abs(curIdx - currentQueueIndex) <= span;
                if (near)
                {
                    target.Path = result.Value.File;
                    target.QualityName = result.Value.QualityName;
                    target.Source = "QQ音乐 · " + target.QualityName;
                    target.CacheState = "已缓存";
                    EnforceCacheLimit();
                    LogQqDebug($"预缓存完成: {Path.GetFileName(target.Path)}");
                }
                else
                {
                    try { if (File.Exists(result.Value.File)) File.Delete(result.Value.File); } catch { }
                    LogQqDebug($"预缓存已过时，删除: {Path.GetFileName(result.Value.File)}");
                }
            }
        }

        /// <summary>下载 QQ 歌曲到缓存目录并解密，返回 (文件路径, 音质描述)。失败返回 null。</summary>
        /// <remarks>按音质档位循环：Hi-Res(RSM1) 下载/解密失败时自动降级到无损/普通/标准。</remarks>
        private async Task<(string File, string QualityName)?> DownloadQqSongAsync(QqSongItem song, QueueTrack? progressTrack)
        {
            // 音质档（白名单）：Hi-Res=RSM1 加密FLAC(26, 原生96k/24bit) > 无损=F000 FLAC(7) > 普通=MP3 320(12) > 标准=MP3 128(13)
            // （不用 OGG640：HQPlayer 不支持 Ogg/Vorbis，下载了也放不了）
            // 禁选一切升频/营销档：AI00 臻品母带(1)、DTS(0)、臻品音质(2)、全景声(3/4)、杜比(5)、NAC(6)、360空间音频
            var (chain, chainNames) = QqApiQualityCombo.SelectedIndex switch
            {
                0 => (new[] { 26, 7, 12, 13 }, new[] { "Hi-Res", "无损", "普通", "标准" }),
                1 => (new[] { 7, 12, 13 }, new[] { "无损", "普通", "标准" }),
                _ => (new[] { 12, 13 }, new[] { "普通", "标准" })
            };
            LogQqDebug($"QQ 获取音源: {song.Title} / {song.Singer} / mid={song.Mid}");
            for (var attempt = 0; attempt < 2; attempt++)
            {
                for (var i = 0; i < chain.Length; i++)
                {
                    SetStatus($"QQ 获取音源…（{song.Title}）");
                    SongUrlResult? urlInfo;
                    try
                    {
                        urlInfo = await qqApi.GetSongUrlAsync(song.Mid, chain[i], song.MediaMid);
                    }
                    catch (Exception ex)
                    {
                        LogQqDebug($"音质 {chainNames[i]} 获取异常: {ex.Message}");
                        continue;
                    }
                    if (urlInfo == null)
                    {
                        LogQqDebug($"音质 {chainNames[i]} 不可用，尝试下一档");
                        continue;
                    }
                    var result = await TryDownloadQualityAsync(song, urlInfo, chainNames[i], progressTrack);
                    if (result != null)
                        return result;
                    LogQqDebug($"音质 {chainNames[i]} 下载失败，尝试下一档");
                }
                if (!qqApi.IsLoggedIn)
                {
                    LogQqDebug("未登录 QQ，无法获取音源，请先在设置中登录");
                    break;
                }
                // 音源全空或全部失败常见原因是登录凭证过期（接口返回 code=0 但 purl 为空，
                // 不会触发 401 自动刷新）：刷新一次凭证后重试整条音质链。
                LogQqDebug("音源全空或全部失败，刷新登录凭证后重试");
                if (!await qqApi.TryRefreshAsync())
                    break;
            }
            LogQqDebug($"API 音源全部失败: {song.Mid}");
            if (progressTrack != null) HideQueueRowProgress(progressTrack);
            return null;
        }

        /// <summary>按指定音质下载、解密并缓存 QQ 歌曲；失败返回 null（由调用方降级下一档）。</summary>
        private async Task<(string File, string QualityName)?> TryDownloadQualityAsync(
            QqSongItem song, SongUrlResult urlInfo, string qualityName, QueueTrack? progressTrack)
        {
            try
            {
                LogQqDebug($"API 音源: {urlInfo.Filename}");
                var streamUrl = urlInfo.Purl.StartsWith("http", StringComparison.OrdinalIgnoreCase)
                    ? urlInfo.Purl
                    : "https://dl.stream.qqmusic.qq.com/" + urlInfo.Purl;

                SetStatus($"QQ 下载中…（{song.Title}）");
                if (progressTrack != null)
                    ShowQqRowProgress(song, 0);
                if (progressTrack != null)
                    ShowQueueRowProgress(progressTrack, 0);
                byte[]? bytes;
                try
                {
                    bytes = await qqApi.DownloadSegmentedAsync(streamUrl, progress: (done, total) =>
                    {
                        if (total <= 0) return;
                        var percent = Math.Min(1.0, done / (double)total);
                        lock (qqDownloadProgress) qqDownloadProgress[song.Mid] = percent;
                        var track = progressTrack;
                        _ = Dispatcher.BeginInvoke(DispatcherPriority.Normal, new Action(() =>
                        {
                            try
                            {
                                if (track != null)
                                {
                                    track.DownloadProgress = percent;
                                    ShowQueueRowProgress(track, percent);
                                }
                                UpdateQqRowProgress(song, percent);
                            }
                            catch { }
                        }));
                    });
                }
                catch
                {
                    if (progressTrack != null)
                    {
                        HideQqRowProgress();
                        HideQueueRowProgress(progressTrack);
                    }
                    throw;
                }
                if (bytes == null || bytes.Length == 0)
                {
                    if (progressTrack != null)
                    {
                        HideQqRowProgress();
                        HideQueueRowProgress(progressTrack);
                    }
                    LogQqDebug("API 音频下载为空");
                    return null;
                }
                LogQqDebug($"下载完成: {bytes.Length} 字节");
                lock (qqDownloadProgress) qqDownloadProgress.Remove(song.Mid);
                if (progressTrack != null)
                    progressTrack.DownloadProgress = 1.0;
                var audioInfo = QqApiClient.ParseAudioInfo(bytes, urlInfo.Filename);
                if (!string.IsNullOrEmpty(audioInfo))
                {
                    // 档位按实际音源判定：FLAC>48k=Hi-Res；FLAC≤48k=无损；MP3=普通（不依赖请求链）
                    qualityName = ClassifyQualityName(audioInfo, qualityName);
                    qualityName += " · " + audioInfo;
                    LogQqDebug($"实际音质: {audioInfo}");
                }
                if (progressTrack != null)
                {
                    _ = Dispatcher.BeginInvoke(DispatcherPriority.Normal, new Action(() =>
                    {
                        try { UpdateQqRowProgress(song, 1.0); }
                        catch { }
                    }));
                    _ = DelayHideQqRowProgressAsync(300, song);
                    if (progressTrack != null)
                    {
                        ShowQueueRowProgress(progressTrack, 1.0);
                        _ = HideQueueRowProgressDelayedAsync(progressTrack, 300);
                    }
                }

                var dot = urlInfo.Filename.LastIndexOf('.');
                var ext = dot >= 0 ? urlInfo.Filename[(dot + 1)..] : "mp3";
                var isHires = urlInfo.Filename.Contains("RSM1", StringComparison.OrdinalIgnoreCase);
                var needPythonDecrypt = isHires;
                if (!isHires)
                {
                    var dec = QQMusicDownloader.MaybeDecrypt(bytes, ext);
                    if (dec != null)
                    {
                        bytes = dec.Value.data;
                        ext = dec.Value.ext;
                    }
                    else if (QQMusicDownloader.LooksEncrypted(bytes))
                        needPythonDecrypt = true;
                }
                if (needPythonDecrypt)
                {
                    // Hi-Res RSM1 裸数据会被 MaybeDecrypt 的 mgg 探测误判成假 OGG，必须跳过 QMC 探测，
                    // 用 CgiGetEDownUrl 获取 ekey 拼接（PcV1Legacy）后交给 qmc_decrypt.py（纯 Python）离线解密
                    LogQqDebug("QMC 常规解密不可用，走 ekey + 内置 Python 解密: " + song.Title);
                    var tmpMflac = Path.Combine(CacheDir, $"{song.Mid}_{DateTime.Now:HHmmssfff}.tmp.mflac");
                    var tmpOutDir = Path.Combine(CacheDir, $"tmpdec_{DateTime.Now:HHmmssfff}");
                    Directory.CreateDirectory(tmpOutDir);
                    await File.WriteAllBytesAsync(tmpMflac, bytes);
                    // 裸数据版（无内嵌 ekey）：从 CgiGetEDownUrl 获取 ekey 拼接到文件尾（PcV1Legacy），再离线解密
                    var ekey = await qqApi.GetEkeyAsync(song.Mid, song.MediaMid);
                    if (!string.IsNullOrEmpty(ekey))
                    {
                        await File.AppendAllTextAsync(tmpMflac, ekey);
                        var pad = (4 - ekey.Length % 4) % 4;
                        using (var fs = new FileStream(tmpMflac, FileMode.Append))
                        {
                            fs.Write(new byte[pad], 0, pad);
                            fs.Write(BitConverter.GetBytes(ekey.Length), 0, 4);
                        }
                        LogQqDebug($"已拼接 ekey({ekey.Length} 字符)");
                    }
                    var pyOut = await Task.Run(() => TryDecryptMflacPython(tmpMflac, tmpOutDir));
                    try { File.Delete(tmpMflac); } catch { }
                    if (pyOut != null)
                    {
                        bytes = await File.ReadAllBytesAsync(pyOut);
                        ext = "flac";
                        LogQqDebug($"Python 解密成功: {Path.GetFileName(pyOut)} ({bytes.Length} 字节)");
                        try { Directory.Delete(tmpOutDir, true); } catch { }
                    }
                    else
                    {
                        LogQqDebug("下载文件仍为加密格式（解密失败），跳过: " + song.Title);
                        if (progressTrack != null) HideQueueRowProgress(progressTrack);
                        return null;
                    }
                    try { Directory.Delete(tmpOutDir, true); } catch { }
                }

                Directory.CreateDirectory(CacheDir);
                // OGG（Vorbis）HQPlayer 不支持：用便携 Python + soundfile 转成 FLAC 再缓存，来源信息仍保留 OGG 标注
                if (ext.Equals("ogg", StringComparison.OrdinalIgnoreCase))
                {
                    var tmpOgg = Path.Combine(CacheDir, $"{song.Mid}_{DateTime.Now:HHmmssfff}.tmp.ogg");
                    await File.WriteAllBytesAsync(tmpOgg, bytes);
                    var flacFile = Path.Combine(CacheDir, $"{song.Mid}_{SanitizeFileName(song.Title)}.flac");
                    var converted = await Task.Run(() => TryConvertOggToFlac(tmpOgg, flacFile));
                    try { File.Delete(tmpOgg); } catch { }
                    if (converted)
                    {
                        LogQqDebug($"OGG 已转 FLAC（HQPlayer 可播）: {Path.GetFileName(flacFile)}");
                        return (flacFile, qualityName);
                    }
                    LogQqDebug("OGG 转 FLAC 失败，保留原文件: " + song.Title);
                    var oggFile = Path.Combine(CacheDir, $"{song.Mid}_{SanitizeFileName(song.Title)}.ogg");
                    await File.WriteAllBytesAsync(oggFile, bytes);
                    return (oggFile, qualityName);
                }
                var file = Path.Combine(CacheDir, $"{song.Mid}_{SanitizeFileName(song.Title)}.{ext}");
                await File.WriteAllBytesAsync(file, bytes);
                LogQqDebug($"已缓存: {file} ({bytes.Length} 字节)");
                return (file, qualityName);
            }
            catch (Exception ex)
            {
                LogQqDebug("QQ 下载异常: " + ex);
                if (progressTrack != null) HideQueueRowProgress(progressTrack);
                return null;
            }
        }

        /// <summary>用随包便携 Python + soundfile 把 OGG/Vorbis 转成 FLAC（无损，HQPlayer 可播放）。</summary>
        private static bool TryConvertOggToFlac(string oggPath, string flacPath)
        {
            try
            {
                var root = Path.Combine(AppContext.BaseDirectory, "qqapi");
                var python = Path.Combine(root, "python", "python.exe");
                if (!File.Exists(python))
                    python = Path.Combine(root, "venv", "Scripts", "python.exe");
                var script = Path.Combine(root, "app", "ogg2flac.py");
                if (!File.Exists(python) || !File.Exists(script))
                    return false;
                using var proc = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = python,
                        Arguments = $"\"{script}\" \"{oggPath}\" \"{flacPath}\"",
                        UseShellExecute = false,
                        CreateNoWindow = true
                    }
                };
                if (!proc.Start())
                    return false;
                if (!proc.WaitForExit(60000))
                {
                    try { proc.Kill(); } catch { }
                    return false;
                }
                return File.Exists(flacPath) && new FileInfo(flacPath).Length > 44;
            }
            catch
            {
                try { if (File.Exists(flacPath)) File.Delete(flacPath); } catch { }
                return false;
            }
        }

        /// <summary>用随包便携 Python + qmc_decrypt.py 离线解密 mflac（支持内嵌 ekey 的旧版 Hi-Res 文件）。
        /// 返回解密后的文件路径；失败返回 null。</summary>
        private static string? TryDecryptMflacPython(string srcPath, string outDir)
        {
            try
            {
                var root = Path.Combine(AppContext.BaseDirectory, "qqapi");
                var python = Path.Combine(root, "python", "python.exe");
                if (!File.Exists(python))
                    python = Path.Combine(root, "venv", "Scripts", "python.exe");
                var script = Path.Combine(root, "app", "qmc_decrypt.py");
                if (!File.Exists(python) || !File.Exists(script))
                {
                    LogQqDebug("缺少 qmc_decrypt.py，无法解密 mflac");
                    return null;
                }
                using var proc = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = python,
                        Arguments = $"\"{script}\" \"{srcPath}\" -o \"{outDir}\" -f",
                        UseShellExecute = false,
                        CreateNoWindow = true
                    }
                };
                if (!proc.Start())
                    return null;
                if (!proc.WaitForExit(60000))
                {
                    try { proc.Kill(); } catch { }
                    return null;
                }
                var name = Path.GetFileNameWithoutExtension(srcPath) + ".flac";
                var dst = Path.Combine(outDir, name);
                return File.Exists(dst) && new FileInfo(dst).Length > 44 ? dst : null;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>按实际音频信息归类档位：FLAC>48k=Hi-Res；FLAC≤48k=无损；MP3=普通。</summary>
        private static string ClassifyQualityName(string audioInfo, string fallback)
        {
            if (audioInfo.StartsWith("FLAC", StringComparison.Ordinal))
            {
                var idx = audioInfo.IndexOf("Hz", StringComparison.Ordinal);
                if (idx > 5 && int.TryParse(audioInfo.AsSpan(5, idx - 5).Trim(), out var rate))
                    return rate > 48000 ? "Hi-Res" : "无损";
                return "无损";
            }
            if (audioInfo.StartsWith("MP3", StringComparison.Ordinal))
                return "普通";
            return fallback;
        }

        /// <summary>缓存命中时从文件头解析音质（码率/声道），显示到来源角标。</summary>
        private void ApplyCachedInfo(QueueTrack track, string path)
        {
            try
            {
                using var fs = File.OpenRead(path);
                var head = new byte[65536];
                var n = fs.Read(head, 0, head.Length);
                var info = QqApiClient.ParseAudioInfo(head, path);
                if (!string.IsNullOrEmpty(info) && !track.Source.Contains(info))
                {
                    track.QualityName = info;
                    track.Source = "QQ音乐 · " + info;
                }
            }
            catch { }
        }
        /// <summary>按歌曲 Mid 查找缓存目录中已下载的文件（文件名格式 {mid}_{标题}）。</summary>
        private string? FindCachedFile(string mid)
        {
            if (string.IsNullOrEmpty(mid)) return null;
            try
            {
                var prefix = mid + "_";
                foreach (var f in Directory.GetFiles(CacheDir))
                {
                    var name = Path.GetFileName(f);
                    if (name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    {
                        var ext = Path.GetExtension(f);
                        // 旧版本曾把带 ID3 标签的 MP3 误判成 OGG 缓存（内容实为 MP3）：
                        // 检测内容后改名为 .mp3 修正，避免系统播放器按 OGG 解码失败。
                        if (ext.Equals(".ogg", StringComparison.OrdinalIgnoreCase) && IsMp3Content(f))
                        {
                            var mp3 = Path.ChangeExtension(f, ".mp3");
                            try
                            {
                                File.Move(f, mp3);
                                return mp3;
                            }
                            catch
                            {
                                continue; // 改名失败视为无效缓存，走重新下载
                            }
                        }
                        // NAA 输出忽略 OGG 缓存（旧版本下载的 OGG640 环绕档，HQPlayer 播放不了），
                        // 命中时走新音质链重新下载（F000 FLAC > MP3 320）
                        if (UseHqOutput && ext.Equals(".ogg", StringComparison.OrdinalIgnoreCase))
                        {
                            continue;
                        }
                        return f;
                    }
                }
            }
            catch { }
            return null;
        }

        /// <summary>按 Buffer 设置清理缓存目录中过旧文件（保留当前播放文件）。</summary>
        private void EnforceCacheLimit()
        {
            try
            {
                Directory.CreateDirectory(CacheDir);
                var limitMb = settings.BufferOption switch
                {
                    0 => 256,
                    2 => 1024,
                    3 => settings.CustomBufferMb > 0 ? settings.CustomBufferMb : 512,
                    _ => 512
                };
                var limitBytes = (long)limitMb * 1024 * 1024;
                var currentPath = currentQueueIndex >= 0 && currentQueueIndex < queueTracks.Count
                    ? queueTracks[currentQueueIndex].Path
                    : null;
                var files = Directory.GetFiles(CacheDir)
                    .Where(f => !string.Equals(f, currentPath, StringComparison.OrdinalIgnoreCase))
                    .OrderBy(f => File.GetLastWriteTimeUtc(f))
                    .ToList();
                long total = files.Sum(f => new FileInfo(f).Length);
                foreach (var f in files)
                {
                    if (total <= limitBytes) break;
                    if (settings.CacheCountMode == 1)
                    {
                        // 按曲数：保留最近 N 首（不含当前播放），更早的删除
                        var keep = settings.CacheCount > 0 ? settings.CacheCount : 5;
                        if (files.Count <= keep) break;
                        var oldest = files[0];
                        files.RemoveAt(0);
                        try
                        {
                            File.Delete(oldest);
                            LogQqDebug($"缓存超数，删除: {Path.GetFileName(oldest)}");
                        }
                        catch { }
                        continue;
                    }
                    try
                    {
                        var len = new FileInfo(f).Length;
                        File.Delete(f);
                        total -= len;
                        LogQqDebug($"缓存超限，删除: {Path.GetFileName(f)}");
                    }
                    catch { }
                }
            }
            catch { }
        }

        /// <summary>分页拉取当前 QQ 列表全量（随机/整单播放用），页间并行加速。</summary>
        private async Task<List<QqSongItem>> LoadEntireQqListAsync()
        {
            // 无限推荐/每日推荐：已加载列表即全量（无限推荐边播边续载），无需重新分页
            if (qqTab == "radar" || qqTab == "daily30")
                return new List<QqSongItem>(qqSongs);
            var all = new List<QqSongItem>();
            try
            {
                const int num = 200;
                var first = await FetchQqPageAsync(1, num);
                var songs = QqApiClient.ParseSongs(first);
                all.AddRange(songs);
                if (songs.Count == 0)
                {
                    if (qqTab == "album") all.Reverse();
                    return all;
                }
                var total = GetQqTotal(first);
                if (total <= songs.Count || total <= 0)
                {
                    if (qqTab == "album") all.Reverse();
                    return all;
                }
                var pages = Math.Min((int)Math.Ceiling(total / (double)num), 16);
                SetStatus($"正在加载整个歌单…（1/{pages}）");
                var tasks = new List<Task<JsonElement>>();
                for (var page = 2; page <= pages; page++)
                    tasks.Add(FetchQqPageAsync(page, num));
                var results = await Task.WhenAll(tasks);
                for (var i = 0; i < results.Length; i++)
                {
                    all.AddRange(QqApiClient.ParseSongs(results[i]));
                    SetStatus($"正在加载整个歌单…（{i + 2}/{pages}）");
                }
            }
            catch (Exception ex)
            {
                SetStatus("加载整个歌单失败：" + ex.Message);
            }
            // 专辑接口返回倒序，整体反转恢复正序（与专辑列表展示一致）
            if (qqTab == "album")
                all.Reverse();
            return all;
        }

        private async Task<JsonElement> FetchQqPageAsync(int page, int num)
        {
            switch (qqTab)
            {
                case "fav":
                    return await qqApi.GetFavSongsAsync(page, num);
                case "playlists" when currentPlaylistId > 0:
                    return await qqApi.GetSonglistDetailAsync(currentPlaylistId, page, num);
                case "album" when currentAlbumMid.Length > 0:
                    return await qqApi.GetAlbumSongsAsync(currentAlbumMid, page, num);
                default:
                    return await qqApi.SearchAsync(QqSearchBox.Text.Trim(), page, num);
            }
        }

        private void Shuffle<T>(IList<T> list)
        {
            for (var i = list.Count - 1; i > 0; i--)
            {
                var j = shuffleRng.Next(i + 1);
                (list[i], list[j]) = (list[j], list[i]);
            }
        }

        private static int GetQqTotal(JsonElement data)
        {
            foreach (var key in new[] { "total", "total_num", "total_song_num", "estimate_sum" })
            {
                if (data.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.Number)
                    return v.GetInt32();
            }
            return 0;
        }

        private void UpdateQqListCount(JsonElement data)
        {
            qqListTotal = GetQqTotal(data);
            QqListCountText.Text = qqListTotal > 0 ? $"共 {qqListTotal} 首" : "";
        }

        private void QqSort_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (!loaded) return;
            if (QqSortCombo.SelectedItem is not ComboBoxItem item || item.Tag is not string key)
                return;
            if (key == "default")
            {
                // 默认 = 接口顺序（收藏即按添加时间倒序）
                return;
            }
            var list = qqSongs.ToList();
            switch (key)
            {
                case "title":
                    list.Sort((a, b) => string.Compare(a.Title, b.Title, StringComparison.CurrentCultureIgnoreCase));
                    break;
                case "singer":
                    list.Sort((a, b) => string.Compare(a.Singer, b.Singer, StringComparison.CurrentCultureIgnoreCase));
                    break;
                case "duration":
                    list.Sort((a, b) => ParseDuration(a.Duration).CompareTo(ParseDuration(b.Duration)));
                    break;
                case "album":
                    list.Sort((a, b) => string.Compare(a.Album, b.Album, StringComparison.CurrentCultureIgnoreCase));
                    break;
                default:
                    return;
            }
            qqSongs.Clear();
            foreach (var s in list)
                qqSongs.Add(s);
        }

        private static double ParseDuration(string d)
        {
            if (TimeSpan.TryParse("00:" + d, out var ts))
                return ts.TotalSeconds;
            return 0;
        }
        private void QueueSelectMode_Changed(object sender, RoutedEventArgs e)
        {
            queueSelectMode = QueueSelectModeBtn.IsChecked == true;
            QueueList.SelectionMode = queueSelectMode ? SelectionMode.Extended : SelectionMode.Single;
        }

        /// <summary>播放列表行点击：非选择模式播放该曲，选择模式切换选中。</summary>
        private void QueueTrack_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button btn || btn.Tag is not QueueTrack track) return;
            if (queueSelectMode)
            {
                var item = FindAncestor<ListBoxItem>(btn);
                if (item != null) item.IsSelected = !item.IsSelected;
                return;
            }
            var idx = queueTracks.IndexOf(track);
            if (idx >= 0 && idx != currentQueueIndex)
            {
                currentQueueIndex = idx;
                _ = PlayCurrentQueueAsync();
            }
        }

        /// <summary>播放列表行内下载进度条（复用 QqRowButtonStyle 内置 DlTrack/DlFill）。</summary>
        private void ShowQueueRowProgress(QueueTrack track, double percent)
        {
            var (t, scale) = GetQueueRowProgressElements(track);
            if (t == null || scale == null)
            {
                // 虚拟化/布局中容器可能未生成，下载刚开始时延迟重试一次
                if (percent <= 0) _ = RetryShowQueueProgressAsync(track, percent);
                return;
            }
            EnsureTrackClip(t);
            t.Visibility = Visibility.Visible;
            scale.BeginAnimation(ScaleTransform.ScaleXProperty,
                new DoubleAnimation(percent, TimeSpan.FromMilliseconds(120)));
        }

        private async Task RetryShowQueueProgressAsync(QueueTrack track, double percent)
        {
            await Task.Delay(250);
            var (t, scale) = GetQueueRowProgressElements(track);
            if (t == null || scale == null) return;
            EnsureTrackClip(t);
            t.Visibility = Visibility.Visible;
            scale.BeginAnimation(ScaleTransform.ScaleXProperty,
                new DoubleAnimation(percent, TimeSpan.FromMilliseconds(120)));
        }

        private void HideQueueRowProgress(QueueTrack track)
        {
            var (t, _) = GetQueueRowProgressElements(track);
            if (t != null) t.Visibility = Visibility.Collapsed;
        }

        private async Task HideQueueRowProgressDelayedAsync(QueueTrack track, int delayMs)
        {
            await Task.Delay(delayMs);
            HideQueueRowProgress(track);
        }

        private (Grid? Track, ScaleTransform? Scale) GetQueueRowProgressElements(QueueTrack track)
        {
            var container = QueueList.ItemContainerGenerator.ContainerFromItem(track);
            if (container is not ListBoxItem item)
                return (null, null);
            var t = FindVisualChildByName(item, "DlTrack") as Grid;
            var fill = FindVisualChildByName(item, "DlFill") as Border;
            return (t, fill?.RenderTransform as ScaleTransform);
        }

        private void LangRadio_Changed(object sender, RoutedEventArgs e)
        {
            if (!loaded) return;
            if (LangZh.IsChecked == true) settings.Language = 1;
            else if (LangEn.IsChecked == true) settings.Language = 2;
            else settings.Language = 0;
            SaveSettings();
            if (Application.Current is App app)
                Lang.Apply(Lang.Resolve(settings), app); // replace language dictionary, DynamicResource texts refresh immediately
            UpdateRemoteStatusText();
        }

        private void UpdateIntervalCombo_Changed(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            if (!loaded) return;
            settings.UpdateCheckInterval = Math.Clamp(UpdateIntervalCombo.SelectedIndex, 0, 4);
            SaveSettings();
        }

        private void UpdateCheckNow_Click(object sender, RoutedEventArgs e)
        {
            _ = CheckForUpdatesAsync(manual: true);
        }

        /// <summary>On startup decide whether an automatic check is due (only evaluated at startup).</summary>
        private void CheckForUpdatesOnStart()
        {
            var interval = Math.Clamp(settings.UpdateCheckInterval, 0, 4);
            if (interval == 4) return; // never
            var span = interval switch
            {
                0 => TimeSpan.FromDays(7),
                1 => TimeSpan.FromDays(30),
                2 => TimeSpan.FromDays(92),
                _ => TimeSpan.FromDays(365),
            };
            if (DateTime.TryParse(settings.LastUpdateCheck, CultureInfo.InvariantCulture,
                    DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var last)
                && DateTime.UtcNow - last < span)
            {
                return;
            }
            _ = CheckForUpdatesAsync(manual: false);
        }

        /// <summary>Check GitHub latest release; errors are silent unless the user clicked manually.</summary>
        private async System.Threading.Tasks.Task CheckForUpdatesAsync(bool manual)
        {
            try
            {
                using var client = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(10) };
                client.DefaultRequestHeaders.UserAgent.ParseAdd("AuralDesk/" + CurrentVersion);
                var json = await client.GetStringAsync("https://api.github.com/repos/HowenXu/AuralDesk/releases/latest");
                using var doc = System.Text.Json.JsonDocument.Parse(json);
                var tag = doc.RootElement.TryGetProperty("tag_name", out var tEl) ? tEl.GetString() : null;
                if (string.IsNullOrWhiteSpace(tag) || !Version.TryParse(tag.TrimStart('v'), out var remote)) return;
                // 仅请求成功才记录检查时间：连不上(网络失败)时不记录，下次启动会再检查
                settings.LastUpdateCheck = DateTime.UtcNow.ToString("o");
                SaveSettings();
                if (remote > CurrentVersion)
                {
                    var ask = MessageBox.Show(
                        Lang.T("updateNewTitle") + " v" + remote + "\n\n" + Lang.T("updateOpenRelease") + "?",
                        "AuralDesk", MessageBoxButton.YesNo, MessageBoxImage.Information);
                    if (ask == MessageBoxResult.Yes)
                        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(
                            "https://github.com/HowenXu/AuralDesk/releases/latest") { UseShellExecute = true });
                }
                else if (manual)
                {
                    MessageBox.Show(Lang.T("updateUpToDate"), "AuralDesk",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            catch
            {
                if (manual)
                    MessageBox.Show(Lang.T("updateCheckFailed"), "AuralDesk",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private static Version CurrentVersion =>
            System.Reflection.Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 1, 0);

        private void PreCache_Changed(object sender, TextChangedEventArgs e)
        {
            if (!loaded) return;
            if (int.TryParse(PreCacheBox.Text.Trim(), out var n))
            {
                settings.PrecacheCount = Math.Clamp(n, 1, 8);
                PreCacheBox.Text = settings.PrecacheCount.ToString();
                SaveSettings();
            }
        }

        private void CachePath_Changed(object sender, TextChangedEventArgs e)
        {
            if (loaded) SaveSettings();
        }


        private void LyricOffset_Changed(object sender, TextChangedEventArgs e)
        {
            if (loaded) SaveSettings();
        }
        private void CachePathBrowse_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var dlg = new Microsoft.Win32.OpenFolderDialog
                {
                    Title = "选择缓存目录",
                    InitialDirectory = string.IsNullOrWhiteSpace(CachePathBox.Text)
                        ? Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
                        : CachePathBox.Text
                };
                if (dlg.ShowDialog() == true)
                {
                    CachePathBox.Text = dlg.FolderName;
                    SaveSettings();
                    SetStatus("缓存目录已更新：" + dlg.FolderName);
                }
            }
            catch (Exception ex)
            {
                LogQqDebug("选择缓存目录失败: " + ex.Message);
            }
        }
        private static T? FindAncestor<T>(DependencyObject? d) where T : DependencyObject
        {
            while (d != null)
            {
                if (d is T match) return match;
                d = System.Windows.Media.VisualTreeHelper.GetParent(d);
            }
            return null;
        }

        // ------------------------------------------------------------------
        // QQ 歌曲行内下载进度条
        // ------------------------------------------------------------------

        private void ShowQqRowProgress(QqSongItem song, double percent)
        {
            if (qqDownloadingSong != null && !ReferenceEquals(qqDownloadingSong, song))
                HideQqRowProgress();
            qqDownloadingSong = song;
            var (track, scale) = GetQqRowProgressElements(song);
            if (track == null || scale == null)
                return;
            EnsureTrackClip(track);
            track.Visibility = Visibility.Visible;
            scale.BeginAnimation(ScaleTransform.ScaleXProperty,
                new DoubleAnimation(percent, TimeSpan.FromMilliseconds(120)));
        }

        /// <summary>把进度条容器裁剪成与卡片一致的圆角形状（贴外缘后按外圆角 10 裁剪）。</summary>
        private static void EnsureTrackClip(Grid track)
        {
            const double radius = 10;
            if (track.Clip is RectangleGeometry existing && Math.Abs(existing.RadiusX - radius) < 0.01)
                return;
            track.Clip = new RectangleGeometry(
                new Rect(0, 0, track.ActualWidth, track.ActualHeight), radius, radius);
            track.SizeChanged += (_, _) =>
            {
                track.Clip = new RectangleGeometry(
                    new Rect(0, 0, track.ActualWidth, track.ActualHeight), radius, radius);
            };
        }

        private void UpdateQqRowProgress(QqSongItem song, double percent)
        {
            if (!ReferenceEquals(qqDownloadingSong, song))
                return;
            var (_, scale) = GetQqRowProgressElements(song);
            if (scale == null)
                return;
            scale.BeginAnimation(ScaleTransform.ScaleXProperty,
                new DoubleAnimation(percent, TimeSpan.FromMilliseconds(150))
                {
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                });
        }

        private void HideQqRowProgress()
        {
            if (qqDownloadingSong == null)
                return;
            var song = qqDownloadingSong;
            qqDownloadingSong = null;
            var (track, _) = GetQqRowProgressElements(song);
            if (track == null)
                return;
            // 加载完成直接消失，不做淡出
            track.Visibility = Visibility.Collapsed;
        }

        /// <summary>延迟隐藏进度条；期间若已切到别的歌则不隐藏。</summary>
        private async Task DelayHideQqRowProgressAsync(int delayMs, QqSongItem song)
        {
            await Task.Delay(delayMs);
            if (ReferenceEquals(qqDownloadingSong, song))
                HideQqRowProgress();
        }

        private (Grid? Track, ScaleTransform? Scale) GetQqRowProgressElements(QqSongItem song)
        {
            // 同一首歌可能在多个列表出现（主歌曲列表 / 搜索单曲 / 歌手歌曲），
            // 下载进度条在所有出现的位置都要显示。
            foreach (var list in new[] { QqSongList, QqSingerSongList, QqSearchSongList })
            {
                var container = list.ItemContainerGenerator.ContainerFromItem(song);
                if (container is not ContentPresenter presenter)
                    continue;
                var track = FindVisualChildByName(presenter, "DlTrack") as Grid;
                var fill = FindVisualChildByName(presenter, "DlFill") as Border;
                if (track != null || fill != null)
                    return (track, fill?.RenderTransform as ScaleTransform);
            }
            return (null, null);
        }

        private static FrameworkElement? FindVisualChildByName(DependencyObject parent, string name)
        {
            for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is FrameworkElement match && match.Name == name)
                    return match;
                var found = FindVisualChildByName(child, name);
                if (found != null)
                    return found;
            }
            return null;
        }

        // ------------------------------------------------------------------
        // QQ 歌词与封面
        // ------------------------------------------------------------------

        private void ClearQqCover()
        {
            try
            {
                CoverImage.Source = null;
                CoverImage.Visibility = Visibility.Collapsed;
                OverlayCoverImage.Source = null;
                OverlayCoverImage.Visibility = Visibility.Collapsed;
            }
            catch
            {
                // 忽略
            }
        }

        private readonly Dictionary<string, BitmapImage> coverCache = new();

        private async Task<BitmapImage?> GetCoverAsync(string albumMid)
        {
            if (coverCache.TryGetValue(albumMid, out var cached))
                return cached;
            var cover = await qqApi.DownloadCoverAsync(albumMid);
            if (cover == null || cover.Length == 0)
            {
                // 网络波动时延迟重试一次
                await Task.Delay(2000);
                cover = await qqApi.DownloadCoverAsync(albumMid);
            }
            if (cover == null || cover.Length == 0)
                return null;
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.StreamSource = new MemoryStream(cover);
            bmp.EndInit();
            bmp.Freeze();
            coverCache[albumMid] = bmp;
            return bmp;
        }

        private async Task LoadQqSongMetaAsync(QqSongItem song)
        {
            try
            {
                var albumMid = song.AlbumMid;
                if (string.IsNullOrEmpty(albumMid))
                {
                    // 列表缺专辑信息时补查一次（仍有少数歌曲无专辑数据，保持占位）
                    albumMid = await qqApi.GetAlbumMidAsync(song.Mid) ?? "";
                }
                if (!string.IsNullOrEmpty(albumMid))
                {
                    var bmp = await GetCoverAsync(albumMid);
                    if (bmp != null)
                    {
                        _ = Dispatcher.BeginInvoke(DispatcherPriority.Normal, new Action(() =>
                        {
                            try
                            {
                                CoverImage.Source = bmp;
                                CoverImage.Visibility = Visibility.Visible;
                                CoverImage.Clip = new RectangleGeometry(new Rect(0, 0, 44, 44), 8, 8);
                                OverlayCoverImage.Source = bmp;
                                OverlayCoverImage.Visibility = Visibility.Visible;
                                OverlayCoverImage.Clip = new RectangleGeometry(new Rect(0, 0, 300, 300), 20, 20);
                            }
                            catch { }
                        }));
                    }
                }

                var lrc = await qqApi.GetLyricAsync(song.Mid);
                var merged = lrc != null ? MergeLyrics(lrc.Value.Lyric, lrc.Value.Trans) : null;
                _ = Dispatcher.BeginInvoke(DispatcherPriority.Normal, new Action(() =>
                {
                    try
                    {
                        if (merged != null && merged.Count > 0)
                        {
                            BuildLyricEntries(merged);
                            ResetPlayback();
                        }
                        else
                        {
                            ShowNoLyrics();
                        }
                    }
                    catch { }
                }));
            }
            catch (Exception ex)
            {
                LogQqDebug("歌词/封面加载失败: " + ex.Message);
                _ = Dispatcher.BeginInvoke(DispatcherPriority.Normal, new Action(() =>
                {
                    try { ShowNoLyrics(); } catch { }
                }));
            }
        }

        // ------------------------------------------------------------------
        // 歌词：初始化 / 高亮 / 淡入淡出
        // ------------------------------------------------------------------

        private void InitLyrics()
        {
            if (lyricFadeReady) return;
            lyricFadeReady = true;
            ShowNoLyrics();
            LyricScroller.SizeChanged += (s, e) => UpdateLyricSpacers();
            playTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
            playTimer.Tick += PlayTick;
            playTimer.Start();
        }


        private void SetActiveLyric(int index, bool animate)
        {
            if (index < 0 || index >= lyricLines.Count) return;
            var accent = (SolidColorBrush)FindResource("AccentBrush");
            var primary = (SolidColorBrush)FindResource("TextPrimaryBrush");
            if (activeLyricIndex >= 0 && activeLyricIndex != index &&
                activeLyricIndex < lyricLines.Count)
            {
                var prevMain = lyricLines[activeLyricIndex];
                prevMain.FontWeight = FontWeights.Normal;
                FadeLyric(prevMain, lyricOriginalColors[activeLyricIndex], 0.72, animate);
                AnimateFontSize(prevMain, MainFont, animate);
                if (activeLyricIndex < lyricTrans.Count)
                {
                    var prevTrans = lyricTrans[activeLyricIndex];
                    prevTrans.FontWeight = FontWeights.Normal;
                    FadeLyric(prevTrans, lyricTransOriginalColors[activeLyricIndex], 0.55, animate);
                    AnimateFontSize(prevTrans, TransFont, animate);
                }
            }
            activeLyricIndex = index;
            var curMain = lyricLines[index];
            curMain.FontWeight = FontWeights.SemiBold;
            FadeLyric(curMain, accent.Color, 1.0, animate);
            AnimateFontSize(curMain, MainFontActive, animate);
            if (index < lyricTrans.Count)
            {
                var curTrans = lyricTrans[index];
                curTrans.FontWeight = FontWeights.SemiBold;
                FadeLyric(curTrans, primary.Color, 0.95, animate);
                AnimateFontSize(curTrans, TransFontActive, animate);
            }
            ScrollLyricToCenter(curMain);
        }

        private void ScrollLyricToCenter(TextBlock main)
        {
            if (LyricScroller == null || main.Parent is not FrameworkElement entry) return;
            if (LyricScroller.ViewportHeight <= 0) return;
            var p = entry.TranslatePoint(new Point(0, 0), LyricScroller);
            // 当前句垂直中心放在视口 38% 处（中间偏上，更符合视觉）
            double target = p.Y + LyricScroller.VerticalOffset + entry.ActualHeight / 2 -
                            LyricScroller.ViewportHeight * 0.38;
            AnimateScrollTo(Math.Max(0, target));
        }

        private void AnimateScrollTo(double target)
        {
            if (LyricScroller == null) return;
            double start = LyricScroller.VerticalOffset;
            scrollStart = start;
            scrollDelta = target - start;
            if (Math.Abs(scrollDelta) < 0.5)
            {
                scrollAnimating = false;
                CompositionTarget.Rendering -= ScrollRenderTick;
                return;
            }
            scrollStartTime = DateTime.UtcNow;
            if (!scrollAnimating)
            {
                scrollAnimating = true;
                CompositionTarget.Rendering += ScrollRenderTick;
            }
        }

        private void ScrollRenderTick(object? sender, EventArgs e)
        {
            double t = (DateTime.UtcNow - scrollStartTime).TotalMilliseconds / 450.0;
            if (t >= 1)
            {
                t = 1;
                scrollAnimating = false;
                CompositionTarget.Rendering -= ScrollRenderTick;
            }
            // easeInOutCubic：起步与收尾都平缓
            double eased = t < 0.5 ? 4 * t * t * t : 1 - Math.Pow(-2 * t + 2, 3) / 2;
            LyricScroller.ScrollToVerticalOffset(scrollStart + scrollDelta * eased);
        }


        /// <summary>解析 LRC 文本为（时间, 歌词）列表，无有效行返回 null。</summary>
        private static List<(TimeSpan time, string text)>? ParseLrcText(string? text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return null;
            var parsed = new List<(TimeSpan time, string text)>();
            foreach (var raw in text.Split('\n'))
            {
                var line = raw.Trim().Trim('\r');
                var m = Regex.Match(line, @"^\[(\d{1,2}):(\d{2})(?:[.:](\d{1,3}))?\](.*)$");
                if (!m.Success) continue;
                var min = int.Parse(m.Groups[1].Value);
                var sec = int.Parse(m.Groups[2].Value);
                var frac = m.Groups[3].Success ? int.Parse(m.Groups[3].Value.PadRight(3, '0')) / 1000.0 : 0.0;
                var lyricText = m.Groups[4].Value.Trim();
                if (lyricText.Length == 0) continue;
                parsed.Add((TimeSpan.FromMinutes(min) + TimeSpan.FromSeconds(sec + frac), lyricText));
            }
            return parsed.Count > 0 ? parsed : null;
        }

        /// <summary>合并原文与翻译歌词：每条目 =（时间, 原文, 翻译）。</summary>
        private static List<(TimeSpan time, string main, string trans)>? MergeLyrics(string lyric, string trans)
        {
            var main = ParseLrcText(lyric);
            if (main == null) return null;
            var transParsed = ParseLrcText(trans);
            var transMap = new Dictionary<TimeSpan, string>();
            if (transParsed != null)
            {
                foreach (var (t, s) in transParsed)
                    if (!transMap.ContainsKey(t))
                        transMap[t] = s;
            }
            var entries = new List<(TimeSpan time, string main, string trans)>();
            foreach (var (t, s) in main)
            {
                entries.Add((t, s, transMap.TryGetValue(t, out var tr) ? tr : ""));
            }
            return entries;
        }

        private void BuildLyricEntries(List<(TimeSpan time, string main, string trans)> entries)
        {
            LyricList.Children.Clear();
            // 防御性清空并行列表（正常流程先 ShowNoLyrics，这里兜底避免换歌时残留旧索引）
            lyricLines.Clear();
            lyricTrans.Clear();
            lyricTimes.Clear();
            lyricOriginalColors.Clear();
            lyricTransOriginalColors.Clear();
            activeLyricIndex = -1;
            lyricEntries.Clear();
            lyricEntries.AddRange(entries);
            var secondary = (SolidColorBrush)FindResource("TextSecondaryBrush");
            var dim = (SolidColorBrush)FindResource("TextDimBrush");
            // 上下留白：让第一句也能滚到正常播放位置，最后一句同理
            topSpacer = new Border { Height = 0, IsHitTestVisible = false };
            bottomSpacer = new Border { Height = 0, IsHitTestVisible = false };
            LyricList.Children.Add(topSpacer);
            for (int i = 0; i < entries.Count; i++)
            {
                var mainText = entries[i].main;
                var transText = entries[i].trans;
                var entry = new StackPanel { Margin = new Thickness(0, 12, 0, 0) };
                var mainTb = new TextBlock
                {
                    Text = mainText,
                    FontSize = 15,
                    Foreground = new SolidColorBrush(secondary.Color),
                    HorizontalAlignment = HorizontalAlignment.Left,
                    Opacity = 0.72,
                    Cursor = Cursors.Hand
                };
                var transTb = new TextBlock
                {
                    Text = transText,
                    FontSize = 12.5,
                    Foreground = new SolidColorBrush(dim.Color),
                    HorizontalAlignment = HorizontalAlignment.Left,
                    Margin = new Thickness(0, 2, 0, 0),
                    Opacity = 0.55,
                    Cursor = Cursors.Hand
                };
                if (string.IsNullOrEmpty(transText))
                    transTb.Visibility = Visibility.Collapsed;
                entry.Children.Add(mainTb);
                entry.Children.Add(transTb);
                lyricLines.Add(mainTb);
                lyricTrans.Add(transTb);
                lyricTimes.Add(entries[i].time);
                lyricOriginalColors.Add(secondary.Color);
                lyricTransOriginalColors.Add(dim.Color);
                LyricList.Children.Add(entry);
            }
            LyricList.Children.Add(bottomSpacer);
        }


        private void UpdateLyricSpacers()
        {
            if (LyricScroller == null || topSpacer == null || bottomSpacer == null) return;
            double vp = LyricScroller.ViewportHeight;
            if (vp <= 0) return;
            topSpacer.Height = vp * 0.38;
            bottomSpacer.Height = vp * 0.62;
        }
        private void PlayTick(object? sender, EventArgs e)
        {
            if (UseHqOutput)
            {
                _ = HqPollAsync();
                return;
            }
            // 真实音频文件播放：进度完全由播放器驱动，不走演示时钟
            if (systemPlayer.HasFile)
            {
                UpdateProgressUi();
                SyncActiveLyric(systemPlayer.Position);
                // 兜底：个别格式（如部分 m4a/ogg）播完不触发 PlaybackStopped 事件，
                // 用位置到达文件末尾来补一次自然切歌（lastPlayStart 时间窗防误跳）。
                if (systemPlayer.IsPlaying && systemPlayer.Length.TotalSeconds > 1 &&
                    systemPlayer.Position >= systemPlayer.Length - TimeSpan.FromMilliseconds(500))
                {
                    if ((DateTime.UtcNow - lastPlayStart).TotalMilliseconds >= 500)
                    {
                        isPlaying = false;
                        SyncPlayIcon();
                        _ = PlayNextQueueAsync();
                        return;
                    }
                }
                // 定期保存进度（每 5 秒一次，避免频繁写盘）
                if (settings.ResumeMode >= 2 && systemPlayer.IsPlaying)
                {
                    var now = DateTime.UtcNow;
                    if ((now - lastResumeSave).TotalSeconds >= 5)
                    {
                        lastResumeSave = now;
                        settings.LastSongPosition = systemPlayer.Position.TotalSeconds;
                        SaveSettings();
                    }
                }
                return;
            }
            if (!isPlaying) return;
            playbackPos += TimeSpan.FromMilliseconds(100);
            if (playbackPos.TotalSeconds >= totalDurationSeconds)
            {
                UpdateProgressUi();
                StopPlayback();
                return;
            }
            UpdateProgressUi();
            int next = activeLyricIndex + 1;
            if (next < lyricTimes.Count && playbackPos >= lyricTimes[next])
            {
                SetActiveLyric(next, true);
            }
        }

        /// <summary>HQPlayer 输出：轮询控制协议，驱动进度/歌词并检测自然播完。</summary>
        private async Task HqPollAsync()
        {
            if (hqPolling) return;
            var throttleMs = (isPlaying || hqIsPlaying || hqPrevState == 2) ? 250 : 2000;
            if (hqPrevState != -1 && (DateTime.UtcNow - lastHqPoll).TotalMilliseconds < throttleMs) return;
            hqPolling = true;
            try
            {
                lastHqPoll = DateTime.UtcNow;
                var status = await Task.Run(() => hqPlayer.GetStatus());
                if (status == null)
                {
                    hqFailCount++;
                    if (hqFailCount >= 2)
                    {
                        SetHqConnected(false);
                        hqPrevState = -1;
                        if (hqIsPlaying)
                        {
                            hqIsPlaying = false;
                            SyncPlayIcon();
                            SetStatus("HQPlayer 连接已断开");
                        }
                    }
                    return;
                }
                hqFailCount = 0;
                SetHqConnected(true, status.ActiveRate, status.ActiveMode);
                bool wasPlaying = hqPrevState == 2;
                var oldHqPlaying = hqIsPlaying;
                hqPrevState = status.State;
                hqIsPlaying = status.State == 2;
                // 用户可能在 HQPlayer 里手动暂停/停止，这里把真实状态同步到播放键
                if (oldHqPlaying != hqIsPlaying)
                    SyncPlayIcon();
                // 播放请求后长时间未进入播放状态（HQPlayer 加载卡住）→ 切下一首
                if (hqPlayRequestTime != DateTime.MinValue &&
                    status.State != 2 &&
                    (DateTime.UtcNow - hqPlayRequestTime).TotalSeconds > 15)
                {
                    LogQqDebug("HQPlayer 长时间未开始播放，强制切下一首");
                    hqPlayRequestTime = DateTime.MinValue;
                    lastHqPosition = -1;
                    lastHqMoveTime = DateTime.MinValue;
                    try { hqPlayer.Stop(); } catch { }
                    await PlayNextQueueAsync();
                    return;
                }
                if (status.State == 2 && hqPlayRequestTime != DateTime.MinValue)
                    hqPlayRequestTime = DateTime.MinValue;
                // 播放中位置长时间不前进（解码卡死）→ 强制切下一首
                if (status.State == 2)
                {
                    if (lastHqPosition >= 0 && Math.Abs(status.Position - lastHqPosition) < 0.5)
                    {
                        if (lastHqMoveTime == DateTime.MinValue)
                            lastHqMoveTime = DateTime.UtcNow;
                        else if ((DateTime.UtcNow - lastHqMoveTime).TotalSeconds > 12)
                        {
                            LogQqDebug("HQPlayer 播放卡住（位置长时间未前进），强制切下一首");
                            lastHqPosition = -1;
                            lastHqMoveTime = DateTime.MinValue;
                            try { hqPlayer.Stop(); } catch { }
                            await PlayNextQueueAsync();
                            return;
                        }
                    }
                    else
                    {
                        lastHqPosition = status.Position;
                        lastHqMoveTime = DateTime.MinValue;
                    }
                }
                else
                {
                    lastHqPosition = -1;
                    lastHqMoveTime = DateTime.MinValue;
                }
                if (status.Length > 0) totalDurationSeconds = status.Length;
                playbackPos = TimeSpan.FromSeconds(status.Position);
                UpdateProgressUi();
                SyncActiveLyric(playbackPos);
                if (settings.ResumeMode >= 2 && status.State == 2)
                {
                    var now = DateTime.UtcNow;
                    if ((now - lastResumeSave).TotalSeconds >= 5)
                    {
                        lastResumeSave = now;
                        settings.LastSongPosition = status.Position;
                        SaveSettings();
                    }
                }
                // 自然播完：播放中位置到达文件末尾，或刚停止且最后位置接近末尾。
                // 用户手动停止（位置在中间）不自动切下一首。
                var finishedNaturally =
                    wasPlaying &&
                    ((status.State == 2 && status.Length > 0 && status.Position >= status.Length - 0.8) ||
                     (status.State == 0 && status.Length > 0 && lastHqPosition >= status.Length - 2.0));
                if (finishedNaturally)
                {
                    hqPrevState = -1;
                    hqIsPlaying = false;
                    SyncPlayIcon();
                    await PlayNextQueueAsync();
                }
                else if (wasPlaying && status.State == 0)
                {
                    // 用户手动停止：保持停止，不自动接下一首
                    hqIsPlaying = false;
                    SyncPlayIcon();
                    SetStatus("HQPlayer 已停止");
                }
            }
            finally
            {
                hqPolling = false;
            }
        }

        private void ResetPlayback()
        {
            playbackPos = TimeSpan.Zero;
            activeLyricIndex = -1;
            SetActiveLyric(0, false);
            UpdateProgressUi();
        }

        private void StopPlayback()
        {
            lastPlayStart = DateTime.UtcNow;
            isPlaying = false;
            PlayBtn.Content = "\uE768";
            OverlayPlayBtn.Content = "\uE768";
            ResetPlayback();
        }

        private void UpdateProgressUi()
        {
            double pos, total;
            if (!UseHqOutput && systemPlayer.HasFile && systemPlayer.Length.TotalSeconds > 0)
            {
                pos = systemPlayer.Position.TotalSeconds;
                total = systemPlayer.Length.TotalSeconds;
            }
            else
            {
                pos = playbackPos.TotalSeconds;
                total = totalDurationSeconds;
            }
            BottomPosText.Text = FormatTime(pos);
            BottomDurText.Text = FormatTime(total);
            OverlayPosText.Text = FormatTime(pos);
            OverlayDurText.Text = FormatTime(total);
            double frac = total > 0 ? Math.Min(1.0, pos / total) : 0;
            if (BottomProgressTrack.ActualWidth > 0)
            {
                BottomProgressFill.Width = BottomProgressTrack.ActualWidth * frac;
            }
            if (OverlayProgressTrack.ActualWidth > 0)
            {
                OverlayProgressFill.Width = OverlayProgressTrack.ActualWidth * frac;
            }
        }

        /// <summary>按播放位置推进/回退歌词高亮。</summary>
        private void SyncActiveLyric(TimeSpan pos)
        {
            if (lyricTimes.Count == 0)
                return;
            var seconds = pos.TotalSeconds - settings.LyricOffsetMs / 1000.0; // 歌词偏移（正=延后）
            var next = activeLyricIndex + 1;
            if (next < lyricTimes.Count && seconds >= lyricTimes[next].TotalSeconds)
            {
                SetActiveLyric(next, true);
                return;
            }
            if (activeLyricIndex >= 0 && seconds < lyricTimes[activeLyricIndex].TotalSeconds)
            {
                var idx = 0;
                while (idx < lyricTimes.Count - 1 && lyricTimes[idx + 1].TotalSeconds <= seconds)
                    idx++;
                SetActiveLyric(idx, true);
            }
        }


        private static string FormatTime(double seconds)
        {
            var ts = TimeSpan.FromSeconds(Math.Max(0, seconds));
            return $"{(int)ts.TotalMinutes}:{ts.Seconds:D2}";
        }

        private void AnimateFontSize(TextBlock tb, double to, bool animate)
        {
            if (!animate || !tb.IsVisible)
            {
                tb.FontSize = to;
                return;
            }
            tb.BeginAnimation(
                TextBlock.FontSizeProperty,
                new DoubleAnimation(to, TimeSpan.FromMilliseconds(300))
                {
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut }
                });
        }
        private void FadeLyric(TextBlock tb, Color toColor, double toOpacity, bool animate)
        {
            if (!animate || !tb.IsVisible)
            {
                ((SolidColorBrush)tb.Foreground).Color = toColor;
                tb.Opacity = toOpacity;
                return;
            }
            var dur = TimeSpan.FromMilliseconds(300);
            ((SolidColorBrush)tb.Foreground).BeginAnimation(
                SolidColorBrush.ColorProperty,
                new ColorAnimation(toColor, dur) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut } });
            tb.BeginAnimation(
                UIElement.OpacityProperty,
                new DoubleAnimation(toOpacity, dur) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut } });
        }

        /// <summary>更新「返回当前播放歌曲」按钮可见性（当前项脱离可视区时出现）。</summary>
        private void UpdateJumpButtons()
        {
            UpdateQueueJumpButton();
            UpdateGlobalJumpButton();
        }

        private void UpdateQueueJumpButton()
        {
            if (QueueJumpCurrentBtn == null) return;
            // 仅队列视图下显示
            if (QueuePanel.Visibility != Visibility.Visible)
            {
                SetJumpButtonVisible(QueueJumpCurrentBtn, false);
                return;
            }
            var cur = currentQueueIndex >= 0 && currentQueueIndex < queueTracks.Count
                ? queueTracks[currentQueueIndex] : null;
            bool show = false;
            if (cur != null && QueueList.ActualHeight > 0)
            {
                var container = QueueList.ItemContainerGenerator.ContainerFromItem(cur);
                if (container is not FrameworkElement fe)
                {
                    // 虚拟化容器未生成：用户看不到即显示按钮
                    show = true;
                }
                else
                {
                    var pos = fe.TranslatePoint(new Point(0, 0), QueueList);
                    show = pos.Y < -fe.ActualHeight || pos.Y > QueueList.ActualHeight;
                }
            }
            SetJumpButtonVisible(QueueJumpCurrentBtn, show);
        }

        /// <summary>全局「返回当前播放歌曲」按钮：QQ 歌曲列表/队列之外的所有视图（首页、歌单列表、设置等）可用。</summary>
        private void UpdateGlobalJumpButton()
        {
            if (GlobalJumpBtn == null) return;
            var hasCurrent = currentQueueIndex >= 0 && currentQueueIndex < queueTracks.Count;
            // 队列视图用 QueueJumpCurrentBtn；歌曲列表/其他视图不显示返回按钮
            var inQqSongList = QqPanel.Visibility == Visibility.Visible
                && QqSongScroller.Visibility == Visibility.Visible;
            var inQueue = QueuePanel.Visibility == Visibility.Visible;
            SetJumpButtonVisible(GlobalJumpBtn, hasCurrent && !inQqSongList && !inQueue);
        }

        private QqSongItem? GetCurrentQqSongItem()
        {
            if (currentQueueIndex < 0 || currentQueueIndex >= queueTracks.Count) return null;
            var cur = queueTracks[currentQueueIndex];
            if (cur.QqSong != null) return cur.QqSong;
            return qqSongs.FirstOrDefault(s => s.Mid == cur.QqMid);
        }

        /// <summary>平滑显示/隐藏跳转按钮（显示时取消进行中的淡出，避免"滚动几次后消失"）。</summary>
        private void SetJumpButtonVisible(Button btn, bool show)
        {
            if (btn == null) return;
            if (show)
            {
                // 立即取消淡出动画，确保按钮一定回来
                btn.BeginAnimation(UIElement.OpacityProperty, null);
                if (btn.Visibility != Visibility.Visible)
                    btn.Visibility = Visibility.Visible;
                if (btn.Opacity < 0.99)
                {
                    btn.BeginAnimation(UIElement.OpacityProperty,
                        new DoubleAnimation(btn.Opacity, 1, TimeSpan.FromMilliseconds(200))
                        { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
                }
                else
                {
                    btn.Opacity = 1;
                }
            }
            else if (btn.Visibility == Visibility.Visible)
            {
                var anim = new DoubleAnimation(btn.Opacity, 0, TimeSpan.FromMilliseconds(200))
                    { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn } };
                anim.Completed += (_, _) =>
                {
                    if (btn.Visibility == Visibility.Visible && btn.Opacity <= 0.01)
                        btn.Visibility = Visibility.Collapsed;
                };
                btn.BeginAnimation(UIElement.OpacityProperty, anim);
            }
        }

        /// <summary>全局按钮：切到包含当前歌曲的视图并滚动到该歌曲。</summary>
        private void GlobalJumpCurrent_Click(object sender, RoutedEventArgs e)
        {
            var cur = GetCurrentQqSongItem();
            var inList = cur != null && qqSongs.Any(s => s.Mid == cur.Mid);
            if (inList && IsQqSource())
            {
                // 切到 QQ 音乐视图并显示歌曲列表
                ShowView("stream");
                QqHomeScroller.Visibility = Visibility.Collapsed;
                QqPlaylistScroller.Visibility = Visibility.Collapsed;
                QqSongScroller.Visibility = Visibility.Visible;
                QqHomeBtn.Visibility = Visibility.Visible;
                ScrollQqListToSong(cur!);
            }
            else if (currentQueueIndex >= 0 && currentQueueIndex < queueTracks.Count)
            {
                ShowView("queue");
                QueueList.ScrollIntoView(queueTracks[currentQueueIndex]);
            }
            _ = Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(UpdateJumpButtons));
        }

        /// <summary>滚动 QQ 歌曲列表到指定歌曲（容器未生成时按索引估算）。</summary>
        private void ScrollQqListToSong(QqSongItem cur)
        {
            var container = QqSongList.ItemContainerGenerator.ContainerFromItem(cur);
            if (container is FrameworkElement fe)
            {
                fe.BringIntoView();
            }
            else
            {
                var idx = qqSongs.IndexOf(cur);
                if (idx >= 0)
                    QqSongScroller.ScrollToVerticalOffset(Math.Max(0, idx * 62.0 - 100));
            }
        }

        private void QueueJumpCurrent_Click(object sender, RoutedEventArgs e)
        {
            if (currentQueueIndex >= 0 && currentQueueIndex < queueTracks.Count)
            {
                QueueList.ScrollIntoView(queueTracks[currentQueueIndex]);
                _ = Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(UpdateQueueJumpButton));
            }
        }

        private void QueueScroll_Changed(object sender, ScrollChangedEventArgs e)
        {
            UpdateQueueJumpButton();
        }
    }
}
