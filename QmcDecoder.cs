using System;
using System.Collections.Generic;

namespace AuralDesk
{
    /// <summary>
    /// QQ 音乐 QMC 解密（参考 unlock-music 项目 rong6/unlock-music 的算法）。
    /// 支持 mflac/mgg 及 qmc0/qmc3/qmcogg/qmcflac/bkcmp3/bkcflac/tkm 等掩码格式。
    /// </summary>
    public static class QmcDecoder
    {
        private static readonly byte[] FlacHeader = { 0x66, 0x4C, 0x61, 0x43 };
        private static readonly byte[] Mp3Header = { 0x49, 0x44, 0x33 };
        private static readonly byte[] OggHeader = { 0x4F, 0x67, 0x67, 0x53 };
        private static readonly byte[] M4aHeader = { 0x66, 0x74, 0x79, 0x70 };

        private static readonly byte[] QmoOggConstHeader =
        {
            79, 103, 103, 83, 0, 2, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
            0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1, 30, 1, 118, 111, 114,
            98, 105, 115, 0, 0, 0, 0, 2, 68, 172, 0, 0, 0, 0, 0, 0,
            0, 238, 2, 0, 0, 0, 0, 0, 184, 1, 79, 103, 103, 83, 0, 0,
            0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1, 0, 0, 0,
            0, 0, 0, 0, 16, 0, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255,
            255, 255, 255, 255, 0, 3, 118, 111, 114, 98, 105, 115, 44, 0, 0, 0,
            88, 105, 112, 104, 46, 79, 114, 103, 32, 108, 105, 98, 86, 111, 114, 98,
            105, 115, 32, 73, 32, 50, 48, 49, 53, 48, 49, 48, 53, 32, 40, 226,
            155, 132, 226, 155, 132, 226, 155, 132, 226, 155, 132, 41, 0, 0, 0, 0,
            0, 0, 0, 0, 84, 73, 84, 76, 69, 61,
        };

        private static readonly byte[] QmoOggConstHeaderConfidence =
        {
            9, 9, 9, 9, 9, 9, 9, 9, 9, 9, 9, 9, 9, 9, 0, 0,
            0, 0, 9, 9, 9, 9, 0, 0, 0, 0, 9, 9, 9, 9, 9, 9,
            9, 9, 9, 9, 9, 9, 9, 6, 3, 3, 3, 3, 6, 6, 6, 6,
            3, 3, 3, 3, 6, 6, 6, 6, 6, 9, 9, 9, 9, 9, 9, 9,
            9, 9, 9, 9, 9, 9, 9, 9, 0, 0, 0, 0, 9, 9, 9, 9,
            0, 0, 0, 0, 6, 0, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3,
            3, 3, 3, 3, 0, 9, 9, 9, 9, 9, 9, 9, 9, 9, 9, 9,
            9, 9, 9, 9, 9, 9, 9, 9, 9, 9, 9, 9, 9, 9, 9, 9,
            9, 9, 9, 9, 9, 9, 9, 9, 9, 9, 9, 9, 9, 9, 9, 9,
            9, 9, 9, 9, 9, 9, 9, 9, 9, 9, 9, 9, 0, 1, 9, 9,
            0, 1, 9, 9, 9, 9, 9, 9, 9, 9,
        };

        private static readonly byte[] QmcDefaultMaskMatrix =
        {
            74, 214, 202, 144, 103, 247, 82, 94, 149, 35, 159, 19, 17, 126, 71, 116,
            61, 144, 170, 63, 81, 198, 9, 213, 159, 250, 102, 249, 243, 214, 161, 144,
            160, 247, 240, 29, 149, 222, 159, 132, 17, 244, 14, 116, 187, 144, 188, 63,
            146, 0, 9, 91, 159, 98, 102, 161,
        };

        private const byte QmcDefaultMaskSuperA = 195;
        private const byte QmcDefaultMaskSuperB = 216;

        /// <summary>QMC 掩码。</summary>
        public sealed class Mask
        {
            private byte[] _matrix128;

