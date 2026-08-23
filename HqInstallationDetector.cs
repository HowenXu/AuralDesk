using System;
using System.IO;
using Microsoft.Win32;

namespace AuralDesk
{
    /// <summary>检测本机是否安装了 HQPlayer（常见安装路径 + 卸载注册表项）。</summary>
    internal static class HqInstallationDetector
    {
        private static readonly string[] ExeNames =
        {
            "HQPlayer6Desktop.exe",
            "HQPlayer5Desktop.exe",
            "HQPlayer.exe"
        };

        public static bool IsInstalled() => LocateExe() != null;

        /// <summary>自动定位 HQPlayer 主程序：常见安装路径 → 卸载注册表项。</summary>
        public static string? LocateExe()
        {
            var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            foreach (var relative in new[]
            {
                @"Signalyst\HQPlayer 6 Desktop",
                @"Signalyst\HQPlayer 5 Desktop",
                @"Signalyst\HQPlayer Desktop"
            })
            {
                var dir = Path.Combine(programFiles, relative);
                foreach (var name in ExeNames)
                {
                    var candidate = Path.Combine(dir, name);
                    if (File.Exists(candidate))
                        return candidate;
                }
            }

            var registryDir = FindInstallDirFromRegistry();
            if (registryDir != null)
            {
                foreach (var name in ExeNames)
                {
                    var candidate = Path.Combine(registryDir, name);
                    if (File.Exists(candidate))
                        return candidate;
                }
            }
            return null;
        }

        private static string? FindInstallDirFromRegistry()
        {
            var roots = new[]
            {
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall",
                @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall"
            };
            foreach (var root in roots)
            {
                foreach (var hive in new[] { Registry.LocalMachine, Registry.CurrentUser })
                {
                    using var key = hive.OpenSubKey(root);
                    if (key == null)
                        continue;
                    foreach (var sub in key.GetSubKeyNames())
                    {
                        using var s = key.OpenSubKey(sub);
                        if (s == null)
                            continue;
                        if (s.GetValue("DisplayName") is not string name ||
                            !name.Contains("HQPlayer", StringComparison.OrdinalIgnoreCase))
                        {
                            continue;
                        }
                        var dir = ResolveInstallDir(s);
                        if (dir != null)
                            return dir;
                    }
                }
            }
            return null;
        }

        private static string? ResolveInstallDir(RegistryKey uninstallKey)
        {
            var candidate = uninstallKey.GetValue("InstallLocation") as string;
            if (string.IsNullOrWhiteSpace(candidate))
            {
                candidate = uninstallKey.GetValue("UninstallString") as string;
                if (!string.IsNullOrWhiteSpace(candidate))
                {
                    candidate = candidate.Trim('"');
                    var idx = candidate.LastIndexOf('\\');
                    if (idx > 0)
                        candidate = candidate[..idx];
                }
            }
            if (string.IsNullOrWhiteSpace(candidate))
            {
                candidate = uninstallKey.GetValue("DisplayIcon") as string;
                if (!string.IsNullOrWhiteSpace(candidate))
                {
                    candidate = candidate.Trim('"');
                    var comma = candidate.IndexOf(',');
                    if (comma > 0)
                        candidate = candidate[..comma];
                    candidate = Path.GetDirectoryName(candidate);
                }
            }
            return !string.IsNullOrWhiteSpace(candidate) && Directory.Exists(candidate)
                ? candidate
                : null;
        }
    }
}
