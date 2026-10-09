using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Web.Script.Serialization;
using System.Collections.Concurrent;

namespace MangaAuthorSorter
{
    internal sealed class AuthorEntityDatabase
    {
        public int Version = 1;
        public int SchemaVersion = 3;
        public long DataRevision;
        public string LegacyAliasHash = "";
        public List<string> LegacyUnparsedLines = new List<string>();
        public List<LegacyAliasGroupRecord> LegacyAliasGroups = new List<LegacyAliasGroupRecord>();
        public List<AuthorEntityRecord> Authors = new List<AuthorEntityRecord>();
        public List<AuthorAliasRecord> Aliases = new List<AuthorAliasRecord>();
        public List<CircleEntityRecord> Circles = new List<CircleEntityRecord>();
        public List<AuthorCircleRecord> AuthorCircles = new List<AuthorCircleRecord>();
        public List<AuthorLookupCacheRecord> LookupCache = new List<AuthorLookupCacheRecord>();
        public List<CircleAliasRecord> CircleAliases = new List<CircleAliasRecord>();
        public List<PublicEntityOverride> PublicOverrides = new List<PublicEntityOverride>();
        public List<AuthorNameDecision> NameDecisions = new List<AuthorNameDecision>();
    }

    internal sealed class LegacyAliasGroupRecord
    {
        public string Id = "";
        public string Canonical = "";
        public List<string> Names = new List<string>();
        public string Status = "Pending";
        public int AuthorId;
    }

    internal sealed class AuthorEntityRecord
    {
        public int Id;
        public string CanonicalName = "";
        public string RomanName = "";
        public string EHArtistTag = "";
        public string NHArtistTag = "";
        public string DanbooruArtistTag = "";
        public string Source = "";
        public string ExternalId = "";
        public string EntityType = "Artist";
        public string VerificationSource = "Unverified";
        public string EHNamespace = "";
        public string EHTag = "";
        public bool Verified;
        public bool UserConfirmed = false;
        public string EntityId = "";
        public string PublicEntityKey = "";
        public List<string> ProviderIdentities = new List<string>();
        public string UpdatedUtc = "";
    }

    internal sealed class AuthorAliasRecord
    {
        public int AuthorId;
        public string Alias = "";
        public string AliasType = "";
        public string Source = "";
        public bool Disabled = false;
    }

    internal sealed class CircleEntityRecord
    {
        public int Id;
        public string CanonicalName = "";
        public string RomanName = "";
        public string EHGroupTag = "";
        public string NHGroupTag = "";
        public string Source = "";
        public string UpdatedUtc = "";
        public string EntityId = "";
        public bool UserConfirmed;
        public string PublicEntityKey = "";
        public List<string> ProviderIdentities = new List<string>();
    }

    internal sealed class AuthorCircleRecord
    {
        public int AuthorId;
        public int CircleId;
        public string Source = "";
        public bool UserConfirmed;
        public bool Disabled;
        public string PublicOverrideId = "";
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
        public bool FromPublicDatabase;
        public AuthorEntityRecord Entity;
        public List<string> IdentityNames = new List<string>();
        public List<CircleEntityRecord> RelatedGroups = new List<CircleEntityRecord>();
    }

    internal sealed class AuthorEntityIndex
    {
        private static readonly string EmptyDependencyVersion = AuthorEntityStore.ContentHash(Encoding.UTF8.GetBytes("AuthorDependencies=1"));
        public readonly List<AliasGroup> AliasGroups = new List<AliasGroup>();
        private readonly GuiGuiAuthorIndexDatabase _publicDatabase;
        private readonly AuthorEntityIndex _publicOverrideIndex;
        private readonly Dictionary<string, AuthorNameDecision> _decisions = new Dictionary<string, AuthorNameDecision>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<int, List<CircleEntityRecord>> _related = new Dictionary<int, List<CircleEntityRecord>>();
        private readonly Dictionary<int, HashSet<string>> _disabledNames = new Dictionary<int, HashSet<string>>();
        private readonly Dictionary<string, string> _dependencies = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private readonly ConcurrentDictionary<string, string> _fileDependencies = new ConcurrentDictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _unresolvedPublicNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _unresolvedPublicDisabled = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<int, string> _publicEntityDependencies = new Dictionary<int, string>();
        private readonly HashSet<int> _activePublicOverrides = new HashSet<int>();
        private readonly HashSet<int> _activePublicGroupOverrides = new HashSet<int>();
        private readonly HashSet<string> _publicGroupNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _unresolvedPublicGroupNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _unresolvedPublicGroupDisabled = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, string> _publicNameDependencies = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private readonly AuthorEntityDatabase _database;
        private readonly Dictionary<string, List<int>> _groupNameToIds = new Dictionary<string, List<int>>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<int, CircleEntityRecord> _groupsById = new Dictionary<int, CircleEntityRecord>();
        private readonly Dictionary<int, List<string>> _groupNamesById = new Dictionary<int, List<string>>();
        private readonly Dictionary<string, List<int>> _nameToIds =
            new Dictionary<string, List<int>>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<int, AuthorEntityRecord> _authorsById =
            new Dictionary<int, AuthorEntityRecord>();
        private readonly Dictionary<int, List<string>> _namesById =
            new Dictionary<int, List<string>>();

        public AuthorEntityIndex(AuthorEntityDatabase db, GuiGuiAuthorIndexDatabase publicDatabase)
            : this(db, publicDatabase, null)
        {
        }

