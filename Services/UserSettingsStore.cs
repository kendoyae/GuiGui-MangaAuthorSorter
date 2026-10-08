using System;
using System.Collections.Generic;
using System.IO;
using System.Globalization;
using System.Linq;
using System.Text;

namespace MangaAuthorSorter
{
    internal sealed class UserSettingsData
    {
        public string SourcePath = "";
        public string AuthorRoot = "";

        public string LanguageCode = "";

        public int MaxAuthorsPerGroup = 20;

        public decimal SafetyReserveGb = 5M;

        public string GroupTemplate =
            GroupNaming.DefaultTemplate;

        public List<string> GroupTemplateHistory =
            new List<string>();

        public string AuthorFolderTemplate =
            AuthorFolderNaming.DefaultTemplate;

        public List<string> AuthorFolderTemplateHistory =
            new List<string>();

        public bool OnlineAuthorLookupEnabled = false;
        public string OnlineAuthorProvider = "EvidenceChain";
        public bool SaveOnlineAuthorCache = true;
        public int MaxOnlineLookupsPerScan = 20;
        public bool UseLocalAuthorReference = true;
        public bool UseEhentaiLookup = true;
        public bool UseNhentaiLookup = true;
        public string NhentaiApiKey = "";
        public bool PerformanceDiagnosticsEnabled = false;
        public bool ScanWarmupEnabled = true;
        public bool EverythingEnabled = true;
        public AuthorRecognitionMode RecognitionMode = AuthorRecognitionMode.Classic;
        public DateTime? LastUpdateCheckUtc;

        // 仅用于从 V1.5 / V1.5.2 自动迁移到
        // “FileTypeProfiles.json”。
        public bool HasLegacyFileTypeConfig;
        public string LegacyFileTypeMode =
            "Compression";

        public List<string> LegacyCompressionExtensions =
            FileTypeRules.GetCompressionDefaults();

        public List<string> LegacyVideoExtensions =
            FileTypeRules.GetVideoDefaults();
    }

    internal sealed class UserSettingsStore
    {
        private readonly string _path;

        public UserSettingsStore(
            string path)
        {
            _path = path;
        }

