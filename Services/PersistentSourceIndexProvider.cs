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
        IFileSystemSnapshotVersionProvider, IFileSystemIndexMutationSink,
        IScanSessionProvider, IDisposable
    {
        private readonly IFileSystemIndexProvider _inner;
        private readonly FileIndexCacheDatabase _database;
        private readonly object _sync = new object();
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
        }

        public string ProviderId { get { return _inner.ProviderId; } }
        public bool IsAvailable { get { return _inner.IsAvailable; } }
        public long SnapshotRevision
        {
            get { return System.Threading.Interlocked.Read(ref _snapshotRevision); }
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
            // A full recursive snapshot can serve a shallow view, but a shallow
            // snapshot must NEVER pretend to cover an unvisited subtree.
            // On cold startup a direct-only request enumerates only this folder.
            string providerVersion = GetProviderConfigurationVersion(_inner);
            string baseKey = BuildSnapshotKey(
                source, blockedPaths, extensions, _inner.ProviderId, providerVersion);
            string fullKey = baseKey + "|Depth=All";
            string shallowKey = baseKey + "|Depth=Top";
            SearchResult canonical = null;
            lock (_sync)
            {
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
                projected.IndexReconcileMs = 0;
                return projected;
            }

            Stopwatch providerTimer = Stopwatch.StartNew();
            SearchResult discovered = _inner.SearchFiles(
                source, recursive, 0, blockedPaths, extensions,
                progress, cancelRequested);
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
                    StoreSnapshotLocked(recursive ? fullKey : shallowKey, source, discovered);
                }
            }
            catch (Exception ex)
            {
                // A successful provider query remains usable if SQLite fails,
                // but a failed reconciliation is visible in diagnostics.
                System.Diagnostics.Debug.WriteLine("Index reconciliation failed: " + ex);
                lock (_sync) _lastDelta = new FileIndexDelta();
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
            lock (_sync)
            {
                foreach (WatchedSnapshot snapshot in _snapshots.Values)
                    snapshot.Dispose();
                _snapshots.Clear();
            }
        }

        private void StoreSnapshotLocked(
            string key,
            string source,
            SearchResult result)
        {
            WatchedSnapshot previous;
            if (_snapshots.TryGetValue(key, out previous)) previous.Dispose();

            WatchedSnapshot snapshot = new WatchedSnapshot
            {
                Key = key,
                Root = source,
                Result = Clone(result, result.Detail),
                LastUsedUtc = DateTime.UtcNow
            };
            try
            {
                FileSystemWatcher watcher = new FileSystemWatcher(source);
                watcher.IncludeSubdirectories = true;
                watcher.NotifyFilter = NotifyFilters.FileName |
                    NotifyFilters.DirectoryName |
                    NotifyFilters.LastWrite |
                    NotifyFilters.Size |
                    NotifyFilters.CreationTime;
                watcher.InternalBufferSize = 32768;
                FileSystemEventHandler changed = delegate(object sender, FileSystemEventArgs e)
                {
                    MarkDirty(key, e != null ? e.FullPath : "");
                };
                RenamedEventHandler renamed = delegate(object sender, RenamedEventArgs e)
                {
                    MarkDirty(key,
                        e != null ? e.OldFullPath : "",
                        e != null ? e.FullPath : "");
                };
                ErrorEventHandler failed = delegate { MarkDirty(key); };
                watcher.Created += changed;
                watcher.Deleted += changed;
                watcher.Changed += changed;
                watcher.Renamed += renamed;
                watcher.Error += failed;
                watcher.EnableRaisingEvents = true;
                snapshot.Watcher = watcher;
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

        private void MarkDirty(string key, params string[] paths)
        {
            lock (_sync)
            {
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
                    System.Threading.Interlocked.Increment(ref _snapshotRevision);
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
                oldest.Dispose();
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

        private static string NormalizePath(string path)
        {
            try { return Path.GetFullPath(path ?? "").TrimEnd('\\', '/').ToUpperInvariant(); }
            catch { return (path ?? "").Trim().TrimEnd('\\', '/').ToUpperInvariant(); }
        }

    }
}
