using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace MangaAuthorSorter
{
    internal sealed class UpdateService
    {
        public const string RepositoryUrl = "https://github.com/kendoyae/GuiGui-MangaAuthorSorter";
        public const string ReleasesUrl = RepositoryUrl + "/releases";
        public const string LatestReleaseApiUrl = "https://api.github.com/repos/kendoyae/GuiGui-MangaAuthorSorter/releases/latest";

        public Task<UpdateCheckResult> CheckAsync()
        {
            return Task.Factory.StartNew<UpdateCheckResult>(delegate { return Check(); });
        }

        private UpdateCheckResult Check()
        {
            UpdateCheckResult result = new UpdateCheckResult();
            result.CurrentVersion = AppVersion.Current;
            try
            {
                HttpWebRequest request = (HttpWebRequest)WebRequest.Create(LatestReleaseApiUrl);
                request.Method = "GET";
                request.Accept = "application/vnd.github+json";
                request.UserAgent = "GuiGui-MangaAuthorSorter/" + AppVersion.UserAgentVersion;
                request.Timeout = 8000;
                request.ReadWriteTimeout = 8000;

                using (HttpWebResponse response = (HttpWebResponse)request.GetResponse())
                using (Stream stream = response.GetResponseStream())
                using (StreamReader reader = new StreamReader(stream))
                {
                    Dictionary<string, object> json = new JavaScriptSerializer()
                        .Deserialize<Dictionary<string, object>>(reader.ReadToEnd());
                    string tag = GetString(json, "tag_name");
                    Version latest;
                    if (!TryParseVersion(tag, out latest))
                    {
                        result.Status = UpdateCheckStatus.InvalidVersion;
                        return result;
                    }

                    result.LatestVersion = latest;
                    result.ReleaseName = GetString(json, "name");
                    result.ReleaseNotes = GetString(json, "body");
                    result.ReleaseUrl = GetString(json, "html_url");
                    DateTime published;
                    if (DateTime.TryParse(GetString(json, "published_at"), out published))
                        result.PublishedAt = published.ToLocalTime();

                    int comparison = latest.CompareTo(result.CurrentVersion);
                    result.Status = comparison > 0
                        ? UpdateCheckStatus.UpdateAvailable
                        : comparison == 0
                            ? UpdateCheckStatus.UpToDate
                            : UpdateCheckStatus.DevelopmentVersion;
                    return result;
                }
            }
            catch (WebException ex)
            {
                HttpWebResponse response = ex.Response as HttpWebResponse;
                if (response != null && response.StatusCode == HttpStatusCode.NotFound)
                    result.Status = UpdateCheckStatus.NoRelease;
                else if (ex.Status == WebExceptionStatus.Timeout)
                    result.Status = UpdateCheckStatus.Timeout;
                else
                    result.Status = UpdateCheckStatus.NetworkError;
            }
            catch
            {
                result.Status = UpdateCheckStatus.InvalidResponse;
            }
            return result;
        }

        internal static bool TryParseVersion(string text, out Version version)
        {
            string value = (text ?? "").Trim();
            if (value.StartsWith("v", StringComparison.OrdinalIgnoreCase))
                value = value.Substring(1).Trim();
            Version parsed;
            if (!Version.TryParse(value, out parsed))
            {
                version = null;
                return false;
            }
            version = parsed.Revision <= 0
                ? new Version(parsed.Major, parsed.Minor, Math.Max(0, parsed.Build))
                : parsed;
            return true;
        }

        private static string GetString(Dictionary<string, object> json, string key)
        {
            object value;
            return json != null && json.TryGetValue(key, out value) && value != null
                ? Convert.ToString(value)
                : "";
        }
    }
}
