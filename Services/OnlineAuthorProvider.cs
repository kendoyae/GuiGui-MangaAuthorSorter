using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Text.RegularExpressions;

namespace MangaAuthorSorter
{
    internal sealed class AuthorProviderCandidate
    {
        public string Provider = "";
        public string ExternalId = "";
        public string TagName = "";
        public string GroupName = "";
        public string TagNamespace = "artist";
        public string EntityType = "Artist";
        public List<string> OtherNames = new List<string>();
        public int IdentityScore;
        public int IndependentWorkCount;
        public bool HardConflict;
        public List<string> EvidenceSources = new List<string>();
    }

    internal sealed class AuthorProviderLookupResult
    {
        public string Provider = "";
        public string Query = "";
        public List<AuthorProviderCandidate> Candidates = new List<AuthorProviderCandidate>();
        public string Error = "";
        public string ErrorStatus = "NetworkError";
    }

    internal interface IAuthorProvider
    {
        string Id { get; }
        string DisplayName { get; }
        Task<AuthorProviderLookupResult> SearchAuthorAsync(string query, CancellationToken cancellationToken);
    }

    /// <summary>
    /// The public entry point is an evidence chain rather than a user-selected site.
    /// E-Hentai supplies primary identity evidence. nHentai can add work evidence,
    /// but is deliberately capped below the automatic confirmation threshold when
    /// it is the only source.
    /// </summary>
    internal sealed class EvidenceChainAuthorProvider : IAuthorProvider
    {
        private static readonly SemaphoreSlim EhSlots = new SemaphoreSlim(1, 1);
        private static readonly SemaphoreSlim NhSlots = new SemaphoreSlim(1, 1);
        private static readonly object RateGate = new object();
        private static DateTime _ehNextRequestUtc = DateTime.MinValue;
        private static DateTime _nhNextRequestUtc = DateTime.MinValue;
        private static int _ehLoad;
        private static int _nhLoad;
        private readonly bool _useEhentai;
        private readonly bool _useNhentai;
        private readonly bool _useLocalReference;
        private readonly string _nhentaiApiKey;

        public EvidenceChainAuthorProvider() : this(true, true, "", true) { }

        public EvidenceChainAuthorProvider(bool useEhentai, bool useNhentai, string nhentaiApiKey, bool useLocalReference = true)
        {
            _useEhentai = useEhentai;
            _useNhentai = useNhentai;
            _nhentaiApiKey = (nhentaiApiKey ?? "").Trim();
            _useLocalReference = useLocalReference;
        }
        private static readonly object HealthGate = new object();
        private static DateTime _ehRetryAfterUtc = DateTime.MinValue;
        private static DateTime _nhRetryAfterUtc = DateTime.MinValue;

        public string Id { get { return "EvidenceChain"; } }
        public string DisplayName { get { return "E-Hentai + nHentai"; } }

        public async Task<AuthorProviderLookupResult> SearchAuthorAsync(string query, CancellationToken cancellationToken)
        {
            AuthorProviderLookupResult combined = new AuthorProviderLookupResult();
            combined.Provider = Id;
            combined.Query = query ?? "";
            if (String.IsNullOrWhiteSpace(query)) return combined;

            AuthorProviderLookupResult eh = null;
            AuthorProviderLookupResult nh = null;
            bool ehAttempted = _useEhentai && CanUseProvider("E-Hentai");
            bool nhAttempted = _useNhentai && CanUseProvider("nHentai");
            bool nhFirst = ehAttempted && nhAttempted &&
                Volatile.Read(ref _nhLoad) < Volatile.Read(ref _ehLoad);

            if (nhFirst)
            {
                nh = await RunProviderAsync(new NhentaiAuthorProvider(_nhentaiApiKey), query, NhSlots, true, cancellationToken);
                UpdateProviderHealth("nHentai", nh);
                Merge(combined, nh);
            }

            if (HasStrongCandidate(combined)) return combined;

            if (ehAttempted)
            {
                eh = await RunProviderAsync(
                    new EhentaiAuthorProvider(_useLocalReference), query, EhSlots, false, cancellationToken);
                UpdateProviderHealth("E-Hentai", eh);
                Merge(combined, eh);
            }

            // EH already supplied enough independent works to confirm the identity.
            // The next network request would not change the current decision.
            if (HasStrongCandidate(combined)) return combined;

            if (nhAttempted && !nhFirst)
            {
                nh = await RunProviderAsync(
                    new NhentaiAuthorProvider(_nhentaiApiKey), query, NhSlots, true, cancellationToken);
                UpdateProviderHealth("nHentai", nh);
                Merge(combined, nh);
            }

            if (combined.Candidates.Count == 0)
            {
                List<string> errors = new List<string>();
                if (eh != null && !String.IsNullOrWhiteSpace(eh.Error)) errors.Add("E-Hentai: " + eh.Error);
                if (nh != null && !String.IsNullOrWhiteSpace(nh.Error)) errors.Add("nHentai: " + nh.Error);
                if (errors.Count > 0)
                {
                    combined.Error = String.Join(" | ", errors.ToArray());
                    if ((eh != null && eh.ErrorStatus == "RateLimited") || (nh != null && nh.ErrorStatus == "RateLimited"))
                        combined.ErrorStatus = "RateLimited";
                    else if ((eh != null && eh.ErrorStatus == "AccessDenied") || (nh != null && nh.ErrorStatus == "AccessDenied"))
                        combined.ErrorStatus = "AccessDenied";
                    else if (nh != null && nh.ErrorStatus == "InvalidApiKey")
                        combined.ErrorStatus = "InvalidApiKey";
                    else if ((eh != null && eh.ErrorStatus == "ProtocolChanged") || (nh != null && nh.ErrorStatus == "ProtocolChanged"))
                        combined.ErrorStatus = "ProtocolChanged";
                    else
                        combined.ErrorStatus = "ProviderUnavailable";
                }
                else if ((_useEhentai || _useNhentai) && !ehAttempted && !nhAttempted)
                {
                    combined.Error = "所有在线证据源均处于暂时停用期；稍后自动重试。";
                    combined.ErrorStatus = "ProviderUnavailable";
                }
            }
            return combined;
        }

        private static bool HasStrongCandidate(AuthorProviderLookupResult result)
        {
            return result != null && result.Candidates.Any(delegate(AuthorProviderCandidate x)
            { return x != null && x.IdentityScore >= 90 && !x.HardConflict; });
        }

