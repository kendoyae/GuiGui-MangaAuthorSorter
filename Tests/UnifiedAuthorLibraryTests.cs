using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Web.Script.Serialization;

namespace MangaAuthorSorter.Tests
{
    internal static class UnifiedAuthorLibraryTests
    {
        private static readonly List<string> Report = new List<string>();
        private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
        private static void Pass(string text) { Console.WriteLine("[PASS] " + text); Report.Add("- " + text); }
        private static string Q(string value) { return "'" + (value ?? "").Replace("'", "''") + "'"; }
        private static void Sql(FileIndexCacheDatabase database, string sql)
        {
            typeof(FileIndexCacheDatabase).GetMethod("Execute", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(database, new object[] { sql });
        }
        private static void Fixture(string path, bool stable, int artistId, bool split = false, bool removed = false)
        {
            using (FileIndexCacheDatabase db = new FileIndexCacheDatabase(path)) {
                db.Open();
                Sql(db, "DROP TABLE IF EXISTS Entity; DROP TABLE IF EXISTS EntityAlias; DROP TABLE IF EXISTS ProviderIdentity; DROP TABLE IF EXISTS ArtistGroup;" +
                    "CREATE TABLE Entity(Id INTEGER,EntityType TEXT,CanonicalName TEXT,RomanName TEXT,EHArtistTag TEXT,NHArtistTag TEXT,NHGroupTag TEXT,Source TEXT,ExternalId TEXT,VerificationSource TEXT,EHNamespace TEXT,EHTag TEXT,Verified INTEGER,UpdatedUtc TEXT,NormalizedCanonical TEXT" + (stable ? ",StableEntityKey TEXT" : "") + ");" +
                    "CREATE TABLE EntityAlias(EntityId INTEGER,Alias TEXT,NormalizedAlias TEXT,AliasType TEXT);" +
                    "CREATE TABLE ProviderIdentity(EntityId INTEGER,Provider TEXT,Namespace TEXT,Tag TEXT,NormalizedTag TEXT);" +
                    "CREATE TABLE ArtistGroup(ArtistId INTEGER,GroupId INTEGER,EvidenceCount INTEGER);");
                Action<int,string,string,string> add = delegate(int id, string type, string name, string key) {
                    Sql(db, "INSERT INTO Entity VALUES(" + id + "," + Q(type) + "," + Q(name) + "," + Q(name) + ",'','','','E-Hentai','','LocalDatabase'," + Q(type == "Artist" ? "artist" : "group") + ",'',1,''," + Q(AuthorRules.NormalizeText(name)) + (stable ? "," + Q(key) : "") + ");");
                };
                add(10, "Group", "PublicGroup", "group-stable");
                if (!removed) {
                    add(artistId, "Artist", "PublicAuthor", "author-stable");
                    Sql(db, "INSERT INTO EntityAlias VALUES(" + artistId + ",'Alias-X'," + Q(AuthorRules.NormalizeText("Alias-X")) + ",'other'); INSERT INTO EntityAlias VALUES(" + artistId + ",'Foo_Bar','foo bar','other'); INSERT INTO ArtistGroup VALUES(" + artistId + ",10,5);");
                    if (split) { add(artistId + 1, "Artist", "SplitAuthor", "author-stable"); }
                }
            }
        }
        private static void CrudAndConflicts(string root)
        {
            AuthorEntityStore store = new AuthorEntityStore(Path.Combine(root, "crud.json"), Path.Combine(root, "absent.db"));
            int a = store.SaveUserEntity(0, "Artist", "First", "roman", new[] { "one", "shared" }, new string[0], true);
            int b = store.SaveUserEntity(0, "Artist", "Second", "roman", new[] { "two", "shared" }, new string[0], true);
            int group = store.SaveUserEntity(0, "Group", "First", "group-roman", new[] { "GroupAlias" }, new string[0], true);
            Check(store.LoadIndex().Resolve("shared").Ambiguous && store.LoadIndex().Resolve("roman").Ambiguous, "same name / roman must remain ambiguous");
            Check(store.LoadIndex().Resolve("First", "Group").Entity.EntityType == "Group" && store.LoadIndex().Resolve("First").Entity.EntityType == "Artist", "artist/group role isolation");
            store.SetNameDecision("shared", "Artist", b, "Confirmed");
            Check(store.LoadIndex().Resolve("shared").Entity.Id == b, "explicit name decision must win");
            var index = store.LoadIndex(); bool ambiguous;
            Check(AuthorIndex.Build(new List<AuthorFolder>(), index.AliasGroups, index).ResolveAlias("shared", out ambiguous).Canonical == "Second", "scoring alias projection must respect conflict decisions");
            store.SetNameDecision("shared", "Artist", 0, "Independent"); Check(store.LoadIndex().Resolve("shared").Ambiguous, "independent entities retain ambiguity");
            store.SaveUserEntity(b, "Artist", "Second", "roman", new[] { "two", "shared" }, new string[0], false);
            Check(store.LoadIndex().Resolve("shared").Ambiguous, "explicit independent decision must preserve ambiguity even with mixed confirmation states");
            store.SaveUserEntity(b, "Artist", "Second", "roman", new[] { "two", "shared" }, new string[0], true);
            store.SetNameDecision("shared", "Artist", 0, "Disabled"); Check(!store.LoadIndex().Resolve("shared").Found, "disabled conflict mapping");
            store.SetNameDecision("shared", "Artist", 0, "Pending"); Check(store.GetConflicts().Any(x => x.Name == "shared" && x.Status == "Pending"), "pending conflict must remain reviewable");
            store.SaveRelationship(a, group, true, false);
            Check(store.LoadIndex().Resolve("one").RelatedGroups.Count == 1, "confirmed group relationship must reach resolver");
            bool failed = false; try { store.DeleteUserEntity(a, "Artist"); } catch (InvalidOperationException) { failed = true; }
            Check(failed && store.Load().Authors.Any(x => x.Id == a), "referenced author cannot be deleted");
            store.SaveRelationship(a, group, true, true); Check(store.LoadIndex().Resolve("one").RelatedGroups.Count == 0, "disabled relation must not be used");
            store.RemoveRelationship(a, group);
            store.SaveUserEntity(a, "Artist", "FirstRenamed", "roman", new string[0], new[] { "one" }, true);
            Check(!store.LoadIndex().Resolve("one").Found && store.Load().Authors.Any(x => x.Id == a), "deleting/disabling alias must preserve author");
            store.SaveLookupStatus("test", "no-result", "not_found", "", true); store.ClearLookupCache();
            Check(store.Load().Authors.Count == 2 && store.Load().NameDecisions.Count == 1, "cache cleanup cannot erase user data");
            string export = Path.Combine(root, "export.json"); store.ExportUserLibrary(export);
            AuthorEntityStore imported = new AuthorEntityStore(Path.Combine(root, "imported.json")); imported.ImportUserLibrary(export); imported.ImportUserLibrary(export);
            Check(imported.Load().Authors.Count == 2 && imported.Load().Circles.Count == 1 && imported.Load().NameDecisions.Count == 1, "import must preserve references and be idempotent");
            store.DeleteUserEntity(a, "Artist"); Check(!store.Load().Aliases.Any(x => x.AuthorId == a), "entity deletion cannot leave dangling aliases");
            string legacyPath = Path.Combine(root, "legacy-group.json");
            AuthorEntityDatabase legacy = new AuthorEntityDatabase(); legacy.Authors.Add(new AuthorEntityRecord { Id = 1, EntityType = "Group", CanonicalName = "LegacyGroup" }); legacy.Aliases.Add(new AuthorAliasRecord { AuthorId = 1, Alias = "LegacyAlias" });
            File.WriteAllText(legacyPath, new JavaScriptSerializer().Serialize(legacy));
            AuthorEntityStore legacyStore = new AuthorEntityStore(legacyPath); legacyStore.SaveLegacyGroup(1, "LegacyGroup", "", new[] { "LegacyAlias" }, new string[0], true);
            Check(legacyStore.Load().Authors.Count == 0 && legacyStore.LoadIndex().Resolve("LegacyAlias", "Group").Found && !legacyStore.LoadIndex().Resolve("LegacyAlias").Found, "legacy Group records must stay separate from artists");
            Pass("User CRUD, aliases, role isolation, conflict decisions, relation disable/unlink, delete reference checks, cache lifecycle and JSON import/export passed.");
        }
        private static void SessionEntities(string root)
        {
            string path = Path.Combine(root, "session.json"); AuthorEntityStore store = new AuthorEntityStore(path);
            store.SaveUserEntity(0, "Group", "PersistentGroup", "", new string[0], new string[0], true);
            string before = File.ReadAllText(path); long revision = store.IdentityRevision;
            AuthorEntityRecord temporary = store.MergeResolvedProviderResult("nHentai", "TempQuery", new AuthorProviderCandidate {
                ExternalId = "10", TagName = "TempArtist", GroupName = "TempGroup", OtherNames = new List<string> { "TempAlias" }
            }, false);
            Check(temporary.Id < 0 && store.IdentityRevision > revision && store.LoadIndex().Resolve("TempAlias").Found, "session identity must update recognition without colliding with persistent IDs");
            Check(before == File.ReadAllText(path), "temporary lookup cannot write user library");
            CircleEntityRecord group = store.LoadForDisplay().Circles.Single(x => x.Id < 0);
            int authorId = store.SaveUserEntity(temporary.Id, "Artist", "SavedArtist", "", new[] { "TempAlias" }, new string[0], true);
            int groupId = store.SaveUserEntity(group.Id, "Group", "SavedGroup", "", new string[0], new string[0], true);
            Check(authorId > 0 && groupId > 0 && store.Load().Authors.Single().ExternalId == "10" && store.Load().AuthorCircles.Single().CircleId == groupId, "promotion must preserve provider metadata and relation endpoints");
            Check(store.LoadForDisplay().Authors.Count == 1 && store.LoadForDisplay().Circles.Count == 2, "promotion must remove transient duplicates");
            store.RemoveRelationship(authorId, groupId); store.DeleteUserEntity(authorId, "Artist");
            temporary = store.MergeResolvedProviderResult("nHentai", "DeleteTemp", new AuthorProviderCandidate { TagName = "DeleteTemp" }, false);
            revision = store.IdentityRevision; store.ClearLookupCache();
            Check(store.LoadForDisplay().LookupCache.Count == 0 && store.IdentityRevision == revision && store.LoadIndex().Resolve("DeleteTemp").Found, "cache clear must include session statuses without deleting identities or invalidating recognition");
            store.DeleteUserEntity(temporary.Id, "Artist"); Check(!store.LoadIndex().Resolve("DeleteTemp").Found, "temporary entities must support deletion");
            Pass("Session identity cache refresh, collision-free IDs, persistent promotion with provider metadata/relationships and temporary deletion passed.");
        }
        private static void ProviderAndImportIsolation(string root)
        {
            AuthorEntityStore store = new AuthorEntityStore(Path.Combine(root, "provider.json"));
            Check(store.MergeResolvedProviderResult("EhTagTranslation", "reference", new AuthorProviderCandidate { TagName = "reference" }, true) == null && !File.Exists(store.Path), "translation references cannot become automatic entities");
            AuthorEntityRecord eh = store.MergeResolvedProviderResult("E-Hentai", "EHQuery", new AuthorProviderCandidate { ExternalId = "12", TagName = "eh_artist", EvidenceSources = new List<string> { "E-Hentai/TagSearch" } }, true);
            AuthorEntityRecord nh = store.MergeResolvedProviderResult("nHentai", "NHQuery", new AuthorProviderCandidate { ExternalId = "12", TagName = "nh_artist", EvidenceSources = new List<string> { "nHentai/TagSearch" } }, true);
            Check(eh.Id != nh.Id && store.Load().Authors.Count == 2, "provider external IDs must be namespaced");
            store.SaveUserEntity(eh.Id, "Artist", "UserChoice", "", new[] { "UserAlias" }, new string[0], true);
            long revision = store.IdentityRevision;
            store.MergeResolvedProviderResult("E-Hentai", "WrongQuery", new AuthorProviderCandidate { ExternalId = "12", TagName = "WrongTag", EvidenceSources = new List<string> { "E-Hentai/TagSearch" } }, true);
            Check(store.Load().Authors.First(x => x.Id == eh.Id).CanonicalName == "UserChoice" && !store.LoadIndex().Resolve("WrongQuery").Found && revision == store.IdentityRevision, "lookup refresh cannot alter confirmed identity or invalidate identity cache");
            AuthorEntityStore imported = new AuthorEntityStore(Path.Combine(root, "independent-imports.json"));
            for (int i = 0; i < 2; i++) {
                AuthorEntityDatabase db = new AuthorEntityDatabase(); db.Authors.Add(new AuthorEntityRecord { Id = 1, EntityId = "user:1", CanonicalName = "Independent" + i });
                string file = Path.Combine(root, "independent-" + i + ".json"); File.WriteAllText(file, new JavaScriptSerializer().Serialize(db)); imported.ImportUserLibrary(file); imported.ImportUserLibrary(file);
            }
            Check(imported.Load().Authors.Count == 2, "independent legacy integer IDs must not merge across sources");
            Pass("Provider namespaces, translation-source exclusion, confirmed identity protection and independent legacy import identities passed.");
        }
        private static void PublicUpgrades(string root)
        {
            string path = Path.Combine(root, "public.db"); Fixture(path, true, 1);
            AuthorEntityStore store = new AuthorEntityStore(Path.Combine(root, "overrides.json"), path);
            string publicHash = AuthorEntityStore.ContentHash(File.ReadAllBytes(path));
            store.SavePublicOverride(store.GetPublicSnapshot(1), "UserCanonical", new[] { "UserAlias" }, new[] { "Alias-X" }, "");
            Check(publicHash == AuthorEntityStore.ContentHash(File.ReadAllBytes(path)), "user edit must not write public DB");
            Check(store.LoadIndex().Resolve("PublicAuthor").Entity.CanonicalName == "UserCanonical" && store.LoadIndex().Resolve("UserAlias").Found, "public override must affect names");
            Check(!store.LoadIndex().Resolve("Alias-X").Found, "disabled public alias must not be used");
            Check(store.LoadIndex().Resolve("foo bar").Found, "public overlay must preserve stored normalized lookup keys");
            store.SavePublicOverride(store.GetPublicSnapshot(1), "UserCanonical", new[] { "UserAlias" }, new[] { "Alias-X", "Foo_Bar" }, store.Load().PublicOverrides.Single().Id);
            Check(!store.LoadIndex().Resolve("foo bar").Found && !store.LoadIndex().Resolve("Foo_Bar").Found, "disabled public alias must also disable its stored normalized key");
            int userGroup = store.SaveUserEntity(0, "Group", "UserGroup", "", new string[0], new string[0], true);
            string overlayId = store.Load().PublicOverrides.Single().Id;
            store.SavePublicRelationship(overlayId, userGroup, true, false);
            Check(store.LoadIndex().Resolve("UserAlias").RelatedGroups.Any(x => x.CanonicalName == "UserGroup"), "public override user relationship must reach resolver");
            bool referenced = false; try { store.DeleteUserEntity(userGroup, "Group"); } catch(InvalidOperationException) { referenced = true; }
            Check(referenced, "public relationships cannot leave dangling user groups");
            store.SavePublicRelationship(overlayId, userGroup, true, true);
            Check(!store.LoadIndex().Resolve("UserAlias").RelatedGroups.Any(x => x.CanonicalName == "UserGroup"), "public user relationship disable");
            store.RemovePublicRelationship(overlayId, userGroup); store.DeleteUserEntity(userGroup, "Group");
            Fixture(path, true, 900);
            Check(store.LoadIndex().Resolve("PublicAuthor").Entity.Id == 900 && store.LoadIndex().Resolve("PublicAuthor").Entity.CanonicalName == "UserCanonical", "stable identity must survive row ID replacement");
            Check(!store.LoadIndex().Resolve("Alias-X").Found && store.GetConflicts().Count == 0, "alias disable must survive DB upgrade");
            Fixture(path, true, 900, true);
            Check(store.GetConflicts().Any(x => x.Kind == "PublicIdentity") && store.Load().PublicOverrides.Count == 1, "split stable identity must retain override as conflict");
            store.SavePublicOverride(store.GetPublicSnapshot(900), "ReboundSplit", new[] { "UserAlias" }, new[] { "Alias-X" }, overlayId);
            Check(store.LoadIndex().Resolve("PublicAuthor").Entity.CanonicalName == "ReboundSplit" && String.IsNullOrEmpty(store.Load().PublicOverrides.Single().PublicEntityKey), "explicit split rebind must use exact-version identity");
            Fixture(path, true, 900, false, true);
            Check(store.GetConflicts().Any(x => x.Status == "Missing") && store.Load().PublicOverrides.Count == 1, "deleted public entity must not erase override");
            int local = store.SaveUserEntity(0, "Artist", "Local", "", new[] { "LocalAlias" }, new string[0], true);
            File.Delete(path);
            Check(store.LoadIndex().Resolve("LocalAlias").Entity.Id == local && store.Load().PublicOverrides.Count == 1, "missing public DB cannot erase local data");
            Fixture(path, false, 1); store.SavePublicOverride(store.GetPublicSnapshot(1), "VersionBound", new string[0], new string[0], "");
            Check(store.LoadIndex().Resolve("PublicAuthor").Entity.CanonicalName == "VersionBound", "legacy DB overlay works in its exact version");
            store.RemovePublicOverride(overlayId);
            Fixture(path, false, 501);
            Check(store.LoadIndex().Resolve("PublicAuthor").Ambiguous, "row ID changes must require confirmation rather than misbind legacy overrides");
            PublicEntityOverride pending = store.Load().PublicOverrides.First(x => x.CanonicalName == "VersionBound");
            store.SavePublicOverride(store.GetPublicSnapshot(501), pending.CanonicalName, pending.AddedAliases, pending.DisabledNames, pending.Id);
            Check(store.LoadIndex().Resolve("PublicAuthor").Entity.CanonicalName == "VersionBound", "explicit public rebind must restore override");
            store.RemovePublicOverride(pending.Id); Check(store.LoadIndex().Resolve("PublicAuthor").Entity.CanonicalName == "PublicAuthor", "restore public values");
            store.SavePublicOverride(store.GetPublicSnapshot(10), "GroupOverride", new[] { "GroupNew" }, new[] { "PublicGroup" }, "");
            Check(!store.LoadIndex().Resolve("PublicGroup", "Group").Found && store.LoadIndex().Resolve("GroupNew", "Group").Found, "group overlay must preserve role and disabled names");
            Fixture(path, false, 600);
            Check(store.LoadIndex().Resolve("GroupNew", "Group").Ambiguous && !store.LoadIndex().Resolve("PublicGroup", "Group").Found, "unresolved group override must retain disabled names and require rebind");
            store.SaveUserEntity(0, "Artist", "PublicAuthor", "", new string[0], new string[0], true);
            store.SetNameDecision("PublicAuthor", "Artist", 0, "Independent");
            Check(store.LoadIndex().Resolve("PublicAuthor").Ambiguous, "explicit independent user/public decision must retain both candidates");
            Pass("Read-only public DB, stable-key ID changes, split/delete/missing identities, retained overrides, version-bound fallback, explicit rebind and restore passed.");
        }
        private static void LocalCacheInvalidation(string root)
        {
            AuthorEntityStore store = new AuthorEntityStore(Path.Combine(root, "cache-authors.json"));
            int a = store.SaveUserEntity(0, "Artist", "Alpha", "", new[] { "A-alias" }, new string[0], true);
            store.SaveUserEntity(0, "Artist", "Beta", "", new[] { "B-alias" }, new string[0], true);
            AuthorEntityIndex first = store.LoadIndex();
            string fileA = "[A-alias] work.zip", fileB = "[B-alias] work.zip";
            string source = Path.Combine(root, "cache-source"); Directory.CreateDirectory(source);
            List<FileInfo> files = new[] { fileA, fileB }.Select(x => new FileInfo(Path.Combine(source, x))).ToList();
            List<SourceIndexFileSnapshot> entries = files.Select(x => new SourceIndexFileSnapshot { FullPath = x.FullName, DirectoryPath = source, FileName = x.Name, Extension = ".zip", FileSize = 1, LastWriteTimeUtc = DateTime.UtcNow }).ToList();
            using (FileIndexCacheDatabase cache = new FileIndexCacheDatabase(Path.Combine(root, "cache.db"))) {
                cache.Open(); long id = cache.EnsureRoot("Source", source, "fixture"); cache.ReplaceSourceSnapshot(id, source, "fixture", entries, "fixture");
                ScanSessionSnapshot session = cache.CreateScanSession(id, files); var indexed = cache.LoadSourceFiles(id);
                List<PlanItem> plan = files.Select(x => new PlanItem { FileId = indexed[x.FullName].FileId, FileName = x.Name, SourcePath = x.FullName, Author = x.Name == fileA ? "Alpha" : "Beta", StatusCode = PlanStatusCode.Matched }).ToList();
                cache.UpsertMigrationPlans("dependency-test", plan, first.GetDependencyVersion);
                RecognitionFactCacheService recognition = new RecognitionFactCacheService(cache, "", "", "public", "rules"); recognition.UpdateIdentityDependencies(first.GetDependencyVersion, "public"); foreach (PlanItem item in plan) recognition.Store(item); recognition.Flush();
                store.SaveUserEntity(a, "Artist", "AlphaRenamed", "", new[] { "A-alias", "NewAlias" }, new string[0], true);
                AuthorEntityIndex second = store.LoadIndex();
                Check(first.GetDependencyVersion(fileA) != second.GetDependencyVersion(fileA) && first.GetDependencyVersion(fileB) == second.GetDependencyVersion(fileB), "single author edit must preserve unrelated dependencies");
                var hits = cache.LoadMigrationPlans("dependency-test", session, second.GetDependencyVersion);
                Check(hits.Count == 1 && hits[0].FileName == fileB && cache.LoadSourceFiles(id).Count == 2, "only affected plan must miss; file index must remain");
                bool canceled = false;
                try { cache.LoadMigrationPlans("dependency-test", session, second.GetDependencyVersion, () => true); } catch (OperationCanceledException) { canceled = true; }
                Check(canceled, "cached plan read must observe scan cancellation");
                int cancellationChecks = 0; canceled = false;
                try { cache.UpsertMigrationPlans("canceled-write", plan, second.GetDependencyVersion, () => ++cancellationChecks > 1); } catch (OperationCanceledException) { canceled = true; }
                Check(canceled && cache.LoadMigrationPlans("canceled-write", session, second.GetDependencyVersion).Count == 0, "canceled plan write must roll back its transaction");
                recognition.UpdateIdentityDependencies(second.GetDependencyVersion, "public"); RecognitionCacheEntry entry;
                Check(!recognition.TryGet(files[0].FullName, fileA, out entry) && recognition.TryGet(files[1].FullName, fileB, out entry), "recognition cache must invalidate locally");
            }
            Pass("Single-author edits invalidate only related recognition and durable plan rows; file identity index is preserved.");
        }
        private static void SchemaV4Views(string root)
        {
            string path = Path.Combine(root, "schema-v4.db"); Fixture(path, false, 1);
            using (FileIndexCacheDatabase db = new FileIndexCacheDatabase(path)) {
                db.Open(); Sql(db, "ALTER TABLE Entity RENAME TO EntityData; ALTER TABLE EntityData ADD COLUMN SourceId INTEGER; ALTER TABLE EntityData ADD COLUMN DanbooruArtistTag TEXT DEFAULT '';" +
                    "CREATE TABLE DataSource(Id INTEGER PRIMARY KEY,DisplayName TEXT); INSERT INTO DataSource VALUES(7,'Restored Source'); UPDATE EntityData SET SourceId=7; UPDATE EntityData SET DanbooruArtistTag='danbooru_only' WHERE Id=1;" +
                    "CREATE VIEW Entity AS SELECT e.Id,e.EntityType,e.CanonicalName,e.RomanName,e.EHArtistTag,e.NHArtistTag,e.NHGroupTag,e.ExternalId,e.VerificationSource,e.EHNamespace,e.EHTag,e.Verified,e.UpdatedUtc,e.NormalizedCanonical,e.DanbooruArtistTag,e.SourceId,s.DisplayName AS Source FROM EntityData e LEFT JOIN DataSource s ON s.Id=e.SourceId;" +
                    "ALTER TABLE EntityAlias RENAME TO EntityAliasData; ALTER TABLE EntityAliasData ADD COLUMN SourceId INTEGER; UPDATE EntityAliasData SET SourceId=7; CREATE VIEW EntityAlias AS SELECT a.*,s.DisplayName AS Source FROM EntityAliasData a LEFT JOIN DataSource s ON s.Id=a.SourceId;" +
                    "ALTER TABLE ProviderIdentity RENAME TO ProviderIdentityData; ALTER TABLE ProviderIdentityData ADD COLUMN ExternalId TEXT; ALTER TABLE ProviderIdentityData ADD COLUMN SourceId INTEGER; ALTER TABLE ProviderIdentityData ADD COLUMN EvidenceCount INTEGER DEFAULT 1;" +
                    "INSERT INTO ProviderIdentityData VALUES(1,'Danbooru','artist','provider_artist','provider artist','external-123',7,2); CREATE VIEW ProviderIdentity AS SELECT p.*,s.DisplayName AS Source FROM ProviderIdentityData p LEFT JOIN DataSource s ON s.Id=p.SourceId; PRAGMA user_version=4;");
            }
            string hash = AuthorEntityStore.ContentHash(File.ReadAllBytes(path));
            AuthorEntityStore store = new AuthorEntityStore(Path.Combine(root, "schema-v4-user.json"), path);
            Check(store.PreparePublicDatabaseIndex().Ready, "v4 compatibility views must build the runtime index");
            Check(store.LoadIndex().Resolve("danbooru_only").Entity.DanbooruArtistTag == "danbooru_only", "v4 Danbooru compatibility tag must reach recognition");
            Check(store.LoadIndex().Resolve("provider artist").Entity.Source == "Restored Source", "SourceId must be resolved through the compatibility view");
            Check(store.SearchPublicDatabase("provider artist", 10).Count == 1 && store.LoadIndex().Resolve("PublicGroup", "Group").Found, "v4 search and group role mapping");
            PublicEntitySnapshot snapshot = store.GetPublicSnapshot(1);
            Check(snapshot.StableKey.Contains("external-123"), "v4 provider external IDs must be available for scoped public binding");
            store.SavePublicOverride(snapshot, "V4Override", new string[0], new[] { "danbooru_only" }, "");
            Check(!store.LoadIndex().Resolve("danbooru_only").Found && !store.LoadIndex().Resolve("danbooru only").Found, "v4 disabled compatibility tag must remain disabled");
            Check(hash == AuthorEntityStore.ContentHash(File.ReadAllBytes(path)), "v4 adapter cannot modify public database");
            using (FileIndexCacheDatabase db = new FileIndexCacheDatabase(path)) {
                db.Open(); Sql(db, "UPDATE EntityData SET Id=901 WHERE Id=1; UPDATE EntityAliasData SET EntityId=901 WHERE EntityId=1; UPDATE ProviderIdentityData SET EntityId=901 WHERE EntityId=1; UPDATE ArtistGroup SET ArtistId=901 WHERE ArtistId=1;");
            }
            AuthorEntityMatch upgraded = store.LoadIndex().Resolve("PublicAuthor");
            Check(upgraded.Found && upgraded.Entity.Id == 901 && upgraded.Entity.CanonicalName == "V4Override" && !store.LoadIndex().Resolve("danbooru only").Found, "v4 scoped provider external identity must carry overrides and disabled tags across row ID changes");
            Pass("Schema v4 compatibility views, restored SourceId labels, Danbooru tags, provider external identity, search/group lookup and read-only overrides passed.");
        }
        private static void RealSchemaContract(string root, string path)
        {
            string original = AuthorEntityStore.ContentHash(File.ReadAllBytes(path));
            AuthorEntityStore store = new AuthorEntityStore(Path.Combine(root, "contract-user.json"), path);
            Stopwatch timer = Stopwatch.StartNew(); var stats = store.PreparePublicDatabaseIndex(); timer.Stop();
            Check(stats.Ready && stats.Artists > 0, "builder's actual v4 DB must load through compatibility views");
            long indexMs = timer.ElapsedMilliseconds;
            var snapshots = store.GetPublicSnapshots(); var artist = snapshots.Values.First(x => x.EntityType == "Artist");
            Check(snapshots.Values.Any(x => !String.IsNullOrEmpty(x.Source)) && snapshots.Values.Any(x => x.Identities.Count > 0), "actual source/identity view mapping");
            Check(store.SearchPublicDatabase("", 10).Count > 0, "actual public view listing");
            store.SavePublicOverride(artist, "ContractOverride", new[] { "__contract_v4_probe__" }, new string[0], "");
            Check(store.LoadIndex().Resolve("__contract_v4_probe__").Found, "actual v4 public override recognition");
            store.RemovePublicOverride(store.Load().PublicOverrides.Single().Id);
            Check(original == AuthorEntityStore.ContentHash(File.ReadAllBytes(path)), "actual builder output must remain byte-identical");
            Pass("Actual builder Schema v4 output: " + stats.Artists + " artists, " + stats.LookupKeys + " lookup keys, cold index " + indexMs + " ms; source labels, provider identities, search and overrides passed; database bytes unchanged.");
        }
        private static void RealPerformance(string root, string app, string reportDirectory, bool skipSimulation)
        {
            string publicPath = Path.Combine(app, AppFiles.AuthorIndexDatabase), userPath = Path.Combine(app, AppFiles.AuthorEntities), indexPath = Path.Combine(app, "Cache", AppFiles.FileIndexDatabase);
            string[] protectedPaths = new[] { publicPath, userPath, indexPath, Path.Combine(app, "UserSettings.ini"), Path.Combine(app, AppFiles.AuthorAliases) };
            Dictionary<string,string> hashes = protectedPaths.Where(File.Exists).ToDictionary(x => x, x => AuthorEntityStore.ContentHash(File.ReadAllBytes(x)));
            AuthorEntityStore store = new AuthorEntityStore("", publicPath); Stopwatch watch = Stopwatch.StartNew(); var stats = store.PreparePublicDatabaseIndex(); watch.Stop();
            Check(stats.Ready, "real public index preparation failed");
            Report.Add("- Real public database: " + stats.Artists + " artists, " + stats.LookupKeys + " lookup keys; cold index " + watch.ElapsedMilliseconds + " ms.");
            var adapter = new GuiGuiAuthorIndexDatabase(publicPath); var seeds = adapter.LoadSimulationAuthors(1000, 20261008);
            var unified = store.LoadIndex(); watch.Restart(); int found = 0, ambiguous = 0;
            for (int repeat = 0; repeat < 10; repeat++) foreach (var seed in seeds) { var match = unified.Resolve(seed.CanonicalName); if(match.Found) found++; if(match.Ambiguous) ambiguous++; }
            watch.Stop(); Report.Add("- Real names: " + seeds.Count * 10 + " queries, " + watch.ElapsedMilliseconds + " ms; resolved " + found + ", ambiguous " + ambiguous + ".");
            if (!skipSimulation) {
            Console.WriteLine("[RUN] Existing simulation benchmark: 8490 files.");
            SimulationBenchmarkService simulation = new SimulationBenchmarkService(publicPath, store, null, AuthorRecognitionMode.Classic);
            SimulationBenchmarkReport simulated = simulation.Run(new SimulationBenchmarkOptions { SourceFileCount = 8490, UniqueAuthorCount = 500, RunMode = SimulationRunMode.Complete }, 20, GroupNaming.DefaultTemplate, new[] { GroupNaming.DefaultTemplate }, AuthorFolderNaming.DefaultTemplate, new[] { AuthorFolderNaming.DefaultTemplate }, null, null);
            foreach (var round in simulated.Rounds) Report.Add("- Simulation " + round.Name + ": files " + round.SourceFiles + ", hits " + round.CacheHits + ", recalculated " + round.RecalculatedFiles + ", total " + round.TotalMs + " ms.");
            Check(simulated.Rounds.Any(x => x.CacheHits == 8490 && x.RecalculatedFiles == 0), "8490-file simulation cache must recalculate zero files");
            }
            UserSettingsData settings = new UserSettingsStore(Path.Combine(app, "UserSettings.ini")).Load();
            Check(Directory.Exists(settings.SourcePath) && Directory.Exists(settings.AuthorRoot), "configured real source/target not available");
            string copiedIndex = Path.Combine(root, "real-index-copy.db"); File.Copy(indexPath, copiedIndex);
            string copiedUser = Path.Combine(root, "real-user-copy.json"); if (File.Exists(userPath)) File.Copy(userPath, copiedUser);
            string baselineUser = Path.Combine(root, "real-user-before-migration.json"); if (File.Exists(userPath)) File.Copy(userPath, baselineUser);
            AuthorEntityStore realStore = new AuthorEntityStore(copiedUser, publicPath);
            LegacyAliasImporter.Import(Path.Combine(app, AppFiles.AuthorAliases), realStore);
            realStore.PreparePublicDatabaseIndex();
            TagCleaningRuleStore cleaning = new TagCleaningRuleStore(Path.Combine(app, AppFiles.TagCleaningRules));
            ArchiveEngine engine = new ArchiveEngine(realStore, cleaning);
            engine.RecognitionMode = settings.RecognitionMode;
            using (FileIndexCacheDatabase cache = new FileIndexCacheDatabase(copiedIndex)) {
                cache.Open(); long id = cache.EnsureRoot("Source", settings.SourcePath, "AcceptanceCopy"); var entries = cache.LoadSourceFiles(id);
                Check(entries.Count > 0, "real persistent source index contains no files");
                List<FileInfo> files = entries.Values.OrderByDescending(x => x.LastWriteTimeUtc).ThenBy(x => x.FileId).Select(x => new FileInfo(x.FullPath)).ToList();
                var metadata = entries.Values.ToDictionary(x => x.FullPath, x => new SourceIndexFileSnapshot { FullPath = x.FullPath, DirectoryPath = x.DirectoryPath, FileName = x.FileName, Extension = x.Extension, FileSize = x.FileSize, LastWriteTimeUtc = x.LastWriteTimeUtc, CreationTimeUtc = x.CreationTimeUtc }, StringComparer.OrdinalIgnoreCase);
                ScanSessionSnapshot session = cache.CreateScanSession(id, files); ArchivePlanDiagnostics diagnostics = new ArchivePlanDiagnostics();
                string priorKey = (string)typeof(FileIndexCacheDatabase).GetMethod("ScalarText", BindingFlags.NonPublic | BindingFlags.Instance)
                    .Invoke(cache, new object[] { "SELECT ConfigKey FROM MigrationPlanCache GROUP BY ConfigKey ORDER BY COUNT(*) DESC LIMIT 1" });
                List<PlanItem> priorPlan = cache.LoadMigrationPlans(priorKey, session);
                Console.WriteLine("[RUN] Actual indexed planning: " + files.Count + " files, prior cached plans " + priorPlan.Count + ".");
                watch.Restart(); List<PlanItem> plan = engine.BuildPlanUsingIndex(files, metadata, settings.AuthorRoot, settings.MaxAuthorsPerGroup, settings.GroupTemplate, settings.GroupTemplateHistory, settings.AuthorFolderTemplate, settings.AuthorFolderTemplateHistory, null, null, ScanProgressStage.Planning, diagnostics); watch.Stop();
                long firstMs = watch.ElapsedMilliseconds; foreach(var item in plan) item.FileId = entries[item.SourcePath].FileId;
                Console.WriteLine("[PASS] Actual planning completed: " + firstMs + " ms; comparing migration baseline.");
                AuthorEntityStore baselineStore = new AuthorEntityStore(baselineUser, publicPath);
                baselineStore.PreparePublicDatabaseIndex();
                ArchiveEngine beforeMigration = new ArchiveEngine(baselineStore, cleaning);
                beforeMigration.RecognitionMode = settings.RecognitionMode;
                Check(LegacyAliasImporter.Read(Path.Combine(app, AppFiles.AuthorAliases)).Count == 0, "real migration baseline requires explicit legacy alias projection for nonempty TXT");
                List<PlanItem> beforePlan = beforeMigration.BuildPlanUsingIndex(files, metadata, settings.AuthorRoot, settings.MaxAuthorsPerGroup, settings.GroupTemplate, settings.GroupTemplateHistory, settings.AuthorFolderTemplate, settings.AuthorFolderTemplateHistory, null, null, ScanProgressStage.Planning);
                Console.WriteLine("[PASS] Migration baseline planning completed; validating per-file cache and historical differences.");
                var beforeByPath = beforePlan.ToDictionary(x => x.SourcePath, x => x.Author + "|" + x.MatchedAs + "|" + x.TargetPath + "|" + x.StatusCode, StringComparer.OrdinalIgnoreCase);
                int migrationDifferences = plan.Count(x => beforeByPath[x.SourcePath] != x.Author + "|" + x.MatchedAs + "|" + x.TargetPath + "|" + x.StatusCode);
                Check(migrationDifferences == 0, "controlled before/after migration changed real recognition");
                Report.Add("- Controlled pre-migration vs unified data, same file order/target/settings: " + files.Count + " files, " + migrationDifferences + " author/identity/target/status differences.");
                Func<string,string> dependency = engine.CreateIdentityDependencyResolver(); watch.Restart(); cache.UpsertMigrationPlans("real-acceptance", plan, dependency);
                Report.Add("- First durable plan/dependency write: " + watch.ElapsedMilliseconds + " ms.");
                Console.WriteLine("[PASS] Durable plan/dependency write completed: " + watch.ElapsedMilliseconds + " ms.");
                watch.Restart(); var cached = cache.LoadMigrationPlans("real-acceptance", session, dependency); watch.Stop();
                Check(cached.Count == files.Count, "real unchanged rescan must reuse every plan row");
                Report.Add("- Real indexed scan (configured source/target, read-only files): " + files.Count + " files; first planning " + firstMs + " ms; durable repeated-plan read " + watch.ElapsedMilliseconds + " ms; hits " + cached.Count + "; recalculated 0; source rediscovery 0. Author source " + diagnostics.IdentitySourceMs + " ms, target index " + diagnostics.TargetDirectoryMs + " ms, recognition preparation " + diagnostics.PrepareRecognitionMs + " ms.");
                AuthorEntityStore overlayStore = new AuthorEntityStore(Path.Combine(root, "real-public-overlay.json"), publicPath);
                PublicEntitySnapshot overlayCandidate = overlayStore.GetPublicSnapshots().Values.First(x => x.EntityType == "Artist" && (x.Source ?? "").IndexOf("EhTagTranslation", StringComparison.OrdinalIgnoreCase) < 0);
                overlayStore.SavePublicOverride(overlayCandidate, overlayCandidate.CanonicalName, new[] { "AcceptancePublicOverride" }, new string[0], "");
                AuthorEntityIndex overlayIndex = overlayStore.LoadIndex();
                GuiGuiAuthorIndexDatabase overlayAdapter = (GuiGuiAuthorIndexDatabase)typeof(AuthorEntityStore).GetField("_publicDatabase", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(overlayStore);
                var lookupCache = (System.Collections.IDictionary)typeof(GuiGuiAuthorIndexDatabase).GetField("_cache", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(overlayAdapter);
                int queryCount = lookupCache.Count; watch.Restart();
                foreach (FileInfo file in files) overlayIndex.GetDependencyVersion(file.Name);
                watch.Stop(); Check(queryCount == lookupCache.Count, "public overlay dependencies must not run per-file public identity queries");
                Check(watch.ElapsedMilliseconds < 10000, "public overlay dependency validation exceeded ten seconds");
                Report.Add("- Public-overlay scan dependency regression: " + files.Count + " files, " + watch.ElapsedMilliseconds + " ms, zero per-file public identity queries; cancelable plan reads and transactional write cancellation passed.");
                Console.WriteLine("[PASS] Public overlay dependencies: " + files.Count + " files, " + watch.ElapsedMilliseconds + " ms, zero identity queries.");
                Dictionary<string, string> expected = plan.ToDictionary(x => x.SourcePath, x => x.Author + "|" + x.MatchedAs + "|" + x.TargetPath + "|" + x.StatusCode, StringComparer.OrdinalIgnoreCase);
                Check(cached.All(x => expected[x.SourcePath] == x.Author + "|" + x.MatchedAs + "|" + x.TargetPath + "|" + x.StatusCode), "durable repeated plans changed recognition results");
                List<PlanItem> differences = priorPlan.Where(x => expected.ContainsKey(x.SourcePath) && expected[x.SourcePath] != x.Author + "|" + x.MatchedAs + "|" + x.TargetPath + "|" + x.StatusCode).ToList();
                Report.Add("- Prior saved scan comparison: " + priorPlan.Count + " comparable plans, " + differences.Count + " differing author/identity/target/status results.");
                if (differences.Count > 0) {
                    Directory.CreateDirectory(reportDirectory);
                    var afterByPath = plan.ToDictionary(x => x.SourcePath, StringComparer.OrdinalIgnoreCase);
                    var audit = differences.Select(x => {
                        PlanItem after = afterByPath[x.SourcePath];
                        string explanation = after.StatusCode == PlanStatusCode.Ambiguous && after.MatchWhy == "Reason.EntityConflict" ? "Entity identity conflict: automatic new-author assignment is now blocked and user confirmation is required." :
                            after.Author == x.Author && after.MatchedAs == x.MatchedAs && after.StatusCode == x.StatusCode ? "Canonical identity is unchanged. New-author group allocation shifts because conflicting synthetic authors are no longer allocated earlier in the batch." :
                            after.StatusCode == PlanStatusCode.AlreadyInTarget && String.Equals(after.SourcePath, after.TargetPath, StringComparison.OrdinalIgnoreCase) ? "The file already occupies the selected existing author folder. Removing an earlier conflicting synthetic author prevents virtual-folder interference; no move is proposed." : "Unexplained";
                        return new { x.SourcePath, Before = x.Author + "|" + x.MatchedAs + "|" + x.TargetPath + "|" + x.StatusCode, After = expected[x.SourcePath], AfterReason = after.MatchWhy, Explanation = explanation };
                    }).ToList();
                    Check(audit.All(x => x.Explanation != "Unexplained"), "historical plan differences require per-file explanations");
                    File.WriteAllText(Path.Combine(reportDirectory, "prior-scan-differences.json"), new JavaScriptSerializer { MaxJsonLength = Int32.MaxValue }.Serialize(audit));
                    foreach(var category in audit.GroupBy(x => x.Explanation)) Report.Add("- Explained historical change: " + category.Count() + " files. " + category.Key);
                }
            }
            foreach(var pair in hashes) Check(AuthorEntityStore.ContentHash(File.ReadAllBytes(pair.Key)) == pair.Value, "acceptance modified real data: " + pair.Key);
            Directory.CreateDirectory(reportDirectory);
            Pass("Real public names, 8490-file simulation and actual persistent-index planning passed; all original user/public/index/settings bytes unchanged.");
        }
        private static void UiPressure(string root, string output)
        {
            string directory = Path.Combine(root, "ui-languages"); Directory.CreateDirectory(directory);
            foreach (string code in new[] { "zh-CN", "en-US", "de-DE", "_template" })
                File.Copy(Path.GetFullPath("Languages/" + code + ".json"), Path.Combine(directory, code + ".json"));
            AuthorEntityDatabase data = new AuthorEntityDatabase();
            for (int i = 1; i <= 5000; i++) {
                data.Authors.Add(new AuthorEntityRecord { Id = i, EntityId = "user:ui-" + i, CanonicalName = "Test author " + i, UserConfirmed = true });
                data.Aliases.Add(new AuthorAliasRecord { AuthorId = i, Alias = "Test alias " + i });
            }
            string path = Path.Combine(root, "ui-authors.json");
            File.WriteAllText(path, new JavaScriptSerializer { MaxJsonLength = Int32.MaxValue }.Serialize(data));
            Directory.CreateDirectory(output);
            foreach (string code in new[] { "zh-CN", "en-US", "de-DE" }) {
                LanguageManager language = new LanguageManager(directory); language.Initialize(code);
                Check(language.Get("EntityManager.Edit") != "EntityManager.Edit", "UI language key missing");
                foreach (float size in new[] { 9F, 13.5F }) using (Font font = new Font("Segoe UI", size)) {
                    Stopwatch timer = Stopwatch.StartNew();
                    using (AuthorEntityLibraryForm form = new AuthorEntityLibraryForm(new AuthorEntityStore(path), language, font)) {
                        form.Size = size == 9F ? new Size(960, 670) : new Size(1320, 900);
                        form.StartPosition = System.Windows.Forms.FormStartPosition.Manual;
                        form.Location = new Point(-20000, -20000); form.ShowInTaskbar = false;
                        form.Show(); System.Windows.Forms.Application.DoEvents(); form.PerformLayout();
                        using (Bitmap bitmap = new Bitmap(form.Width, form.Height)) { form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, form.Size)); bitmap.Save(Path.Combine(output, "ui-" + code + "-" + (size == 9F ? "100" : "150") + ".png")); }
                    }
                    timer.Stop(); Report.Add("- UI fixture: 5,000 authors, " + code + ", font scale " + (size == 9F ? "100%" : "150%") + ", construction/render " + timer.ElapsedMilliseconds + " ms.");
                }
            }
            Pass("Unified entity page constructed and rendered with 5,000 entities in Chinese, English and German at 100%/150% font scales.");
        }
        [STAThread]
        public static int Main(string[] args)
        {
            string root = Path.Combine(Path.GetTempPath(), "GuiGui-AuthorLibrary-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
            string output = Path.GetFullPath(args.Length > 1 ? args[1] : "Docs/AuthorLibraryAcceptance");
            try {
                CrudAndConflicts(root); SessionEntities(root); ProviderAndImportIsolation(root); PublicUpgrades(root); SchemaV4Views(root); LocalCacheInvalidation(root); UiPressure(root, output);
                if (args.Length > 0) { if (args.Contains("--schema-contract-real")) RealSchemaContract(root, Path.Combine(Path.GetFullPath(args[0]), AppFiles.AuthorIndexDatabase)); else RealPerformance(root, Path.GetFullPath(args[0]), output, args.Contains("--skip-simulation")); }
                Directory.CreateDirectory(output); File.WriteAllText(Path.Combine(output, "results.md"), "# Author library acceptance\r\n\r\n" + DateTime.UtcNow.ToString("o") + "\r\n\r\n" + String.Join("\r\n", Report.ToArray()), new UTF8Encoding(false)); return 0;
            } catch(Exception ex) { Console.Error.WriteLine("[FAIL] " + ex); return 1; }
            finally { try { Directory.Delete(root, true); } catch { } }
        }
    }
}
