using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Diagnostics;
using System.Threading.Tasks;

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

        private sealed class LaggingSdkProvider : IFileSystemIndexProvider
        {
            public int Calls;
            public string ProviderId { get { return "LaggingSDK"; } }
            public bool IsAvailable { get { return true; } }
            public SearchResult SearchFiles(string source, bool recursive, int limit, HashSet<string> blocked,
                IEnumerable<string> extensions, Action<ScanProgressInfo> progress, Func<bool> canceled)
            {
                Calls++;
                List<FileInfo> files = Directory.EnumerateFiles(source, "*.zip").Take(1).Select(x => new FileInfo(x)).ToList();
                return new SearchResult { Files = files, IndexFiles = files, Backend = "Everything SDK", EverythingQueryCount = 1,
                    IndexEntries = files.Select(x => new SourceIndexFileSnapshot { FullPath = x.FullName, DirectoryPath = source,
                        FileName = x.Name, Extension = x.Extension, FileSize = x.Length, LastWriteTimeUtc = x.LastWriteTimeUtc }).ToList() };
            }
        }

        private static void ColdSdkLagCannotOmitFiles(string root)
        {
            string source = Path.Combine(root, "sdk-lag"); Directory.CreateDirectory(source);
            File.WriteAllText(Path.Combine(source, "one.zip"), "one"); File.WriteAllText(Path.Combine(source, "two.zip"), "two");
            LaggingSdkProvider sdk = new LaggingSdkProvider(); CountingProvider native = new CountingProvider();
            using (FileIndexCacheDatabase db = new FileIndexCacheDatabase(Path.Combine(root, "sdk-lag.db")))
            {
                db.Open();
                using (PersistentSourceIndexProvider provider = new PersistentSourceIndexProvider(new FileSystemIndexProviderRouter(sdk, native), db))
                {
                    SearchResult result = provider.SearchFiles(source, false, 0, new HashSet<string>(), new[] { ".zip" }, null, null);
                    Assert(result.Files.Count == 2 && sdk.Calls == 1 && native.Calls == 1 && result.EverythingQueryCount == 1,
                        "cold SDK lag must reconcile missing names without another Everything query");
                    result = provider.SearchFiles(source, false, 0, new HashSet<string>(), new[] { ".zip" }, null, null);
                    Assert(result.Files.Count == 2 && sdk.Calls == 1 && native.Calls == 1 && result.EverythingQueryCount == 0,
                        "the corrected monitored snapshot must be reusable");
                }
            }
            Console.WriteLine("[PASS] Cold Everything index lag cannot omit existing files; one SDK query, one necessary native fallback, then shared reuse.");
        }

        private static void RecognitionVersionCacheTracksContent(string root)
        {
            string path = Path.Combine(root, "recognition-version.json");
            File.WriteAllText(path, "{\"SchemaVersion\":3,\"Authors\":[{\"Id\":1,\"CanonicalName\":\"Alpha\"}]}");
            AuthorEntityStore store = new AuthorEntityStore(path);
            string alias = store.GetRecognitionVersion(true), entity = store.GetRecognitionVersion(false);
            Assert(store.GetRecognitionVersion(false) == entity && store.GetRecognitionVersion(true) == alias,
                "unchanged content must retain semantic versions");
            DateTime timestamp = File.GetLastWriteTimeUtc(path);
            File.WriteAllText(path, File.ReadAllText(path).Replace("Alpha", "Bravo"));
            File.SetLastWriteTimeUtc(path, timestamp);
            Assert(store.GetRecognitionVersion(false) != entity && store.GetRecognitionVersion(true) == alias,
                "same-size, same-timestamp external edits must change only affected recognition versions");
            File.WriteAllText(path, "invalid JSON");
            bool rejected = false;
            try { store.GetRecognitionVersion(false); } catch { rejected = true; }
            Assert(rejected, "invalid changed content cannot silently retain a cached recognition fingerprint");
            Console.WriteLine("[PASS] Recognition fingerprint reuse detects equal-size external edits with preserved timestamps and rejects malformed data.");
        }

        private static void ScopeEnumerationPreservesNames(string root)
        {
            string source = Path.Combine(root, "scope-names");
            Directory.CreateDirectory(source);
            string nested = Path.Combine(source, "nested"); Directory.CreateDirectory(nested);
            Directory.CreateDirectory(Path.Combine(source, "empty"));
            File.WriteAllText(Path.Combine(source, "one.zip"), "one");
            File.WriteAllText(Path.Combine(source, "ignored.txt"), "ignored");
            File.WriteAllText(Path.Combine(nested, "two.ZIP"), "two");
            HashSet<string> extensions = new HashSet<string>(new[] { "zip" }, StringComparer.OrdinalIgnoreCase);
            foreach (bool recursive in new[] { false, true })
            {
                HashSet<string> expected = new HashSet<string>(Directory.EnumerateFiles(source, "*",
                    recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly)
                    .Where(x => String.Equals(Path.GetExtension(x), ".zip", StringComparison.OrdinalIgnoreCase)), StringComparer.OrdinalIgnoreCase);
                Assert(expected.SetEquals(ScopeFileNames.Enumerate(source, recursive, extensions, null)),
                    "scope enumeration must preserve recursive scope, mixed-case extensions and empty folders");
            }
            bool canceled = false;
            try { ScopeFileNames.Enumerate(source, true, extensions, () => true).ToList(); }
            catch (OperationCanceledException) { canceled = true; }
            Assert(canceled, "scope validation must honor cancellation");
            File.Delete(Path.Combine(source, "one.zip"));
            File.Move(Path.Combine(nested, "two.ZIP"), Path.Combine(source, "renamed.zip"));
            Assert(ScopeFileNames.Enumerate(source, true, extensions, null).Single() == Path.Combine(source, "renamed.zip"),
                "scope enumeration cannot reuse names after rename and deletion");
            Console.WriteLine("[PASS] Scope enumeration preserves names, depth, exclusions, empty directories, cancellation and file changes.");
        }

        private static void IndexedTimestampOrderingDoesNotReadDisk(string root)
        {
            FileInfo old = new FileInfo(Path.Combine(root, "absent-old.zip"));
            FileInfo latest = new FileInfo(Path.Combine(root, "absent-new.zip"));
            FileInfo same = new FileInfo(Path.Combine(root, "absent-same.zip"));
            FileInfo unknown = new FileInfo(Path.Combine(root, "absent-unknown.zip"));
            DateTime now = DateTime.UtcNow;
            Dictionary<string, DateTime> timestamps = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);
            timestamps[old.FullName] = now.AddDays(-1);
            timestamps[latest.FullName] = now;
            timestamps[same.FullName] = now;
            List<FileInfo> ordered = EverythingService.OrderByIndexedModificationTime(
                new[] { old, latest, same, unknown }, timestamps);
            Assert(ordered.SequenceEqual(new[] { latest, same, old, unknown }),
                "latest-first ordering must use SDK timestamps even for nonexistent files; ties stable, missing last");
            Console.WriteLine("[PASS] Latest-first SDK result ordering uses indexed timestamps without physical file reads.");
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
                SharedDiscoveryReusesOneQuery(testRoot);
                MetadataChangesPreserveRecognition(testRoot);
                LargeCacheRecoveryBenchmark(testRoot);
                CorruptCacheIsPreservedAndRebuilt(testRoot);
                TagRuleChangesInvalidateOnlyDependentCaches(testRoot);
                ColdSdkLagCannotOmitFiles(testRoot);
                IndexedTimestampOrderingDoesNotReadDisk(testRoot);
                ScopeEnumerationPreservesNames(testRoot);
                RecognitionVersionCacheTracksContent(testRoot);
                CacheWritesAndUnrelatedFilesDoNotInvalidateDiscovery(testRoot);
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
                ScanSettingsSurviveRestart(testRoot);
                RestoredPreviewSkipsOnlyUnchangedRows();
                StartupTranslationsUpgradeOldLanguagePacks(testRoot);
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

        private static void CacheWritesAndUnrelatedFilesDoNotInvalidateDiscovery(string root)
        {
            string source = Path.Combine(root, "cache-in-source"); Directory.CreateDirectory(source);
            File.WriteAllText(Path.Combine(source, "book.zip"), "test");
            CountingProvider inner = new CountingProvider();
            using (FileIndexCacheDatabase db = new FileIndexCacheDatabase(Path.Combine(source, "cache.db")))
            {
                db.Open();
                using (PersistentSourceIndexProvider provider = new PersistentSourceIndexProvider(inner, db))
                {
                    provider.SearchFiles(source, false, 0, new HashSet<string>(), new[] { ".zip" }, null, null);
                    long revision = provider.SnapshotRevision;
                    db.MarkClean();
                    File.WriteAllText(Path.Combine(source, "settings.txt"), "unrelated");
                    File.Move(Path.Combine(source, "settings.txt"), Path.Combine(source, "renamed-settings.txt"));
                    Thread.Sleep(100);
                    provider.SearchFiles(source, false, 0, new HashSet<string>(), new[] { ".zip" }, null, null);
                    Assert(inner.Calls == 1 && provider.SnapshotRevision == revision,
                        "cache journals and unrelated settings changes must not trigger repeated discovery");
                    SearchResult all = provider.SearchFiles(source, false, 0, new HashSet<string>(), new[] { ".zip", ".db" }, null, null);
                    Assert(all.Files.Count == 1 && all.Files.All(x => x.Name != "cache.db"), "the source index must never expose its own database for archiving");
                    long scoped = provider.GetSnapshotRevision(source, false, new HashSet<string>(), new[] { ".zip" });
                    string other = Path.Combine(root, "other-source"); Directory.CreateDirectory(other);
                    string otherFile = Path.Combine(other, "other.zip"); File.WriteAllText(otherFile, "other");
                    provider.SearchFiles(other, false, 0, new HashSet<string>(), new[] { ".zip" }, null, null);
                    long otherRevision = provider.GetSnapshotRevision(other, false, new HashSet<string>(), new[] { ".zip" });
                    File.AppendAllText(otherFile, "changed");
                    DateTime deadline = DateTime.UtcNow.AddSeconds(3);
                    while (provider.GetSnapshotRevision(other, false, new HashSet<string>(), new[] { ".zip" }) == otherRevision && DateTime.UtcNow < deadline) Thread.Sleep(10);
                    Assert(provider.GetSnapshotRevision(other, false, new HashSet<string>(), new[] { ".zip" }) > otherRevision &&
                        provider.GetSnapshotRevision(source, false, new HashSet<string>(), new[] { ".zip" }) == scoped,
                        "inactive roots must not supersede validation of an unchanged active context");
                }
            }
            Console.WriteLine("[PASS] Cache writes and unrelated files cannot create validation loops; the database is excluded from its own scan.");
        }

        private static void TagRuleChangesInvalidateOnlyDependentCaches(string root)
        {
            string source = Path.Combine(root, "tag-cache"); Directory.CreateDirectory(source);
            string path = Path.Combine(source, "[Author] Tags.zip"); File.WriteAllText(path, "test");
            using (FileIndexCacheDatabase db = new FileIndexCacheDatabase(Path.Combine(root, "tag-cache.db")))
            {
                db.Open(); long id = db.EnsureRoot("Source", source, "ContractFake");
                db.ReplaceSourceSnapshot(id, source, "ContractFake", new[] { new FileInfo(path) });
                long fileId = db.LoadSourceFiles(id)[path].FileId;
                ParsedMetadataCacheService parsed = new ParsedMetadataCacheService(db, "tags-v1");
                parsed.StoreAuthorCandidates(path, Path.GetFileName(path), new[] { "Author" }); parsed.Flush();
                RecognitionFactCacheService recognized = new RecognitionFactCacheService(db, "aliases", "entities", "public", "rules|Tag=tags-v1");
                recognized.Store(new PlanItem { SourcePath = path, FileName = Path.GetFileName(path), Author = "Author", StatusCode = PlanStatusCode.Matched });
                recognized.Flush();
                List<string> candidates; RecognitionCacheEntry fact;
                Assert(parsed.TryGetAuthorCandidates(path, Path.GetFileName(path), out candidates) &&
                    recognized.TryGet(path, Path.GetFileName(path), out fact), "baseline parser and recognition facts should be valid");
                parsed.UpdateTagCleaningVersion("tags-v2"); recognized.UpdateRuleVersion("rules|Tag=tags-v2");
                Assert(!parsed.TryGetAuthorCandidates(path, Path.GetFileName(path), out candidates) &&
                    !recognized.TryGet(path, Path.GetFileName(path), out fact), "tag edits must invalidate both parsing and downstream recognition");
                Assert(db.LoadSourceFiles(id)[path].FileId == fileId && db.LoadParsedMetadata().Count == 1 && db.LoadRecognitionCache().Count == 1,
                    "dependency invalidation must preserve file identities and legacy cache rows");
            }
            Console.WriteLine("[PASS] Tag-rule fingerprints invalidate parsing and recognition lazily without deleting the file index or old cache rows.");
        }

        private static void CorruptCacheIsPreservedAndRebuilt(string root)
        {
            string path = Path.Combine(root, "corrupt.db"); File.WriteAllText(path, "corrupt cache fixture");
            using (FileIndexCacheDatabase db = new FileIndexCacheDatabase(path))
            {
                db.Open(); CheckCacheRebuilt(db, root);
            }
            string[] backups = Directory.GetFiles(root, "corrupt.db.corrupt-*");
            Assert(backups.Length == 1 && File.ReadAllText(backups[0]) == "corrupt cache fixture", "corrupt cache must be preserved byte-for-byte");
            FileIndexCacheDatabase disposed = new FileIndexCacheDatabase(Path.Combine(root, "disposed.db"));
            disposed.Open(); disposed.Dispose(); bool rejected = false;
            try { disposed.LoadDisplayedSession(); } catch (ObjectDisposedException) { rejected = true; }
            Assert(rejected, "late restoration must not reopen a database after window shutdown");
            Console.WriteLine("[PASS] Corrupt cache is preserved and rebuilt; disposed databases reject late background access.");
        }

        private static void CheckCacheRebuilt(FileIndexCacheDatabase db, string root)
        {
            long id = db.EnsureRoot("Source", root, "ContractFake");
            Assert(id > 0 && db.LoadSourceFiles(id).Count == 0, "rebuilt cache must be usable and empty");
        }

        private static void SharedDiscoveryReusesOneQuery(string root)
        {
            string source = Path.Combine(root, "shared-discovery"); Directory.CreateDirectory(source);
            File.WriteAllText(Path.Combine(source, "book.zip"), "test");
            CountingProvider inner = new CountingProvider();
            using (FileIndexCacheDatabase db = new FileIndexCacheDatabase(Path.Combine(root, "shared.db")))
            {
                db.Open();
                using (PersistentSourceIndexProvider provider = new PersistentSourceIndexProvider(inner, db))
                {
                    Func<SearchResult> query = delegate { return provider.SearchFiles(source, true, 0,
                        new HashSet<string>(), new[] { ".zip" }, null, null); };
                    Task<SearchResult>[] requests = Enumerable.Range(0, 4).Select(i => Task.Run(query)).ToArray();
                    Task.WaitAll(requests);
                    Assert(inner.Calls == 1 && requests.All(x => x.Result.Files.Count == 1),
                        "concurrent matching requests must share discovery and reconciliation");
                    using (ScanWarmupService preparation = new ScanWarmupService(provider))
                    {
                        ScanWarmupMetrics metrics;
                        SearchResult reused = preparation.GetOrRunSource(new ScanWarmupRequest {
                            Source = source, Recursive = true, Extensions = new List<string> { ".zip" }
                        }, null, null, out metrics);
                        Assert(inner.Calls == 1 && metrics.WarmupHit && reused.EverythingQueryCount == 0,
                            "automatic preparation must delegate snapshot validity to the index");
                    }
                }
            }
            Console.WriteLine("[PASS] Concurrent requests and preparation share one discovery; cached projections perform zero Everything queries.");
        }

        private static void MetadataChangesPreserveRecognition(string root)
        {
            string path = Path.Combine(root, "[Author] Metadata.zip");
            PlanItem item = new PlanItem { SourcePath = path, FileName = Path.GetFileName(path),
                Author = "Author", TargetPath = "target.zip", RecognitionScore = 100,
                ManualTargetAuthor = "Confirmed" };
            SourceIndexFileSnapshot metadata = new SourceIndexFileSnapshot {
                FullPath = path, FileSize = 4096, LastWriteTimeUtc = DateTime.UtcNow
            };
            FileIndexDelta delta = new FileIndexDelta { Modified = 1 };
            delta.PlanInvalidatedPaths.Add(path);
            Assert(MainForm.TryRefreshPlanMetadata(item, new FileInfo(path), metadata, delta),
                "provider metadata must update the plan without parsing or reading a nonexistent file");
            Assert(item.FileSize == 4096 && item.Author == "Author" && item.TargetPath == "target.zip" &&
                item.RecognitionScore == 100 && item.ManualTargetAuthor == "Confirmed", "metadata refresh changed recognition or confirmation");
            delta.RecognitionInvalidatedPaths.Add(path);
            Assert(!MainForm.TryRefreshPlanMetadata(item, new FileInfo(path), metadata, delta),
                "recognition-invalidated rows must not take the metadata shortcut");
            delta.RecognitionInvalidatedPaths.Clear();
            delta.PathChanges.Add(new FileIndexPathChange { OldPath = path, NewPath = path + ".renamed" });
            Assert(!MainForm.TryRefreshPlanMetadata(item, new FileInfo(path), metadata, delta),
                "moved or renamed rows require downstream replanning");
            Console.WriteLine("[PASS] Metadata-only changes preserve recognition, target and manual confirmation; rename/rule invalidations bypass the shortcut.");
        }

        private static void LargeCacheRecoveryBenchmark(string root)
        {
            string source = Path.Combine(root, "large-cache"); Directory.CreateDirectory(source);
            List<SourceIndexFileSnapshot> entries = Enumerable.Range(0, 8490).Select(i => new SourceIndexFileSnapshot {
                FullPath = Path.Combine(source, "[Author] " + i + ".zip"), DirectoryPath = source,
                FileName = "[Author] " + i + ".zip", Extension = ".zip", FileSize = 1024,
                LastWriteTimeUtc = new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc)
            }).ToList();
            string dbPath = Path.Combine(root, "large-cache.db");
            const string scope = "CanonicalRecursive=True|Extensions=.zip";
            using (FileIndexCacheDatabase db = new FileIndexCacheDatabase(dbPath))
            {
                db.Open(); long id = db.EnsureRoot("Source", source, "ContractFake");
                db.ReplaceSourceSnapshot(id, source, "ContractFake", entries, scope);
                Dictionary<string, SourceFileIndexEntry> indexed = db.LoadSourceFiles(id);
                using (PersistentSourceIndexProvider provider = new PersistentSourceIndexProvider(new CountingProvider(), db))
                {
                    ScanSessionSnapshot session = provider.StartScanSession(source, new SearchResult {
                        Files = entries.Select(x => new FileInfo(x.FullPath)).ToList()
                    }, 0).ScanSession;
                    List<PlanItem> plans = entries.Select(x => new PlanItem { FileId = indexed[x.FullPath].FileId,
                        SourcePath = x.FullPath, FileName = x.FileName, FileSize = x.FileSize,
                        LastWriteTime = x.LastWriteTimeUtc.ToLocalTime(), Author = "Author", TargetPath = "target.zip" }).ToList();
                    db.UpsertMigrationPlans("benchmark", plans); db.SaveDisplayedSession(session);
                    Stopwatch timer = Stopwatch.StartNew();
                    FileIndexDelta unchanged = db.ReplaceSourceSnapshot(id, source, "ContractFake", entries, scope);
                    timer.Stop(); long validationMs = timer.ElapsedMilliseconds;
                    Assert(unchanged.CacheHits == 8490 && unchanged.Modified == 0 && unchanged.Added == 0 && unchanged.Removed == 0,
                        "8490 unchanged entries must reuse all indexed rows");
                    timer.Restart();
                    List<PlanItem> loaded = db.LoadMigrationPlans("benchmark", session);
                    timer.Stop();
                    Assert(loaded.Count == 8490 && loaded.All(x => x.Author == "Author"), "large cache restoration lost recognition");
                    Console.WriteLine("[BENCH] 8490 synthetic entries: reconciliation=" + validationMs +
                        " ms; plan read=" + timer.ElapsedMilliseconds + " ms; reused=8490; recognition=0 (cache read only).");
                    entries[0].FileSize++;
                    FileIndexDelta changed = db.ReplaceSourceSnapshot(id, source, "ContractFake", entries, scope);
                    Assert(changed.Modified == 1 && changed.CacheHits == 8489, "large cache must isolate one metadata change");
                    entries.RemoveAt(1);
                    changed = db.ReplaceSourceSnapshot(id, source, "ContractFake", entries, scope);
                    Assert(changed.Removed == 1 && changed.CacheHits == 8489, "large cache must isolate one deletion");
                }
            }
            using (FileIndexCacheDatabase db = new FileIndexCacheDatabase(dbPath))
            {
                db.Open();
                Assert(db.LoadDisplayedSession() != null, "last displayed session must survive reopening SQLite");
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
                changed.ManualTargetAuthor = "Confirmed Author";
                changed.ManualTargetName = "Confirmed Name";
                changed.ManualTargetDir = Path.Combine(root, "Confirmed");
                database.UpsertMigrationPlans("contract-v1", new[] { changed });
                database.SaveDisplayedSession(session);
                List<PlanItem> after = database.LoadMigrationPlans("contract-v1", session);
                Assert(after.Count == 64, "a partial plan update must retain the untouched plans");
                Assert(after.Count(delegate(PlanItem item) { return item.Author == "Author 2"; }) == 1,
                    "only one plan should change after an incremental update");
                Assert(after.Count(delegate(PlanItem item) { return item.Author == "Author 1"; }) == 63,
                    "the other 63 plans must remain unchanged");
            }
            using (FileIndexCacheDatabase reopened = new FileIndexCacheDatabase(Path.Combine(testRoot, "plans-batched.db")))
            {
                reopened.Open();
                ScanSessionSnapshot restoredSession = reopened.LoadDisplayedSession();
                List<PlanItem> restored = reopened.LoadMigrationPlans("contract-v1", restoredSession);
                Assert(restored.Count == 64, "restart must restore the displayed session without scanning");
                PlanItem confirmed = restored.Single(delegate(PlanItem item) { return item.Author == "Author 2"; });
                Assert(confirmed.ManualTargetAuthor == "Confirmed Author" && confirmed.ManualTargetName == "Confirmed Name" &&
                    confirmed.ManualTargetDir == Path.Combine(root, "Confirmed"), "manual confirmation must survive restart");
                Assert(reopened.LoadMigrationPlans("different-rules", restoredSession).Count == 0,
                    "changed rules must not restore invalid plans");
            }
        }

        private static void StartupTranslationsUpgradeOldLanguagePacks(string root)
        {
            string directory = Path.Combine(root, "old-languages");
            Directory.CreateDirectory(directory);
            foreach (string code in new[] { "zh-CN", "en-US", "de-DE" })
            {
                File.WriteAllText(Path.Combine(directory, code + ".json"),
                    "{\"meta\":{\"code\":\"" + code + "\",\"name\":\"Legacy\",\"version\":78},\"strings\":{\"Common.OK\":\"OK\"}}");
                LanguageManager manager = new LanguageManager(directory);
                manager.Initialize(code);
                foreach (string key in new[] { "Status.StartupCheckComplete", "Status.StartupRestoredElapsed",
                    "Status.RestoredPreviousScan", "Status.RestoredSourceUnavailable", "Status.RestoreUnavailable", "Status.PlanFilesChanged" })
                    Assert(manager.Get(key) != key, "new status must translate when upgrading old " + code + " language packs: " + key);
                string translated = manager.Format("Status.StartupCheckComplete", 12, 34);
                Assert(translated.Contains("12") && translated.Contains("34"), "startup timing placeholders must format");
                if (code == "zh-CN") Assert(translated.Contains("已恢复缓存"), "Chinese must use the Chinese startup translation");
            }
        }

        private static void RestoredPreviewSkipsOnlyUnchangedRows()
        {
            PlanItem previous = new PlanItem { SourcePath = "C:\\sample.zip", Author = "Author", TargetPath = "C:\\Author\\sample.zip" };
            PlanItem loaded = new PlanItem { SourcePath = previous.SourcePath, Author = previous.Author, TargetPath = previous.TargetPath };
            Assert(MainForm.SamePreview(new[] { previous }, new[] { loaded }), "unchanged loaded rows should reuse the restored grid");
            loaded.TargetPath = "C:\\Other\\sample.zip";
            Assert(!MainForm.SamePreview(new[] { previous }, new[] { loaded }), "changed destination must refresh the preview");
            loaded.TargetPath = previous.TargetPath;
            loaded.CandidatePaths.Add("C:\\Candidate");
            Assert(!MainForm.SamePreview(new[] { previous }, new[] { loaded }), "changed candidates must refresh the preview");
            Assert(!MainForm.SamePreview(new[] { previous }, new PlanItem[0]), "deleted rows must refresh the preview");
            loaded.CandidatePaths.Clear();
            loaded.ConflictSourcePaths.Add("C:\\duplicate.zip");
            Assert(!MainForm.SamePreview(new[] { previous }, new[] { loaded }), "changed conflict evidence must refresh the preview");
            loaded.ConflictSourcePaths.Clear(); loaded.ExclusionRuleName = "new rule";
            Assert(!MainForm.SamePreview(new[] { previous }, new[] { loaded }), "changed exclusion evidence must refresh the preview");
        }

        private static void ScanSettingsSurviveRestart(string root)
        {
            string path = Path.Combine(root, "settings.ini");
            UserSettingsStore store = new UserSettingsStore(path);
            store.UpdateScanSettings(1500, true, 4);
            store.UpdateListViewFilter("matched");
            store.UpdateLanguage("en-US");
            UserSettingsData restored = new UserSettingsStore(path).Load();
            Assert(restored.ScanLimit == 1500 && restored.ScanRecursive && restored.ScanMode == 4,
                "scan settings must survive restart and unrelated settings saves");
            Assert(restored.ListViewFilter == "matched", "status filter must survive unrelated settings saves");
            store.UpdateListViewFilter("invalid-filter");
            Assert(new UserSettingsStore(path).Load().ListViewFilter == "all", "unknown historical filters must fall back to all");
            store.UpdateScanSettings(0, false, 0);
            restored = new UserSettingsStore(path).Load();
            Assert(restored.ScanLimit == 0 && !restored.ScanRecursive && restored.ScanMode == 0,
                "unlimited count and unchecked recursive state must round-trip");
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
            Assert(restored.StartupRestoreMs == -1 && restored.BackgroundValidationMs == -1 &&
                restored.EverythingQueryCount == -1 && restored.FirstInteractiveMs == -1 && restored.ActualRecognitions == -1,
                "historical entries must report absent new fields as unavailable, not zero");
            ScanPerformanceDiagnostics.Record(new ScanPerformanceEntry { Time = time.AddSeconds(1),
                StartupRestoreMs = 25, BackgroundValidationMs = 200, EverythingQueryCount = 0,
                FirstInteractiveMs = 100, StartupWindowShownMs = 75, StartupInitializationStages = "10,20,0,30", ActualRecognitions = 0,
                StartupUiTrace = "Menu:25;Responsive:100",
                SdkPrepareMs = 0, EverythingWaitMs = 500, EverythingReadMs = 40, DiscoveryCheckMs = 20,
                DiscoverySetMs = 0, DiscoveryEnumerateMs = 19, DiscoveryCompareMs = 0 });
            ScanPerformanceDiagnostics.Initialize(logPath, true, false, true);
            ScanPerformanceEntry modern = ScanPerformanceDiagnostics.Snapshot().Last();
            Assert(modern.StartupRestoreMs == 25 && modern.BackgroundValidationMs == 200 && modern.EverythingQueryCount == 0 &&
                modern.FirstInteractiveMs == 100 && modern.ActualRecognitions == 0,
                "new metrics, including explicit zero counts, must survive restart");
            Assert(restored.StartupWindowShownMs == -1 && modern.StartupWindowShownMs == 75, "window initialization metrics must remain optional and round-trip");
            Assert(String.IsNullOrEmpty(restored.StartupInitializationStages) && modern.StartupInitializationStages == "10,20,0,30", "startup initialization details must preserve zero and remain optional");
            Assert(String.IsNullOrEmpty(restored.StartupUiTrace) && modern.StartupUiTrace == "Menu:25;Responsive:100",
                "nested startup trace must remain optional and retain individual point/interval values");
            Assert(restored.DiscoverySetMs == -1 && restored.DiscoveryEnumerateMs == -1 && restored.DiscoveryCompareMs == -1 &&
                modern.DiscoverySetMs == 0 && modern.DiscoveryEnumerateMs == 19 && modern.DiscoveryCompareMs == 0,
                "completeness substages must distinguish missing history from measured zero");
            Assert(ScanPerformanceDiagnostics.WarmupEnabled, "legacy disabled warmup must not disable automatic preparation");
            Assert(restored.SdkPrepareMs == -1 && restored.EverythingWaitMs == -1 && restored.EverythingReadMs == -1 && restored.DiscoveryCheckMs == -1,
                "old diagnostics must not invent detailed provider timings");
            Assert(modern.SdkPrepareMs == 0 && modern.EverythingWaitMs == 500 && modern.EverythingReadMs == 40 && modern.DiscoveryCheckMs == 20,
                "provider substage timings, including zero, must survive history reload");

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
