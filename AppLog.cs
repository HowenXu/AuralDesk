using System;
using System.IO;

namespace AuralDesk
{
    /// <summary>统一调试日志（写入 文档\AuralDesk\qq_debug.log），各模块共用。</summary>
    internal static class AppLog
    {
        // 分片下载的多个 worker 会同时写日志；无锁时 File.AppendAllText 会互相抢占句柄，
        // 抛 IOException 后日志被写到备用目录，排查时日志会莫名分成两处
        private static readonly object Sync = new();

        public static void Write(string message)
        {
            var line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {message}{Environment.NewLine}";
            lock (Sync)
            {
                // 主目录为 文档\AuralDesk；个别电脑文档目录不可写时回退到 LocalAppData
                foreach (var dir in new[]
                {
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "AuralDesk"),
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AuralDesk", "logs")
                })
                {
                    try
                    {
                        Directory.CreateDirectory(dir);
                        File.AppendAllText(Path.Combine(dir, "qq_debug.log"), line);
                        return;
                    }
                    catch
                    {
                        // 尝试下一个目录
                    }
                }
            }
        }
    }
}
