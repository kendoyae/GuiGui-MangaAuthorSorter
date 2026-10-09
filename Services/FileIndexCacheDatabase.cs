using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Linq;

namespace MangaAuthorSorter
{
    /// <summary>
    /// Durable source/destination index. Windows' inbox SQLite runtime keeps the
    /// application dependency-free. All mutations are transactional and the
    /// filesystem remains authoritative.
    /// </summary>
    internal sealed class FileIndexCacheDatabase : IDisposable
    {
        private const int SqliteOk = 0;
        private const int SqliteRow = 100;
        private const int SqliteDone = 101;
        private const int OpenReadWrite = 0x00000002;
        private const int OpenCreate = 0x00000004;
        private const int OpenFullMutex = 0x00010000;
        private readonly object _gate = new object();
        private readonly string _path;
        private IntPtr _db;
        private bool _disposed;
        public bool WasRecoveredFromCorruption { get; private set; }

        public FileIndexCacheDatabase(string path)
        {
            if (String.IsNullOrWhiteSpace(path)) throw new ArgumentNullException("path");
            _path = path;
        }

        public string PathName { get { return _path; } }

        public void Open()
        {
            lock (_gate)
            {
                if (_disposed) throw new ObjectDisposedException("FileIndexCacheDatabase");
                if (_db != IntPtr.Zero) return;
                string directory = Path.GetDirectoryName(_path);
                if (!String.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);
                int code = Native.sqlite3_open_v2(
                    Utf8Z(_path), out _db,
                    OpenReadWrite | OpenCreate | OpenFullMutex, IntPtr.Zero);
                if (code != SqliteOk || _db == IntPtr.Zero)
                    throw new InvalidOperationException("无法打开文件索引缓存：" + ErrorMessage(_db));
                try
                {
                    Execute("PRAGMA journal_mode=WAL; PRAGMA synchronous=NORMAL; PRAGMA foreign_keys=ON; PRAGMA busy_timeout=5000;");
                    EnsureSchema();
                }
                catch
                {
                    int failure = Native.sqlite3_errcode(_db) & 0xff;
                    Native.sqlite3_close(_db); _db = IntPtr.Zero;
                    if ((failure != 11 && failure != 26) || !File.Exists(_path) || WasRecoveredFromCorruption) throw;
                    // Preserve a corrupt database and its journal as a matching
                    // set; never treat permissions or I/O failures as corruption.
                    string backup = _path + ".corrupt-" + DateTime.UtcNow.ToString("yyyyMMddHHmmss") + "-" + Guid.NewGuid().ToString("N");
                    List<KeyValuePair<string, string>> moved = new List<KeyValuePair<string, string>>();
                    try
                    {
                        foreach (string suffix in new[] { "", "-wal", "-shm" })
                        {
                            if (!File.Exists(_path + suffix)) continue;
                            File.Move(_path + suffix, backup + suffix);
                            moved.Add(new KeyValuePair<string, string>(_path + suffix, backup + suffix));
                        }
                    }
                    catch
                    {
                        foreach (KeyValuePair<string, string> entry in moved.AsEnumerable().Reverse())
                            if (!File.Exists(entry.Key) && File.Exists(entry.Value)) File.Move(entry.Value, entry.Key);
                        throw;
                    }
                    WasRecoveredFromCorruption = true;
                    Open();
                }
            }
        }

        public PersistentCacheState GetPreviousRunState()
        {
            lock (_gate)
            {
                EnsureOpen();
                string value = ScalarText("SELECT Value FROM CacheMetadata WHERE Key='RunState'");
                PersistentCacheState state;
                return Enum.TryParse<PersistentCacheState>(value, true, out state)
                    ? state : PersistentCacheState.Unknown;
            }
        }

        public void MarkActive()
        {
            SetMetadata("RunState", PersistentCacheState.Active.ToString());
            SetMetadata("OpenedUtc", DateTime.UtcNow.ToString("o"));
        }

        public void MarkClean()
        {
            SetMetadata("RunState", PersistentCacheState.Clean.ToString());
            SetMetadata("ClosedUtc", DateTime.UtcNow.ToString("o"));
        }

        public long EnsureRoot(string kind, string rootPath, string provider)
        {
            lock (_gate)
            {
                EnsureOpen();
                string normalized = NormalizeRoot(rootPath);
                using (Statement statement = Prepare(
                    "INSERT OR IGNORE INTO Roots(Kind,RootPath,NormalizedPath,Provider,State,LastSyncUtc) VALUES(?1,?2,?3,?4,'NeedsValidation','');"))
                {
                    statement.Bind(1, kind ?? "Source");
                    statement.Bind(2, rootPath ?? "");
                    statement.Bind(3, normalized);
                    statement.Bind(4, provider ?? "");
                    statement.StepDone();
                }
                using (Statement statement = Prepare("SELECT RootId FROM Roots WHERE Kind=?1 AND NormalizedPath=?2"))
                {
                    statement.Bind(1, kind ?? "Source");
                    statement.Bind(2, normalized);
                    return statement.Step() == SqliteRow ? statement.Int64(0) : 0;
                }
            }
        }

        public Dictionary<string, SourceFileIndexEntry> LoadSourceFiles(long rootId)
        {
            Dictionary<string, SourceFileIndexEntry> result =
                new Dictionary<string, SourceFileIndexEntry>(StringComparer.OrdinalIgnoreCase);
            lock (_gate)
            {
                EnsureOpen();
                using (Statement statement = Prepare(
                    "SELECT FileId,FullPath,RelativePath,DirectoryPath,FileName,Extension,FileSize,LastWriteUtc,CreationUtc FROM SourceFiles WHERE RootId=?1"))
                {
                    statement.Bind(1, rootId);
                    while (statement.Step() == SqliteRow)
                    {
                        SourceFileIndexEntry entry = new SourceFileIndexEntry();
                        entry.FileId = statement.Int64(0);
                        entry.RootId = rootId;
                        entry.FullPath = statement.Text(1);
                        entry.RelativePath = statement.Text(2);
                        entry.DirectoryPath = statement.Text(3);
                        entry.FileName = statement.Text(4);
                        entry.Extension = statement.Text(5);
                        entry.FileSize = statement.Int64(6);
                        entry.LastWriteTimeUtc = ParseUtc(statement.Text(7));
                        entry.CreationTimeUtc = ParseUtc(statement.Text(8));
                        result[entry.FullPath] = entry;
                    }
                }
            }
            return result;
        }

        public ScanSessionSnapshot CreateScanSession(
            long rootId,
            IEnumerable<System.IO.FileInfo> files)
        {
            lock (_gate)
            {
                EnsureOpen();
                Execute("BEGIN IMMEDIATE;");
                try
                {
                    using (Statement statement = Prepare(
                        "INSERT INTO ScanSessions(RootId,CreatedUtc,State) VALUES(?1,?2,'Active')"))
                    {
                        statement.Bind(1, rootId);
                        statement.Bind(2, DateTime.UtcNow.ToString("o"));
                        statement.StepDone();
                    }
                    long sessionId;
                    using (Statement statement = Prepare("SELECT last_insert_rowid()"))
                        sessionId = statement.Step() == SqliteRow ? statement.Int64(0) : 0;

                    using (Statement statement = Prepare(
                        "INSERT OR IGNORE INTO ScanSessionFiles(SessionId,FileId) " +
                        "SELECT ?1,FileId FROM SourceFiles WHERE RootId=?2 AND NormalizedPath=?3"))
                    {
                        foreach (System.IO.FileInfo file in files ?? Enumerable.Empty<System.IO.FileInfo>())
                        {
                            if (file == null) continue;
                            statement.Reset();
                            statement.Bind(1, sessionId);
                            statement.Bind(2, rootId);
                            statement.Bind(3, NormalizePath(file.FullName));
                            statement.StepDone();
                        }
                    }

                    using (Statement statement = Prepare(
                        "UPDATE ScanSessions SET State='Complete' WHERE SessionId=?1"))
                    {
                        statement.Bind(1, sessionId);
                        statement.StepDone();
                    }
                    Execute("DELETE FROM ScanSessions WHERE SessionId NOT IN " +
                        "(SELECT SessionId FROM ScanSessions ORDER BY SessionId DESC LIMIT 16);");
                    Execute("COMMIT;");

                    ScanSessionSnapshot snapshot = new ScanSessionSnapshot
                    {
                        SessionId = sessionId,
                        RootId = rootId
                    };
                    using (Statement statement = Prepare(
                        "SELECT f.FileId,f.FullPath FROM ScanSessionFiles sf " +
                        "INNER JOIN SourceFiles f ON f.FileId=sf.FileId WHERE sf.SessionId=?1"))
                    {
                        statement.Bind(1, sessionId);
                        while (statement.Step() == SqliteRow)
                        {
                            long fileId = statement.Int64(0);
                            string path = statement.Text(1);
                            snapshot.FileIds.Add(fileId);
                            snapshot.FileIdsByPath[path] = fileId;
                        }
                    }
                    return snapshot;
                }
                catch
                {
                    try { Execute("ROLLBACK;"); } catch { }
                    throw;
                }
            }
        }

