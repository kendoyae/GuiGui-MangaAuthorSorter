using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace MangaAuthorSorter
{
    internal sealed class AuthorReferenceRemoteInfo
    {
        public long Size;
        public DateTime UpdatedUtc;
        public string DownloadUrl = "";
    }

    internal sealed class AuthorReferenceLocalInfo
    {
        public bool Exists;
        public long Size;
        public DateTime UpdatedUtc;
        public string Revision = "";
        public int ArtistCount;
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
        public string Source = "EhTagTranslation";
    }

    internal sealed class AuthorReferenceLibraryService
    {
        private static readonly AuthorReferenceLibraryService _current =
            new AuthorReferenceLibraryService(
                "EhTagTranslation",
                "https://github.com/EhTagTranslation/Database/releases/latest/download/db.text.json.gz",
                System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "EhTagReference.json.gz"),
                true);
        private static readonly AuthorReferenceLibraryService _tagDatabase =
            new AuthorReferenceLibraryService(
                "EhTagDb",
                "https://github.com/EhTagTranslation/EhTagDb/releases/latest/download/aggregated.sqlite.gz",
                System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "EhTagAggregate.sqlite.gz"),
                false);

        private readonly object _gate = new object();
        private readonly string _path;
        private readonly string _latestUrl;
        private readonly bool _isNameMapping;
        private Dictionary<string, List<string>> _artistByName;
        private Dictionary<string, List<AuthorReferenceIdentity>> _identityByName;
        private AuthorReferenceLocalInfo _localInfo;
        private CancellationTokenSource _downloadCancellation;
        private bool _pauseRequested;

        public static AuthorReferenceLibraryService Current { get { return _current; } }
        public static AuthorReferenceLibraryService TagDatabase { get { return _tagDatabase; } }
        public static AuthorReferenceLibraryService[] All { get { return new[] { _current, _tagDatabase }; } }
        public string Name { get; private set; }
        public string Path { get { return _path; } }
        public string PartialPath { get { return _path + ".part"; } }
        private string ExpandedSqlitePath { get { return _path.EndsWith(".gz", StringComparison.OrdinalIgnoreCase) ? _path.Substring(0, _path.Length - 3) : _path + ".sqlite"; } }
        public bool IsDownloading { get; private set; }
        public event Action<AuthorReferenceDownloadProgress> ProgressChanged;

        private AuthorReferenceLibraryService(string name, string latestUrl, string path, bool isNameMapping)
        {
            Name = name;
            _latestUrl = latestUrl;
            _path = path;
            _isNameMapping = isNameMapping;
        }

        public Task<AuthorReferenceRemoteInfo> GetRemoteInfoAsync()
        {
            return Task.Run(delegate
            {
                HttpWebRequest request = CreateRequest(_latestUrl);
                request.AddRange(0, 0);
                using (HttpWebResponse response = (HttpWebResponse)request.GetResponse())
                {
                    long total = ParseTotalLength(response.Headers[HttpResponseHeader.ContentRange]);
                    if (total <= 0) total = response.ContentLength;
                    return new AuthorReferenceRemoteInfo
                    {
                        Size = total,
                        UpdatedUtc = response.LastModified.ToUniversalTime(),
                        DownloadUrl = response.ResponseUri != null ? response.ResponseUri.AbsoluteUri : _latestUrl
                    };
                }
            });
        }

        public AuthorReferenceLocalInfo GetLocalInfo()
        {
            lock (_gate)
            {
                if (!File.Exists(_path)) return new AuthorReferenceLocalInfo();
                if (_localInfo == null) EnsureLoadedUnlocked();
                return _localInfo ?? new AuthorReferenceLocalInfo();
            }
        }

        public bool TryResolveArtist(string name, out string tag)
        {
            tag = "";
            if (!_isNameMapping) return false;
            string key = AuthorRules.NormalizeText(name ?? "");
            if (key.Length == 0) return false;
            lock (_gate)
            {
                EnsureLoadedUnlocked();
                List<string> matches;
                if (_artistByName == null || !_artistByName.TryGetValue(key, out matches) || matches == null || matches.Count != 1)
                    return false;
                tag = matches[0];
                return tag.Length > 0;
            }
        }

        public bool TryResolveIdentity(string name, out AuthorReferenceIdentity identity)
        {
            identity = null;
            if (!_isNameMapping) return false;
            string key = AuthorRules.NormalizeText(name ?? "");
            if (key.Length == 0) return false;
            lock (_gate)
            {
                EnsureLoadedUnlocked();
                List<AuthorReferenceIdentity> matches;
                if (_identityByName == null || !_identityByName.TryGetValue(key, out matches) || matches == null) return false;
                List<AuthorReferenceIdentity> unique = matches
                    .GroupBy(delegate(AuthorReferenceIdentity x) { return (x.Namespace ?? "") + "\n" + (x.Tag ?? ""); }, StringComparer.OrdinalIgnoreCase)
                    .Select(delegate(IGrouping<string, AuthorReferenceIdentity> x) { return x.First(); })
                    .ToList();
                if (unique.Count != 1) return false;
                identity = unique[0];
                return identity != null && identity.Tag.Length > 0;
            }
        }

        public bool TryGetArtistEvidence(string tag, out int workCount)
        {
            return TryGetIdentityEvidence("artist", tag, out workCount);
        }

        public bool TryGetIdentityEvidence(string identityNamespace, string tag, out int workCount)
        {
            workCount = 0;
            if (_isNameMapping || String.IsNullOrWhiteSpace(tag) || !File.Exists(_path)) return false;
            try
            {
                EnsureExpandedSqlite();
                IntPtr db;
                if (sqlite3_open16(ExpandedSqlitePath, out db) != 0 || db == IntPtr.Zero) return false;
                try
                {
                    IntPtr statement;
                    string safeNamespace = String.Equals(identityNamespace, "group", StringComparison.OrdinalIgnoreCase) ? "group" : "artist";
                    string sql = "SELECT count FROM tag_aggregate WHERE namespace='" + safeNamespace + "' AND tag=? LIMIT 1";
                    if (sqlite3_prepare_v2(db, sql, -1, out statement, IntPtr.Zero) != 0 || statement == IntPtr.Zero) return false;
                    try
                    {
                        byte[] utf8 = Encoding.UTF8.GetBytes(tag + "\0");
                        if (sqlite3_bind_text(statement, 1, utf8, utf8.Length - 1, new IntPtr(-1)) != 0) return false;
                        if (sqlite3_step(statement) != 100) return false;
                        workCount = sqlite3_column_int(statement, 0);
                        return workCount > 0;
                    }
                    finally { sqlite3_finalize(statement); }
                }
                finally { sqlite3_close(db); }
            }
            catch { return false; }
        }

        public async Task<AuthorReferenceDownloadResult> DownloadAsync(AuthorReferenceRemoteInfo remote)
        {
            lock (_gate)
            {
                if (IsDownloading) throw new InvalidOperationException("download already active");
                IsDownloading = true;
                _pauseRequested = false;
                _downloadCancellation = new CancellationTokenSource();
            }
            try
            {
                return await DownloadCoreAsync(remote ?? await GetRemoteInfoAsync(), _downloadCancellation.Token);
            }
            catch (WebException)
            {
                lock (_gate)
                {
                    if (_pauseRequested) return AuthorReferenceDownloadResult.Paused;
                }
                throw;
            }
            catch (OperationCanceledException)
            {
                lock (_gate)
                {
                    if (_pauseRequested) return AuthorReferenceDownloadResult.Paused;
                }
                throw;
            }
            finally
            {
                lock (_gate)
                {
                    IsDownloading = false;
                    if (_downloadCancellation != null) _downloadCancellation.Dispose();
                    _downloadCancellation = null;
                }
            }
        }

        public void Pause()
        {
            lock (_gate)
            {
                if (!IsDownloading || _downloadCancellation == null) return;
                _pauseRequested = true;
                _downloadCancellation.Cancel();
            }
        }

        private async Task<AuthorReferenceDownloadResult> DownloadCoreAsync(AuthorReferenceRemoteInfo remote, CancellationToken token)
        {
            long existing = File.Exists(PartialPath) ? new FileInfo(PartialPath).Length : 0;
            if (remote.Size > 0 && existing > remote.Size)
            {
                File.Delete(PartialPath);
                existing = 0;
            }

            // Always start from the stable latest-release URL. GitHub's redirected
            // asset URL is signed and may expire while a download is paused.
            HttpWebRequest request = CreateRequest(_latestUrl);
            if (existing > 0) request.AddRange(existing);
            using (token.Register(delegate { try { request.Abort(); } catch { } }))
            using (HttpWebResponse response = (HttpWebResponse)await request.GetResponseAsync())
            {
                bool resumed = response.StatusCode == HttpStatusCode.PartialContent && existing > 0;
                if (existing > 0 && !resumed)
                {
                    existing = 0;
                    if (File.Exists(PartialPath)) File.Delete(PartialPath);
                }
                long total = remote.Size > 0 ? remote.Size : existing + Math.Max(0, response.ContentLength);
                FileMode mode = resumed ? FileMode.Append : FileMode.Create;
                byte[] buffer = new byte[64 * 1024];
                long received = existing;
                DateTime sampleTime = DateTime.UtcNow;
                long sampleBytes = received;
                using (Stream input = response.GetResponseStream())
                using (FileStream output = new FileStream(PartialPath, mode, FileAccess.Write, FileShare.Read))
                {
                    while (true)
                    {
                        token.ThrowIfCancellationRequested();
                        int read = await input.ReadAsync(buffer, 0, buffer.Length, token);
                        if (read <= 0) break;
                        await output.WriteAsync(buffer, 0, read, token);
                        received += read;
                        DateTime now = DateTime.UtcNow;
                        double seconds = (now - sampleTime).TotalSeconds;
                        if (seconds >= 0.25)
                        {
                            RaiseProgress(received, total, (received - sampleBytes) / seconds);
                            sampleTime = now;
                            sampleBytes = received;
                        }
                    }
                    await output.FlushAsync(token);
                }
                RaiseProgress(received, total, 0);
                if (total > 0 && received != total)
                    throw new IOException("download ended before the expected file size");
            }

            AuthorReferenceLocalInfo validated;
            Dictionary<string, List<AuthorReferenceIdentity>> loadedIdentities = null;
            Dictionary<string, List<string>> index = _isNameMapping
                ? LoadIndex(PartialPath, out validated, out loadedIdentities)
                : ValidateGzipSqlite(PartialPath, out validated);
            if (index == null || validated == null || (_isNameMapping && validated.ArtistCount <= 0))
                throw new InvalidDataException("author reference database validation failed");

            string backup = _path + ".bak";
            if (File.Exists(_path)) File.Replace(PartialPath, _path, backup, true);
            else File.Move(PartialPath, _path);
            File.SetLastWriteTimeUtc(_path, remote.UpdatedUtc > DateTime.MinValue ? remote.UpdatedUtc : DateTime.UtcNow);
            if (!_isNameMapping) EnsureExpandedSqlite();
            lock (_gate)
            {
                _artistByName = index;
                if (_isNameMapping) _identityByName = loadedIdentities;
                validated.Exists = true;
                validated.Size = new FileInfo(_path).Length;
                validated.UpdatedUtc = File.GetLastWriteTimeUtc(_path);
                _localInfo = validated;
            }
            return AuthorReferenceDownloadResult.Completed;
        }

        private void EnsureLoadedUnlocked()
        {
            if (_artistByName != null && _localInfo != null) return;
            if (!File.Exists(_path))
            {
                _artistByName = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
                _identityByName = new Dictionary<string, List<AuthorReferenceIdentity>>(StringComparer.OrdinalIgnoreCase);
                _localInfo = new AuthorReferenceLocalInfo();
                return;
            }
            try
            {
                Dictionary<string, List<AuthorReferenceIdentity>> loadedIdentities = null;
                _artistByName = _isNameMapping
                    ? LoadIndex(_path, out _localInfo, out loadedIdentities)
                    : ValidateGzipSqlite(_path, out _localInfo);
                if (_isNameMapping) _identityByName = loadedIdentities;
            }
            catch
            {
                _artistByName = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
                _localInfo = new AuthorReferenceLocalInfo();
            }
        }

        private static Dictionary<string, List<string>> LoadIndex(
            string path,
            out AuthorReferenceLocalInfo info,
            out Dictionary<string, List<AuthorReferenceIdentity>> loadedIdentities)
        {
            string json;
            using (FileStream file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (GZipStream gzip = new GZipStream(file, CompressionMode.Decompress))
            using (StreamReader reader = new StreamReader(gzip, Encoding.UTF8)) json = reader.ReadToEnd();
            JavaScriptSerializer serializer = new JavaScriptSerializer { MaxJsonLength = Int32.MaxValue, RecursionLimit = 256 };
            Dictionary<string, object> root = serializer.DeserializeObject(json) as Dictionary<string, object>;
            Dictionary<string, List<string>> index = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            int count = 0;
            object dataValue;
            Dictionary<string, List<AuthorReferenceIdentity>> identities = new Dictionary<string, List<AuthorReferenceIdentity>>(StringComparer.OrdinalIgnoreCase);
            object[] sections = root != null && root.TryGetValue("data", out dataValue) ? dataValue as object[] : null;
            foreach (object sectionValue in sections ?? new object[0])
            {
                Dictionary<string, object> section = sectionValue as Dictionary<string, object>;
                object nsValue;
                if (section == null || !section.TryGetValue("namespace", out nsValue)) continue;
                string identityNamespace = Convert.ToString(nsValue);
                if (!String.Equals(identityNamespace, "artist", StringComparison.OrdinalIgnoreCase) &&
                    !String.Equals(identityNamespace, "group", StringComparison.OrdinalIgnoreCase)) continue;
                object artistsValue;
                Dictionary<string, object> artists = section.TryGetValue("data", out artistsValue) ? artistsValue as Dictionary<string, object> : null;
                foreach (KeyValuePair<string, object> pair in artists ?? new Dictionary<string, object>())
                {
                    Dictionary<string, object> value = pair.Value as Dictionary<string, object>;
                    object displayValue;
                    string display = value != null && value.TryGetValue("name", out displayValue) ? Convert.ToString(displayValue) : "";
                    if (String.Equals(identityNamespace, "artist", StringComparison.OrdinalIgnoreCase))
                    {
                        Add(index, pair.Key, pair.Key);
                        Add(index, display, pair.Key);
                    }
                    AddIdentity(identities, pair.Key, identityNamespace, pair.Key, display);
                    AddIdentity(identities, display, identityNamespace, pair.Key, display);
                    count++;
                }
            }
            string revision = "";
            if (root != null)
            {
                object headValue;
                Dictionary<string, object> head = root.TryGetValue("head", out headValue) ? headValue as Dictionary<string, object> : null;
                object shaValue;
                if (head != null && head.TryGetValue("sha", out shaValue)) revision = Convert.ToString(shaValue);
            }
            info = new AuthorReferenceLocalInfo
            {
                Exists = true,
                Size = new FileInfo(path).Length,
                UpdatedUtc = File.GetLastWriteTimeUtc(path),
                Revision = revision,
                ArtistCount = count
            };
            loadedIdentities = identities;
            return index;
        }

        private static void AddIdentity(Dictionary<string, List<AuthorReferenceIdentity>> index, string name, string identityNamespace, string tag, string display)
        {
            string key = AuthorRules.NormalizeText(name ?? "");
            if (key.Length == 0 || String.IsNullOrWhiteSpace(tag)) return;
            List<AuthorReferenceIdentity> values;
            if (!index.TryGetValue(key, out values)) { values = new List<AuthorReferenceIdentity>(); index[key] = values; }
            values.Add(new AuthorReferenceIdentity { Namespace = identityNamespace ?? "", Tag = tag ?? "", DisplayName = display ?? "" });
        }

        private static Dictionary<string, List<string>> ValidateGzipSqlite(string path, out AuthorReferenceLocalInfo info)
        {
            byte[] header = new byte[16];
            using (FileStream file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (GZipStream gzip = new GZipStream(file, CompressionMode.Decompress))
            {
                int offset = 0;
                while (offset < header.Length)
                {
                    int read = gzip.Read(header, offset, header.Length - offset);
                    if (read <= 0) break;
                    offset += read;
                }
            }
            string signature = Encoding.ASCII.GetString(header);
            if (!String.Equals(signature, "SQLite format 3\0", StringComparison.Ordinal))
                throw new InvalidDataException("invalid SQLite reference database");
            info = new AuthorReferenceLocalInfo
            {
                Exists = true,
                Size = new FileInfo(path).Length,
                UpdatedUtc = File.GetLastWriteTimeUtc(path),
                Revision = "SQLite",
                ArtistCount = 0
            };
            return new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        }

        private void EnsureExpandedSqlite()
        {
            string target = ExpandedSqlitePath;
            if (File.Exists(target) && File.GetLastWriteTimeUtc(target) >= File.GetLastWriteTimeUtc(_path)) return;
            string temporary = target + ".part";
            using (FileStream input = new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (GZipStream gzip = new GZipStream(input, CompressionMode.Decompress))
            using (FileStream output = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
                gzip.CopyTo(output);
            if (File.Exists(target)) File.Replace(temporary, target, target + ".bak", true);
            else File.Move(temporary, target);
            File.SetLastWriteTimeUtc(target, File.GetLastWriteTimeUtc(_path));
        }

        [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode)]
        private static extern int sqlite3_open16(string filename, out IntPtr db);
        [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
        private static extern int sqlite3_prepare_v2(IntPtr db, string sql, int bytes, out IntPtr statement, IntPtr tail);
        [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern int sqlite3_bind_text(IntPtr statement, int index, byte[] value, int bytes, IntPtr destructor);
        [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern int sqlite3_step(IntPtr statement);
        [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern int sqlite3_column_int(IntPtr statement, int column);
        [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern int sqlite3_finalize(IntPtr statement);
        [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern int sqlite3_close(IntPtr db);

        private static void Add(Dictionary<string, List<string>> index, string name, string tag)
        {
            string key = AuthorRules.NormalizeText(name ?? "");
            if (key.Length == 0 || String.IsNullOrWhiteSpace(tag)) return;
            List<string> values;
            if (!index.TryGetValue(key, out values)) { values = new List<string>(); index[key] = values; }
            if (!values.Contains(tag)) values.Add(tag);
        }

        private void RaiseProgress(long received, long total, double speed)
        {
            Action<AuthorReferenceDownloadProgress> handler = ProgressChanged;
            if (handler != null) handler(new AuthorReferenceDownloadProgress { Received = received, Total = total, BytesPerSecond = speed });
        }

        private static HttpWebRequest CreateRequest(string url)
        {
            HttpWebRequest request = (HttpWebRequest)WebRequest.Create(url);
            request.Method = "GET";
            request.UserAgent = "GuiGui/" + AppVersion.UserAgentVersion + " (author reference database updater)";
            request.Accept = "application/octet-stream";
            request.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;
            request.AllowAutoRedirect = true;
            request.Timeout = 20000;
            request.ReadWriteTimeout = 20000;
            try { ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12; } catch { }
            return request;
        }

        private static long ParseTotalLength(string contentRange)
        {
            if (String.IsNullOrWhiteSpace(contentRange)) return 0;
            int slash = contentRange.LastIndexOf('/');
            long value;
            return slash >= 0 && Int64.TryParse(contentRange.Substring(slash + 1), out value) ? value : 0;
        }
    }
}