        private static async Task<AuthorProviderLookupResult> RunProviderAsync(
            IAuthorProvider provider,
            string query,
            SemaphoreSlim slots,
            bool nhentai,
            CancellationToken cancellationToken)
        {
            if (nhentai) Interlocked.Increment(ref _nhLoad); else Interlocked.Increment(ref _ehLoad);
            bool acquired = false;
            try
            {
                await slots.WaitAsync(cancellationToken);
                acquired = true;
                int attempt = 0;
                while (true)
                {
                    await WaitForRateWindowAsync(nhentai, cancellationToken);
                    AuthorProviderLookupResult result = await provider.SearchAuthorAsync(query, cancellationToken);
                    if (result == null || String.IsNullOrWhiteSpace(result.Error) ||
                        (!String.Equals(result.ErrorStatus, "ProviderUnavailable", StringComparison.OrdinalIgnoreCase) &&
                         !String.Equals(result.ErrorStatus, "RateLimited", StringComparison.OrdinalIgnoreCase)))
                        return result;
                    if (attempt >= 2) return result;
                    int delay = attempt == 0 ? 1000 : 2000;
                    attempt++;
                    await Task.Delay(delay, cancellationToken);
                }
            }
            finally
            {
                if (acquired) slots.Release();
                if (nhentai) Interlocked.Decrement(ref _nhLoad); else Interlocked.Decrement(ref _ehLoad);
            }
        }

        private static async Task WaitForRateWindowAsync(bool nhentai, CancellationToken cancellationToken)
        {
            while (true)
            {
                int wait;
                lock (RateGate)
                {
                    DateTime now = DateTime.UtcNow;
                    DateTime next = nhentai ? _nhNextRequestUtc : _ehNextRequestUtc;
                    wait = next > now ? (int)Math.Ceiling((next - now).TotalMilliseconds) : 0;
                    if (wait <= 0)
                    {
                        DateTime newNext = now.AddMilliseconds(nhentai ? 6500 : 3500);
                        if (nhentai) _nhNextRequestUtc = newNext; else _ehNextRequestUtc = newNext;
                        return;
                    }
                }
                await Task.Delay(Math.Min(wait, 500), cancellationToken);
            }
        }

        private static bool CanUseProvider(string provider)
        {
            lock (HealthGate)
            {
                DateTime retry = String.Equals(provider, "nHentai", StringComparison.OrdinalIgnoreCase)
                    ? _nhRetryAfterUtc
                    : _ehRetryAfterUtc;
                return retry <= DateTime.UtcNow;
            }
        }

        private static void UpdateProviderHealth(string provider, AuthorProviderLookupResult result)
        {
            if (result == null || String.IsNullOrWhiteSpace(result.Error)) return;
            TimeSpan cooldown;
            if (String.Equals(result.ErrorStatus, "AccessDenied", StringComparison.OrdinalIgnoreCase))
                cooldown = TimeSpan.FromMinutes(30);
            else if (String.Equals(result.ErrorStatus, "RateLimited", StringComparison.OrdinalIgnoreCase))
                cooldown = TimeSpan.FromMinutes(10);
            else if (String.Equals(result.ErrorStatus, "ProtocolChanged", StringComparison.OrdinalIgnoreCase))
                cooldown = TimeSpan.FromMinutes(30);
            else if (String.Equals(result.ErrorStatus, "ProviderUnavailable", StringComparison.OrdinalIgnoreCase))
                cooldown = TimeSpan.FromMinutes(5);
            else
                return;

            lock (HealthGate)
            {
                DateTime retry = DateTime.UtcNow.Add(cooldown);
                if (String.Equals(provider, "nHentai", StringComparison.OrdinalIgnoreCase))
                    _nhRetryAfterUtc = retry;
                else
                    _ehRetryAfterUtc = retry;
            }
        }

        private static void Merge(AuthorProviderLookupResult target, AuthorProviderLookupResult source)
        {
            if (target == null || source == null) return;
            foreach (AuthorProviderCandidate incoming in source.Candidates ?? new List<AuthorProviderCandidate>())
            {
                if (incoming == null) continue;
                AuthorProviderCandidate existing = target.Candidates.FirstOrDefault(delegate(AuthorProviderCandidate x)
                {
                    return x != null && String.Equals(
                        NormalizeProviderName(x.TagName),
                        NormalizeProviderName(incoming.TagName),
                        StringComparison.OrdinalIgnoreCase);
                });
                if (existing == null)
                {
                    incoming.Provider = target.Provider;
                    target.Candidates.Add(incoming);
                    continue;
                }

                existing.IndependentWorkCount = Math.Max(existing.IndependentWorkCount, incoming.IndependentWorkCount);
                existing.IdentityScore = Math.Max(existing.IdentityScore, incoming.IdentityScore);
                existing.HardConflict = existing.HardConflict || incoming.HardConflict;
                foreach (string evidence in incoming.EvidenceSources)
                    if (!existing.EvidenceSources.Contains(evidence)) existing.EvidenceSources.Add(evidence);
                foreach (string alias in incoming.OtherNames)
                    if (!existing.OtherNames.Contains(alias)) existing.OtherNames.Add(alias);
                if (String.IsNullOrWhiteSpace(existing.GroupName)) existing.GroupName = incoming.GroupName;
            }
        }

        private static string NormalizeProviderName(string value)
        {
            string n = (value ?? "").Trim().Replace(' ', '_');
            return AuthorRules.NormalizeText(n).Replace(' ', '_');
        }
    }

    internal abstract class WorkEvidenceAuthorProvider : IAuthorProvider
    {
        public abstract string Id { get; }
        public abstract string DisplayName { get; }
        public abstract Task<AuthorProviderLookupResult> SearchAuthorAsync(string query, CancellationToken cancellationToken);

        protected static async Task<string> DownloadStringAsync(string url, CancellationToken cancellationToken, string authorization = null)
        {
            HttpWebRequest request = (HttpWebRequest)WebRequest.Create(url);
            request.Method = "GET";
            request.Accept = "text/html,application/json";
            request.UserAgent = "GuiGui/" + AppVersion.UserAgentVersion + " (Windows; metadata evidence lookup)";
            if (!String.IsNullOrWhiteSpace(authorization)) request.Headers[HttpRequestHeader.Authorization] = authorization;
            request.Timeout = 12000;
            request.ReadWriteTimeout = 12000;
            request.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;
            try { ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12; } catch { }

            using (CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
            {
                timeout.CancelAfter(12000);
                using (timeout.Token.Register(delegate { try { request.Abort(); } catch { } }))
                using (WebResponse response = await request.GetResponseAsync())
                using (Stream stream = response.GetResponseStream())
                using (StreamReader reader = new StreamReader(stream, Encoding.UTF8))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    return await reader.ReadToEndAsync();
                }
            }
        }