        public AuthorEntityIndex(AuthorEntityDatabase db, GuiGuiAuthorIndexDatabase publicDatabase, Dictionary<int, PublicEntitySnapshot> snapshots, bool includeDependencies = true)
        {
            _publicDatabase = publicDatabase;
            if (db == null) return;
            _database = db;
            var circleAliases = db.CircleAliases.ToLookup(x => x.CircleId);
            foreach (CircleEntityRecord circle in db.Circles)
            {
                _groupsById[circle.Id] = circle;
                var disabled = new HashSet<string>(circleAliases[circle.Id].Where(x => x.Disabled).Select(x => AuthorRules.NormalizeText(x.Alias)), StringComparer.OrdinalIgnoreCase);
                List<string> names = new[] { circle.CanonicalName, circle.RomanName, circle.EHGroupTag, circle.NHGroupTag }
                    .Concat(circleAliases[circle.Id].Where(x => !x.Disabled).Select(x => x.Alias)).Where(x => !String.IsNullOrWhiteSpace(x) && !disabled.Contains(AuthorRules.NormalizeText(x))).Distinct().ToList();
                _groupNamesById[circle.Id] = names;
                foreach (string value in names) foreach (string norm in AuthorRules.GetLiteralNorms(value)) {
                    if (disabled.Contains(norm)) continue;
                    List<int> ids; if (!_groupNameToIds.TryGetValue(norm, out ids)) { ids = new List<int>(); _groupNameToIds[norm] = ids; }
                    if (!ids.Contains(circle.Id)) ids.Add(circle.Id);
                }
            }
            foreach (var group in db.Aliases.Where(x => x.Disabled).GroupBy(x => x.AuthorId))
                _disabledNames[group.Key] = new HashSet<string>(group.Select(x => AuthorRules.NormalizeText(x.Alias)), StringComparer.OrdinalIgnoreCase);
            foreach (AuthorNameDecision decision in db.NameDecisions.Where(x => x.EntityType == "Artist"))
                _decisions[AuthorRules.NormalizeText(decision.Name)] = decision;
            if (snapshots != null && db.PublicOverrides.Count > 0)
            {
                _publicOverrideIndex = new AuthorEntityIndex(BuildPublicView(db, snapshots), null, null, false);
                JavaScriptSerializer overlaySerializer = new JavaScriptSerializer { MaxJsonLength = Int32.MaxValue };
                foreach (PublicEntityOverride record in db.PublicOverrides.Where(x => x.EntityType == "Artist")) {
                    string fingerprint = AuthorEntityStore.ContentHash(Encoding.UTF8.GetBytes(overlaySerializer.Serialize(record)));
                    foreach (string name in record.OriginalNames.Concat(record.AddedAliases).Concat(record.DisabledNames).Concat(new[] { record.CanonicalName })) {
                        string norm = NormalizeIdentity(name), previous; _publicNameDependencies.TryGetValue(norm, out previous);
                        _publicNameDependencies[norm] = previous + "|" + fingerprint;
                    }
                    List<PublicEntitySnapshot> bound = AuthorEntityStore.BindOverride(record, snapshots.Values);
                    if (bound.Count == 1) {
                        _publicEntityDependencies[bound[0].Id] = fingerprint;
                        foreach (string name in bound[0].Aliases.Concat(bound[0].LookupNames.SelectMany(x => new[] { x.Value, x.NormalizedName })).Concat(new[] { bound[0].CanonicalName, bound[0].RomanName })) {
                            foreach (string norm in GetLookupNorms(name)) {
                                string previous; _publicNameDependencies.TryGetValue(norm, out previous);
                                _publicNameDependencies[norm] = previous + "|" + fingerprint;
                            }
                        }
                        if (record.Status == "Active") _activePublicOverrides.Add(bound[0].Id);
                    }
                }
                foreach (PublicEntityOverride record in db.PublicOverrides.Where(x => x.EntityType == "Artist" && x.Status == "Active" && AuthorEntityStore.BindOverride(x, snapshots.Values).Count != 1)) {
                    foreach (string name in record.OriginalNames.Concat(record.AddedAliases)) _unresolvedPublicNames.Add(NormalizeIdentity(name));
                    foreach (string name in record.DisabledNames) _unresolvedPublicDisabled.Add(NormalizeIdentity(name));
                }
                foreach (PublicEntityOverride record in db.PublicOverrides.Where(x => x.EntityType == "Group")) {
                    foreach (string name in record.OriginalNames.Concat(record.AddedAliases).Concat(record.DisabledNames).Concat(new[] { record.CanonicalName })) _publicGroupNames.Add(NormalizeIdentity(name));
                    List<PublicEntitySnapshot> bound = AuthorEntityStore.BindOverride(record, snapshots.Values);
                    if (record.Status == "Active" && bound.Count == 1) _activePublicGroupOverrides.Add(bound[0].Id);
                    else if (record.Status == "Active") {
                        foreach (string name in record.OriginalNames.Concat(record.AddedAliases)) _unresolvedPublicGroupNames.Add(NormalizeIdentity(name));
                        foreach (string name in record.DisabledNames) _unresolvedPublicGroupDisabled.Add(NormalizeIdentity(name));
                    }
                }
            }
            foreach (LegacyAliasGroupRecord record in db.LegacyAliasGroups)
            {
                if (record.AuthorId > 0) continue;
                AliasGroup group = new AliasGroup { Canonical = record.Canonical, EntityGroupId = record.Id };
                group.Names.AddRange(record.Names);
                foreach (string alias in group.Names)
                    foreach (string key in AuthorRules.GetLiteralNorms(alias)) group.Norms.Add(key);
                AliasGroups.Add(group);
            }

            foreach (AuthorEntityRecord author in db.Authors ?? new List<AuthorEntityRecord>())
            {
                if (author == null || author.Id == 0 || !String.Equals(author.EntityType, "Artist", StringComparison.OrdinalIgnoreCase) ||
                    (!author.UserConfirmed && (author.Source ?? "").IndexOf("EhTagTranslation", StringComparison.OrdinalIgnoreCase) >= 0)) continue;
                _authorsById[author.Id] = author;
                AddName(author.Id, author.CanonicalName);
                AddName(author.Id, author.RomanName);
                AddName(author.Id, author.EHArtistTag);
                AddName(author.Id, PrettyTag(author.EHArtistTag));
                AddName(author.Id, author.NHArtistTag);
                AddName(author.Id, PrettyTag(author.NHArtistTag));
                AddName(author.Id, author.DanbooruArtistTag);
                AddName(author.Id, PrettyTag(author.DanbooruArtistTag));
            }

            foreach (AuthorAliasRecord alias in db.Aliases ?? new List<AuthorAliasRecord>())
            {
                if (alias == null || alias.AuthorId == 0 || alias.Disabled || !_authorsById.ContainsKey(alias.AuthorId) ||
                    (alias.Source ?? "").IndexOf("EhTagTranslation", StringComparison.OrdinalIgnoreCase) >= 0) continue;
                AddName(alias.AuthorId, alias.Alias);
            }
            var relatedCirclesById = db.Circles.ToDictionary(x => x.Id);
            foreach (AuthorCircleRecord relation in db.AuthorCircles.Where(x => !x.Disabled))
            {
                CircleEntityRecord circle;
                relatedCirclesById.TryGetValue(relation.CircleId, out circle);
                if (circle == null) continue;
                List<CircleEntityRecord> groups;
                if (!_related.TryGetValue(relation.AuthorId, out groups)) { groups = new List<CircleEntityRecord>(); _related[relation.AuthorId] = groups; }
                groups.Add(circle);
            }
            if (!includeDependencies) return;
            foreach (var pair in _authorsById)
            {
                List<string> names;
                if (!_namesById.TryGetValue(pair.Key, out names)) continue;
                AliasGroup group = new AliasGroup { Canonical = pair.Value.CanonicalName, EntityGroupId = pair.Value.EntityId };
                foreach (string name in names)
                {
                    AuthorNameDecision decision;
                    if (_decisions.TryGetValue(AuthorRules.NormalizeText(name), out decision) &&
                        (decision.Status == "Disabled" || (decision.Status == "Confirmed" && decision.EntityId != pair.Key))) continue;
                    group.Names.Add(name);
                    foreach (string key in AuthorRules.GetLiteralNorms(name)) group.Norms.Add(key);
                }
                if (group.Norms.Count > 0) AliasGroups.Add(group);
            }
            JavaScriptSerializer serializer = new JavaScriptSerializer { MaxJsonLength = Int32.MaxValue };
            var aliasesByAuthor = db.Aliases.ToLookup(x => x.AuthorId);
            var relationsByAuthor = db.AuthorCircles.ToLookup(x => x.AuthorId);
            var circlesById = db.Circles.ToDictionary(x => x.Id);
            Dictionary<int, string> entityVersions = new Dictionary<int, string>();
            foreach (var pair in _authorsById)
                entityVersions[pair.Key] = AuthorEntityStore.ContentHash(Encoding.UTF8.GetBytes(serializer.Serialize(new {
                    pair.Value.CanonicalName, pair.Value.RomanName, pair.Value.EHArtistTag, pair.Value.NHArtistTag, pair.Value.DanbooruArtistTag,
                    pair.Value.UserConfirmed, pair.Value.EntityType,
                    Aliases = aliasesByAuthor[pair.Key].OrderBy(x => x.Alias).ToList(),
                    Relations = relationsByAuthor[pair.Key].ToList(),
                    Groups = relationsByAuthor[pair.Key].Select(x => new {
                        Entity = circlesById.ContainsKey(x.CircleId) ? circlesById[x.CircleId] : null,
                        Aliases = circleAliases[x.CircleId].ToList()
                    }).ToList()
                })));
            foreach (var pair in _nameToIds)
                _dependencies[pair.Key] = String.Join(";", pair.Value.OrderBy(x => x).Select(x => entityVersions[x]).ToArray());
            foreach (var pair in _decisions) {
                string previous; _dependencies.TryGetValue(pair.Key, out previous);
                _dependencies[pair.Key] = previous + "|decision=" + serializer.Serialize(pair.Value);
            }
            foreach (LegacyAliasGroupRecord rule in db.LegacyAliasGroups.Where(x => x.AuthorId == 0))
                foreach (string name in rule.Names)
                    foreach (string norm in AuthorRules.GetLiteralNorms(name)) {
                        string previous; _dependencies.TryGetValue(norm, out previous);
                        _dependencies[norm] = previous + "|rule=" + serializer.Serialize(rule);
                    }
        }

