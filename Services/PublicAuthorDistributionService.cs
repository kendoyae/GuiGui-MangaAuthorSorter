using System;
using System.IO;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace MangaAuthorSorter
{
    internal sealed class PublicAuthorReleaseManifest
    {
        public int schemaVersion { get; set; }
        public string revision { get; set; }
        public string url { get; set; }
        public string sha256 { get; set; }
        public long size { get; set; }
    }

    /// <summary>
    /// Official index distribution is optional: the developer publishes an HTTPS manifest
    /// and a Schema v4 SQLite image. A user can instead import an existing local SQLite.
    /// Every installation is staged as a complete public index, never applied over live
    /// SQLite readers, and never changes AuthorEntities.json. Local evidence belongs to
    /// the explicitly selected advanced builder workflow and is not required here.
    /// No .NET 8 or WPF dependencies.
    /// </summary>
    internal sealed class PublicAuthorDistributionService
    {
        public static readonly PublicAuthorDistributionService Current = new PublicAuthorDistributionService();
        private const long MaximumSize = 1024L * 1024 * 1024;
        private static readonly JavaScriptSerializer Json = new JavaScriptSerializer();
        private static string HashFile(string path)
        {
            using (var hasher = SHA256.Create())
            using (var stream = File.OpenRead(path))
                return BitConverter.ToString(hasher.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
        }
        private static bool TrustedDownload(string url)
        {
            Uri parsed;
            if (!Uri.TryCreate(url, UriKind.Absolute, out parsed) || parsed.Scheme != Uri.UriSchemeHttps) return false;
            string host = parsed.Host.ToLowerInvariant();
            return host == "github.com" || host == "objects.githubusercontent.com" ||
                host == "raw.githubusercontent.com" || host == "release-assets.githubusercontent.com" ||
                host == "kendoyae.github.io";
        }
        private static HttpWebRequest Request(string url)
        {
            HttpWebRequest request = (HttpWebRequest)WebRequest.Create(url);
            request.UserAgent = "GuiGui/" + AppVersion.UserAgentVersion;
            request.Timeout = 25000;
            request.ReadWriteTimeout = 30000;
            request.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;
            try { ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12; } catch { }
            return request;
        }
        public Task<PublicAuthorReleaseManifest> CheckAsync()
        {
            return Task.Run(delegate
            {
                ReferenceSourcesConfig sources = AuthorReferenceLibraryService.Current.GetSourcesConfiguration(CancellationToken.None);
                string manifestUrl = sources.publicDatabase == null ? "" : sources.publicDatabase.manifestUrl;
                if (!AuthorReferenceLibraryService.ValidOfficialManifestUrl(manifestUrl))
                    throw new InvalidOperationException("Official database manifest is not published/configured. Set publicDatabase.manifestUrl in SourcesConfig.json first, or import a local .db file.");
                HttpWebRequest req = Request(manifestUrl);
                string json;
                using (HttpWebResponse reply = (HttpWebResponse)req.GetResponse())
                using (Stream stream = reply.GetResponseStream())
                using (StreamReader reader = new StreamReader(stream, Encoding.UTF8))
                {
                    if (!AuthorReferenceLibraryService.ValidOfficialManifestUrl(reply.ResponseUri.AbsoluteUri))
                        throw new InvalidDataException("Official manifest redirected to an untrusted host.");
                    char[] buffer = new char[65537]; int amount = reader.ReadBlock(buffer, 0, buffer.Length);
                    if (amount > 65536) throw new InvalidDataException("Official manifest is too large.");
                    json = new string(buffer, 0, amount);
                }
                PublicAuthorReleaseManifest release = Json.Deserialize<PublicAuthorReleaseManifest>(json);
                if (release == null || release.schemaVersion != 4 || String.IsNullOrWhiteSpace(release.revision) ||
                    release.revision.Length > 128 || release.size < 8192 || release.size > MaximumSize ||
                    !System.Text.RegularExpressions.Regex.IsMatch(release.sha256 ?? "", "^[a-fA-F0-9]{64}$") ||
                    !TrustedDownload(release.url))
                    throw new InvalidDataException("Official index manifest is invalid or unsupported (requires Schema v4, SHA-256 and size).");
                return release;
            });
        }
        public Task InstallAsync(PublicAuthorReleaseManifest release, Action<long, long> progress)
        {
            return Task.Run(delegate
            {
                if (release == null || !TrustedDownload(release.url)) throw new InvalidDataException("Untrusted official index URL.");
                string temp = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "GuiGuiAuthorIndex.db.download");
                try
                {
                    HttpWebRequest req = Request(release.url);
                    using (HttpWebResponse reply = (HttpWebResponse)req.GetResponse())
                    using (Stream input = reply.GetResponseStream())
                    using (Stream output = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
                    {
                        if (!TrustedDownload(reply.ResponseUri.AbsoluteUri)) throw new InvalidDataException("Official database redirected to an untrusted host.");
                        if (reply.ContentLength > release.size) throw new InvalidDataException("Remote size exceeds manifest.");
                        byte[] buf = new byte[131072]; int n; long done = 0;
                        while ((n = input.Read(buf, 0, buf.Length)) > 0)
                        {
                            done += n;
                            if (done > release.size) throw new InvalidDataException("Official database download exceeded manifest size.");
                            output.Write(buf, 0, n);
                            if (progress != null) progress(done, release.size);
                        }
                    }
                    if (new FileInfo(temp).Length != release.size ||
                        !String.Equals(HashFile(temp), release.sha256, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidDataException("Official index SHA-256/size mismatch; nothing installed.");
                    StageLocalDatabase(temp);
                }
                finally { if (File.Exists(temp)) File.Delete(temp); }
            });
        }
        public Task ImportAsync(string dbFile)
        {
            return Task.Run(delegate { StageLocalDatabase(dbFile); });
        }
        private static void StageLocalDatabase(string dbFile)
        {
            PublicAuthorIndexMergeService.StageOfficialIndex(dbFile);
        }
    }
}
