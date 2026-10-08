using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;

namespace MangaAuthorSorter
{
    /// <summary>
    /// Stores durable recognition facts, not migration destinations or UI rows.
    /// Destination planning is deliberately rerun from these facts.
    /// </summary>
    internal sealed class RecognitionFactCacheService : IRecognitionFactCache, ICacheLayerDiagnostics
    {
        private readonly FileIndexCacheDatabase _database;
        private string _aliasVersion;
        private string _entityVersion;
        private string _authorIndexVersion;
        private Func<string, string> _identityDependency;
        private string _ruleVersion;
        private readonly ConcurrentDictionary<string, RecognitionCacheEntry> _entries;
        private readonly ConcurrentDictionary<string, RecognitionCacheEntry> _pending =
            new ConcurrentDictionary<string, RecognitionCacheEntry>(StringComparer.OrdinalIgnoreCase);
        private long _hits;
        private long _misses;
        public long HitCount { get { return Interlocked.Read(ref _hits); } }
        public long MissCount { get { return Interlocked.Read(ref _misses); } }

        public RecognitionFactCacheService(
            FileIndexCacheDatabase database,
            string aliasVersion,
            string entityVersion,
            string authorIndexVersion,
            string ruleVersion)
        {
            if (database == null) throw new ArgumentNullException("database");
            _database = database;
            _aliasVersion = aliasVersion ?? "";
            _entityVersion = entityVersion ?? "";
            _authorIndexVersion = authorIndexVersion ?? "";
            _ruleVersion = ruleVersion ?? "";
            Dictionary<string, RecognitionCacheEntry> loaded;
            try { loaded = database.LoadRecognitionCache(); }
            catch { loaded = new Dictionary<string, RecognitionCacheEntry>(StringComparer.OrdinalIgnoreCase); }
            _entries = new ConcurrentDictionary<string, RecognitionCacheEntry>(loaded, StringComparer.OrdinalIgnoreCase);
        }

        public bool TryGet(string fullPath, string fileName, out RecognitionCacheEntry entry)
        {
            entry = null;
            RecognitionCacheEntry cached;
            if (String.IsNullOrWhiteSpace(fullPath) || !_entries.TryGetValue(fullPath, out cached))
            { Interlocked.Increment(ref _misses); return false; }
            if (!String.Equals(cached.InputFingerprint, Fingerprint(fileName), StringComparison.Ordinal) ||
                (_identityDependency == null && !String.Equals(cached.AliasVersion, _aliasVersion, StringComparison.Ordinal)) ||
                !String.Equals(cached.EntityVersion, _identityDependency == null ? _entityVersion : "dependency:" + _identityDependency(fileName), StringComparison.Ordinal) ||
                !String.Equals(cached.LocalDatabaseVersion, _authorIndexVersion, StringComparison.Ordinal) ||
                !String.Equals(cached.RecognitionRuleVersion, _ruleVersion, StringComparison.Ordinal))
            { Interlocked.Increment(ref _misses); return false; }
            PlanStatusCode cachedStatus;
            if (!Enum.TryParse<PlanStatusCode>(cached.RecognitionStatus, out cachedStatus) ||
                cachedStatus == PlanStatusCode.ManualAuthor ||
                cachedStatus == PlanStatusCode.ManualFolder ||
                cachedStatus == PlanStatusCode.Unrecognized ||
                cachedStatus == PlanStatusCode.Ambiguous ||
                cachedStatus == PlanStatusCode.CandidateConfirmation ||
                String.IsNullOrWhiteSpace(cached.MatchedAuthor))
            { Interlocked.Increment(ref _misses); return false; }
            entry = cached;
            Interlocked.Increment(ref _hits);
            return true;
        }

        public void UpdateRuleVersion(string ruleVersion)
        {
            _ruleVersion = ruleVersion ?? "";
        }

