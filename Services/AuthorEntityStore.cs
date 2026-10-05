using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Web.Script.Serialization;

namespace MangaAuthorSorter
{
    internal sealed class AuthorEntityDatabase
    {
        public int Version = 1;
        public List<AuthorEntityRecord> Authors = new List<AuthorEntityRecord>();
        public List<AuthorAliasRecord> Aliases = new List<AuthorAliasRecord>();
        public List<CircleEntityRecord> Circles = new List<CircleEntityRecord>();
        public List<AuthorCircleRecord> AuthorCircles = new List<AuthorCircleRecord>();
        public List<AuthorLookupCacheRecord> LookupCache = new List<AuthorLookupCacheRecord>();
    }

    internal sealed class AuthorEntityRecord
    {
        public int Id;
        public string CanonicalName = "";
        public string RomanName = "";
        public string EHArtistTag = "";
        public string DanbooruArtistTag = "";
        public string Source = "";
        public string ExternalId = "";
        public bool Verified;
        public string UpdatedUtc = "";
    }

    internal sealed class AuthorAliasRecord
    {
        public int AuthorId;
        public string Alias = "";
        public string AliasType = "";
        public string Source = "";
    }

    internal sealed class CircleEntityRecord
    {
        public int Id;
        public string CanonicalName = "";
        public string RomanName = "";
        public string EHGroupTag = "";
        public string DanbooruGroupTag = "";
        public string Source = "";
        public string UpdatedUtc = "";
    }

    internal sealed class AuthorCircleRecord
    {
        public int AuthorId;
        public int CircleId;
        public string Source = "";
    }

    internal sealed class AuthorLookupCacheRecord
    {
        public string Provider = "";
        public string Query = "";
        public string QueryNorm = "";
        public string Status = ""; // resolved / ambiguous / not_found / error
        public int AuthorId;
        public string CheckedUtc = "";
        public string Detail = "";
    }

    internal sealed class AuthorEntityMatch
    {
        public bool Found;
        public bool Ambiguous;
        public AuthorEntityRecord Entity;
        public List<string> IdentityNames = new List<string>();
    }

    internal sealed class AuthorEntityIndex
    {
        private readonly Dictionary<string, List<int>> _nameToIds =
            new Dictionary<string, List<int>>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<int, AuthorEntityRecord> _authorsById =
            new Dictionary<int, AuthorEntityRecord>();
        private readonly Dictionary<int, List<string>> _namesById =
            new Dictionary<int, List<string>>();

        public AuthorEntityIndex(AuthorEntityDatabase db)
        {
            if (db == null) return;

            foreach (AuthorEntityRecord author in db.Authors ?? new List<AuthorEntityRecord>())
            {
                if (author == null || author.Id <= 0) continue;
                _authorsById[author.Id] = author;
                AddName(author.Id, author.CanonicalName);
                AddName(author.Id, author.RomanName);
                AddName(author.Id, author.DanbooruArtistTag);
                AddName(author.Id, PrettyTag(author.DanbooruArtistTag));
                AddName(author.Id, author.EHArtistTag);
            }

            foreach (AuthorAliasRecord alias in db.Aliases ?? new List<AuthorAliasRecord>())
            {
                if (alias == null || alias.AuthorId <= 0) continue;
                AddName(alias.AuthorId, alias.Alias);
            }
        }

        public AuthorEntityMatch Resolve(string name)
        {
            AuthorEntityMatch result = new AuthorEntityMatch();
            string norm = NormalizeIdentity(name);
            if (norm.Length == 0) return result;

            List<int> ids;
            if (!_nameToIds.TryGetValue(norm, out ids) || ids == null || ids.Count == 0)
                return result;

            List<int> unique = ids.Distinct().ToList();
            if (unique.Count > 1)
            {
                result.Ambiguous = true;
                return result;
            }

            AuthorEntityRecord entity;
            if (!_authorsById.TryGetValue(unique[0], out entity) || entity == null)
                return result;

            result.Found = true;
            result.Entity = entity;
            List<string> names;
            if (_namesById.TryGetValue(entity.Id, out names) && names != null)
                result.IdentityNames = new List<string>(names);
            return result;
        }

