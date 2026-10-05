using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace MangaAuthorSorter
{
    internal sealed class AliasLibrary
    {
        private readonly string _path;
        private readonly Func<string, string> _translate;

        public AliasLibrary(string path, Func<string, string> translate)
        {
            _path = path;
            _translate = translate;
        }

        public string PathName { get { return _path; } }

        public void EnsureExists()
        {
            if (!File.Exists(_path))
                Save(new List<AliasGroup>());
        }

        public List<AliasGroup> Load()
        {
            List<AliasGroup> groups = new List<AliasGroup>();
            if (!File.Exists(_path)) return groups;

            foreach (string line in File.ReadAllLines(_path, Encoding.UTF8))
            {
                string s = (line ?? "").Trim();
                if (s.Length == 0 || s.StartsWith("#")) continue;

                string[] raw = s.Split('|');
                List<string> parts = new List<string>();
                foreach (string x in raw)
                {
                    string p = (x ?? "").Trim();
                    if (p.Length > 0) parts.Add(p);
                }
                if (parts.Count < 2) continue;

                AliasGroup g = new AliasGroup();
                g.Canonical = parts[0];
                foreach (string p in parts)
                {
                    if (!ContainsNormalized(g.Names, p)) g.Names.Add(p);
                    foreach (string n in AuthorRules.GetLiteralNorms(p)) g.Norms.Add(n);
                }
                groups.Add(g);
            }
            return groups;
        }

        public void Save(List<AliasGroup> groups)
        {
            List<string> lines = GetHeaderLines();
            if (lines.Count == 0)
                lines.AddRange(CreateLocalizedHeader());

            foreach (AliasGroup g in groups)
            {
                List<string> ordered = new List<string>();
                if (!String.IsNullOrWhiteSpace(g.Canonical)) ordered.Add(g.Canonical.Trim());
                foreach (string n in g.Names)
                {
                    if (String.IsNullOrWhiteSpace(n)) continue;
                    if (!ContainsNormalized(ordered, n)) ordered.Add(n.Trim());
                }
                if (ordered.Count > 0) lines.Add(String.Join("|", ordered.ToArray()));
            }
            File.WriteAllLines(_path, lines.ToArray(), new UTF8Encoding(true));
        }

        private List<string> GetHeaderLines()
        {
            List<string> header = new List<string>();
            if (!File.Exists(_path)) return header;

            try
            {
                foreach (string raw in File.ReadAllLines(_path, Encoding.UTF8))
                {
                    string line = raw ?? "";
                    string trimmed = line.Trim();
                    if (trimmed.Length == 0 || trimmed.StartsWith("#"))
                    {
                        header.Add(line);
                        continue;
                    }
                    break;
                }
            }
            catch
            {
                header.Clear();
            }

            while (header.Count > 0 && String.IsNullOrWhiteSpace(header[header.Count - 1]))
                header.RemoveAt(header.Count - 1);
            if (header.Count > 0) header.Add("");
            return header;
        }

        private List<string> CreateLocalizedHeader()
        {
            List<string> lines = new List<string>();
            lines.Add("# " + T("DataFile.AuthorAliases.Title"));
            lines.Add("# " + T("DataFile.AuthorAliases.Format"));
            lines.Add("# " + T("DataFile.AuthorAliases.SameAuthor"));
            lines.Add("# " + T("DataFile.AuthorAliases.AutoUpdate"));
            lines.Add("");
            return lines;
        }

        private string T(string key)
        {
            if (_translate == null) return key;
            string value = _translate(key);
            return String.IsNullOrWhiteSpace(value) ? key : value;
        }

        public AliasGroup GetGroupForName(string name, List<AliasGroup> groups, out bool ambiguous)
        {
            ambiguous = false;
            if (String.IsNullOrWhiteSpace(name)) return null;

            HashSet<string> norms = new HashSet<string>(AuthorRules.GetLiteralNorms(name), StringComparer.OrdinalIgnoreCase);
            List<AliasGroup> hits = new List<AliasGroup>();
            foreach (AliasGroup g in groups)
            {
                bool found = false;
                foreach (string n in g.Norms)
                {
                    if (norms.Contains(n)) { found = true; break; }
                }
                if (found) hits.Add(g);
            }
            if (hits.Count == 1) return hits[0];
            if (hits.Count > 1) ambiguous = true;
            return null;
        }

        public void MergeAliasGroup(string canonical, IEnumerable<string> names)
        {
            if (String.IsNullOrWhiteSpace(canonical)) return;
            canonical = canonical.Trim();

            List<string> incoming = new List<string>();
            incoming.Add(canonical);
            if (names != null)
                foreach (string n in names)
                    if (!String.IsNullOrWhiteSpace(n)) incoming.Add(n.Trim());
            incoming = Dedup(incoming);

            HashSet<string> incomingNorms = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string n in incoming)
                foreach (string norm in AuthorRules.GetLiteralNorms(n)) incomingNorms.Add(norm);

            List<AliasGroup> kept = new List<AliasGroup>();
            List<string> mergedNames = new List<string>(incoming);
            foreach (AliasGroup g in Load())
            {
                bool hit = false;
                foreach (string norm in g.Norms)
                    if (incomingNorms.Contains(norm)) { hit = true; break; }
                if (hit) mergedNames.AddRange(g.Names);
                else kept.Add(g);
            }

            mergedNames = Dedup(mergedNames);
            AliasGroup merged = new AliasGroup();
            merged.Canonical = canonical;
            merged.Names.Add(canonical);
            foreach (string n in mergedNames)
                if (!String.Equals(AuthorRules.NormalizeText(n), AuthorRules.NormalizeText(canonical), StringComparison.OrdinalIgnoreCase))
                    merged.Names.Add(n);
            foreach (string n in merged.Names)
                foreach (string norm in AuthorRules.GetLiteralNorms(n)) merged.Norms.Add(norm);
            kept.Add(merged);
            Save(kept);
        }

        private static bool ContainsNormalized(List<string> list, string value)
        {
            string key = AuthorRules.NormalizeText(value);
            foreach (string e in list)
                if (String.Equals(AuthorRules.NormalizeText(e), key, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        private static List<string> Dedup(IEnumerable<string> input)
        {
            List<string> result = new List<string>();
            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string value in input)
            {
                if (String.IsNullOrWhiteSpace(value)) continue;
                string t = value.Trim();
                string key = AuthorRules.NormalizeText(t);
                if (key.Length == 0) key = t;
                if (seen.Add(key)) result.Add(t);
            }
            return result;
        }
    }
}
