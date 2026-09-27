using System;
using System.Globalization;
using System.IO;
using System.Text;

namespace BilderUmbenenner
{
    /// <summary>
    /// Minimal EXIF reader for the capture date (DateTimeOriginal, falling back to
    /// DateTimeDigitized). Supports JPEG, TIFF-based files (TIFF, DNG and most RAW
    /// formats), PNG (eXIf chunk), WebP (EXIF chunk) and, by scanning, HEIC/AVIF/CR3.
    /// </summary>
    public static class ExifReader
    {
        private const int ScanLimit = 4 * 1024 * 1024;

        public static DateTime? ReadCaptureDate(string path)
        {
            try
            {
                using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                {
                    var head = new byte[16];
                    int n = fs.Read(head, 0, head.Length);
                    if (n < 12) return null;
                    fs.Position = 0;

                    if (head[0] == 0xFF && head[1] == 0xD8) return FromJpeg(fs);
                    if ((head[0] == 'I' && head[1] == 'I') || (head[0] == 'M' && head[1] == 'M'))
                    {
                        var d = FromTiff(ReadBytes(fs, ScanLimit), 0);
                        if (d != null) return d;
                    }
                    if (head[0] == 0x89 && head[1] == 'P' && head[2] == 'N' && head[3] == 'G') return FromPng(fs);
                    if (Ascii(head, 0, 4) == "RIFF" && Ascii(head, 8, 4) == "WEBP") return FromWebp(fs);

                    fs.Position = 0;
                    return FromScan(ReadBytes(fs, ScanLimit));
                }
            }
            catch
            {
                return null;
            }
        }

        private static DateTime? FromJpeg(Stream s)
        {
            s.Position = 2;
            while (true)
            {
                int b = s.ReadByte();
                while (b == 0xFF) b = s.ReadByte();
                if (b < 0 || b == 0xD9 || b == 0xDA) return null; // EOI / start of scan
                int hi = s.ReadByte(), lo = s.ReadByte();
                if (lo < 0) return null;
                int len = (hi << 8) | lo;
                if (len < 2) return null;
                if (b == 0xE1)
                {
                    var seg = ReadBytes(s, len - 2);
                    if (seg.Length > 6 && Ascii(seg, 0, 6) == "Exif\0\0")
                    {
                        var d = FromTiff(seg, 6);
                        if (d != null) return d;
                    }
                }
                else s.Position += len - 2;
            }
        }

        private static DateTime? FromPng(Stream s)
        {
            s.Position = 8;
            var hdr = new byte[8];
            while (s.Read(hdr, 0, 8) == 8)
            {
                uint len = (uint)((hdr[0] << 24) | (hdr[1] << 16) | (hdr[2] << 8) | hdr[3]);
                string type = Ascii(hdr, 4, 4);
                if (type == "eXIf" || type == "exIf")
                {
                    var data = ReadBytes(s, (int)Math.Min(len, ScanLimit));
                    int off = data.Length > 6 && Ascii(data, 0, 6) == "Exif\0\0" ? 6 : 0;
                    return FromTiff(data, off);
                }
                if (type == "IEND") return null;
                s.Position += len + 4; // data + CRC
            }
            return null;
        }

        private static DateTime? FromWebp(Stream s)
        {
            s.Position = 12;
            var hdr = new byte[8];
            while (s.Read(hdr, 0, 8) == 8)
            {
                string type = Ascii(hdr, 0, 4);
                int len = BitConverter.ToInt32(hdr, 4);
                if (len < 0) return null;
                if (type == "EXIF")
                {
                    var data = ReadBytes(s, Math.Min(len, ScanLimit));
                    int off = data.Length > 6 && Ascii(data, 0, 6) == "Exif\0\0" ? 6 : 0;
                    return FromTiff(data, off);
                }
                s.Position += len + (len & 1);
            }
            return null;
        }