        private void AddName(int authorId, string name)
        {
            if (authorId <= 0 || String.IsNullOrWhiteSpace(name)) return;
            string norm = NormalizeIdentity(name);
            if (norm.Length == 0) return;

            List<int> ids;
            if (!_nameToIds.TryGetValue(norm, out ids))
            {
                ids = new List<int>();
                _nameToIds[norm] = ids;
            }
            if (!ids.Contains(authorId)) ids.Add(authorId);

            List<string> names;
            if (!_namesById.TryGetValue(authorId, out names))
            {
                names = new List<string>();
                _namesById[authorId] = names;
            }
            if (!names.Any(delegate(string x) { return String.Equals(x, name, StringComparison.OrdinalIgnoreCase); }))
                names.Add(name.Trim());
        }

        private static string NormalizeIdentity(string value)
        {
            return AuthorRules.NormalizeText(value ?? "");
        }

        private static string PrettyTag(string value)
        {
            return String.IsNullOrWhiteSpace(value) ? "" : value.Replace('_', ' ').Trim();
        }
    }

    internal sealed class AuthorEntityStore
    {
        private readonly string _path;
        private readonly object _sync = new object();
        private readonly AuthorEntityDatabase _sessionOverlay = new AuthorEntityDatabase();
        private int _nextSessionAuthorId = 1000000000;

        public AuthorEntityStore(string path)
        {
            _path = path;
        }

        public string Path { get { return _path; } }

        public AuthorEntityDatabase Load()
        {
            lock (_sync)
            {
                if (!File.Exists(_path))
                    return new AuthorEntityDatabase();

                try
                {
                    string json = File.ReadAllText(_path, Encoding.UTF8);
                    JavaScriptSerializer serializer = new JavaScriptSerializer();
                    serializer.MaxJsonLength = Int32.MaxValue;
                    AuthorEntityDatabase db = serializer.Deserialize<AuthorEntityDatabase>(json);
                    return NormalizeDatabase(db);
                }
                catch
                {
                    return new AuthorEntityDatabase();
                }
            }
        }

        public AuthorEntityIndex LoadIndex()
        {
            AuthorEntityDatabase combined = Load();
            lock (_sync)
            {
                combined.Authors.AddRange(_sessionOverlay.Authors);
                combined.Aliases.AddRange(_sessionOverlay.Aliases);
                combined.Circles.AddRange(_sessionOverlay.Circles);
                combined.AuthorCircles.AddRange(_sessionOverlay.AuthorCircles);
                combined.LookupCache.AddRange(_sessionOverlay.LookupCache);
            }
            return new AuthorEntityIndex(combined);
        }

        public void EnsureExists()
        {
            lock (_sync)
            {
                if (File.Exists(_path)) return;
                SaveInternal(new AuthorEntityDatabase());
            }
        }

        public AuthorLookupCacheRecord GetFreshLookup(string provider, string query, int staleDays)
        {
            AuthorEntityDatabase db = Load();
            lock (_sync)
            {
                db.LookupCache.AddRange(_sessionOverlay.LookupCache);
            }
            string norm = AuthorRules.NormalizeText(query ?? "");
            if (norm.Length == 0) return null;

            AuthorLookupCacheRecord entry = (db.LookupCache ?? new List<AuthorLookupCacheRecord>())
                .Where(delegate(AuthorLookupCacheRecord x)
                {
                    return x != null &&
                        String.Equals(x.Provider ?? "", provider ?? "", StringComparison.OrdinalIgnoreCase) &&
                        String.Equals(x.QueryNorm ?? "", norm, StringComparison.OrdinalIgnoreCase);
                })
                .OrderByDescending(delegate(AuthorLookupCacheRecord x) { return ParseUtc(x.CheckedUtc); })
                .FirstOrDefault();

            if (entry == null) return null;
            DateTime checkedUtc = ParseUtc(entry.CheckedUtc);
            if (checkedUtc == DateTime.MinValue) return null;
            if (staleDays > 0 && checkedUtc < DateTime.UtcNow.AddDays(-staleDays)) return null;
            return entry;
        }

