using System;
using NAudio.Wave;

namespace AuralDesk
{
    /// <summary>
    /// 系统音频输出（默认播放设备，NAudio WaveOutEvent + MediaFoundationReader，
    /// Win10 下支持 mp3/flac/m4a/ogg 等常见格式）。
    /// </summary>
    public sealed class SystemAudioPlayer : IDisposable
    {
        private WaveOutEvent? output;
        private MediaFoundationReader? reader;

        public bool HasFile { get; private set; }
        public bool IsPlaying { get; private set; }

        /// <summary>播放结束（自然播完）时触发。</summary>
        public event Action? PlaybackStopped;

        public void Play(string path)
        {
            Load(path);
            if (output != null)
            {
                output.Play();
                IsPlaying = true;
            }
        }

        /// <summary>加载文件但不自动播放（用于启动恢复）。</summary>
        public void Load(string path)
        {
            Stop();
            HasFile = true;
            reader = new MediaFoundationReader(path);
            output = new WaveOutEvent();
            output.Init(reader);
            output.PlaybackStopped += (s, e) =>
            {
                // 只响应当前设备的停止事件；旧设备（已被替换）的延迟回调忽略
                if (!ReferenceEquals(s, output)) return;
                IsPlaying = false;
                PlaybackStopped?.Invoke();
            };
            IsPlaying = false;
        }

        /// <summary>继续播放已加载的文件。</summary>
        public void Resume()
        {
            if (!HasFile || output == null) return;
            output.Play();
            IsPlaying = true;
        }

        /// <summary>暂停播放（不释放资源，可 Resume 继续）。</summary>
        public void Pause()
        {
            if (!HasFile || output == null || !IsPlaying) return;
            output.Pause();
            IsPlaying = false;
        }

        /// <summary>跳转到指定位置（用于恢复进度）。</summary>
        public void SeekTo(TimeSpan position)
        {
            if (reader == null) return;
            try
            {
                if (IsPlaying && output != null)
                {
                    // 播放中 seek：先 Stop 清空输出缓冲，再定位并重新播放，
                    // 否则 WaveOutEvent 缓冲里残留旧位置音频会导致声音与位置错位。
                    // （Stop 会触发 PlaybackStopped，调用方需用 lastPlayStart 时间窗忽略。）
                    output.Stop();
                    reader.CurrentTime = position;
                    output.Play();
                    IsPlaying = true;
                }
                else
                {
                    reader.CurrentTime = position;
                }
            }
            catch
            {
                // 越界等忽略
            }
        }

        public void Toggle()
        {
            if (!HasFile || output == null) return;
            if (IsPlaying)
            {
                output.Pause();
                IsPlaying = false;
            }
            else
            {
                output.Play();
                IsPlaying = true;
            }
        }

        public TimeSpan Position => reader?.CurrentTime ?? TimeSpan.Zero;
        public TimeSpan Length => reader?.TotalTime ?? TimeSpan.Zero;

        public void Stop()
        {
            try { output?.Stop(); } catch { }
            output?.Dispose();
            reader?.Dispose();
            output = null;
            reader = null;
            IsPlaying = false;
        }

        public void Dispose() => Stop();
    }
}