        public string GetDependencyVersion(string fileName)
        {
            if (_dependencies.Count == 0 && _publicNameDependencies.Count == 0 && _publicOverrideIndex == null) return EmptyDependencyVersion;
            return _fileDependencies.GetOrAdd(fileName ?? "", delegate(string input)
            {
                List<string> candidates = AuthorRules.GetAuthorCandidatesFromFileName(System.IO.Path.GetFileNameWithoutExtension(input));
                SortedSet<string> norms = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (string candidate in candidates)
                {
                    foreach (string norm in GetLookupNorms(candidate)) norms.Add(norm);
                    foreach (string norm in AuthorRules.GetLiteralNorms(candidate)) norms.Add(norm);
                }
                StringBuilder evidence = new StringBuilder("AuthorDependencies=1");
                foreach (string norm in norms) {
                    string value; if (_dependencies.TryGetValue(norm, out value)) evidence.Append('|').Append(norm).Append('=').Append(value);
                    if (_publicNameDependencies.TryGetValue(norm, out value)) evidence.Append("|public-name:").Append(norm).Append('=').Append(value);
                }
                // Every affected public lookup key is projected when the immutable
                // index is built. Dependency checks must never issue public SQL.
                return AuthorEntityStore.ContentHash(Encoding.UTF8.GetBytes(evidence.ToString()));
            });
        }

        public AuthorEntityMatch Resolve(string name, string entityType)
        {
            if (entityType == "Artist") return Resolve(name);
            AuthorEntityMatch result = new AuthorEntityMatch();
            if (entityType != "Group" || _database == null) return result;
            string norm = NormalizeIdentity(name);
            AuthorNameDecision decision = _database.NameDecisions.FirstOrDefault(x => x.EntityType == "Group" && NormalizeIdentity(x.Name) == norm);
            if (decision != null && decision.Status == "Disabled") return result;
            HashSet<int> hits = new HashSet<int>();
            foreach (string lookup in AuthorRules.GetLiteralNorms(name)) {
                List<int> ids; if (_groupNameToIds.TryGetValue(lookup, out ids)) foreach (int id in ids) hits.Add(id);
            }
            List<CircleEntityRecord> groups = hits.Select(id => _groupsById[id]).ToList();
            if (groups.Count > 0 && decision != null && (decision.Status == "Pending" || decision.Status == "Independent")) {
                AuthorEntityMatch publicCandidate = _publicDatabase == null ? new AuthorEntityMatch() : _publicDatabase.ResolveGroup(name);
                if (publicCandidate.Found || publicCandidate.Ambiguous) return new AuthorEntityMatch { Ambiguous = true };
            }
            if (decision != null && decision.Status == "Confirmed") groups = groups.Where(x => x.Id == decision.EntityId).ToList();
            if (groups.Count > 1) { result.Ambiguous = true; return result; }
            if (groups.Count == 1) {
                CircleEntityRecord g = groups[0]; result.Found = true;
                result.Entity = new AuthorEntityRecord { Id = g.Id, EntityType = "Group", CanonicalName = g.CanonicalName, RomanName = g.RomanName, EntityId = g.EntityId };
                result.IdentityNames = new List<string>(_groupNamesById[g.Id]);
                return result;
            }
            result = _publicDatabase == null ? new AuthorEntityMatch() : _publicDatabase.ResolveGroup(name);
            bool affected = _publicGroupNames.Contains(norm) || (result.Entity != null && _activePublicGroupOverrides.Contains(result.Entity.Id));
            if (_publicOverrideIndex != null && affected) { result = _publicOverrideIndex.Resolve(name, "Group"); result.FromPublicDatabase = true; }
            if (result.Found && result.Entity != null && _activePublicGroupOverrides.Contains(result.Entity.Id)) return result;
            if (_unresolvedPublicGroupDisabled.Contains(norm)) return new AuthorEntityMatch();
            if (_unresolvedPublicGroupNames.Contains(norm)) return new AuthorEntityMatch { Ambiguous = true, FromPublicDatabase = true };
            return result;
        }

        private static AuthorEntityDatabase BuildPublicView(AuthorEntityDatabase user, Dictionary<int, PublicEntitySnapshot> snapshots)
        {
            AuthorEntityDatabase view = new AuthorEntityDatabase();
            Dictionary<int, PublicEntityOverride> overlays = new Dictionary<int, PublicEntityOverride>();
            foreach (PublicEntityOverride record in user.PublicOverrides.Where(x => x.Status == "Active"))
            {
                List<PublicEntitySnapshot> bound = AuthorEntityStore.BindOverride(record, snapshots.Values);
                if (bound.Count == 1) overlays[bound[0].Id] = record;
            }
            foreach (PublicEntitySnapshot row in snapshots.Values)
            {
                if ((row.Source ?? "").IndexOf("EhTagTranslation", StringComparison.OrdinalIgnoreCase) >= 0) continue;
                PublicEntityOverride overlay;
                overlays.TryGetValue(row.Id, out overlay);
                string canonical = overlay != null && !String.IsNullOrWhiteSpace(overlay.CanonicalName) ? overlay.CanonicalName : row.CanonicalName;
                List<string> names = row.Aliases.Concat(row.LookupNames.Select(x => x.Value))
                    .Concat(new[] { row.CanonicalName, row.RomanName }).Concat(overlay == null ? (IEnumerable<string>)new string[0] : overlay.AddedAliases).ToList();
                Func<string, bool> allowed = n => overlay == null || !overlay.DisabledNames.Any(x => AuthorRules.NormalizeText(x) == AuthorRules.NormalizeText(n));
                names = names.Where(allowed).ToList();
                List<string> disabledKeys = row.LookupNames.Where(x => !allowed(x.Value)).Select(x => x.NormalizedName).ToList();
                names.AddRange(row.LookupNames.Where(x => allowed(x.Value)).Select(x => x.NormalizedName));
                if (row.EntityType == "Artist")
                {
                    view.Authors.Add(new AuthorEntityRecord { Id = row.Id, EntityType = "Artist", CanonicalName = canonical,
                        RomanName = "", Source = "Public", EntityId = row.StableKey });
                    foreach (string name in names) view.Aliases.Add(new AuthorAliasRecord { AuthorId = row.Id, Alias = name, Source = "Public" });
                    // Canonical display values remain available while disabled names are excluded from lookup.
                    foreach (string name in overlay == null ? (IEnumerable<string>)new string[0] : overlay.DisabledNames)
                        view.Aliases.Add(new AuthorAliasRecord { AuthorId = row.Id, Alias = name, Disabled = true });
                    foreach (string name in disabledKeys) view.Aliases.Add(new AuthorAliasRecord { AuthorId = row.Id, Alias = name, Disabled = true });
                    foreach (int groupId in row.RelatedGroups) view.AuthorCircles.Add(new AuthorCircleRecord { AuthorId = row.Id, CircleId = groupId, Source = "Public", UserConfirmed = true });
                    if (overlay != null)
                        foreach (PublicUserGroupRelation relation in overlay.UserGroups.Where(x => !x.Disabled))
                            view.AuthorCircles.Add(new AuthorCircleRecord { AuthorId = row.Id, CircleId = -relation.CircleId,
                                Source = "User", UserConfirmed = relation.UserConfirmed });
                }
                else if (row.EntityType == "Group") {
                    view.Circles.Add(new CircleEntityRecord { Id = row.Id, CanonicalName = canonical, RomanName = row.RomanName });
                    foreach (string name in names) view.CircleAliases.Add(new CircleAliasRecord { CircleId = row.Id, Alias = name, Source = "Public" });
                    foreach (string name in overlay == null ? (IEnumerable<string>)new string[0] : overlay.DisabledNames)
                        view.CircleAliases.Add(new CircleAliasRecord { CircleId = row.Id, Alias = name, Disabled = true, Source = "Public" });
                    foreach (string name in disabledKeys) view.CircleAliases.Add(new CircleAliasRecord { CircleId = row.Id, Alias = name, Disabled = true, Source = "Public" });
                }
            }
            foreach (CircleEntityRecord circle in user.Circles.Where(x => x.Id > 0))
            {
                view.Circles.Add(new CircleEntityRecord { Id = -circle.Id, CanonicalName = circle.CanonicalName, RomanName = circle.RomanName, EntityId = circle.EntityId, UserConfirmed = circle.UserConfirmed });
                foreach (CircleAliasRecord alias in user.CircleAliases.Where(x => x.CircleId == circle.Id))
                    view.CircleAliases.Add(new CircleAliasRecord { CircleId = -circle.Id, Alias = alias.Alias, Disabled = alias.Disabled });
            }
            return view;
        }

