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
        public double Volume;       // dB（-80..20 之间，具体范围由 HQPlayer 设置决定）
        public long ActiveRate;     // Hz，升频后的输出采样率
        public string ActiveMode = "";  // 例如 "SDM (DSD)" / "PCM" / "[source]"
        public string ActiveFilter = "";
        public string ActiveShaper = "";
        public double ProcessSpeed;      // 处理倍速（>1 才追得上实时）
        public int ActiveBits;
        public int ActiveChannels;
        public string Song = "";
    }

    /// <summary>HQPlayer 当前配置的处理链（来自 &lt;State/&gt;，数值都是枚举列表的 index）。</summary>
    public sealed class HqPlayerState
    {
        public int State;
        public int Mode;
        public int Filter1x;
        public int FilterNx;
        public int Shaper;
        public int Rate;
        public double Volume;
        public int ActiveMode;
        public long ActiveRate;
    }

    /// <summary>枚举列表项（Modes / Filters / Shapers / Rates）。Index 是 Set* 要用的值。</summary>
    public sealed class HqListEntry
    {
        public int Index;
        public string Name = "";
        public long Rate;
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
                    Volume = GetDouble(root, "volume"),
                    ActiveRate = GetLong(root, "active_rate"),
                    ActiveMode = GetAttr(root, "active_mode"),
                    ActiveFilter = GetAttr(root, "active_filter"),
                    ActiveShaper = GetAttr(root, "active_shaper"),
                    ProcessSpeed = GetDouble(root, "process_speed"),
                    ActiveBits = GetInt(root, "active_bits"),
                    ActiveChannels = GetInt(root, "active_channels")
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

        /// <summary>读当前配置的处理链。注意 filter/shaper/rate/mode 都是枚举列表的 index。</summary>
        public HqPlayerState? GetState()
        {
            var resp = SendCommand("<?xml version=\"1.0\"?><State/>");
            if (string.IsNullOrEmpty(resp)) return null;
            try
            {
                var root = XDocument.Parse(resp).Root;
                if (root == null || root.Name.LocalName != "State") return null;
                return new HqPlayerState
                {
                    State = GetInt(root, "state"),
                    Mode = GetInt(root, "mode"),
                    Filter1x = GetInt(root, "filter1x"),
                    FilterNx = GetInt(root, "filterNx") != 0 ? GetInt(root, "filterNx") : GetInt(root, "filter"),
                    Shaper = GetInt(root, "shaper"),
                    Rate = GetInt(root, "rate"),
                    Volume = GetDouble(root, "volume"),
                    ActiveMode = GetInt(root, "active_mode"),
                    ActiveRate = GetLong(root, "active_rate")
                };
            }
            catch
            {
                return null;
            }
        }

        private readonly System.Collections.Generic.Dictionary<string, List<HqListEntry>> listCache = new();
        private readonly System.Collections.Generic.Dictionary<string, DateTime> listCacheTime = new();

        /// <summary>
        /// 读 Modes / Filters / Shapers / Rates 枚举。列表随输出模式变化，缓存 60 秒；
        /// 切换模式后调用 <see cref="InvalidateLists"/> 让下次重新拉取。
        /// </summary>
        public List<HqListEntry> GetList(string kind, bool refresh = false)
        {
            lock (sync)
            {
                if (!refresh && listCache.TryGetValue(kind, out var cached)
                    && DateTime.UtcNow - listCacheTime[kind] < TimeSpan.FromSeconds(60))
                    return cached;

                var cmd = kind switch
                {
                    "modes" => "<GetModes/>",
                    "filters" => "<GetFilters/>",
                    "shapers" => "<GetShapers/>",
                    "rates" => "<GetRates/>",
                    _ => null
                };
                if (cmd == null) return new List<HqListEntry>();

                var result = new List<HqListEntry>();
                var resp = SendCommand("<?xml version=\"1.0\"?>" + cmd);
                if (!string.IsNullOrEmpty(resp))
                {
                    try
                    {
                        var root = XDocument.Parse(resp).Root;
                        if (root != null)
                        {
                            foreach (var el in root.Elements())
                            {
                                result.Add(new HqListEntry
                                {
                                    Index = GetInt(el, "index"),
                                    Name = GetAttr(el, "name"),
                                    Rate = GetLong(el, "rate")
                                });
                            }
                        }
                    }
                    catch
                    {
                        // 解析失败返回空表，下次再试
                    }
                }
                listCache[kind] = result;
                listCacheTime[kind] = DateTime.UtcNow;
                return result;
            }
        }

        public void InvalidateLists()
        {
            lock (sync)
            {
                listCache.Clear();
                listCacheTime.Clear();
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

        /// <summary>设置 HQPlayer 音量（dB）。HQPlayer 的 XML 控制接口只认 dB 数值。</summary>
        public bool SetVolume(double db) =>
            HasOk(SendCommand(
                "<?xml version=\"1.0\"?><Volume value=\"" +
                db.ToString("0.#", CultureInfo.InvariantCulture) + "\"/>"));

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
