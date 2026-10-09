using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;

namespace MangaAuthorSorter
{
    /// <summary>
    /// Read-through cache for filename parsing. The database stores parsing
    /// facts; scan plans remain transient and are derived afterwards.
    /// </summary>
    internal sealed class ParsedMetadataCacheService : IParsedMetadataCache, ICacheLayerDiagnostics
    {
        private const string ParserVersion = FileNameStructure.RuleVersion;
        private const char Separator = '\u001f';
        private readonly FileIndexCacheDatabase _database;
        private string _tagCleaningVersion;
        private readonly ConcurrentDictionary<string, ParsedMetadataCacheEntry> _entries;
        private readonly ConcurrentDictionary<string, ParsedMetadataCacheEntry> _pending =
            new ConcurrentDictionary<string, ParsedMetadataCacheEntry>(StringComparer.OrdinalIgnoreCase);
        private long _hits;
        private long _misses;
        public long HitCount { get { return Interlocked.Read(ref _hits); } }
        public long MissCount { get { return Interlocked.Read(ref _misses); } }

        public ParsedMetadataCacheService(FileIndexCacheDatabase database, string tagCleaningVersion)
        {
            if (database == null) throw new ArgumentNullException("database");
            _database = database;
            _tagCleaningVersion = tagCleaningVersion ?? "";
            Dictionary<string, ParsedMetadataCacheEntry> loaded;
            try { loaded = database.LoadParsedMetadata(); }
            catch { loaded = new Dictionary<string, ParsedMetadataCacheEntry>(StringComparer.OrdinalIgnoreCase); }
            _entries = new ConcurrentDictionary<string, ParsedMetadataCacheEntry>(loaded, StringComparer.OrdinalIgnoreCase);
        }

        public void UpdateTagCleaningVersion(string version)
        {
            // Keep old rows; their dependency fingerprint makes them miss lazily.
            _tagCleaningVersion = version ?? "";
        }

        public bool TryGetAuthorCandidates(string fullPath, string fileName, out List<string> candidates)
        {
            candidates = null;
            ParsedMetadataCacheEntry entry;
            if (String.IsNullOrWhiteSpace(fullPath) || !_entries.TryGetValue(fullPath, out entry))
            { Interlocked.Increment(ref _misses); return false; }
            string fingerprint = Fingerprint(fileName, ParserVersion, _tagCleaningVersion);
            if (!String.Equals(entry.ParserVersion, ParserVersion, StringComparison.Ordinal) ||
                !String.Equals(entry.TagCleaningVersion, _tagCleaningVersion, StringComparison.Ordinal) ||
                !String.Equals(entry.InputFingerprint, fingerprint, StringComparison.Ordinal))
            { Interlocked.Increment(ref _misses); return false; }
            candidates = String.IsNullOrEmpty(entry.AuthorCandidate)
                ? new List<string>()
                : entry.AuthorCandidate.Split(new[] { Separator }, StringSplitOptions.RemoveEmptyEntries).ToList();
            Interlocked.Increment(ref _hits);
            return true;
        }

        public void StoreAuthorCandidates(string fullPath, string fileName, IEnumerable<string> candidates)
        {
            if (String.IsNullOrWhiteSpace(fullPath)) return;
            ParsedMetadataCacheEntry entry = new ParsedMetadataCacheEntry
            {
                FullPath = fullPath,
                AuthorCandidate = String.Join(Separator.ToString(), candidates ?? Enumerable.Empty<string>()),
                NormalizedFileName = fileName ?? "",
                ParserVersion = ParserVersion,
                TagCleaningVersion = _tagCleaningVersion,
                InputFingerprint = Fingerprint(fileName, ParserVersion, _tagCleaningVersion)
            };
            _entries[fullPath] = entry;
            _pending[fullPath] = entry;
        }

        public void Flush()
        {
            if (_pending.IsEmpty) return;
            List<ParsedMetadataCacheEntry> batch = new List<ParsedMetadataCacheEntry>();
            foreach (KeyValuePair<string, ParsedMetadataCacheEntry> pair in _pending)
            {
                ParsedMetadataCacheEntry removed;
                if (_pending.TryRemove(pair.Key, out removed)) batch.Add(removed);
            }
            try { _database.UpsertParsedMetadata(batch); }
            catch
            {
                foreach (ParsedMetadataCacheEntry entry in batch) _pending[entry.FullPath] = entry;
            }
        }

        public void Relocate(string oldPath, string newPath)
        {
            if (String.IsNullOrWhiteSpace(oldPath) || String.IsNullOrWhiteSpace(newPath) ||
                String.Equals(oldPath, newPath, StringComparison.OrdinalIgnoreCase))
                return;
            ParsedMetadataCacheEntry entry;
            if (_entries.TryRemove(oldPath, out entry) && entry != null)
            {
                entry.FullPath = newPath;
                _entries[newPath] = entry;
            }
            ParsedMetadataCacheEntry pending;
            if (_pending.TryRemove(oldPath, out pending) && pending != null)
            {
                pending.FullPath = newPath;
                _pending[newPath] = pending;
            }
        }

        public void Invalidate(string fullPath)
        {
            if (String.IsNullOrWhiteSpace(fullPath)) return;
            ParsedMetadataCacheEntry ignored;
            _entries.TryRemove(fullPath, out ignored);
            _pending.TryRemove(fullPath, out ignored);
            try { _database.DeleteParsedMetadata(fullPath); } catch { }
        }

        private static string Fingerprint(string fileName, string parserVersion, string tagVersion)
        {
            byte[] bytes = Encoding.UTF8.GetBytes((fileName ?? "") + "\n" + parserVersion + "\n" + tagVersion);
            UInt64 hash = 14695981039346656037UL;
            foreach (byte value in bytes) { hash ^= value; hash *= 1099511628211UL; }
            return bytes.Length.ToString() + ":" + hash.ToString("X16");
        }
    }
}