        private AuthorEntityMatch ResolvePublic(string name)
        {
            AuthorEntityMatch original = _publicDatabase != null ? _publicDatabase.Resolve(name) : new AuthorEntityMatch();
            bool affected = _publicNameDependencies.ContainsKey(NormalizeIdentity(name)) ||
                (original.Entity != null && _activePublicOverrides.Contains(original.Entity.Id));
            AuthorEntityMatch result = _publicOverrideIndex != null && affected ? _publicOverrideIndex.Resolve(name) : original;
            if (_publicOverrideIndex != null) result.FromPublicDatabase = true;
            if (result.Entity != null && (result.Entity.Source ?? "").IndexOf("EhTagTranslation", StringComparison.OrdinalIgnoreCase) >= 0)
                return new AuthorEntityMatch();
            if (result.Found && result.Entity != null && _activePublicOverrides.Contains(result.Entity.Id)) return result;
            if (_unresolvedPublicDisabled.Contains(NormalizeIdentity(name))) return new AuthorEntityMatch();
            if (_unresolvedPublicNames.Contains(NormalizeIdentity(name))) return new AuthorEntityMatch { Ambiguous = true, FromPublicDatabase = true };
            return result;
        }

        public AuthorEntityMatch Resolve(string name)
        {
            AuthorEntityMatch result = new AuthorEntityMatch();
            string norm = NormalizeIdentity(name);
            if (norm.Length == 0) return result;

            // Resolve structured names by creator role before any generic name
            // lookup. The society portion must never become an Artist match.
            StructuredAuthorParts parts = AuthorRules.GetStructuredAuthorParts(name);
            if (parts != null && parts.Creators != null && parts.Creators.Count > 0)
            {
                Dictionary<string, AuthorEntityMatch> creatorMatches =
                    new Dictionary<string, AuthorEntityMatch>(StringComparer.OrdinalIgnoreCase);
                bool creatorAmbiguous = false;
                foreach (string creator in parts.Creators)
                {
                    if (String.IsNullOrWhiteSpace(creator) ||
                        String.Equals(NormalizeIdentity(creator), norm, StringComparison.OrdinalIgnoreCase))
                        continue;
                    AuthorEntityMatch creatorMatch = Resolve(creator);
                    if (creatorMatch.Ambiguous)
                    {
                        creatorAmbiguous = true;
                        continue;
                    }
                    if (!creatorMatch.Found || creatorMatch.Entity == null) continue;
                    string key = (creatorMatch.FromPublicDatabase ? "public:" : "personal:") +
                        creatorMatch.Entity.Id.ToString();
                    creatorMatches[key] = creatorMatch;
                }
                if (creatorMatches.Count == 1 && !creatorAmbiguous)
                    foreach (AuthorEntityMatch only in creatorMatches.Values) return only;

                // A confirmed relationship may disambiguate one creator name;
                // multiple distinct creators remain a multi-author ambiguity.
                if (parts.Creators.Count == 1 && !String.IsNullOrWhiteSpace(parts.Society))
                {
                    HashSet<int> candidates = new HashSet<int>();
                    foreach (string lookup in GetLookupNorms(parts.Creators[0])) {
                        List<int> ids; if (_nameToIds.TryGetValue(lookup, out ids)) foreach (int id in ids) candidates.Add(id);
                    }
                    AuthorEntityMatch group = candidates.Count > 1 ? Resolve(parts.Society, "Group") : new AuthorEntityMatch();
                    if (group.Found && !group.FromPublicDatabase && group.Entity != null)
                    {
                        List<int> linked = candidates.Where(id => _database.AuthorCircles.Any(r => r.AuthorId == id &&
                            r.CircleId == group.Entity.Id && r.UserConfirmed && !r.Disabled)).ToList();
                        if (linked.Count == 1) {
                            int id = linked[0]; return new AuthorEntityMatch { Found = true, Entity = _authorsById[id],
                                IdentityNames = new List<string>(_namesById[id]), RelatedGroups = _related.ContainsKey(id) ? new List<CircleEntityRecord>(_related[id]) : new List<CircleEntityRecord>() };
                        }
                    }
                }

                // Only ambiguous/missing creator evidence pays for the
                // relationship-constrained public DB lookup.
                if (_publicDatabase != null)
                {
                    AuthorEntityMatch structuredPublic = ResolvePublic(name);
                    if (structuredPublic.Found || structuredPublic.Ambiguous)
                        return structuredPublic;
                }

                if (creatorAmbiguous || creatorMatches.Count > 1)
                    return new AuthorEntityMatch { Ambiguous = true };
                return result;
            }

            HashSet<int> matchedIds = new HashSet<int>();
            AuthorNameDecision explicitDecision;
            if (_decisions.TryGetValue(norm, out explicitDecision) && explicitDecision.Status == "Disabled") return result;
            foreach (string lookupNorm in GetLookupNorms(name))
            {
                List<int> ids;
                if (_nameToIds.TryGetValue(lookupNorm, out ids) && ids != null)
                    foreach (int id in ids) matchedIds.Add(id);
            }

            if (matchedIds.Count == 0)
                return ResolvePublic(name);

            if (explicitDecision != null && (explicitDecision.Status == "Pending" || explicitDecision.Status == "Independent")) {
                AuthorEntityMatch publicCandidate = ResolvePublic(name);
                if (publicCandidate.Found || publicCandidate.Ambiguous) return new AuthorEntityMatch { Ambiguous = true };
            }

            List<int> unique = matchedIds.ToList();
            if (explicitDecision != null && explicitDecision.Status == "Confirmed" && unique.Contains(explicitDecision.EntityId))
                unique = new List<int> { explicitDecision.EntityId };
            List<int> confirmed = unique.Where(id => _authorsById[id].UserConfirmed).ToList();
            if (confirmed.Count > 0 && (explicitDecision == null || (explicitDecision.Status != "Independent" && explicitDecision.Status != "Pending"))) unique = confirmed;
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
            List<CircleEntityRecord> related;
            if (_related.TryGetValue(entity.Id, out related)) result.RelatedGroups = new List<CircleEntityRecord>(related);
            List<string> names;
            if (_namesById.TryGetValue(entity.Id, out names) && names != null)
                result.IdentityNames = new List<string>(names);
            return result;
        }

        private void AddName(int authorId, string name)
        {
            if (authorId == 0 || String.IsNullOrWhiteSpace(name)) return;
            HashSet<string> disabled;
            if (_disabledNames.TryGetValue(authorId, out disabled) && disabled.Contains(AuthorRules.NormalizeText(name))) return;
            foreach (string norm in GetLookupNorms(name))
            {
                if (disabled != null && disabled.Contains(norm)) continue;
                List<int> ids;
                if (!_nameToIds.TryGetValue(norm, out ids))
                {
                    ids = new List<int>();
                    _nameToIds[norm] = ids;
                }
                if (!ids.Contains(authorId)) ids.Add(authorId);
            }

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

        private static List<string> GetLookupNorms(string value)
        {
            HashSet<string> norms = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            StructuredAuthorParts parts = AuthorRules.GetStructuredAuthorParts(value);
            IEnumerable<string> names = parts != null && parts.Creators != null && parts.Creators.Count > 0
                ? (IEnumerable<string>)parts.Creators
                : (IEnumerable<string>)new string[] { value ?? "" };
            foreach (string name in names)
                foreach (string norm in AuthorRules.GetNorms(name ?? ""))
                    if (!String.IsNullOrWhiteSpace(norm)) norms.Add(norm);
            string direct = NormalizeIdentity(value);
            if (parts == null && direct.Length > 0) norms.Add(direct);
            return norms.ToList();
        }

        private static string PrettyTag(string value)
        {
            return String.IsNullOrWhiteSpace(value) ? "" : value.Replace('_', ' ').Trim();
        }
    }

