using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace MangaAuthorSorter
{
    internal sealed class AuthorProviderCandidate
    {
        public string Provider = "";
        public string ExternalId = "";
        public string TagName = "";
        public string GroupName = "";
        public List<string> OtherNames = new List<string>();
    }

    internal sealed class AuthorProviderLookupResult
    {
        public string Provider = "";
        public string Query = "";
        public List<AuthorProviderCandidate> Candidates = new List<AuthorProviderCandidate>();
        public string Error = "";
    }

    internal interface IAuthorProvider
    {
        string Id { get; }
        string DisplayName { get; }
        Task<AuthorProviderLookupResult> SearchAuthorAsync(string query, CancellationToken cancellationToken);
    }

    internal sealed class DanbooruAuthorProvider : IAuthorProvider
    {
        private sealed class DanbooruArtistDto
        {
            public int id { get; set; }
            public string name { get; set; }
            public string group_name { get; set; }
            public string[] other_names { get; set; }
            public bool is_deleted { get; set; }
            public bool is_banned { get; set; }
        }

        public string Id { get { return "Danbooru"; } }
        public string DisplayName { get { return "Danbooru"; } }

        public async Task<AuthorProviderLookupResult> SearchAuthorAsync(string query, CancellationToken cancellationToken)
        {
            AuthorProviderLookupResult result = new AuthorProviderLookupResult();
            result.Provider = Id;
            result.Query = query ?? "";

            if (String.IsNullOrWhiteSpace(query)) return result;

            string url =
                "https://danbooru.donmai.us/artists.json?search%5Bany_name_matches%5D=" +
                Uri.EscapeDataString(query.Trim()) +
                "&limit=10";

            HttpWebRequest request = (HttpWebRequest)WebRequest.Create(url);
            request.Method = "GET";
            request.Accept = "application/json";
            request.UserAgent = "MangaAuthorSorter/1.11.23 (Windows .NET Framework; author identity lookup)";
            request.Timeout = 10000;
            request.ReadWriteTimeout = 10000;
            request.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;

            try
            {
                ServicePointManager.SecurityProtocol = ServicePointManager.SecurityProtocol | SecurityProtocolType.Tls12;
            }
            catch { }

            try
            {
                using (CancellationTokenSource timeout =
                    CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
                {
                    timeout.CancelAfter(10000);
                    using (timeout.Token.Register(delegate { try { request.Abort(); } catch { } }))
                    {
                        using (WebResponse response = await request.GetResponseAsync())
                        using (Stream stream = response.GetResponseStream())
                        using (StreamReader reader = new StreamReader(stream, Encoding.UTF8))
                        {
                            cancellationToken.ThrowIfCancellationRequested();
                            string json = await reader.ReadToEndAsync();
                            cancellationToken.ThrowIfCancellationRequested();
                            JavaScriptSerializer serializer = new JavaScriptSerializer();
                            serializer.MaxJsonLength = Int32.MaxValue;
                            List<DanbooruArtistDto> data = serializer.Deserialize<List<DanbooruArtistDto>>(json) ?? new List<DanbooruArtistDto>();

                            foreach (DanbooruArtistDto item in data)
                            {
                                if (item == null || item.is_deleted || String.IsNullOrWhiteSpace(item.name)) continue;
                                AuthorProviderCandidate candidate = new AuthorProviderCandidate();
                                candidate.Provider = Id;
                                candidate.ExternalId = item.id.ToString();
                                candidate.TagName = item.name ?? "";
                                candidate.GroupName = item.group_name ?? "";
                                if (item.other_names != null)
                                    candidate.OtherNames.AddRange(item.other_names.Where(delegate(string x) { return !String.IsNullOrWhiteSpace(x); }));
                                result.Candidates.Add(candidate);
                            }
                        }
                    }
                }
            }
            catch (WebException ex)
            {
                if (cancellationToken.IsCancellationRequested)
                    throw new OperationCanceledException();
                result.Error = ex.Message;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                result.Error = ex.Message;
            }

            return result;
        }
    }

    internal sealed class OnlineAuthorResolutionStats
    {
        public int LocalRecognized;
        public int PendingOnline;
        public int OnlineQueried;
        public int OnlineResolved;
        public int OnlineAmbiguous;
        public int OnlineNotFound;
        public int CachedSkipped;
        public int StillUnresolved;
        public int NewEntities;
    }

    internal sealed class OnlineAuthorResolver
    {
        private readonly AuthorEntityStore _store;
        private readonly IAuthorProvider _provider;
        private readonly TagCleaningRuleStore _tagCleaningStore;

        public OnlineAuthorResolver(AuthorEntityStore store, IAuthorProvider provider)
            : this(store, provider, null)
        {
        }

        public OnlineAuthorResolver(
            AuthorEntityStore store,
            IAuthorProvider provider,
            TagCleaningRuleStore tagCleaningStore)
        {
            _store = store;
            _provider = provider;
            _tagCleaningStore = tagCleaningStore;
        }

        public OnlineAuthorResolutionStats Resolve(
            List<PlanItem> plan,
            int maxLookups,
            bool saveCache,
            Action<ScanProgressInfo> progress,
            Func<bool> cancelRequested)
        {
            OnlineAuthorResolutionStats stats = new OnlineAuthorResolutionStats();
            if (plan == null || _store == null || _provider == null) return stats;

            List<string> queries = CollectQueries(plan, _tagCleaningStore);
            stats.PendingOnline = queries.Count;
            stats.LocalRecognized = Math.Max(0, plan.Count - CountUnresolved(plan));

            if (maxLookups <= 0) maxLookups = 1;
            if (queries.Count > maxLookups)
                queries = queries.Take(maxLookups).ToList();

            int total = queries.Count;
            DateTime lastNetwork = DateTime.MinValue;

            for (int i = 0; i < queries.Count; i++)
            {
                if (cancelRequested != null && cancelRequested())
                    throw new OperationCanceledException();

                string query = queries[i];
                if (progress != null)
                {
                    ScanProgressInfo info = new ScanProgressInfo();
                    info.Stage = ScanProgressStage.OnlineResolving;
                    info.Current = i + 1;
                    info.Total = total;
                    info.CurrentPath = query;
                    info.Indeterminate = false;
                    progress(info);
                }

                AuthorLookupCacheRecord cached = _store.GetFreshLookup(_provider.Id, query, 14);
                if (cached != null)
                {
                    if (String.Equals(cached.Status, "not_found", StringComparison.OrdinalIgnoreCase) ||
                        String.Equals(cached.Status, "ambiguous", StringComparison.OrdinalIgnoreCase) ||
                        String.Equals(cached.Status, "resolved", StringComparison.OrdinalIgnoreCase))
                    {
                        stats.CachedSkipped++;
                        continue;
                    }
                }

                TimeSpan sinceLast = DateTime.UtcNow - lastNetwork;
                if (lastNetwork != DateTime.MinValue && sinceLast.TotalMilliseconds < 1100)
                {
                    int wait = (int)Math.Ceiling(1100 - sinceLast.TotalMilliseconds);
                    while (wait > 0)
                    {
                        if (cancelRequested != null && cancelRequested())
                            throw new OperationCanceledException();
                        int slice = Math.Min(wait, 100);
                        Thread.Sleep(slice);
                        wait -= slice;
                    }
                }

                AuthorProviderLookupResult lookup = null;
                using (CancellationTokenSource cts = new CancellationTokenSource())
                {
                    Task<AuthorProviderLookupResult> task = _provider.SearchAuthorAsync(query, cts.Token);
                    while (!task.IsCompleted)
                    {
                        if (cancelRequested != null && cancelRequested())
                        {
                            cts.Cancel();
                            throw new OperationCanceledException();
                        }
                        Thread.Sleep(100);
                    }

                    if (task.IsCanceled)
                        throw new OperationCanceledException();

                    if (task.IsFaulted)
                    {
                        string detail = task.Exception != null
                            ? task.Exception.GetBaseException().Message
                            : "provider task failed";
                        _store.SaveLookupStatus(_provider.Id, query, "error", detail, saveCache);
                        lastNetwork = DateTime.UtcNow;
                        stats.OnlineQueried++;
                        continue;
                    }

                    lookup = task.Result;
                }

                lastNetwork = DateTime.UtcNow;
                stats.OnlineQueried++;
                if (lookup == null || !String.IsNullOrWhiteSpace(lookup.Error))
                {
                    _store.SaveLookupStatus(_provider.Id, query, "error", lookup != null ? lookup.Error : "empty response", saveCache);
                    continue;
                }

                List<AuthorProviderCandidate> strong = GetStrongArtistMatches(query, lookup.Candidates);
                if (strong.Count == 1)
                {
                    AuthorEntityRecord entity = _store.MergeResolvedProviderResult(_provider.Id, query, strong[0], saveCache);
                    if (entity != null)
                    {
                        stats.OnlineResolved++;
                        stats.NewEntities++;
                    }
                }
                else if (strong.Count > 1)
                {
                    stats.OnlineAmbiguous++;
                    _store.SaveLookupStatus(_provider.Id, query, "ambiguous", strong.Count + " exact artist identities", saveCache);
                }
                else
                {
                    stats.OnlineNotFound++;
                    _store.SaveLookupStatus(_provider.Id, query, "not_found", "no exact artist name/other-name match", saveCache);
                }
            }

            stats.StillUnresolved = Math.Max(0, stats.PendingOnline - stats.OnlineResolved);
            return stats;
        }

        private static List<string> CollectQueries(
            List<PlanItem> plan,
            TagCleaningRuleStore tagCleaningStore)
        {
            List<string> result = new List<string>();
            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (PlanItem item in plan)
            {
                if (item == null || String.IsNullOrWhiteSpace(item.Author)) continue;
                RecognitionVisual visual = RecognitionVisualResolver.Resolve(item);
                string code = visual != null ? (visual.Code ?? "") : "";
                if (!String.Equals(code, "new", StringComparison.OrdinalIgnoreCase) &&
                    !String.Equals(code, "new-reuse", StringComparison.OrdinalIgnoreCase) &&
                    !String.Equals(code, "unrecognized", StringComparison.OrdinalIgnoreCase))
                    continue;

                string query = item.Author.Trim();
                if (query.Length < 2 || query.Length > 120 || AuthorRules.IsMetadataTag(query, tagCleaningStore)) continue;
                if (AuthorRules.GetStructuredAuthorParts(query) != null) continue; // MVP: only plain identities are queried.

                string key = AuthorRules.NormalizeText(query);
                if (key.Length > 0 && seen.Add(key)) result.Add(query);
            }

            return result;
        }

        private static int CountUnresolved(List<PlanItem> plan)
        {
            int count = 0;
            foreach (PlanItem item in plan)
            {
                RecognitionVisual visual = RecognitionVisualResolver.Resolve(item);
                string code = visual != null ? (visual.Code ?? "") : "";
                if (code == "new" || code == "new-reuse" || code == "unrecognized") count++;
            }
            return count;
        }

        private static List<AuthorProviderCandidate> GetStrongArtistMatches(string query, List<AuthorProviderCandidate> candidates)
        {
            string queryNorm = NormalizeProviderName(query);
            List<AuthorProviderCandidate> strong = new List<AuthorProviderCandidate>();

            foreach (AuthorProviderCandidate candidate in candidates ?? new List<AuthorProviderCandidate>())
            {
                if (candidate == null) continue;
                bool match = String.Equals(NormalizeProviderName(candidate.TagName), queryNorm, StringComparison.OrdinalIgnoreCase);
                if (!match)
                {
                    foreach (string other in candidate.OtherNames ?? new List<string>())
                    {
                        if (String.Equals(NormalizeProviderName(other), queryNorm, StringComparison.OrdinalIgnoreCase))
                        {
                            match = true;
                            break;
                        }
                    }
                }

                // group_name-only results are intentionally not treated as an
                // author identity in the MVP because one circle may have many members.
                // Also reject an exact-name record when other returned artists explicitly
                // declare that name as their group; this is a conservative signal that
                // the exact record represents a circle rather than an individual author.
                if (match && !IsLikelyGroupRecord(candidate, candidates))
                    strong.Add(candidate);
            }

            return strong;
        }

        private static bool IsLikelyGroupRecord(
            AuthorProviderCandidate candidate,
            List<AuthorProviderCandidate> candidates)
        {
            if (candidate == null || String.IsNullOrWhiteSpace(candidate.TagName))
                return false;

            string candidateName = NormalizeProviderName(candidate.TagName);
            foreach (AuthorProviderCandidate other in candidates ?? new List<AuthorProviderCandidate>())
            {
                if (other == null || Object.ReferenceEquals(other, candidate)) continue;
                if (String.IsNullOrWhiteSpace(other.GroupName)) continue;
                if (String.Equals(
                        NormalizeProviderName(other.GroupName),
                        candidateName,
                        StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        private static string NormalizeProviderName(string value)
        {
            string n = (value ?? "").Trim().Replace(' ', '_');
            return AuthorRules.NormalizeText(n).Replace(' ', '_');
        }
    }
}
