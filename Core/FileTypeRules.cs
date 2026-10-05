using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace MangaAuthorSorter
{
    internal static class FileTypeRules
    {
        public const string DefaultArchiveProfileId =
            "archive";

        public const string DefaultVideoProfileId =
            "video";

        private static readonly string[] CompressionDefaults =
            new string[]
            {
                "zip",
                "rar",
                "7z"
            };

        private static readonly string[] VideoDefaults =
            new string[]
            {
                "mp4",
                "mkv",
                "avi",
                "mov",
                "wmv",
                "webm",
                "m4v",
                "flv",
                "mpg",
                "mpeg",
                "ts",
                "mts",
                "m2ts",
                "rm",
                "rmvb",
                "3gp",
                "3g2",
                "vob",
                "ogv",
                "mxf"
            };

        public static List<string> GetCompressionDefaults()
        {
            return new List<string>(
                CompressionDefaults);
        }

        public static List<string> GetVideoDefaults()
        {
            return new List<string>(
                VideoDefaults);
        }

        public static bool IsDefaultVideoExtension(
            string extension)
        {
            string normalized;
            string error;

            if (!TryNormalizeExtension(
                    extension,
                    out normalized,
                    out error))
            {
                return false;
            }

            return VideoDefaults.Contains(
                normalized,
                StringComparer.OrdinalIgnoreCase);
        }

        public static List<string> GetDefaultsForProfileId(
            string profileId)
        {
            if (String.Equals(
                    profileId,
                    DefaultVideoProfileId,
                    StringComparison.OrdinalIgnoreCase))
            {
                return GetVideoDefaults();
            }

            if (String.Equals(
                    profileId,
                    DefaultArchiveProfileId,
                    StringComparison.OrdinalIgnoreCase))
            {
                return GetCompressionDefaults();
            }

            return new List<string>();
        }

        public static bool TryNormalizeExtension(
            string raw,
            out string extension,
            out string error)
        {
            extension = "";
            error = "";

            string value =
                (raw ?? "").Trim();

            while (value.StartsWith(
                       "*.",
                       StringComparison.Ordinal))
            {
                value =
                    value.Substring(2);
            }

            while (value.StartsWith(
                       ".",
                       StringComparison.Ordinal))
            {
                value =
                    value.Substring(1);
            }

            value =
                value.Trim()
                    .ToLowerInvariant();

            if (value.Length == 0)
            {
                error =
                    "扩展名不能为空。";
                return false;
            }

            if (value.Length > 20)
            {
                error =
                    "扩展名过长。";
                return false;
            }

            if (!Regex.IsMatch(
                    value,
                    @"^[a-z0-9][a-z0-9_+\-]*$",
                    RegexOptions.CultureInvariant))
            {
                error =
                    "扩展名只能包含英文字母、数字、下划线、加号或减号，例如：mp4、mkv、cbz。";
                return false;
            }

            extension = value;
            return true;
        }

        public static List<string> NormalizeExtensions(
            IEnumerable<string> values)
        {
            List<string> result =
                new List<string>();

            if (values == null)
                return result;

            foreach (string raw in values)
            {
                string ext;
                string error;

                if (!TryNormalizeExtension(
                        raw,
                        out ext,
                        out error))
                {
                    continue;
                }

                if (!result.Contains(
                        ext,
                        StringComparer.OrdinalIgnoreCase))
                {
                    result.Add(ext);
                }
            }

            result.Sort(
                StringComparer.OrdinalIgnoreCase);

            return result;
        }

        public static bool ContainsExtension(
            IEnumerable<string> extensions,
            string pathOrExtension)
        {
            string raw =
                pathOrExtension ?? "";

            int dot =
                raw.LastIndexOf('.');

            if (dot >= 0 &&
                dot < raw.Length - 1)
            {
                raw =
                    raw.Substring(dot + 1);
            }

            string normalized;
            string error;

            if (!TryNormalizeExtension(
                    raw,
                    out normalized,
                    out error))
            {
                return false;
            }

            return extensions != null &&
                extensions.Any(
                    delegate(string x)
                    {
                        return String.Equals(
                            x,
                            normalized,
                            StringComparison.OrdinalIgnoreCase);
                    });
        }

        public static string BuildEverythingExtQuery(
            IEnumerable<string> extensions)
        {
            List<string> normalized =
                NormalizeExtensions(
                    extensions);

            if (normalized.Count == 0)
                return "";

            return "ext:" +
                String.Join(
                    ";",
                    normalized.ToArray());
        }

        public static string FormatSummary(
            IEnumerable<string> extensions,
            int maxShown)
        {
            List<string> normalized =
                NormalizeExtensions(
                    extensions);

            if (normalized.Count == 0)
                return "未设置";

            int take =
                Math.Max(
                    1,
                    maxShown);

            List<string> shown =
                normalized.Take(
                        take)
                    .Select(
                        delegate(string x)
                        {
                            return "." + x;
                        })
                    .ToList();

            string text =
                String.Join(
                    ", ",
                    shown.ToArray());

            if (normalized.Count > take)
            {
                text +=
                    " … (共 " +
                    normalized.Count.ToString() +
                    " 种)";
            }

            return text;
        }
    }
}