        public FileIndexDelta ReplaceSourceSnapshot(
            long rootId,
            string rootPath,
            string provider,
            IEnumerable<System.IO.FileInfo> files)
        {
            return ReplaceSourceSnapshot(
                rootId, rootPath, provider, files, "");
        }

        public FileIndexDelta ReplaceSourceSnapshot(
            long rootId, string rootPath, string provider,
            IEnumerable<System.IO.FileInfo> files, string indexScopeKey)
        { return ReplaceSourceSnapshot(rootId, rootPath, provider, files, indexScopeKey, false); }

        public FileIndexDelta ReplaceSourceSnapshot(
            long rootId, string rootPath, string provider,
            IEnumerable<System.IO.FileInfo> files, string indexScopeKey, bool shallowOnly)
        {
            List<SourceSnapshotItem> incoming = new List<SourceSnapshotItem>();
            foreach (System.IO.FileInfo file in files ?? Enumerable.Empty<System.IO.FileInfo>())
            {
                if (file == null) continue;
                try
                {
                    incoming.Add(new SourceSnapshotItem
                    {
                        FullPath = file.FullName,
                        DirectoryPath = file.DirectoryName ?? "",
                        FileName = file.Name ?? "",
                        Extension = file.Extension ?? "",
                        Size = file.Length,
                        ModifiedUtc = file.LastWriteTimeUtc,
                        CreatedUtc = file.CreationTimeUtc
                    });
                }
                catch { }
            }
            return ReplaceSourceSnapshotItems(
                rootId, rootPath, provider, incoming, indexScopeKey, shallowOnly);
        }

        /// <summary>
        /// Reconciles a source snapshot whose metadata already came from an
        /// index provider (for example Everything). This overload is critical:
        /// it prevents a fast Everything query from degrading into one physical
        /// FileInfo metadata read per file before the delta can be calculated.
        /// </summary>
        public FileIndexDelta ReplaceSourceSnapshot(
            long rootId, string rootPath, string provider,
            IEnumerable<SourceIndexFileSnapshot> files, string indexScopeKey)
        { return ReplaceSourceSnapshot(rootId, rootPath, provider, files, indexScopeKey, false); }

        public FileIndexDelta ReplaceSourceSnapshot(
            long rootId, string rootPath, string provider,
            IEnumerable<SourceIndexFileSnapshot> files, string indexScopeKey, bool shallowOnly)
        {
            List<SourceSnapshotItem> incoming = new List<SourceSnapshotItem>();
            foreach (SourceIndexFileSnapshot file in files ?? Enumerable.Empty<SourceIndexFileSnapshot>())
            {
                if (file == null || String.IsNullOrWhiteSpace(file.FullPath)) continue;
                string fullPath;
                try { fullPath = System.IO.Path.GetFullPath(file.FullPath); }
                catch { fullPath = file.FullPath; }
                incoming.Add(new SourceSnapshotItem
                {
                    FullPath = fullPath,
                    DirectoryPath = !String.IsNullOrWhiteSpace(file.DirectoryPath)
                        ? file.DirectoryPath : (System.IO.Path.GetDirectoryName(fullPath) ?? ""),
                    FileName = !String.IsNullOrWhiteSpace(file.FileName)
                        ? file.FileName : (System.IO.Path.GetFileName(fullPath) ?? ""),
                    Extension = !String.IsNullOrWhiteSpace(file.Extension)
                        ? file.Extension : (System.IO.Path.GetExtension(fullPath) ?? ""),
                    Size = file.FileSize,
                    ModifiedUtc = file.LastWriteTimeUtc,
                    CreatedUtc = file.CreationTimeUtc
                });
            }
            return ReplaceSourceSnapshotItems(
                rootId, rootPath, provider, incoming, indexScopeKey, shallowOnly);
        }