        public AuthorEntityRecord MergeResolvedProviderResult(
            string provider,
            string query,
            AuthorProviderCandidate candidate,
            bool saveCache)
        {
            if (candidate == null) return null;

            lock (_sync)
            {
                AuthorEntityDatabase db = saveCache
                    ? LoadUnlocked()
                    : _sessionOverlay;
                AuthorEntityRecord author = FindAuthorByProviderIdentity(db, provider, candidate.ExternalId, candidate.TagName);

                if (author == null)
                {
                    author = new AuthorEntityRecord();
                    author.Id = saveCache
                        ? NextAuthorId(db)
                        : _nextSessionAuthorId++;
                    author.CanonicalName = ChooseCanonicalName(query, candidate);
                    author.RomanName = PrettyTag(candidate.TagName);
                    author.DanbooruArtistTag = String.Equals(provider, "Danbooru", StringComparison.OrdinalIgnoreCase)
                        ? (candidate.TagName ?? "") : "";
                    author.Source = provider ?? "";
                    author.ExternalId = candidate.ExternalId ?? "";
                    author.Verified = false;
                    author.UpdatedUtc = DateTime.UtcNow.ToString("o");
                    db.Authors.Add(author);
                }
                else
                {
                    if (String.IsNullOrWhiteSpace(author.CanonicalName))
                        author.CanonicalName = ChooseCanonicalName(query, candidate);
                    if (String.IsNullOrWhiteSpace(author.RomanName))
                        author.RomanName = PrettyTag(candidate.TagName);
                    if (String.Equals(provider, "Danbooru", StringComparison.OrdinalIgnoreCase) &&
                        String.IsNullOrWhiteSpace(author.DanbooruArtistTag))
                        author.DanbooruArtistTag = candidate.TagName ?? "";
                    author.UpdatedUtc = DateTime.UtcNow.ToString("o");
                }

                AddAlias(db, author.Id, query, "query", provider);
                AddAlias(db, author.Id, candidate.TagName, "provider_tag", provider);
                AddAlias(db, author.Id, PrettyTag(candidate.TagName), "roman", provider);
                foreach (string alias in candidate.OtherNames ?? new List<string>())
                {
                    AddAlias(db, author.Id, alias, "other_name", provider);
                    AddAlias(db, author.Id, PrettyTag(alias), "other_name", provider);
                }

                if (!String.IsNullOrWhiteSpace(candidate.GroupName))
                {
                    CircleEntityRecord circle = FindOrCreateCircle(db, provider, candidate.GroupName);
                    if (circle != null)
                        AddAuthorCircle(db, author.Id, circle.Id, provider);
                }

                UpsertLookupCache(db, provider, query, "resolved", author.Id, "unique exact artist identity");

                if (saveCache)
                    SaveInternal(db);
                return author;
            }
        }

        public void SaveLookupStatus(string provider, string query, string status, string detail, bool saveCache)
        {
            lock (_sync)
            {
                AuthorEntityDatabase db = saveCache
                    ? LoadUnlocked()
                    : _sessionOverlay;
                UpsertLookupCache(db, provider, query, status, 0, detail);
                if (saveCache)
                    SaveInternal(db);
            }
        }

        private AuthorEntityDatabase LoadUnlocked()
        {
            if (!File.Exists(_path)) return new AuthorEntityDatabase();
            try
            {
                string json = File.ReadAllText(_path, Encoding.UTF8);
                JavaScriptSerializer serializer = new JavaScriptSerializer();
                serializer.MaxJsonLength = Int32.MaxValue;
                return NormalizeDatabase(serializer.Deserialize<AuthorEntityDatabase>(json));
            }
            catch
            {
                return new AuthorEntityDatabase();
            }
        }