    internal sealed partial class AuthorEntityStore
    {
        private readonly string _path;
        private GuiGuiAuthorIndexDatabase _publicDatabase;
        private readonly object _sync = new object();
        private readonly AuthorEntityDatabase _sessionOverlay = new AuthorEntityDatabase();
        private int _nextSessionAuthorId = -1;
        private long _identityRevision;
        private string _recognitionContentHash, _recognitionAliasVersion, _recognitionEntityVersion;
        public long IdentityRevision { get { return System.Threading.Interlocked.Read(ref _identityRevision); } }

        public event Action Changed;

        public AuthorEntityStore(string path, string publicDatabasePath)
        {
            _path = path;
            _publicDatabase = new GuiGuiAuthorIndexDatabase(publicDatabasePath);
        }

        public AuthorEntityStore(string path)
            : this(
                path,
                System.IO.Path.Combine(
                    System.IO.Path.GetDirectoryName(path) ?? AppDomain.CurrentDomain.BaseDirectory,
                    AppFiles.AuthorIndexDatabase))
        {
        }

        public string Path { get { return _path; } }

        public string GetRecognitionVersion(bool aliasesOnly)
        {
            lock (_sync)
            {
                // Check content, not only timestamps: external edits of equal
                // size with a preserved timestamp must still invalidate rules.
                bool exists = File.Exists(_path);
                byte[] bytes = exists ? File.ReadAllBytes(_path) : new byte[0];
                string contentHash;
                using (System.Security.Cryptography.SHA256 hash = System.Security.Cryptography.SHA256.Create())
                    contentHash = exists ? Convert.ToBase64String(hash.ComputeHash(bytes)) : "Missing";
                if (_recognitionContentHash != contentHash)
                {
                    AuthorEntityDatabase db;
                    using (StreamReader reader = new StreamReader(new MemoryStream(bytes), Encoding.UTF8, true))
                        db = exists ? ParseDatabase(reader.ReadToEnd(), _path) : new AuthorEntityDatabase();
                    string aliasVersion = ComputeRecognitionVersion(db, true);
                    string entityVersion = ComputeRecognitionVersion(db, false);
                    _recognitionAliasVersion = aliasVersion;
                    _recognitionEntityVersion = entityVersion;
                    _recognitionContentHash = contentHash;
                }
                return aliasesOnly ? _recognitionAliasVersion : _recognitionEntityVersion;
            }
        }

        private static string ComputeRecognitionVersion(AuthorEntityDatabase db, bool aliasesOnly)
        {
            JavaScriptSerializer serializer = new JavaScriptSerializer { MaxJsonLength = Int32.MaxValue };
            object view = aliasesOnly ? (object)db.LegacyAliasGroups.Select(x => new { x.Id, x.Canonical, x.Names, x.Status }).ToList()
                : (object)new {
                    Authors = db.Authors.Select(x => new { x.Id, x.EntityType, x.CanonicalName, x.RomanName,
                        x.EHArtistTag, x.NHArtistTag, x.DanbooruArtistTag, x.EHTag, x.EHNamespace, x.UserConfirmed, x.Source }).ToList(),
                    Aliases = db.Aliases,
                    Circles = db.Circles.Select(x => new { x.Id, x.CanonicalName, x.RomanName, x.EHGroupTag, x.NHGroupTag }).ToList(),
                    Relations = db.AuthorCircles, CircleAliases = db.CircleAliases,
                    Overrides = db.PublicOverrides, Decisions = db.NameDecisions
                };
            using (System.Security.Cryptography.SHA256 hash = System.Security.Cryptography.SHA256.Create())
                return Convert.ToBase64String(hash.ComputeHash(Encoding.UTF8.GetBytes(serializer.Serialize(view))));
        }

        public void MigrateLegacyAliases(string legacyPath, List<AliasGroup> groups)
        {
            lock (_sync)
            {
                AuthorEntityDatabase db = LoadUnlocked();
                string hash = LegacyHash(legacyPath);
                if (!String.IsNullOrEmpty(db.LegacyAliasHash)) return;
                ValidateDatabase(db);
                if (File.Exists(_path) && !File.Exists(_path + ".pre-alias-migration.bak"))
                    File.Copy(_path, _path + ".pre-alias-migration.bak", false);
                if (File.Exists(legacyPath) && !File.Exists(legacyPath + ".migration.bak"))
                    File.Copy(legacyPath, legacyPath + ".migration.bak", false);
                if (File.Exists(legacyPath))
                    foreach (string line in File.ReadAllLines(legacyPath, Encoding.UTF8))
                        if (!String.IsNullOrWhiteSpace(line) && !line.TrimStart().StartsWith("#") &&
                            line.Split('|').Count(x => !String.IsNullOrWhiteSpace(x)) < 2)
                            db.LegacyUnparsedLines.Add(line);
                SetAliasGroups(db, groups, false);
                db.LegacyAliasHash = hash;
                SaveInternal(db);
            }
            RaiseChanged();
        }

        public bool LegacyAliasesChanged(string legacyPath)
        {
            AuthorEntityDatabase db = Load();
            return !String.IsNullOrEmpty(db.LegacyAliasHash) && db.LegacyAliasHash != LegacyHash(legacyPath);
        }

        private static string LegacyHash(string path)
        {
            if (!File.Exists(path)) return "missing";
            using (System.Security.Cryptography.SHA256 hash = System.Security.Cryptography.SHA256.Create())
                return Convert.ToBase64String(hash.ComputeHash(File.ReadAllBytes(path)));
        }

        private static void SetAliasGroups(AuthorEntityDatabase db, List<AliasGroup> groups, bool userEdit)
        {
            // Keep each legacy group independent. Overlapping names remain ambiguous.
            List<LegacyAliasGroupRecord> records = new List<LegacyAliasGroupRecord>();
            foreach (AliasGroup group in groups ?? new List<AliasGroup>())
            {
                if (group == null || String.IsNullOrWhiteSpace(group.Canonical))
                    throw new InvalidDataException("Alias group requires a canonical name.");
                LegacyAliasGroupRecord previous = db.LegacyAliasGroups.FirstOrDefault(x =>
                    (!String.IsNullOrEmpty(group.EntityGroupId) && x.Id == group.EntityGroupId) ||
                    (x.Canonical == group.Canonical && x.Names.SequenceEqual(group.Names)));
                records.Add(new LegacyAliasGroupRecord {
                    Id = previous == null ? Guid.NewGuid().ToString("N") : previous.Id,
                    Canonical = group.Canonical, Names = new List<string>(group.Names),
                    Status = userEdit && (previous == null || previous.Canonical != group.Canonical || !previous.Names.SequenceEqual(group.Names))
                        ? "Confirmed" : previous == null ? "Pending" : previous.Status,
                    AuthorId = previous == null ? 0 : previous.AuthorId
                });
            }
            foreach (LegacyAliasGroupRecord record in records)
                if (records.Any(other => other != record && other.Names.Any(name =>
                    record.Names.Any(n => AuthorRules.NormalizeText(n) == AuthorRules.NormalizeText(name)))))
                    record.Status = "Conflict";
            db.LegacyAliasGroups = records;
            db.Aliases.RemoveAll(x => (x.Source ?? "").StartsWith("UserAliasGroup:", StringComparison.Ordinal));
            foreach (LegacyAliasGroupRecord record in records)
            {
                // Structured folder rules preserve their established semantics and
                // must not be converted into an Artist named after a group.
                if (AuthorRules.GetStructuredAuthorParts(record.Canonical) != null) continue;
                AuthorEntityRecord author = db.Authors.FirstOrDefault(x => x.Id == record.AuthorId);
                if (author == null)
                {
                    author = new AuthorEntityRecord { Id = NextAuthorId(db), EntityId = "user:" + Guid.NewGuid().ToString("N"),
                        Source = "UserAliasGroup:" + record.Id, EntityType = "Artist" };
                    db.Authors.Add(author);
                    record.AuthorId = author.Id;
                }
                author.CanonicalName = record.Canonical;
                author.VerificationSource = "LegacyAlias";
                if (record.Status == "Confirmed")
                {
                    author.UserConfirmed = true;
                    author.Verified = true;
                    author.VerificationSource = "UserConfirmed";
                }
                author.UpdatedUtc = DateTime.UtcNow.ToString("o");
                foreach (string name in record.Names)
                    AddAlias(db, author.Id, name, "legacy", "UserAliasGroup:" + record.Id);
            }
        }

