using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;

namespace MangaAuthorSorter.Tests
{
    internal static class IncrementalIndexContractTests
    {
        private sealed class CountingProvider : IFileSystemIndexProvider
        {
            public int Calls;
            public string ProviderId { get { return "ContractFake"; } }
            public bool IsAvailable { get { return true; } }

            public SearchResult SearchFiles(
                string source,
                bool recursive,
                int scanLimit,
                HashSet<string> blockedPaths,
                IEnumerable<string> extensions,
                Action<ScanProgressInfo> progress,
                Func<bool> cancelRequested)
            {
                Calls++;
                List<string> allowed = FileTypeRules.NormalizeExtensions(extensions);
                SearchOption option = recursive
                    ? SearchOption.AllDirectories
                    : SearchOption.TopDirectoryOnly;
                List<FileInfo> files = Directory.EnumerateFiles(source, "*", option)
                    .Where(delegate(string path)
                    {
                        return FileTypeRules.ContainsExtension(allowed, path);
                    })
                    .Select(delegate(string path) { return new FileInfo(path); })
                    .ToList();
                return new SearchResult
                {
                    Files = files,
                    DiscoveredCount = files.Count,
                    Backend = ProviderId,
                    Detail = "contract"
                };
            }
        }

        private static void UnifiedAliasMigrationIsSafe(string root)
        {
            string directory = Path.Combine(root, "unified-authors"); Directory.CreateDirectory(directory);
            string legacy = Path.Combine(directory, "AuthorAliases.txt"), json = Path.Combine(directory, "AuthorEntities.json");
            string original = "First|shared|one\r\nSecond|shared|two\r\nunparsed-rule"; File.WriteAllText(legacy, original);
            AuthorEntityStore store = new AuthorEntityStore(json); List<AliasGroup> baseline = LegacyAliasImporter.Read(legacy);
            LegacyAliasImporter.Import(legacy, store);
            Assert(store.Load().LegacyAliasGroups.Count == 2 && store.Load().LegacyUnparsedLines.Count == 1, "migration must preserve groups and unparsed lines");
            AuthorIndex before = AuthorIndex.Build(new List<AuthorFolder>(), baseline, null);
            AuthorEntityIndex index = store.LoadIndex(); AuthorIndex after = AuthorIndex.Build(new List<AuthorFolder>(), index.AliasGroups, index);
            foreach (string name in new[] { "First", "Second", "one", "two", "shared", "missing" }) {
                bool oldAmbiguous, ambiguous; AliasGroup a = before.ResolveAlias(name, out oldAmbiguous), b = after.ResolveAlias(name, out ambiguous);
                Assert(oldAmbiguous == ambiguous && (a == null ? "" : a.Canonical) == (b == null ? "" : b.Canonical), "migration changed alias resolution");
            }
            Assert(index.Resolve("one").Found && index.Resolve("shared").Ambiguous, "entity index must retain ambiguity");
            string semantic = store.GetRecognitionVersion(false); store.SaveLookupStatus("Test", "missing", "not_found", "test", true);
            Assert(semantic == store.GetRecognitionVersion(false), "online status cache must not invalidate author data");
            string migrated = File.ReadAllText(json); LegacyAliasImporter.Import(legacy, new AuthorEntityStore(json));
            Assert(migrated == File.ReadAllText(json), "migration must be idempotent");
            store.MergeManualAuthorNames("Third", new[] { "shared", "three" });
            Assert(store.Load().Authors.Count == 3 && File.ReadAllText(legacy) == original, "manual edit cannot merge unrelated authors or write TXT");
            File.AppendAllText(legacy, "\r\nExternal|change"); string saved = File.ReadAllText(json);
            LegacyAliasImporter.Import(legacy, new AuthorEntityStore(json));
            Assert(store.LegacyAliasesChanged(legacy) && File.ReadAllText(json) == saved, "external edits cannot overwrite JSON");
            File.Delete(json + ".bak"); Directory.CreateDirectory(json + ".bak"); bool failed = false;
            try { store.ClearLookupCache(); } catch (IOException) { failed = true; } catch (UnauthorizedAccessException) { failed = true; }
            Assert(failed && File.ReadAllText(json) == saved, "failed replacement must preserve original");
            File.WriteAllText(json, "{broken"); failed = false;
            try { store.ClearLookupCache(); } catch { failed = true; }
            Assert(failed && File.ReadAllText(json) == "{broken", "corrupt JSON cannot be reset");
            Assert(File.Exists(legacy + ".migration.bak"), "legacy backup must exist");
        }
        public static int Main()
        {
            string testRoot = Path.Combine(
                Path.GetTempPath(),
                "GuiGui-IncrementalContract-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(testRoot);
            try
            {
                UnifiedAliasMigrationIsSafe(testRoot);
                Console.WriteLine("[PASS] Unified aliases: safe and idempotent migration, overlap isolation, durable edits, external-change detection and failed-write rollback.");
                PerformanceHistoryPreservesSelectedDetailsAfterRestart(testRoot);
                Console.WriteLine("[PASS] GL4 history reload retains all metrics; newer warmup cannot override selected records; clearing persists.");
                IncrementalAuthorIndexPreservesMatches();
                Console.WriteLine("[PASS] Incremental author-folder registration equals a full rebuild (including ambiguity and role indexes).");
                DirectOnlyEverythingQueryUsesParentFunction();
                Console.WriteLine("[PASS] Shallow Everything query uses indexed parent: rather than enumerating descendants.");
                                PlanKeyIgnoresSessionAndDirectoryState();
                Console.WriteLine("[PASS] Per-file plan key ignores limit, scope and directory revisions.");
                ProgramOwnedMoveDoesNotRediscover(testRoot);
                Console.WriteLine("[PASS] Program-owned move updates index/session without rediscovery.");
                RecursiveToggleIsIndexProjection(testRoot);
                Console.WriteLine("[PASS] Cold shallow scan skips descendants; full index later serves both scopes.");
                ShallowScanDoesNotDeleteRecursiveFacts(testRoot);
                Console.WriteLine("[PASS] Shallow-only scan does not remove cached descendant recognition.");
                BulkIndexReconciliationSupportsPreparedStatementReuse(testRoot);
                Console.WriteLine("[PASS] Batched SQLite reconciliation adds, reuses and modifies indexed entries.");
                IndexedMetadataSnapshotAvoidsPhysicalFileRead(testRoot);
                Console.WriteLine("[PASS] Provider metadata can reconcile FileIndex without physical file metadata reads.");
                BulkPlanUpsertUpdatesOnlyChangedRows(testRoot);
                Console.WriteLine("[PASS] Batch SQLite plan cache upsert preserves all rows and partial updates.");
                PlanCacheWritePolicyAvoidsRewritingCacheHits();
                Console.WriteLine("[PASS] Unchanged plan cache hits cannot trigger any SQLite plan write.");
                UnchangedSnapshotProducesOnlyCacheHits(testRoot);
                Console.WriteLine("[PASS] Unchanged snapshots produce cache hits only.");
                FileTypeScopeChangePreservesExistingIndexRows(testRoot);
                Console.WriteLine("[PASS] File-type scope changes do not delete durable rows from other scopes.");
                ExternalDirectoryMovePreservesFileIdentity(testRoot);
                Console.WriteLine("[PASS] External directory move is classified as Moved, not Renamed.");
                DeletedFileInvalidatesRuntimeFacts(testRoot);
                Console.WriteLine("[PASS] Deleted files evict runtime recognition/plan facts.");
                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("[FAIL] " + ex);
                return 1;
            }
            finally
            {
                try { Directory.Delete(testRoot, true); } catch { }
            }
        }

        private static void BulkPlanUpsertUpdatesOnlyChangedRows(string testRoot)
        {
            string root = Path.Combine(testRoot, "plans-batched");
            Directory.CreateDirectory(root);
            DateTime timestamp = new DateTime(2026, 10, 8, 12, 0, 0, DateTimeKind.Utc);
            List<SourceIndexFileSnapshot> index = new List<SourceIndexFileSnapshot>();
            List<FileInfo> files = new List<FileInfo>();
            for (int i = 0; i < 64; i++)
            {
                string path = Path.Combine(root, "plan-" + i.ToString("D4") + ".zip");
                File.WriteAllText(path, "sample");
                files.Add(new FileInfo(path));
                index.Add(new SourceIndexFileSnapshot
                {
                    FullPath = path,
                    DirectoryPath = root,
                    FileName = Path.GetFileName(path),
                    Extension = ".zip",
                    FileSize = 6,
                    CreationTimeUtc = timestamp,
                    LastWriteTimeUtc = timestamp
                });
            }
            using (FileIndexCacheDatabase database = new FileIndexCacheDatabase(
                Path.Combine(testRoot, "plans-batched.db")))
            {
                database.Open();
                long rootId = database.EnsureRoot("Source", root, "ContractFake");
                database.ReplaceSourceSnapshot(rootId, root, "ContractFake", index,
                    "CanonicalRecursive=True|Extensions=.zip");
                ScanSessionSnapshot session = database.CreateScanSession(rootId, files);
                List<PlanItem> initial = new List<PlanItem>();
                foreach (FileInfo file in files)
                    initial.Add(new PlanItem
                    {
                        FileId = session.FileIdsByPath[file.FullName],
                        SourcePath = file.FullName,
                        FileName = file.Name,
                        Author = "Author 1",
                        TargetPath = Path.Combine(root, "Author 1", file.Name),
                        Status = "NewAuthor",
                        StatusCode = PlanStatusCode.NewAuthor,
                        LastWriteTime = timestamp,
                        FileSize = 6
                    });
                database.UpsertMigrationPlans("contract-v1", initial);
                List<PlanItem> first = database.LoadMigrationPlans("contract-v1", session);
                Assert(first.Count == 64, "batched prepared statement must persist all 64 plans");
                Assert(first.All(delegate(PlanItem item) { return item.Author == "Author 1"; }),
                    "a reset statement must not reuse previous row bindings");
                PlanItem changed = initial[17];
                changed.Author = "Author 2";
                database.UpsertMigrationPlans("contract-v1", new[] { changed });
                List<PlanItem> after = database.LoadMigrationPlans("contract-v1", session);
                Assert(after.Count == 64, "a partial plan update must retain the untouched plans");
                Assert(after.Count(delegate(PlanItem item) { return item.Author == "Author 2"; }) == 1,
                    "only one plan should change after an incremental update");
                Assert(after.Count(delegate(PlanItem item) { return item.Author == "Author 1"; }) == 63,
                    "the other 63 plans must remain unchanged");
            }
        }

        private static void PlanCacheWritePolicyAvoidsRewritingCacheHits()
        {
            Assert(!FileIndexCacheDatabase.HasNewMigrationPlans(new PlanItem[0]),
                "a cache-only scan must not write plans");
            Assert(!FileIndexCacheDatabase.HasNewMigrationPlans(new[] {
                new PlanItem { FileId = 0 }
            }), "a non-indexed row must not write plans");
            Assert(FileIndexCacheDatabase.HasNewMigrationPlans(new[] {
                new PlanItem { FileId = 42 }
            }), "a real new plan must be persisted");
        }

        private static void PerformanceHistoryPreservesSelectedDetailsAfterRestart(string testRoot)
        {
            string logPath = Path.Combine(testRoot, "ScanPerformance.log");
            ScanPerformanceDiagnostics.Initialize(logPath, true, true, true);
            DateTime time = new DateTime(2026, 10, 8, 20, 22, 13);
            ScanPerformanceDiagnostics.Record(new ScanPerformanceEntry
            {
                Time = time,
                Provider = "Everything SDK",
                ResultCount = 1562,
                CandidateCount = 1633,
                AuthorMatchMs = 1852,
                FileDiscoveryMs = 231,
                PlanningLoopMs = 1588,
                InitialIndexBuilds = 1,
                IncrementalIndexAdds = 482,
                UniqueAuthors = 752,
                NewAuthorFolders = 482,
                IndexAdded = 1633,
                RecalculatedFiles = 1562,
                TotalResponseMs = 2279,
                PlanCacheReadMs = 18,
                PlanCacheWriteMs = 36,
                FinalizeMs = 14
            });

            ScanPerformanceDiagnostics.Initialize(logPath, true, true, true);
            List<ScanPerformanceEntry> entries = ScanPerformanceDiagnostics.Snapshot();
            Assert(entries.Count == 1, "one persisted performance record should reload");
            ScanPerformanceEntry restored = entries[0];
            Assert(restored.Time == time && restored.ResultCount == 1562 && restored.CandidateCount == 1633,
                "date and record counts should survive restart");
            Assert(restored.AuthorMatchMs == 1852 && restored.PlanningLoopMs == 1588 && restored.TotalResponseMs == 2279,
                "recognition and timing details should survive restart");
            Assert(restored.InitialIndexBuilds == 1 && restored.IncrementalIndexAdds == 482 && restored.UniqueAuthors == 752,
                "target index metrics should survive restart");
            Assert(restored.PlanCacheReadMs == 18 && restored.PlanCacheWriteMs == 36 && restored.FinalizeMs == 14,
                "cache IO and finalization timing must survive restart");

            ScanWarmupStatusEntry newerWarmup = new ScanWarmupStatusEntry { Time = time.AddMinutes(5) };
            Assert(!ScanPerformanceForm.ShouldShowWarmupForSelection(restored, newerWarmup),
                "live warmup must not override a selected historical scan");
            Assert(ScanPerformanceForm.ShouldShowWarmupForSelection(null, newerWarmup),
                "live warmup should remain visible when no scan is selected");

            string error;
            Assert(ScanPerformanceDiagnostics.TryClear(out error), "clearing performance history failed: " + error);
            ScanPerformanceDiagnostics.Initialize(logPath, true, true, true);
            Assert(ScanPerformanceDiagnostics.Snapshot().Count == 0,
                "cleared performance history should not return after restart");
        }

        private static AuthorFolder TestFolder(string name, string path, string extra)
        {
            AuthorFolder folder = new AuthorFolder { AuthorName = name, AuthorPath = path, PreferredIdentity = name };
            ArchiveEngine.IndexAuthorFolderName(folder, name);
            if (!String.IsNullOrWhiteSpace(extra)) ArchiveEngine.IndexAuthorFolderName(folder, extra);
            return folder;
        }

        private static void CompareAuthorResults(IEnumerable<AuthorFolder> actual,
            IEnumerable<AuthorFolder> expected, string context)
        {
            string a = String.Join("|", actual.Select(delegate(AuthorFolder x) { return x.AuthorPath; }).ToArray());
            string b = String.Join("|", expected.Select(delegate(AuthorFolder x) { return x.AuthorPath; }).ToArray());
            Assert(String.Equals(a, b, StringComparison.Ordinal), context + ": " + a + " != " + b);
        }

        private static void IncrementalAuthorIndexPreservesMatches()
        {
            List<AuthorFolder> folders = new List<AuthorFolder>();
            folders.Add(TestFolder("石恵", @"X:\A", "ishikei"));
            folders.Add(TestFolder("木鈴亭 (木鈴カケル)", @"X:\B", "木鈴カケル"));
            AliasGroup group = new AliasGroup { Canonical = "石恵" };
            group.Norms.Add(AuthorRules.NormalizeText("ishikei"));
            List<AliasGroup> groups = new List<AliasGroup> { group };
            AuthorIndex incremental = AuthorIndex.Build(folders, groups, null);
            string[] names = { "石恵", "ishikei", "木鈴亭", "木鈴カケル", "偽MIDI泥の会", "新人", "同名", "円舞" };
            for (int i = 0; i < 40; i++)
            {
                string name = i % 6 == 0 ? "同名" : i % 4 == 0 ? "偽MIDI泥の会 (新人)" : "作家" + i;
                string path = @"X:\Artists\" + i;
                AuthorFolder added = TestFolder(name, path, i % 5 == 0 ? "新人" : "");
                folders.Add(added);
                incremental.AddPlannedFolder(added);
                AuthorIndex rebuilt = AuthorIndex.Build(folders, groups, null);
                Assert(incremental.FolderCount == rebuilt.FolderCount, "folder count changed");
                Assert(incremental.AliasNameCount == rebuilt.AliasNameCount, "alias keys changed");
                foreach (string candidate in names)
                {
                    CompareAuthorResults(incremental.FindDirect(candidate), rebuilt.FindDirect(candidate), "direct " + candidate);
                    CompareAuthorResults(incremental.FindGeneric(candidate), rebuilt.FindGeneric(candidate), "generic " + candidate);
                    CompareAuthorResults(incremental.FindCautious(candidate), rebuilt.FindCautious(candidate), "cautious " + candidate);
                    CompareAuthorResults(incremental.FindRole(candidate, 1), rebuilt.FindRole(candidate, 1), "society " + candidate);
                    CompareAuthorResults(incremental.FindRole(candidate, 2), rebuilt.FindRole(candidate, 2), "creator " + candidate);
                    CompareAuthorResults(incremental.FindRole(candidate, 3), rebuilt.FindRole(candidate, 3), "segment " + candidate);
                    CompareAuthorResults(incremental.FindCandidateFolders(candidate + " 漫画"), rebuilt.FindCandidateFolders(candidate + " 漫画"), "bigrams " + candidate);
                    bool ab, bb;
                    AliasGroup ga = incremental.ResolveAlias(candidate, out ab);
                    AliasGroup gb = rebuilt.ResolveAlias(candidate, out bb);
                    Assert(Object.ReferenceEquals(ga, gb) && ab == bb, "alias result changed");
                }
            }
            Assert(incremental.FindDirect("同名").Count >= 2, "same author label should remain ambiguous");
        }

        private static void PlanKeyIgnoresSessionAndDirectoryState()
        {
            ScanWarmupRequest baseline = NewPlanRequest();
            string expected = baseline.PlanKey;

            ScanWarmupRequest changed = NewPlanRequest();
            changed.Recursive = true;
            changed.RequestedLimit = 10;
            changed.Mode = ScanModeKind.Exact;
            changed.Extensions = new List<string> { "zip", "rar", "7z", "cbz" };
            changed.SourceVersion = "different-source-revision";
            changed.SourceSnapshotRevision = 999;
            changed.TargetVersion = "different-target-revision";

            Assert(String.Equals(expected, changed.PlanKey, StringComparison.Ordinal),
                "scope, limit or directory versions must not invalidate per-file plans");

            changed.RecognitionVersion = "changed-recognition-rules";
            Assert(!String.Equals(expected, changed.PlanKey, StringComparison.Ordinal),
                "recognition rule changes must invalidate per-file plans");
        }

        private static ScanWarmupRequest NewPlanRequest()
        {
            return new ScanWarmupRequest
            {
                Source = @"C:\Contract\Source",
                Target = @"D:\Contract\Target",
                Recursive = false,
                RequestedLimit = 0,
                Mode = ScanModeKind.Global,
                Extensions = new List<string> { "zip", "rar" },
                GroupTemplate = "[{0}]",
                RecognizedGroupTemplates = new List<string> { "[{0}]" },
                AuthorFolderTemplate = "{0}",
                RecognizedAuthorFolderTemplates = new List<string> { "{0}" },
                RecognitionVersion = "recognition-v1",
                SourceVersion = "source-v1",
                TargetVersion = "target-v1",
                MaxAuthors = 20
            };
        }

        private static void ProgramOwnedMoveDoesNotRediscover(string testRoot)
        {
            string source = Path.Combine(testRoot, "source");
            string target = Path.Combine(testRoot, "target");
            Directory.CreateDirectory(source);
            Directory.CreateDirectory(target);
            string sourceFile = Path.Combine(source, "[Author] Book.zip");
            string targetFile = Path.Combine(target, "[Author] Book.zip");
            File.WriteAllText(sourceFile, "contract");

            CountingProvider inner = new CountingProvider();
            using (FileIndexCacheDatabase database = new FileIndexCacheDatabase(
                Path.Combine(testRoot, "FileIndexCache.db")))
            {
                database.Open();
                using (PersistentSourceIndexProvider provider =
                    new PersistentSourceIndexProvider(inner, database))
                {
                    List<string> extensions = new List<string> { ".zip" };
                    SearchResult initial = provider.SearchFiles(
                        source, false, 0, new HashSet<string>(), extensions,
                        null, null);
                    Assert(inner.Calls == 1, "initial discovery count");
                    SearchResult session = provider.StartScanSession(
                        source, initial, 0);
                    Assert(session.Files.Count == 1, "initial session membership");
                    long revision = provider.SnapshotRevision;

                    provider.RegisterExpectedMutation(
                        sourceFile, target, targetFile, targetFile + ".moving");
                    File.Move(sourceFile, targetFile);
                    database.ApplySuccessfulMove(sourceFile, targetFile);
                    provider.ApplySuccessfulMove(sourceFile, targetFile);
                    Thread.Sleep(250);

                    SearchResult afterMove = provider.SearchFiles(
                        source, false, 0, new HashSet<string>(), extensions,
                        null, null);
                    Assert(inner.Calls == 1,
                        "program move must not invoke the discovery provider again");
                    Assert(afterMove.Files.Count == 0,
                        "program move must remove the source from the global index");
                    Assert(provider.CurrentScanSession.FileIds.Count == 0,
                        "program move must remove the file from the current ScanSession");
                    Assert(provider.SnapshotRevision == revision,
                        "program move must not impersonate an external change");

                    string externalFile = Path.Combine(source, "[Other] New.zip");
                    File.WriteAllText(externalFile, "external");
                    DateTime deadline = DateTime.UtcNow.AddSeconds(3);
                    while (provider.SnapshotRevision == revision &&
                        DateTime.UtcNow < deadline)
                        Thread.Sleep(25);
                    Assert(provider.SnapshotRevision > revision,
                        "an unregistered external change must invalidate the snapshot");

                    SearchResult afterExternalChange = provider.SearchFiles(
                        source, false, 0, new HashSet<string>(), extensions,
                        null, null);
                    Assert(inner.Calls == 2,
                        "external change must run reconciliation exactly once");
                    Assert(afterExternalChange.Files.Count == 1,
                        "reconciliation must discover the external file");
                }
            }
        }

        private static void RecursiveToggleIsIndexProjection(string testRoot)
        {
            string root = Path.Combine(testRoot, "recursive-projection");
            string child = Path.Combine(root, "child");
            Directory.CreateDirectory(child);
            File.WriteAllText(Path.Combine(root, "root.zip"), "root");
            File.WriteAllText(Path.Combine(child, "child.zip"), "child");

            CountingProvider inner = new CountingProvider();
            using (FileIndexCacheDatabase database = new FileIndexCacheDatabase(
                Path.Combine(testRoot, "recursive-projection.db")))
            {
                database.Open();
                using (PersistentSourceIndexProvider provider =
                    new PersistentSourceIndexProvider(inner, database))
                {
                    List<string> extensions = new List<string> { ".zip" };
                    SearchResult topOnly = provider.SearchFiles(
                        root, false, 0, new HashSet<string>(), extensions, null, null);
                    Assert(inner.Calls == 1, "first shallow scan performs one direct-only discovery");
                    Assert(topOnly.Files.Count == 1, "non-recursive projection contains only root files");

                    SearchResult recursive = provider.SearchFiles(
                        root, true, 0, new HashSet<string>(), extensions, null, null);
                    Assert(inner.Calls == 2, "recursive toggle fills unindexed descendants once");
                    Assert(recursive.Files.Count == 2, "recursive scan discovers child files");
                    SearchResult shallowAgain = provider.SearchFiles(
                        root, false, 0, new HashSet<string>(), extensions, null, null);
                    Assert(inner.Calls == 2 && shallowAgain.Files.Count == 1,
                        "completed recursive snapshot must serve shallow query without rediscovery");

                    long rootId = database.EnsureRoot("Source", root, inner.ProviderId);
                    Assert(database.LoadSourceFiles(rootId).Count == 2,
                        "durable source index must remain canonical and recursive");
                }
            }
        }

        private static void DirectOnlyEverythingQueryUsesParentFunction()
        {
            string root = @"D:\Archives\Comics";
            string full = EverythingService.BuildEverythingSourceQuery(root, true, "ext:zip");
            string shallow = EverythingService.BuildEverythingSourceQuery(root, false, "ext:zip");
            Assert(full.StartsWith("\"" + root + "\"", StringComparison.Ordinal),
                "recursive query must still search the folder subtree");
            Assert(shallow.StartsWith("parent:\"" + root + "\"", StringComparison.Ordinal),
                "direct-only query must use Everything parent: predicate");
            Assert(shallow.EndsWith(" ext:zip", StringComparison.Ordinal),
                "extension filter must remain applied");
        }

        private static void ShallowScanDoesNotDeleteRecursiveFacts(string testRoot)
        {
            string root = Path.Combine(testRoot, "shallow-retains-full");
            string sub = Path.Combine(root, "nested");
            Directory.CreateDirectory(sub);
            string nested = Path.Combine(sub, "nested.zip");
            File.WriteAllText(nested, "in subfolder");
            CountingProvider inner = new CountingProvider();
            using (FileIndexCacheDatabase db = new FileIndexCacheDatabase(
                Path.Combine(testRoot, "shallow-retains-full.db")))
            {
                db.Open();
                using (PersistentSourceIndexProvider provider =
                    new PersistentSourceIndexProvider(inner, db))
                {
                    List<string> ext = new List<string> { ".zip" };
                    SearchResult full = provider.SearchFiles(root, true, 0,
                        new HashSet<string>(), ext, null, null);
                    Assert(full.Files.Count == 1, "recursive baseline should include nested file");
                    // The cold shallow scan is tested separately. Here the
                    // database is updated directly so FileSystemWatcher scheduling
                    // cannot make this preservation assertion flaky.
                    string topFile = Path.Combine(root, "top.zip");
                    File.WriteAllText(topFile, "direct child");
                    long rootId = db.EnsureRoot("Source", root, inner.ProviderId);
                    List<FileInfo> shallow = new List<FileInfo> { new FileInfo(topFile) };
                    db.ReplaceSourceSnapshot(rootId, root, inner.ProviderId,
                        shallow, "CanonicalRecursive=True|Extensions=.zip|Depth=Top", true);
                    Assert(db.LoadSourceFiles(rootId).ContainsKey(nested),
                        "shallow scan must retain durable nested row");
                    Assert(db.LoadSourceFiles(rootId).ContainsKey(topFile),
                        "shallow scan should upsert direct child");
                }
            }
        }

        private static void BulkIndexReconciliationSupportsPreparedStatementReuse(string testRoot)
        {
            string root = Path.Combine(testRoot, "batch-index");
            Directory.CreateDirectory(root);
            DateTime stamp = new DateTime(2026, 10, 8, 0, 0, 0, DateTimeKind.Utc);
            List<SourceIndexFileSnapshot> entries = new List<SourceIndexFileSnapshot>();
            for (int i = 0; i < 256; ++i)
            {
                string path = Path.Combine(root, i.ToString("D4") + ".zip");
                entries.Add(new SourceIndexFileSnapshot {
                    FullPath = path, DirectoryPath = root,
                    FileName = Path.GetFileName(path), Extension = ".zip",
                    FileSize = 1024 + i, LastWriteTimeUtc = stamp,
                    CreationTimeUtc = stamp
                });
            }
            using (FileIndexCacheDatabase db = new FileIndexCacheDatabase(
                Path.Combine(testRoot, "batch-index.db")))
            {
                db.Open();
                long id = db.EnsureRoot("Source", root, "ContractFake");
                const string scope = "CanonicalRecursive=True|Extensions=.zip";
                FileIndexDelta first = db.ReplaceSourceSnapshot(id, root, "ContractFake", entries, scope);
                Assert(first.Added == entries.Count, "batch first pass must insert all entries");
                FileIndexDelta second = db.ReplaceSourceSnapshot(id, root, "ContractFake", entries, scope);
                Assert(second.CacheHits == entries.Count && second.Added == 0,
                    "batch second pass must reuse every unchanged row");
                entries[5].FileSize++;
                FileIndexDelta third = db.ReplaceSourceSnapshot(id, root, "ContractFake", entries, scope);
                Assert(third.Modified == 1 && third.CacheHits == entries.Count - 1,
                    "batch third pass must update exactly one changed item");
                Assert(db.LoadSourceFiles(id).Count == entries.Count,
                    "prepared statements must not change durable row count");
            }
        }

        private static void IndexedMetadataSnapshotAvoidsPhysicalFileRead(string testRoot)
        {
            string root = Path.Combine(testRoot, "provider-metadata");
            Directory.CreateDirectory(root);
            string indexedPath = Path.Combine(root, "indexed-only.zip");
            DateTime modified = new DateTime(2026, 10, 8, 1, 2, 3, DateTimeKind.Utc);
            DateTime created = new DateTime(2026, 10, 7, 1, 2, 3, DateTimeKind.Utc);

            using (FileIndexCacheDatabase database = new FileIndexCacheDatabase(
                Path.Combine(testRoot, "provider-metadata.db")))
            {
                database.Open();
                long rootId = database.EnsureRoot("Source", root, "Everything SDK");
                FileIndexDelta delta = database.ReplaceSourceSnapshot(
                    rootId, root, "Everything SDK",
                    new List<SourceIndexFileSnapshot>
                    {
                        new SourceIndexFileSnapshot
                        {
                            FullPath = indexedPath,
                            DirectoryPath = root,
                            FileName = "indexed-only.zip",
                            Extension = ".zip",
                            FileSize = 123456,
                            LastWriteTimeUtc = modified,
                            CreationTimeUtc = created
                        }
                    },
                    "CanonicalRecursive=True|Extensions=.zip");

                Assert(!File.Exists(indexedPath),
                    "contract path intentionally does not exist on disk");
                Assert(delta.Added == 1, "provider metadata should create one index row");
                SourceFileIndexEntry row = database.LoadSourceFiles(rootId).Values.Single();
                Assert(row.FileSize == 123456 && row.LastWriteTimeUtc == modified,
                    "provider metadata must be persisted without FileInfo metadata IO");
            }
        }

        private static void UnchangedSnapshotProducesOnlyCacheHits(string testRoot)
        {
            string root = Path.Combine(testRoot, "unchanged-delta");
            Directory.CreateDirectory(root);
            string path = Path.Combine(root, "same.zip");
            File.WriteAllText(path, "same");
            FileInfo file = new FileInfo(path);

            using (FileIndexCacheDatabase database = new FileIndexCacheDatabase(
                Path.Combine(testRoot, "unchanged-delta.db")))
            {
                database.Open();
                long rootId = database.EnsureRoot("Source", root, "Contract");
                const string scope = "CanonicalRecursive=True|Extensions=.zip";
                database.ReplaceSourceSnapshot(rootId, root, "Contract",
                    new[] { file }, scope);
                FileIndexDelta second = database.ReplaceSourceSnapshot(
                    rootId, root, "Contract", new[] { new FileInfo(path) }, scope);

                Assert(second.CacheHits == 1, "unchanged file must be a direct index cache hit");
                Assert(second.Added == 0 && second.Modified == 0 &&
                    second.Moved == 0 && second.Renamed == 0 && second.Removed == 0,
                    "unchanged file must not be classified as a change");
                Assert(second.RecognitionInvalidatedPaths.Count == 0 &&
                    second.PlanInvalidatedPaths.Count == 0,
                    "unchanged file must not invalidate recognition or plan facts");
            }
        }

        private static void FileTypeScopeChangePreservesExistingIndexRows(string testRoot)
        {
            string root = Path.Combine(testRoot, "scope-union");
            Directory.CreateDirectory(root);
            File.WriteAllText(Path.Combine(root, "one.zip"), "zip");
            File.WriteAllText(Path.Combine(root, "two.rar"), "rar");

            CountingProvider inner = new CountingProvider();
            using (FileIndexCacheDatabase database = new FileIndexCacheDatabase(
                Path.Combine(testRoot, "scope-union.db")))
            {
                database.Open();
                using (PersistentSourceIndexProvider provider =
                    new PersistentSourceIndexProvider(inner, database))
                {
                    provider.SearchFiles(root, true, 0, new HashSet<string>(),
                        new List<string> { ".zip" }, null, null);
                    provider.SearchFiles(root, true, 0, new HashSet<string>(),
                        new List<string> { ".rar" }, null, null);
                }

                // Reopen the provider so the same .rar scope is reconciled from
                // storage rather than satisfied by the in-memory watched snapshot.
                using (PersistentSourceIndexProvider provider =
                    new PersistentSourceIndexProvider(inner, database))
                {
                    provider.SearchFiles(root, true, 0, new HashSet<string>(),
                        new List<string> { ".rar" }, null, null);
                }

                long rootId = database.EnsureRoot("Source", root, inner.ProviderId);
                Dictionary<string, SourceFileIndexEntry> rows = database.LoadSourceFiles(rootId);
                Assert(rows.Count == 2,
                    "changing/repeating file-type scopes must preserve durable rows from other scopes");
            }
        }

        private static void ExternalDirectoryMovePreservesFileIdentity(string testRoot)
        {
            string root = Path.Combine(testRoot, "external-move");
            string from = Path.Combine(root, "from");
            string to = Path.Combine(root, "to");
            Directory.CreateDirectory(from);
            Directory.CreateDirectory(to);
            string oldPath = Path.Combine(from, "same.zip");
            string newPath = Path.Combine(to, "same.zip");
            File.WriteAllText(oldPath, "same");

            CountingProvider inner = new CountingProvider();
            using (FileIndexCacheDatabase database = new FileIndexCacheDatabase(
                Path.Combine(testRoot, "external-move.db")))
            {
                database.Open();
                using (PersistentSourceIndexProvider provider =
                    new PersistentSourceIndexProvider(inner, database))
                {
                    List<string> extensions = new List<string> { ".zip" };
                    provider.SearchFiles(root, true, 0, new HashSet<string>(), extensions, null, null);
                    long revision = provider.SnapshotRevision;
                    File.Move(oldPath, newPath);
                    DateTime deadline = DateTime.UtcNow.AddSeconds(3);
                    while (provider.SnapshotRevision == revision && DateTime.UtcNow < deadline)
                        Thread.Sleep(25);
                    Assert(provider.SnapshotRevision > revision,
                        "external move must invalidate the canonical snapshot");

                    provider.SearchFiles(root, true, 0, new HashSet<string>(), extensions, null, null);
                    FileIndexDelta delta = provider.LastDelta;
                    Assert(delta.Moved == 1 && delta.Renamed == 0,
                        "same-name directory relocation must be classified as Moved");
                    Assert(delta.RecognitionInvalidatedPaths.Count == 0,
                        "pure move must preserve filename-derived recognition facts");
                }
            }
        }

        private static void DeletedFileInvalidatesRuntimeFacts(string testRoot)
        {
            string root = Path.Combine(testRoot, "deleted-invalidation");
            Directory.CreateDirectory(root);
            string path = Path.Combine(root, "gone.zip");
            File.WriteAllText(path, "gone");

            using (FileIndexCacheDatabase database = new FileIndexCacheDatabase(
                Path.Combine(testRoot, "deleted-invalidation.db")))
            {
                database.Open();
                long rootId = database.EnsureRoot("Source", root, "Contract");
                const string scope = "CanonicalRecursive=True|Extensions=.zip";
                database.ReplaceSourceSnapshot(
                    rootId, root, "Contract", new[] { new FileInfo(path) }, scope);

                File.Delete(path);
                FileIndexDelta delta = database.ReplaceSourceSnapshot(
                    rootId, root, "Contract", new FileInfo[0], scope);

                Assert(delta.Removed == 1, "deleted file must be classified as Removed");
                Assert(delta.RecognitionInvalidatedPaths.Contains(path, StringComparer.OrdinalIgnoreCase),
                    "deleted path must evict runtime recognition facts");
                Assert(delta.PlanInvalidatedPaths.Contains(path, StringComparer.OrdinalIgnoreCase),
                    "deleted path must evict runtime plan facts");
            }
        }

        private static void Assert(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }
    }
}