        private FileIndexDelta ReplaceSourceSnapshotItems(
            long rootId,
            string rootPath,
            string provider,
            List<SourceSnapshotItem> incoming,
            string indexScopeKey,
            bool shallowOnly)
        {
            lock (_gate)
            {
                EnsureOpen();
                string scopeKey = NormalizeIndexScopeKey(indexScopeKey);
                Dictionary<string, SourceFileIndexEntry> existing = LoadSourceFiles(rootId);
                HashSet<long> previousScopeFileIds = LoadScopeFileIds(rootId, scopeKey);
                bool scopeKnown = IsScopeKnown(rootId, scopeKey);

                HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (SourceSnapshotItem item in incoming)
                    seen.Add(item.FullPath ?? "");

                // Relocation candidates come from the current scope when that
                // scope has already been synchronized. During migration/new
                // scope discovery we may also reuse a matching durable row from
                // another scope so facts survive a profile/view change.
                Dictionary<string, List<SourceFileIndexEntry>> renameCandidates =
                    new Dictionary<string, List<SourceFileIndexEntry>>(StringComparer.Ordinal);
                foreach (SourceFileIndexEntry oldEntry in existing.Values)
                {
                    // Shallow discovery cannot infer that a missing descendant
                    // was moved or removed: it was never queried.
                    if (shallowOnly && !String.Equals(
                        NormalizePath(oldEntry.DirectoryPath), NormalizePath(rootPath),
                        StringComparison.Ordinal)) continue;
                    if (seen.Contains(oldEntry.FullPath)) continue;
                    if (scopeKnown && !previousScopeFileIds.Contains(oldEntry.FileId))
                        continue;
                    string signature = RenameSignature(
                        oldEntry.FileSize,
                        oldEntry.LastWriteTimeUtc,
                        oldEntry.CreationTimeUtc,
                        oldEntry.Extension);
                    List<SourceFileIndexEntry> matches;
                    if (!renameCandidates.TryGetValue(signature, out matches))
                    {
                        matches = new List<SourceFileIndexEntry>();
                        renameCandidates[signature] = matches;
                    }
                    matches.Add(oldEntry);
                }

                HashSet<long> relocatedFileIds = new HashSet<long>();
                HashSet<long> incomingFileIds = new HashSet<long>();
                FileIndexDelta delta = new FileIndexDelta();
                Execute("BEGIN IMMEDIATE;");
                try
                {
                    // Prepare hot-path statements once per reconciliation. 8k
                    // newly indexed files should not recompile the same SQL
                    // 20k+ times. Reset/clear bindings before every reuse.
                    using (Statement upsert = Prepare(
                        "INSERT INTO SourceFiles(RootId,FullPath,NormalizedPath,RelativePath,DirectoryPath,FileName,Extension,FileSize,LastWriteUtc,CreationUtc) " +
                        "VALUES(?1,?2,?3,?4,?5,?6,?7,?8,?9,?10) " +
                        "ON CONFLICT(RootId,NormalizedPath) DO UPDATE SET " +
                        "FullPath=excluded.FullPath,RelativePath=excluded.RelativePath,DirectoryPath=excluded.DirectoryPath," +
                        "FileName=excluded.FileName,Extension=excluded.Extension,FileSize=excluded.FileSize," +
                        "LastWriteUtc=excluded.LastWriteUtc,CreationUtc=excluded.CreationUtc"))
                    using (Statement findId = Prepare(
                        "SELECT FileId FROM SourceFiles WHERE RootId=?1 AND NormalizedPath=?2"))
                    using (Statement addMembership = Prepare(
                        "INSERT OR IGNORE INTO SourceFileScopes(RootId,ScopeKey,FileId) VALUES(?1,?2,?3)"))
                    {
                    foreach (SourceSnapshotItem item in incoming)
                    {
                        if (item == null || String.IsNullOrWhiteSpace(item.FullPath)) continue;
                        SourceFileIndexEntry previous;
                        long currentFileId = 0;
                        bool rowWritten = false;

                        if (!existing.TryGetValue(item.FullPath, out previous))
                        {
                            string signature = RenameSignature(
                                item.Size,
                                item.ModifiedUtc,
                                item.CreatedUtc,
                                item.Extension);
                            List<SourceFileIndexEntry> candidates;
                            SourceFileIndexEntry relocated = null;
                            if (renameCandidates.TryGetValue(signature, out candidates))
                            {
                                List<SourceFileIndexEntry> available = candidates
                                    .Where(x => !relocatedFileIds.Contains(x.FileId))
                                    .ToList();
                                if (available.Count == 1) relocated = available[0];
                            }

                            if (relocated != null)
                            {
                                string relative = MakeRelative(rootPath, item.FullPath);
                                using (Statement statement = Prepare(
                                    "UPDATE SourceFiles SET FullPath=?1,NormalizedPath=?2,RelativePath=?3,DirectoryPath=?4,FileName=?5,Extension=?6,FileSize=?7,LastWriteUtc=?8,CreationUtc=?9 WHERE FileId=?10"))
                                {
                                    statement.Bind(1, item.FullPath);
                                    statement.Bind(2, NormalizePath(item.FullPath));
                                    statement.Bind(3, relative);
                                    statement.Bind(4, item.DirectoryPath ?? "");
                                    statement.Bind(5, item.FileName ?? "");
                                    statement.Bind(6, item.Extension ?? "");
                                    statement.Bind(7, item.Size);
                                    statement.Bind(8, item.ModifiedUtc.ToString("o"));
                                    statement.Bind(9, item.CreatedUtc.ToString("o"));
                                    statement.Bind(10, relocated.FileId);
                                    statement.StepDone();
                                }
                                currentFileId = relocated.FileId;
                                rowWritten = true;
                                relocatedFileIds.Add(relocated.FileId);
                                bool sameName = String.Equals(
                                    relocated.FileName, item.FileName,
                                    StringComparison.Ordinal);
                                FileIndexPathChange change = new FileIndexPathChange
                                {
                                    Kind = sameName
                                        ? FileIndexPathChangeKind.Moved
                                        : FileIndexPathChangeKind.Renamed,
                                    FileId = relocated.FileId,
                                    OldPath = relocated.FullPath,
                                    NewPath = item.FullPath
                                };
                                delta.PathChanges.Add(change);
                                if (sameName)
                                {
                                    delta.Moved++;
                                }
                                else
                                {
                                    delta.Renamed++;
                                    delta.PlanInvalidatedPaths.Add(relocated.FullPath);
                                    delta.PlanInvalidatedPaths.Add(item.FullPath);
                                    delta.RecognitionInvalidatedPaths.Add(relocated.FullPath);
                                    delta.RecognitionInvalidatedPaths.Add(item.FullPath);
                                    DeleteFactsForFileId(relocated.FileId);
                                }
                                delta.ChangedPaths.Add(relocated.FullPath);
                                delta.ChangedPaths.Add(item.FullPath);
                            }
                            else
                            {
                                delta.Added++;
                                delta.ChangedPaths.Add(item.FullPath);
                            }
                        }
                        else
                        {
                            currentFileId = previous.FileId;
                            if (previous.FileSize != item.Size ||
                                previous.LastWriteTimeUtc != item.ModifiedUtc ||
                                previous.CreationTimeUtc != item.CreatedUtc)
                            {
                                // GuiGui recognition is filename based. Content/
                                // timestamp metadata changes invalidate migration
                                // planning metadata, not author recognition facts.
                                delta.Modified++;
                                delta.ChangedPaths.Add(item.FullPath);
                                delta.PlanInvalidatedPaths.Add(item.FullPath);
                            }
                            else
                            {
                                delta.CacheHits++;
                                // The durable row already represents this exact
                                // provider snapshot. Do not issue an UPDATE for
                                // an unchanged file; this makes a restart-time
                                // Everything reconciliation effectively read-only.
                                rowWritten = true;
                            }
                        }

                        if (!rowWritten)
                        {
                            string relativePath = MakeRelative(rootPath, item.FullPath);
                            upsert.Reset();
                            upsert.Bind(1, rootId);
                            upsert.Bind(2, item.FullPath);
                            upsert.Bind(3, NormalizePath(item.FullPath));
                            upsert.Bind(4, relativePath);
                            upsert.Bind(5, item.DirectoryPath ?? "");
                            upsert.Bind(6, item.FileName ?? "");
                            upsert.Bind(7, item.Extension ?? "");
                            upsert.Bind(8, item.Size);
                            upsert.Bind(9, item.ModifiedUtc.ToString("o"));
                            upsert.Bind(10, item.CreatedUtc.ToString("o"));
                            upsert.StepDone();
                            if (currentFileId <= 0)
                            {
                                findId.Reset();
                                findId.Bind(1, rootId);
                                findId.Bind(2, NormalizePath(item.FullPath));
                                currentFileId = findId.Step() == SqliteRow ? findId.Int64(0) : 0;
                            }
                        }

                        if (currentFileId > 0)
                        {
                            incomingFileIds.Add(currentFileId);
                            if (!scopeKnown || !previousScopeFileIds.Contains(currentFileId))
                            {
                                addMembership.Reset();
                                addMembership.Bind(1, rootId);
                                addMembership.Bind(2, scopeKey);
                                addMembership.Bind(3, currentFileId);
                                addMembership.StepDone();
                            }
                        }
                    }

                    } // dispose prepared reconciliation statements before commit

                    // Only an already-synchronized identical scope is allowed to
                    // declare a previously seen row absent. A different file-type
                    // profile or block-list scope must never delete another
                    // scope's durable facts merely because it cannot see them.
                    if (scopeKnown)
                    {
                        foreach (long oldFileId in previousScopeFileIds)
                        {
                            if (incomingFileIds.Contains(oldFileId) ||
                                relocatedFileIds.Contains(oldFileId))
                                continue;
                            SourceFileIndexEntry oldEntry = existing.Values
                                .FirstOrDefault(x => x.FileId == oldFileId);
                            // First remove only this scope membership. Another
                            // scope (e.g. the recursive index) still owns its
                            // durable facts until THAT scope is refreshed.
                            using (Statement scopeMember = Prepare(
                                "DELETE FROM SourceFileScopes WHERE RootId=?1 AND ScopeKey=?2 AND FileId=?3"))
                            {
                                scopeMember.Bind(1, rootId);
                                scopeMember.Bind(2, scopeKey);
                                scopeMember.Bind(3, oldFileId);
                                scopeMember.StepDone();
                            }
                            bool retainedByOtherScope = false;
                            if (shallowOnly)
                            {
                                using (Statement other = Prepare(
                                    "SELECT 1 FROM SourceFileScopes WHERE FileId=?1 LIMIT 1"))
                                {
                                    other.Bind(1, oldFileId);
                                    retainedByOtherScope = other.Step() == SqliteRow;
                                }
                            }
                            if (retainedByOtherScope) continue;
                            using (Statement statement = Prepare(
                                "DELETE FROM SourceFiles WHERE FileId=?1"))
                            {
                                statement.Bind(1, oldFileId);
                                statement.StepDone();
                            }
                            delta.Removed++;
                            if (oldEntry != null)
                            {
                                delta.ChangedPaths.Add(oldEntry.FullPath);
                                // The durable row and its dependent facts are gone.
                                // Evict the same path from the in-memory caches too,
                                // otherwise a delete/recreate-at-same-path sequence
                                // could reuse stale filename-derived facts.
                                delta.PlanInvalidatedPaths.Add(oldEntry.FullPath);
                                delta.RecognitionInvalidatedPaths.Add(oldEntry.FullPath);
                            }
                        }
                    }

                    using (Statement scope = Prepare(
                        "INSERT INTO SourceScopes(RootId,ScopeKey,LastSyncUtc) VALUES(?1,?2,?3) " +
                        "ON CONFLICT(RootId,ScopeKey) DO UPDATE SET LastSyncUtc=excluded.LastSyncUtc"))
                    {
                        scope.Bind(1, rootId);
                        scope.Bind(2, scopeKey);
                        scope.Bind(3, DateTime.UtcNow.ToString("o"));
                        scope.StepDone();
                    }
                    using (Statement lastScope = Prepare(
                        "UPDATE Roots SET IndexScopeKey=?1 WHERE RootId=?2"))
                    {
                        lastScope.Bind(1, scopeKey);
                        lastScope.Bind(2, rootId);
                        lastScope.StepDone();
                    }
                    UpdateRootState(rootId, provider, "Clean");
                    Execute("COMMIT;");
                    return delta;
                }
                catch
                {
                    try { Execute("ROLLBACK;"); } catch { }
                    throw;
                }
            }
        }

        private HashSet<long> LoadScopeFileIds(long rootId, string scopeKey)
        {
            HashSet<long> result = new HashSet<long>();
            using (Statement statement = Prepare(
                "SELECT FileId FROM SourceFileScopes WHERE RootId=?1 AND ScopeKey=?2"))
            {
                statement.Bind(1, rootId);
                statement.Bind(2, scopeKey);
                while (statement.Step() == SqliteRow)
                    result.Add(statement.Int64(0));
            }
            return result;
        }

        private bool IsScopeKnown(long rootId, string scopeKey)
        {
            using (Statement statement = Prepare(
                "SELECT 1 FROM SourceScopes WHERE RootId=?1 AND ScopeKey=?2 LIMIT 1"))
            {
                statement.Bind(1, rootId);
                statement.Bind(2, scopeKey);
                return statement.Step() == SqliteRow;
            }
        }


        private static string NormalizeIndexScopeKey(string value)
        {
            return String.IsNullOrWhiteSpace(value) ? "__DEFAULT__" : value.Trim();
        }

        private sealed class SourceSnapshotItem
        {
            public string FullPath = "";
            public string DirectoryPath = "";
            public string FileName = "";
            public string Extension = "";
            public long Size;
            public DateTime ModifiedUtc;
            public DateTime CreatedUtc;
        }

        private static string RenameSignature(
            long size,
            DateTime modifiedUtc,
            DateTime createdUtc,
            string extension)
        {
            return size.ToString(System.Globalization.CultureInfo.InvariantCulture) + "|" +
                modifiedUtc.Ticks.ToString(System.Globalization.CultureInfo.InvariantCulture) + "|" +
                createdUtc.Ticks.ToString(System.Globalization.CultureInfo.InvariantCulture) + "|" +
                (extension ?? "").ToUpperInvariant();
        }