        private void SaveInternal(AuthorEntityDatabase db)
        {
            try
            {
                string dir = System.IO.Path.GetDirectoryName(_path);
                if (!String.IsNullOrWhiteSpace(dir)) Directory.CreateDirectory(dir);
                JavaScriptSerializer serializer = new JavaScriptSerializer();
                serializer.MaxJsonLength = Int32.MaxValue;
                string json = serializer.Serialize(NormalizeDatabase(db));
                string temp = _path + ".tmp";
                File.WriteAllText(temp, json, new UTF8Encoding(false));

                if (File.Exists(_path))
                {
                    string backup = _path + ".bak";
                    try
                    {
                        File.Replace(temp, _path, backup, true);
                        return;
                    }
                    catch
                    {
                        try { File.Copy(_path, backup, true); } catch { }
                        File.Delete(_path);
                    }
                }

                File.Move(temp, _path);
            }
            catch
            {
                // Cache persistence must never block the archive workflow.
            }
        }

        private static AuthorEntityDatabase NormalizeDatabase(AuthorEntityDatabase db)
        {
            if (db == null) db = new AuthorEntityDatabase();
            if (db.Authors == null) db.Authors = new List<AuthorEntityRecord>();
            if (db.Aliases == null) db.Aliases = new List<AuthorAliasRecord>();
            if (db.Circles == null) db.Circles = new List<CircleEntityRecord>();
            if (db.AuthorCircles == null) db.AuthorCircles = new List<AuthorCircleRecord>();
            if (db.LookupCache == null) db.LookupCache = new List<AuthorLookupCacheRecord>();
            if (db.Version <= 0) db.Version = 1;
            return db;
        }

        private static int NextAuthorId(AuthorEntityDatabase db)
        {
            return db.Authors.Count == 0 ? 1 : db.Authors.Max(delegate(AuthorEntityRecord x) { return x != null ? x.Id : 0; }) + 1;
        }

        private static int NextCircleId(AuthorEntityDatabase db)
        {
            return db.Circles.Count == 0 ? 1 : db.Circles.Max(delegate(CircleEntityRecord x) { return x != null ? x.Id : 0; }) + 1;
        }

        private static AuthorEntityRecord FindAuthorByProviderIdentity(AuthorEntityDatabase db, string provider, string externalId, string tagName)
        {
            foreach (AuthorEntityRecord author in db.Authors)
            {
                if (author == null) continue;
                if (!String.IsNullOrWhiteSpace(externalId) &&
                    String.Equals(author.Source ?? "", provider ?? "", StringComparison.OrdinalIgnoreCase) &&
                    String.Equals(author.ExternalId ?? "", externalId, StringComparison.OrdinalIgnoreCase))
                    return author;

                if (String.Equals(provider, "Danbooru", StringComparison.OrdinalIgnoreCase) &&
                    !String.IsNullOrWhiteSpace(tagName) &&
                    String.Equals(author.DanbooruArtistTag ?? "", tagName, StringComparison.OrdinalIgnoreCase))
                    return author;
            }
            return null;
        }

        private static void AddAlias(AuthorEntityDatabase db, int authorId, string alias, string type, string source)
        {
            if (authorId <= 0 || String.IsNullOrWhiteSpace(alias)) return;
            string value = alias.Trim();
            string norm = AuthorRules.NormalizeText(value);
            if (norm.Length == 0) return;

            bool exists = db.Aliases.Any(delegate(AuthorAliasRecord x)
            {
                return x != null && x.AuthorId == authorId &&
                    String.Equals(AuthorRules.NormalizeText(x.Alias ?? ""), norm, StringComparison.OrdinalIgnoreCase);
            });
            if (exists) return;

            AuthorAliasRecord record = new AuthorAliasRecord();
            record.AuthorId = authorId;
            record.Alias = value;
            record.AliasType = type ?? "";
            record.Source = source ?? "";
            db.Aliases.Add(record);
        }

