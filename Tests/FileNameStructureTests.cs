using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace MangaAuthorSorter.Tests
{
    internal static class FileNameStructureTests
    {
        private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
        private sealed class Provider : IAuthorProvider
        {
            public int Calls;
            public string Id { get { return "test"; } }
            public string DisplayName { get { return "test"; } }
            public Task<AuthorProviderLookupResult> SearchAuthorAsync(string query, CancellationToken token)
            {
                Calls++;
                return Task.FromResult(new AuthorProviderLookupResult { Candidates = new List<AuthorProviderCandidate> {
                    new AuthorProviderCandidate { TagName = "title artist", EvidenceSources = new List<string> { "E-Hentai/TitleSearch+LiveGData" } } } });
            }
        }
        public static int Main()
        {
            string root = Path.Combine(Path.GetTempPath(), "GuiGui-Structure-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            try
            {
                string[] identities = { "フロム脳患者の会 (ティラヌー)", "ひよりみのソラ (陽寄瑞貴)", "LAMINARIA (しおこんぶ)" };
                foreach (string identity in identities)
                {
                    string file = "(未知展会X) [" + identity + "] 作品 [中国翻译]";
                    Check(AuthorRules.GetAuthorCandidatesFromFileName(file).SequenceEqual(new[] { identity }), "unknown event must yield later composite identity");
                    Check(FileNameStructure.Parse(file).Creators.Count == 1, "artist role");
                }
                string titleFile = "(秋葉原超同人祭) まきばのぼにゅうにっき ~サキュバス編~ [罗洁爱儿个人机翻]";
                Check(AuthorRules.GetAuthorCandidatesFromFileName(titleFile).Count == 0, "title-only file must not supply event or translator as author");
                Check(AuthorRules.GetAuthorCandidatesFromFileName("(作者甲) [作者乙] 作品").Count == 2, "nonstandard adjacent identities must all survive");
                var activities = FileNameStructure.DiscoverActivities(identities.Select(x => "(未知展会X) [" + x + "] 作品"));
                Check(FileNameStructure.Parse("(未知展会X) 作品", activities).SuspectedActivity, "cross-file evidence should protect unstructured sibling");
                Check(FileNameStructure.DiscoverActivities(Enumerable.Repeat("(作者甲) [社团 (作者乙)] 作品", 20)).Count == 0, "frequency alone must not blacklist identity");
                Check(AuthorRules.GetAuthorFromFileName("(作者甲) 作品") == "作者甲", "legacy parentheses author preserved");
                using (var db = new FileIndexCacheDatabase(Path.Combine(root, "cache.db")))
                {
                    db.Open();
                    db.UpsertParsedMetadata(new[] { new ParsedMetadataCacheEntry { FullPath = "fixture", ParserVersion = "2", AuthorCandidate = "秋葉原超同人祭", InputFingerprint = "old" } });
                    var cache = new ParsedMetadataCacheService(db, "rules"); List<string> values;
                    Check(!cache.TryGetAuthorCandidates("fixture", titleFile, out values), "old parser facts invalidated");
                    cache.StoreAuthorCandidates("fixture", titleFile, new string[0]); cache.Flush();
                    Check(cache.TryGetAuthorCandidates("fixture", titleFile, out values) && values.Count == 0, "current parsing facts reused");
                }
                var store = new AuthorEntityStore(Path.Combine(root, "entities.json"), Path.Combine(root, "absent.db"));
                var engine = new ArchiveEngine(store, null);
                string target = Path.Combine(root, "target"); Directory.CreateDirectory(target);
                string group = Path.Combine(target, "_漫画作者1"); Directory.CreateDirectory(group);
                foreach (string identity in identities) Directory.CreateDirectory(Path.Combine(group, identity));
                Directory.CreateDirectory(Path.Combine(group, "秋葉原超同人祭"));
                var source = identities.Select((x, i) => new PlanItem { FileName = "(未知展会X) [" + x + "] 作品.zip", SourcePath = Path.Combine(root, "file" + i + ".zip") }).ToList();
                source.Add(new PlanItem { FileName = titleFile + ".rar", SourcePath = Path.Combine(root, "unknown.rar") });
                source.Add(new PlanItem { FileName = "(未知展会X) 没有作者的作品.zip", SourcePath = Path.Combine(root, "sibling.zip") });
                foreach (PlanItem item in source) File.WriteAllText(item.SourcePath, "fixture");
                foreach (AuthorRecognitionMode mode in new[] { AuthorRecognitionMode.Classic, AuthorRecognitionMode.Scoring })
                {
                    engine.RecognitionMode = mode;
                    var plan = engine.ResolvePlan(source, target, 100);
                    Check(plan.Count == source.Count, "all fixture files planned");
                    Check(plan.Take(3).All(x => x.StatusCode == PlanStatusCode.Matched), "local structure matches in " + mode);
                    Check(plan.Skip(3).All(x => String.IsNullOrWhiteSpace(x.Author)), "activity folders must not rescue unknown title in " + mode);
                    Check(plan.All(x => x.MatchWhy.Contains("[structure]")), "structure diagnostic");
                }
                var provider = new Provider();
                var resolver = new OnlineAuthorResolver(store, provider);
                var unknown = engine.ResolvePlan(source.Skip(3).Take(1), target, 100);
                resolver.Resolve(unknown, 3, true, null, null);
                Check(provider.Calls == 1 && unknown[0].CandidateNames.Contains("title artist"), "online title fallback candidates: calls=" + provider.Calls + ", author=" + unknown[0].Author + ", names=" + String.Join(",", unknown[0].CandidateNames));
                Check(!store.LoadIndex().Resolve(FileNameStructure.Parse(titleFile).WorkTitle).Found, "title must never be learned as an alias");
                resolver.Resolve(unknown, 3, true, null, null);
                Check(provider.Calls == 1, "title cache avoids repeated request");
                Check(engine.ResolvePlan(source.Skip(3).Take(1), target, 100)[0].CandidateNames.Contains("title artist"), "offline local work metadata fallback");
                store.SaveUserEntity(0, "Artist", "秋葉原超同人祭", "", new string[0], new string[0], true);
                Check(engine.ResolvePlan(source.Skip(3).Take(1), target, 100)[0].Author == "秋葉原超同人祭", "user-confirmed identity outranks syntax");
                Console.WriteLine("[PASS] Structure, unknown prefixes, all candidates, batch evidence, both modes, title fallback, cache and user override.");
                return 0;
            }
            catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
            finally { Directory.Delete(root, true); }
        }
    }
}
