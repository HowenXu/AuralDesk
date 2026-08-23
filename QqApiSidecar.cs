using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;

namespace AuralDesk
{
    /// <summary>
    /// QQ 音乐 API sidecar 进程管理。
    /// 负责拉起/复用本地 Python FastAPI 服务，并在应用退出时回收。
    /// </summary>
    public sealed class QqApiSidecar
    {
        private Process? _process;
        private readonly string? _root;

        /// <summary>本地服务地址。</summary>
        public string BaseUrl { get; } = "http://127.0.0.1:8123";

        /// <summary>组件缺失或启动失败时的错误信息。</summary>
        public string? Error { get; private set; }

        public QqApiSidecar()
        {
            var candidates = new[]
            {
                Path.Combine(AppContext.BaseDirectory, "qqapi")
            };
            foreach (var candidate in candidates)
            {
                if (File.Exists(Path.Combine(candidate, "python", "python.exe")) ||
                    File.Exists(Path.Combine(candidate, "venv", "Scripts", "python.exe")))
                {
                    _root = candidate;
                    break;
                }
            }
            if (_root == null)
            {
                Error = "未找到 QQ 音乐组件（qqapi），请重新安装或检查组件目录。";
                AppLog.Write("未找到 QQ 音乐组件（qqapi），程序目录: " + AppContext.BaseDirectory);
            }
        }

        public bool Available => _root != null;

        /// <summary>确保服务在运行，返回是否可用。</summary>
        public async Task<bool> EnsureStartedAsync()
        {
            if (_process != null && !_process.HasExited)
                return true;
            if (await PingAsync())
                return true; // 已有残留服务在跑，直接复用
            if (_root == null)
                return false;

            try
            {
                // 优先使用随包自带的便携 Python（embeddable + 依赖，可移植）；
                // 旧的 venv 方式仅作兼容回退
                var portablePython = Path.Combine(_root, "python", "python.exe");
                var python = File.Exists(portablePython)
                    ? portablePython
                    : Path.Combine(_root, "venv", "Scripts", "python.exe");
                var run = Path.Combine(_root, "app", "web", "run.py");
                // sidecar 的日志/凭证/设备信息写入用户可写目录，避免 Program Files 权限问题（无需 UAC 提权）
                var dataDir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "AuralDesk", "qqapi-data");
                try { Directory.CreateDirectory(dataDir); } catch { }
                var psi = new ProcessStartInfo(python, $"\"{run}\"")
                {
                    WorkingDirectory = Path.Combine(_root, "app"),
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };
                psi.EnvironmentVariables["AURALDESK_QQAPI_DATA"] = dataDir;
                _process = Process.Start(psi);
                if (_process == null)
                {
                    Error = "QQ 音乐组件启动失败：无法创建进程。";
                    return false;
                }
                _process.OutputDataReceived += (_, e) => LogSidecar(e.Data);
                _process.ErrorDataReceived += (_, e) => LogSidecar(e.Data);
                _process.BeginOutputReadLine();
                _process.BeginErrorReadLine();

                for (var i = 0; i < 40; i++) // 最长约 20 秒
                {
                    await Task.Delay(500);
                    if (await PingAsync())
                        return true;
                    if (_process.HasExited)
                    {
                        Error = "QQ 音乐组件启动失败（进程已退出）。";
                        return false;
                    }
                }
                Error = "QQ 音乐组件启动超时，请查看日志。";
                return false;
            }
            catch (Exception ex)
            {
                Error = "QQ 音乐组件启动异常：" + ex.Message;
                return false;
            }
        }

        private static async Task<bool> PingAsync()
        {
            try
            {
                using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
                using var resp = await client.GetAsync("http://127.0.0.1:8123/openapi.json");
                return resp.IsSuccessStatusCode;
            }
            catch
            {
                return false;
            }
        }

        private static void LogSidecar(string? line)
        {
            if (string.IsNullOrWhiteSpace(line))
                return;
            QQMusicDownloader.LogQqDebug("[sidecar] " + line);
        }

        /// <summary>停止 sidecar 进程（含子进程）。</summary>
        public void Stop()
        {
            if (_process == null)
                return;
            try
            {
                _process.Kill(entireProcessTree: true);
            }
            catch
            {
                // 进程可能已退出
            }
            _process.Dispose();
            _process = null;
        }
    }
}
