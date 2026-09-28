using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Principal;

namespace TidyUp
{
    /// <summary>One line in the cleanup list.</summary>
    abstract class CleanItem
    {
        public string Name { get; protected set; }
        public string Description { get; protected set; }
        public long Size { get; protected set; }
        public int Skipped { get; protected set; }

        public abstract void Scan();
        public abstract long Clean();
    }

    /// <summary>Clears the contents of some folders and/or deletes some files. Skips anything in use.</summary>
    class FolderItem : CleanItem
    {
        readonly Func<IEnumerable<string>> _folders;
        readonly Func<IEnumerable<string>> _files;
        readonly TimeSpan _minAge;

        public FolderItem(string name, string description,
            Func<IEnumerable<string>> folders,
            Func<IEnumerable<string>> files = null,
            TimeSpan? minAge = null)
        {
            Name = name;
            Description = description;
            _folders = folders;
            _files = files;
            _minAge = minAge ?? TimeSpan.Zero;
        }

        public override void Scan()
        {
            long total = 0;
            foreach (var d in Safe(_folders)) total += SizeOf(d);
            foreach (var f in Safe(_files)) total += FileSize(f);
            Size = total;
        }

        public override long Clean()
        {
            Skipped = 0;
            long freed = 0;
            foreach (var d in Safe(_folders)) freed += ClearDir(d);
            foreach (var f in Safe(_files)) freed += DeleteFile(f);
            return freed;
        }

        // ---- helpers ----

        static List<string> Safe(Func<IEnumerable<string>> source)
        {
            try { return source == null ? new List<string>() : source().ToList(); }
            catch { return new List<string>(); }
        }

        bool OldEnough(DateTime lastWriteUtc)
        {
            return _minAge == TimeSpan.Zero || DateTime.UtcNow - lastWriteUtc >= _minAge;
        }

        static bool IsLink(string path)
        {
            try { return (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0; }
            catch { return true; }
        }

        long FileSize(string path)
        {
            try
            {
                var fi = new FileInfo(path);
                if (fi.Exists && OldEnough(fi.LastWriteTimeUtc)) return fi.Length;
            }
            catch { }
            return 0;
        }

        long SizeOf(string dir)
        {
            long total = 0;
            try
            {
                foreach (var f in Directory.GetFiles(dir)) total += FileSize(f);
                foreach (var d in Directory.GetDirectories(dir))
                    if (!IsLink(d)) total += SizeOf(d);
            }
            catch { }
            return total;
        }

        long DeleteFile(string path)
        {
            try
            {
                var fi = new FileInfo(path);
                if (!fi.Exists || !OldEnough(fi.LastWriteTimeUtc)) return 0;
                long len = fi.Length;
                if (fi.IsReadOnly) fi.IsReadOnly = false;
                fi.Delete();
                return len;
            }
            catch
            {
                Skipped++;
                return 0;
            }
        }

        long ClearDir(string dir)
        {
            long freed = 0;
            try
            {
                foreach (var f in Directory.GetFiles(dir)) freed += DeleteFile(f);
                foreach (var d in Directory.GetDirectories(dir))
                {
                    if (IsLink(d)) continue;
                    freed += ClearDir(d);
                    try { Directory.Delete(d); } catch { } // only succeeds when empty
                }
            }
            catch { Skipped++; }
            return freed;
        }
    }

    /// <summary>Empties the Windows Recycle Bin.</summary>
    class RecycleBinItem : CleanItem
    {
        [StructLayout(LayoutKind.Sequential)]
        struct SHQUERYRBINFO
        {
            public int cbSize;
            public long i64Size;
            public long i64NumItems;
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        static extern int SHQueryRecycleBin(string rootPath, ref SHQUERYRBINFO info);

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        static extern int SHEmptyRecycleBin(IntPtr hwnd, string rootPath, uint flags);

        const uint NoConfirmation = 0x1, NoProgressUi = 0x2, NoSound = 0x4;

        public RecycleBinItem()
        {
            Name = "Recycle Bin";
            Description = "Files you have already deleted. Emptying it cannot be undone.";
        }

