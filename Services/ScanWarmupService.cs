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
                    String.Join(";", RecognizedAuthorFolderTemplates ?? new List<string>());
            }
        }

        public string PlanKey
        {
            get
            {
                // Scan mode and result limit are projections over the complete
                // classification snapshot. Changing either must not rebuild it.
                return SourceKey + "|" + TargetKey + "|MaxAuthors=" + MaxAuthors;
            }
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
        public long TargetIndexMs;
        public string Provider = "";
        public bool WarmupHit;
        public bool WarmupPartialHit;
        public bool SnapshotHit;
        public long SnapshotPrepareMs;
        public bool ReadyBeforeRequest;
        public long WarmupWaitMs;
    }

    /// <summary>
    /// Owns one low-priority, replaceable warmup. It caches filesystem discovery
    /// only; author parsing and any online resolution remain in the normal scan.
    /// </summary>
    internal sealed class ScanWarmupService : IDisposable
    {
        private readonly object _gate = new object();
        private readonly EverythingService _everything;
        private readonly ArchiveEngine _engine;
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
            public SearchResult Source;
            public List<PlanItem> Plan;
            public ScanWarmupMetrics Metrics;
            public Dictionary<string, ProjectionCacheEntry> Projections = new Dictionary<string, ProjectionCacheEntry>(StringComparer.Ordinal);
        }

        private sealed class ProjectionCacheEntry
        {
            public List<PlanItem> Plan;
            public int ClassifiedCount;
            public int MatchedCount;
        }

        public ScanWarmupService(EverythingService everything, ArchiveEngine engine)
        {
            _everything = everything;
            _engine = engine;
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
                if (_cache.TryGetValue(planKey, out cached) && DateTime.Now - cached.LastUsed <= CacheLifetime)
                {
                    CancelLocked(ScanWarmupState.Invalidated);
                    cached.LastUsed = DateTime.Now; _sourceKey = sourceKey; _targetKey = targetKey; _planKey = planKey;
                    _sourceCache = Clone(cached.Source); _preparedPlan = ClonePlan(cached.Plan); _metrics = CopyMetrics(cached.Metrics); _failure = null; State = ScanWarmupState.Ready;
                    cachedStatus = StatusFromCache(cached);
                }
                if (cachedStatus != null) { }
                else
                {

                bool preserveSource = String.Equals(_sourceKey, sourceKey, StringComparison.Ordinal) &&
                    _sourceCache != null;
                SearchResult preservedSource = preserveSource ? _sourceCache : null;
                CancelLocked(ScanWarmupState.Invalidated);
                _sourceKey = sourceKey;
                _targetKey = targetKey;
                _planKey = planKey;
                _sourceCache = preservedSource;
                _preparedPlan = null;
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
                    waitTimer.Stop();
                    metrics.WarmupWaitMs = readyBeforeRequest ? 0 : waitTimer.ElapsedMilliseconds;
                    return Clone(_sourceCache);
                }
            }

            Stopwatch timer = Stopwatch.StartNew();
            SearchResult direct = _everything.SearchFiles(
                request.Source, request.Recursive, 0, request.BlockedPaths,
                request.Extensions, progress, cancelRequested);
            timer.Stop();
            metrics = new ScanWarmupMetrics();
            metrics.FileDiscoveryMs = timer.ElapsedMilliseconds;
            metrics.Provider = direct.Backend ?? "";
            waitTimer.Stop();
            metrics.ReadyBeforeRequest = false;
            metrics.WarmupWaitMs = waitTimer.ElapsedMilliseconds;
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
            }
            plan = null;
            prepareMilliseconds = 0;
            return false;
        }

        public bool TryGetProjection(ScanWarmupRequest request, out List<PlanItem> plan, out int classifiedCount, out int matchedCount)
        {
            lock (_gate)
            {
                WarmupCacheEntry cached; ProjectionCacheEntry projection;
                if (request != null && _cache.TryGetValue(request.PlanKey, out cached) &&
                    cached.Projections.TryGetValue(ProjectionKey(request), out projection))
                {
                    cached.LastUsed = DateTime.Now; plan = ClonePlan(projection.Plan);
                    classifiedCount = projection.ClassifiedCount; matchedCount = projection.MatchedCount; return true;
                }
            }
            plan = null; classifiedCount = 0; matchedCount = 0; return false;
        }

        public void StoreProjection(ScanWarmupRequest request, List<PlanItem> plan, int classifiedCount, int matchedCount)
        {
            if (request == null || plan == null) return;
            lock (_gate)
            {
                WarmupCacheEntry cached;
                if (!_cache.TryGetValue(request.PlanKey, out cached)) return;
                cached.LastUsed = DateTime.Now;
                cached.Projections[ProjectionKey(request)] = new ProjectionCacheEntry
                { Plan = ClonePlan(plan), ClassifiedCount = classifiedCount, MatchedCount = matchedCount };
            }
        }

        private static string ProjectionKey(ScanWarmupRequest request)
        { return ((int)request.Mode).ToString() + "|" + request.RequestedLimit.ToString(); }

        private void Run(ScanWarmupRequest request, string sourceKey, string targetKey, string planKey, long generation, CancellationToken token)
        {
            ThreadPriority originalPriority = Thread.CurrentThread.Priority;
            try
            {
                try { Thread.CurrentThread.Priority = ThreadPriority.BelowNormal; } catch { }
                Stopwatch targetTimer = Stopwatch.StartNew();
                _engine.PrepareTargetDirectoryCache(
                    request.Target, request.GroupTemplate, request.RecognizedGroupTemplates,
                    request.AuthorFolderTemplate, request.RecognizedAuthorFolderTemplates,
                    delegate { return token.IsCancellationRequested; });
                targetTimer.Stop();
                token.ThrowIfCancellationRequested();
                ScanPerformanceDiagnostics.PublishWarmupStatus(new ScanWarmupStatusEntry
                {
                    State = ScanWarmupState.Preparing,
                    TargetIndexMs = targetTimer.ElapsedMilliseconds
                });

                Stopwatch sourceTimer = Stopwatch.StartNew();
                SearchResult source;
                lock (_gate) source = _sourceCache != null ? Clone(_sourceCache) : null;
                if (source == null)
                {
                    source = _everything.SearchFiles(
                        request.Source, request.Recursive, 0, request.BlockedPaths,
                        request.Extensions, null,
                        delegate { return token.IsCancellationRequested; });
                }
                sourceTimer.Stop();
                token.ThrowIfCancellationRequested();
                ScanPerformanceDiagnostics.PublishWarmupStatus(new ScanWarmupStatusEntry
                {
                    State = ScanWarmupState.PartiallyReady,
                    Provider = source.Backend ?? "",
                    CandidateCount = source.Files != null ? source.Files.Count : 0,
                    FileDiscoveryMs = sourceTimer.ElapsedMilliseconds,
                    TargetIndexMs = targetTimer.ElapsedMilliseconds
                });

                List<PlanItem> prepared = null;
                Stopwatch prepareTimer = Stopwatch.StartNew();
                List<FileInfo> files = source.Files;
                prepared = _engine.BuildPlan(
                    files, request.Target, request.MaxAuthors,
                    request.GroupTemplate, request.RecognizedGroupTemplates,
                    request.AuthorFolderTemplate, request.RecognizedAuthorFolderTemplates,
                    null, delegate { return token.IsCancellationRequested; },
                    ScanProgressStage.Planning);
                prepareTimer.Stop();
                token.ThrowIfCancellationRequested();
                lock (_gate)
                {
                    if (generation != _generation || token.IsCancellationRequested) return;
                    _sourceCache = Clone(source);
                    _metrics.FileDiscoveryMs = sourceTimer.ElapsedMilliseconds;
                    _metrics.TargetIndexMs = targetTimer.ElapsedMilliseconds;
                    _metrics.Provider = source.Backend ?? "";
                    _metrics.SnapshotPrepareMs = prepareTimer.ElapsedMilliseconds;
                    _preparedPlan = prepared != null ? ClonePlan(prepared) : null;
                    State = ScanWarmupState.Ready;
                    _cache[planKey] = new WarmupCacheEntry { LastUsed = DateTime.Now, Source = Clone(source), Plan = ClonePlan(prepared), Metrics = CopyMetrics(_metrics) };
                    TrimCacheLocked();
                }
                ScanPerformanceDiagnostics.PublishWarmupStatus(new ScanWarmupStatusEntry
                {
                    State = ScanWarmupState.Ready,
                    Provider = source.Backend ?? "",
                    CandidateCount = source.Files != null ? source.Files.Count : 0,
                    FileDiscoveryMs = sourceTimer.ElapsedMilliseconds,
                    TargetIndexMs = targetTimer.ElapsedMilliseconds,
                    SnapshotPrepareMs = prepareTimer.ElapsedMilliseconds
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
                    State = ScanWarmupState.Failed
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
                SnapshotPrepareMs = cached.Metrics.SnapshotPrepareMs };
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
            copy.ExcludedItems = source.ExcludedItems != null
                ? new List<ScanExcludedItem>(source.ExcludedItems)
                : new List<ScanExcludedItem>();
            copy.DiscoveredCount = source.DiscoveredCount;
            copy.Backend = source.Backend;
            copy.Detail = source.Detail;
            return copy;
        }

        private static ScanWarmupMetrics CopyMetrics(ScanWarmupMetrics source)
        {
            ScanWarmupMetrics copy = new ScanWarmupMetrics();
            copy.FileDiscoveryMs = source.FileDiscoveryMs;
            copy.TargetIndexMs = source.TargetIndexMs;
            copy.Provider = source.Provider;
            copy.WarmupHit = source.WarmupHit;
            copy.WarmupPartialHit = source.WarmupPartialHit;
            copy.SnapshotHit = source.SnapshotHit;
            copy.SnapshotPrepareMs = source.SnapshotPrepareMs;
            copy.ReadyBeforeRequest = source.ReadyBeforeRequest;
            copy.WarmupWaitMs = source.WarmupWaitMs;
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
                copy.IsExcludedPreview = item.IsExcludedPreview;
                copy.ExclusionRuleName = item.ExclusionRuleName;
                copy.ExclusionScope = item.ExclusionScope;
                copy.ExclusionIsDirectory = item.ExclusionIsDirectory;
                result.Add(copy);
            }
            return result;
        }


        public void Dispose() { Cancel(); }
    }
}
