using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;

namespace MangaAuthorSorter
{
    internal sealed class DestinationIndexCacheService : IDestinationIndexCache, ICacheLayerDiagnostics
    {
        private readonly FileIndexCacheDatabase _database;
        private long _hits;
        private long _misses;
        public long HitCount { get { return Interlocked.Read(ref _hits); } }
        public long MissCount { get { return Interlocked.Read(ref _misses); } }

        public DestinationIndexCacheService(FileIndexCacheDatabase database)
        {
            if (database == null) throw new ArgumentNullException("database");
            _database = database;
        }

        public bool TryLoad(string root, string cacheKey, out List<DestinationFolderIndexEntry> folders)
        {
            folders = new List<DestinationFolderIndexEntry>();
            if (!Directory.Exists(root)) { Interlocked.Increment(ref _misses); return false; }
            try
            {
                folders = _database.LoadDestinationFolders(root, GetStamp(root) + "|" + (cacheKey ?? ""));
                bool hit = folders.Count > 0;
                if (hit) Interlocked.Increment(ref _hits);
                else Interlocked.Increment(ref _misses);
                return hit;
            }
            catch { Interlocked.Increment(ref _misses); return false; }
        }

        public void Store(
            string root,
            string cacheKey,
            IEnumerable<KeyValuePair<int, string>> groups,
            IEnumerable<AuthorFolder> authors)
        {
            if (!Directory.Exists(root)) return;
            try { _database.ReplaceDestinationFolders(root, GetStamp(root) + "|" + (cacheKey ?? ""), groups, authors); }
            catch { }
        }

        public void Invalidate(string root)
        {
            try { _database.InvalidateDestination(root); }
            catch { }
        }

        public string GetCurrentVersion(string root)
        {
            if (!Directory.Exists(root)) return "Missing";
            try { return GetStamp(root); }
            catch { return "Unavailable"; }
        }

        private static string GetStamp(string rootPath)
        {
            UInt64 hash = 14695981039346656037UL;
            Action<string> add = delegate(string value)
            {
                foreach (char c in value ?? "") { hash ^= c; hash *= 1099511628211UL; }
            };
            DirectoryInfo root = new DirectoryInfo(rootPath);
            add(root.FullName.ToUpperInvariant());
            add(root.LastWriteTimeUtc.Ticks.ToString());
            foreach (DirectoryInfo group in root.GetDirectories().OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase))
            {
                add(group.Name.ToUpperInvariant());
                add(group.LastWriteTimeUtc.Ticks.ToString());
                DirectoryInfo[] authors;
                try { authors = group.GetDirectories(); } catch { authors = new DirectoryInfo[0]; }
                add(authors.Length.ToString());
                foreach (DirectoryInfo author in authors.OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase))
                {
                    add(author.Name.ToUpperInvariant());
                    add(author.LastWriteTimeUtc.Ticks.ToString());
                }
            }
            return hash.ToString("X16");
        }
    }
}