        /// <summary>Searches the data for an embedded EXIF/TIFF block (HEIC, AVIF, CR3 ...).</summary>
        private static DateTime? FromScan(byte[] data)
        {
            for (int i = 0; i + 8 < data.Length; i++)
            {
                bool ii = data[i] == 'I' && data[i + 1] == 'I' && data[i + 2] == 0x2A && data[i + 3] == 0;
                bool mm = data[i] == 'M' && data[i + 1] == 'M' && data[i + 2] == 0 && data[i + 3] == 0x2A;
                if (!ii && !mm) continue;
                var d = FromTiff(data, i);
                if (d != null) return d;
            }
            return null;
        }

        /// <summary>Parses a TIFF structure starting at 'start' (byte order mark).</summary>
        private static DateTime? FromTiff(byte[] d, int start)
        {
            if (start + 8 > d.Length) return null;
            bool le;
            if (d[start] == 'I' && d[start + 1] == 'I') le = true;
            else if (d[start] == 'M' && d[start + 1] == 'M') le = false;
            else return null;
            // the magic number (42, or ORF/RW2 variants) is not checked on purpose

            long ifd0 = U32(d, start + 4, le);
            long exifIfd = FindExifIfd(d, start, ifd0, le);
            if (exifIfd <= 0) return null;
            return ScanExifIfd(d, start, exifIfd, le);
        }

        private static long FindExifIfd(byte[] d, int start, long ifd, bool le)
        {
            long pos = start + ifd;
            if (ifd <= 0 || pos + 2 > d.Length) return -1;
            int count = U16(d, (int)pos, le);
            for (int i = 0; i < count; i++)
            {
                long e = pos + 2 + i * 12;
                if (e + 12 > d.Length) break;
                if (U16(d, (int)e, le) == 0x8769) return U32(d, (int)e + 8, le);
            }
            return -1;
        }

        private static DateTime? ScanExifIfd(byte[] d, int start, long ifd, bool le)
        {
            long pos = start + ifd;
            if (pos + 2 > d.Length) return null;
            int count = U16(d, (int)pos, le);
            DateTime? original = null, digitized = null;
            for (int i = 0; i < count; i++)
            {
                long e = pos + 2 + i * 12;
                if (e + 12 > d.Length) break;
                int tag = U16(d, (int)e, le);
                if (tag != 0x9003 && tag != 0x9004) continue;
                long cnt = U32(d, (int)e + 4, le);
                if (cnt < 19) continue;
                long valPos = start + U32(d, (int)e + 8, le);
                if (valPos < 0 || valPos + 19 > d.Length) continue;
                var date = ParseDate(Ascii(d, (int)valPos, 19));
                if (tag == 0x9003) original = date; else digitized = date;
            }
            return original ?? digitized;
        }

        private static DateTime? ParseDate(string s)
        {
            DateTime dt;
            if (DateTime.TryParseExact(s, "yyyy:MM:dd HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out dt)
                && dt.Year > 1900)
                return dt;
            return null;
        }

        private static int U16(byte[] d, int p, bool le)
        {
            return le ? d[p] | (d[p + 1] << 8) : (d[p] << 8) | d[p + 1];
        }

        private static long U32(byte[] d, int p, bool le)
        {
            return le ? (uint)(d[p] | (d[p + 1] << 8) | (d[p + 2] << 16) | (d[p + 3] << 24))
                      : (uint)((d[p] << 24) | (d[p + 1] << 16) | (d[p + 2] << 8) | d[p + 3]);
        }

        private static string Ascii(byte[] d, int p, int n)
        {
            return Encoding.ASCII.GetString(d, p, Math.Min(n, d.Length - p));
        }

        private static byte[] ReadBytes(Stream s, int max)
        {
            var buf = new byte[(int)Math.Min(max, Math.Max(0, s.Length - s.Position))];
            int read = 0, r;
            while (read < buf.Length && (r = s.Read(buf, read, buf.Length - read)) > 0) read += r;
            if (read < buf.Length) Array.Resize(ref buf, read);
            return buf;
        }
    }
}