        public UserSettingsData Load()
        {
            UserSettingsData data =
                new UserSettingsData();

            bool hasCompression = false;
            bool hasVideo = false;
            List<string> legacyMixed = null;

            if (File.Exists(_path))
            {
                try
                {
                    foreach (string raw in
                             File.ReadAllLines(
                                 _path,
                                 Encoding.UTF8))
                    {
                        string line =
                            (raw ?? "").Trim();

                        if (line.Length == 0 ||
                            line.StartsWith(
                                "#",
                                StringComparison.Ordinal))
                        {
                            continue;
                        }

                        int equals =
                            line.IndexOf('=');

                        if (equals <= 0)
                            continue;

                        string key =
                            line.Substring(
                                0,
                                equals)
                                .Trim();

                        string decoded =
                            Decode(
                                line.Substring(
                                    equals + 1)
                                    .Trim());

                        if (String.Equals(
                                key,
                                "Source",
                                StringComparison.OrdinalIgnoreCase))
                        {
                            data.SourcePath =
                                decoded;
                        }
                        else if (String.Equals(
                                     key,
                                     "AuthorRoot",
                                     StringComparison.OrdinalIgnoreCase))
                        {
                            data.AuthorRoot =
                                decoded;
                        }
                        else if (String.Equals(
                                     key,
                                     "Language",
                                     StringComparison.OrdinalIgnoreCase))
                        {
                            data.LanguageCode =
                                decoded;
                        }
                        else if (String.Equals(
                                     key,
                                     "PerformanceDiagnosticsEnabled",
                                     StringComparison.OrdinalIgnoreCase))
                        {
                            bool parsedDiagnostics;
                            if (Boolean.TryParse(decoded, out parsedDiagnostics))
                                data.PerformanceDiagnosticsEnabled = parsedDiagnostics;
                        }
                        else if (String.Equals(key, "ScanWarmupEnabled", StringComparison.OrdinalIgnoreCase))
                        {
                            bool parsedWarmup;
                            if (Boolean.TryParse(decoded, out parsedWarmup)) data.ScanWarmupEnabled = parsedWarmup;
                        }
                        else if (String.Equals(key, "EverythingEnabled", StringComparison.OrdinalIgnoreCase))
                        {
                            bool parsedEverything;
                            if (Boolean.TryParse(decoded, out parsedEverything)) data.EverythingEnabled = parsedEverything;
                        }
                        else if (String.Equals(key, "RecognitionMode", StringComparison.OrdinalIgnoreCase))
                        {
                            data.RecognitionMode = String.Equals(decoded, "Scoring", StringComparison.OrdinalIgnoreCase)
                                ? AuthorRecognitionMode.Scoring
                                : AuthorRecognitionMode.Classic;
                        }
                        else if (String.Equals(key, "LastUpdateCheckUtc", StringComparison.OrdinalIgnoreCase))
                        {
                            DateTime parsedCheck;
                            if (DateTime.TryParse(decoded, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out parsedCheck))
                                data.LastUpdateCheckUtc = parsedCheck.ToUniversalTime();
                        }
                        else if (String.Equals(
                                     key,
                                     "MaxAuthorsPerGroup",
                                     StringComparison.OrdinalIgnoreCase))
                        {
                            int parsedMax;
                            if (Int32.TryParse(decoded, out parsedMax) &&
                                parsedMax >= 1 &&
                                parsedMax <= 999)
                            {
                                data.MaxAuthorsPerGroup = parsedMax;
                            }
                        }
                        else if (String.Equals(
                                     key,
                                     "SafetyReserveGb",
                                     StringComparison.OrdinalIgnoreCase))
                        {
                            decimal parsedReserve;
                            if (Decimal.TryParse(
                                    decoded,
                                    NumberStyles.Number,
                                    CultureInfo.InvariantCulture,
                                    out parsedReserve) &&
                                parsedReserve >= 0M &&
                                parsedReserve <= 1024M)
                            {
                                data.SafetyReserveGb = parsedReserve;
                            }
                        }
                        else if (String.Equals(
                                     key,
                                     "GroupTemplate",
                                     StringComparison.OrdinalIgnoreCase))
                        {
                            data.GroupTemplate =
                                decoded;
                        }
                        else if (String.Equals(
                                     key,
                                     "GroupTemplateHistory",
                                     StringComparison.OrdinalIgnoreCase))
                        {
                            data.GroupTemplateHistory =
                                SplitLines(
                                    decoded);
                        }
                        else if (String.Equals(
                                     key,
                                     "AuthorFolderTemplate",
                                     StringComparison.OrdinalIgnoreCase))
                        {
                            data.AuthorFolderTemplate =
                                decoded;
                        }
                        else if (String.Equals(
                                     key,
                                     "AuthorFolderTemplateHistory",
                                     StringComparison.OrdinalIgnoreCase))
                        {
                            data.AuthorFolderTemplateHistory =
                                SplitLines(
                                    decoded);
                        }
                        else if (String.Equals(
                                     key,
                                     "OnlineAuthorLookupEnabled",
                                     StringComparison.OrdinalIgnoreCase))
                        {
                            bool value;
                            if (Boolean.TryParse(decoded, out value))
                                data.OnlineAuthorLookupEnabled = value;
                        }
                        else if (String.Equals(
                                     key,
                                     "OnlineAuthorProvider",
                                     StringComparison.OrdinalIgnoreCase))
                        {
                            // Migrate the removed single-site provider setting.
                            // The resolver now owns source routing as one chain.
                            data.OnlineAuthorProvider = "EvidenceChain";
                        }
                        else if (String.Equals(
                                     key,
                                     "SaveOnlineAuthorCache",
                                     StringComparison.OrdinalIgnoreCase))
                        {
                            bool value;
                            if (Boolean.TryParse(decoded, out value))
                                data.SaveOnlineAuthorCache = value;
                        }
                        else if (String.Equals(
                                     key,
                                     "MaxOnlineLookupsPerScan",
                                     StringComparison.OrdinalIgnoreCase))
                        {
                            int value;
                            if (Int32.TryParse(decoded, out value) && value >= 1 && value <= 100)
                                data.MaxOnlineLookupsPerScan = value;
                        }
                        else if (String.Equals(key, "UseLocalAuthorReference", StringComparison.OrdinalIgnoreCase))
                        {
                            bool value; if (Boolean.TryParse(decoded, out value)) data.UseLocalAuthorReference = value;
                        }
                        else if (String.Equals(key, "UseEhentaiLookup", StringComparison.OrdinalIgnoreCase))
                        {
                            bool value; if (Boolean.TryParse(decoded, out value)) data.UseEhentaiLookup = value;
                        }
                        else if (String.Equals(key, "UseNhentaiLookup", StringComparison.OrdinalIgnoreCase))
                        {
                            bool value; if (Boolean.TryParse(decoded, out value)) data.UseNhentaiLookup = value;
                        }
                        else if (String.Equals(key, "NhentaiApiKey", StringComparison.OrdinalIgnoreCase))
                        {
                            data.NhentaiApiKey = decoded;
                        }
                        else if (String.Equals(
                                     key,
                                     "FileTypeMode",
                                     StringComparison.OrdinalIgnoreCase))
                        {
                            data.LegacyFileTypeMode =
                                String.Equals(
                                    decoded,
                                    "Video",
                                    StringComparison.OrdinalIgnoreCase)
                                    ? "Video"
                                    : "Compression";

                            data.HasLegacyFileTypeConfig =
                                true;
                        }
                        else if (String.Equals(
                                     key,
                                     "CompressionExtensions",
                                     StringComparison.OrdinalIgnoreCase))
                        {
                            data.LegacyCompressionExtensions =
                                ParseExtensions(
                                    decoded);

                            hasCompression = true;
                            data.HasLegacyFileTypeConfig =
                                true;
                        }
                        else if (String.Equals(
                                     key,
                                     "VideoExtensions",
                                     StringComparison.OrdinalIgnoreCase))
                        {
                            data.LegacyVideoExtensions =
                                ParseExtensions(
                                    decoded);

                            hasVideo = true;
                            data.HasLegacyFileTypeConfig =
                                true;
                        }
                        else if (String.Equals(
                                     key,
                                     "Extensions",
                                     StringComparison.OrdinalIgnoreCase))
                        {
                            legacyMixed =
                                ParseExtensions(
                                    decoded);

                            data.HasLegacyFileTypeConfig =
                                true;
                        }
                    }
                }
                catch
                {
                    // 配置损坏不能阻止程序启动。
                }
            }

            string normalizedTemplate;
            string templateError;

            if (!GroupNaming.TryValidateTemplate(
                    data.GroupTemplate,
                    out normalizedTemplate,
                    out templateError))
            {
                normalizedTemplate =
                    GroupNaming.DefaultTemplate;
            }

            data.GroupTemplate =
                normalizedTemplate;

            data.GroupTemplateHistory =
                GroupNaming.NormalizeTemplates(
                    data.GroupTemplate,
                    data.GroupTemplateHistory);

            string normalizedAuthorFolderTemplate;
            string authorFolderTemplateError;

            if (!AuthorFolderNaming.TryValidateTemplate(
                    data.AuthorFolderTemplate,
                    out normalizedAuthorFolderTemplate,
                    out authorFolderTemplateError))
            {
                normalizedAuthorFolderTemplate =
                    AuthorFolderNaming.DefaultTemplate;
            }

            data.AuthorFolderTemplate =
                normalizedAuthorFolderTemplate;

            data.AuthorFolderTemplateHistory =
                AuthorFolderNaming.NormalizeTemplates(
                    data.AuthorFolderTemplate,
                    data.AuthorFolderTemplateHistory);

            // V1.5 的单一 Extensions 兼容：
            // 已知视频后缀迁移进视频模式，其余进入压缩包模式。
            if (legacyMixed != null)
            {
                if (!hasCompression)
                {
                    List<string> compression =
                        legacyMixed.Where(
                            delegate(string ext)
                            {
                                return !FileTypeRules
                                    .IsDefaultVideoExtension(
                                        ext);
                            })
                        .ToList();

                    if (compression.Count > 0)
                    {
                        data.LegacyCompressionExtensions =
                            FileTypeRules.NormalizeExtensions(
                                compression);
                    }
                }

                if (!hasVideo)
                {
                    List<string> video =
                        legacyMixed.Where(
                            delegate(string ext)
                            {
                                return FileTypeRules
                                    .IsDefaultVideoExtension(
                                        ext);
                            })
                        .ToList();

                    if (video.Count > 0)
                    {
                        data.LegacyVideoExtensions =
                            FileTypeRules.NormalizeExtensions(
                                video);
                    }
                }
            }

            data.LegacyCompressionExtensions =
                FileTypeRules.NormalizeExtensions(
                    data.LegacyCompressionExtensions);

            data.LegacyVideoExtensions =
                FileTypeRules.NormalizeExtensions(
                    data.LegacyVideoExtensions);

            if (data.LegacyCompressionExtensions.Count == 0)
            {
                data.LegacyCompressionExtensions =
                    FileTypeRules.GetCompressionDefaults();
            }

            if (data.LegacyVideoExtensions.Count == 0)
            {
                data.LegacyVideoExtensions =
                    FileTypeRules.GetVideoDefaults();
            }

            return data;
        }

