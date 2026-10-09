using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace GuiGui.Shared
{
    // This file is intentionally byte-for-byte identical in GuiGui and AuthorDbBuilder.
    // It is compatible with .NET Framework 4.8 and .NET 8, so CSV import and
    // full database construction use the same normalization/filtering contract.
    public static class ReferenceNameRules
    {
        private static readonly Regex Spaces = new Regex(@"\s+", RegexOptions.Compiled);
        private static readonly Regex BracketSpaces = new Regex(@"\s*([\(\)\[\]])\s*", RegexOptions.Compiled);

        public static string Display(string value)
        {
            return Spaces.Replace((value ?? "").Trim(), " ");
        }

        public static string Normalize(string value)
        {
            if (String.IsNullOrWhiteSpace(value)) return "";
            string s = value.Normalize(NormalizationForm.FormKC)
                .Replace("\u200B", "").Replace("\u200C", "").Replace("\u200D", "")
                .Replace("\u2060", "").Replace("\uFEFF", "")
                .Replace('（', '(').Replace('）', ')')
                .Replace('［', '[').Replace('］', ']')
                .Replace('【', '[').Replace('】', ']');
            s = BracketSpaces.Replace(Spaces.Replace(s, " ").Trim(), "$1");
            var open = new Stack<char>();
            bool invalid = false;
            foreach (char c in s)
            {
                if (c == '(' || c == '[') open.Push(c);
                else if (c == ')' || c == ']')
                {
                    if (open.Count == 0) { invalid = true; break; }
                    char from = open.Pop();
                    if ((from == '(' && c != ')') || (from == '[' && c != ']')) { invalid = true; break; }
                }
            }
            if (!invalid && open.Count == 1) s += open.Pop() == '(' ? ")" : "]";
            return s.Trim().ToLowerInvariant();
        }

        public static string PrettyTag(string value) { return Display((value ?? "").Replace('_', ' ')); }
        public static bool IsUseful(string value)
        {
            return !String.IsNullOrWhiteSpace(value) && value.Length <= 300 &&
                value != "[]" && !String.Equals(value, "null", StringComparison.OrdinalIgnoreCase) &&
                !String.Equals(value, "N/A", StringComparison.OrdinalIgnoreCase);
        }
        public static IReadOnlyCollection<string> Split(string value)
        {
            if (String.IsNullOrWhiteSpace(value)) return new string[0];
            return value.Split(new[] { ',', ';', '|', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(Display).Where(IsUseful).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        }

        // RFC4180 CSV/TSV, including embedded newlines and escaped quotation marks.
        public static IEnumerable<string[]> ReadDelimited(System.IO.TextReader input, char separator)
        {
            var row = new List<string>(); var field = new StringBuilder();
            bool quoted = false; bool started = false; int ch;
            while ((ch = input.Read()) != -1)
            {
                char c = (char)ch;
                if (quoted)
                {
                    if (c == '"')
                    {
                        if (input.Peek() == '"') { input.Read(); field.Append('"'); }
                        else quoted = false;
                    }
                    else field.Append(c);
                }
                else if (c == '"' && field.Length == 0) { quoted = true; started = true; }
                else if (c == separator) { row.Add(field.ToString()); field.Clear(); started = true; }
                else if (c == '\n')
                {
                    row.Add(field.ToString()); field.Clear(); started = false;
                    yield return row.ToArray(); row.Clear();
                }
                else if (c != '\r') { field.Append(c); started = true; }
                if (field.Length > 4 * 1024 * 1024) throw new System.IO.InvalidDataException("CSV field too large");
            }
            if (quoted) throw new System.IO.InvalidDataException("Unterminated CSV quote");
            if (started || row.Count != 0 || field.Length != 0) { row.Add(field.ToString()); yield return row.ToArray(); }
        }
    }
}