        protected static async Task<string> PostJsonAsync(string url, string json, CancellationToken cancellationToken)
        {
            byte[] body = Encoding.UTF8.GetBytes(json ?? "{}");
            HttpWebRequest request = (HttpWebRequest)WebRequest.Create(url);
            request.Method = "POST";
            request.ContentType = "application/json";
            request.Accept = "application/json";
            request.UserAgent = "GuiGui/" + AppVersion.UserAgentVersion + " (Windows; metadata evidence lookup)";
            request.Timeout = 12000;
            request.ReadWriteTimeout = 12000;
            request.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;
            try { ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12; } catch { }

            using (CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
            {
                timeout.CancelAfter(12000);
                using (timeout.Token.Register(delegate { try { request.Abort(); } catch { } }))
                {
                    using (Stream requestStream = await request.GetRequestStreamAsync())
                        await requestStream.WriteAsync(body, 0, body.Length, cancellationToken);
                    using (WebResponse response = await request.GetResponseAsync())
                    using (Stream stream = response.GetResponseStream())
                    using (StreamReader reader = new StreamReader(stream, Encoding.UTF8))
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        return await reader.ReadToEndAsync();
                    }
                }
            }
        }

        protected static string NormalizeTag(string value)
        {
            return AuthorRules.NormalizeText((value ?? "").Trim().Replace('_', ' '));
        }

        protected static int ScoreForWorks(int count, bool primaryIdentitySource)
        {
            if (count >= 3) return primaryIdentitySource ? 92 : 88;
            if (count >= 2) return 88;
            if (count == 1) return 75;
            return 0;
        }

        protected static string ClassifyWebError(WebException ex)
        {
            HttpWebResponse response = ex != null ? ex.Response as HttpWebResponse : null;
            if (response != null &&
                (response.StatusCode == HttpStatusCode.Forbidden ||
                 response.StatusCode == HttpStatusCode.Unauthorized))
                return "AccessDenied";
            if (response != null && (response.StatusCode == (HttpStatusCode)429 || response.StatusCode == HttpStatusCode.ServiceUnavailable))
                return "RateLimited";
            return "ProviderUnavailable";
        }
    }

    internal sealed class EhentaiAuthorProvider : WorkEvidenceAuthorProvider
    {
        private readonly bool _useLocalReference;
        public EhentaiAuthorProvider() : this(true) { }
        public EhentaiAuthorProvider(bool useLocalReference) { _useLocalReference = useLocalReference; }
        private sealed class EhGDataResponse { public List<EhGalleryMetadata> gmetadata { get; set; } }
        private sealed class EhGalleryMetadata
        {
            public object gid { get; set; }
            public object first_gid { get; set; }
            public string title { get; set; }
            public string title_jpn { get; set; }
            public string filecount { get; set; }
            public string[] tags { get; set; }
            public string error { get; set; }
        }

        public override string Id { get { return "E-Hentai"; } }
        public override string DisplayName { get { return "E-Hentai"; } }

        public override async Task<AuthorProviderLookupResult> SearchAuthorAsync(string query, CancellationToken cancellationToken)
        {
            AuthorProviderLookupResult result = new AuthorProviderLookupResult();
            result.Provider = Id;
            result.Query = query ?? "";
            if (String.IsNullOrWhiteSpace(query)) return result;
            try
            {
                string originalQuery = query.Trim();
                string referenceTag = "";
                string providerQuery = _useLocalReference && AuthorReferenceLibraryService.Current.TryResolveArtist(originalQuery, out referenceTag)
                    ? referenceTag
                    : originalQuery;
                string namespaceQuery = "artist:\"" + providerQuery + "$\"";
                string url = "https://e-hentai.org/?f_search=" + Uri.EscapeDataString(namespaceQuery);
                string html = await DownloadStringAsync(url, cancellationToken);
                MatchCollection matches = Regex.Matches(
                    html ?? "",
                    @"(?:https?://(?:e-hentai|exhentai)\.org)?/g/(?<gid>\d+)/(?<token>[0-9a-f]+)/",
                    RegexOptions.IgnoreCase);
                List<object[]> gidList = new List<object[]>();
                HashSet<string> seenGids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (Match match in matches)
                {
                    string gid = match.Groups["gid"].Value;
                    string token = match.Groups["token"].Value;
                    long numericGid;
                    if (!String.IsNullOrWhiteSpace(gid) && !String.IsNullOrWhiteSpace(token) &&
                        Int64.TryParse(gid, out numericGid) && seenGids.Add(gid))
                        gidList.Add(new object[] { numericGid, token });
                    if (gidList.Count >= 25) break;
                }
                if (gidList.Count > 0)
                {
                    JavaScriptSerializer serializer = new JavaScriptSerializer();
                    serializer.MaxJsonLength = Int32.MaxValue;
                    Dictionary<string, object> request = new Dictionary<string, object>();
                    request["method"] = "gdata";
                    request["gidlist"] = gidList;
                    request["namespace"] = 1;
                    string metadataJson = await PostJsonAsync(
                        "https://api.e-hentai.org/api.php",
                        serializer.Serialize(request),
                        cancellationToken);
                    EhGDataResponse metadata = serializer.Deserialize<EhGDataResponse>(metadataJson) ?? new EhGDataResponse();
                    HashSet<string> independentWorks = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    string expectedArtist = NormalizeTag(providerQuery);
                    foreach (EhGalleryMetadata gallery in metadata.gmetadata ?? new List<EhGalleryMetadata>())
                    {
                        if (gallery == null || !String.IsNullOrWhiteSpace(gallery.error)) continue;
                        bool exactArtist = (gallery.tags ?? new string[0]).Any(delegate(string tag)
                        {
                            if (String.IsNullOrWhiteSpace(tag) || !tag.StartsWith("artist:", StringComparison.OrdinalIgnoreCase)) return false;
                            return String.Equals(NormalizeTag(tag.Substring(7)), expectedArtist, StringComparison.OrdinalIgnoreCase);
                        });
                        if (!exactArtist) continue;
                        string firstGid = Convert.ToString(gallery.first_gid);
                        string gid = Convert.ToString(gallery.gid);
                        string chain = !String.IsNullOrWhiteSpace(firstGid) ? firstGid : gid;
                        if (!String.IsNullOrWhiteSpace(chain)) independentWorks.Add(chain);
                    }

                    int workCount = independentWorks.Count;
                    if (workCount > 0)
                    {
                        AuthorProviderCandidate candidate = new AuthorProviderCandidate();
                        candidate.Provider = Id;
                        candidate.ExternalId = "artist:" + providerQuery;
                        candidate.TagName = providerQuery;
                        candidate.TagNamespace = "artist";
                        candidate.EntityType = "Artist";
                        if (!String.Equals(NormalizeTag(originalQuery), NormalizeTag(providerQuery), StringComparison.OrdinalIgnoreCase))
                            candidate.OtherNames.Add(originalQuery);
                        candidate.IndependentWorkCount = workCount;
                        candidate.IdentityScore = ScoreForWorks(workCount, true);
                        candidate.EvidenceSources.Add("E-Hentai/LiveSearch+LiveGData");
                        if (!String.Equals(NormalizeTag(originalQuery), NormalizeTag(providerQuery), StringComparison.OrdinalIgnoreCase))
                            candidate.EvidenceSources.Add("EhTagTranslation/LocalReference");
                        int aggregateWorks;
                        if (_useLocalReference && AuthorReferenceLibraryService.TagDatabase.TryGetArtistEvidence(providerQuery, out aggregateWorks))
                            candidate.EvidenceSources.Add("EhTagDb/LocalAggregate(" + aggregateWorks + ")");
                        result.Candidates.Add(candidate);
                    }
                }

                // E-Hentai's canonical artist tags are commonly romanized. A Japanese
                // author name therefore cannot be found by artist:"<Japanese name>".
                // The website itself can still find galleries by ordinary text search;
                // inspect those galleries' artist tags as a conservative fallback.
                if (result.Candidates.Count == 0)
                    await AddExactNamespaceCandidatesAsync(result, originalQuery, "group", cancellationToken);
                if (result.Candidates.Count == 0)
                    await AddTitleSearchCandidatesAsync(result, originalQuery, cancellationToken);
            }
            catch (WebException ex)
            {
                if (cancellationToken.IsCancellationRequested)
                    throw new OperationCanceledException();
                result.Error = ex.Message;
                result.ErrorStatus = ClassifyWebError(ex);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                result.Error = ex.Message;
                result.ErrorStatus = "ProtocolChanged";
            }

            return result;
        }