        public UserSettingsData UpdateSettings(
            string source,
            string authorRoot,
            string groupTemplate,
            int maxAuthorsPerGroup,
            string authorFolderTemplate)
        {
            UserSettingsData current =
                Load();

            if (!String.IsNullOrWhiteSpace(
                    source) &&
                Directory.Exists(
                    source))
            {
                current.SourcePath =
                    Path.GetFullPath(
                        source.Trim());
            }

            if (!String.IsNullOrWhiteSpace(
                    authorRoot) &&
                Directory.Exists(
                    authorRoot))
            {
                current.AuthorRoot =
                    Path.GetFullPath(
                        authorRoot.Trim());
            }

            if (maxAuthorsPerGroup >= 1 &&
                maxAuthorsPerGroup <= 999)
            {
                current.MaxAuthorsPerGroup =
                    maxAuthorsPerGroup;
            }

            string normalizedTemplate;
            string templateError;

            if (GroupNaming.TryValidateTemplate(
                    groupTemplate,
                    out normalizedTemplate,
                    out templateError))
            {
                List<string> history =
                    new List<string>(
                        current.GroupTemplateHistory ??
                        new List<string>());

                if (!String.IsNullOrWhiteSpace(
                        current.GroupTemplate))
                {
                    history.Add(
                        current.GroupTemplate);
                }

                history.Add(
                    normalizedTemplate);

                current.GroupTemplate =
                    normalizedTemplate;

                current.GroupTemplateHistory =
                    GroupNaming.NormalizeTemplates(
                        current.GroupTemplate,
                        history);
            }

            string normalizedAuthorFolderTemplate;
            string authorFolderTemplateError;

            if (AuthorFolderNaming.TryValidateTemplate(
                    authorFolderTemplate,
                    out normalizedAuthorFolderTemplate,
                    out authorFolderTemplateError))
            {
                List<string> authorHistory =
                    new List<string>(
                        current.AuthorFolderTemplateHistory ??
                        new List<string>());

                if (!String.IsNullOrWhiteSpace(
                        current.AuthorFolderTemplate))
                {
                    authorHistory.Add(
                        current.AuthorFolderTemplate);
                }

                authorHistory.Add(
                    normalizedAuthorFolderTemplate);

                current.AuthorFolderTemplate =
                    normalizedAuthorFolderTemplate;

                current.AuthorFolderTemplateHistory =
                    AuthorFolderNaming.NormalizeTemplates(
                        current.AuthorFolderTemplate,
                        authorHistory);
            }

            Save(current);
            return current;
        }