        static long Query()
        {
            var info = new SHQUERYRBINFO { cbSize = Marshal.SizeOf(typeof(SHQUERYRBINFO)) };
            return SHQueryRecycleBin(null, ref info) == 0 ? info.i64Size : 0;
        }

        public override void Scan()
        {
            try { Size = Query(); } catch { Size = 0; }
        }

        public override long Clean()
        {
            Skipped = 0;
            try
            {
                long before = Query();
                int hr = SHEmptyRecycleBin(IntPtr.Zero, null, NoConfirmation | NoProgressUi | NoSound);
                return hr == 0 ? before : 0;
            }
            catch { Skipped++; return 0; }
        }
    }

    static class Cleaner
    {
        public static bool IsAdmin
        {
            get
            {
                using (var id = WindowsIdentity.GetCurrent())
                    return new WindowsPrincipal(id).IsInRole(WindowsBuiltInRole.Administrator);
            }
        }

        public static string FormatSize(long bytes)
        {
            string[] units = { "B", "KB", "MB", "GB", "TB" };
            double v = bytes;
            int i = 0;
            while (v >= 1024 && i < units.Length - 1) { v /= 1024; i++; }
            return (i == 0 ? v.ToString("0") : v.ToString("0.#")) + " " + units[i];
        }

        public static List<CleanItem> CreateItems(bool includeSystem)
        {
            string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string win = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            string explorer = Path.Combine(local, @"Microsoft\Windows\Explorer");

            var items = new List<CleanItem>();

            items.Add(new FolderItem("Temporary files",
                "Leftovers from apps and installers. Anything used in the last hour is left alone.",
                () => new[] { Path.GetTempPath() },
                minAge: TimeSpan.FromHours(1)));

            if (includeSystem)
                items.Add(new FolderItem("Windows temporary files",
                    "Leftovers Windows no longer needs. Anything used in the last hour is left alone.",
                    () => new[] { Path.Combine(win, "Temp") },
                    minAge: TimeSpan.FromHours(1)));

            items.Add(new RecycleBinItem());

            items.Add(new FolderItem("Picture previews",
                "Small saved previews of your pictures. Windows recreates them when needed.",
                null,
                () => Directory.GetFiles(explorer, "thumbcache_*.db")));

            items.Add(new FolderItem("Error reports",
                "Reports saved when an app or Windows had a problem.",
                () =>
                {
                    var list = new List<string> { Path.Combine(local, "CrashDumps") };
                    if (includeSystem) list.Add(Path.Combine(win, "Minidump"));
                    return list;
                },
                () => includeSystem ? new[] { Path.Combine(win, "MEMORY.DMP") } : new string[0]));

            if (includeSystem)
                items.Add(new FolderItem("Old Windows update files",
                    "Update files that have already been installed.",
                    () => new[] { Path.Combine(win, @"SoftwareDistribution\Download") }));

            items.Add(new FolderItem("Chrome temporary files",
                "Saved pages and images that help sites load faster. Passwords and history are not touched.",
                () => ChromiumCaches(Path.Combine(local, @"Google\Chrome\User Data"))));

            items.Add(new FolderItem("Edge temporary files",
                "Saved pages and images that help sites load faster. Passwords and history are not touched.",
                () => ChromiumCaches(Path.Combine(local, @"Microsoft\Edge\User Data"))));

            items.Add(new FolderItem("Firefox temporary files",
                "Saved pages and images that help sites load faster. Passwords and history are not touched.",
                () => Directory.GetDirectories(Path.Combine(local, @"Mozilla\Firefox\Profiles"))
                               .Select(p => Path.Combine(p, "cache2"))
                               .Where(Directory.Exists)));

            return items;
        }

        static IEnumerable<string> ChromiumCaches(string userDataDir)
        {
            if (!Directory.Exists(userDataDir)) yield break;

            foreach (var profile in Directory.GetDirectories(userDataDir))
            {
                string name = Path.GetFileName(profile);
                if (name != "Default" && !name.StartsWith("Profile ", StringComparison.OrdinalIgnoreCase))
                    continue;

                foreach (var sub in new[] { "Cache", "Code Cache", "GPUCache" })
                {
                    string path = Path.Combine(profile, sub);
                    if (Directory.Exists(path)) yield return path;
                }
            }
        }
    }
}