        public void UpdateIdentityVersions(string aliasVersion, string entityVersion)
        {
            _aliasVersion = aliasVersion ?? "";
            _entityVersion = entityVersion ?? "";
        }
        public void UpdateIdentityDependencies(Func<string, string> dependency, string publicVersion)
        {
            _identityDependency = dependency; _authorIndexVersion = publicVersion ?? "";
        }

        public void Store(PlanItem item)
        {
            if (item == null || String.IsNullOrWhiteSpace(item.SourcePath)) return;
            bool canonicalNewIdentity =
                item.StatusCode == PlanStatusCode.NewAuthor ||
                item.StatusCode == PlanStatusCode.NewAuthorReuse;
            string identity = canonicalNewIdentity && !String.IsNullOrWhiteSpace(item.MatchedAs)
                ? item.MatchedAs
                : item.Author;
            RecognitionCacheEntry entry = new RecognitionCacheEntry
            {
                FullPath = item.SourcePath,
                MatchedAuthor = identity,
                MatchedGroup = "",
                Confidence = item.RecognitionScore,
                RunnerUpConfidence = item.RecognitionRunnerUpScore,
                RecognitionStatus = item.StatusCode.ToString(),
                RecognitionSource = canonicalNewIdentity ? "CanonicalIdentity" : "ParsedIdentity",
                RecognitionReason = item.MatchWhy ?? "",
                EvidenceKind = item.EvidenceKind.ToString(),
                AliasVersion = _aliasVersion,
                EntityVersion = _identityDependency == null ? _entityVersion : "dependency:" + _identityDependency(item.FileName),
                RecognitionRuleVersion = _ruleVersion,
                LocalDatabaseVersion = _authorIndexVersion,
                InputFingerprint = Fingerprint(item.FileName),
                UpdatedUtc = DateTime.UtcNow.ToString("o")
            };
            _entries[item.SourcePath] = entry;
            _pending[item.SourcePath] = entry;
        }

        public void Flush()
        {
            if (_pending.IsEmpty) return;
            List<RecognitionCacheEntry> batch = new List<RecognitionCacheEntry>();
            foreach (KeyValuePair<string, RecognitionCacheEntry> pair in _pending)
            {
                RecognitionCacheEntry removed;
                if (_pending.TryRemove(pair.Key, out removed)) batch.Add(removed);
            }
            try { _database.UpsertRecognitionCache(batch); }
            catch
            {
                foreach (RecognitionCacheEntry entry in batch) _pending[entry.FullPath] = entry;
            }
        }

        public void Relocate(string oldPath, string newPath)
        {
            if (String.IsNullOrWhiteSpace(oldPath) || String.IsNullOrWhiteSpace(newPath) ||
                String.Equals(oldPath, newPath, StringComparison.OrdinalIgnoreCase))
                return;
            RecognitionCacheEntry entry;
            if (_entries.TryRemove(oldPath, out entry) && entry != null)
            {
                entry.FullPath = newPath;
                _entries[newPath] = entry;
            }
            RecognitionCacheEntry pending;
            if (_pending.TryRemove(oldPath, out pending) && pending != null)
            {
                pending.FullPath = newPath;
                _pending[newPath] = pending;
            }
        }

        public void Invalidate(string fullPath)
        {
            if (String.IsNullOrWhiteSpace(fullPath)) return;
            RecognitionCacheEntry ignored;
            _entries.TryRemove(fullPath, out ignored);
            _pending.TryRemove(fullPath, out ignored);
            try
            {
                _database.DeleteRecognitionFact(fullPath);
                _database.DeleteMigrationPlans(fullPath);
            }
            catch { }
        }

        private string Fingerprint(string fileName)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(
                (fileName ?? "") + "\n" + _aliasVersion + "\n" + _entityVersion + "\n" +
                _authorIndexVersion + "\n" + _ruleVersion);
            UInt64 hash = 14695981039346656037UL;
            foreach (byte value in bytes) { hash ^= value; hash *= 1099511628211UL; }
            return bytes.Length.ToString() + ":" + hash.ToString("X16");
        }
    }
}