        public void ApplySuccessfulMove(string sourcePath, string targetPath)
        {
            lock (_gate)
            {
                EnsureOpen();
                long fileId = 0;
                string rootPath = "";
                string oldFileName = "";
                using (Statement lookup = Prepare(
                    "SELECT f.FileId,r.RootPath,f.FileName FROM SourceFiles f " +
                    "INNER JOIN Roots r ON r.RootId=f.RootId WHERE f.NormalizedPath=?1"))
                {
                    lookup.Bind(1, NormalizePath(sourcePath));
                    if (lookup.Step() == SqliteRow)
                    {
                        fileId = lookup.Int64(0);
                        rootPath = lookup.Text(1);
                        oldFileName = lookup.Text(2);
                    }
                }

                Execute("BEGIN IMMEDIATE;");
                try
                {
                    bool staysInSource = fileId > 0 && IsPathUnderRoot(targetPath, rootPath) &&
                        System.IO.File.Exists(targetPath);
                    if (staysInSource)
                    {
                        System.IO.FileInfo file = new System.IO.FileInfo(targetPath);
                        UpdateSourceFile(fileId, rootPath, file);
                        if (!String.Equals(oldFileName, file.Name, StringComparison.Ordinal))
                        {
                            DeleteFactsForFileId(fileId);
                        }
                    }
                    else
                    {
                        using (Statement statement = Prepare("DELETE FROM SourceFiles WHERE NormalizedPath=?1"))
                        {
                            statement.Bind(1, NormalizePath(sourcePath));
                            statement.StepDone();
                        }
                    }

                    using (Statement statement = Prepare(
                        "INSERT INTO PendingChanges(ChangeKind,OldPath,NewPath,CreatedUtc) VALUES('Move',?1,?2,?3)"))
                    {
                        statement.Bind(1, sourcePath ?? "");
                        statement.Bind(2, targetPath ?? "");
                        statement.Bind(3, DateTime.UtcNow.ToString("o"));
                        statement.StepDone();
                    }
                    Execute("COMMIT;");
                }
                catch
                {
                    try { Execute("ROLLBACK;"); } catch { }
                    throw;
                }
            }
        }

        private void UpdateSourceFile(long fileId, string rootPath, System.IO.FileInfo file)
        {
            using (Statement statement = Prepare(
                "UPDATE SourceFiles SET FullPath=?1,NormalizedPath=?2,RelativePath=?3," +
                "DirectoryPath=?4,FileName=?5,Extension=?6,FileSize=?7," +
                "LastWriteUtc=?8,CreationUtc=?9 WHERE FileId=?10"))
            {
                statement.Bind(1, file.FullName);
                statement.Bind(2, NormalizePath(file.FullName));
                statement.Bind(3, MakeRelative(rootPath, file.FullName));
                statement.Bind(4, file.DirectoryName ?? "");
                statement.Bind(5, file.Name ?? "");
                statement.Bind(6, file.Extension ?? "");
                statement.Bind(7, file.Length);
                statement.Bind(8, file.LastWriteTimeUtc.ToString("o"));
                statement.Bind(9, file.CreationTimeUtc.ToString("o"));
                statement.Bind(10, fileId);
                statement.StepDone();
            }
        }

        private void DeleteFactsForFileId(long fileId)
        {
            foreach (string table in new[] { "ParsedMetadata", "RecognitionCache", "MigrationPlanCache" })
            {
                using (Statement statement = Prepare("DELETE FROM " + table + " WHERE FileId=?1"))
                {
                    statement.Bind(1, fileId);
                    statement.StepDone();
                }
            }
        }

        private static bool IsPathUnderRoot(string path, string rootPath)
        {
            if (String.IsNullOrWhiteSpace(path) || String.IsNullOrWhiteSpace(rootPath)) return false;
            try
            {
                string root = Path.GetFullPath(rootPath).TrimEnd('\\', '/');
                string value = Path.GetFullPath(path);
                return value.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
            }
            catch { return false; }
        }

        public void ApplySuccessfulRename(string oldPath, string newPath)
        {
            lock (_gate)
            {
                EnsureOpen();
                System.IO.FileInfo file = new System.IO.FileInfo(newPath);
                if (!file.Exists) throw new System.IO.FileNotFoundException(newPath);
                long fileId = 0;
                string rootPath = "";
                using (Statement statement = Prepare(
                    "SELECT f.FileId,r.RootPath FROM SourceFiles f " +
                    "INNER JOIN Roots r ON r.RootId=f.RootId WHERE f.NormalizedPath=?1"))
                {
                    statement.Bind(1, NormalizePath(oldPath));
                    if (statement.Step() == SqliteRow)
                    {
                        fileId = statement.Int64(0);
                        rootPath = statement.Text(1);
                    }
                }
                if (fileId <= 0) return;

                Execute("BEGIN IMMEDIATE;");
                try
                {
                    UpdateSourceFile(fileId, rootPath, file);
                    using (Statement statement = Prepare(
                        "DELETE FROM ParsedMetadata WHERE FileId=?1;"))
                    {
                        statement.Bind(1, fileId);
                        statement.StepDone();
                    }
                    using (Statement statement = Prepare(
                        "DELETE FROM RecognitionCache WHERE FileId=?1;"))
                    {
                        statement.Bind(1, fileId);
                        statement.StepDone();
                    }
                    using (Statement statement = Prepare(
                        "DELETE FROM MigrationPlanCache WHERE FileId=?1;"))
                    {
                        statement.Bind(1, fileId);
                        statement.StepDone();
                    }
                    using (Statement statement = Prepare(
                        "INSERT INTO PendingChanges(ChangeKind,OldPath,NewPath,CreatedUtc) " +
                        "VALUES('Rename',?1,?2,?3)"))
                    {
                        statement.Bind(1, oldPath ?? "");
                        statement.Bind(2, newPath ?? "");
                        statement.Bind(3, DateTime.UtcNow.ToString("o"));
                        statement.StepDone();
                    }
                    Execute("COMMIT;");
                }
                catch
                {
                    try { Execute("ROLLBACK;"); } catch { }
                    throw;
                }
            }
        }

        public Dictionary<string, ParsedMetadataCacheEntry> LoadParsedMetadata()
        {
            Dictionary<string, ParsedMetadataCacheEntry> result =
                new Dictionary<string, ParsedMetadataCacheEntry>(StringComparer.OrdinalIgnoreCase);
            lock (_gate)
            {
                EnsureOpen();
                using (Statement statement = Prepare(
                    "SELECT s.FileId,s.FullPath,p.AuthorCandidate,p.GroupCandidate,p.Title,p.EventName," +
                    "p.DetectedTags,p.NormalizedFileName,p.ParserVersion,p.TagCleaningVersion,p.InputFingerprint " +
                    "FROM ParsedMetadata p INNER JOIN SourceFiles s ON s.FileId=p.FileId"))
                {
                    while (statement.Step() == SqliteRow)
                    {
                        ParsedMetadataCacheEntry entry = new ParsedMetadataCacheEntry();
                        entry.FileId = statement.Int64(0);
                        entry.FullPath = statement.Text(1);
                        entry.AuthorCandidate = statement.Text(2);
                        entry.GroupCandidate = statement.Text(3);
                        entry.Title = statement.Text(4);
                        entry.EventName = statement.Text(5);
                        entry.DetectedTags = statement.Text(6);
                        entry.NormalizedFileName = statement.Text(7);
                        entry.ParserVersion = statement.Text(8);
                        entry.TagCleaningVersion = statement.Text(9);
                        entry.InputFingerprint = statement.Text(10);
                        result[entry.FullPath] = entry;
                    }
                }
            }
            return result;
        }

        public void DeleteParsedMetadata(string fullPath)
        {
            DeleteFileFact("ParsedMetadata", fullPath);
        }

        public void DeleteRecognitionFact(string fullPath)
        {
            DeleteFileFact("RecognitionCache", fullPath);
        }

        public void DeleteMigrationPlans(string fullPath)
        {
            DeleteFileFact("MigrationPlanCache", fullPath);
        }

        private void DeleteFileFact(string table, string fullPath)
        {
            if (table != "ParsedMetadata" && table != "RecognitionCache" &&
                table != "MigrationPlanCache")
                throw new ArgumentException("Unsupported fact table.", "table");
            lock (_gate)
            {
                EnsureOpen();
                using (Statement statement = Prepare(
                    "DELETE FROM " + table + " WHERE FileId IN " +
                    "(SELECT FileId FROM SourceFiles WHERE NormalizedPath=?1)"))
                {
                    statement.Bind(1, NormalizePath(fullPath));
                    statement.StepDone();
                }
            }
        }

