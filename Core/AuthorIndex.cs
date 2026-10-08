using System;
using System.Collections.Generic;

namespace MangaAuthorSorter
{
    /// <summary>
    /// Scan-owned lookup index. Parallel preparation is read-only; subsequently
    /// the single planning thread may incrementally register new author folders.
    /// </summary>
    internal sealed class AuthorIndex
    {
        private readonly Dictionary<string, List<AuthorFolder>> _direct;
        private readonly Dictionary<string, List<AuthorFolder>> _generic;
        private readonly Dictionary<string, List<AuthorFolder>> _society;
        private readonly Dictionary<string, List<AuthorFolder>> _creator;
        private readonly Dictionary<string, List<AuthorFolder>> _segment;
        private readonly Dictionary<string, List<AuthorFolder>> _cautious;
        private readonly Dictionary<string, List<AliasGroup>> _aliases;
        private readonly Dictionary<string, List<AuthorFolder>> _candidateBigrams;
        private readonly List<AuthorFolder> _folders;

        public readonly AuthorEntityIndex Entities;
        public int FolderCount { get { return _folders.Count; } }
        public readonly int AliasNameCount;

        private AuthorIndex(List<AuthorFolder> folders, List<AliasGroup> aliases, AuthorEntityIndex entities)
        {
            _direct = NewFolderMap();
            _generic = NewFolderMap();
            _society = NewFolderMap();
            _creator = NewFolderMap();
            _segment = NewFolderMap();
            _cautious = NewFolderMap();
            _aliases = new Dictionary<string, List<AliasGroup>>(StringComparer.OrdinalIgnoreCase);
            _candidateBigrams = NewFolderMap();
            _folders = new List<AuthorFolder>();
            Entities = entities;

            foreach (AuthorFolder folder in folders ?? new List<AuthorFolder>())
            {
                if (folder == null) continue;
                AddPlannedFolder(folder);
            }

            foreach (AliasGroup group in aliases ?? new List<AliasGroup>())
            {
                if (group == null) continue;
                foreach (string norm in group.Norms ?? new HashSet<string>())
                {
                    if (String.IsNullOrWhiteSpace(norm)) continue;
                    List<AliasGroup> list;
                    if (!_aliases.TryGetValue(norm, out list))
                    {
                        list = new List<AliasGroup>();
                        _aliases[norm] = list;
                        AliasNameCount++;
                    }
                    if (!list.Contains(group)) list.Add(group);
                }
            }
        }

        // Only used by the owning scan thread after parallel preparation has
        // completed. This is deliberately NOT a public concurrent mutation API.
        // The same name/path buckets and insertion order as a full rebuild are
        // preserved, including overlapping identities which must remain ambiguous.
        internal void AddPlannedFolder(AuthorFolder folder)
        {
            if (folder == null) return;
            _folders.Add(folder);
            AddFolder(_direct, folder.DirectNorms, folder);
            AddFolder(_generic, folder.Norms, folder);
            AddFolder(_society, folder.SocietyNorms, folder);
            AddFolder(_creator, folder.CreatorNorms, folder);
            AddFolder(_segment, folder.SegmentNorms, folder);
            AddFolder(_cautious, folder.CautiousNorms, folder);
            IndexCandidateName(folder.AuthorName, folder);
            IndexCandidateName(folder.PreferredIdentity, folder);
        }

        public static AuthorIndex Build(List<AuthorFolder> folders, List<AliasGroup> aliases, AuthorEntityIndex entities)
        {
            return new AuthorIndex(folders, aliases, entities);
        }

        public AliasGroup ResolveAlias(string name, out bool ambiguous)
        {
            ambiguous = false;
            HashSet<AliasGroup> hits = new HashSet<AliasGroup>();
            foreach (string norm in AuthorRules.GetLiteralNorms(name ?? ""))
            {
                List<AliasGroup> list;
                if (_aliases.TryGetValue(norm, out list)) foreach (AliasGroup group in list) hits.Add(group);
            }
            if (hits.Count == 1) foreach (AliasGroup group in hits) return group;
            ambiguous = hits.Count > 1;
            return null;
        }