        private static async Task AddTitleSearchCandidatesAsync(
            AuthorProviderLookupResult result,
            string query,
            CancellationToken cancellationToken)
        {
            string url = "https://e-hentai.org/?f_search=" + Uri.EscapeDataString("\"" + query + "\"");
            string html = await DownloadStringAsync(url, cancellationToken);
            MatchCollection matches = Regex.Matches(
                html ?? "",
                @"(?:https?://(?:e-hentai|exhentai)\.org)?/g/(?<gid>\d+)/(?<token>[0-9a-f]+)/",
                RegexOptions.IgnoreCase);
            List<object[]> gidList = new List<object[]>();
            HashSet<string> seenGids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (Match match in matches)
            {
                string gid = match.Groups["gid"].Value;
                string token = match.Groups["token"].Value;
                long numericGid;
                if (!String.IsNullOrWhiteSpace(gid) && !String.IsNullOrWhiteSpace(token) &&
                    Int64.TryParse(gid, out numericGid) && seenGids.Add(gid))
                    gidList.Add(new object[] { numericGid, token });
                if (gidList.Count >= 25) break;
            }
            if (gidList.Count == 0) return;

            JavaScriptSerializer serializer = new JavaScriptSerializer();
            serializer.MaxJsonLength = Int32.MaxValue;
            Dictionary<string, object> request = new Dictionary<string, object>();
            request["method"] = "gdata";
            request["gidlist"] = gidList;
            request["namespace"] = 1;
            string metadataJson = await PostJsonAsync(
                "https://api.e-hentai.org/api.php",
                serializer.Serialize(request),
                cancellationToken);
            EhGDataResponse metadata = serializer.Deserialize<EhGDataResponse>(metadataJson) ?? new EhGDataResponse();
            Dictionary<string, HashSet<string>> worksByIdentity =
                new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
            HashSet<string> searchAliasBridgeIdentities =
                new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            HashSet<string> verifiedTitleBridgeIdentities =
                new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (EhGalleryMetadata gallery in metadata.gmetadata ?? new List<EhGalleryMetadata>())
            {
                if (gallery == null || !String.IsNullOrWhiteSpace(gallery.error)) continue;
                bool queryAppearsInTitle = ContainsNormalized(gallery.title, query) || ContainsNormalized(gallery.title_jpn, query);
                string chain = !String.IsNullOrWhiteSpace(Convert.ToString(gallery.first_gid))
                    ? Convert.ToString(gallery.first_gid)
                    : Convert.ToString(gallery.gid);
                if (String.IsNullOrWhiteSpace(chain)) continue;
                string japaneseIdentity = GetLeadingBracket(gallery.title_jpn);
                string englishIdentity = GetLeadingBracket(gallery.title);
                bool structuredTitleBridge = ContainsNormalized(japaneseIdentity, query) && !String.IsNullOrWhiteSpace(englishIdentity);

                foreach (string tag in gallery.tags ?? new string[0])
                {
                    if (String.IsNullOrWhiteSpace(tag)) continue;
                    string identityNamespace;
                    if (tag.StartsWith("artist:", StringComparison.OrdinalIgnoreCase)) identityNamespace = "artist";
                    else if (tag.StartsWith("group:", StringComparison.OrdinalIgnoreCase)) identityNamespace = "group";
                    else continue;
                    string identityTag = tag.Substring(identityNamespace.Length + 1).Trim();
                    if (identityTag.Length == 0) continue;
                    bool tagMatchesEnglishIdentity = String.Equals(
                        NormalizeIdentityCompact(identityTag),
                        NormalizeIdentityCompact(englishIdentity),
                        StringComparison.OrdinalIgnoreCase);
                    if (structuredTitleBridge && !tagMatchesEnglishIdentity)
                        continue;
                    // E-Hentai may resolve a Japanese/Chinese alias in ordinary search even
                    // though both returned titles are romanized. In that case the leading
                    // title identity and the structured artist/group tag provide the bridge.
                    if (!queryAppearsInTitle && !tagMatchesEnglishIdentity) continue;
                    string identityKey = identityNamespace + "\n" + identityTag;
                    HashSet<string> works;
                    if (!worksByIdentity.TryGetValue(identityKey, out works))
                    {
                        works = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                        worksByIdentity[identityKey] = works;
                    }
                    works.Add(chain);
                    if (!queryAppearsInTitle) searchAliasBridgeIdentities.Add(identityKey);
                    if ((structuredTitleBridge && tagMatchesEnglishIdentity) || !queryAppearsInTitle)
                        verifiedTitleBridgeIdentities.Add(identityKey);
                }
            }

            foreach (KeyValuePair<string, HashSet<string>> pair in worksByIdentity)
            {
                string[] identityParts = pair.Key.Split('\n');
                string identityNamespace = identityParts[0];
                string identityTag = identityParts.Length > 1 ? identityParts[1] : "";
                bool searchAliasBridge = searchAliasBridgeIdentities.Contains(pair.Key);
                if (searchAliasBridge && (pair.Value.Count < 2 || searchAliasBridgeIdentities.Count != 1))
                    continue;
                bool verifiedTitleBridge = verifiedTitleBridgeIdentities.Contains(pair.Key) &&
                    pair.Value.Count >= 2 && verifiedTitleBridgeIdentities.Count == 1;
                AuthorProviderCandidate candidate = new AuthorProviderCandidate();
                candidate.Provider = "E-Hentai";
                candidate.ExternalId = identityNamespace + ":" + identityTag;
                candidate.TagName = identityTag;
                candidate.TagNamespace = identityNamespace;
                candidate.EntityType = identityNamespace == "group" ? "Group" : "Artist";
                candidate.OtherNames.Add(query);
                candidate.IndependentWorkCount = pair.Value.Count;
                candidate.IdentityScore = verifiedTitleBridge
                    ? Math.Max(92, ScoreForWorks(pair.Value.Count, true))
                    : ScoreForWorks(pair.Value.Count, true);
                candidate.EvidenceSources.Add(verifiedTitleBridge
                    ? (searchAliasBridge ? "E-Hentai/SearchAliasBridge+LiveGData" : "E-Hentai/TitleIdentityBridge+LiveGData")
                    : "E-Hentai/TitleSearch+LiveGData");
                result.Candidates.Add(candidate);
            }
        }

