using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace MangaAuthorSorter
{
    internal enum ScanWarmupState
    {
        Disabled,
        Idle,
        Preparing,
        PartiallyReady,
        Ready,
        Cancelled,
        Invalidated,
        Failed
    }

    internal sealed class ScanWarmupRequest
    {
        public string Source = "";
        public string Target = "";
        public bool Recursive;
        public List<string> Extensions = new List<string>();
        public HashSet<string> BlockedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        public string GroupTemplate = "";
        public List<string> RecognizedGroupTemplates = new List<string>();
        public string AuthorFolderTemplate = "";
        public List<string> RecognizedAuthorFolderTemplates = new List<string>();
        public string SourceVersion = "";
        public string RecognitionVersion = "";
        public string PersistentRecognitionVersion = "";
        public Func<string, string> IdentityDependency;
        public long SourceSnapshotRevision;
        public string TargetVersion = "";
        public ScanModeKind Mode;
        public int RequestedLimit;
        public int MaxAuthors;

        public string SourceKey
        {
            get
            {
                return NormalizePath(Source) + "|" + Recursive + "|" +
                    String.Join(";", FileTypeRules.NormalizeExtensions(Extensions).OrderBy(x => x)) +
                    "|" + (SourceVersion ?? "");
            }
        }

        public string TargetKey
        {
            get
            {
                return NormalizePath(Target) + "|" + (GroupTemplate ?? "") + "|" +
                    String.Join(";", RecognizedGroupTemplates ?? new List<string>()) + "|" +
                    (AuthorFolderTemplate ?? "") + "|" +
                    String.Join(";", RecognizedAuthorFolderTemplates ?? new List<string>()) + "|" +
                    (TargetVersion ?? "");
            }
        }

        public string PlanKey
        {
            get
            {
                // This key describes rules that can change a per-file result.
                // Recursive scope, extensions, result limits, ScanSession
                // membership and source/target directory revisions only select
                // or reconcile files; they must never invalidate unchanged
                // files that already have cached parsing/recognition/planning.
                return "PerFilePlan=2|Source=" + NormalizePath(Source) +
                    "|Rules=" + (RecognitionVersion ?? "") +
                    "|Target=" + NormalizePath(Target) +
                    "|Group=" + (GroupTemplate ?? "") +
                    "|Groups=" + String.Join(";", RecognizedGroupTemplates ?? new List<string>()) +
                    "|AuthorFolder=" + (AuthorFolderTemplate ?? "") +
                    "|AuthorFolders=" + String.Join(";", RecognizedAuthorFolderTemplates ?? new List<string>()) +
                    "|MaxAuthors=" + MaxAuthors;
            }
        }

        public string PersistentPlanKey
        {
            get { return String.IsNullOrEmpty(PersistentRecognitionVersion) ? PlanKey :
                PlanKey.Replace("|Rules=" + RecognitionVersion + "|Target=", "|Rules=" + PersistentRecognitionVersion + "|Target="); }
        }

        private static string NormalizePath(string path)
        {
            try { return Path.GetFullPath(path ?? "").TrimEnd('\\', '/').ToUpperInvariant(); }
            catch { return (path ?? "").Trim().ToUpperInvariant(); }
        }
    }

    internal sealed class ScanWarmupMetrics
    {
        public long FileDiscoveryMs;
        public long ProviderQueryMs, IndexReconcileMs;
        public long TargetIndexMs;
        public string Provider = "";
        public bool WarmupHit;
        public bool WarmupPartialHit;
        public bool SnapshotHit;
        public long SnapshotPrepareMs;
        public bool ReadyBeforeRequest;
        public long WarmupWaitMs;
        public long ParsedCacheHits, ParsedCacheMisses;
        public long RecognitionCacheHits, RecognitionCacheMisses;
        public long DestinationCacheHits, DestinationCacheMisses;
        public int IndexCacheHits, IndexAdded, IndexRemoved, IndexModified, IndexMoved, IndexRenamed;
        public int PlanCacheHits, RecalculatedFiles;
    }

    /// <summary>
    /// Owns one low-priority, replaceable warmup. Startup warmup is deliberately
    /// lightweight: it only publishes cache readiness. Filesystem discovery and
    /// target reconciliation belong to the shared scan worker. Completed plans
    /// can be reused here; complete source snapshots belong only to the index.
    /// </summary>
    internal sealed class ScanWarmupService : IDisposable
    {
        private readonly object _gate = new object();
        private readonly IFileSystemIndexProvider _fileSystemProvider;
        private string _sourceKey = "";
        private string _targetKey = "";
        private string _planKey = "";
        private List<PlanItem> _preparedPlan;
        private ScanWarmupMetrics _metrics = new ScanWarmupMetrics();
        private readonly Dictionary<string, WarmupCacheEntry> _cache = new Dictionary<string, WarmupCacheEntry>(StringComparer.Ordinal);
        private const int CacheCapacity = 4;
        private static readonly TimeSpan CacheLifetime = TimeSpan.FromMinutes(10);

        private sealed class WarmupCacheEntry
        {
            public DateTime LastUsed;
            public string SourceKey = "";
            public List<PlanItem> Plan;
            public ScanWarmupMetrics Metrics;
        }

        public ScanWarmupService(IFileSystemIndexProvider fileSystemProvider)
        {
            if (fileSystemProvider == null) throw new ArgumentNullException("fileSystemProvider");
            _fileSystemProvider = fileSystemProvider;
            State = ScanWarmupState.Idle;
        }

        public ScanWarmupState State { get; private set; }

        public void Start(ScanWarmupRequest request)
        {
            if (request == null) return;
            lock (_gate)
            {
                if (State == ScanWarmupState.Ready && _sourceKey == request.SourceKey &&
                    _targetKey == request.TargetKey && _planKey == request.PlanKey) return;
                if (_planKey != request.PlanKey)
                {
                    WarmupCacheEntry cached;
                    _preparedPlan = _cache.TryGetValue(request.PlanKey, out cached) &&
                        DateTime.Now - cached.LastUsed <= CacheLifetime ? ClonePlan(cached.Plan) : null;
                }
                _sourceKey = request.SourceKey;
                _targetKey = request.TargetKey;
                _planKey = request.PlanKey;
                State = ScanWarmupState.Ready;
            }
            // Readiness bookkeeping performs no disk work and needs no task.
            // Author indices are prepared by the existing background initializer;
            // discovery and validation are owned by the shared scan worker.
            ScanPerformanceDiagnostics.PublishWarmupStatus(new ScanWarmupStatusEntry {
                State = ScanWarmupState.Ready, Stage = "Performance.Node.Ready"
            });
        }
        public SearchResult GetOrRunSource(
            ScanWarmupRequest request,
            Action<ScanProgressInfo> progress,
            Func<bool> cancelRequested,
            out ScanWarmupMetrics metrics)
        {
            // PersistentSourceIndexProvider is the sole owner of discovery
            // snapshots and watcher health. A second in-memory copy here could
            // bypass a failed watcher or reuse a result after its scope expired.
            Stopwatch timer = Stopwatch.StartNew();
            SearchResult result = _fileSystemProvider.SearchFiles(request.Source,
                request.Recursive, 0, request.BlockedPaths, request.Extensions,
                progress, cancelRequested);
            timer.Stop();
            metrics = new ScanWarmupMetrics {
                Provider = result.Backend ?? "",
                FileDiscoveryMs = timer.ElapsedMilliseconds,
                ProviderQueryMs = result.ProviderQueryMs,
                IndexReconcileMs = result.IndexReconcileMs,
                WarmupHit = result.SourceSnapshotHit,
                ReadyBeforeRequest = result.SourceSnapshotHit
            };
            return result;
        }
        public void Cancel()
        {
            lock (_gate) State = ScanWarmupState.Cancelled;
            ScanPerformanceDiagnostics.PublishWarmupStatus(new ScanWarmupStatusEntry
            {
                State = ScanWarmupState.Cancelled
            });
        }

        /// <summary>
        /// Applies a program-owned move to every in-memory projection. This is
        /// not an invalidation: the persistent index and current ScanSession
        /// have already been updated by the mutation coordinator.
        /// </summary>
        public void ApplySuccessfulMove(string sourcePath, long fileId)
        {
            lock (_gate)
            {
                RemoveMovedPlan(ref _preparedPlan, sourcePath, fileId);
                foreach (WarmupCacheEntry cached in _cache.Values)
                {
                    RemoveMovedPlan(ref cached.Plan, sourcePath, fileId);
                }
            }
        }

        public void ApplySuccessfulRename(
            string oldPath,
            string newPath,
            long fileId)
        {
            lock (_gate)
            {
                RemoveMovedPlan(ref _preparedPlan, oldPath, fileId);
                foreach (WarmupCacheEntry cached in _cache.Values)
                {
                    RemoveMovedPlan(ref cached.Plan, oldPath, fileId);
                }
            }
        }

        public bool TryGetPreparedPlan(
            ScanWarmupRequest request,
            out List<PlanItem> plan,
            out long prepareMilliseconds)
        {
            Stopwatch timer = Stopwatch.StartNew();
            lock (_gate)
            {
                if (request != null &&
                    State == ScanWarmupState.Ready &&
                    String.Equals(_planKey, request.PlanKey, StringComparison.Ordinal) &&
                    _preparedPlan != null)
                {
                    plan = ClonePlan(_preparedPlan);
                    prepareMilliseconds = timer.ElapsedMilliseconds;
                    _metrics.SnapshotHit = true;
                    return true;
                }

                WarmupCacheEntry cached;
                if (request != null &&
                    _cache.TryGetValue(request.PlanKey, out cached) &&
                    cached.Plan != null &&
                    DateTime.Now - cached.LastUsed <= CacheLifetime)
                {
                    cached.LastUsed = DateTime.Now;
                    _sourceKey = request.SourceKey;
                    _targetKey = request.TargetKey;
                    _planKey = request.PlanKey;
                    _preparedPlan = ClonePlan(cached.Plan);
                    _metrics = CopyMetrics(cached.Metrics);
                    _metrics.SnapshotHit = true;
                    State = ScanWarmupState.Ready;
                    plan = ClonePlan(_preparedPlan);
                    prepareMilliseconds = timer.ElapsedMilliseconds;
                    return true;
                }
            }
            plan = null;
            prepareMilliseconds = 0;
            return false;
        }

        public void StorePreparedPlan(
            ScanWarmupRequest request,
            IEnumerable<PlanItem> plan,
            long prepareMilliseconds)
        {
            if (request == null || plan == null) return;
            List<PlanItem> stored = ClonePlan(plan);
            lock (_gate)
            {
                bool samePlan = String.Equals(_planKey, request.PlanKey, StringComparison.Ordinal);
                _sourceKey = request.SourceKey;
                _targetKey = request.TargetKey;
                _planKey = request.PlanKey;
                _preparedPlan = samePlan ? MergePlanEntries(_preparedPlan, stored) : stored;
                _metrics.SnapshotPrepareMs = 0;
                _metrics.SnapshotHit = false;
                State = ScanWarmupState.Ready;
                WarmupCacheEntry cached;
                if (!_cache.TryGetValue(request.PlanKey, out cached))
                {
                    cached = new WarmupCacheEntry
                    {
                        LastUsed = DateTime.Now,
                        SourceKey = request.SourceKey,
                        Metrics = CopyMetrics(_metrics)
                    };
                    _cache[request.PlanKey] = cached;
                }
                cached.LastUsed = DateTime.Now;
                cached.SourceKey = request.SourceKey;
                cached.Plan = MergePlanEntries(cached.Plan, stored);
                cached.Metrics = CopyMetrics(_metrics);
                TrimCacheLocked();
            }
        }

        private void TrimCacheLocked()
        {
            while (_cache.Count > CacheCapacity)
            {
                string oldestKey = null; DateTime oldest = DateTime.MaxValue;
                foreach (KeyValuePair<string, WarmupCacheEntry> pair in _cache)
                    if (pair.Value.LastUsed < oldest) { oldest = pair.Value.LastUsed; oldestKey = pair.Key; }
                if (oldestKey == null) break; _cache.Remove(oldestKey);
            }
        }

        private static void RemoveMovedPlan(
            ref List<PlanItem> plan,
            string sourcePath,
            long fileId)
        {
            if (plan == null) return;
            plan.RemoveAll(delegate(PlanItem item)
            {
                if (item == null) return false;
                if (fileId > 0 && item.FileId == fileId) return true;
                return String.Equals(
                    NormalizeFilePath(item.SourcePath),
                    NormalizeFilePath(sourcePath),
                    StringComparison.Ordinal);
            });
        }

        private static string NormalizeFilePath(string path)
        {
            try { return Path.GetFullPath(path ?? "").TrimEnd('\\', '/').ToUpperInvariant(); }
            catch { return (path ?? "").Trim().ToUpperInvariant(); }
        }

        private static ScanWarmupMetrics CopyMetrics(ScanWarmupMetrics source)
        {
            ScanWarmupMetrics copy = new ScanWarmupMetrics();
            copy.FileDiscoveryMs = source.FileDiscoveryMs;
            copy.ProviderQueryMs = source.ProviderQueryMs;
            copy.IndexReconcileMs = source.IndexReconcileMs;
            copy.TargetIndexMs = source.TargetIndexMs;
            copy.Provider = source.Provider;
            copy.WarmupHit = source.WarmupHit;
            copy.WarmupPartialHit = source.WarmupPartialHit;
            copy.SnapshotHit = source.SnapshotHit;
            copy.SnapshotPrepareMs = source.SnapshotPrepareMs;
            copy.ReadyBeforeRequest = source.ReadyBeforeRequest;
            copy.WarmupWaitMs = source.WarmupWaitMs;
            copy.ParsedCacheHits = source.ParsedCacheHits;
            copy.ParsedCacheMisses = source.ParsedCacheMisses;
            copy.RecognitionCacheHits = source.RecognitionCacheHits;
            copy.RecognitionCacheMisses = source.RecognitionCacheMisses;
            copy.DestinationCacheHits = source.DestinationCacheHits;
            copy.DestinationCacheMisses = source.DestinationCacheMisses;
            copy.IndexCacheHits = source.IndexCacheHits;
            copy.IndexAdded = source.IndexAdded;
            copy.IndexRemoved = source.IndexRemoved;
            copy.IndexModified = source.IndexModified;
            copy.IndexMoved = source.IndexMoved;
            copy.IndexRenamed = source.IndexRenamed;
            copy.PlanCacheHits = source.PlanCacheHits;
            copy.RecalculatedFiles = source.RecalculatedFiles;
            return copy;
        }

        private static List<PlanItem> ClonePlan(IEnumerable<PlanItem> source)
        {
            List<PlanItem> result = new List<PlanItem>();
            if (source == null) return result;
            foreach (PlanItem item in source)
            {
                if (item == null) continue;
                PlanItem copy = new PlanItem();
                copy.FileId = item.FileId;
                copy.FileName = item.FileName;
                copy.SourcePath = item.SourcePath;
                copy.Author = item.Author;
                copy.MatchedAs = item.MatchedAs;
                copy.MatchWhy = item.MatchWhy;
                copy.TargetDir = item.TargetDir;
                copy.TargetPath = item.TargetPath;
                copy.Status = item.Status;
                copy.CanMove = item.CanMove;
                copy.ManualTargetDir = item.ManualTargetDir;
                copy.ManualTargetName = item.ManualTargetName;
                copy.ManualTargetAuthor = item.ManualTargetAuthor;
                copy.CandidatePaths = new List<string>(item.CandidatePaths);
                copy.CandidateNames = new List<string>(item.CandidateNames);
                copy.CandidateIsPlanned = new List<bool>(item.CandidateIsPlanned);
                copy.LastWriteTime = item.LastWriteTime;
                copy.FileSize = item.FileSize;
                copy.RecognitionScore = item.RecognitionScore;
                copy.RecognitionRunnerUpScore = item.RecognitionRunnerUpScore;
                copy.StatusCode = item.StatusCode;
                copy.EvidenceKind = item.EvidenceKind;
                copy.StatusArgument = item.StatusArgument;
                copy.PlanConflictKind = item.PlanConflictKind;
                copy.ConflictTargetPath = item.ConflictTargetPath;
                copy.ConflictSourcePaths = new List<string>(item.ConflictSourcePaths);
                copy.CanMoveBeforePlanConflict = item.CanMoveBeforePlanConflict;
                copy.StatusBeforePlanConflict = item.StatusBeforePlanConflict;
                copy.StatusCodeBeforePlanConflict = item.StatusCodeBeforePlanConflict;
                copy.IsExcludedPreview = item.IsExcludedPreview;
                copy.ExclusionRuleName = item.ExclusionRuleName;
                copy.ExclusionScope = item.ExclusionScope;
                copy.ExclusionIsDirectory = item.ExclusionIsDirectory;
                result.Add(copy);
            }
            return result;
        }

        private static List<PlanItem> MergePlanEntries(
            IEnumerable<PlanItem> existing,
            IEnumerable<PlanItem> updates)
        {
            Dictionary<string, PlanItem> merged =
                new Dictionary<string, PlanItem>(StringComparer.OrdinalIgnoreCase);
            foreach (PlanItem item in ClonePlan(existing))
                merged[PlanIdentity(item)] = item;
            foreach (PlanItem item in ClonePlan(updates))
                merged[PlanIdentity(item)] = item;
            return merged.Values.ToList();
        }

        private static string PlanIdentity(PlanItem item)
        {
            if (item != null && item.FileId > 0)
                return "ID:" + item.FileId.ToString();
            return "PATH:" + (item != null ? item.SourcePath ?? "" : "");
        }


        public void Dispose() { Cancel(); }
    }
}