        public void SaveDisplayedSession(ScanSessionSnapshot session)
        {
            if (session != null && session.SessionId > 0)
                SetMetadata("DisplayedSession", session.SessionId.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        public ScanSessionSnapshot LoadDisplayedSession()
        {
            lock (_gate)
            {
                EnsureOpen();
                long id;
                if (!Int64.TryParse(ScalarText("SELECT Value FROM CacheMetadata WHERE Key='DisplayedSession'"), out id)) return null;
                using (Statement statement = Prepare("SELECT RootId FROM ScanSessions WHERE SessionId=?1 AND State='Complete'"))
                {
                    statement.Bind(1, id);
                    return statement.Step() == SqliteRow ? new ScanSessionSnapshot { SessionId = id, RootId = statement.Int64(0) } : null;
                }
            }
        }

        public List<PlanItem> LoadMigrationPlans(
            string configKey,
            ScanSessionSnapshot session,
            Func<string, string> identityDependency = null, Func<bool> cancelRequested = null)
        {
            List<PlanItem> result = new List<PlanItem>();
            if (session == null || session.SessionId <= 0) return result;
            lock (_gate)
            {
                EnsureOpen();
                using (Statement statement = Prepare(
                    "SELECT p.FileId,s.FileName,s.FullPath,p.Author,p.MatchedAs,p.MatchWhy," +
                    "p.TargetDir,p.TargetPath,p.Status,p.CanMove,p.FileSize,p.LastWriteTime," +
                    "p.RecognitionScore,p.RunnerUpScore,p.StatusCode,p.EvidenceKind,p.StatusArgument," +
                    "p.CandidatePaths,p.CandidateNames,p.CandidatePlanned,p.ConfigKey,p.ManualTargetDir,p.ManualTargetName,p.ManualTargetAuthor " +
                    "FROM MigrationPlanCache p INNER JOIN SourceFiles s ON s.FileId=p.FileId " +
                    "INNER JOIN ScanSessionFiles sf ON sf.FileId=p.FileId " +
                    "WHERE sf.SessionId=?1 AND " + (identityDependency == null ? "p.ConfigKey=?2" :
                        "substr(p.ConfigKey,1,length(?2)+length('|AuthorDependency='))=?2||'|AuthorDependency='")))
                {
                    statement.Bind(1, session.SessionId);
                    statement.Bind(2, configKey ?? "");
                    while (statement.Step() == SqliteRow)
                    {
                        if (cancelRequested != null && cancelRequested()) throw new OperationCanceledException();
                        if (identityDependency != null && statement.Text(20) != (configKey ?? "") + "|AuthorDependency=" + identityDependency(statement.Text(1))) continue;
                        PlanStatusCode statusCode;
                        RecognitionEvidenceKind evidenceKind;
                        Enum.TryParse<PlanStatusCode>(statement.Text(14), out statusCode);
                        Enum.TryParse<RecognitionEvidenceKind>(statement.Text(15), out evidenceKind);
                        DateTime modified;
                        if (!DateTime.TryParse(
                            statement.Text(11), null,
                            System.Globalization.DateTimeStyles.RoundtripKind,
                            out modified)) modified = DateTime.MinValue;
                        PlanItem item = new PlanItem
                        {
                            FileId = statement.Int64(0),
                            FileName = statement.Text(1),
                            SourcePath = statement.Text(2),
                            Author = statement.Text(3),
                            MatchedAs = statement.Text(4),
                            MatchWhy = statement.Text(5),
                            TargetDir = statement.Text(6),
                            TargetPath = statement.Text(7),
                            Status = statement.Text(8),
                            CanMove = statement.Int64(9) != 0,
                            FileSize = statement.Int64(10),
                            LastWriteTime = modified,
                            RecognitionScore = (int)statement.Int64(12),
                            RecognitionRunnerUpScore = (int)statement.Int64(13),
                            StatusCode = statusCode,
                            EvidenceKind = evidenceKind,
                            StatusArgument = statement.Text(16)
                        };
                        item.CandidatePaths = SplitPlanList(statement.Text(17));
                        item.CandidateNames = SplitPlanList(statement.Text(18));
                        item.CandidateIsPlanned = SplitPlanBools(statement.Text(19));
                        item.ManualTargetDir = statement.Text(21);
                        item.ManualTargetName = statement.Text(22);
                        item.ManualTargetAuthor = statement.Text(23);
                        result.Add(item);
                    }
                }
            }
            return result;
        }

        // Used by the scan completion handler: a fully cached run must be
        // read-only, regardless of how many rows appear in its visible plan.
        internal static bool HasNewMigrationPlans(IEnumerable<PlanItem> items)
        {
            return items != null && items.Any(delegate(PlanItem item) {
                return item != null && item.FileId > 0;
            });
        }

        public void UpsertMigrationPlans(string configKey, IEnumerable<PlanItem> items, Func<string, string> identityDependency = null, Func<bool> cancelRequested = null)
        {
            List<PlanItem> batch = (items ?? Enumerable.Empty<PlanItem>())
                .Where(delegate(PlanItem item) { return item != null && item.FileId > 0; })
                .ToList();
            if (batch.Count == 0) return;
            lock (_gate)
            {
                EnsureOpen();
                Execute("BEGIN IMMEDIATE;");
                try
                {
                    using (Statement statement = Prepare(
                            "INSERT OR REPLACE INTO MigrationPlanCache(" +
                            "FileId,ConfigKey,Author,MatchedAs,MatchWhy,TargetDir,TargetPath,Status,CanMove," +
                            "FileSize,LastWriteTime,RecognitionScore,RunnerUpScore,StatusCode,EvidenceKind," +
                            "StatusArgument,CandidatePaths,CandidateNames,CandidatePlanned,UpdatedUtc,ManualTargetDir,ManualTargetName,ManualTargetAuthor) " +
                            "VALUES(?1,?2,?3,?4,?5,?6,?7,?8,?9,?10,?11,?12,?13,?14,?15,?16,?17,?18,?19,?20,?21,?22,?23)"))
                    {
                        foreach (PlanItem item in batch)
                        {
                            if (cancelRequested != null && cancelRequested()) throw new OperationCanceledException();
                            statement.Bind(1, item.FileId); statement.Bind(2, (configKey ?? "") +
                                (identityDependency == null ? "" : "|AuthorDependency=" + identityDependency(item.FileName)));
                            statement.Bind(3, item.Author ?? ""); statement.Bind(4, item.MatchedAs ?? "");
                            statement.Bind(5, item.MatchWhy ?? ""); statement.Bind(6, item.TargetDir ?? "");
                            statement.Bind(7, item.TargetPath ?? ""); statement.Bind(8, item.Status ?? "");
                            statement.Bind(9, item.CanMove ? 1 : 0); statement.Bind(10, item.FileSize);
                            statement.Bind(11, item.LastWriteTime.ToString("o"));
                            statement.Bind(12, item.RecognitionScore);
                            statement.Bind(13, item.RecognitionRunnerUpScore);
                            statement.Bind(14, item.StatusCode.ToString());
                            statement.Bind(15, item.EvidenceKind.ToString());
                            statement.Bind(16, item.StatusArgument ?? "");
                            statement.Bind(17, JoinPlanList(item.CandidatePaths));
                            statement.Bind(18, JoinPlanList(item.CandidateNames));
                            statement.Bind(19, String.Join(",", (item.CandidateIsPlanned ?? new List<bool>())
                                .Select(delegate(bool value) { return value ? "1" : "0"; })));
                            statement.Bind(20, DateTime.UtcNow.ToString("o"));
                            statement.Bind(21, item.ManualTargetDir ?? "");
                            statement.Bind(22, item.ManualTargetName ?? "");
                            statement.Bind(23, item.ManualTargetAuthor ?? "");
                            statement.StepDone();
                            statement.Reset();
                        }
                    }
                    Execute("COMMIT;");
                }
                catch
                {
                    try { Execute("ROLLBACK;"); } catch { }
                    throw;
                }
            }
        }

        private static string JoinPlanList(IEnumerable<string> values)
        {
            return String.Join("\u001f", values ?? Enumerable.Empty<string>());
        }

        private static List<string> SplitPlanList(string value)
        {
            return String.IsNullOrEmpty(value)
                ? new List<string>()
                : value.Split(new[] { '\u001f' }, StringSplitOptions.None).ToList();
        }

        private static List<bool> SplitPlanBools(string value)
        {
            return String.IsNullOrEmpty(value)
                ? new List<bool>()
                : value.Split(',').Select(delegate(string part) { return part == "1"; }).ToList();
        }

        public void UpsertParsedMetadata(IEnumerable<ParsedMetadataCacheEntry> entries)
        {
            List<ParsedMetadataCacheEntry> batch = (entries ?? Enumerable.Empty<ParsedMetadataCacheEntry>()).ToList();
            if (batch.Count == 0) return;
            lock (_gate)
            {
                EnsureOpen();
                Execute("BEGIN IMMEDIATE;");
                try
                {
                    foreach (ParsedMetadataCacheEntry entry in batch)
                    {
                        if (entry == null || String.IsNullOrWhiteSpace(entry.FullPath)) continue;
                        using (Statement statement = Prepare(
                            "INSERT OR REPLACE INTO ParsedMetadata(" +
                            "FileId,AuthorCandidate,GroupCandidate,Title,EventName,DetectedTags,NormalizedFileName," +
                            "ParserVersion,TagCleaningVersion,InputFingerprint) " +
                            "SELECT FileId,?1,?2,?3,?4,?5,?6,?7,?8,?9 FROM SourceFiles WHERE NormalizedPath=?10"))
                        {
                            statement.Bind(1, entry.AuthorCandidate);
                            statement.Bind(2, entry.GroupCandidate);
                            statement.Bind(3, entry.Title);
                            statement.Bind(4, entry.EventName);
                            statement.Bind(5, entry.DetectedTags);
                            statement.Bind(6, entry.NormalizedFileName);
                            statement.Bind(7, entry.ParserVersion);
                            statement.Bind(8, entry.TagCleaningVersion);
                            statement.Bind(9, entry.InputFingerprint);
                            statement.Bind(10, NormalizePath(entry.FullPath));
                            statement.StepDone();
                        }
                    }
                    Execute("COMMIT;");
                }
                catch
                {
                    try { Execute("ROLLBACK;"); } catch { }
                    throw;
                }
            }
        }

        public Dictionary<string, RecognitionCacheEntry> LoadRecognitionCache()
        {
            Dictionary<string, RecognitionCacheEntry> result =
                new Dictionary<string, RecognitionCacheEntry>(StringComparer.OrdinalIgnoreCase);
            lock (_gate)
            {
                EnsureOpen();
                using (Statement statement = Prepare(
                    "SELECT s.FileId,s.FullPath,r.EntityId,r.MatchedAuthor,r.MatchedGroup,r.Confidence," +
                    "r.RecognitionStatus,r.RecognitionSource,r.RecognitionReason,r.AliasVersion,r.EntityVersion," +
                    "r.RecognitionRuleVersion,r.ExclusionRuleVersion,r.LocalDatabaseVersion,r.InputFingerprint," +
                    "r.EvidenceKind,r.RunnerUpConfidence,r.UpdatedUtc " +
                    "FROM RecognitionCache r INNER JOIN SourceFiles s ON s.FileId=r.FileId"))
                {
                    while (statement.Step() == SqliteRow)
                    {
                        RecognitionCacheEntry entry = new RecognitionCacheEntry();
                        entry.FileId = statement.Int64(0);
                        entry.FullPath = statement.Text(1);
                        entry.EntityId = (int)statement.Int64(2);
                        entry.MatchedAuthor = statement.Text(3);
                        entry.MatchedGroup = statement.Text(4);
                        entry.Confidence = (int)statement.Int64(5);
                        entry.RecognitionStatus = statement.Text(6);
                        entry.RecognitionSource = statement.Text(7);
                        entry.RecognitionReason = statement.Text(8);
                        entry.AliasVersion = statement.Text(9);
                        entry.EntityVersion = statement.Text(10);
                        entry.RecognitionRuleVersion = statement.Text(11);
                        entry.ExclusionRuleVersion = statement.Text(12);
                        entry.LocalDatabaseVersion = statement.Text(13);
                        entry.InputFingerprint = statement.Text(14);
                        entry.EvidenceKind = statement.Text(15);
                        entry.RunnerUpConfidence = (int)statement.Int64(16);
                        entry.UpdatedUtc = statement.Text(17);
                        result[entry.FullPath] = entry;
                    }
                }
            }
            return result;
        }

        public void UpsertRecognitionCache(IEnumerable<RecognitionCacheEntry> entries)
        {
            List<RecognitionCacheEntry> batch = (entries ?? Enumerable.Empty<RecognitionCacheEntry>()).ToList();
            if (batch.Count == 0) return;
            lock (_gate)
            {
                EnsureOpen();
                Execute("BEGIN IMMEDIATE;");
                try
                {
                    foreach (RecognitionCacheEntry entry in batch)
                    {
                        if (entry == null || String.IsNullOrWhiteSpace(entry.FullPath)) continue;
                        using (Statement statement = Prepare(
                            "INSERT OR REPLACE INTO RecognitionCache(" +
                            "FileId,EntityId,MatchedAuthor,MatchedGroup,Confidence,RecognitionStatus,RecognitionSource," +
                            "RecognitionReason,AliasVersion,EntityVersion,RecognitionRuleVersion,ExclusionRuleVersion," +
                            "LocalDatabaseVersion,InputFingerprint,EvidenceKind,RunnerUpConfidence,UpdatedUtc) " +
                            "SELECT FileId,?1,?2,?3,?4,?5,?6,?7,?8,?9,?10,?11,?12,?13,?14,?15,?16 " +
                            "FROM SourceFiles WHERE NormalizedPath=?17"))
                        {
                            statement.Bind(1, entry.EntityId);
                            statement.Bind(2, entry.MatchedAuthor);
                            statement.Bind(3, entry.MatchedGroup);
                            statement.Bind(4, entry.Confidence);
                            statement.Bind(5, entry.RecognitionStatus);
                            statement.Bind(6, entry.RecognitionSource);
                            statement.Bind(7, entry.RecognitionReason);
                            statement.Bind(8, entry.AliasVersion);
                            statement.Bind(9, entry.EntityVersion);
                            statement.Bind(10, entry.RecognitionRuleVersion);
                            statement.Bind(11, entry.ExclusionRuleVersion);
                            statement.Bind(12, entry.LocalDatabaseVersion);
                            statement.Bind(13, entry.InputFingerprint);
                            statement.Bind(14, entry.EvidenceKind);
                            statement.Bind(15, entry.RunnerUpConfidence);
                            statement.Bind(16, entry.UpdatedUtc);
                            statement.Bind(17, NormalizePath(entry.FullPath));
                            statement.StepDone();
                        }
                    }
                    Execute("COMMIT;");
                }
                catch
                {
                    try { Execute("ROLLBACK;"); } catch { }
                    throw;
                }
            }
        }

        public List<DestinationFolderIndexEntry> LoadDestinationFolders(string rootPath, string stamp)
        {
            List<DestinationFolderIndexEntry> result = new List<DestinationFolderIndexEntry>();
            lock (_gate)
            {
                EnsureOpen();
                string normalizedRoot = NormalizeRoot(rootPath);
                long rootId = 0;
                using (Statement root = Prepare("SELECT RootId FROM Roots WHERE Kind='Destination' AND NormalizedPath=?1"))
                {
                    root.Bind(1, normalizedRoot);
                    if (root.Step() == SqliteRow) rootId = root.Int64(0);
                }
                if (rootId == 0) return result;
                string savedStamp = ScalarText("SELECT Value FROM CacheMetadata WHERE Key='DestinationStamp:" + rootId.ToString() + "'");
                if (!String.Equals(savedStamp, stamp ?? "", StringComparison.Ordinal)) return result;
                using (Statement statement = Prepare(
                    "SELECT FolderId,RootId,ParentFolderId,FullPath,RelativePath,FolderName,NormalizedFolderName," +
                    "GroupNumber,LogicalAuthor,IsGroup FROM DestinationFolders WHERE RootId=?1 ORDER BY IsGroup DESC,FullPath"))
                {
                    statement.Bind(1, rootId);
                    while (statement.Step() == SqliteRow)
                    {
                        result.Add(new DestinationFolderIndexEntry
                        {
                            FolderId = statement.Int64(0),
                            RootId = statement.Int64(1),
                            ParentFolderId = statement.Int64(2),
                            FullPath = statement.Text(3),
                            RelativePath = statement.Text(4),
                            FolderName = statement.Text(5),
                            NormalizedFolderName = statement.Text(6),
                            GroupNumber = (int)statement.Int64(7),
                            LogicalAuthor = statement.Text(8),
                            IsGroup = statement.Int64(9) != 0
                        });
                    }
                }
            }
            return result;
        }

        public void ReplaceDestinationFolders(
            string rootPath,
            string stamp,
            IEnumerable<KeyValuePair<int, string>> groups,
            IEnumerable<AuthorFolder> authors)
        {
            lock (_gate)
            {
                EnsureOpen();
                long rootId = EnsureRoot("Destination", rootPath, "DirectoryIndex");
                Execute("BEGIN IMMEDIATE;");
                try
                {
                    using (Statement clear = Prepare("DELETE FROM DestinationFolders WHERE RootId=?1"))
                    {
                        clear.Bind(1, rootId);
                        clear.StepDone();
                    }
                    Dictionary<int, long> groupIds = new Dictionary<int, long>();
                    foreach (KeyValuePair<int, string> group in groups ?? Enumerable.Empty<KeyValuePair<int, string>>())
                    {
                        string name = System.IO.Path.GetFileName(group.Value.TrimEnd('\\', '/'));
                        using (Statement insert = Prepare(
                            "INSERT INTO DestinationFolders(RootId,ParentFolderId,FullPath,NormalizedPath,RelativePath," +
                            "FolderName,NormalizedFolderName,GroupNumber,LogicalAuthor,IsGroup) " +
                            "VALUES(?1,0,?2,?3,?4,?5,?6,?7,'',1)"))
                        {
                            insert.Bind(1, rootId); insert.Bind(2, group.Value); insert.Bind(3, NormalizePath(group.Value));
                            insert.Bind(4, MakeRelative(rootPath, group.Value)); insert.Bind(5, name);
                            insert.Bind(6, name.ToUpperInvariant()); insert.Bind(7, group.Key); insert.StepDone();
                        }
                        groupIds[group.Key] = Int64.Parse(ScalarText("SELECT last_insert_rowid()"));
                    }
                    foreach (AuthorFolder author in authors ?? Enumerable.Empty<AuthorFolder>())
                    {
                        long parentId;
                        if (author == null || !groupIds.TryGetValue(author.GroupNumber, out parentId)) continue;
                        string physicalName = System.IO.Path.GetFileName((author.AuthorPath ?? "").TrimEnd('\\', '/'));
                        using (Statement insert = Prepare(
                            "INSERT INTO DestinationFolders(RootId,ParentFolderId,FullPath,NormalizedPath,RelativePath," +
                            "FolderName,NormalizedFolderName,GroupNumber,LogicalAuthor,IsGroup) " +
                            "VALUES(?1,?2,?3,?4,?5,?6,?7,?8,?9,0)"))
                        {
                            insert.Bind(1, rootId); insert.Bind(2, parentId); insert.Bind(3, author.AuthorPath);
                            insert.Bind(4, NormalizePath(author.AuthorPath)); insert.Bind(5, MakeRelative(rootPath, author.AuthorPath));
                            insert.Bind(6, physicalName); insert.Bind(7, physicalName.ToUpperInvariant());
                            insert.Bind(8, author.GroupNumber); insert.Bind(9, author.AuthorName); insert.StepDone();
                        }
                    }
                    using (Statement meta = Prepare("INSERT OR REPLACE INTO CacheMetadata(Key,Value) VALUES(?1,?2)"))
                    {
                        meta.Bind(1, "DestinationStamp:" + rootId.ToString());
                        meta.Bind(2, stamp ?? "");
                        meta.StepDone();
                    }
                    UpdateRootState(rootId, "DirectoryIndex", "Clean");
                    Execute("COMMIT;");
                }
                catch
                {
                    try { Execute("ROLLBACK;"); } catch { }
                    throw;
                }
            }
        }

        public void InvalidateDestination(string rootPath)
        {
            lock (_gate)
            {
                EnsureOpen();
                using (Statement statement = Prepare(
                    "UPDATE Roots SET State='NeedsValidation' WHERE Kind='Destination' AND NormalizedPath=?1"))
                {
                    statement.Bind(1, NormalizeRoot(rootPath));
                    statement.StepDone();
                }
            }
        }

        public void Dispose()
        {
            lock (_gate)
            {
                _disposed = true;
                if (_db == IntPtr.Zero) return;
                Native.sqlite3_close(_db);
                _db = IntPtr.Zero;
            }
        }

        private void EnsureSchema()
        {
            Execute(
                "CREATE TABLE IF NOT EXISTS CacheMetadata(Key TEXT PRIMARY KEY,Value TEXT NOT NULL);" +
                "CREATE TABLE IF NOT EXISTS Roots(RootId INTEGER PRIMARY KEY AUTOINCREMENT,Kind TEXT NOT NULL,RootPath TEXT NOT NULL,NormalizedPath TEXT NOT NULL,Provider TEXT NOT NULL DEFAULT '',State TEXT NOT NULL DEFAULT 'NeedsValidation',LastSyncUtc TEXT NOT NULL DEFAULT '',IndexScopeKey TEXT NOT NULL DEFAULT '',UNIQUE(Kind,NormalizedPath));" +
                "CREATE TABLE IF NOT EXISTS SourceFiles(FileId INTEGER PRIMARY KEY AUTOINCREMENT,RootId INTEGER NOT NULL,FullPath TEXT NOT NULL,NormalizedPath TEXT NOT NULL,RelativePath TEXT NOT NULL,DirectoryPath TEXT NOT NULL,FileName TEXT NOT NULL,Extension TEXT NOT NULL,FileSize INTEGER NOT NULL,LastWriteUtc TEXT NOT NULL,CreationUtc TEXT NOT NULL,UNIQUE(RootId,NormalizedPath),FOREIGN KEY(RootId) REFERENCES Roots(RootId) ON DELETE CASCADE);" +
                "CREATE INDEX IF NOT EXISTS IX_SourceFiles_Path ON SourceFiles(NormalizedPath);" +
                "CREATE INDEX IF NOT EXISTS IX_SourceFiles_Root_Directory ON SourceFiles(RootId,DirectoryPath);" +
                "CREATE INDEX IF NOT EXISTS IX_SourceFiles_Root_FileName ON SourceFiles(RootId,FileName);" +
                "CREATE TABLE IF NOT EXISTS SourceScopes(RootId INTEGER NOT NULL,ScopeKey TEXT NOT NULL,LastSyncUtc TEXT NOT NULL DEFAULT '',PRIMARY KEY(RootId,ScopeKey),FOREIGN KEY(RootId) REFERENCES Roots(RootId) ON DELETE CASCADE);" +
                "CREATE TABLE IF NOT EXISTS SourceFileScopes(RootId INTEGER NOT NULL,ScopeKey TEXT NOT NULL,FileId INTEGER NOT NULL,PRIMARY KEY(RootId,ScopeKey,FileId),FOREIGN KEY(RootId) REFERENCES Roots(RootId) ON DELETE CASCADE,FOREIGN KEY(FileId) REFERENCES SourceFiles(FileId) ON DELETE CASCADE);" +
                "CREATE INDEX IF NOT EXISTS IX_SourceFileScopes_File ON SourceFileScopes(FileId);" +
                "CREATE TABLE IF NOT EXISTS ScanSessions(SessionId INTEGER PRIMARY KEY AUTOINCREMENT,RootId INTEGER NOT NULL,CreatedUtc TEXT NOT NULL,State TEXT NOT NULL,FOREIGN KEY(RootId) REFERENCES Roots(RootId) ON DELETE CASCADE);" +
                "CREATE TABLE IF NOT EXISTS ScanSessionFiles(SessionId INTEGER NOT NULL,FileId INTEGER NOT NULL,PRIMARY KEY(SessionId,FileId),FOREIGN KEY(SessionId) REFERENCES ScanSessions(SessionId) ON DELETE CASCADE,FOREIGN KEY(FileId) REFERENCES SourceFiles(FileId) ON DELETE CASCADE);" +
                "CREATE INDEX IF NOT EXISTS IX_ScanSessionFiles_File ON ScanSessionFiles(FileId);" +
                "CREATE TABLE IF NOT EXISTS ParsedMetadata(FileId INTEGER PRIMARY KEY,AuthorCandidate TEXT,GroupCandidate TEXT,Title TEXT,EventName TEXT,DetectedTags TEXT,NormalizedFileName TEXT,ParserVersion TEXT,TagCleaningVersion TEXT,InputFingerprint TEXT NOT NULL DEFAULT '',FOREIGN KEY(FileId) REFERENCES SourceFiles(FileId) ON DELETE CASCADE);" +
                "CREATE TABLE IF NOT EXISTS RecognitionCache(FileId INTEGER PRIMARY KEY,EntityId INTEGER,MatchedAuthor TEXT,MatchedGroup TEXT,Confidence INTEGER,RecognitionStatus TEXT,RecognitionSource TEXT,RecognitionReason TEXT,AliasVersion TEXT,EntityVersion TEXT,RecognitionRuleVersion TEXT,ExclusionRuleVersion TEXT,LocalDatabaseVersion TEXT,InputFingerprint TEXT NOT NULL DEFAULT '',EvidenceKind TEXT NOT NULL DEFAULT '',RunnerUpConfidence INTEGER NOT NULL DEFAULT 0,UpdatedUtc TEXT NOT NULL DEFAULT '',FOREIGN KEY(FileId) REFERENCES SourceFiles(FileId) ON DELETE CASCADE);" +
                "CREATE TABLE IF NOT EXISTS MigrationPlanCache(FileId INTEGER NOT NULL,ConfigKey TEXT NOT NULL,Author TEXT,MatchedAs TEXT,MatchWhy TEXT,TargetDir TEXT,TargetPath TEXT,Status TEXT,CanMove INTEGER NOT NULL DEFAULT 0,FileSize INTEGER NOT NULL DEFAULT -1,LastWriteTime TEXT NOT NULL DEFAULT '',RecognitionScore INTEGER NOT NULL DEFAULT 0,RunnerUpScore INTEGER NOT NULL DEFAULT 0,StatusCode TEXT,EvidenceKind TEXT,StatusArgument TEXT,CandidatePaths TEXT,CandidateNames TEXT,CandidatePlanned TEXT,UpdatedUtc TEXT NOT NULL DEFAULT '',PRIMARY KEY(FileId,ConfigKey),FOREIGN KEY(FileId) REFERENCES SourceFiles(FileId) ON DELETE CASCADE);" +
                "CREATE TABLE IF NOT EXISTS DestinationFolders(FolderId INTEGER PRIMARY KEY AUTOINCREMENT,RootId INTEGER NOT NULL,ParentFolderId INTEGER,FullPath TEXT NOT NULL,NormalizedPath TEXT NOT NULL,RelativePath TEXT NOT NULL,FolderName TEXT NOT NULL,NormalizedFolderName TEXT NOT NULL,GroupNumber INTEGER NOT NULL DEFAULT 0,LogicalAuthor TEXT NOT NULL DEFAULT '',IsGroup INTEGER NOT NULL DEFAULT 0,UNIQUE(RootId,NormalizedPath),FOREIGN KEY(RootId) REFERENCES Roots(RootId) ON DELETE CASCADE);" +
                "CREATE TABLE IF NOT EXISTS DestinationFiles(FileId INTEGER PRIMARY KEY AUTOINCREMENT,RootId INTEGER NOT NULL,FolderId INTEGER,FullPath TEXT NOT NULL,NormalizedPath TEXT NOT NULL,FileName TEXT NOT NULL,NormalizedFileName TEXT NOT NULL,Extension TEXT NOT NULL,FileSize INTEGER NOT NULL,LastWriteUtc TEXT NOT NULL,UNIQUE(RootId,NormalizedPath),FOREIGN KEY(RootId) REFERENCES Roots(RootId) ON DELETE CASCADE);" +
                "CREATE TABLE IF NOT EXISTS AuthorFolderMappings(EntityId INTEGER NOT NULL,RootId INTEGER NOT NULL,FolderId INTEGER NOT NULL,PRIMARY KEY(EntityId,RootId));" +
                "CREATE TABLE IF NOT EXISTS PendingChanges(ChangeId INTEGER PRIMARY KEY AUTOINCREMENT,ChangeKind TEXT NOT NULL,OldPath TEXT,NewPath TEXT,CreatedUtc TEXT NOT NULL);"
            );
            TryExecute("ALTER TABLE Roots ADD COLUMN IndexScopeKey TEXT NOT NULL DEFAULT '';");
            TryExecute("ALTER TABLE MigrationPlanCache ADD COLUMN ManualTargetDir TEXT NOT NULL DEFAULT '';");
            TryExecute("ALTER TABLE MigrationPlanCache ADD COLUMN ManualTargetName TEXT NOT NULL DEFAULT '';");
            TryExecute("ALTER TABLE MigrationPlanCache ADD COLUMN ManualTargetAuthor TEXT NOT NULL DEFAULT '';");
            TryExecute("ALTER TABLE ParsedMetadata ADD COLUMN InputFingerprint TEXT NOT NULL DEFAULT '';");
            TryExecute("ALTER TABLE RecognitionCache ADD COLUMN InputFingerprint TEXT NOT NULL DEFAULT '';");
            TryExecute("ALTER TABLE RecognitionCache ADD COLUMN EvidenceKind TEXT NOT NULL DEFAULT '';");
            TryExecute("ALTER TABLE RecognitionCache ADD COLUMN RunnerUpConfidence INTEGER NOT NULL DEFAULT 0;");
            TryExecute("ALTER TABLE RecognitionCache ADD COLUMN UpdatedUtc TEXT NOT NULL DEFAULT '';");
            TryExecute("ALTER TABLE DestinationFolders ADD COLUMN GroupNumber INTEGER NOT NULL DEFAULT 0;");
            TryExecute("ALTER TABLE DestinationFolders ADD COLUMN LogicalAuthor TEXT NOT NULL DEFAULT '';");
            TryExecute("ALTER TABLE DestinationFolders ADD COLUMN IsGroup INTEGER NOT NULL DEFAULT 0;");
            SetMetadata("SchemaVersion", "9");
        }

        private void TryExecute(string sql)
        {
            try { Execute(sql); }
            catch { }
        }

        private void UpdateRootState(long rootId, string provider, string state)
        {
            using (Statement statement = Prepare("UPDATE Roots SET Provider=?1,State=?2,LastSyncUtc=?3 WHERE RootId=?4"))
            {
                statement.Bind(1, provider ?? "");
                statement.Bind(2, state ?? "Clean");
                statement.Bind(3, DateTime.UtcNow.ToString("o"));
                statement.Bind(4, rootId);
                statement.StepDone();
            }
        }

        private void SetMetadata(string key, string value)
        {
            lock (_gate)
            {
                EnsureOpen();
                using (Statement statement = Prepare("INSERT OR REPLACE INTO CacheMetadata(Key,Value) VALUES(?1,?2)"))
                {
                    statement.Bind(1, key);
                    statement.Bind(2, value ?? "");
                    statement.StepDone();
                }
            }
        }

        private string ScalarText(string sql)
        {
            using (Statement statement = Prepare(sql))
                return statement.Step() == SqliteRow ? statement.Text(0) : "";
        }

        private void EnsureOpen()
        {
            if (_db == IntPtr.Zero) Open();
        }

        private void Execute(string sql)
        {
            IntPtr error;
            int code = Native.sqlite3_exec(_db, Utf8Z(sql), IntPtr.Zero, IntPtr.Zero, out error);
            if (code == SqliteOk) return;
            string message = error == IntPtr.Zero ? ErrorMessage(_db) : Marshal.PtrToStringAnsi(error);
            if (error != IntPtr.Zero) Native.sqlite3_free(error);
            throw new InvalidOperationException("文件索引数据库错误：" + message);
        }

        private Statement Prepare(string sql)
        {
            IntPtr statement;
            int code = Native.sqlite3_prepare_v2(_db, Utf8Z(sql), -1, out statement, IntPtr.Zero);
            if (code != SqliteOk || statement == IntPtr.Zero)
                throw new InvalidOperationException("文件索引数据库错误：" + ErrorMessage(_db));
            return new Statement(statement);
        }

        private static string NormalizeRoot(string path)
        {
            try { return Path.GetFullPath(path ?? "").TrimEnd('\\', '/').ToUpperInvariant(); }
            catch { return (path ?? "").Trim().TrimEnd('\\', '/').ToUpperInvariant(); }
        }

        private static string NormalizePath(string path)
        {
            try { return Path.GetFullPath(path ?? "").ToUpperInvariant(); }
            catch { return (path ?? "").Trim().ToUpperInvariant(); }
        }

        private static string MakeRelative(string root, string path)
        {
            string normalizedRoot = Path.GetFullPath(root ?? "").TrimEnd('\\', '/') + Path.DirectorySeparatorChar;
            string normalizedPath = Path.GetFullPath(path ?? "");
            if (normalizedPath.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase))
                return normalizedPath.Substring(normalizedRoot.Length);
            return normalizedPath;
        }

        private static DateTime ParseUtc(string value)
        {
            DateTime parsed;
            return DateTime.TryParse(value, null, System.Globalization.DateTimeStyles.RoundtripKind, out parsed)
                ? parsed.ToUniversalTime() : DateTime.MinValue;
        }

        private static byte[] Utf8Z(string value)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(value ?? "");
            byte[] terminated = new byte[bytes.Length + 1];
            Buffer.BlockCopy(bytes, 0, terminated, 0, bytes.Length);
            return terminated;
        }