        public PublicAuthorIndexInfo GetPublicDatabaseInfo()
        {
            string version = PublicDatabaseVersion;
            return _publicDatabase.GetInfo();
        }

        public PublicAuthorIdentityIndexStats PreparePublicDatabaseIndex()
        {
            string version = PublicDatabaseVersion;
            return _publicDatabase.PrepareIdentityIndex();
        }

        public PublicAuthorIdentityIndexStats GetPublicDatabaseIndexStats()
        {
            return _publicDatabase.GetIdentityIndexStats();
        }

        public List<PublicAuthorIndexRow> SearchPublicDatabase(string query, int limit)
        {
            string version = PublicDatabaseVersion;
            return _publicDatabase.Search(query, limit);
        }

        public List<PublicAuthorIndexRow> SearchPublicDatabase(string query, int offset, int limit)
        {
            string version = PublicDatabaseVersion;
            return _publicDatabase.Search(query, offset, limit);
        }

        public long CountPublicDatabaseSearch(string query)
        {
            return _publicDatabase.CountSearch(query);
        }

        public AuthorEntityDatabase Load()
        {
            lock (_sync)
            {
                return LoadUnlocked();
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
                combined.CircleAliases.AddRange(_sessionOverlay.CircleAliases);
                combined.AuthorCircles.AddRange(_sessionOverlay.AuthorCircles);
                combined.LookupCache.AddRange(_sessionOverlay.LookupCache);
            }
            string publicVersion = PublicDatabaseVersion;
            Dictionary<int, PublicEntitySnapshot> snapshots = null;
            if (combined.PublicOverrides.Count > 0)
                try { snapshots = GetPublicSnapshots(); } catch { snapshots = new Dictionary<int, PublicEntitySnapshot>(); }
            return new AuthorEntityIndex(combined, _publicDatabase, snapshots);
        }

        public AuthorEntityDatabase LoadForDisplay()
        {
            AuthorEntityDatabase combined = Load();
            lock (_sync)
            {
                combined.Authors.AddRange(_sessionOverlay.Authors);
                combined.Aliases.AddRange(_sessionOverlay.Aliases);
                combined.Circles.AddRange(_sessionOverlay.Circles);
                combined.CircleAliases.AddRange(_sessionOverlay.CircleAliases);
                combined.AuthorCircles.AddRange(_sessionOverlay.AuthorCircles);
                combined.LookupCache.AddRange(_sessionOverlay.LookupCache);
            }
            return combined;
        }

        public void EnsureExists()
        {
            lock (_sync)
            {
                if (File.Exists(_path)) return;
                SaveInternal(new AuthorEntityDatabase());
            }
        }

        public List<string> GetWorkTitleCandidates(string title)
        {
            AuthorLookupCacheRecord cached = GetFreshLookup("WorkTitle-v1", title, 14);
            if (cached == null || cached.Status != "candidate") return new List<string>();
            try { return new JavaScriptSerializer().Deserialize<List<string>>(cached.Detail) ?? new List<string>(); }
            catch { return new List<string>(); }
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
            if ((!String.IsNullOrWhiteSpace(candidate.EntityType) && !String.Equals(candidate.EntityType, "Artist", StringComparison.OrdinalIgnoreCase)) ||
                (!String.IsNullOrWhiteSpace(candidate.TagNamespace) && !String.Equals(candidate.TagNamespace, "artist", StringComparison.OrdinalIgnoreCase))) return null;

            string evidenceSource = BuildEvidenceSourceLabel(candidate, provider);
            if (evidenceSource.IndexOf("EhTagTranslation", StringComparison.OrdinalIgnoreCase) >= 0) return null;

            lock (_sync)
            {
                AuthorEntityDatabase db = saveCache
                    ? LoadUnlocked()
                    : _sessionOverlay;
                AuthorEntityRecord author = FindAuthorByProviderIdentity(db, evidenceSource, candidate.ExternalId, candidate.TagName);

                if (author != null && (author.UserConfirmed ||
                    ((author.Verified || author.VerificationSource == "EHTagExact" || author.VerificationSource == "EHTitleBridge") &&
                     (author.Source ?? "").IndexOf("E-Hentai", StringComparison.OrdinalIgnoreCase) >= 0 &&
                     evidenceSource.IndexOf("E-Hentai", StringComparison.OrdinalIgnoreCase) < 0)))
                {
                    // Provider cache refreshes never modify a user's confirmed identity.
                    UpsertLookupCache(db, provider, query, "resolved", author.Id, "preserved user-confirmed identity");
                    if (saveCache) SaveInternal(db);
                    RaiseChanged(false);
                    return author;
                }

                if (author == null)
                {
                    author = new AuthorEntityRecord();
                    author.Id = saveCache
                        ? NextAuthorId(db)
                        : _nextSessionAuthorId--;
                    author.EntityId = (saveCache ? "user:" : "session:") + Guid.NewGuid().ToString("N");
                    author.CanonicalName = ChooseCanonicalName(query, candidate);
                    author.RomanName = PrettyTag(candidate.TagName);
                    if (String.Equals(candidate.TagNamespace, "artist", StringComparison.OrdinalIgnoreCase) &&
                        candidate.EvidenceSources.Any(delegate(string x) { return x.StartsWith("E-Hentai/", StringComparison.OrdinalIgnoreCase); }))
                        author.EHArtistTag = candidate.TagName ?? "";
                    if (candidate.EvidenceSources.Any(delegate(string x) { return x.StartsWith("nHentai/", StringComparison.OrdinalIgnoreCase); }))
                        author.NHArtistTag = candidate.TagName ?? "";
                    author.EntityType = candidate.EntityType ?? "Artist";
                    author.VerificationSource = candidate.EvidenceSources.Any(delegate(string x) { return x.StartsWith("E-Hentai/TitleSearch", StringComparison.OrdinalIgnoreCase); })
                        ? "EHTitleBridge"
                        : "EHTagExact";
                    author.EHNamespace = candidate.TagNamespace ?? "artist";
                    author.EHTag = candidate.TagName ?? "";
                    author.Source = evidenceSource;
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
                    if (String.Equals(candidate.TagNamespace, "artist", StringComparison.OrdinalIgnoreCase) &&
                        String.IsNullOrWhiteSpace(author.EHArtistTag) && candidate.EvidenceSources.Any(delegate(string x) { return x.StartsWith("E-Hentai/", StringComparison.OrdinalIgnoreCase); }))
                        author.EHArtistTag = candidate.TagName ?? "";
                    if (String.IsNullOrWhiteSpace(author.NHArtistTag) && candidate.EvidenceSources.Any(delegate(string x) { return x.StartsWith("nHentai/", StringComparison.OrdinalIgnoreCase); }))
                        author.NHArtistTag = candidate.TagName ?? "";
                    if (!String.IsNullOrWhiteSpace(evidenceSource))
                        author.Source = evidenceSource;
                    if (!String.IsNullOrWhiteSpace(candidate.EntityType)) author.EntityType = candidate.EntityType;
                    if (!String.IsNullOrWhiteSpace(candidate.TagNamespace)) author.EHNamespace = candidate.TagNamespace;
                    if (!String.IsNullOrWhiteSpace(candidate.TagName)) author.EHTag = candidate.TagName;
                    author.VerificationSource = candidate.EvidenceSources.Any(delegate(string x) { return x.StartsWith("E-Hentai/TitleSearch", StringComparison.OrdinalIgnoreCase); })
                        ? "EHTitleBridge"
                        : "EHTagExact";
                    author.UpdatedUtc = DateTime.UtcNow.ToString("o");
                }

                AddAlias(db, author.Id, query, "query", evidenceSource);
                AddAlias(db, author.Id, candidate.TagName, "provider_tag", evidenceSource);
                AddAlias(db, author.Id, PrettyTag(candidate.TagName), "roman", evidenceSource);
                foreach (string alias in candidate.OtherNames ?? new List<string>())
                {
                    AddAlias(db, author.Id, alias, "other_name", evidenceSource);
                    AddAlias(db, author.Id, PrettyTag(alias), "other_name", evidenceSource);
                }

                if (!String.IsNullOrWhiteSpace(candidate.GroupName))
                {
                    CircleEntityRecord circle = FindOrCreateCircle(db, provider, candidate.GroupName, !saveCache);
                    if (circle != null)
                        AddAuthorCircle(db, author.Id, circle.Id, provider);
                }

                UpsertLookupCache(db, provider, query, "resolved", author.Id, "unique exact artist identity");

                if (saveCache)
                    SaveInternal(db);
                RaiseChanged();
                return author;
            }
        }

