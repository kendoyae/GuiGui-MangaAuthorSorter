using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace MangaAuthorSorter
{
    // Import-only compatibility for pre-unification files. No TXT writer exists.
    internal static class LegacyAliasImporter
    {
        public static List<AliasGroup> Read(string path)
        {
            List<AliasGroup> groups = new List<AliasGroup>();
            if (!File.Exists(path)) return groups;
            foreach (string line in File.ReadAllLines(path, Encoding.UTF8))
            {
                string value = (line ?? "").Trim();
                if (value.Length == 0 || value.StartsWith("#")) continue;
                List<string> names = new List<string>();
                int rawNameCount = 0;
                HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (string part in value.Split('|'))
                    if (!String.IsNullOrWhiteSpace(part)) { rawNameCount++; if (seen.Add(AuthorRules.NormalizeText(part))) names.Add(part.Trim()); }
                if (rawNameCount < 2) continue;
                AliasGroup group = new AliasGroup { Canonical = names[0] };
                group.Names.AddRange(names);
                foreach (string name in names)
                    foreach (string norm in AuthorRules.GetLiteralNorms(name)) group.Norms.Add(norm);
                groups.Add(group);
            }
            return groups;
        }
        public static void Import(string path, AuthorEntityStore store)
        {
            store.MigrateLegacyAliases(path, Read(path));
        }
    }
}
