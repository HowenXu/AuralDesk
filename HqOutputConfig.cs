using System;
using System.IO;
using System.Xml.Linq;

namespace AuralDesk
{
    /// <summary>
    /// HQPlayer 配置里的输出描述：
    /// Main = 卡片第一行（HQPlayer / HQPlayer NAA），Backend = 后端名，Detail = 地址/设备标识。
    /// </summary>
    public sealed class HqOutputDesc
    {
        public string Main = "";
        public string Backend = "";
        public string Detail = "";
    }

    /// <summary>
    /// HQPlayer Desktop 的输出配置（只读）。控制协议里没有切换输出设备的命令，
    /// 但 settings.xml 里记录了当前后端和设备，可以准确显示出来。
    /// </summary>
    public static class HqOutputConfig
    {
        private static HqOutputDesc cached = new();
        private static DateTime cachedWriteTime = DateTime.MinValue;

        public static string SettingsPath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "HQPlayer", "settings.xml");

        /// <summary>
        /// 读 settings.xml 的 engine 节点。只有 NetworkAudioAdapter / NetworkAudioAdapter IPv6
        /// 才在标题里写 NAA；ASIO/WASAPI 等一律只写 HQPlayer。
        /// </summary>
        public static HqOutputDesc Describe()
        {
            try
            {
                var path = SettingsPath;
                if (!File.Exists(path)) return new HqOutputDesc();
                var mtime = File.GetLastWriteTimeUtc(path);
                if (mtime == cachedWriteTime) return cached;

                var doc = XDocument.Load(path);
                var engine = doc.Root?.Element("engine");
                var type = engine?.Attribute("type")?.Value ?? "";
                var result = new HqOutputDesc();
                switch (type.ToLowerInvariant())
                {
                    case "network":
                    case "network6":   // HQPlayer 用 network6 表示 NetworkAudioAdapter IPv6
                    {
                        var net = engine?.Element("network");
                        var address = net?.Attribute("address")?.Value ?? "";
                        var device = net?.Attribute("device")?.Value ?? "";
                        result.Main = "HQPlayer NAA";
                        result.Backend = "NetworkAudioAdapter" +
                            (net?.Attribute("ipv6")?.Value == "1" ? " IPv6" : "");
                        // 只显示 NAA 地址；hw:CARD=... 这类 ALSA 设备标识在卡片里又长又难读，
                        // 用户要看完整名字可以去 HQPlayer 的 Outputs 页。
                        result.Detail = address;
                        break;
                    }
                    case "asio":
                    {
                        var device = engine?.Element("asio")?.Attribute("device")?.Value ?? "";
                        result.Main = "HQPlayer";
                        result.Backend = "ASIO";
                        result.Detail = device;
                        break;
                    }
                    case "wasapi":
                    {
                        var device = engine?.Element("wasapi")?.Attribute("device")?.Value ?? "";
                        result.Main = "HQPlayer";
                        result.Backend = "WASAPI";
                        // WASAPI 存的是 {GUID} 端点，展示出来没有意义
                        result.Detail = device.StartsWith("{", StringComparison.Ordinal) ? "" : device;
                        break;
                    }
                    default:
                        result.Main = "HQPlayer";
                        result.Backend = type;
                        break;
                }
                cachedWriteTime = mtime;
                cached = result;
                return result;
            }
            catch
            {
                return cached;
            }
        }
    }
}