        public void UpdateOnlineAuthorSettings(
            bool enabled,
            string provider,
            bool saveCache,
            int maxLookupsPerScan,
            bool useLocalReference,
            bool useEhentai,
            bool useNhentai,
            string nhentaiApiKey)
        {
            UserSettingsData current = Load();
            current.OnlineAuthorLookupEnabled = enabled;
            current.OnlineAuthorProvider = String.IsNullOrWhiteSpace(provider)
                ? "EvidenceChain"
                : provider.Trim();
            current.SaveOnlineAuthorCache = saveCache;
            current.MaxOnlineLookupsPerScan = Math.Max(1, Math.Min(100, maxLookupsPerScan));
            current.UseLocalAuthorReference = useLocalReference;
            current.UseEhentaiLookup = useEhentai;
            current.UseNhentaiLookup = useNhentai;
            current.NhentaiApiKey = (nhentaiApiKey ?? "").Trim();
            Save(current);
        }

        public void UpdateSafetyReserve(decimal reserveGb)
        {
            UserSettingsData current =
                Load();

            if (reserveGb < 0M)
                reserveGb = 0M;
            if (reserveGb > 1024M)
                reserveGb = 1024M;

            current.SafetyReserveGb = reserveGb;
            Save(current);
        }

        public void UpdateValidPaths(
            string source,
            string authorRoot)
        {
            UserSettingsData current =
                Load();

            UpdateSettings(
                source,
                authorRoot,
                current.GroupTemplate,
                current.MaxAuthorsPerGroup,
                current.AuthorFolderTemplate);
        }

