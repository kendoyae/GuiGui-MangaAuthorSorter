using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace MangaAuthorSorter
{
    internal static class AuthorFolderNaming
    {
        public const string Token = "{author}";
        public const string DefaultTemplate = "{author}";

        public static bool TryBuildTemplate(
            string prefix,
            string suffix,
            out string template,
            out string error)
        {
            template = (prefix ?? "") + Token + (suffix ?? "");
            return TryValidateTemplate(template, out template, out error);
        }

        public static bool TryDecomposeTemplate(
            string template,
            out string prefix,
            out string suffix)
        {
            prefix = "";
            suffix = "";

            string normalized;
            string error;
            if (!TryValidateTemplate(template, out normalized, out error))
                return false;

            int index = normalized.IndexOf(Token, StringComparison.Ordinal);
            if (index < 0)
                return false;

            prefix = normalized.Substring(0, index);
            suffix = normalized.Substring(index + Token.Length);
            return true;
        }

        public static bool TryValidateTemplate(
            string template,
            out string normalized,
            out string error)
        {
            normalized = template ?? "";
            error = "";

            int first = normalized.IndexOf(Token, StringComparison.Ordinal);
            if (first < 0)
            {
                error = "AuthorFolderNaming.RequireToken";
                return false;
            }

            if (normalized.IndexOf(Token, first + Token.Length, StringComparison.Ordinal) >= 0)
            {
                error = "AuthorFolderNaming.OneToken";
                return false;
            }

            string sample = normalized.Replace(Token, "Author");
            if (String.IsNullOrWhiteSpace(sample))
            {
                error = "AuthorFolderNaming.Invalid";
                return false;
            }

            if (sample.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            {
                error = "AuthorFolderNaming.InvalidChars";
                return false;
            }

            if (sample.EndsWith(".", StringComparison.Ordinal) ||
                sample.EndsWith(" ", StringComparison.Ordinal))
            {
                error = "AuthorFolderNaming.EndDotSpace";
                return false;
            }

            if (sample.Length > 255)
            {
                error = "AuthorFolderNaming.TooLong";
                return false;
            }

            if (IsReservedWindowsName(sample))
            {
                error = "AuthorFolderNaming.ReservedName";
                return false;
            }

            return true;
        }

        private static bool IsReservedWindowsName(string name)
        {
            string value = (name ?? "").TrimEnd(' ', '.');
            if (value.Length == 0)
                return false;

            int dot = value.IndexOf('.');
            string baseName =
                (dot >= 0 ? value.Substring(0, dot) : value)
                    .TrimEnd(' ')
                    .ToUpperInvariant();

            if (baseName == "CON" ||
                baseName == "PRN" ||
                baseName == "AUX" ||
                baseName == "NUL" ||
                baseName == "CONIN$" ||
                baseName == "CONOUT$")
            {
                return true;
            }

            if (baseName.Length == 4)
            {
                string prefix = baseName.Substring(0, 3);
                char number = baseName[3];

                if ((prefix == "COM" || prefix == "LPT") &&
                    number >= '1' &&
                    number <= '9')
                {
                    return true;
                }
            }

            return false;
        }

        public static string Render(string template, string author)
        {
            string normalized;
            string error;
            if (!TryValidateTemplate(template, out normalized, out error))
                normalized = DefaultTemplate;

            return normalized.Replace(Token, author ?? "");
        }

        public static List<string> NormalizeTemplates(
            string currentTemplate,
            IEnumerable<string> history)
        {
            List<string> result = new List<string>();

            AddTemplate(result, currentTemplate);

            if (history != null)
            {
                foreach (string item in history)
                    AddTemplate(result, item);
            }

            AddTemplate(result, DefaultTemplate);

            return result;
        }

        public static bool TryExtractAuthor(
            string folderName,
            IEnumerable<string> templates,
            out string author)
        {
            author = "";
            string name = folderName ?? "";
            if (name.Length == 0)
                return false;

            List<string> candidates = NormalizeTemplates(DefaultTemplate, templates)
                .OrderByDescending(
                    delegate(string template)
                    {
                        string prefix;
                        string suffix;
                        if (!TryDecomposeTemplate(template, out prefix, out suffix))
                            return -1;
                        return prefix.Length + suffix.Length;
                    })
                .ToList();

            foreach (string template in candidates)
            {
                string prefix;
                string suffix;
                if (!TryDecomposeTemplate(template, out prefix, out suffix))
                    continue;

                if (!name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    continue;

                if (!name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                    continue;

                int length = name.Length - prefix.Length - suffix.Length;
                if (length <= 0)
                    continue;

                string inner = name.Substring(prefix.Length, length).Trim();
                if (inner.Length == 0)
                    continue;

                author = inner;
                return true;
            }

            return false;
        }

        private static void AddTemplate(List<string> list, string template)
        {
            string normalized;
            string error;
            if (!TryValidateTemplate(template, out normalized, out error))
                return;

            if (!list.Any(
                    delegate(string item)
                    {
                        return String.Equals(item, normalized, StringComparison.Ordinal);
                    }))
            {
                list.Add(normalized);
            }
        }
    }
}
