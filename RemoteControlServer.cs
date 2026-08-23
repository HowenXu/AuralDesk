using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace AuralDesk
{
    /// <summary>遥控接口返回的歌曲状态。</summary>
    public sealed class RemoteSongInfo
    {
        public string Title { get; set; } = "";
        public string Singer { get; set; } = "";
        public string Source { get; set; } = "";
        public double Position { get; set; }
        public double Length { get; set; }
        public bool Playing { get; set; }
    }

    /// <summary>遥控接口返回的队列项。</summary>
    public sealed class RemoteQueueEntry
    {
        public string Title { get; set; } = "";
        public string Singer { get; set; } = "";
        public bool IsCurrent { get; set; }
    }

    /// <summary>遥控接口返回的歌词行。</summary>
    public sealed class RemoteLyricLine
    {
        public double Time { get; set; }
        public string Main { get; set; } = "";
        public string Trans { get; set; } = "";
    }

    public sealed class RemoteLyricData
    {
        public List<RemoteLyricLine> Lines { get; set; } = new();
        public int ActiveIndex { get; set; } = -1;
    }

    /// <summary>
    /// 局域网遥控服务：HTTP 控制 API + UDP 发现广播响应（手机端 "AURALDESK_DISCOVER" → "AURALDESK_RESPONSE 38570"）。
    /// </summary>
    public sealed class RemoteControlServer
    {
        public const int HttpPort = 38570;
        public const int DiscoverPort = 38571;


        public delegate RemoteSongInfo? StatusProvider();
        public delegate void ControlHandler(string action);   // toggle / next / prev
        public delegate void SeekHandler(double position);
        public delegate List<RemoteQueueEntry> QueueProvider();
        public delegate RemoteLyricData? LyricProvider();
        public delegate string QqStateProvider(int qstart, int qcount); // 流媒体/播放队列状态 JSON（队列分页）
        public delegate void QqActionHandler(string json); // 执行流媒体/队列动作

        private TcpListener? tcp;
        private UdpClient? udp;
        private CancellationTokenSource? cts;

        private StatusProvider? getStatus;
        private ControlHandler? control;
        private SeekHandler? seek;
        private QueueProvider? getQueue;
        private LyricProvider? getLyric;
        private QqStateProvider? getQqState;
        private QqActionHandler? doQqAction;

        public bool Running => tcp != null;

        public void Start(
            StatusProvider status, ControlHandler controlAction, SeekHandler seekAction,
            QueueProvider queue, LyricProvider lyric,
            QqStateProvider? qqState = null, QqActionHandler? qqAction = null)
        {
            Stop();
            getStatus = status;
            control = controlAction;
            seek = seekAction;
            getQueue = queue;
            getLyric = lyric;
            getQqState = qqState;
            doQqAction = qqAction;

            cts = new CancellationTokenSource();
            tcp = new TcpListener(IPAddress.Any, HttpPort);
            tcp.Start();
            _ = Task.Run(() => HttpLoopAsync(cts.Token));

            udp = new UdpClient(DiscoverPort);
            _ = Task.Run(() => UdpLoopAsync(cts.Token));
        }

        public void Stop()
        {
            cts?.Cancel();
            try { tcp?.Stop(); } catch { }
            tcp = null;
            try { udp?.Close(); } catch { }
            udp = null;
            cts?.Dispose();
            cts = null;
        }

        private async Task UdpLoopAsync(CancellationToken ct)
        {
            var remote = new IPEndPoint(IPAddress.Any, 0);
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    var result = await udp!.ReceiveAsync(ct);
                    var msg = Encoding.UTF8.GetString(result.Buffer);
                    if (msg.Trim().Equals("AURALDESK_DISCOVER", StringComparison.OrdinalIgnoreCase))
                    {
                        var reply = Encoding.UTF8.GetBytes($"AURALDESK_RESPONSE {HttpPort}");
                        await udp.SendAsync(reply, reply.Length, result.RemoteEndPoint);
                    }
                }
                catch
                {
                    if (ct.IsCancellationRequested) break;
                }
            }
        }

        private async Task HttpLoopAsync(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    var client = await tcp!.AcceptTcpClientAsync(ct);
                    _ = Task.Run(() => HandleClientAsync(client));
                    continue;
                }
                catch
                {
                    break;
                }
            }
        }

        private async Task HandleClientAsync(TcpClient client)
        {
            using var _ = client;
            try
            {
                using var stream = client.GetStream();
                // 读取请求行 + 头（原始字节累积；头部之后可能同包带 body，必须保留）
                using var raw = new MemoryStream();
                var buf = new byte[4096];
                int contentLength = 0;
                var headerEnd = -1;
                while (headerEnd < 0)
                {
                    var n = await stream.ReadAsync(buf, 0, buf.Length);
                    if (n <= 0) return;
                    raw.Write(buf, 0, n);
                    var head = Encoding.UTF8.GetString(raw.ToArray());
                    headerEnd = head.IndexOf("\r\n\r\n", StringComparison.Ordinal);
                    if (raw.Length > 65536) return; // 防御异常请求
                }

                var rawBytes = raw.ToArray();
                var headText = Encoding.UTF8.GetString(rawBytes, 0, headerEnd + 4);
                foreach (var line in headText.Split("\r\n"))
                {
                    if (line.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase) &&
                        int.TryParse(line[(line.IndexOf(':') + 1)..].Trim(), out var cl))
                    {
                        contentLength = cl;
                    }
                }

                var lines = headText.Split("\r\n");
                var requestLine = lines.Length > 0 ? lines[0].Split(' ') : Array.Empty<string>();
                if (requestLine.Length < 2) return;
                var method = requestLine[0];
                var path = requestLine[1];

                var rawRequest = requestLine[1];
                var queryIdx = path.IndexOf('?');
                if (queryIdx >= 0) path = path[..queryIdx];

                // 读取 body（POST）
                var body = "";
                if (contentLength > 0)
                {
                    var bodyStart = headerEnd + 4;
                    var bodyGot = rawBytes.Length - bodyStart;
                    if (bodyGot >= contentLength)
                    {
                        // 头和 body 同包到达
                        body = Encoding.UTF8.GetString(rawBytes, bodyStart, contentLength);
                    }
                    else
                    {
                        var more = new byte[contentLength - bodyGot];
                        var got = 0;
                        while (got < more.Length)
                        {
                            var n = await stream.ReadAsync(more, got, more.Length - got);
                            if (n <= 0) break;
                            got += n;
                        }
                        var prefix = bodyGot > 0 ? Encoding.UTF8.GetString(rawBytes, bodyStart, bodyGot) : "";
                        body = prefix + Encoding.UTF8.GetString(more, 0, got);
                    }
                }

                string responseBody;
                var contentType = "text/html; charset=utf-8";
                switch (path)
                {
                    case "/":
                    case "/index.html":
                        responseBody = LoadRemotePage();
                        break;
                    case "/api/status":
                        contentType = "application/json; charset=utf-8";
                        responseBody = JsonSerializer.Serialize(getStatus?.Invoke() ?? new RemoteSongInfo());
                        break;
                    case "/api/toggle":
                    case "/api/next":
                    case "/api/prev":
                        contentType = "application/json; charset=utf-8";
                        control?.Invoke(path[(path.LastIndexOf('/') + 1)..]);
                        responseBody = "{\"ok\":true}";
                        break;
                    case "/api/seek":
                        contentType = "application/json; charset=utf-8";
                        if (method == "POST")
                        {
                            try
                            {
                                using var doc = JsonDocument.Parse(body);
                                if (doc.RootElement.TryGetProperty("position", out var p) && p.TryGetDouble(out var pos))
                                    seek?.Invoke(Math.Max(0, pos));
                            }
                            catch { }
                        }
                        responseBody = "{\"ok\":true}";
                        break;
                    case "/api/queue":
                        contentType = "application/json; charset=utf-8";
                        responseBody = JsonSerializer.Serialize(getQueue?.Invoke() ?? new List<RemoteQueueEntry>());
                        break;
                    case "/api/lyric":
                        contentType = "application/json; charset=utf-8";
                        responseBody = JsonSerializer.Serialize(getLyric?.Invoke() ?? new RemoteLyricData());
                        break;
                    case "/api/qq/state":
                        contentType = "application/json; charset=utf-8";
                        {
                            var qstart = 0;
                            var qcount = 30;
                            var qm = Regex.Match(rawRequest, "qstart=(\\d+)");
                            if (qm.Success) int.TryParse(qm.Groups[1].Value, out qstart);
                            var qc = Regex.Match(rawRequest, "qcount=(\\d+)");
                            if (qc.Success) int.TryParse(qc.Groups[1].Value, out qcount);
                            responseBody = getQqState?.Invoke(qstart, qcount) ?? "{}";
                        }
                        break;
                    case "/api/qq/action":
                        contentType = "application/json; charset=utf-8";
                        if (method == "POST" && !string.IsNullOrEmpty(body))
                            doQqAction?.Invoke(body);
                        responseBody = "{\"ok\":true}";
                        break;
                    default:
                        contentType = "application/json; charset=utf-8";
                        responseBody = "{\"error\":\"not found\"}";
                        break;
                }

                var responseBytes = Encoding.UTF8.GetBytes(responseBody);
                var header =
                    "HTTP/1.1 200 OK\r\n" +
                    "Content-Type: " + contentType + "\r\n" +
                    "Content-Length: " + responseBytes.Length + "\r\n" +
                    "Connection: close\r\n" +
                    "Access-Control-Allow-Origin: *\r\n" +
                    "\r\n";
                await stream.WriteAsync(Encoding.UTF8.GetBytes(header).AsMemory(0, header.Length));
                await stream.WriteAsync(responseBytes);
            }
            catch
            {
                // 单连接异常不影响服务
            }
        }

        private static string LoadRemotePage()
        {
            try
            {
                var asm = typeof(RemoteControlServer).Assembly;
                using var s = asm.GetManifestResourceStream("AuralDesk.remote_control.html");
                if (s == null) return "<html><body>remote page missing</body></html>";
                using var r = new StreamReader(s, Encoding.UTF8);
                return r.ReadToEnd();
            }
            catch
            {
                return "<html><body>remote page missing</body></html>";
            }
        }

    }
}