        public AuthorEntityRecord MergeResolvedLocalReference(
            string query,
            AuthorReferenceIdentity identity,
            bool saveCache)
        {
            if (identity == null || String.IsNullOrWhiteSpace(identity.Tag)) return null;
            lock (_sync)
            {
                AuthorEntityDatabase db = saveCache ? LoadUnlocked() : _sessionOverlay;
                AuthorEntityRecord author = MergeResolvedLocalReferenceInto(db, query, identity, saveCache);
                if (saveCache) SaveInternal(db);
                RaiseChanged();
                return author;
            }
        }

        public int MergeResolvedLocalReferences(
            IDictionary<string, AuthorReferenceIdentity> matches,
            bool saveCache)
        {
            if (matches == null || matches.Count == 0) return 0;
            lock (_sync)
            {
                AuthorEntityDatabase db = saveCache ? LoadUnlocked() : _sessionOverlay;
                int merged = 0;
                foreach (KeyValuePair<string, AuthorReferenceIdentity> pair in matches)
                {
                    if (MergeResolvedLocalReferenceInto(db, pair.Key, pair.Value, saveCache) != null)
                        merged++;
                }
                // Persist the complete local batch once. Previously every author
                // reparsed and rewrote AuthorEntities.json separately.
                if (saveCache && merged > 0) SaveInternal(db);
                if (merged > 0) RaiseChanged();
                return merged;
            }
        }

        private AuthorEntityRecord MergeResolvedLocalReferenceInto(
            AuthorEntityDatabase db,
            string query,
            AuthorReferenceIdentity identity,
            bool saveCache)
        {
            if (db == null || identity == null || String.IsNullOrWhiteSpace(identity.Tag)) return null;
            string localSource = identity.Source ?? "EhTagTranslation";
            if (localSource.IndexOf("EhTagTranslation", StringComparison.OrdinalIgnoreCase) >= 0 ||
                !String.Equals(identity.Namespace, "artist", StringComparison.OrdinalIgnoreCase)) return null;
            string externalId = (identity.Namespace ?? "") + ":" + identity.Tag;
            AuthorEntityRecord author = db.Authors.FirstOrDefault(delegate(AuthorEntityRecord x)
            {
                return x != null &&
                    (String.Equals(x.ExternalId ?? "", externalId, StringComparison.OrdinalIgnoreCase) ||
                     (String.Equals(x.EHNamespace ?? "", identity.Namespace ?? "", StringComparison.OrdinalIgnoreCase) &&
                      String.Equals(x.EHTag ?? "", identity.Tag ?? "", StringComparison.OrdinalIgnoreCase)));
            });
            if (author != null && author.UserConfirmed) return author;
            if (author == null)
            {
                author = new AuthorEntityRecord();
                author.Id = saveCache ? NextAuthorId(db) : _nextSessionAuthorId--;
                author.CanonicalName = ContainsCjk(query) ? (query ?? "").Trim() : PrettyTag(identity.Tag);
                author.RomanName = PrettyTag(identity.Tag);
                author.Source = localSource;
                author.ExternalId = externalId;
                author.EntityType = String.Equals(identity.Namespace, "group", StringComparison.OrdinalIgnoreCase) ? "Group" : "Artist";
                author.VerificationSource = "LocalDatabase";
                author.EHNamespace = identity.Namespace ?? "";
                author.EHTag = identity.Tag ?? "";
                if (String.Equals(identity.Namespace, "artist", StringComparison.OrdinalIgnoreCase)) author.EHArtistTag = identity.Tag ?? "";
                author.Verified = true;
                db.Authors.Add(author);
            }
            author.UpdatedUtc = DateTime.UtcNow.ToString("o");
            AddAlias(db, author.Id, query, "query", localSource);
            AddAlias(db, author.Id, identity.DisplayName, "reference_name", localSource);
            AddAlias(db, author.Id, identity.Tag, "provider_tag", localSource);
            UpsertLookupCache(db, "LocalReference", query, "resolved", author.Id,
                "namespace=" + identity.Namespace + "; tag=" + identity.Tag + "; verification=LocalDatabase");
            return author;
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
                RaiseChanged(false);
            }
        }

        private void RaiseChanged(bool identityChanged = true)
        {
            if (identityChanged) System.Threading.Interlocked.Increment(ref _identityRevision);
            Action handler = Changed;
            if (handler != null) handler();
        }

        private AuthorEntityDatabase LoadUnlocked()
        {
            if (!File.Exists(_path)) return new AuthorEntityDatabase();
            return ReadDatabase(_path);
        }

        private static AuthorEntityDatabase ReadDatabase(string path)
        {
            return ParseDatabase(File.ReadAllText(path, Encoding.UTF8), path);
        }

        private static AuthorEntityDatabase ParseDatabase(string text, string path)
        {
            JavaScriptSerializer serializer = new JavaScriptSerializer { MaxJsonLength = Int32.MaxValue };
            AuthorEntityDatabase db = serializer.Deserialize<AuthorEntityDatabase>(text);
            if (db == null) throw new InvalidDataException("Author entity database is null: " + path);
            if (db.SchemaVersion > 3) throw new InvalidDataException("Unsupported author entity schema: " + db.SchemaVersion);
            db = NormalizeDatabase(db);
            ValidateDatabase(db);
            return db;
        }

        private void SaveInternal(AuthorEntityDatabase db)
        {
            db = NormalizeDatabase(db);
            ValidateDatabase(db);
            db.DataRevision++;
            string temp = _path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                string dir = System.IO.Path.GetDirectoryName(_path);
                if (!String.IsNullOrWhiteSpace(dir)) Directory.CreateDirectory(dir);
                JavaScriptSerializer serializer = new JavaScriptSerializer();
                serializer.MaxJsonLength = Int32.MaxValue;
                string json = serializer.Serialize(NormalizeDatabase(db));
                using (FileStream stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    byte[] bytes = new UTF8Encoding(false).GetBytes(json);
                    stream.Write(bytes, 0, bytes.Length);
                    stream.Flush(true);
                }
                ValidateDatabase(ReadDatabase(temp));

                if (File.Exists(_path))
                {
                    string backup = _path + ".bak";
                    File.Replace(temp, _path, backup, true);
                    return;
                }

                File.Move(temp, _path);
            }
            finally
            {
                if (File.Exists(temp)) File.Delete(temp);
            }
        }

        private static void ValidateDatabase(AuthorEntityDatabase db)
        {
            HashSet<int> authors = new HashSet<int>();
            foreach (AuthorEntityRecord author in db.Authors)
                if (author == null || author.Id <= 0 || !authors.Add(author.Id))
                    throw new InvalidDataException("Invalid or duplicate author ID.");
            HashSet<int> circles = new HashSet<int>();
            foreach (CircleEntityRecord circle in db.Circles)
                if (circle == null || circle.Id <= 0 || !circles.Add(circle.Id))
                    throw new InvalidDataException("Invalid or duplicate group ID.");
            if (db.Aliases.Any(x => x == null || !authors.Contains(x.AuthorId)) ||
                db.AuthorCircles.Any(x => x == null || !authors.Contains(x.AuthorId) || !circles.Contains(x.CircleId)) ||
                db.CircleAliases.Any(x => x == null || !circles.Contains(x.CircleId)))
                throw new InvalidDataException("Dangling entity relationship.");
            if (db.NameDecisions.Any(x => x == null || (x.Status == "Confirmed" &&
                !(x.EntityType == "Artist" ? authors.Contains(x.EntityId) : circles.Contains(x.EntityId)))))
                throw new InvalidDataException("Dangling conflict decision.");
            if (db.PublicOverrides.Any(x => x == null || String.IsNullOrWhiteSpace(x.Id) || x.AddedAliases == null ||
                x.DisabledNames == null || x.OriginalNames == null || x.UserGroups == null ||
                x.UserGroups.Any(r => r == null || !circles.Contains(r.CircleId))) || db.PublicOverrides.GroupBy(x => x.Id).Any(x => x.Count() > 1))
                throw new InvalidDataException("Invalid public override.");
            HashSet<string> groupIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (LegacyAliasGroupRecord group in db.LegacyAliasGroups)
                if (group == null || String.IsNullOrWhiteSpace(group.Id) || !groupIds.Add(group.Id) ||
                    String.IsNullOrWhiteSpace(group.Canonical) || group.Names == null ||
                    group.Names.Any(String.IsNullOrWhiteSpace) ||
                    (group.AuthorId > 0 && !authors.Contains(group.AuthorId)))
                    throw new InvalidDataException("Invalid migrated alias group or entity reference.");
        }