        private static string NormalizeIdentityCompact(string value)
        {
            string normalized = NormalizeTag(value);
            return Regex.Replace(normalized ?? "", @"[^\p{L}\p{Nd}]", "");
        }

        private static async Task AddExactNamespaceCandidatesAsync(
            AuthorProviderLookupResult result,
            string query,
            string identityNamespace,
            CancellationToken cancellationToken)
        {
            string search = identityNamespace + ":\"" + query + "$\"";
            string html = await DownloadStringAsync("https://e-hentai.org/?f_search=" + Uri.EscapeDataString(search), cancellationToken);
            MatchCollection matches = Regex.Matches(html ?? "", @"(?:https?://(?:e-hentai|exhentai)\.org)?/g/(?<gid>\d+)/(?<token>[0-9a-f]+)/", RegexOptions.IgnoreCase);
            List<object[]> gidList = new List<object[]>();
            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (Match match in matches)
            {
                long gid;
                if (Int64.TryParse(match.Groups["gid"].Value, out gid) && seen.Add(match.Groups["gid"].Value))
                    gidList.Add(new object[] { gid, match.Groups["token"].Value });
                if (gidList.Count >= 25) break;
            }
            if (gidList.Count == 0) return;
            JavaScriptSerializer serializer = new JavaScriptSerializer { MaxJsonLength = Int32.MaxValue };
            Dictionary<string, object> request = new Dictionary<string, object>();
            request["method"] = "gdata"; request["gidlist"] = gidList; request["namespace"] = 1;
            string json = await PostJsonAsync("https://api.e-hentai.org/api.php", serializer.Serialize(request), cancellationToken);
            EhGDataResponse metadata = serializer.Deserialize<EhGDataResponse>(json) ?? new EhGDataResponse();
            HashSet<string> works = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            string expected = NormalizeTag(query);
            foreach (EhGalleryMetadata gallery in metadata.gmetadata ?? new List<EhGalleryMetadata>())
            {
                if (gallery == null || !String.IsNullOrWhiteSpace(gallery.error)) continue;
                bool exact = (gallery.tags ?? new string[0]).Any(delegate(string tag)
                {
                    string prefix = identityNamespace + ":";
                    return !String.IsNullOrWhiteSpace(tag) && tag.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) &&
                        String.Equals(NormalizeTag(tag.Substring(prefix.Length)), expected, StringComparison.OrdinalIgnoreCase);
                });
                if (!exact) continue;
                string chain = !String.IsNullOrWhiteSpace(Convert.ToString(gallery.first_gid)) ? Convert.ToString(gallery.first_gid) : Convert.ToString(gallery.gid);
                if (chain.Length > 0) works.Add(chain);
            }
            if (works.Count == 0) return;
            result.Candidates.Add(new AuthorProviderCandidate
            {
                Provider = "E-Hentai",
                ExternalId = identityNamespace + ":" + query,
                TagName = query,
                TagNamespace = identityNamespace,
                EntityType = identityNamespace == "group" ? "Group" : "Artist",
                IndependentWorkCount = works.Count,
                IdentityScore = ScoreForWorks(works.Count, true),
                EvidenceSources = new List<string> { "E-Hentai/Exact" + identityNamespace + "+LiveGData" }
            });
        }

        private static bool ContainsNormalized(string text, string query)
        {
            string haystack = AuthorRules.NormalizeText(text ?? "");
            string needle = AuthorRules.NormalizeText(query ?? "");
            return needle.Length > 0 && haystack.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static string GetLeadingBracket(string title)
        {
            string value = (title ?? "").TrimStart();
            if (value.Length < 3) return "";
            char open = value[0];
            char close = open == '[' ? ']' : open == '［' ? '］' : open == '【' ? '】' : '\0';
            if (close == '\0') return "";
            int end = value.IndexOf(close, 1);
            return end > 1 ? value.Substring(1, end - 1).Trim() : "";
        }
    }

