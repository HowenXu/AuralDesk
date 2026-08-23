using System;
using System.Globalization;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace AuralDesk
{
    /// <summary>HQPlayer 控制协议快照（XML-over-TCP，默认端口 4321，参考 hqp6-control）。</summary>
    public sealed class HqPlayerStatus
    {
        public int State;           // 0=停止 1=暂停 2=播放 3=正在停止
        public double Position;     // 秒
        public double Length;       // 秒
        public int Track;
        public int TracksTotal;
        public long ActiveRate;     // Hz，升频后的输出采样率
        public string ActiveMode = "";  // 例如 "SDM (DSD)" / "PCM" / "[source]"
        public string ActiveFilter = "";
        public string ActiveShaper = "";
        public string Song = "";
    }

    public sealed class HqPlayerClient
    {
        private readonly object sync = new();
        private string host;
        private int port;
        private TcpClient? client;
        private NetworkStream? stream;

        public bool Connected
        {
            get { lock (sync) return client?.Connected == true; }
        }

        public HqPlayerClient(string host, int port)
        {
            this.host = host;
            this.port = port;
        }

        public void UpdateEndpoint(string host, int port)
        {
            lock (sync)
            {
                this.host = host;
                this.port = port;
                DisconnectInternal();
            }
        }

        private bool ConnectInternal()
        {
            try
            {
                DisconnectInternal();
                var c = new TcpClient();
                c.NoDelay = true;
                c.Connect(host, port);
                // HQPlayer 在 DSD256 EC 等高负载升频时控制响应会明显变慢，超时放宽到 3s
                c.ReceiveTimeout = 3000;
                c.SendTimeout = 3000;
                client = c;
                stream = c.GetStream();
                return true;
            }
            catch
            {
                DisconnectInternal();
                return false;
            }
        }

        private void DisconnectInternal()
        {
            try { stream?.Dispose(); } catch { }
            try { client?.Dispose(); } catch { }
            stream = null;
            client = null;
        }

        public void Disconnect()
        {
            lock (sync) DisconnectInternal();
        }

        /// <summary>发送一条 XML 控制命令并读取完整响应文档；失败返回 null。</summary>
        public string? SendCommand(string commandXml)
        {
            lock (sync)
            {
                if (client == null || stream == null || !client.Connected)
                {
                    if (!ConnectInternal()) return null;
                }
                try
                {
                    var payload = Encoding.UTF8.GetBytes(commandXml + "\n");
                    stream!.Write(payload, 0, payload.Length);
                    stream.Flush();

                    var buffer = new byte[8192];
                    var data = new MemoryStream();
                    while (true)
                    {
                        int n;
                        try
                        {
                            n = stream.Read(buffer, 0, buffer.Length);
                        }
                        catch (IOException)
                        {
                            break; // 超时视为响应结束
                        }
                        if (n <= 0) break;
                        data.Write(buffer, 0, n);
                        var text = Sanitize(Encoding.UTF8.GetString(data.ToArray()));
                        if (IsCompleteDocument(text)) break;
                    }
                    var result = Sanitize(Encoding.UTF8.GetString(data.ToArray()));
                    return string.IsNullOrWhiteSpace(result) ? null : result;
                }
                catch
                {
                    DisconnectInternal();
                    return null;
                }
            }
        }

        private static string Sanitize(string xml)
        {
            // 属性值里的裸 & 会破坏 XML 解析，宽松补全实体
            return Regex.Replace(xml, "&(?!(amp|lt|gt|quot|apos|#))", "&amp;");
        }

        private static bool IsCompleteDocument(string text)
        {
            text = text.Trim();
            if (!text.StartsWith("<?xml", StringComparison.OrdinalIgnoreCase) &&
                !text.StartsWith("<", StringComparison.Ordinal)) return false;
            try
            {
                XDocument.Parse(text);
                return true;
            }
            catch
            {
                return false;
            }
        }

        public HqPlayerStatus? GetStatus()
        {
            var resp = SendCommand("<?xml version=\"1.0\"?><Status subscribe=\"0\"/>");
            if (string.IsNullOrEmpty(resp)) return null;
            try
            {
                var root = XDocument.Parse(resp).Root;
                if (root == null || root.Name.LocalName != "Status") return null;
                var st = new HqPlayerStatus
                {
                    State = GetInt(root, "state"),
                    Position = GetDouble(root, "position"),
                    Length = GetDouble(root, "length"),
                    Track = GetInt(root, "track"),
                    TracksTotal = GetInt(root, "tracks_total"),
                    ActiveRate = GetLong(root, "active_rate"),
                    ActiveMode = GetAttr(root, "active_mode"),
                    ActiveFilter = GetAttr(root, "active_filter"),
                    ActiveShaper = GetAttr(root, "active_shaper")
                };
                var meta = root.Element("metadata");
                if (meta != null) st.Song = GetAttr(meta, "song");
                return st;
            }
            catch
            {
                return null;
            }
        }

        public bool ClearPlaylist() =>
            HasOk(SendCommand("<?xml version=\"1.0\"?><PlaylistClear/>"));

        public bool AddToPlaylist(string path)
        {
            // 注意：HQPlayer 只认原始路径（百分号编码的 URI 会导致中文/空格解析失败），
            // 只需把反斜杠归一化并对 & 做 XML 实体转义即可。
            var uri = path.Replace('\\', '/');
            return HasOk(SendCommand(
                "<?xml version=\"1.0\"?><PlaylistAdd uri=\"" + EscapeAttr(uri) + "\"/>"));
        }

        public bool Play() =>
            HasOk(SendCommand("<?xml version=\"1.0\"?><Play last=\"0\"/>"));

        /// <summary>明确播放列表指定位置（index 从 0 开始）。</summary>
        public bool PlayIndex(int index) =>
            HasOk(SendCommand("<?xml version=\"1.0\"?><PlayListPlay index=\"" + index + "\"/>"));

        public bool Pause() =>
            HasOk(SendCommand("<?xml version=\"1.0\"?><Pause/>"));

        public bool Stop() =>
            HasOk(SendCommand("<?xml version=\"1.0\"?><Stop/>"));

        public bool Next() =>
            HasOk(SendCommand("<?xml version=\"1.0\"?><Next/>"));

        public bool Previous() =>
            HasOk(SendCommand("<?xml version=\"1.0\"?><Previous/>"));

        public bool Seek(double seconds) =>
            HasOk(SendCommand(
                "<?xml version=\"1.0\"?><Seek position=\"" +
                seconds.ToString("0.###", CultureInfo.InvariantCulture) + "\"/>"));

        /// <summary>清空 HQPlayer 播放列表并播放指定本地文件。</summary>
        public bool PlayFile(string path)
        {
            // 先 Stop 再清列表：HQPlayer 播放中直接 Clear 可能残留上一首（列表多出一首，
            // 导致实际播放与软件显示错位）；Stop 后清空更干净。
            Stop();
            if (!ClearPlaylist()) return false;
            if (!AddToPlaylist(path)) return false;
            // 明确播放第 0 项（列表意外残留时也不会播错歌）
            return PlayIndex(0) || Play();
        }

        private static bool HasOk(string? resp) =>
            resp != null && resp.Contains("result=\"OK\"", StringComparison.Ordinal);

        private static string EscapeAttr(string value) =>
            value.Replace("&", "&amp;").Replace("\"", "&quot;").Replace("<", "&lt;");

        private static string GetAttr(XElement el, string name) =>
            el.Attribute(name)?.Value ?? "";

        private static int GetInt(XElement el, string name)
        {
            var v = el.Attribute(name)?.Value;
            return int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out var r) ? r : 0;
        }

        private static long GetLong(XElement el, string name)
        {
            var v = el.Attribute(name)?.Value;
            return long.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out var r) ? r : 0;
        }

        private static double GetDouble(XElement el, string name)
        {
            var v = el.Attribute(name)?.Value;
            return double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out var r) ? r : 0;
        }
    }
}