        private static string ErrorMessage(IntPtr db)
        {
            if (db == IntPtr.Zero) return "SQLite open failed";
            IntPtr pointer = Native.sqlite3_errmsg(db);
            return pointer == IntPtr.Zero ? "SQLite error" : Marshal.PtrToStringAnsi(pointer);
        }

        private sealed class Statement : IDisposable
        {
            private IntPtr _statement;
            public Statement(IntPtr statement) { _statement = statement; }
            public void Bind(int index, string value)
            {
                byte[] bytes = Encoding.UTF8.GetBytes(value ?? "");
                Check(Native.sqlite3_bind_text(_statement, index, bytes, bytes.Length, new IntPtr(-1)));
            }
            public void Bind(int index, long value) { Check(Native.sqlite3_bind_int64(_statement, index, value)); }
            public int Step() { return Native.sqlite3_step(_statement); }
            public void Reset()
            {
                Check(Native.sqlite3_reset(_statement));
                Check(Native.sqlite3_clear_bindings(_statement));
            }
            public void StepDone()
            {
                int code = Step();
                if (code != SqliteDone) throw new InvalidOperationException("文件索引数据库写入失败。");
            }
            public long Int64(int column) { return Native.sqlite3_column_int64(_statement, column); }
            public string Text(int column)
            {
                IntPtr pointer = Native.sqlite3_column_text(_statement, column);
                int length = Native.sqlite3_column_bytes(_statement, column);
                if (pointer == IntPtr.Zero || length <= 0) return "";
                byte[] bytes = new byte[length];
                Marshal.Copy(pointer, bytes, 0, length);
                return Encoding.UTF8.GetString(bytes);
            }
            public void Dispose()
            {
                if (_statement == IntPtr.Zero) return;
                Native.sqlite3_finalize(_statement);
                _statement = IntPtr.Zero;
            }
            private static void Check(int code)
            {
                if (code != SqliteOk) throw new InvalidOperationException("文件索引数据库参数绑定失败。");
            }
        }

