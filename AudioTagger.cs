using System;
using System.IO;
using System.Linq;

namespace AuralDesk
{
    /// <summary>
    /// 把 QQ 音乐的歌曲信息（标题/歌手/专辑）和封面写进下载文件。
    /// 使用 TagLib#，FLAC / MP3 / M4A / OGG 都能写，封面分别落到
    /// FLAC picture block、ID3 APIC、MP4 covr、Ogg METADATA_BLOCK_PICTURE。
    /// </summary>
    public static class AudioTagger
    {
        public static bool Tag(string file, QqSongItem song, byte[]? cover)
        {
            try
            {
                if (!File.Exists(file)) return false;
                using var tagged = TagLib.File.Create(file);

                if (!string.IsNullOrWhiteSpace(song.Title))
                    tagged.Tag.Title = song.Title.Trim();

                var artists = (song.Singer ?? "")
                    .Split(new[] { " / " }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(a => a.Trim())
                    .Where(a => a.Length > 0)
                    .ToArray();
                if (artists.Length > 0)
                {
                    tagged.Tag.Performers = artists;
                    tagged.Tag.AlbumArtists = artists;
                }

                if (!string.IsNullOrWhiteSpace(song.Album))
                    tagged.Tag.Album = song.Album.Trim();

                if (cover != null && cover.Length > 0)
                {
                    var mime = DetectImageMime(cover);
                    var picture = new TagLib.Picture(new TagLib.ByteVector(cover))
                    {
                        Type = TagLib.PictureType.FrontCover,
                        MimeType = mime,
                        Description = "Cover"
                    };
                    tagged.Tag.Pictures = new TagLib.IPicture[] { picture };
                }

                tagged.Save();
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static string DetectImageMime(byte[] data)
        {
            if (data.Length >= 3 && data[0] == 0xFF && data[1] == 0xD8 && data[2] == 0xFF)
                return "image/jpeg";
            if (data.Length >= 8 && data[0] == 0x89 && data[1] == 0x50
                && data[2] == 0x4E && data[3] == 0x47)
                return "image/png";
            return "image/jpeg";
        }
    }
}