    internal sealed class NhentaiAuthorProvider : WorkEvidenceAuthorProvider
    {
        private sealed class NhSearchDto { public List<NhGalleryDto> result { get; set; } public int total { get; set; } }
        private sealed class NhGalleryDto
        {
            public int id { get; set; }
            public string english_title { get; set; }
            public string japanese_title { get; set; }
            public List<NhTagDto> tags { get; set; }
        }
        private sealed class NhTagDto { public string type { get; set; } public string name { get; set; } public string slug { get; set; } }
        private readonly string _apiKey;

        public NhentaiAuthorProvider() : this("") { }
        public NhentaiAuthorProvider(string apiKey) { _apiKey = (apiKey ?? "").Trim(); }

        public override string Id { get { return "nHentai"; } }
        public override string DisplayName { get { return "nHentai"; } }

        public override async Task<AuthorProviderLookupResult> SearchAuthorAsync(string query, CancellationToken cancellationToken)
        {
            AuthorProviderLookupResult result = new AuthorProviderLookupResult();
            result.Provider = Id;
            result.Query = query ?? "";
            if (String.IsNullOrWhiteSpace(query)) return result;
            try
            {
                string structured = "artist:\"" + query.Trim() + "\"";
                string url = "https://nhentai.net/api/v2/search?query=" + Uri.EscapeDataString(structured) + "&page=1&sort=date";
                string auth = _apiKey.Length > 0 ? "Key " + _apiKey : null;
                string json = await DownloadStringAsync(url, cancellationToken, auth);
                JavaScriptSerializer serializer = new JavaScriptSerializer();
                serializer.MaxJsonLength = Int32.MaxValue;
                NhSearchDto data = serializer.Deserialize<NhSearchDto>(json) ?? new NhSearchDto();
                bool titleBridge = false;
                if (data.total <= 0 || data.result == null || data.result.Count == 0)
                {
                    string plainUrl = "https://nhentai.net/api/v2/search?query=" + Uri.EscapeDataString(query.Trim()) + "&page=1&sort=date";
                    string plainJson = await DownloadStringAsync(plainUrl, cancellationToken, auth);
                    data = serializer.Deserialize<NhSearchDto>(plainJson) ?? new NhSearchDto();
                    titleBridge = true;
                }
                int works = data.total;
                string group = "";
                string artist = "";
                NhGalleryDto first = (data.result ?? new List<NhGalleryDto>()).FirstOrDefault();
                if (titleBridge)
                {
                    first = (data.result ?? new List<NhGalleryDto>()).FirstOrDefault(delegate(NhGalleryDto gallery)
                    {
                        return gallery != null && String.Equals(
                            NormalizeTag(GetLeadingBracket(gallery.japanese_title)),
                            NormalizeTag(query),
                            StringComparison.OrdinalIgnoreCase);
                    });
                }
                if (first != null)
                {
                    string detailUrl = "https://nhentai.net/api/v2/galleries/" + first.id;
                    string detailJson = await DownloadStringAsync(detailUrl, cancellationToken, auth);
                    NhGalleryDto detail = serializer.Deserialize<NhGalleryDto>(detailJson) ?? new NhGalleryDto();
                    NhTagDto artistTag = (detail.tags ?? new List<NhTagDto>()).FirstOrDefault(delegate(NhTagDto tag)
                    { return tag != null && String.Equals(tag.type, "artist", StringComparison.OrdinalIgnoreCase); });
                    NhTagDto groupTag = (detail.tags ?? new List<NhTagDto>()).FirstOrDefault(delegate(NhTagDto tag)
                    { return tag != null && String.Equals(tag.type, "group", StringComparison.OrdinalIgnoreCase); });
                    if (artistTag != null) artist = artistTag.name ?? artistTag.slug ?? "";
                    if (groupTag != null) group = groupTag.name ?? groupTag.slug ?? "";
                }
                if (works > 0 && artist.Length > 0)
                {
                    AuthorProviderCandidate candidate = new AuthorProviderCandidate();
                    candidate.Provider = Id;
                    candidate.ExternalId = "artist:" + artist;
                    candidate.TagName = artist;
                    candidate.GroupName = group;
                    if (!String.Equals(NormalizeTag(artist), NormalizeTag(query), StringComparison.OrdinalIgnoreCase))
                        candidate.OtherNames.Add(query.Trim());
                    candidate.IndependentWorkCount = works;
                    candidate.IdentityScore = ScoreForWorks(works, true);
                    candidate.EvidenceSources.Add(titleBridge ? "nHentai/API-v2-TitleBridge" : "nHentai/API-v2");
                    result.Candidates.Add(candidate);
                }
            }
            catch (WebException ex)
            {
                if (cancellationToken.IsCancellationRequested) throw new OperationCanceledException();
                result.Error = ex.Message;
                HttpWebResponse response = ex.Response as HttpWebResponse;
                result.ErrorStatus = response != null && response.StatusCode == HttpStatusCode.Unauthorized
                    ? "InvalidApiKey"
                    : ClassifyWebError(ex);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex) { result.Error = ex.Message; result.ErrorStatus = "ProtocolChanged"; }
            return result;
        }