            /// <summary>用 58 字节矩阵 + 两个 super 字节构造 128 字节掩码（mgg 探测用）。</summary>
            public Mask(byte[] matrix58, byte superA, byte superB)
            {
                if (matrix58.Length != 56) throw new InvalidOperationException("incorrect mask58 matrix length");
                var m128 = new byte[128];
                for (int rowIdx = 0; rowIdx < 8; rowIdx++)
                {
                    int dst = rowIdx * 16;
                    m128[dst] = superA;
                    Array.Copy(matrix58, 7 * rowIdx, m128, dst + 1, 7);
                    m128[dst + 8] = superB;
                    for (int k = 0; k < 7; k++)
                    {
                        m128[dst + 9 + k] = matrix58[56 - 7 - 7 * rowIdx + (6 - k)];
                    }
                }
                _matrix128 = m128;
            }

            /// <summary>直接用 128 字节掩码（mflac 探测用）。</summary>
            public Mask(byte[] matrix128)
            {
                if (matrix128.Length != 128) throw new InvalidOperationException("incorrect mask128 length");
                _matrix128 = matrix128;
            }

            public byte[] Decrypt(byte[] data)
            {
                var dst = (byte[])data.Clone();
                int index = -1;
                int maskIdx = -1;
                for (int cur = 0; cur < data.Length; cur++)
                {
                    index++;
                    maskIdx++;
                    if (index == 0x8000 || (index > 0x8000 && (index + 1) % 0x8000 == 0))
                    {
                        index++;
                        maskIdx++;
                    }
                    maskIdx %= 128;
                    dst[cur] ^= _matrix128[maskIdx];
                }
                return dst;
            }
        }

        /// <summary>默认掩码，适用于 qmc0/qmc3/qmcflac/qmcogg/bkcmp3/bkcflac/tkm 等。</summary>
        public static Mask CreateDefaultMask()
            => new Mask(QmcDefaultMaskMatrix, QmcDefaultMaskSuperA, QmcDefaultMaskSuperB);

        /// <summary>按扩展名选择解密路径，掩码检测失败（新加密格式）返回 null。</summary>
        public static (byte[] data, string ext)? Decrypt(byte[] file, string ext)
        {
            string e = ext.ToLowerInvariant();
            byte[] audioData;
            Mask seed;
            switch (e)
            {
                case "mflac":
                case "mgg":
                    if (file.Length < 0x170) return null;
                    audioData = file[..^0x170];
                    var detected = e == "mflac" ? DetectMflac(audioData) : DetectMgg(audioData);
                    if (detected == null) return null; // 掩码检测失败（新加密格式需外部 key 服务）
                    seed = detected;
                    break;
                case "qmc0":
                case "qmc3":
                case "qmcogg":
                case "qmcflac":
                case "bkcmp3":
                case "bkcflac":
                case "tkm":
                    audioData = file;
                    seed = CreateDefaultMask();
                    break;
                default:
                    return null;
            }
            var dec = seed.Decrypt(audioData);
            string fallback = e switch
            {
                "mflac" => "flac",
                "mgg" => "ogg",
                "qmc0" or "qmc3" => "mp3",
                "qmcogg" => "ogg",
                "qmcflac" => "flac",
                "bkcmp3" => "mp3",
                "bkcflac" => "flac",
                "tkm" => "m4a",
                _ => "bin"
            };
            return (dec, DetectAudioExt(dec, fallback));
        }

        /// <summary>
        /// 扩展名识别不出加密类型（下载文件名是普通 .flac/.ogg 等）时，
        /// 自动探测 mflac/mgg 掩码并解密；失败返回 null。
        /// </summary>
        public static (byte[] data, string ext)? TryDecryptAny(byte[] file)
        {
            if (file.Length < 0x170)
                return null;
            var audioData = file[..^0x170];

            var mflac = DetectMflac(audioData);
            if (mflac != null)
            {
                var dec = mflac.Decrypt(audioData);
                if (dec.Length >= 4 && IsBytesEqual(FlacHeader, dec[..4]))
                    return (dec, "flac");
            }

            var mgg = DetectMgg(audioData);
            if (mgg != null)
            {
                var dec = mgg.Decrypt(audioData);
                var detected = DetectAudioExt(dec, "ogg");
                if (dec.Length > 0 && detected != "bin")
                    return (dec, detected);
            }
            return null;
        }