        private static CircleEntityRecord FindOrCreateCircle(AuthorEntityDatabase db, string provider, string groupName)
        {
            string norm = AuthorRules.NormalizeText(groupName ?? "");
            if (norm.Length == 0) return null;

            CircleEntityRecord found = db.Circles.FirstOrDefault(delegate(CircleEntityRecord x)
            {
                if (x == null) return false;
                return String.Equals(AuthorRules.NormalizeText(x.CanonicalName ?? ""), norm, StringComparison.OrdinalIgnoreCase) ||
                       String.Equals(AuthorRules.NormalizeText(x.DanbooruGroupTag ?? ""), norm, StringComparison.OrdinalIgnoreCase);
            });
            if (found != null) return found;

            CircleEntityRecord circle = new CircleEntityRecord();
            circle.Id = NextCircleId(db);
            circle.CanonicalName = PrettyTag(groupName);
            circle.RomanName = PrettyTag(groupName);
            circle.DanbooruGroupTag = String.Equals(provider, "Danbooru", StringComparison.OrdinalIgnoreCase) ? groupName : "";
            circle.Source = provider ?? "";
            circle.UpdatedUtc = DateTime.UtcNow.ToString("o");
            db.Circles.Add(circle);
            return circle;
        }

        private static void AddAuthorCircle(AuthorEntityDatabase db, int authorId, int circleId, string source)
        {
            if (authorId <= 0 || circleId <= 0) return;
            if (db.AuthorCircles.Any(delegate(AuthorCircleRecord x) { return x != null && x.AuthorId == authorId && x.CircleId == circleId; })) return;
            AuthorCircleRecord relation = new AuthorCircleRecord();
            relation.AuthorId = authorId;
            relation.CircleId = circleId;
            relation.Source = source ?? "";
            db.AuthorCircles.Add(relation);
        }

        private static void UpsertLookupCache(AuthorEntityDatabase db, string provider, string query, string status, int authorId, string detail)
        {
            string norm = AuthorRules.NormalizeText(query ?? "");
            if (norm.Length == 0) return;

            db.LookupCache.RemoveAll(delegate(AuthorLookupCacheRecord x)
            {
                return x != null &&
                    String.Equals(x.Provider ?? "", provider ?? "", StringComparison.OrdinalIgnoreCase) &&
                    String.Equals(x.QueryNorm ?? "", norm, StringComparison.OrdinalIgnoreCase);
            });

            AuthorLookupCacheRecord entry = new AuthorLookupCacheRecord();
            entry.Provider = provider ?? "";
            entry.Query = query ?? "";
            entry.QueryNorm = norm;
            entry.Status = status ?? "";
            entry.AuthorId = authorId;
            entry.CheckedUtc = DateTime.UtcNow.ToString("o");
            entry.Detail = detail ?? "";
            db.LookupCache.Add(entry);
        }

        private static string ChooseCanonicalName(string query, AuthorProviderCandidate candidate)
        {
            if (ContainsCjk(query)) return (query ?? "").Trim();

            foreach (string name in candidate.OtherNames ?? new List<string>())
            {
                string pretty = PrettyTag(name);
                if (ContainsCjk(pretty)) return pretty;
            }

            string providerName = PrettyTag(candidate.TagName);
            return providerName.Length > 0 ? providerName : (query ?? "").Trim();
        }

        private static bool ContainsCjk(string value)
        {
            if (String.IsNullOrEmpty(value)) return false;
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                if ((c >= '\u3040' && c <= '\u30ff') ||
                    (c >= '\u3400' && c <= '\u4dbf') ||
                    (c >= '\u4e00' && c <= '\u9fff') ||
                    (c >= '\uf900' && c <= '\ufaff'))
                    return true;
            }
            return false;
        }

        private static string PrettyTag(string value)
        {
            return String.IsNullOrWhiteSpace(value) ? "" : value.Replace('_', ' ').Trim();
        }

        private static DateTime ParseUtc(string value)
        {
            DateTime parsed;
            if (DateTime.TryParse(value, null, System.Globalization.DateTimeStyles.RoundtripKind, out parsed))
                return parsed.ToUniversalTime();
            return DateTime.MinValue;
        }
    }
}