        private static class Native
        {
            [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
            internal static extern int sqlite3_open_v2(byte[] filename, out IntPtr db, int flags, IntPtr vfs);
            [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
            internal static extern int sqlite3_close(IntPtr db);
            [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
            internal static extern int sqlite3_exec(IntPtr db, byte[] sql, IntPtr callback, IntPtr argument, out IntPtr errorMessage);
            [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
            internal static extern void sqlite3_free(IntPtr pointer);
            [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
            internal static extern IntPtr sqlite3_errmsg(IntPtr db);
            [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
            internal static extern int sqlite3_errcode(IntPtr db);
            [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
            internal static extern int sqlite3_prepare_v2(IntPtr db, byte[] sql, int bytes, out IntPtr statement, IntPtr tail);
            [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
            internal static extern int sqlite3_bind_text(IntPtr statement, int index, byte[] value, int bytes, IntPtr destructor);
            [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
            internal static extern int sqlite3_bind_int64(IntPtr statement, int index, long value);
            [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
            internal static extern int sqlite3_step(IntPtr statement);
            [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
            internal static extern int sqlite3_reset(IntPtr statement);
            [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
            internal static extern int sqlite3_clear_bindings(IntPtr statement);
            [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
            internal static extern int sqlite3_finalize(IntPtr statement);
            [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
            internal static extern long sqlite3_column_int64(IntPtr statement, int column);
            [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
            internal static extern IntPtr sqlite3_column_text(IntPtr statement, int column);
            [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
            internal static extern int sqlite3_column_bytes(IntPtr statement, int column);
        }
    }
}