        private static AuthorEntityDatabase NormalizeDatabase(AuthorEntityDatabase db)
        {
            if (db == null) db = new AuthorEntityDatabase();
            if (db.Authors == null) db.Authors = new List<AuthorEntityRecord>();
            if (db.Aliases == null) db.Aliases = new List<AuthorAliasRecord>();
            if (db.Circles == null) db.Circles = new List<CircleEntityRecord>();
            if (db.AuthorCircles == null) db.AuthorCircles = new List<AuthorCircleRecord>();
            if (db.LookupCache == null) db.LookupCache = new List<AuthorLookupCacheRecord>();
            if (db.CircleAliases == null) db.CircleAliases = new List<CircleAliasRecord>();
            if (db.PublicOverrides == null) db.PublicOverrides = new List<PublicEntityOverride>();
            if (db.NameDecisions == null) db.NameDecisions = new List<AuthorNameDecision>();
            db.SchemaVersion = 3;
            foreach (CircleEntityRecord circle in db.Circles)
            {
                if (circle == null) continue;
                if (String.IsNullOrEmpty(circle.EntityId)) circle.EntityId = "group:" + circle.Id;
                if (circle.ProviderIdentities == null) circle.ProviderIdentities = new List<string>();
            }
            if (db.LegacyAliasGroups == null) db.LegacyAliasGroups = new List<LegacyAliasGroupRecord>();
            if (db.LegacyUnparsedLines == null) db.LegacyUnparsedLines = new List<string>();
            foreach (AuthorEntityRecord author in db.Authors)
            {
                if (author == null) continue;
                if (author.ProviderIdentities == null) author.ProviderIdentities = new List<string>();
                if (String.IsNullOrWhiteSpace(author.EntityId)) author.EntityId = "user:" + author.Id;
                if (String.IsNullOrWhiteSpace(author.EntityType)) author.EntityType = "Artist";
                if (String.IsNullOrWhiteSpace(author.VerificationSource))
                    author.VerificationSource = author.Verified ? "LegacyVerified" : "Unverified";
                if (String.IsNullOrWhiteSpace(author.EHNamespace) && !String.IsNullOrWhiteSpace(author.EHArtistTag))
                    author.EHNamespace = "artist";
                if (String.IsNullOrWhiteSpace(author.EHTag) && !String.IsNullOrWhiteSpace(author.EHArtistTag))
                    author.EHTag = author.EHArtistTag;
            }
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
            List<AuthorEntityRecord> hits = new List<AuthorEntityRecord>();
            bool eh = (provider ?? "").IndexOf("E-Hentai", StringComparison.OrdinalIgnoreCase) >= 0;
            bool nh = !eh && (provider ?? "").IndexOf("nHentai", StringComparison.OrdinalIgnoreCase) >= 0;
            foreach (AuthorEntityRecord author in db.Authors)
            {
                if (author == null || author.EntityType != "Artist") continue;
                if (!String.IsNullOrWhiteSpace(externalId) &&
                    String.Equals(author.ExternalId ?? "", externalId, StringComparison.OrdinalIgnoreCase) &&
                    String.Equals(author.Source ?? "", provider ?? "", StringComparison.OrdinalIgnoreCase))
                { hits.Add(author); continue; }

                if (!String.IsNullOrWhiteSpace(tagName) &&
                    ((eh && String.Equals(author.EHArtistTag ?? "", tagName, StringComparison.OrdinalIgnoreCase)) ||
                     (nh && String.Equals(author.NHArtistTag ?? "", tagName, StringComparison.OrdinalIgnoreCase)) ||
                     (String.Equals(author.Source ?? "", provider ?? "", StringComparison.OrdinalIgnoreCase) &&
                      author.EHNamespace != "group" && String.Equals(author.EHTag ?? "", tagName, StringComparison.OrdinalIgnoreCase))))
                    hits.Add(author);
            }
            return hits.Count == 1 ? hits[0] : null;
        }

        private static void AddAlias(AuthorEntityDatabase db, int authorId, string alias, string type, string source)
        {
            if (authorId == 0 || String.IsNullOrWhiteSpace(alias)) return;
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

        private static CircleEntityRecord FindOrCreateCircle(AuthorEntityDatabase db, string provider, string groupName, bool transient)
        {
            string norm = AuthorRules.NormalizeText(groupName ?? "");
            if (norm.Length == 0) return null;

            CircleEntityRecord found = db.Circles.FirstOrDefault(delegate(CircleEntityRecord x)
            {
                if (x == null) return false;
                return String.Equals(AuthorRules.NormalizeText(x.CanonicalName ?? ""), norm, StringComparison.OrdinalIgnoreCase) ||
                       String.Equals(AuthorRules.NormalizeText(x.EHGroupTag ?? ""), norm, StringComparison.OrdinalIgnoreCase) ||
                       String.Equals(AuthorRules.NormalizeText(x.NHGroupTag ?? ""), norm, StringComparison.OrdinalIgnoreCase);
            });
            if (found != null) return found;

            CircleEntityRecord circle = new CircleEntityRecord();
            circle.Id = transient ? (db.Circles.Count == 0 ? -1 : db.Circles.Min(x => x.Id) - 1) : NextCircleId(db);
            circle.EntityId = (transient ? "session-group:" : "group:") + Guid.NewGuid().ToString("N");
            circle.CanonicalName = PrettyTag(groupName);
            circle.RomanName = PrettyTag(groupName);
            circle.NHGroupTag = String.Equals(provider, "nHentai", StringComparison.OrdinalIgnoreCase) ||
                String.Equals(provider, "EvidenceChain", StringComparison.OrdinalIgnoreCase) ? groupName : "";
            circle.Source = provider ?? "";
            circle.UpdatedUtc = DateTime.UtcNow.ToString("o");
            db.Circles.Add(circle);
            return circle;
        }

        private static void AddAuthorCircle(AuthorEntityDatabase db, int authorId, int circleId, string source)
        {
            if (authorId == 0 || circleId == 0) return;
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

        private static string BuildEvidenceSourceLabel(AuthorProviderCandidate candidate, string fallback)
        {
            bool eh = candidate != null && (candidate.EvidenceSources ?? new List<string>())
                .Any(delegate(string x) { return (x ?? "").StartsWith("E-Hentai/", StringComparison.OrdinalIgnoreCase); });
            bool nh = candidate != null && (candidate.EvidenceSources ?? new List<string>())
                .Any(delegate(string x) { return (x ?? "").StartsWith("nHentai/", StringComparison.OrdinalIgnoreCase); });
            bool reference = candidate != null && (candidate.EvidenceSources ?? new List<string>())
                .Any(delegate(string x) { return (x ?? "").StartsWith("EhTagTranslation/", StringComparison.OrdinalIgnoreCase); });
            bool aggregate = candidate != null && (candidate.EvidenceSources ?? new List<string>())
                .Any(delegate(string x) { return (x ?? "").StartsWith("EhTagDb/", StringComparison.OrdinalIgnoreCase); });
            List<string> sources = new List<string>();
            if (reference) sources.Add("EhTagTranslation");
            if (aggregate) sources.Add("EhTagDb");
            if (eh) sources.Add("E-Hentai");
            if (nh) sources.Add("nHentai");
            if (sources.Count > 0) return String.Join(" + ", sources.ToArray());
            return fallback ?? "";
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