        /// <summary>按文件头检测真实音频格式。</summary>
        public static string DetectRealExt(byte[] data, string fallback)
            => DetectAudioExt(data, fallback);

        private static Mask? DetectMflac(byte[] data)
        {
            int searchLen = Math.Min(0x8000, data.Length);
            Mask? mask = null;
            for (int blockIdx = 0; blockIdx < searchLen; blockIdx += 128)
            {
                try
                {
                    var chunk = new byte[128];
                    Array.Copy(data, blockIdx, chunk, 0, Math.Min(128, data.Length - blockIdx));
                    mask = new Mask(chunk);
                    var probe = mask.Decrypt(data[..FlacHeader.Length]);
                    if (IsBytesEqual(FlacHeader, probe)) break;
                }
                catch
                {
                    // 该 128 字节块不是有效掩码，继续探测下一块
                }
            }
            return mask;
        }

        private static Mask? DetectMgg(byte[] input)
        {
            if (input.Length < QmoOggConstHeader.Length) return null;
            var confidence = new Dictionary<int, Dictionary<byte, int>>();
            for (int i = 0; i < 58; i++) confidence[i] = new Dictionary<byte, int>();
            for (int idx128 = 0; idx128 < QmoOggConstHeader.Length; idx128++)
            {
                int conf = QmoOggConstHeaderConfidence[idx128];
                if (conf == 0) continue;
                int idx58 = GetMask58Index(idx128);
                byte mask = (byte)(input[idx128] ^ QmoOggConstHeader[idx128]);
                if (confidence[idx58].TryGetValue(mask, out var cur)) confidence[idx58][mask] = cur + conf;
                else confidence[idx58][mask] = conf;
            }
            try
            {
                var matrix = new byte[56];
                for (int i = 0; i < 56; i++) matrix[i] = GetMaskConfidenceResult(confidence[i]);
                byte superA = GetMaskConfidenceResult(confidence[56]);
                byte superB = GetMaskConfidenceResult(confidence[57]);
                return new Mask(matrix, superA, superB);
            }
            catch
            {
                return null;
            }
        }

        private static byte GetMaskConfidenceResult(Dictionary<byte, int> c)
        {
            if (c.Count == 0) throw new InvalidOperationException("can not match at least one key");
            byte result = 0;
            int conf = 0;
            foreach (var kv in c)
            {
                if (kv.Value > conf)
                {
                    result = kv.Key;
                    conf = kv.Value;
                }
            }
            return result;
        }

        private static int GetMask58Index(int idx128)
        {
            if (idx128 > 127) idx128 %= 128;
            int col = idx128 % 16;
            int row = (idx128 - col) / 16;
            switch (col)
            {
                case 0: row = 8; col = 0; break;       // Super 1
                case 8: row = 8; col = 1; break;       // Super 2
                default:
                    if (col > 7) { row = 7 - row; col = 15 - col; }
                    else { col -= 1; }
                    break;
            }
            return row * 7 + col;
        }

        private static string DetectAudioExt(byte[] data, string fallback)
        {
            if (data.Length >= Mp3Header.Length && IsBytesEqual(Mp3Header, data[..Mp3Header.Length])) return "mp3";
            if (data.Length >= FlacHeader.Length && IsBytesEqual(FlacHeader, data[..FlacHeader.Length])) return "flac";
            if (data.Length >= OggHeader.Length && IsBytesEqual(OggHeader, data[..OggHeader.Length])) return "ogg";
            if (data.Length >= 8 && IsBytesEqual(M4aHeader, data[4..8])) return "m4a";
            return fallback;
        }

        private static bool IsBytesEqual(byte[] a, byte[] b)
        {
            if (b.Length < a.Length) return false;
            for (int i = 0; i < a.Length; i++)
            {
                if (a[i] != b[i]) return false;
            }
            return true;
        }
    }
}