        public void UpdateLanguage(
            string languageCode)
        {
            UserSettingsData current =
                Load();

            current.LanguageCode =
                (languageCode ?? "").Trim();

            Save(current);
        }

        public void UpdatePerformanceDiagnostics(bool enabled)
        {
            UserSettingsData current = Load();
            current.PerformanceDiagnosticsEnabled = enabled;
            Save(current);
        }

        public void UpdatePerformanceSettings(bool diagnostics, bool warmup, bool everything)
        {
            UserSettingsData current = Load();
            current.PerformanceDiagnosticsEnabled = diagnostics;
            current.ScanWarmupEnabled = warmup;
            current.EverythingEnabled = everything;
            Save(current);
        }

        public void UpdateRecognitionMode(AuthorRecognitionMode mode)
        {
            UserSettingsData current = Load();
            current.RecognitionMode = mode;
            Save(current);
        }

        public void UpdateLastUpdateCheckUtc(DateTime value)
        {
            UserSettingsData current = Load();
            current.LastUpdateCheckUtc = value.ToUniversalTime();
            Save(current);
        }

        private void Save(
            UserSettingsData data)
        {
            try
            {
                List<string> lines =
                    new List<string>();

                lines.Add(
                    "# MangaAuthorSorter - User Settings");
                lines.Add(
                    "# File type profiles are stored separately in FileTypeProfiles.json.");

                lines.Add(
                    "Source=" +
                    Encode(
                        data.SourcePath));

                lines.Add(
                    "AuthorRoot=" +
                    Encode(
                        data.AuthorRoot));

                lines.Add(
                    "Language=" +
                    Encode(
                        data.LanguageCode));

                lines.Add(
                    "MaxAuthorsPerGroup=" +
                    Encode(
                        data.MaxAuthorsPerGroup.ToString()));

                lines.Add(
                    "SafetyReserveGb=" +
                    Encode(
                        data.SafetyReserveGb.ToString(
                            CultureInfo.InvariantCulture)));

                lines.Add(
                    "GroupTemplate=" +
                    Encode(
                        data.GroupTemplate));

                lines.Add(
                    "GroupTemplateHistory=" +
                    Encode(
                        String.Join(
                            "\n",
                            (data.GroupTemplateHistory ??
                             new List<string>())
                                .ToArray())));

                lines.Add(
                    "AuthorFolderTemplate=" +
                    Encode(
                        data.AuthorFolderTemplate));

                lines.Add(
                    "AuthorFolderTemplateHistory=" +
                    Encode(
                        String.Join(
                            "\n",
                            (data.AuthorFolderTemplateHistory ??
                             new List<string>())
                                .ToArray())));

                lines.Add(
                    "OnlineAuthorLookupEnabled=" +
                    Encode(data.OnlineAuthorLookupEnabled.ToString()));

                lines.Add(
                    "OnlineAuthorProvider=" +
                    Encode(data.OnlineAuthorProvider));

                lines.Add(
                    "SaveOnlineAuthorCache=" +
                    Encode(data.SaveOnlineAuthorCache.ToString()));

                lines.Add(
                    "MaxOnlineLookupsPerScan=" +
                    Encode(data.MaxOnlineLookupsPerScan.ToString()));
                lines.Add("UseLocalAuthorReference=" + Encode(data.UseLocalAuthorReference.ToString()));
                lines.Add("UseEhentaiLookup=" + Encode(data.UseEhentaiLookup.ToString()));
                lines.Add("UseNhentaiLookup=" + Encode(data.UseNhentaiLookup.ToString()));
                lines.Add("NhentaiApiKey=" + Encode(data.NhentaiApiKey ?? ""));

                lines.Add(
                    "PerformanceDiagnosticsEnabled=" +
                    Encode(data.PerformanceDiagnosticsEnabled.ToString()));
                lines.Add("ScanWarmupEnabled=" + Encode(data.ScanWarmupEnabled.ToString()));
                lines.Add("EverythingEnabled=" + Encode(data.EverythingEnabled.ToString()));
                lines.Add("RecognitionMode=" + Encode(data.RecognitionMode == AuthorRecognitionMode.Scoring ? "Scoring" : "Classic"));
                lines.Add("LastUpdateCheckUtc=" + Encode(data.LastUpdateCheckUtc.HasValue
                    ? data.LastUpdateCheckUtc.Value.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture)
                    : ""));

                File.WriteAllLines(
                    _path,
                    lines.ToArray(),
                    new UTF8Encoding(true));
            }
            catch
            {
                // 设置记忆失败不影响主功能。
            }
        }

