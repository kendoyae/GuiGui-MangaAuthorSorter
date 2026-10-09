using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace MangaAuthorSorter
{
    // Remote configuration changes source locations and schedules; it cannot inject executable parsers.
    internal sealed class ReferenceSourceConfig
    {
        public string id { get; set; }
        public string name { get; set; }
        public bool enabled { get; set; }
        public string type { get; set; }
        public string repository { get; set; }
        public string branch { get; set; }
        public string pathPrefix { get; set; }
        public string fileSuffix { get; set; }
        public int checkIntervalHours { get; set; }
    }
    internal sealed class AdvancedBuilderSourceConfig
    {
        public string id { get; set; }
        public bool enabled { get; set; }
        public string repository { get; set; }
        public string releaseTag { get; set; }
        public string assetName { get; set; }
    }
    internal sealed class PublicDatabaseSourceConfig
    {
        public string manifestUrl { get; set; }
    }
    internal sealed class ReferenceSourcesConfig
    {
        public int schemaVersion { get; set; }
        public int revision { get; set; }
        public int refreshHours { get; set; }
        public List<ReferenceSourceConfig> sources { get; set; }
        public List<AdvancedBuilderSourceConfig> advancedSources { get; set; }
        public PublicDatabaseSourceConfig publicDatabase { get; set; }
    }
    internal sealed class ReferenceSourceFile
    {
        public string Source;
        public string Path;
        public string Sha;
        public long Size;
        public string Url;
    }
    internal sealed class AuthorReferenceRemoteInfo
    {
        public long Size;
        public DateTime UpdatedUtc;
        public string DownloadUrl = "";
        public int TotalFiles;
        public int ChangedFiles;
        public List<ReferenceSourceFile> Changed = new List<ReferenceSourceFile>();
        public List<ReferenceSourceFile> All = new List<ReferenceSourceFile>();
        public List<string> EnabledSourceIds = new List<string>();
        public int ConfigRevision;
        public List<ReferenceSourceFile> Withdrawn = new List<ReferenceSourceFile>();
        public string Notice = "";
    }
    internal sealed class AuthorReferenceLocalInfo
    {
        public bool Exists;
        public long Size;
        public DateTime UpdatedUtc;
        public string Revision = "";
        public int ArtistCount;
        public long WorkCount;
        public long EvidenceCount;
        public long MatchedCount;
        public long AmbiguousCount;
        public long CandidateCount;
        public long BaselineCount;
    }
    internal sealed class AuthorReferenceDownloadProgress
    {
        public long Received;
        public long Total;
        public double BytesPerSecond;
    }
    internal enum AuthorReferenceDownloadResult { Completed, Paused }
    internal sealed class AuthorReferenceIdentity
    {
        public string Namespace = "";
        public string Tag = "";
        public string DisplayName = "";
        public string Source = "nh-metadata-archive";
    }

    /// <summary>
    /// User-owned work-level evidence. Public DB is rebuilt transactionally from the official baseline.
    /// A unique text match is candidate corroboration, not an independent provider identity.
    /// History not imported at bootstrap is explicitly marked 'baseline-only'.
    /// </summary>
    internal sealed class AuthorReferenceLibraryService
    {
        public const string ConfigUrl = "https://kendoyae.github.io/GuiGui-MangaAuthorSorter/data/sources.json";
        private static readonly AuthorReferenceLibraryService _current = new AuthorReferenceLibraryService();
        public static AuthorReferenceLibraryService Current { get { return _current; } }
        private readonly string _path = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "GuiGuiReferenceEvidence.db");
        private readonly string _cachePath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "SourceConfigCache.json");
        private readonly string _localConfigPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "SourcesConfig.json");
        private readonly object _gate = new object();
        private CancellationTokenSource _cancel;
        private bool _pauseRequested;
        private ReferenceSourcesConfig _config;
        public bool IsDownloading { get; private set; }
        public string Name { get { return "GitHub metadata (CSV)"; } }
        public string Path { get { return _path; } }
        public string PartialPath { get { return _path + ".download"; } }
        public event Action<AuthorReferenceDownloadProgress> ProgressChanged;
        private AuthorReferenceLibraryService() { }

        // Work CSV is not an E-Hentai namespace identity. NEVER use it to assert an EH artist/group tag.
        public bool TryResolveArtist(string name, out string tag) { tag = ""; return false; }
        public bool TryResolveIdentity(string name, out AuthorReferenceIdentity identity) { identity = null; return false; }

        private static JavaScriptSerializer Serializer() { return new JavaScriptSerializer { MaxJsonLength = Int32.MaxValue, RecursionLimit = 32 }; }
        private static HttpWebRequest NewRequest(string url)
        {
            Uri uri = new Uri(url);
            if (uri.Scheme != Uri.UriSchemeHttps) throw new InvalidDataException("HTTPS required for reference source");
            HttpWebRequest req = (HttpWebRequest)WebRequest.Create(uri);
            req.UserAgent = "GuiGui/" + AppVersion.UserAgentVersion + " (+https://github.com/kendoyae/MangaAuthorSorter)";
            req.Accept = "application/vnd.github+json, text/plain, */*";
            req.Timeout = 20000;
            req.ReadWriteTimeout = 25000;
            req.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;
            try { ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12; } catch { }
            return req;
        }
        private static byte[] DownloadBytes(string url, long limit, CancellationToken token)
        {
            HttpWebRequest req = NewRequest(url);
            using (token.Register(delegate { try { req.Abort(); } catch { } }))
            using (HttpWebResponse response = (HttpWebResponse)req.GetResponse())
            using (Stream input = response.GetResponseStream())
            using (MemoryStream output = new MemoryStream())
            {
                if (response.ContentLength > limit) throw new InvalidDataException("source file exceeds safe download size");
                byte[] block = new byte[65536]; int n;
                while ((n = input.Read(block, 0, block.Length)) > 0)
                {
                    token.ThrowIfCancellationRequested();
                    output.Write(block, 0, n);
                    if (output.Length > limit) throw new InvalidDataException("source file exceeds safe download size");
                }
                return output.ToArray();
            }
        }
        private static string GetText(string url, long limit, CancellationToken token) { return Encoding.UTF8.GetString(DownloadBytes(url, limit, token)); }
        private static bool ValidConfig(ReferenceSourcesConfig config)
        {
            if (config == null || config.schemaVersion != 1 || config.sources == null || config.sources.Count > 20) return false;
            if (config.publicDatabase != null && !String.IsNullOrWhiteSpace(config.publicDatabase.manifestUrl) &&
                !ValidOfficialManifestUrl(config.publicDatabase.manifestUrl)) return false;
            foreach (ReferenceSourceConfig s in config.sources)
            {
                if (s == null || String.IsNullOrWhiteSpace(s.id) || s.id.Length > 80 || s.type != "github-monthly-csv") return false;
                if (String.IsNullOrWhiteSpace(s.repository) || !System.Text.RegularExpressions.Regex.IsMatch(s.repository, @"^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$")) return false;
                if (!System.Text.RegularExpressions.Regex.IsMatch(s.branch ?? "", @"^[A-Za-z0-9_.-]{1,100}$")) return false;
                if (String.IsNullOrWhiteSpace(s.pathPrefix) || s.pathPrefix.Contains("..") || s.pathPrefix.StartsWith("/")) return false;
                if (s.fileSuffix != ".csv") return false;
                if (s.checkIntervalHours < 1 || s.checkIntervalHours > 720) return false;
            }
            if (config.advancedSources != null)
            {
                if (config.advancedSources.Count > 3 ||
                    config.advancedSources.Select(x => x == null ? "" : x.id).Distinct(StringComparer.OrdinalIgnoreCase).Count() != config.advancedSources.Count) return false;
                foreach (AdvancedBuilderSourceConfig item in config.advancedSources)
                {
                    if (item == null || (item.id != "eh-current" && item.id != "eh-tag-aggregate")) return false;
                    if (String.IsNullOrWhiteSpace(item.repository) || !System.Text.RegularExpressions.Regex.IsMatch(item.repository,@"^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$")) return false;
                    if (String.IsNullOrWhiteSpace(item.releaseTag) || !System.Text.RegularExpressions.Regex.IsMatch(item.releaseTag,@"^[A-Za-z0-9_./-]{1,100}$") || item.releaseTag.Contains("..")) return false;
                    if (String.IsNullOrWhiteSpace(item.assetName) || !System.Text.RegularExpressions.Regex.IsMatch(item.assetName,@"^[A-Za-z0-9_.-]{1,100}$")) return false;
                    if (item.id=="eh-current" && !(item.assetName.EndsWith(".zstd",StringComparison.OrdinalIgnoreCase) || item.assetName.EndsWith(".zst",StringComparison.OrdinalIgnoreCase))) return false;
                    if (item.id=="eh-tag-aggregate" && !item.assetName.EndsWith(".sqlite.gz",StringComparison.OrdinalIgnoreCase)) return false;
                }
            }
            return config.sources.Select(s => s.id).Distinct(StringComparer.OrdinalIgnoreCase).Count() == config.sources.Count;
        }
        internal static bool ValidOfficialManifestUrl(string url)
        {
            Uri parsed;
            return Uri.TryCreate(url, UriKind.Absolute, out parsed) && parsed.Scheme == Uri.UriSchemeHttps &&
                (parsed.Host.Equals("raw.githubusercontent.com", StringComparison.OrdinalIgnoreCase) ||
                 parsed.Host.Equals("kendoyae.github.io", StringComparison.OrdinalIgnoreCase));
        }
        internal ReferenceSourcesConfig GetSourcesConfiguration(CancellationToken token)
        {
            return LoadConfig(token);
        }
        public void EnsureEvidenceStorage()
        {
            using (EvidenceDatabase db = new EvidenceDatabase(_path)) db.EnsureSchema();
        }
        private ReferenceSourcesConfig LoadConfig(CancellationToken token)
        {
            // Explicit user request refreshes the remote configuration, with safe cached/local fallback.
            ReferenceSourcesConfig remote = null;
            try
            {
                string json = GetText(ConfigUrl, 128 * 1024, token);
                remote = Serializer().Deserialize<ReferenceSourcesConfig>(json);
                if (ValidConfig(remote))
                {
                    string temp = _cachePath + ".tmp";
                    File.WriteAllText(temp, json, Encoding.UTF8);
                    if (File.Exists(_cachePath)) File.Delete(_cachePath);
                    File.Move(temp, _cachePath);
                }
                else remote = null;
            }
            catch (OperationCanceledException) { throw; }
            catch { remote = null; }
            if (remote != null) return remote;
            foreach (string file in new[] { _cachePath, _localConfigPath })
                try { if (File.Exists(file)) { ReferenceSourcesConfig c = Serializer().Deserialize<ReferenceSourcesConfig>(File.ReadAllText(file, Encoding.UTF8)); if (ValidConfig(c)) return c; } }
                catch { }
            try
            {
                ReferenceSourcesConfig embedded = Serializer().Deserialize<ReferenceSourcesConfig>(
                    EmbeddedResourceService.ReadText("MangaAuthorSorter.SourcesConfig.json"));
                if (ValidConfig(embedded)) return embedded;
            }
            catch { }
            throw new InvalidDataException("No valid GitHub sources configuration (remote, cached, local or embedded).");
        }
        public bool ShouldAutoCheck()
        {
            try
            {
                string stamp = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "SourceLastChecked.utc");
                if (!File.Exists(stamp)) return true;
                DateTime last;
                if (!DateTime.TryParse(File.ReadAllText(stamp).Trim(), null,
                    System.Globalization.DateTimeStyles.RoundtripKind, out last)) return true;
                ReferenceSourcesConfig configured = null;
                foreach (string configFile in new[] { _cachePath, _localConfigPath })
                {
                    if (!File.Exists(configFile)) continue;
                    try { configured = Serializer().Deserialize<ReferenceSourcesConfig>(File.ReadAllText(configFile, Encoding.UTF8)); if (ValidConfig(configured)) break; }
                    catch { configured = null; }
                }
                int hours = configured != null && configured.refreshHours >= 1 && configured.refreshHours <= 720 ? configured.refreshHours : 24;
                if (configured != null)
                    foreach (ReferenceSourceConfig source in configured.sources.Where(x => x.enabled))
                        hours = Math.Min(hours, source.checkIntervalHours);
                return DateTime.UtcNow >= last.ToUniversalTime().AddHours(hours);
            }
            catch { return true; }
        }
        public Task<AuthorReferenceRemoteInfo> GetRemoteInfoAsync(bool fullHistory = false)
        {
            return Task.Run(delegate
            {
                ReferenceSourcesConfig config = LoadConfig(CancellationToken.None);
                AuthorReferenceRemoteInfo info = new AuthorReferenceRemoteInfo { ConfigRevision = config.revision };
                foreach (ReferenceSourceConfig source in config.sources.Where(s => s.enabled))
                {
                    string treeUrl = "https://api.github.com/repos/" + source.repository + "/git/trees/" + Uri.EscapeDataString(source.branch) + "?recursive=1";
                    Dictionary<string, object> doc = Serializer().DeserializeObject(GetText(treeUrl, 4 * 1024 * 1024, CancellationToken.None)) as Dictionary<string, object>;
                    object truncated;
                    if (doc == null || (doc.TryGetValue("truncated", out truncated) && Convert.ToBoolean(truncated))) throw new InvalidDataException("GitHub tree list incomplete: " + source.id);
                    object entries;
                    if (!doc.TryGetValue("tree", out entries)) throw new InvalidDataException("GitHub tree unavailable: " + source.id);
                    foreach (object element in (object[])entries)
                    {
                        Dictionary<string, object> item = element as Dictionary<string, object>;
                        if (item == null || Convert.ToString(item["type"]) != "blob") continue;
                        string path = Convert.ToString(item["path"]);
                        if (!path.StartsWith(source.pathPrefix, StringComparison.Ordinal) || !path.EndsWith(source.fileSuffix, StringComparison.OrdinalIgnoreCase)) continue;
                        string sha = Convert.ToString(item["sha"]);
                        long size = Convert.ToInt64(item["size"]);
                        if (size <= 0 || size > 64L * 1024 * 1024 || !System.Text.RegularExpressions.Regex.IsMatch(sha, "^[0-9a-fA-F]{40}$")) continue;
                        ReferenceSourceFile f = new ReferenceSourceFile { Source = source.id, Path = path, Sha = sha, Size = size,
                            Url = "https://raw.githubusercontent.com/" + source.repository + "/" + source.branch + "/" + path };
                        info.All.Add(f);
                    }
                    info.EnabledSourceIds.Add(source.id);
                }
                info.All = info.All.OrderBy(f => f.Source, StringComparer.Ordinal).ThenBy(f => f.Path, StringComparer.Ordinal).ToList();
                // At initial install, do not re-download the full historical archive already shipped as a public index.
                // Older SHA baselines are explicitly not marked imported; changes to those blobs trigger a download.
                using (EvidenceDatabase db = new EvidenceDatabase(_path))
                {
                    db.EnsureSchema();
                    if (!fullHistory && PublicAuthorIndexMergeService.HasActiveIndex && !db.HasSourceRows())
                    {
                        foreach (var group in info.All.GroupBy(x => x.Source))
                        {
                            ReferenceSourceFile newest = group.OrderBy(x => x.Path, StringComparer.Ordinal).LastOrDefault();
                            foreach (ReferenceSourceFile file in group)
                                if (!Object.ReferenceEquals(file, newest)) db.RecordBaseline(file.Source, file.Path, file.Sha);
                        }
                        info.Notice = "Historical files baselined (not imported); newest month scheduled for evidence import.";
                    }
                    foreach (ReferenceSourceFile f in info.All)
                        if (!String.Equals(fullHistory ? db.GetImportedSha(f.Source, f.Path) : db.GetFileSha(f.Source, f.Path), f.Sha, StringComparison.OrdinalIgnoreCase)) info.Changed.Add(f);
                    HashSet<string> active = new HashSet<string>(info.All.Select(x => x.Source + "\n" + x.Path), StringComparer.Ordinal);
                    foreach (ReferenceSourceFile stored in db.ImportedFiles())
                        if (info.EnabledSourceIds.Contains(stored.Source) && !active.Contains(stored.Source + "\n" + stored.Path))
                            info.Withdrawn.Add(stored);
                }
                info.TotalFiles = info.All.Count;
                info.ChangedFiles = info.Changed.Count + info.Withdrawn.Count;
                info.Size = info.Changed.Sum(f => f.Size);
                info.DownloadUrl = ConfigUrl;
                info.UpdatedUtc = DateTime.UtcNow;
                lock (_gate) _config = config;
                try { File.WriteAllText(System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "SourceLastChecked.utc"),
                    DateTime.UtcNow.ToString("o"), Encoding.UTF8); } catch { }
                return info;
            });
        }
        public AuthorReferenceLocalInfo GetLocalInfo()
        {
            AuthorReferenceLocalInfo result = new AuthorReferenceLocalInfo();
            if (!File.Exists(_path)) return result;
            try
            {
                using (EvidenceDatabase db = new EvidenceDatabase(_path))
                {
                    db.EnsureSchema();
                    result.Exists = true;
                    result.Size = new FileInfo(_path).Length;
                    result.UpdatedUtc = File.GetLastWriteTimeUtc(_path);
                    result.WorkCount = db.UniqueWorks();
                    result.EvidenceCount = db.UniqueEvidence();
                    result.MatchedCount = db.CountUniqueWhere("Status='matched'");
                    result.AmbiguousCount = db.CountUniqueWhere("Status='ambiguous'");
                    result.CandidateCount = db.CountUniqueWhere("Status='candidate'");
                    result.BaselineCount = db.CountBaseline();
                    result.ArtistCount = (int)Math.Min(Int32.MaxValue, result.EvidenceCount);
                    result.Revision = db.Revision();
                }
            }
            catch { result.Exists = false; }
            return result;
        }
        public async Task<AuthorReferenceDownloadResult> DownloadAsync(AuthorReferenceRemoteInfo remote)
        {
            lock (_gate)
            {
                if (IsDownloading) throw new InvalidOperationException("reference update already running");
                IsDownloading = true; _pauseRequested = false; _cancel = new CancellationTokenSource();
            }
            try
            {
                if (remote == null) remote = await GetRemoteInfoAsync();
                CancellationToken token = _cancel.Token;
                await Task.Run(delegate
                {
                    AuthorEntityStore store = new AuthorEntityStore(System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "AuthorEntities.json"));
                    AuthorEntityIndex index = store.LoadIndex();
                    long total = remote.Size, received = 0;
                    using (EvidenceDatabase db = new EvidenceDatabase(_path))
                    {
                        db.EnsureSchema();
                        foreach (ReferenceSourceFile withdrawn in remote.Withdrawn)
                        {
                            token.ThrowIfCancellationRequested();
                            db.Withdraw(withdrawn.Source, withdrawn.Path);
                        }
                        foreach (ReferenceSourceFile file in remote.Changed)
                        {
                            token.ThrowIfCancellationRequested();
                            byte[] csv = DownloadBytes(file.Url, Math.Min(64L * 1024 * 1024, file.Size + 1), token);
                            if (csv.LongLength != file.Size || !String.Equals(GitBlobSha(csv), file.Sha, StringComparison.OrdinalIgnoreCase))
                                throw new InvalidDataException("Git blob size/SHA mismatch: " + file.Path);
                            db.Import(file, csv, index, token);
                            received += file.Size;
                            Action<AuthorReferenceDownloadProgress> callback = ProgressChanged;
                            if (callback != null) callback(new AuthorReferenceDownloadProgress { Received = received, Total = total });
                        }
                    }
                }, token);
                // Stage one coherent public-index rebuild AFTER all changed CSV files have
                // committed. A restart activates the staged image before any readers open.
                await Task.Run(delegate { PublicAuthorIndexMergeService.BuildPending(_path); }, token);
                return AuthorReferenceDownloadResult.Completed;
            }
            catch (OperationCanceledException)
            {
                lock (_gate) { if (_pauseRequested) return AuthorReferenceDownloadResult.Paused; }
                throw;
            }
            catch (WebException)
            {
                lock (_gate) { if (_pauseRequested) return AuthorReferenceDownloadResult.Paused; }
                throw;
            }
            finally
            {
                lock (_gate) { IsDownloading = false; _cancel.Dispose(); _cancel = null; }
            }
        }
        /// <summary>Import one user-chosen CSV without pulling the complete upstream archive.</summary>
        public Task ImportLocalCsvAsync(string filename, CancellationToken token, AdvancedOperationControl control = null)
        {
            return Task.Run(delegate
            {
                if (!String.Equals(System.IO.Path.GetExtension(filename), ".csv", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("请选择 CSV 文件。");
                if(control!=null)control.Checkpoint(token);
                byte[] contents = File.ReadAllBytes(filename);
                if (contents.LongLength > 64L * 1024 * 1024) throw new InvalidDataException("单个 CSV 文件超过 64 MB 限制。");
                ReferenceSourceFile f = new ReferenceSourceFile { Source="nh-metadata-archive",
                    Path="manual/"+System.IO.Path.GetFileName(filename), Size=contents.LongLength,
                    Sha=GitBlobSha(contents) };
                AuthorEntityStore store = new AuthorEntityStore(System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "AuthorEntities.json"));
                using (EvidenceDatabase db = new EvidenceDatabase(_path))
                {
                    db.EnsureSchema(); db.Import(f, contents, store.LoadIndex(), token);
                }
                token.ThrowIfCancellationRequested();
                PublicAuthorIndexMergeService.BuildPending(_path);
            }, token);
        }
        public Task ImportLocalCsvDirectoryAsync(string directory, AuthorReferenceRemoteInfo remote, IProgress<AdvancedSourceProgress> progress, CancellationToken token, AdvancedOperationControl control = null)
        {
            return Task.Run(delegate
            {
                if (!Directory.Exists(directory)) throw new DirectoryNotFoundException(directory);
                string[] files=Directory.GetFiles(directory,"*.csv",SearchOption.TopDirectoryOnly);
                if(files.Length==0)throw new InvalidDataException("当前目录没有可过滤的 CSV 文件。");
                AuthorEntityIndex index=new AuthorEntityStore(System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"AuthorEntities.json")).LoadIndex();
                using(EvidenceDatabase db=new EvidenceDatabase(_path))
                {
                    db.EnsureSchema();
                    if (remote != null)
                        foreach (ReferenceSourceFile withdrawn in remote.Withdrawn)
                            if (withdrawn.Source=="nh-metadata-archive") db.Withdraw(withdrawn.Source,withdrawn.Path);
                    for(int i=0;i<files.Length;i++)
                    {
                        if(control!=null)control.Checkpoint(token); else token.ThrowIfCancellationRequested();
                        string name=System.IO.Path.GetFileName(files[i]);
                        byte[] bytes=File.ReadAllBytes(files[i]);
                        if(bytes.LongLength>64L*1024*1024)throw new InvalidDataException("CSV 单文件超过 64MB");
                        string sha=GitBlobSha(bytes);
                        string expected=files[i]+".sha";
                        if(File.Exists(expected) && !String.Equals(File.ReadAllText(expected).Trim(),sha,StringComparison.OrdinalIgnoreCase))
                            throw new InvalidDataException("CSV 文件校验不通过："+name);
                        var file=new ReferenceSourceFile{Source="nh-metadata-archive",Path="by_month/"+name,Sha=sha,Size=bytes.LongLength};
                        if(!String.Equals(db.GetImportedSha(file.Source,file.Path),file.Sha,StringComparison.OrdinalIgnoreCase))
                            db.Import(file,bytes,index,token);
                        if(progress!=null)progress.Report(new AdvancedSourceProgress{Message="过滤 CSV "+(i+1)+"/"+files.Length+"："+name,Current=i+1,Total=files.Length});
                    }
                }
                token.ThrowIfCancellationRequested();
                PublicAuthorIndexMergeService.BuildPending(_path, null, progress, token, control);
            },token);
        }
        /// <summary>
        /// Stream an E-Hentai SQLite source, keeping the evidence scoped by source and
        /// transaction. Gzip is built into Framework; ZSTD is optional/external.
        /// </summary>
        public Task ImportExternalSourceAsync(string sourceId, string fileName,
            IProgress<AdvancedSourceProgress> progress, CancellationToken token, AdvancedOperationControl control = null)
        {
            return Task.Run(delegate
            {
                if (sourceId != "eh-current" && sourceId != "eh-tag-aggregate")
                    throw new InvalidDataException("未知的高级数据源。");
                string local=AdvancedSourcePreparation.Prepare(fileName, progress, token, control);
                bool generated=!String.Equals(local,fileName,StringComparison.OrdinalIgnoreCase);
                try
                {
                    string hash;
                    using (SHA256 digest = SHA256.Create())
                    using (FileStream input = File.OpenRead(fileName))
                        hash=BitConverter.ToString(digest.ComputeHash(input)).Replace("-", "").ToLowerInvariant();
                    string sourceFile=sourceId == "eh-current" ? "full-sqlite" : "tag-aggregate";
                    var file=new ReferenceSourceFile { Source=sourceId,Path=sourceFile,Sha=hash,Size=new FileInfo(fileName).Length };
                    AuthorEntityIndex index = new AuthorEntityStore(System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"AuthorEntities.json")).LoadIndex();
                    using (AdvancedSourceSqliteReader reader = new AdvancedSourceSqliteReader(local))
                    using (EvidenceDatabase db = new EvidenceDatabase(_path))
                    {
                        db.EnsureSchema();
                        if (!String.Equals(db.GetImportedSha(file.Source,file.Path),file.Sha,StringComparison.OrdinalIgnoreCase))
                            db.ImportRows(file,reader.Read(sourceId,token),index,progress,token,control);
                        else if (progress != null) progress.Report(new AdvancedSourceProgress {
                            Message="来源文件此前已过滤；直接重新合并公共库",Current=0,Total=0 });
                    }
                    token.ThrowIfCancellationRequested();
                    PublicAuthorIndexMergeService.BuildPending(_path, null, progress, token, control);
                }
                finally
                {
                    if (generated && File.Exists(local)) try{ File.Delete(local); } catch{}
                }
            },token);
        }
        /// <summary>Rebuild staged public index from already committed evidence. No re-download or re-import.</summary>
        public Task RebuildPublicIndexAsync(IProgress<AdvancedSourceProgress> progress, CancellationToken token,
            AdvancedOperationControl control = null)
        {
            return Task.Run(delegate
            {
                if (!File.Exists(_path)) throw new FileNotFoundException("找不到已保存的参考证据，请先过滤来源。", _path);
                PublicAuthorIndexMergeService.BuildPending(_path, null, progress, token, control);
            }, token);
        }
        public void Pause() { lock (_gate) { if (_cancel != null) { _pauseRequested = true; _cancel.Cancel(); } } }
        // Recalculate identity corroboration from the current unified user/public index without downloading CSVs.
        public Task RevalidateAsync()
        {
            return Task.Run(delegate
            {
                lock (_gate) { if (IsDownloading) throw new InvalidOperationException("Wait for reference download to finish."); }
                AuthorEntityStore store = new AuthorEntityStore(System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "AuthorEntities.json"));
                AuthorEntityIndex index = store.LoadIndex();
                using (EvidenceDatabase db = new EvidenceDatabase(_path)) { db.EnsureSchema(); db.Revalidate(index); }
                PublicAuthorIndexMergeService.BuildPending(_path);
            });
        }
        public bool TryGetArtistWorkEvidence(string name, out long workCount)
        {
            workCount = 0;
            if (String.IsNullOrWhiteSpace(name) || !File.Exists(_path)) return false;
            try
            {
                using (EvidenceDatabase db = new EvidenceDatabase(_path))
                {
                    db.EnsureSchema();
                    workCount = db.WorkEvidenceForName(AuthorRules.NormalizeText(name));
                    return workCount > 0;
                }
            }
            catch { return false; }
        }
        public List<string[]> GetEvidenceSamples(int maxRows)
        {
            using (EvidenceDatabase db = new EvidenceDatabase(_path)) { db.EnsureSchema(); return db.Sample(Math.Max(1, Math.Min(500, maxRows))); }
        }
        private static string GitBlobSha(byte[] bytes)
        {
            byte[] prefix = Encoding.ASCII.GetBytes("blob " + bytes.Length + "\0");
            using (SHA1 hash = SHA1.Create())
            {
                hash.TransformBlock(prefix, 0, prefix.Length, prefix, 0);
                hash.TransformFinalBlock(bytes, 0, bytes.Length);
                return BitConverter.ToString(hash.Hash).Replace("-", "").ToLowerInvariant();
            }
        }

        // Quote-aware RFC4180 reader: handles commas, escaped quotes, and embedded newlines.
        internal static IEnumerable<string[]> ReadCsv(TextReader input)
        {
            return GuiGui.Shared.ReferenceNameRules.ReadDelimited(input, ',');
        }

        private sealed class EvidenceDatabase : IDisposable
        {
            private IntPtr _db;
            private const int OK = 0, ROW = 100, DONE = 101;
            public EvidenceDatabase(string path)
            {
                byte[] filename = Bytes(path);
                if (Native.sqlite3_open_v2(filename, out _db, 0x00000002 | 0x00000004, IntPtr.Zero) != OK || _db == IntPtr.Zero)
                    throw new IOException("Cannot open reference evidence database");
                Native.sqlite3_busy_timeout(_db, 5000);
            }
            public void Dispose() { if (_db != IntPtr.Zero) { Native.sqlite3_close(_db); _db = IntPtr.Zero; } }
            private static byte[] Bytes(string s) { return Encoding.UTF8.GetBytes((s ?? "") + "\0"); }
            private void Sql(string sql)
            {
                IntPtr err;
                if (Native.sqlite3_exec(_db, Bytes(sql), IntPtr.Zero, IntPtr.Zero, out err) != OK)
                {
                    if (err != IntPtr.Zero) Native.sqlite3_free(err);
                    throw new InvalidDataException("SQLite reference transaction failed");
                }
            }
            public void EnsureSchema()
            {
                Sql("CREATE TABLE IF NOT EXISTS SourceFile(SourceId TEXT NOT NULL, Path TEXT NOT NULL, Sha TEXT NOT NULL, State TEXT NOT NULL, UpdatedUtc TEXT NOT NULL, PRIMARY KEY(SourceId,Path));" +
                    "CREATE TABLE IF NOT EXISTS WorkEvidence(SourceId TEXT NOT NULL, SourceFile TEXT NOT NULL, GalleryId TEXT NOT NULL, JapaneseTitle TEXT, EnglishTitle TEXT, ArtistRaw TEXT, GroupRaw TEXT, PRIMARY KEY(SourceId,GalleryId,SourceFile));" +
                    "CREATE TABLE IF NOT EXISTS EntityEvidence(SourceId TEXT NOT NULL, SourceFile TEXT NOT NULL, GalleryId TEXT NOT NULL, EntityType TEXT NOT NULL, Name TEXT NOT NULL, NormalizedName TEXT NOT NULL, Status TEXT NOT NULL, EntityKey TEXT, PRIMARY KEY(SourceId,SourceFile,GalleryId,EntityType,NormalizedName));" +
                    "CREATE INDEX IF NOT EXISTS IX_EntityEvidence_Name ON EntityEvidence(EntityType,NormalizedName);" +
                    "CREATE INDEX IF NOT EXISTS IX_EntityEvidence_Status ON EntityEvidence(Status);");
                // The two display-normalized columns share the builder's exact rules.
                if (Scalar("SELECT COUNT(*) FROM pragma_table_info('EntityEvidence') WHERE name='PrettyName'") == "0")
                    Sql("ALTER TABLE EntityEvidence ADD COLUMN PrettyName TEXT NOT NULL DEFAULT '';");
                if (Scalar("SELECT COUNT(*) FROM pragma_table_info('EntityEvidence') WHERE name='NormalizedPretty'") == "0")
                    Sql("ALTER TABLE EntityEvidence ADD COLUMN NormalizedPretty TEXT NOT NULL DEFAULT '';");
            }
            private IntPtr Prepare(string sql)
            {
                IntPtr stmt;
                if (Native.sqlite3_prepare_v2(_db, Bytes(sql), -1, out stmt, IntPtr.Zero) != OK || stmt == IntPtr.Zero)
                    throw new InvalidDataException("Invalid SQLite statement: " + sql);
                return stmt;
            }
            private static string GetString(IntPtr stmt, int col)
            {
                IntPtr p = Native.sqlite3_column_text(stmt, col); if (p == IntPtr.Zero) return "";
                int len = Native.sqlite3_column_bytes(stmt, col); byte[] b = new byte[len]; Marshal.Copy(p, b, 0, len); return Encoding.UTF8.GetString(b);
            }
            private void Execute(string sql, params string[] args)
            {
                IntPtr stmt = Prepare(sql);
                try
                {
                    for (int i = 0; i < args.Length; i++) { byte[] data = Bytes(args[i]); if (Native.sqlite3_bind_text(stmt, i+1, data, data.Length-1, new IntPtr(-1)) != OK) throw new InvalidDataException("SQLite bind failed"); }
                    if (Native.sqlite3_step(stmt) != DONE) throw new InvalidDataException("SQLite evidence write failed");
                }
                finally { Native.sqlite3_finalize(stmt); }
            }
            private string Scalar(string sql, params string[] args)
            {
                IntPtr stmt = Prepare(sql);
                try
                {
                    for (int i = 0; i < args.Length; i++) { byte[] data = Bytes(args[i]); if (Native.sqlite3_bind_text(stmt, i+1, data, data.Length-1, new IntPtr(-1)) != OK) throw new InvalidDataException("SQLite bind failed"); }
                    return Native.sqlite3_step(stmt) == ROW ? GetString(stmt, 0) : "";
                }
                finally { Native.sqlite3_finalize(stmt); }
            }
            public long Count(string table) { return Int64.Parse(Scalar("SELECT CAST(COUNT(*) AS TEXT) FROM " + table)); }
            public long WorkEvidenceForName(string norm)
            {
                return Int64.Parse(Scalar("SELECT CAST(COUNT(*) AS TEXT) FROM (SELECT DISTINCT SourceId,GalleryId FROM EntityEvidence WHERE EntityType='Artist' AND NormalizedName=?)", norm));
            }
            public long CountBaseline() { return Int64.Parse(Scalar("SELECT CAST(COUNT(*) AS TEXT) FROM SourceFile WHERE State='baseline-only'")); }
            public long UniqueWorks() { return Int64.Parse(Scalar("SELECT CAST(COUNT(*) AS TEXT) FROM (SELECT DISTINCT SourceId,GalleryId FROM WorkEvidence)")); }
            public long UniqueEvidence() { return Int64.Parse(Scalar("SELECT CAST(COUNT(*) AS TEXT) FROM (SELECT DISTINCT SourceId,GalleryId,EntityType,NormalizedName FROM EntityEvidence)")); }
            public long CountUniqueWhere(string condition) { return Int64.Parse(Scalar("SELECT CAST(COUNT(*) AS TEXT) FROM (SELECT DISTINCT SourceId,GalleryId,EntityType,NormalizedName FROM EntityEvidence WHERE " + condition + ")")); }
            public long CountWhere(string condition) { return Int64.Parse(Scalar("SELECT CAST(COUNT(*) AS TEXT) FROM EntityEvidence WHERE " + condition)); }
            public bool HasSourceRows() { return Count("SourceFile") != 0; }
            public string GetFileSha(string source, string path) { return Scalar("SELECT Sha FROM SourceFile WHERE SourceId=? AND Path=? AND State!='withdrawn'", source, path); }
            public string GetImportedSha(string source, string path) { return Scalar("SELECT Sha FROM SourceFile WHERE SourceId=? AND Path=? AND State='imported'", source, path); }
            public string Revision() { return Scalar("SELECT COALESCE(MAX(UpdatedUtc),'') FROM SourceFile WHERE State='imported'"); }
            public List<ReferenceSourceFile> ImportedFiles()
            {
                List<ReferenceSourceFile> files = new List<ReferenceSourceFile>();
                IntPtr stmt = Prepare("SELECT SourceId,Path,Sha FROM SourceFile WHERE State='imported'");
                try
                {
                    while (Native.sqlite3_step(stmt) == ROW)
                        files.Add(new ReferenceSourceFile { Source = GetString(stmt, 0), Path = GetString(stmt, 1), Sha = GetString(stmt, 2) });
                }
                finally { Native.sqlite3_finalize(stmt); }
                return files;
            }
            public void Withdraw(string source, string path)
            {
                Sql("BEGIN IMMEDIATE TRANSACTION");
                try
                {
                    Execute("DELETE FROM EntityEvidence WHERE SourceId=? AND SourceFile=?", source, path);
                    Execute("DELETE FROM WorkEvidence WHERE SourceId=? AND SourceFile=?", source, path);
                    Execute("UPDATE SourceFile SET State='withdrawn',UpdatedUtc=? WHERE SourceId=? AND Path=?", DateTime.UtcNow.ToString("o"), source, path);
                    Sql("COMMIT");
                }
                catch { Sql("ROLLBACK"); throw; }
            }
            public void RecordBaseline(string source, string path, string sha)
            {
                Execute("INSERT OR IGNORE INTO SourceFile(SourceId,Path,Sha,State,UpdatedUtc) VALUES(?,?,?,'baseline-only',?)", source, path, sha, DateTime.UtcNow.ToString("o"));
            }
            public void Revalidate(AuthorEntityIndex index)
            {
                List<string[]> distinct = new List<string[]>();
                IntPtr stmt = Prepare("SELECT DISTINCT EntityType,NormalizedName,Name FROM EntityEvidence");
                try
                {
                    while (Native.sqlite3_step(stmt) == ROW)
                        distinct.Add(new[] { GetString(stmt, 0), GetString(stmt, 1), GetString(stmt, 2) });
                }
                finally { Native.sqlite3_finalize(stmt); }
                Sql("BEGIN IMMEDIATE TRANSACTION");
                try
                {
                    foreach (string[] name in distinct)
                    {
                        AuthorEntityMatch match = index.Resolve(name[2], name[0]);
                        string status = match.Ambiguous ? "ambiguous" : match.Found && match.Entity!=null ? "matched" : "candidate";
                        string entityKey = status=="matched" ? (match.Entity.EntityId??"") : "";
                        Execute("UPDATE EntityEvidence SET Status=?,EntityKey=? WHERE EntityType=? AND NormalizedName=?",
                            status,entityKey,name[0],name[1]);
                    }
                    Sql("COMMIT");
                }
                catch { Sql("ROLLBACK"); throw; }
            }
            public List<string[]> Sample(int maxRows)
            {
                List<string[]> result = new List<string[]>();
                IntPtr stmt = Prepare("SELECT e.EntityType,e.Name,e.SourceId,e.GalleryId,e.Status,e.EntityKey," +
                    "COALESCE(NULLIF(w.JapaneseTitle,''),w.EnglishTitle,'') FROM EntityEvidence e LEFT JOIN WorkEvidence w ON " +
                    "e.SourceId=w.SourceId AND e.SourceFile=w.SourceFile AND e.GalleryId=w.GalleryId " +
                    "ORDER BY CASE e.Status WHEN 'ambiguous' THEN 0 WHEN 'candidate' THEN 1 ELSE 2 END,e.SourceFile DESC LIMIT " + maxRows);
                try
                {
                    while (Native.sqlite3_step(stmt) == ROW)
                    {
                        string[] row=new string[7]; for (int i=0;i<7;i++) row[i]=GetString(stmt,i);
                        result.Add(row);
                    }
                }
                finally { Native.sqlite3_finalize(stmt); }
                return result;
            }
            private static string CsvValue(string[] row, Dictionary<string,int> headers, string column)
            {
                int i; return headers.TryGetValue(column, out i) && i < row.Length ? (row[i] ?? "").Trim() : "";
            }
            public void ImportRows(ReferenceSourceFile file, IEnumerable<AdvancedGalleryRow> rows,
                AuthorEntityIndex index, IProgress<AdvancedSourceProgress> progress, CancellationToken token, AdvancedOperationControl control)
            {
                Sql("BEGIN IMMEDIATE TRANSACTION");
                try
                {
                    Execute("DELETE FROM EntityEvidence WHERE SourceId=? AND SourceFile=?", file.Source, file.Path);
                    Execute("DELETE FROM WorkEvidence WHERE SourceId=? AND SourceFile=?", file.Source, file.Path);
                    Dictionary<string,string> matchCache = new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
                    long count=0;
                    foreach (AdvancedGalleryRow item in rows)
                    {
                        if(control!=null)control.Checkpoint(token); else token.ThrowIfCancellationRequested();
                        if (String.IsNullOrWhiteSpace(item.Id)) continue;
                        if (!item.IsTagAggregate)
                            Execute("INSERT OR REPLACE INTO WorkEvidence(SourceId,SourceFile,GalleryId,JapaneseTitle,EnglishTitle,ArtistRaw,GroupRaw) VALUES(?,?,?,?,?,?,?)",
                                file.Source,file.Path,item.Id,item.JapaneseTitle,item.EnglishTitle,item.Artists,item.Groups);
                        StoreNames(file,item.Id,"Artist",item.Artists,index,matchCache);
                        StoreNames(file,item.Id,"Group",item.Groups,index,matchCache);
                        count++;
                        if (count%5000==0 && progress!=null) progress.Report(new AdvancedSourceProgress {
                            Message="过滤 "+file.Source+"：已处理 "+count.ToString("N0")+" 条", Current=0, Total=0 });
                    }
                    Execute("INSERT OR REPLACE INTO SourceFile(SourceId,Path,Sha,State,UpdatedUtc) VALUES(?,?,?,'imported',?)",
                        file.Source,file.Path,file.Sha,DateTime.UtcNow.ToString("o"));
                    Sql("COMMIT");
                    if (progress!=null)progress.Report(new AdvancedSourceProgress {Message="已完成过滤 "+count.ToString("N0")+" 条，准备合并公共库",Current=0,Total=0});
                }
                catch { Sql("ROLLBACK"); throw; }
            }
            public void Import(ReferenceSourceFile file, byte[] csv, AuthorEntityIndex index, CancellationToken token)
            {
                // Each changed file is replaced atomically, including removed works and their evidence.
                Sql("BEGIN IMMEDIATE TRANSACTION");
                try
                {
                    Execute("DELETE FROM EntityEvidence WHERE SourceId=? AND SourceFile=?", file.Source, file.Path);
                    Execute("DELETE FROM WorkEvidence WHERE SourceId=? AND SourceFile=?", file.Source, file.Path);
                    using (StreamReader reader = new StreamReader(new MemoryStream(csv), Encoding.UTF8, true))
                    {
                        IEnumerator<string[]> rows = ReadCsv(reader).GetEnumerator();
                        if (!rows.MoveNext()) throw new InvalidDataException("Empty CSV: " + file.Path);
                        Dictionary<string,int> columns = new Dictionary<string,int>(StringComparer.OrdinalIgnoreCase);
                        for (int i=0; i<rows.Current.Length; i++) columns[rows.Current[i].Trim().TrimStart('\ufeff')] = i;
                        foreach (string required in new[] { "ID", "ARTIST", "GROUP_NAME" })
                            if (!columns.ContainsKey(required)) throw new InvalidDataException("CSV missing " + required + ": " + file.Path);
                        HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
                        Dictionary<string, string> matchCache = new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
                        while (rows.MoveNext())
                        {
                            token.ThrowIfCancellationRequested();
                            string[] row = rows.Current;
                            string id = CsvValue(row,columns,"ID");
                            long numericId;
                            if (!Int64.TryParse(id,out numericId) || numericId<=0 || !seen.Add(id)) continue;
                            string artists = CsvValue(row,columns,"ARTIST");
                            string groups = CsvValue(row,columns,"GROUP_NAME");
                            string jp = CsvValue(row,columns,"JP_TITLE");
                            string en = CsvValue(row,columns,"EN_TITLE");
                            Execute("INSERT INTO WorkEvidence(SourceId,SourceFile,GalleryId,JapaneseTitle,EnglishTitle,ArtistRaw,GroupRaw) VALUES(?,?,?,?,?,?,?)", file.Source,file.Path,id,jp,en,artists,groups);
                            StoreNames(file,id,"Artist",artists,index,matchCache);
                            StoreNames(file,id,"Group",groups,index,matchCache);
                        }
                    }
                    Execute("INSERT OR REPLACE INTO SourceFile(SourceId,Path,Sha,State,UpdatedUtc) VALUES(?,?,?,'imported',?)",
                        file.Source, file.Path, file.Sha, DateTime.UtcNow.ToString("o"));
                    Sql("COMMIT");
                }
                catch { Sql("ROLLBACK"); throw; }
            }
            private void StoreNames(ReferenceSourceFile file, string galleryId, string type, string raw, AuthorEntityIndex index, Dictionary<string,string> cache)
            {
                // One gallery may name several artists/groups. Comma separation only splits records, NEVER merges identities.
                foreach (string part in GuiGui.Shared.ReferenceNameRules.Split(raw))
                {
                    string name = GuiGui.Shared.ReferenceNameRules.Display(part); string norm = GuiGui.Shared.ReferenceNameRules.Normalize(name);
                    if (norm.Length==0 || name.Length>250) continue;
                    string cacheKey=type+":"+norm, value;
                    if (!cache.TryGetValue(cacheKey,out value))
                    {
                        AuthorEntityMatch match = index.Resolve(name,type);
                        string status = match.Ambiguous ? "ambiguous" : match.Found && match.Entity != null ? "matched" : "candidate";
                        string entityKey = match.Found && !match.Ambiguous && match.Entity!=null
                            ? (match.Entity.EntityId ?? "") : "";
                        value = status+"\n"+entityKey;
                        cache[cacheKey]=value;
                    }
                    string[] parts=value.Split(new[]{'\n'},2);
                    string pretty = GuiGui.Shared.ReferenceNameRules.PrettyTag(name);
                    string normalizedPretty = GuiGui.Shared.ReferenceNameRules.Normalize(pretty);
                    Execute("INSERT OR REPLACE INTO EntityEvidence(SourceId,SourceFile,GalleryId,EntityType,Name,NormalizedName,PrettyName,NormalizedPretty,Status,EntityKey) VALUES(?,?,?,?,?,?,?,?,?,?)",
                        file.Source,file.Path,galleryId,type,name,norm,pretty,normalizedPretty,parts[0],parts.Length>1?parts[1]:"");
                }
            }
            private static class Native
            {
                [DllImport("winsqlite3.dll", CallingConvention=CallingConvention.Cdecl)] public static extern int sqlite3_open_v2(byte[] file,out IntPtr db,int flags,IntPtr vfs);
                [DllImport("winsqlite3.dll", CallingConvention=CallingConvention.Cdecl)] public static extern int sqlite3_close(IntPtr db);
                [DllImport("winsqlite3.dll", CallingConvention=CallingConvention.Cdecl)] public static extern int sqlite3_busy_timeout(IntPtr db,int ms);
                [DllImport("winsqlite3.dll", CallingConvention=CallingConvention.Cdecl)] public static extern int sqlite3_exec(IntPtr db,byte[] sql,IntPtr callback,IntPtr arg,out IntPtr err);
                [DllImport("winsqlite3.dll", CallingConvention=CallingConvention.Cdecl)] public static extern void sqlite3_free(IntPtr ptr);
                [DllImport("winsqlite3.dll", CallingConvention=CallingConvention.Cdecl)] public static extern int sqlite3_prepare_v2(IntPtr db,byte[] sql,int len,out IntPtr stmt,IntPtr tail);
                [DllImport("winsqlite3.dll", CallingConvention=CallingConvention.Cdecl)] public static extern int sqlite3_bind_text(IntPtr stmt,int index,byte[] text,int length,IntPtr destructor);
                [DllImport("winsqlite3.dll", CallingConvention=CallingConvention.Cdecl)] public static extern int sqlite3_step(IntPtr stmt);
                [DllImport("winsqlite3.dll", CallingConvention=CallingConvention.Cdecl)] public static extern int sqlite3_finalize(IntPtr stmt);
                [DllImport("winsqlite3.dll", CallingConvention=CallingConvention.Cdecl)] public static extern IntPtr sqlite3_column_text(IntPtr stmt,int col);
                [DllImport("winsqlite3.dll", CallingConvention=CallingConvention.Cdecl)] public static extern int sqlite3_column_bytes(IntPtr stmt,int col);
            }
        }
    }
}