        private static string GetLeadingBracket(string title)
        {
            string value = (title ?? "").TrimStart();
            if (value.Length < 3) return "";
            char close = value[0] == '[' ? ']' : value[0] == '［' ? '］' : value[0] == '【' ? '】' : '\0';
            if (close == '\0') return "";
            int end = value.IndexOf(close, 1);
            return end > 1 ? value.Substring(1, end - 1).Trim() : "";
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
        private readonly bool _useLocalReference;

        public OnlineAuthorResolver(AuthorEntityStore store, IAuthorProvider provider)
            : this(store, provider, null, true)
        {
        }

        public OnlineAuthorResolver(
            AuthorEntityStore store,
            IAuthorProvider provider,
            TagCleaningRuleStore tagCleaningStore,
            bool useLocalReference = true)
        {
            _store = store;
            _provider = provider;
            _tagCleaningStore = tagCleaningStore;
            _useLocalReference = useLocalReference;
        }

        public OnlineAuthorResolutionStats Resolve(
            List<PlanItem> plan,
            int maxLookups,
            bool saveCache,
            Action<ScanProgressInfo> progress,
            Func<bool> cancelRequested,
            bool manualLookup = false)
        {
            OnlineAuthorResolutionStats stats = new OnlineAuthorResolutionStats();
            if (plan == null || _store == null || _provider == null) return stats;

            AuthorEntityIndex knownEntities = _store.LoadIndex();
            List<string> queries = CollectQueries(plan, _tagCleaningStore, knownEntities, manualLookup);
            stats.PendingOnline = queries.Count;
            stats.LocalRecognized = Math.Max(0, plan.Count - CountUnresolved(plan));

            if (maxLookups <= 0) maxLookups = 1;
            if (queries.Count > maxLookups)
                queries = queries.Take(maxLookups).ToList();

            int total = queries.Count;
            CancellationTokenSource batchCts = new CancellationTokenSource();
            Dictionary<string, AuthorReferenceIdentity> localMatches =
                new Dictionary<string, AuthorReferenceIdentity>(StringComparer.OrdinalIgnoreCase);
            if (_useLocalReference)
            {
                foreach (string localQuery in queries)
                {
                    if (manualLookup &&
                        _store.GetFreshLookup("LocalReference", localQuery, 0) != null)
                        continue;

                    AuthorReferenceIdentity localIdentity;
                    if (AuthorReferenceLibraryService.Current.TryResolveIdentity(localQuery, out localIdentity))
                        localMatches[localQuery] = localIdentity;
                    else if (manualLookup)
                        _store.SaveLookupStatus(
                            "LocalReference",
                            localQuery,
                            "not_found",
                            "manual local reference lookup completed",
                            saveCache);
                }
            }

            // Local exact matches are a single in-memory batch and a single disk
            // commit. They never enter either online provider queue.
            int locallyMerged = _store.MergeResolvedLocalReferences(localMatches, saveCache);
            stats.OnlineResolved += locallyMerged;
            stats.NewEntities += locallyMerged;

            Dictionary<string, Task<AuthorProviderLookupResult>> scheduled =
                new Dictionary<string, Task<AuthorProviderLookupResult>>(StringComparer.OrdinalIgnoreCase);
            foreach (string pendingQuery in queries)
            {
                if (!manualLookup && localMatches.ContainsKey(pendingQuery)) continue;
                if (!manualLookup && ShouldSkipCachedLookup(pendingQuery)) continue;
                scheduled[pendingQuery] = _provider.SearchAuthorAsync(pendingQuery, batchCts.Token);
            }

            for (int i = 0; i < queries.Count; i++)
            {
                if (cancelRequested != null && cancelRequested())
                {
                    batchCts.Cancel();
                    throw new OperationCanceledException();
                }

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

                // A unique namespace + tag from the explicitly downloaded
                // local reference database is treated as verified local data.
                // It is persisted once and stops the online chain immediately.
                if (!manualLookup && localMatches.ContainsKey(query)) continue;

                AuthorLookupCacheRecord cached = manualLookup
                    ? null
                    : _store.GetFreshLookup(_provider.Id, query, 14);
                if (cached != null)
                {
                    string currentReferenceTag;
                    bool hasReferenceTag = AuthorReferenceLibraryService.Current.TryResolveArtist(query, out currentReferenceTag);
                    bool stalePreFallbackMiss = String.Equals(cached.Status, "not_found", StringComparison.OrdinalIgnoreCase) &&
                        ContainsCjk(query) && (cached.Detail ?? "").IndexOf("title-fallback-v3", StringComparison.OrdinalIgnoreCase) < 0;
                    bool staleReferenceMiss = String.Equals(cached.Status, "not_found", StringComparison.OrdinalIgnoreCase) &&
                        hasReferenceTag && (cached.Detail ?? "").IndexOf("reference=" + currentReferenceTag, StringComparison.OrdinalIgnoreCase) < 0;
                    if ((!stalePreFallbackMiss && !staleReferenceMiss && String.Equals(cached.Status, "not_found", StringComparison.OrdinalIgnoreCase)) ||
                        String.Equals(cached.Status, "ambiguous", StringComparison.OrdinalIgnoreCase) ||
                        String.Equals(cached.Status, "candidate", StringComparison.OrdinalIgnoreCase) ||
                        String.Equals(cached.Status, "resolved", StringComparison.OrdinalIgnoreCase))
                    {
                        stats.CachedSkipped++;
                        continue;
                    }

                    if (IsTemporaryFailureStillCoolingDown(cached))
                    {
                        stats.CachedSkipped++;
                        continue;
                    }
                }

                AuthorProviderLookupResult lookup = null;
                Task<AuthorProviderLookupResult> task;
                if (!scheduled.TryGetValue(query, out task))
                    continue;
                while (!task.IsCompleted)
                {
                    if (cancelRequested != null && cancelRequested())
                    {
                        batchCts.Cancel();
                        throw new OperationCanceledException();
                    }
                    Thread.Sleep(50);
                }

                if (task.IsCanceled)
                    throw new OperationCanceledException();

                if (task.IsFaulted)
                {
                    string detail = task.Exception != null
                        ? task.Exception.GetBaseException().Message
                        : "provider task failed";
                    _store.SaveLookupStatus(_provider.Id, query, "error", detail, saveCache);
                    stats.OnlineQueried++;
                    continue;
                }

                lookup = task.Result;
                stats.OnlineQueried++;
                if (lookup == null || !String.IsNullOrWhiteSpace(lookup.Error))
                {
                    _store.SaveLookupStatus(
                        _provider.Id,
                        query,
                        lookup != null ? (lookup.ErrorStatus ?? "NetworkError") : "NetworkError",
                        lookup != null ? lookup.Error : "empty response",
                        saveCache);
                    continue;
                }

                List<AuthorProviderCandidate> exact = GetStrongArtistMatches(query, lookup.Candidates);
                List<AuthorProviderCandidate> confirmed = exact.Where(delegate(AuthorProviderCandidate x)
                {
                    return x != null && x.IdentityScore >= 90 && !x.HardConflict;
                }).ToList();
                if (confirmed.Count == 1 && exact.Count == 1)
                {
                    AuthorEntityRecord entity = _store.MergeResolvedProviderResult(_provider.Id, query, confirmed[0], saveCache);
                    if (entity != null)
                    {
                        stats.OnlineResolved++;
                        stats.NewEntities++;
                    }
                }
                else if (exact.Count > 0)
                {
                    stats.OnlineAmbiguous++;
                    int bestScore = exact.Max(delegate(AuthorProviderCandidate x) { return x != null ? x.IdentityScore : 0; });
                    _store.SaveLookupStatus(
                        _provider.Id,
                        query,
                        "candidate",
                        exact.Count + " candidate(s), best IdentityScore=" + bestScore,
                        saveCache);
                }
                else
                {
                    stats.OnlineNotFound++;
                    string referenceTag;
                    string referenceDetail = AuthorReferenceLibraryService.Current.TryResolveArtist(query, out referenceTag)
                        ? "; reference=" + referenceTag
                        : "";
                    _store.SaveLookupStatus(_provider.Id, query, "not_found", "title-fallback-v3: no exact artist name/other-name match" + referenceDetail, saveCache);
                }
            }

            stats.StillUnresolved = Math.Max(0, stats.PendingOnline - stats.OnlineResolved);
            batchCts.Dispose();
            return stats;
        }

        private bool ShouldSkipCachedLookup(string query)
        {
            AuthorLookupCacheRecord cached = _store.GetFreshLookup(_provider.Id, query, 14);
            if (cached == null) return false;
            string currentReferenceTag;
            bool hasReferenceTag = AuthorReferenceLibraryService.Current.TryResolveArtist(query, out currentReferenceTag);
            bool stalePreFallbackMiss = String.Equals(cached.Status, "not_found", StringComparison.OrdinalIgnoreCase) &&
                ContainsCjk(query) && (cached.Detail ?? "").IndexOf("title-fallback-v3", StringComparison.OrdinalIgnoreCase) < 0;
            bool staleReferenceMiss = String.Equals(cached.Status, "not_found", StringComparison.OrdinalIgnoreCase) &&
                hasReferenceTag && (cached.Detail ?? "").IndexOf("reference=" + currentReferenceTag, StringComparison.OrdinalIgnoreCase) < 0;
            if ((!stalePreFallbackMiss && !staleReferenceMiss && String.Equals(cached.Status, "not_found", StringComparison.OrdinalIgnoreCase)) ||
                String.Equals(cached.Status, "ambiguous", StringComparison.OrdinalIgnoreCase) ||
                String.Equals(cached.Status, "candidate", StringComparison.OrdinalIgnoreCase) ||
                String.Equals(cached.Status, "resolved", StringComparison.OrdinalIgnoreCase)) return true;
            return IsTemporaryFailureStillCoolingDown(cached);
        }

        private static bool IsTemporaryFailureStillCoolingDown(AuthorLookupCacheRecord cached)
        {
            if (cached == null) return false;
            string status = cached.Status ?? "";
            TimeSpan cooldown;
            if (String.Equals(status, "AccessDenied", StringComparison.OrdinalIgnoreCase))
                cooldown = TimeSpan.FromMinutes(30);
            else if (String.Equals(status, "RateLimited", StringComparison.OrdinalIgnoreCase))
                cooldown = TimeSpan.FromMinutes(10);
            else if (String.Equals(status, "ProtocolChanged", StringComparison.OrdinalIgnoreCase))
                cooldown = TimeSpan.FromMinutes(30);
            else if (String.Equals(status, "ProviderUnavailable", StringComparison.OrdinalIgnoreCase) ||
                     String.Equals(status, "NetworkError", StringComparison.OrdinalIgnoreCase) ||
                     String.Equals(status, "error", StringComparison.OrdinalIgnoreCase))
                cooldown = TimeSpan.FromMinutes(5);
            else
                return false;

            DateTime checkedUtc;
            if (!DateTime.TryParse(
                    cached.CheckedUtc ?? "",
                    System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.RoundtripKind,
                    out checkedUtc))
                return false;
            return checkedUtc.ToUniversalTime().Add(cooldown) > DateTime.UtcNow;
        }

        private static List<string> CollectQueries(
            List<PlanItem> plan,
            TagCleaningRuleStore tagCleaningStore,
            AuthorEntityIndex knownEntities,
            bool manualLookup)
        {
            List<string> result = new List<string>();
            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (PlanItem item in plan)
            {
                if (item == null || String.IsNullOrWhiteSpace(item.Author)) continue;
                RecognitionVisual visual = RecognitionVisualResolver.Resolve(item);
                string code = visual != null ? (visual.Code ?? "") : "";
                if (!manualLookup &&
                    !String.Equals(code, "new", StringComparison.OrdinalIgnoreCase) &&
                    !String.Equals(code, "new-reuse", StringComparison.OrdinalIgnoreCase))
                    continue;

                string raw = item.Author.Trim();
                StructuredAuthorParts structured = AuthorRules.GetStructuredAuthorParts(raw);
                IEnumerable<string> queries = structured != null && structured.Creators != null && structured.Creators.Count > 0
                    ? (IEnumerable<string>)structured.Creators
                    : (IEnumerable<string>)new string[] { raw };

                foreach (string candidate in queries)
                {
                    string query = (candidate ?? "").Trim();
                    if (query.Length < 2 || query.Length > 120 || AuthorRules.IsMetadataTag(query, tagCleaningStore)) continue;

                    // AuthorEntities.json is indexed first; GuiGuiAuthorIndex.db is
                    // its read-only fallback. A local hit must never be sent to an
                    // online provider merely because it is a newly created folder.
                    AuthorEntityMatch known = knownEntities != null
                        ? knownEntities.Resolve(query)
                        : new AuthorEntityMatch();
                    if (!manualLookup && known.Found) continue;

                    string key = AuthorRules.NormalizeText(query);
                    if (key.Length > 0 && seen.Add(key)) result.Add(query);
                }
            }

            return result;
        }

        private static bool ContainsCjk(string value)
        {
            foreach (char c in value ?? "")
            {
                if ((c >= 0x3040 && c <= 0x30ff) ||
                    (c >= 0x3400 && c <= 0x9fff) ||
                    (c >= 0xac00 && c <= 0xd7af)) return true;
            }
            return false;
        }

        private static int CountUnresolved(List<PlanItem> plan)
        {
            int count = 0;
            foreach (PlanItem item in plan)
            {
                RecognitionVisual visual = RecognitionVisualResolver.Resolve(item);
                string code = visual != null ? (visual.Code ?? "") : "";
                if (code == "new" || code == "new-reuse") count++;
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
