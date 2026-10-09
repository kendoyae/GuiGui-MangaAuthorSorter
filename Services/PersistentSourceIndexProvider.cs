using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace MangaAuthorSorter
{
    /// <summary>
    /// Maintains GuiGui's durable SourceIndex while preserving the provider's
    /// discovery semantics. A later view can load the same index without doing
    /// filename parsing or recognition again.
    /// </summary>
    internal sealed class PersistentSourceIndexProvider : IFileSystemIndexProvider,
        IFileSystemSnapshotVersionProvider, IFileSystemScopedSnapshotVersionProvider, IFileSystemIndexMutationSink,
        IScanSessionProvider, IDisposable
    {
        private readonly IFileSystemIndexProvider _inner;
        private readonly FileIndexCacheDatabase _database;
        private readonly string _databasePath;
        private readonly object _sync = new object();
        private readonly object _discoveryGate = new object();
        private readonly List<WatchedSnapshot> _retiredSnapshots = new List<WatchedSnapshot>();
        private FileIndexDelta _lastDelta = new FileIndexDelta();
        private readonly Dictionary<string, WatchedSnapshot> _snapshots =
            new Dictionary<string, WatchedSnapshot>(StringComparer.Ordinal);
        private const int SnapshotCapacity = 8;
        private long _snapshotRevision;
        private ScanSessionSnapshot _currentScanSession;
        private readonly Dictionary<string, DateTime> _knownMutationPaths =
            new Dictionary<string, DateTime>(StringComparer.Ordinal);

        private sealed class WatchedSnapshot : IDisposable
        {
            public string Key = "";
            public string Root = "";
            public SearchResult Result;
            public FileSystemWatcher Watcher;
            public bool Dirty;
            public bool MonitorHealthy;
            public bool Recursive;
            public long Revision;
            public HashSet<string> Extensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            public DateTime LastUsedUtc;

            public void Dispose()
            {
                if (Watcher == null) return;
                try { Watcher.EnableRaisingEvents = false; } catch { }
                Watcher.Dispose();
                Watcher = null;
                MonitorHealthy = false;
            }
        }

        public PersistentSourceIndexProvider(
            IFileSystemIndexProvider inner,
            FileIndexCacheDatabase database)
        {
            if (inner == null) throw new ArgumentNullException("inner");
            if (database == null) throw new ArgumentNullException("database");
            _inner = inner;
            _database = database;
            _databasePath = NormalizePath(database.PathName);
        }

        public string ProviderId { get { return _inner.ProviderId; } }
        public bool IsAvailable { get { return _inner.IsAvailable; } }
        public long SnapshotRevision
        {
            get { return System.Threading.Interlocked.Read(ref _snapshotRevision); }
        }

        public long GetSnapshotRevision(string source, bool recursive, HashSet<string> blockedPaths, IEnumerable<string> extensions)
        {
            string key = BuildSnapshotKey(source, blockedPaths, extensions, _inner.ProviderId, GetProviderConfigurationVersion(_inner));
            lock (_sync)
            {
                WatchedSnapshot snapshot;
                if (_snapshots.TryGetValue(key + (recursive ? "|Depth=All" : "|Depth=Top"), out snapshot)) return snapshot.Revision;
                return !recursive && _snapshots.TryGetValue(key + "|Depth=All", out snapshot) ? snapshot.Revision : 0;
            }
        }

        public FileIndexDelta LastDelta
        {
            get
            {
                lock (_sync)
                {
                    return CloneDelta(_lastDelta);
                }
            }
        }

        public ScanSessionSnapshot CurrentScanSession
        {
            get
            {
                lock (_sync)
                {
                    return CloneSession(_currentScanSession);
                }
            }
        }

        public SearchResult SearchFiles(
            string source,
            bool recursive,
            int scanLimit,
            HashSet<string> blockedPaths,
            IEnumerable<string> extensions,
            Action<ScanProgressInfo> progress,
            Func<bool> cancelRequested)
        {
            // Matching requests wait for one discovery, then reuse its monitored
            // result. Cancellation is checked while waiting, including shutdown.
            while (!System.Threading.Monitor.TryEnter(_discoveryGate, 40))
                if (cancelRequested != null && cancelRequested()) throw new OperationCanceledException();
            try
            {
                if (cancelRequested != null && cancelRequested()) throw new OperationCanceledException();
                return SearchFilesCore(source, recursive, scanLimit, blockedPaths, extensions, progress, cancelRequested);
            }
            finally
            {
                System.Threading.Monitor.Exit(_discoveryGate);
                DisposeRetiredSnapshots();
            }
        }

        private SearchResult SearchFilesCore(
            string source, bool recursive, int scanLimit, HashSet<string> blockedPaths,
            IEnumerable<string> extensions, Action<ScanProgressInfo> progress, Func<bool> cancelRequested)
        {
            // A full recursive snapshot can serve a shallow view, but a shallow
            // snapshot must NEVER pretend to cover an unvisited subtree.
            // On cold startup a direct-only request enumerates only this folder.
            string providerVersion = GetProviderConfigurationVersion(_inner);
            string baseKey = BuildSnapshotKey(
                source, blockedPaths, extensions, _inner.ProviderId, providerVersion);
            string fullKey = baseKey + "|Depth=All";
            string shallowKey = baseKey + "|Depth=Top";
            SearchResult canonical = null;
            bool observedChanges = false;
            lock (_sync)
            {
                observedChanges = _snapshots.Values.Any(x => PathsMatch(x.Root, source) && x.Dirty);
                WatchedSnapshot cached;
                if ((_snapshots.TryGetValue(fullKey, out cached) &&
                     cached.MonitorHealthy && !cached.Dirty && cached.Result != null) ||
                    (!recursive && _snapshots.TryGetValue(shallowKey, out cached) &&
                     cached.MonitorHealthy && !cached.Dirty && cached.Result != null))
                {
                    cached.LastUsedUtc = DateTime.UtcNow;
                    _lastDelta = new FileIndexDelta {
                        CacheHits = cached.Result.Files != null ? cached.Result.Files.Count : 0
                    };
                    canonical = Clone(cached.Result, "SourceIndex 命中；文件系统无变化");
                }
            }
            if (canonical != null)
            {
                SearchResult projected = Project(canonical, source, recursive, scanLimit);
                projected.ProviderQueryMs = 0;
                projected.SourceSnapshotHit = true;
                projected.EverythingQueryCount = 0;
                projected.SdkPrepareMs = -1;
                projected.EverythingWaitMs = -1;
                projected.EverythingReadMs = -1;
                projected.DiscoveryCheckMs = -1;
                projected.DiscoverySetMs = -1;
                projected.DiscoveryEnumerateMs = -1;
                projected.DiscoveryCompareMs = -1;
                projected.IndexReconcileMs = 0;
                return projected;
            }

            // Monitor the discovery interval as well as the completed snapshot.
            // A mutation observed during reconciliation leaves this view dirty.
            string activeKey = recursive ? fullKey : shallowKey;
            lock (_sync) StoreSnapshotLocked(activeKey, source, new SearchResult(), recursive, extensions);
            Stopwatch providerTimer = Stopwatch.StartNew();
            SearchResult discovered;
            try
            {
                IFileSystemIndexRefreshProvider refresh = _inner as IFileSystemIndexRefreshProvider;
                discovered = observedChanges && refresh != null
                    ? refresh.SearchFilesAfterChange(source, recursive, 0, blockedPaths, extensions, progress, cancelRequested)
                    : _inner.SearchFiles(source, recursive, 0, blockedPaths, extensions, progress, cancelRequested);
                if (refresh != null)
                    discovered = refresh.ValidateDiscoveryScope(discovered, source, recursive, blockedPaths, extensions, progress, cancelRequested);
                bool changedDuringQuery;
                lock (_sync) changedDuringQuery = _snapshots[activeKey].Dirty;
                if (changedDuringQuery && refresh != null && discovered.Backend == "Everything SDK")
                {
                    SearchResult sdkResult = discovered;
                    int queries = discovered.EverythingQueryCount;
                    discovered = refresh.SearchFilesAfterChange(source, recursive, 0, blockedPaths, extensions, progress, cancelRequested);
                    discovered.EverythingQueryCount += queries;
                    discovered.SdkPrepareMs = sdkResult.SdkPrepareMs;
                    discovered.EverythingWaitMs = sdkResult.EverythingWaitMs;
                    discovered.EverythingReadMs = sdkResult.EverythingReadMs;
                    discovered.DiscoveryCheckMs = sdkResult.DiscoveryCheckMs;
                    discovered.DiscoverySetMs = sdkResult.DiscoverySetMs;
                    discovered.DiscoveryEnumerateMs = sdkResult.DiscoveryEnumerateMs;
                    discovered.DiscoveryCompareMs = sdkResult.DiscoveryCompareMs;
                }
                // The cache must never index or archive itself when the user
                // chooses a source that also contains the application's folder.
                discovered.Files.RemoveAll(x => IsInternalCachePath(x.FullName));
                discovered.IndexFiles.RemoveAll(x => IsInternalCachePath(x.FullName));
                discovered.IndexEntries.RemoveAll(x => IsInternalCachePath(x.FullPath));
                discovered.ExcludedItems.RemoveAll(x => IsInternalCachePath(x.Path));
            }
            catch
            {
                lock (_sync)
                {
                    WatchedSnapshot failed;
                    if (_snapshots.TryGetValue(activeKey, out failed))
                    { _retiredSnapshots.Add(failed); _snapshots.Remove(activeKey); }
                }
                throw;
            }
            providerTimer.Stop();
            Stopwatch syncTimer = Stopwatch.StartNew();
            try
            {
                long rootId = _database.EnsureRoot("Source", source, discovered.Backend);
                // Top-only reconciliation is a separate scope. It can update
                // direct children but cannot delete unseen descendants.
                // Reuse V1.12.6's full-scope key to preserve known-scope
                // deletion/move detection after upgrading an existing DB.
                string scopeKey = BuildIndexScopeKey(extensions) +
                    (recursive ? "" : "|Depth=Top");
                FileIndexDelta delta;
                if (discovered.IndexEntries != null && discovered.IndexEntries.Count > 0)
                    delta = _database.ReplaceSourceSnapshot(
                        rootId, source, discovered.Backend,
                        discovered.IndexEntries, scopeKey, !recursive);
                else
                {
                    IEnumerable<FileInfo> indexedFiles =
                        discovered.IndexFiles != null && discovered.IndexFiles.Count > 0
                            ? discovered.IndexFiles : discovered.Files;
                    delta = _database.ReplaceSourceSnapshot(
                        rootId, source, discovered.Backend, indexedFiles, scopeKey, !recursive);
                }
                lock (_sync)
                {
                    _lastDelta = delta;
                    WatchedSnapshot snapshot;
                    if (_snapshots.TryGetValue(activeKey, out snapshot))
                        snapshot.Result = Clone(discovered, discovered.Detail);
                }
            }
            catch (Exception ex)
            {
                // A successful provider query remains usable if SQLite fails,
                // but a failed reconciliation is visible in diagnostics.
                System.Diagnostics.Debug.WriteLine("Index reconciliation failed: " + ex);
                lock (_sync)
                {
                    _lastDelta = new FileIndexDelta();
                    WatchedSnapshot failed;
                    if (_snapshots.TryGetValue(activeKey, out failed)) failed.Dirty = true;
                }
            }
            finally
            {
                syncTimer.Stop();
            }
            SearchResult view = Project(discovered, source, recursive, scanLimit);
            view.ProviderQueryMs = providerTimer.ElapsedMilliseconds;
            view.IndexReconcileMs = syncTimer.ElapsedMilliseconds;
            return view;
        }

        /// <summary>
        /// Creates the only set that is allowed to enter parsing, recognition,
        /// statistics and execution planning for a user initiated scan. The
        /// completeIndex remains the durable/global index; it is never exposed
        /// as the current scan merely because warm-up discovered it.
        /// </summary>
        public SearchResult StartScanSession(
            string sourceRoot,
            SearchResult completeIndex,
            int scanLimit)
        {
            SearchResult sessionResult = Clone(completeIndex, completeIndex != null
                ? completeIndex.Detail : "");
            List<FileInfo> completeFiles = sessionResult.Files ?? new List<FileInfo>();
            if (scanLimit > 0 && completeFiles.Count > scanLimit)
                sessionResult.Files = completeFiles.Take(scanLimit).ToList();

            // Excluded paths are diagnostic output from synchronizing the full
            // index. They are not members of this scan and must not inflate its
            // list or counters when a quantity limit is active.
            if (scanLimit > 0)
                sessionResult.ExcludedItems = new List<ScanExcludedItem>();
            sessionResult.DiscoveredCount = sessionResult.Files.Count;

            long rootId = _database.EnsureRoot(
                "Source", sourceRoot, sessionResult.Backend);
            ScanSessionSnapshot session = _database.CreateScanSession(
                rootId, sessionResult.Files);
            sessionResult.ScanSession = CloneSession(session);
            lock (_sync) { _currentScanSession = session; }
            return sessionResult;
        }

        public void RegisterExpectedMutation(params string[] paths)
        {
            lock (_sync)
            {
                DateTime expires = DateTime.UtcNow.AddSeconds(10);
                foreach (string path in paths ?? new string[0])
                {
                    if (String.IsNullOrWhiteSpace(path)) continue;
                    _knownMutationPaths[NormalizePath(path)] = expires;
                }
            }
        }

        public void CancelExpectedMutation(params string[] paths)
        {
            lock (_sync)
            {
                foreach (string path in paths ?? new string[0])
                {
                    if (String.IsNullOrWhiteSpace(path)) continue;
                    _knownMutationPaths.Remove(NormalizePath(path));
                }
            }
        }

        public void ApplySuccessfulMove(string sourcePath, string targetPath)
        {
            string sourceKey = NormalizePath(sourcePath);
            string targetKey = NormalizePath(targetPath);
            lock (_sync)
            {
                DateTime expires = DateTime.UtcNow.AddSeconds(10);
                _knownMutationPaths[sourceKey] = expires;
                _knownMutationPaths[targetKey] = expires;

                long movedFileId = 0;
                if (_currentScanSession != null)
                    _currentScanSession.FileIdsByPath.TryGetValue(sourcePath ?? "", out movedFileId);

                bool staysIndexed = false;
                foreach (WatchedSnapshot snapshot in _snapshots.Values)
                {
                    if (snapshot.Result == null || snapshot.Result.Files == null)
                        continue;
                    bool targetInside = IsUnderRoot(targetPath, snapshot.Root);
                    int index = snapshot.Result.Files.FindIndex(
                        delegate(FileInfo file)
                        {
                            return file != null && String.Equals(
                                NormalizePath(file.FullName), sourceKey,
                                StringComparison.Ordinal);
                        });
                    if (index >= 0)
                    {
                        if (targetInside)
                        {
                            snapshot.Result.Files[index] = new FileInfo(targetPath);
                            staysIndexed = true;
                        }
                        else
                        {
                            snapshot.Result.Files.RemoveAt(index);
                            snapshot.Result.DiscoveredCount = Math.Max(
                                0, snapshot.Result.DiscoveredCount - 1);
                        }
                    }

                    if (snapshot.Result.IndexFiles != null)
                    {
                        int indexFile = snapshot.Result.IndexFiles.FindIndex(
                            delegate(FileInfo file)
                            {
                                return file != null && String.Equals(
                                    NormalizePath(file.FullName), sourceKey,
                                    StringComparison.Ordinal);
                            });
                        if (indexFile >= 0)
                        {
                            if (targetInside)
                                snapshot.Result.IndexFiles[indexFile] = new FileInfo(targetPath);
                            else
                                snapshot.Result.IndexFiles.RemoveAt(indexFile);
                        }
                    }
                    if (snapshot.Result.IndexEntries != null)
                    {
                        int entryIndex = snapshot.Result.IndexEntries.FindIndex(
                            delegate(SourceIndexFileSnapshot entry)
                            {
                                return entry != null && String.Equals(
                                    NormalizePath(entry.FullPath), sourceKey,
                                    StringComparison.Ordinal);
                            });
                        if (entryIndex >= 0)
                        {
                            if (targetInside)
                            {
                                SourceIndexFileSnapshot entry = snapshot.Result.IndexEntries[entryIndex];
                                entry.FullPath = targetPath ?? "";
                                entry.DirectoryPath = Path.GetDirectoryName(targetPath ?? "") ?? "";
                                entry.FileName = Path.GetFileName(targetPath ?? "") ?? "";
                                entry.Extension = Path.GetExtension(targetPath ?? "") ?? "";
                            }
                            else snapshot.Result.IndexEntries.RemoveAt(entryIndex);
                        }
                    }
                    snapshot.LastUsedUtc = DateTime.UtcNow;
                }

                if (_currentScanSession != null && movedFileId > 0)
                {
                    _currentScanSession.FileIdsByPath.Remove(sourcePath ?? "");
                    if (staysIndexed)
                    {
                        _currentScanSession.FileIdsByPath[targetPath ?? ""] = movedFileId;
                        _currentScanSession.FileIds.Add(movedFileId);
                    }
                    else
                    {
                        _currentScanSession.FileIds.Remove(movedFileId);
                    }
                }

                if (staysIndexed)
                {
                    _lastDelta = new FileIndexDelta
                    {
                        Moved = 1,
                        ChangedPaths = new List<string> { sourcePath ?? "", targetPath ?? "" },
                        PathChanges = new List<FileIndexPathChange>
                        {
                            new FileIndexPathChange
                            {
                                Kind = FileIndexPathChangeKind.Moved,
                                FileId = movedFileId,
                                OldPath = sourcePath ?? "",
                                NewPath = targetPath ?? ""
                            }
                        }
                    };
                }
                else
                {
                    _lastDelta = new FileIndexDelta
                    {
                        Removed = 1,
                        ChangedPaths = new List<string> { sourcePath ?? "" }
                    };
                }
            }
        }

        public void ApplySuccessfulRename(string oldPath, string newPath)
        {
            string oldKey = NormalizePath(oldPath);
            string newKey = NormalizePath(newPath);
            lock (_sync)
            {
                DateTime expires = DateTime.UtcNow.AddSeconds(10);
                _knownMutationPaths[oldKey] = expires;
                _knownMutationPaths[newKey] = expires;
                foreach (WatchedSnapshot snapshot in _snapshots.Values)
                {
                    if (snapshot.Result == null || snapshot.Result.Files == null) continue;
                    int index = snapshot.Result.Files.FindIndex(
                        delegate(FileInfo file)
                        {
                            return file != null && String.Equals(
                                NormalizePath(file.FullName), oldKey, StringComparison.Ordinal);
                        });
                    if (index >= 0) snapshot.Result.Files[index] = new FileInfo(newPath);
                    if (snapshot.Result.IndexFiles != null)
                    {
                        int indexFile = snapshot.Result.IndexFiles.FindIndex(
                            delegate(FileInfo file)
                            {
                                return file != null && String.Equals(
                                    NormalizePath(file.FullName), oldKey, StringComparison.Ordinal);
                            });
                        if (indexFile >= 0) snapshot.Result.IndexFiles[indexFile] = new FileInfo(newPath);
                    }
                    if (snapshot.Result.IndexEntries != null)
                    {
                        SourceIndexFileSnapshot entry = snapshot.Result.IndexEntries.FirstOrDefault(
                            delegate(SourceIndexFileSnapshot candidate)
                            {
                                return candidate != null && String.Equals(
                                    NormalizePath(candidate.FullPath), oldKey, StringComparison.Ordinal);
                            });
                        if (entry != null)
                        {
                            entry.FullPath = newPath ?? "";
                            entry.DirectoryPath = Path.GetDirectoryName(newPath ?? "") ?? "";
                            entry.FileName = Path.GetFileName(newPath ?? "") ?? "";
                            entry.Extension = Path.GetExtension(newPath ?? "") ?? "";
                        }
                    }
                    snapshot.LastUsedUtc = DateTime.UtcNow;
                }
                if (_currentScanSession != null)
                {
                    long fileId;
                    if (_currentScanSession.FileIdsByPath.TryGetValue(oldPath ?? "", out fileId))
                    {
                        _currentScanSession.FileIdsByPath.Remove(oldPath ?? "");
                        _currentScanSession.FileIdsByPath[newPath ?? ""] = fileId;
                    }
                }
                _lastDelta = new FileIndexDelta
                {
                    Renamed = 1,
                    ChangedPaths = new List<string> { oldPath ?? "", newPath ?? "" },
                    PlanInvalidatedPaths = new List<string> { oldPath ?? "", newPath ?? "" },
                    RecognitionInvalidatedPaths = new List<string> { oldPath ?? "", newPath ?? "" },
                    PathChanges = new List<FileIndexPathChange>
                    {
                        new FileIndexPathChange
                        {
                            Kind = FileIndexPathChangeKind.Renamed,
                            OldPath = oldPath ?? "",
                            NewPath = newPath ?? ""
                        }
                    }
                };
            }
        }

        public void Dispose()
        {
            List<WatchedSnapshot> snapshots;
            lock (_sync)
            {
                snapshots = _snapshots.Values.Concat(_retiredSnapshots).ToList();
                _snapshots.Clear();
                _retiredSnapshots.Clear();
            }
            // FileSystemWatcher.Dispose can wait for an event callback. Never
            // hold the very lock that the callback needs while waiting for it.
            foreach (WatchedSnapshot snapshot in snapshots) snapshot.Dispose();
        }

        private void DisposeRetiredSnapshots()
        {
            List<WatchedSnapshot> retired;
            lock (_sync) { retired = _retiredSnapshots.ToList(); _retiredSnapshots.Clear(); }
            foreach (WatchedSnapshot snapshot in retired) snapshot.Dispose();
        }

        private void StoreSnapshotLocked(
            string key,
            string source,
            SearchResult result, bool recursive, IEnumerable<string> extensions)
        {
            WatchedSnapshot previous;
            long revision = 0;
            if (!_snapshots.TryGetValue(key, out previous) && key.EndsWith("|Depth=Top", StringComparison.Ordinal))
            {
                WatchedSnapshot full;
                if (_snapshots.TryGetValue(key.Substring(0, key.Length - "|Depth=Top".Length) + "|Depth=All", out full)) revision = full.Revision;
            }
            if (_snapshots.TryGetValue(key, out previous)) _retiredSnapshots.Add(previous);
            if (previous != null) revision = previous.Revision;

            WatchedSnapshot snapshot = new WatchedSnapshot
            {
                Key = key,
                Root = source,
                Recursive = recursive,
                Revision = revision,
                Extensions = new HashSet<string>(FileTypeRules.NormalizeExtensions(extensions), StringComparer.OrdinalIgnoreCase),
                Result = Clone(result, result.Detail),
                LastUsedUtc = DateTime.UtcNow
            };
            try
            {
                FileSystemWatcher watcher = new FileSystemWatcher(source);
                watcher.IncludeSubdirectories = recursive;
                watcher.NotifyFilter = NotifyFilters.FileName |
                    NotifyFilters.DirectoryName |
                    NotifyFilters.LastWrite |
                    NotifyFilters.Size;
                watcher.InternalBufferSize = 32768;
                FileSystemEventHandler changed = delegate(object sender, FileSystemEventArgs e)
                {
                    if (snapshot.Dirty) return;
                    if (e != null && IsInternalCachePath(e.FullPath)) return;
                    // Directory metadata notifications can be generated by
                    // enumeration. Name events still track directory mutations.
                    if (e != null && e.ChangeType == WatcherChangeTypes.Changed && Directory.Exists(e.FullPath)) return;
                    if (e != null && !snapshot.Extensions.Contains(Path.GetExtension(e.FullPath).TrimStart('.')))
                    {
                        bool directory = Directory.Exists(e.FullPath);
                        bool containedFiles = snapshot.Recursive && ContainsIndexedDescendants(snapshot, e.FullPath);
                        if (!snapshot.Recursive || (!directory && !containedFiles)) return;
                    }
                    MarkDirty(key, snapshot, e != null ? e.FullPath : "");
                };
                RenamedEventHandler renamed = delegate(object sender, RenamedEventArgs e)
                {
                    if (snapshot.Dirty) return;
                    if (e != null && IsInternalCachePath(e.OldFullPath) && IsInternalCachePath(e.FullPath)) return;
                    if (e != null && !snapshot.Extensions.Contains(Path.GetExtension(e.OldFullPath).TrimStart('.')) &&
                        !snapshot.Extensions.Contains(Path.GetExtension(e.FullPath).TrimStart('.')) &&
                        (!snapshot.Recursive || (!Directory.Exists(e.FullPath) && !ContainsIndexedDescendants(snapshot, e.OldFullPath)))) return;
                    MarkDirty(key, snapshot,
                        e != null ? e.OldFullPath : "",
                        e != null ? e.FullPath : "");
                };
                ErrorEventHandler failed = delegate { MarkDirty(key, snapshot); };
                watcher.Created += changed;
                watcher.Deleted += changed;
                watcher.Changed += changed;
                watcher.Renamed += renamed;
                watcher.Error += failed;
                snapshot.Watcher = watcher;
                _snapshots[key] = snapshot;
                watcher.EnableRaisingEvents = true;
                snapshot.MonitorHealthy = true;
            }
            catch
            {
                snapshot.MonitorHealthy = false;
                snapshot.Dirty = true;
            }
            _snapshots[key] = snapshot;
            TrimSnapshotsLocked();
        }

        private void MarkDirty(string key, WatchedSnapshot expected, params string[] paths)
        {
            lock (_sync)
            {
                WatchedSnapshot current;
                if (!_snapshots.TryGetValue(key, out current) || !Object.ReferenceEquals(current, expected)) return;
                DateTime now = DateTime.UtcNow;
                foreach (string expired in _knownMutationPaths
                    .Where(x => x.Value < now).Select(x => x.Key).ToList())
                    _knownMutationPaths.Remove(expired);
                if (paths != null && paths.Length > 0 && paths.All(
                    delegate(string path)
                    {
                        DateTime expiry;
                        return _knownMutationPaths.TryGetValue(
                            NormalizePath(path), out expiry) && expiry >= now;
                    }))
                    return;

                WatchedSnapshot snapshot;
                if (_snapshots.TryGetValue(key, out snapshot) && !snapshot.Dirty)
                {
                    snapshot.Dirty = true;
                    snapshot.Revision = System.Threading.Interlocked.Increment(ref _snapshotRevision);
                }
            }
        }

        private void TrimSnapshotsLocked()
        {
            while (_snapshots.Count > SnapshotCapacity)
            {
                WatchedSnapshot oldest = _snapshots.Values
                    .OrderBy(x => x.LastUsedUtc)
                    .FirstOrDefault();
                if (oldest == null) break;
                _retiredSnapshots.Add(oldest);
                _snapshots.Remove(oldest.Key);
            }
        }

        private static SearchResult Clone(SearchResult source, string detail)
        {
            SearchResult copy = new SearchResult();
            if (source == null) return copy;
            copy.Files = source.Files != null
                ? new List<FileInfo>(source.Files)
                : new List<FileInfo>();
            copy.IndexFiles = source.IndexFiles != null
                ? new List<FileInfo>(source.IndexFiles)
                : new List<FileInfo>();
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
            copy.EverythingQueryCount = source.EverythingQueryCount;
            copy.SdkPrepareMs = source.SdkPrepareMs;
            copy.EverythingWaitMs = source.EverythingWaitMs;
            copy.EverythingReadMs = source.EverythingReadMs;
            copy.DiscoveryCheckMs = source.DiscoveryCheckMs;
            copy.DiscoverySetMs = source.DiscoverySetMs;
            copy.DiscoveryEnumerateMs = source.DiscoveryEnumerateMs;
            copy.DiscoveryCompareMs = source.DiscoveryCompareMs;
            copy.IndexReconcileMs = source.IndexReconcileMs;
            copy.Detail = detail ?? source.Detail ?? "";
            copy.ScanSession = CloneSession(source.ScanSession);
            return copy;
        }

        private static ScanSessionSnapshot CloneSession(ScanSessionSnapshot source)
        {
            if (source == null) return null;
            return new ScanSessionSnapshot
            {
                SessionId = source.SessionId,
                RootId = source.RootId,
                FileIds = new HashSet<long>(source.FileIds),
                FileIdsByPath = new Dictionary<string, long>(
                    source.FileIdsByPath, StringComparer.OrdinalIgnoreCase)
            };
        }

        private static string BuildSnapshotKey(
            string source,
            HashSet<string> blockedPaths,
            IEnumerable<string> extensions,
            string providerId,
            string providerVersion)
        {
            string root;
            try { root = Path.GetFullPath(source ?? "").TrimEnd('\\', '/').ToUpperInvariant(); }
            catch { root = (source ?? "").Trim().ToUpperInvariant(); }
            string extensionKey = String.Join(";",
                FileTypeRules.NormalizeExtensions(extensions)
                    .OrderBy(x => x, StringComparer.OrdinalIgnoreCase));
            string blockedKey = String.Join(";",
                (blockedPaths ?? new HashSet<string>())
                    .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                    .Select(NormalizePath));
            return root + "|CanonicalRecursive=True" +
                "|Provider=" + (providerId ?? "") +
                "|ProviderConfig=" + (providerVersion ?? "") +
                "|Extensions=" + extensionKey + "|Blocked=" + blockedKey;
        }

        private static bool IsUnderRoot(string path, string root)
        {
            try
            {
                string normalizedRoot = NormalizePath(root);
                string normalizedPath = NormalizePath(path);
                return normalizedPath.StartsWith(
                    normalizedRoot + Path.DirectorySeparatorChar,
                    StringComparison.Ordinal);
            }
            catch { return false; }
        }

        private static SearchResult Project(
            SearchResult canonical,
            string source,
            bool recursive,
            int scanLimit)
        {
            SearchResult result = Clone(canonical, canonical != null ? canonical.Detail : "");
            if (canonical == null) return result;
            string root = NormalizePath(source);
            string prefix = root + Path.DirectorySeparatorChar;
            IEnumerable<FileInfo> query = canonical.Files ?? new List<FileInfo>();
            query = query.Where(delegate(FileInfo file)
            {
                if (file == null) return false;
                string path = NormalizePath(file.FullName);
                string directory = NormalizePath(file.DirectoryName ?? "");
                return recursive
                    ? String.Equals(directory, root, StringComparison.Ordinal) ||
                      directory.StartsWith(prefix, StringComparison.Ordinal)
                    : String.Equals(directory, root, StringComparison.Ordinal);
            });
            if (scanLimit > 0) query = query.Take(scanLimit);
            result.Files = query.ToList();
            result.DiscoveredCount = result.Files.Count;
            result.ScanSession = null;
            return result;
        }

        private static string BuildIndexScopeKey(
            IEnumerable<string> extensions)
        {
            // Persistent FileIndex scope describes which physical file types are
            // indexed. Block lists, scan-exclusion rules, provider choice and
            // recursive/view flags are projections and must not own/delete facts.
            string extensionKey = String.Join(";",
                FileTypeRules.NormalizeExtensions(extensions)
                    .OrderBy(x => x, StringComparer.OrdinalIgnoreCase));
            return "CanonicalRecursive=True|Extensions=" + extensionKey;
        }

        private static string GetProviderConfigurationVersion(
            IFileSystemIndexProvider provider)
        {
            IFileSystemIndexConfigurationVersionProvider versioned =
                provider as IFileSystemIndexConfigurationVersionProvider;
            return versioned != null
                ? (versioned.IndexConfigurationVersion ?? "")
                : "";
        }

        private static FileIndexDelta CloneDelta(FileIndexDelta source)
        {
            if (source == null) return new FileIndexDelta();
            FileIndexDelta copy = new FileIndexDelta
            {
                CacheHits = source.CacheHits,
                Added = source.Added,
                Removed = source.Removed,
                Modified = source.Modified,
                Moved = source.Moved,
                Renamed = source.Renamed,
                ChangedPaths = new List<string>(source.ChangedPaths ?? new List<string>()),
                PlanInvalidatedPaths = new List<string>(
                    source.PlanInvalidatedPaths ?? new List<string>()),
                RecognitionInvalidatedPaths = new List<string>(
                    source.RecognitionInvalidatedPaths ?? new List<string>())
            };
            foreach (FileIndexPathChange change in source.PathChanges ?? new List<FileIndexPathChange>())
            {
                if (change == null) continue;
                copy.PathChanges.Add(new FileIndexPathChange
                {
                    Kind = change.Kind,
                    FileId = change.FileId,
                    OldPath = change.OldPath,
                    NewPath = change.NewPath
                });
            }
            return copy;
        }

        private bool ContainsIndexedDescendants(WatchedSnapshot snapshot, string path)
        {
            lock (_sync)
            {
                if (snapshot.Result == null) return false;
                return snapshot.Result.IndexEntries.Any(x => IsUnderRoot(x.FullPath, path)) ||
                    snapshot.Result.IndexFiles.Any(x => IsUnderRoot(x.FullName, path)) ||
                    snapshot.Result.Files.Any(x => IsUnderRoot(x.FullName, path)) ||
                    snapshot.Result.ExcludedItems.Any(x => PathsMatch(x.Path, path) || IsUnderRoot(x.Path, path));
            }
        }

        private bool IsInternalCachePath(string path)
        {
            string normalized = NormalizePath(path);
            return normalized == _databasePath || normalized == _databasePath + "-WAL" ||
                normalized == _databasePath + "-SHM" || normalized == _databasePath + "-JOURNAL" ||
                normalized.StartsWith(_databasePath + ".CORRUPT-", StringComparison.Ordinal);
        }

        private static bool PathsMatch(string left, string right)
        { return String.Equals(NormalizePath(left), NormalizePath(right), StringComparison.Ordinal); }

        private static string NormalizePath(string path)
        {
            try { return Path.GetFullPath(path ?? "").TrimEnd('\\', '/').ToUpperInvariant(); }
            catch { return (path ?? "").Trim().TrimEnd('\\', '/').ToUpperInvariant(); }
        }

    }
}
