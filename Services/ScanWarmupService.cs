using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

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
    /// target reconciliation belong to an explicit user scan. A completed scan
    /// may still publish its immutable source/plan here for unchanged follow-ups.
    /// </summary>
    internal sealed class ScanWarmupService : IDisposable
    {
        private readonly object _gate = new object();
        private readonly IFileSystemIndexProvider _fileSystemProvider;
        private CancellationTokenSource _cancellation;
        private Task _task;
        private long _generation;
        private string _sourceKey = "";
        private string _targetKey = "";
        private string _planKey = "";
        private SearchResult _sourceCache;
        private List<PlanItem> _preparedPlan;
        private Exception _failure;
        private ScanWarmupMetrics _metrics = new ScanWarmupMetrics();
        private readonly Dictionary<string, WarmupCacheEntry> _cache = new Dictionary<string, WarmupCacheEntry>(StringComparer.Ordinal);
        private const int CacheCapacity = 4;
        private static readonly TimeSpan CacheLifetime = TimeSpan.FromMinutes(10);

        private sealed class WarmupCacheEntry
        {
            public DateTime LastUsed;
            public string SourceKey = "";
            public SearchResult Source;
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
            if (request == null || !Directory.Exists(request.Source) || !Directory.Exists(request.Target))
                return;

            string sourceKey = request.SourceKey;
            string targetKey = request.TargetKey;
            string planKey = request.PlanKey;
            ScanWarmupStatusEntry cachedStatus = null;
            lock (_gate)
            {
                if ((State == ScanWarmupState.Preparing || State == ScanWarmupState.Ready) &&
                    String.Equals(_sourceKey, sourceKey, StringComparison.Ordinal) &&
                    String.Equals(_targetKey, targetKey, StringComparison.Ordinal) &&
                    String.Equals(_planKey, planKey, StringComparison.Ordinal))
                    return;

                WarmupCacheEntry cached;
                if (_cache.TryGetValue(planKey, out cached) &&
                    String.Equals(cached.SourceKey, sourceKey, StringComparison.Ordinal) &&
                    DateTime.Now - cached.LastUsed <= CacheLifetime)
                {
                    CancelLocked(ScanWarmupState.Invalidated);
                    cached.LastUsed = DateTime.Now; _sourceKey = sourceKey; _targetKey = targetKey; _planKey = planKey;
                    _sourceCache = Clone(cached.Source); _preparedPlan = cached.Plan != null ? ClonePlan(cached.Plan) : null; _metrics = CopyMetrics(cached.Metrics); _failure = null; State = ScanWarmupState.Ready;
                    cachedStatus = StatusFromCache(cached);
                }
                if (cachedStatus != null) { }
                else
                {

                bool preserveSource = String.Equals(_sourceKey, sourceKey, StringComparison.Ordinal) &&
                    _sourceCache != null;
                SearchResult preservedSource = preserveSource ? _sourceCache : null;
                List<PlanItem> preservedPlan = null;
                WarmupCacheEntry planCache;
                if (_cache.TryGetValue(planKey, out planCache) &&
                    DateTime.Now - planCache.LastUsed <= CacheLifetime)
                    preservedPlan = ClonePlan(planCache.Plan);
                CancelLocked(ScanWarmupState.Invalidated);
                _sourceKey = sourceKey;
                _targetKey = targetKey;
                _planKey = planKey;
                _sourceCache = preservedSource;
                _preparedPlan = preservedPlan;
                _failure = null;
                _metrics = new ScanWarmupMetrics();
                _cancellation = new CancellationTokenSource();
                CancellationToken token = _cancellation.Token;
                long generation = ++_generation;
                State = ScanWarmupState.Preparing;
                _task = Task.Factory.StartNew(
                    delegate { Run(request, sourceKey, targetKey, planKey, generation, token); },
                    token,
                    TaskCreationOptions.None,
                    TaskScheduler.Default);
                }
            }
            if (cachedStatus != null) { ScanPerformanceDiagnostics.PublishWarmupStatus(cachedStatus); return; }
            ScanPerformanceDiagnostics.PublishWarmupStatus(new ScanWarmupStatusEntry
            {
                State = ScanWarmupState.Preparing
            });
        }

        public SearchResult GetOrRunSource(
            ScanWarmupRequest request,
            Action<ScanProgressInfo> progress,
            Func<bool> cancelRequested,
            out ScanWarmupMetrics metrics)
        {
            Stopwatch waitTimer = Stopwatch.StartNew();
            bool readyBeforeRequest;
            lock (_gate)
            {
                readyBeforeRequest = State == ScanWarmupState.Ready &&
                    String.Equals(_sourceKey, request.SourceKey, StringComparison.Ordinal) &&
                    String.Equals(_targetKey, request.TargetKey, StringComparison.Ordinal) &&
                    String.Equals(_planKey, request.PlanKey, StringComparison.Ordinal);
            }
            Start(request);
            Task active;
            lock (_gate) active = _task;

            while (active != null && !active.IsCompleted)
            {
                if (cancelRequested != null && cancelRequested())
                    throw new OperationCanceledException();
                try { active.Wait(40); }
                catch (AggregateException) { break; }
            }

            lock (_gate)
            {
                if (String.Equals(_sourceKey, request.SourceKey, StringComparison.Ordinal) && _sourceCache != null)
                {
                    metrics = CopyMetrics(_metrics);
                    metrics.WarmupHit = true;
                    metrics.ReadyBeforeRequest = readyBeforeRequest;
                    long waitBeforeCopy = waitTimer.ElapsedMilliseconds;
                    SearchResult view = Clone(_sourceCache);
                    waitTimer.Stop();
                    metrics.WarmupWaitMs = readyBeforeRequest ? 0 : waitBeforeCopy;
                    // Include the actual in-memory snapshot projection cost
                    // while excluding historical provider/SQLite timings.
                    metrics.FileDiscoveryMs = waitTimer.ElapsedMilliseconds;
                    metrics.ProviderQueryMs = 0;
                    metrics.IndexReconcileMs = 0;
                    if (view != null)
                    {
                        view.ProviderQueryMs = 0;
                        view.IndexReconcileMs = 0;
                    }
                    return view;
                }
            }

            Stopwatch timer = Stopwatch.StartNew();
            SearchResult direct = _fileSystemProvider.SearchFiles(
                request.Source, request.Recursive, 0, request.BlockedPaths,
                request.Extensions, progress, cancelRequested);
            timer.Stop();
            metrics = new ScanWarmupMetrics();
            metrics.FileDiscoveryMs = timer.ElapsedMilliseconds;
            metrics.Provider = direct.Backend ?? "";
            waitTimer.Stop();
            metrics.ReadyBeforeRequest = false;
            metrics.WarmupWaitMs = waitTimer.ElapsedMilliseconds;

            // Only a user-initiated discovery may populate the in-memory source
            // snapshot. Startup warmup must never do filesystem enumeration.
            lock (_gate)
            {
                if (String.Equals(_sourceKey, request.SourceKey, StringComparison.Ordinal) &&
                    String.Equals(_targetKey, request.TargetKey, StringComparison.Ordinal) &&
                    String.Equals(_planKey, request.PlanKey, StringComparison.Ordinal))
                {
                    _sourceCache = Clone(direct);
                    _metrics = CopyMetrics(metrics);
                    State = ScanWarmupState.Ready;

                    WarmupCacheEntry cached;
                    if (!_cache.TryGetValue(request.PlanKey, out cached))
                    {
                        cached = new WarmupCacheEntry();
                        _cache[request.PlanKey] = cached;
                    }
                    cached.LastUsed = DateTime.Now;
                    cached.SourceKey = request.SourceKey;
                    cached.Source = Clone(direct);
                    cached.Plan = _preparedPlan != null
                        ? ClonePlan(_preparedPlan)
                        : null;
                    cached.Metrics = CopyMetrics(metrics);
                    TrimCacheLocked();
                }
            }
            return direct;
        }

        public void Cancel()
        {
            lock (_gate) CancelLocked(ScanWarmupState.Cancelled);
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
                RemoveMovedFile(_sourceCache, sourcePath, fileId);
                RemoveMovedPlan(ref _preparedPlan, sourcePath, fileId);
                foreach (WarmupCacheEntry cached in _cache.Values)
                {
                    RemoveMovedFile(cached.Source, sourcePath, fileId);
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
                RenameSourceFile(_sourceCache, oldPath, newPath, fileId);
                RemoveMovedPlan(ref _preparedPlan, oldPath, fileId);
                foreach (WarmupCacheEntry cached in _cache.Values)
                {
                    RenameSourceFile(cached.Source, oldPath, newPath, fileId);
                    RemoveMovedPlan(ref cached.Plan, oldPath, fileId);
                }
            }
        }

        public bool TryGetPreparedPlan(
            ScanWarmupRequest request,
            out List<PlanItem> plan,
            out long prepareMilliseconds)
        {
            lock (_gate)
            {
                if (request != null &&
                    State == ScanWarmupState.Ready &&
                    String.Equals(_planKey, request.PlanKey, StringComparison.Ordinal) &&
                    _preparedPlan != null)
                {
                    plan = ClonePlan(_preparedPlan);
                    prepareMilliseconds = _metrics.SnapshotPrepareMs;
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
                    _sourceCache = Clone(cached.Source);
                    _preparedPlan = ClonePlan(cached.Plan);
                    _metrics = CopyMetrics(cached.Metrics);
                    _metrics.SnapshotHit = true;
                    State = ScanWarmupState.Ready;
                    plan = ClonePlan(_preparedPlan);
                    prepareMilliseconds = _metrics.SnapshotPrepareMs;
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
                _sourceKey = request.SourceKey;
                _targetKey = request.TargetKey;
                _planKey = request.PlanKey;
                _preparedPlan = MergePlanEntries(_preparedPlan, stored);
                _metrics.SnapshotPrepareMs = prepareMilliseconds;
                _metrics.SnapshotHit = false;
                State = ScanWarmupState.Ready;
                WarmupCacheEntry cached;
                if (!_cache.TryGetValue(request.PlanKey, out cached))
                {
                    cached = new WarmupCacheEntry
                    {
                        LastUsed = DateTime.Now,
                        SourceKey = request.SourceKey,
                        Source = Clone(_sourceCache),
                        Metrics = CopyMetrics(_metrics)
                    };
                    _cache[request.PlanKey] = cached;
                }
                cached.LastUsed = DateTime.Now;
                cached.SourceKey = request.SourceKey;
                cached.Source = Clone(_sourceCache);
                cached.Plan = MergePlanEntries(cached.Plan, stored);
                cached.Metrics = CopyMetrics(_metrics);
                TrimCacheLocked();
            }
        }

        private void Run(ScanWarmupRequest request, string sourceKey, string targetKey, string planKey, long generation, CancellationToken token)
        {
            ThreadPriority originalPriority = Thread.CurrentThread.Priority;
            string failureStage = "持久缓存准备";
            try
            {
                try { Thread.CurrentThread.Priority = ThreadPriority.BelowNormal; } catch { }
                token.ThrowIfCancellationRequested();
                lock (_gate)
                {
                    if (generation != _generation || token.IsCancellationRequested) return;
                    // Keep any source/plan produced by an earlier foreground
                    // scan, but do not create either one during startup warmup.
                    _metrics.FileDiscoveryMs = 0;
                    _metrics.TargetIndexMs = 0;
                    _metrics.SnapshotPrepareMs = 0;
                    State = ScanWarmupState.Ready;
                    if (_sourceCache != null || _preparedPlan != null)
                    {
                        WarmupCacheEntry cached;
                        if (!_cache.TryGetValue(planKey, out cached))
                        {
                            cached = new WarmupCacheEntry();
                            _cache[planKey] = cached;
                        }
                        cached.LastUsed = DateTime.Now;
                        cached.SourceKey = sourceKey;
                        cached.Source = Clone(_sourceCache);
                        cached.Plan = _preparedPlan != null
                            ? ClonePlan(_preparedPlan)
                            : null;
                        cached.Metrics = CopyMetrics(_metrics);
                        TrimCacheLocked();
                    }
                }
                ScanPerformanceDiagnostics.PublishWarmupStatus(new ScanWarmupStatusEntry
                {
                    State = ScanWarmupState.Ready,
                    Stage = "持久缓存已就绪",
                    FileDiscoveryMs = 0,
                    TargetIndexMs = 0,
                    SnapshotPrepareMs = 0
                });
            }
            catch (OperationCanceledException)
            {
                lock (_gate) if (generation == _generation) State = ScanWarmupState.Cancelled;
            }
            catch (Exception ex)
            {
                lock (_gate)
                {
                    if (generation != _generation) return;
                    _failure = ex;
                    State = ScanWarmupState.Failed;
                }
                ScanPerformanceDiagnostics.PublishWarmupStatus(new ScanWarmupStatusEntry
                {
                    State = ScanWarmupState.Failed,
                    Stage = failureStage,
                    Error = ex.GetType().Name + ": " + ex.Message
                });
            }
            finally
            {
                try { Thread.CurrentThread.Priority = originalPriority; } catch { }
            }
        }

        private void CancelLocked(ScanWarmupState state)
        {
            ++_generation;
            if (_cancellation != null) _cancellation.Cancel();
            _cancellation = null;
            _task = null;
            State = state;
        }

        private static ScanWarmupStatusEntry StatusFromCache(WarmupCacheEntry cached)
        {
            return new ScanWarmupStatusEntry { State = ScanWarmupState.Ready, Provider = cached.Metrics.Provider,
                CandidateCount = cached.Source != null && cached.Source.Files != null ? cached.Source.Files.Count : 0,
                FileDiscoveryMs = cached.Metrics.FileDiscoveryMs, TargetIndexMs = cached.Metrics.TargetIndexMs,
                SnapshotPrepareMs = cached.Metrics.SnapshotPrepareMs,
                ParsedCacheHits = cached.Metrics.ParsedCacheHits,
                ParsedCacheMisses = cached.Metrics.ParsedCacheMisses,
                RecognitionCacheHits = cached.Metrics.RecognitionCacheHits,
                RecognitionCacheMisses = cached.Metrics.RecognitionCacheMisses,
                DestinationCacheHits = cached.Metrics.DestinationCacheHits,
                DestinationCacheMisses = cached.Metrics.DestinationCacheMisses };
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

        private static SearchResult Clone(SearchResult source)
        {
            SearchResult copy = new SearchResult();
            if (source == null) return copy;
            copy.Files = source.Files != null ? new List<FileInfo>(source.Files) : new List<FileInfo>();
            copy.IndexFiles = source.IndexFiles != null ? new List<FileInfo>(source.IndexFiles) : new List<FileInfo>();
            copy.IndexEntries = source.IndexEntries != null
                ? source.IndexEntries.Where(x => x != null).Select(
                    delegate(SourceIndexFileSnapshot entry)
                    {
                        return new SourceIndexFileSnapshot
                        {
                            FullPath = entry.FullPath,
                            DirectoryPath = entry.DirectoryPath,
                            FileName = entry.FileName,
                            Extension = entry.Extension,
                            FileSize = entry.FileSize,
                            LastWriteTimeUtc = entry.LastWriteTimeUtc,
                            CreationTimeUtc = entry.CreationTimeUtc
                        };
                    }).ToList()
                : new List<SourceIndexFileSnapshot>();
            copy.ExcludedItems = source.ExcludedItems != null
                ? new List<ScanExcludedItem>(source.ExcludedItems)
                : new List<ScanExcludedItem>();
            copy.DiscoveredCount = source.DiscoveredCount;
            copy.Backend = source.Backend;
            copy.ProviderQueryMs = source.ProviderQueryMs;
            copy.IndexReconcileMs = source.IndexReconcileMs;
            copy.Detail = source.Detail;
            if (source.ScanSession != null)
            {
                copy.ScanSession = new ScanSessionSnapshot
                {
                    SessionId = source.ScanSession.SessionId,
                    RootId = source.ScanSession.RootId,
                    FileIds = new HashSet<long>(source.ScanSession.FileIds),
                    FileIdsByPath = new Dictionary<string, long>(
                        source.ScanSession.FileIdsByPath,
                        StringComparer.OrdinalIgnoreCase)
                };
            }
            return copy;
        }

        private static void RemoveMovedFile(
            SearchResult source,
            string sourcePath,
            long fileId)
        {
            if (source == null) return;
            int removed = source.Files != null
                ? source.Files.RemoveAll(delegate(FileInfo file)
                {
                    return file != null && String.Equals(
                        NormalizeFilePath(file.FullName),
                        NormalizeFilePath(sourcePath),
                        StringComparison.Ordinal);
                })
                : 0;
            if (removed > 0)
                source.DiscoveredCount = Math.Max(0, source.DiscoveredCount - removed);
            if (source.IndexFiles != null)
            {
                source.IndexFiles.RemoveAll(delegate(FileInfo file)
                {
                    return file != null && String.Equals(
                        NormalizeFilePath(file.FullName),
                        NormalizeFilePath(sourcePath),
                        StringComparison.Ordinal);
                });
            }
            if (source.IndexEntries != null)
            {
                source.IndexEntries.RemoveAll(delegate(SourceIndexFileSnapshot entry)
                {
                    return entry != null && String.Equals(
                        NormalizeFilePath(entry.FullPath),
                        NormalizeFilePath(sourcePath),
                        StringComparison.Ordinal);
                });
            }

            ScanSessionSnapshot session = source.ScanSession;
            if (session == null) return;
            long mappedId;
            if (session.FileIdsByPath.TryGetValue(sourcePath ?? "", out mappedId))
            {
                session.FileIdsByPath.Remove(sourcePath ?? "");
                session.FileIds.Remove(mappedId);
            }
            if (fileId > 0) session.FileIds.Remove(fileId);
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

        private static void RenameSourceFile(
            SearchResult source,
            string oldPath,
            string newPath,
            long fileId)
        {
            if (source == null) return;
            if (source.Files != null)
            {
                int index = source.Files.FindIndex(delegate(FileInfo file)
                {
                    return file != null && String.Equals(
                        NormalizeFilePath(file.FullName),
                        NormalizeFilePath(oldPath),
                        StringComparison.Ordinal);
                });
                if (index >= 0) source.Files[index] = new FileInfo(newPath);
            }
            if (source.IndexFiles != null)
            {
                int index = source.IndexFiles.FindIndex(delegate(FileInfo file)
                {
                    return file != null && String.Equals(
                        NormalizeFilePath(file.FullName),
                        NormalizeFilePath(oldPath),
                        StringComparison.Ordinal);
                });
                if (index >= 0) source.IndexFiles[index] = new FileInfo(newPath);
            }
            if (source.IndexEntries != null)
            {
                SourceIndexFileSnapshot entry = source.IndexEntries.FirstOrDefault(
                    delegate(SourceIndexFileSnapshot candidate)
                    {
                        return candidate != null && String.Equals(
                            NormalizeFilePath(candidate.FullPath),
                            NormalizeFilePath(oldPath),
                            StringComparison.Ordinal);
                    });
                if (entry != null)
                {
                    entry.FullPath = newPath ?? "";
                    entry.DirectoryPath = Path.GetDirectoryName(newPath ?? "") ?? "";
                    entry.FileName = Path.GetFileName(newPath ?? "") ?? "";
                    entry.Extension = Path.GetExtension(newPath ?? "") ?? "";
                }
            }

            ScanSessionSnapshot session = source.ScanSession;
            if (session == null) return;
            long mappedId;
            if (session.FileIdsByPath.TryGetValue(oldPath ?? "", out mappedId))
            {
                session.FileIdsByPath.Remove(oldPath ?? "");
                session.FileIdsByPath[newPath ?? ""] = mappedId;
            }
            else if (fileId > 0)
            {
                session.FileIdsByPath[newPath ?? ""] = fileId;
                session.FileIds.Add(fileId);
            }
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
