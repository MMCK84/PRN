using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace BilderUmbenenner
{
    public class RenameItem
    {
        public string OriginalPath;
        public string CurrentPath;
        public DateTime Created;
        public DateTime Modified;
        public string NewName;
        public string Status = "";

        public DateTime EarliestDate
        {
            get { return Created < Modified ? Created : Modified; }
        }

        public static RenameItem FromFile(string path)
        {
            var info = new FileInfo(path);
            return new RenameItem
            {
                OriginalPath = info.FullName,
                CurrentPath = info.FullName,
                Created = info.CreationTime,
                Modified = info.LastWriteTime
            };
        }
    }

    public static class RenameEngine
    {
        public const string DateFormat = "yyyyMMdd_HHmm";

        public static readonly HashSet<string> ImageExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".jpg", ".jpeg", ".jpe", ".jfif", ".png", ".gif", ".bmp", ".tif", ".tiff",
            ".webp", ".heic", ".heif", ".avif", ".ico",
            ".raw", ".cr2", ".cr3", ".nef", ".nrw", ".arw", ".srf", ".sr2", ".orf",
            ".rw2", ".raf", ".dng", ".pef", ".srw", ".x3f", ".3fr", ".erf", ".kdc"
        };

        public static bool IsImage(string path)
        {
            return ImageExtensions.Contains(Path.GetExtension(path));
        }

        /// <summary>
        /// Computes the new file name for every item. Items sharing the same target
        /// minute in the same folder get the suffixes _1, _2, ... in chronological order.
        /// Files in the folder that are not part of the list are never overwritten.
        /// </summary>
        public static void ComputeNewNames(IList<RenameItem> items)
        {
            foreach (var group in items.GroupBy(i => Path.GetDirectoryName(i.CurrentPath), StringComparer.OrdinalIgnoreCase))
            {
                var batchPaths = new HashSet<string>(group.Select(i => i.CurrentPath), StringComparer.OrdinalIgnoreCase);
                var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                try
                {
                    foreach (var f in Directory.GetFiles(group.Key))
                        if (!batchPaths.Contains(f))
                            taken.Add(Path.GetFileName(f));
                }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }

                foreach (var item in group.OrderBy(i => i.EarliestDate).ThenBy(i => i.CurrentPath, StringComparer.OrdinalIgnoreCase))
                {
                    string baseName = item.EarliestDate.ToString(DateFormat);
                    string ext = Path.GetExtension(item.CurrentPath);
                    string candidate = baseName + ext;
                    int n = 1;
                    while (taken.Contains(candidate))
                        candidate = baseName + "_" + (n++) + ext;
                    taken.Add(candidate);
                    item.NewName = candidate;
                }
            }
        }

        /// <summary>
        /// Renames all items to their NewName. Uses temporary names first so that
        /// files inside the batch can swap names without conflicts.
        /// Returns the list of (from, to) renames that were performed, for undo.
        /// </summary>
        public static List<KeyValuePair<string, string>> Apply(IList<RenameItem> items)
        {
            var done = new List<KeyValuePair<string, string>>();
            var pending = new List<KeyValuePair<RenameItem, string>>();

            foreach (var item in items)
            {
                if (string.IsNullOrEmpty(item.NewName)) continue;
                string target = Path.Combine(Path.GetDirectoryName(item.CurrentPath), item.NewName);
                if (string.Equals(target, item.CurrentPath, StringComparison.Ordinal))
                {
                    item.Status = "Unverändert";
                    continue;
                }
                string temp = Path.Combine(Path.GetDirectoryName(item.CurrentPath),
                    "~umb_" + Guid.NewGuid().ToString("N") + Path.GetExtension(item.CurrentPath));
                try
                {
                    File.Move(item.CurrentPath, temp);
                    pending.Add(new KeyValuePair<RenameItem, string>(item, temp));
                }
                catch (Exception ex)
                {
                    item.Status = "Fehler: " + ex.Message;
                }
            }

            foreach (var p in pending)
            {
                var item = p.Key;
                string target = Path.Combine(Path.GetDirectoryName(item.CurrentPath), item.NewName);
                try
                {
                    if (File.Exists(target))
                        throw new IOException("Zieldatei existiert bereits");
                    File.Move(p.Value, target);
                    done.Add(new KeyValuePair<string, string>(item.CurrentPath, target));
                    item.CurrentPath = target;
                    item.Status = "OK";
                }
                catch (Exception ex)
                {
                    try { File.Move(p.Value, item.CurrentPath); } catch { }
                    item.Status = "Fehler: " + ex.Message;
                }
            }
            return done;
        }

        /// <summary>Reverts renames from Apply (in reverse order, via temporary names).</summary>
        public static int Undo(List<KeyValuePair<string, string>> done, out List<string> errors)
        {
            errors = new List<string>();
            var temps = new List<KeyValuePair<string, string>>();
            for (int i = done.Count - 1; i >= 0; i--)
            {
                string temp = Path.Combine(Path.GetDirectoryName(done[i].Value),
                    "~umb_" + Guid.NewGuid().ToString("N") + Path.GetExtension(done[i].Value));
                try
                {
                    File.Move(done[i].Value, temp);
                    temps.Add(new KeyValuePair<string, string>(temp, done[i].Key));
                }
                catch (Exception ex) { errors.Add(Path.GetFileName(done[i].Value) + ": " + ex.Message); }
            }
            int count = 0;
            foreach (var t in temps)
            {
                try
                {
                    if (File.Exists(t.Value)) throw new IOException("Datei existiert bereits");
                    File.Move(t.Key, t.Value);
                    count++;
                }
                catch (Exception ex) { errors.Add(Path.GetFileName(t.Value) + ": " + ex.Message + " (liegt als " + Path.GetFileName(t.Key) + " vor)"); }
            }
            return count;
        }
    }
}