        public List<AuthorFolder> FindDirect(string candidate)
        {
            HashSet<string> norms = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            string identity = AuthorRules.GetDirectMatchIdentity(candidate ?? "");
            AddValue(norms, AuthorRules.NormalizeText(candidate ?? ""));
            AddValue(norms, AuthorRules.NormalizeText(identity));
            AddValue(norms, AuthorRules.NormalizeTerminalPunctuation(identity));
            AddValue(norms, AuthorRules.NormalizeCjkIdentityWhitespace(identity));
            return Lookup(_direct, norms);
        }

        public List<AuthorFolder> FindExactDirect(string norm) { return Lookup(_direct, new string[] { norm }); }
        public List<AuthorFolder> FindGeneric(string candidate) { return Lookup(_generic, AuthorRules.GetNorms(candidate ?? "")); }
        public List<AuthorFolder> FindCautious(string candidate) { return Lookup(_cautious, AuthorRules.GetCautiousNorms(candidate ?? "")); }

        public List<AuthorFolder> FindRole(string candidate, int role)
        {
            Dictionary<string, List<AuthorFolder>> map = role == 1 ? _society : role == 2 ? _creator : _segment;
            return Lookup(map, AuthorRules.GetNorms(candidate ?? ""));
        }

        public List<AuthorFolder> FindCandidateFolders(string fileName)
        {
            string normalized = AuthorRules.NormalizeText(fileName ?? "");
            if (normalized.Length < 2) return new List<AuthorFolder>(_folders);
            HashSet<string> grams = GetBigrams(normalized);
            List<AuthorFolder> hits = Lookup(_candidateBigrams, grams);
            return hits.Count > 0 ? hits : new List<AuthorFolder>();
        }

        private void IndexCandidateName(string name, AuthorFolder folder)
        {
            string normalized = AuthorRules.NormalizeText(name ?? "");
            foreach (string gram in GetBigrams(normalized))
                AddFolder(_candidateBigrams, new string[] { gram }, folder);
        }

        private static HashSet<string> GetBigrams(string value)
        {
            HashSet<string> result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            string compact = (value ?? "").Replace(" ", "");
            for (int i = 0; i + 1 < compact.Length; i++) result.Add(compact.Substring(i, 2));
            return result;
        }

        private static Dictionary<string, List<AuthorFolder>> NewFolderMap()
        {
            return new Dictionary<string, List<AuthorFolder>>(StringComparer.OrdinalIgnoreCase);
        }

        private static void AddFolder(Dictionary<string, List<AuthorFolder>> map, IEnumerable<string> norms, AuthorFolder folder)
        {
            if (norms == null) return;
            foreach (string norm in norms)
            {
                if (String.IsNullOrWhiteSpace(norm)) continue;
                List<AuthorFolder> list;
                if (!map.TryGetValue(norm, out list)) { list = new List<AuthorFolder>(); map[norm] = list; }
                bool exists = false;
                foreach (AuthorFolder current in list)
                    if (String.Equals(current.AuthorPath, folder.AuthorPath, StringComparison.OrdinalIgnoreCase)) { exists = true; break; }
                if (!exists) list.Add(folder);
            }
        }

        private static List<AuthorFolder> Lookup(Dictionary<string, List<AuthorFolder>> map, IEnumerable<string> norms)
        {
            List<AuthorFolder> result = new List<AuthorFolder>();
            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (norms == null) return result;
            foreach (string norm in norms)
            {
                if (String.IsNullOrWhiteSpace(norm)) continue;
                List<AuthorFolder> list;
                if (!map.TryGetValue(norm, out list)) continue;
                foreach (AuthorFolder folder in list)
                    if (seen.Add(folder.AuthorPath ?? "")) result.Add(folder);
            }
            return result;
        }

        private static void AddValue(HashSet<string> values, string value)
        {
            if (!String.IsNullOrWhiteSpace(value)) values.Add(value);
        }
    }
}