        private static List<string> ParseExtensions(
            string value)
        {
            return FileTypeRules.NormalizeExtensions(
                (value ?? "")
                    .Split(
                        new char[]
                        {
                            ';',
                            ',',
                            '\r',
                            '\n'
                        },
                        StringSplitOptions.RemoveEmptyEntries));
        }

        private static List<string> SplitLines(
            string value)
        {
            if (String.IsNullOrEmpty(
                    value))
            {
                return new List<string>();
            }

            return value.Split(
                    new string[]
                    {
                        "\r\n",
                        "\n",
                        "\r"
                    },
                    StringSplitOptions.RemoveEmptyEntries)
                .Select(
                    delegate(string x)
                    {
                        return x.Trim();
                    })
                .Where(
                    delegate(string x)
                    {
                        return x.Length > 0;
                    })
                .ToList();
        }

        private static string Encode(
            string value)
        {
            if (String.IsNullOrEmpty(
                    value))
            {
                return "";
            }

            return Convert.ToBase64String(
                Encoding.UTF8.GetBytes(
                    value));
        }

        private static string Decode(
            string value)
        {
            if (String.IsNullOrWhiteSpace(
                    value))
            {
                return "";
            }

            try
            {
                return Encoding.UTF8.GetString(
                    Convert.FromBase64String(
                        value));
            }
            catch
            {
                return "";
            }
        }
    }
}
