using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;

namespace MangaAuthorSorter
{
    internal sealed class PublicAuthorIndexInfo
    {
        public bool Exists;
        public string Path = "";
        public long Artists;
        public long Groups;
        public long Relations;
        public long NonAsciiEntityNames;
        public long NonAsciiAliases;
        public string Error = "";
    }

    internal sealed class PublicAuthorIndexRow
    {
        public int Id;
        public string EntityType = "";
        public string CanonicalName = "";
        public string RomanName = "";
        public string Aliases = "";
        public string ProviderTags = "";
        public string RelatedGroups = "";
        public string Source = "";
        public string VerificationSource = "";
    }


    internal sealed class PublicAuthorIdentityIndexStats
    {
        public bool Ready;
        public int Artists;
        public int LookupKeys;
        public int IdentityNames;
        public int Relations;
        public long BuildMs;
    }

    // Thin, read-only adapter over the public author index. Windows ships the
    // SQLite runtime as winsqlite3.dll, so GuiGui can remain a single EXE and
    // does not need to deploy or maintain a separate SQLite provider.
    internal sealed class GuiGuiAuthorIndexDatabase : IDisposable
    {
        private const int SqliteOk = 0;
        private const int SqliteRow = 100;
        private const int SqliteDone = 101;
        private const int OpenReadOnly = 0x00000001;

        private sealed class PublicAuthorIdentityIndex
        {
            public readonly Dictionary<string, int[]> NameToArtistIds =
                new Dictionary<string, int[]>(StringComparer.OrdinalIgnoreCase);
            public readonly Dictionary<int, AuthorEntityRecord> Artists =
                new Dictionary<int, AuthorEntityRecord>();
            public readonly Dictionary<int, List<string>> IdentityNames =
                new Dictionary<int, List<string>>();
            public readonly Dictionary<int, List<CircleEntityRecord>> RelatedGroups =
                new Dictionary<int, List<CircleEntityRecord>>();
            public readonly PublicAuthorIdentityIndexStats Stats = new PublicAuthorIdentityIndexStats();
        }

        private readonly string _path;
        private readonly object _sync = new object();
        private readonly object _cacheGate = new object();
        private readonly object _identityIndexGate = new object();
        private IntPtr _resolverDb = IntPtr.Zero;
        private volatile PublicAuthorIdentityIndex _identityIndex;
        private volatile bool _identityIndexDisabled;
        private AuthorEntityIndex _groupIndex;
        private readonly Dictionary<string, AuthorEntityMatch> _cache =
            new Dictionary<string, AuthorEntityMatch>(StringComparer.OrdinalIgnoreCase);

        public GuiGuiAuthorIndexDatabase(string path)
        {
            _path = path ?? "";
        }

        public string PathName { get { return _path; } }

        public AuthorEntityMatch ResolveGroup(string name)
        {
            lock (_sync)
            {
                if (_groupIndex == null)
                {
                    AuthorEntityDatabase data = new AuthorEntityDatabase();
                    foreach (PublicEntitySnapshot row in LoadSnapshots("").Values.Where(x => x.EntityType == "Group"))
                    {
                        data.Circles.Add(new CircleEntityRecord { Id = row.Id, CanonicalName = row.CanonicalName, RomanName = row.RomanName, EntityId = row.StableKey });
                        foreach (string alias in row.Aliases.Concat(row.LookupNames.SelectMany(x => new[] { x.Value, x.NormalizedName })))
                            data.CircleAliases.Add(new CircleAliasRecord { CircleId = row.Id, Alias = alias });
                    }
                    _groupIndex = new AuthorEntityIndex(data, null);
                }
                AuthorEntityMatch match = _groupIndex.Resolve(name, "Group"); match.FromPublicDatabase = true; return match;
            }
        }

        public void Dispose()
        {
            lock (_sync)
            {
                if (_resolverDb != IntPtr.Zero) Native.sqlite3_close(_resolverDb);
                _resolverDb = IntPtr.Zero;
            }
        }

        public Dictionary<int, PublicEntitySnapshot> LoadSnapshots(string version)
        {
            Dictionary<int, PublicEntitySnapshot> rows = new Dictionary<int, PublicEntitySnapshot>();
            IntPtr db = IntPtr.Zero;
            if (!File.Exists(_path)) return rows;
            if (Native.sqlite3_open_v2(Utf8Z(_path), out db, OpenReadOnly, IntPtr.Zero) != SqliteOk || db == IntPtr.Zero)
            {
                if (db != IntPtr.Zero) Native.sqlite3_close(db);
                throw new InvalidOperationException("Unable to read public author database.");
            }
            try
            {
                HashSet<string> columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                WithStatement(db, "PRAGMA table_info(Entity)", stmt => {
                    while (Native.sqlite3_step(stmt) == SqliteRow) columns.Add(Text(stmt, 1)); });
                bool stable = columns.Contains("StableEntityKey");
                WithStatement(db, "SELECT Id,EntityType,CanonicalName,RomanName," + (stable ? "StableEntityKey" : "''") + ",Source,NormalizedCanonical," + OptionalDanbooruColumn(db, "") + " FROM Entity", stmt => {
                    while (Native.sqlite3_step(stmt) == SqliteRow)
                    {
                        PublicEntitySnapshot row = new PublicEntitySnapshot { Id = Native.sqlite3_column_int(stmt, 0),
                            EntityType = Text(stmt, 1), CanonicalName = Text(stmt, 2), RomanName = Text(stmt, 3),
                            StableKey = String.IsNullOrWhiteSpace(Text(stmt, 4)) ? "" : "stable:" + Text(stmt, 4), DatabaseVersion = version, Source = Text(stmt, 5) };
                        rows[row.Id] = row;
                        row.LookupNames.Add(new PublicNameLookup { Value = row.CanonicalName, NormalizedName = Text(stmt, 6) });
                        if (!String.IsNullOrWhiteSpace(Text(stmt, 7))) { AddUnique(row.Aliases, Text(stmt, 7)); row.LookupNames.Add(new PublicNameLookup { Value = Text(stmt, 7), NormalizedName = AuthorRules.NormalizeText(PrettyTag(Text(stmt, 7))) }); }
                    }
                });
                WithStatement(db, "SELECT EntityId,Alias,NormalizedAlias FROM EntityAlias", stmt => {
                    while (Native.sqlite3_step(stmt) == SqliteRow) {
                        PublicEntitySnapshot row;
                        if (rows.TryGetValue(Native.sqlite3_column_int(stmt, 0), out row)) {
                            AddUnique(row.Aliases, Text(stmt, 1));
                            row.LookupNames.Add(new PublicNameLookup { Value = Text(stmt, 1), NormalizedName = Text(stmt, 2) });
                        }
                    }
                });
                columns.Clear();
                WithStatement(db, "PRAGMA table_info(ProviderIdentity)", stmt => {
                    while (Native.sqlite3_step(stmt) == SqliteRow) columns.Add(Text(stmt, 1)); });
                bool externalId = columns.Contains("ExternalId");
                Dictionary<int, List<string>> providerKeys = new Dictionary<int, List<string>>();
                WithStatement(db, "SELECT EntityId,Provider,Namespace,Tag,NormalizedTag," + (externalId ? "ExternalId" : "''") + " FROM ProviderIdentity", stmt => {
                    while (Native.sqlite3_step(stmt) == SqliteRow) {
                        int id = Native.sqlite3_column_int(stmt, 0); PublicEntitySnapshot row;
                        if (!rows.TryGetValue(id, out row)) continue;
                        AddUnique(row.Identities, Text(stmt, 1) + ":" + Text(stmt, 2) + ":" + Text(stmt, 3));
                        if (Text(stmt, 2) != (row.EntityType == "Artist" ? "artist" : "group")) continue;
                        row.LookupNames.Add(new PublicNameLookup { Value = Text(stmt, 3), NormalizedName = Text(stmt, 4) });
                        if (!String.IsNullOrWhiteSpace(Text(stmt, 5))) {
                            List<string> keys;
                            if (!providerKeys.TryGetValue(id, out keys)) { keys = new List<string>(); providerKeys[id] = keys; }
                            keys.Add("provider:" + Text(stmt, 1) + ":" + Text(stmt, 2) + ":" + Text(stmt, 5));
                        }
                    }
                });
                foreach (var pair in providerKeys)
                    if (String.IsNullOrEmpty(rows[pair.Key].StableKey)) rows[pair.Key].StableKey = pair.Value.OrderBy(x => x, StringComparer.Ordinal).First();
                WithStatement(db, "SELECT ArtistId,GroupId FROM ArtistGroup", stmt => {
                    while (Native.sqlite3_step(stmt) == SqliteRow) {
                        PublicEntitySnapshot row;
                        if (rows.TryGetValue(Native.sqlite3_column_int(stmt, 0), out row)) row.RelatedGroups.Add(Native.sqlite3_column_int(stmt, 1));
                    }
                });
                return rows;
            }
            finally { Native.sqlite3_close(db); }
        }

        public AuthorEntityMatch Resolve(string name)
        {
            string norm = AuthorRules.NormalizeText(name ?? "");
            if (norm.Length == 0) return new AuthorEntityMatch();
            // Once the immutable identity index is ready, ordinary lookups must
            // not touch the filesystem at all. This matters especially when the
            // DB sits under a sync/antivirus filtered directory.
            if (_identityIndex == null && !File.Exists(_path)) return new AuthorEntityMatch();

            AuthorEntityMatch cached;
            lock (_cacheGate)
                if (_cache.TryGetValue(norm, out cached)) return cached;

            AuthorEntityMatch result = new AuthorEntityMatch();
            try
            {
                StructuredAuthorParts structured = AuthorRules.GetStructuredAuthorParts(name);
                if (structured != null)
                {
                    // Structured creator/group relationships are comparatively rare.
                    // Keep the proven relational query for those cases, but do not
                    // serialize ordinary author-name lookups behind the SQLite lock.
                    lock (_sync)
                    {
                        AuthorEntityMatch constrained = QueryStructured(structured);
                        if (constrained.Found || constrained.Ambiguous)
                        {
                            lock (_cacheGate) _cache[norm] = constrained;
                            return constrained;
                        }
                    }
                }

                PublicAuthorIdentityIndex index = EnsureIdentityIndex();
                Dictionary<int, AuthorEntityMatch> matches = new Dictionary<int, AuthorEntityMatch>();
                HashSet<string> lookupNorms = new HashSet<string>(AuthorRules.GetNorms(name ?? ""), StringComparer.OrdinalIgnoreCase);
                lookupNorms.Add(norm);
                foreach (string lookupNorm in lookupNorms)
                {
                    if (String.IsNullOrWhiteSpace(lookupNorm)) continue;
                    AuthorEntityMatch candidate;
                    if (index != null)
                    {
                        candidate = QueryIndexed(index, lookupNorm);
                    }
                    else
                    {
                        // Compatibility fallback for an older/incompatible public
                        // DB schema: keep the proven per-name SQL resolver instead
                        // of turning a failed preload into a false not-found result.
                        lock (_sync) candidate = QuerySql(lookupNorm);
                    }
                    if (candidate.Ambiguous) { result = candidate; break; }
                    if (candidate.Found && candidate.Entity != null)
                        matches[candidate.Entity.Id] = candidate;
                }
                if (!result.Ambiguous)
                {
                    if (matches.Count == 1)
                        foreach (AuthorEntityMatch only in matches.Values) result = only;
                    else if (matches.Count > 1)
                        result = new AuthorEntityMatch { Ambiguous = true, FromPublicDatabase = true };
                }
            }
            catch (DllNotFoundException) { result = new AuthorEntityMatch(); }
            catch (EntryPointNotFoundException) { result = new AuthorEntityMatch(); }
            catch (BadImageFormatException) { result = new AuthorEntityMatch(); }
            catch { result = new AuthorEntityMatch(); }

            lock (_cacheGate) _cache[norm] = result;
            return result;
        }

        public PublicAuthorIdentityIndexStats PrepareIdentityIndex()
        {
            PublicAuthorIdentityIndex index = EnsureIdentityIndex();
            return CopyStats(index != null ? index.Stats : null);
        }

        public PublicAuthorIdentityIndexStats GetIdentityIndexStats()
        {
            return CopyStats(_identityIndex != null ? _identityIndex.Stats : null);
        }

        public PublicAuthorIndexInfo GetInfo()
        {
            PublicAuthorIndexInfo info = new PublicAuthorIndexInfo { Path = _path, Exists = File.Exists(_path) };
            if (!info.Exists) return info;
            IntPtr db = IntPtr.Zero;
            try
            {
                if (Native.sqlite3_open_v2(Utf8Z(_path), out db, OpenReadOnly, IntPtr.Zero) != SqliteOk || db == IntPtr.Zero)
                    throw new InvalidOperationException("无法以只读方式打开数据库");
                info.Artists = Scalar(db, "SELECT COUNT(*) FROM Entity WHERE EntityType='Artist'");
                info.Groups = Scalar(db, "SELECT COUNT(*) FROM Entity WHERE EntityType='Group'");
                info.Relations = Scalar(db, "SELECT COUNT(*) FROM ArtistGroup");
                info.NonAsciiEntityNames = Scalar(db, "SELECT COUNT(*) FROM Entity WHERE length(CAST(CanonicalName AS BLOB))>length(CanonicalName)");
                info.NonAsciiAliases = Scalar(db, "SELECT COUNT(*) FROM EntityAlias WHERE length(CAST(Alias AS BLOB))>length(Alias)");
            }
            catch (Exception ex) { info.Error = ex.Message; }
            finally { if (db != IntPtr.Zero) Native.sqlite3_close(db); }
            return info;
        }

        public List<PublicAuthorIndexRow> Search(string query, int limit)
        {
            return Search(query, 0, limit);
        }

        public List<SimulationAuthorSeed> LoadSimulationAuthors(int count, int seed)
        {
            List<SimulationAuthorSeed> result = new List<SimulationAuthorSeed>();
            if (!File.Exists(_path) || count <= 0) return result;

            count = Math.Max(1, Math.Min(100000, count));
            long positiveSeed = seed == Int32.MinValue ? Int32.MaxValue : Math.Abs((long)seed);
            IntPtr db = IntPtr.Zero;
            try
            {
                if (Native.sqlite3_open_v2(Utf8Z(_path), out db, OpenReadOnly, IntPtr.Zero) != SqliteOk || db == IntPtr.Zero)
                    throw new InvalidOperationException("无法以只读方式打开 GuiGuiAuthorIndex.db");

                // Keep the sample deterministic so two GuiGui versions can be
                // benchmarked against exactly the same author set. The arithmetic
                // sort gives a stable pseudo-shuffle without modifying the DB.
                string sql =
                    "SELECT e.Id,e.CanonicalName,e.RomanName " +
                    "FROM Entity e WHERE e.EntityType='Artist' AND e.CanonicalName<>'' " +
                    "ORDER BY (((CAST(e.Id AS INTEGER) * 1103515245) + " + positiveSeed.ToString(CultureInfo.InvariantCulture) + ") % 2147483647),e.Id " +
                    "LIMIT " + count.ToString(CultureInfo.InvariantCulture);

                WithStatement(db, sql, delegate(IntPtr stmt)
                {
                    while (Native.sqlite3_step(stmt) == SqliteRow)
                    {
                        SimulationAuthorSeed item = new SimulationAuthorSeed();
                        item.Id = Native.sqlite3_column_int(stmt, 0);
                        item.CanonicalName = Text(stmt, 1);
                        item.RomanName = Text(stmt, 2);
                        if (!String.IsNullOrWhiteSpace(item.CanonicalName)) result.Add(item);
                    }
                });
                // Fetch aliases once for the selected IDs. The previous
                // correlated query scanned the alias table during sample sorting.
                if (result.Count > 0)
                {
                    Dictionary<int, SimulationAuthorSeed> selected = result.ToDictionary(x => x.Id);
                    WithStatement(db, "SELECT EntityId,Alias FROM EntityAlias WHERE Alias<>'' AND EntityId IN (" +
                        String.Join(",", selected.Keys.Select(x => x.ToString(CultureInfo.InvariantCulture)).ToArray()) + ") ORDER BY EntityId,AliasType,Alias", delegate(IntPtr stmt)
                    {
                        while (Native.sqlite3_step(stmt) == SqliteRow) {
                            SimulationAuthorSeed item;
                            if (selected.TryGetValue(Native.sqlite3_column_int(stmt, 0), out item) && item.Alias.Length == 0) item.Alias = Text(stmt, 1);
                        }
                    });
                }
                return result;
            }
            finally
            {
                if (db != IntPtr.Zero) Native.sqlite3_close(db);
            }
        }

        public long CountSearch(string query)
        {
            if (!File.Exists(_path)) return 0;
            string norm = AuthorRules.NormalizeText(query ?? "");
            IntPtr db = IntPtr.Zero;
            if (Native.sqlite3_open_v2(Utf8Z(_path), out db, OpenReadOnly, IntPtr.Zero) != SqliteOk || db == IntPtr.Zero)
            {
                if (db != IntPtr.Zero) Native.sqlite3_close(db);
                return 0;
            }
            try
            {
                if (norm.Length == 0) return Scalar(db, "SELECT COUNT(*) FROM Entity");
                long value = 0;
                const string sql =
                    "WITH matched(Id) AS (" +
                    "SELECT Id FROM Entity WHERE NormalizedCanonical LIKE ?1 " +
                    "UNION SELECT EntityId FROM EntityAlias WHERE NormalizedAlias LIKE ?1 " +
                    "UNION SELECT EntityId FROM ProviderIdentity WHERE NormalizedTag LIKE ?1) " +
                    "SELECT COUNT(*) FROM matched";
                WithStatement(db, sql, "%" + norm + "%", delegate(IntPtr stmt)
                {
                    if (Native.sqlite3_step(stmt) == SqliteRow) value = Native.sqlite3_column_int64(stmt, 0);
                });
                return value;
            }
            finally { Native.sqlite3_close(db); }
        }

        public List<PublicAuthorIndexRow> Search(string query, int offset, int limit)
        {
            List<PublicAuthorIndexRow> rows = new List<PublicAuthorIndexRow>();
            if (!File.Exists(_path)) return rows;
            limit = Math.Max(1, Math.Min(500, limit));
            offset = Math.Max(0, offset);
            string norm = AuthorRules.NormalizeText(query ?? "");
            IntPtr db = IntPtr.Zero;
            if (Native.sqlite3_open_v2(Utf8Z(_path), out db, OpenReadOnly, IntPtr.Zero) != SqliteOk || db == IntPtr.Zero)
            {
                if (db != IntPtr.Zero) Native.sqlite3_close(db);
                return rows;
            }
            try
            {
                string sql;
                string parameter;
                if (norm.Length == 0)
                {
                    sql = "SELECT Id,EntityType,CanonicalName,RomanName,Source,VerificationSource FROM Entity ORDER BY Id DESC LIMIT " + limit + " OFFSET " + offset;
                    parameter = null;
                }
                else
                {
                    sql =
                        "WITH matched(Id) AS (" +
                        "SELECT Id FROM Entity WHERE NormalizedCanonical LIKE ?1 " +
                        "UNION SELECT EntityId FROM EntityAlias WHERE NormalizedAlias LIKE ?1 " +
                        "UNION SELECT EntityId FROM ProviderIdentity WHERE NormalizedTag LIKE ?1) " +
                        "SELECT e.Id,e.EntityType,e.CanonicalName,e.RomanName,e.Source,e.VerificationSource " +
                        "FROM matched m JOIN Entity e ON e.Id=m.Id ORDER BY e.EntityType,e.CanonicalName LIMIT " + limit + " OFFSET " + offset;
                    parameter = "%" + norm + "%";
                }

                Action<IntPtr> read = delegate(IntPtr stmt)
                {
                    while (Native.sqlite3_step(stmt) == SqliteRow)
                    {
                        PublicAuthorIndexRow row = new PublicAuthorIndexRow();
                        row.Id = Native.sqlite3_column_int(stmt, 0);
                        row.EntityType = Text(stmt, 1);
                        row.CanonicalName = Text(stmt, 2);
                        row.RomanName = Text(stmt, 3);
                        row.Source = Text(stmt, 4);
                        row.VerificationSource = Text(stmt, 5);
                        rows.Add(row);
                    }
                };
                if (parameter == null) WithStatement(db, sql, read);
                else WithStatement(db, sql, parameter, read);

                foreach (PublicAuthorIndexRow row in rows)
                {
                    List<string> aliases = new List<string>();
                    WithStatement(db, "SELECT Alias FROM EntityAlias WHERE EntityId=?1 ORDER BY AliasType,Alias", row.Id, delegate(IntPtr stmt)
                    {
                        while (Native.sqlite3_step(stmt) == SqliteRow) AddUnique(aliases, Text(stmt, 0));
                    });
                    row.Aliases = String.Join(" | ", aliases.ToArray());

                    List<string> tags = new List<string>();
                    WithStatement(db, "SELECT Provider || ':' || Namespace || ':' || Tag FROM ProviderIdentity WHERE EntityId=?1 ORDER BY Provider,Namespace,Tag", row.Id, delegate(IntPtr stmt)
                    {
                        while (Native.sqlite3_step(stmt) == SqliteRow) AddUnique(tags, Text(stmt, 0));
                    });
                    row.ProviderTags = String.Join(" | ", tags.ToArray());

                    if (String.Equals(row.EntityType, "Artist", StringComparison.OrdinalIgnoreCase))
                    {
                        List<string> groups = new List<string>();
                        WithStatement(db,
                            "SELECT g.CanonicalName || CASE WHEN g.RomanName<>'' AND g.RomanName<>g.CanonicalName THEN ' (' || g.RomanName || ')' ELSE '' END || ' ×' || r.EvidenceCount " +
                            "FROM ArtistGroup r JOIN Entity g ON g.Id=r.GroupId WHERE r.ArtistId=?1 ORDER BY r.EvidenceCount DESC,g.CanonicalName",
                            row.Id,
                            delegate(IntPtr stmt) { while (Native.sqlite3_step(stmt) == SqliteRow) AddUnique(groups, Text(stmt, 0)); });
                        row.RelatedGroups = String.Join(" | ", groups.ToArray());
                    }
                }
                return rows;
            }
            finally { Native.sqlite3_close(db); }
        }

        private AuthorEntityMatch QuerySql(string norm)
        {
            IntPtr db = GetResolverDatabase();
            if (db == IntPtr.Zero) return new AuthorEntityMatch();

            string entitySql =
                "WITH matched(Id) AS (" +
                "SELECT Id FROM Entity WHERE EntityType='Artist' AND NormalizedCanonical=?1 " +
                "UNION SELECT a.EntityId FROM EntityAlias a JOIN Entity x ON x.Id=a.EntityId " +
                "WHERE x.EntityType='Artist' AND a.NormalizedAlias=?1 " +
                "UNION SELECT p.EntityId FROM ProviderIdentity p JOIN Entity x ON x.Id=p.EntityId " +
                "WHERE x.EntityType='Artist' AND p.Namespace='artist' AND p.NormalizedTag=?1) " +
                "SELECT e.Id,e.CanonicalName,e.RomanName,e.EHArtistTag,e.NHArtistTag," +
                "e.Source,e.ExternalId,e.EntityType,e.VerificationSource,e.EHNamespace,e.EHTag,e.Verified,e.UpdatedUtc,e.NormalizedCanonical," + OptionalDanbooruColumn(db, "e.") + " " +
                "FROM matched m JOIN Entity e ON e.Id=m.Id " +
                "ORDER BY e.Verified DESC,e.Id LIMIT 3";

            List<AuthorEntityRecord> found = new List<AuthorEntityRecord>();
            WithStatement(db, entitySql, norm, delegate(IntPtr stmt)
            {
                while (Native.sqlite3_step(stmt) == SqliteRow)
                    found.Add(ReadArtist(stmt));
            });

            if (found.Count == 0) return new AuthorEntityMatch();
            if (found.Count > 1) return new AuthorEntityMatch { Ambiguous = true, FromPublicDatabase = true };
            return LoadArtistMatch(db, found[0]);
        }

        private static AuthorEntityMatch QueryIndexed(PublicAuthorIdentityIndex index, string norm)
        {
            if (index == null || String.IsNullOrWhiteSpace(norm)) return new AuthorEntityMatch();
            int[] ids;
            if (!index.NameToArtistIds.TryGetValue(norm, out ids) || ids == null || ids.Length == 0)
                return new AuthorEntityMatch();
            if (ids.Length > 1)
                return new AuthorEntityMatch { Ambiguous = true, FromPublicDatabase = true };

            AuthorEntityRecord entity;
            if (!index.Artists.TryGetValue(ids[0], out entity) || entity == null)
                return new AuthorEntityMatch();

            AuthorEntityMatch result = new AuthorEntityMatch
            {
                Found = true,
                FromPublicDatabase = true,
                Entity = entity
            };
            List<string> names;
            if (index.IdentityNames.TryGetValue(entity.Id, out names) && names != null)
                result.IdentityNames = new List<string>(names);
            List<CircleEntityRecord> groups;
            if (index.RelatedGroups.TryGetValue(entity.Id, out groups) && groups != null)
                result.RelatedGroups = new List<CircleEntityRecord>(groups);
            return result;
        }

        private PublicAuthorIdentityIndex EnsureIdentityIndex()
        {
            PublicAuthorIdentityIndex ready = _identityIndex;
            if (ready != null) return ready;
            if (_identityIndexDisabled || !File.Exists(_path)) return null;

            lock (_identityIndexGate)
            {
                if (_identityIndex != null) return _identityIndex;
                if (_identityIndexDisabled) return null;
                try
                {
                    PublicAuthorIdentityIndex built = BuildIdentityIndex();
                    _identityIndex = built;
                    return built;
                }
                catch
                {
                    _identityIndexDisabled = true;
                    return null;
                }
            }
        }

        private PublicAuthorIdentityIndex BuildIdentityIndex()
        {
            Stopwatch watch = Stopwatch.StartNew();
            PublicAuthorIdentityIndex index = new PublicAuthorIdentityIndex();
            Dictionary<string, List<int>> mutableLookup =
                new Dictionary<string, List<int>>(StringComparer.OrdinalIgnoreCase);
            IntPtr db = IntPtr.Zero;
            try
            {
                if (Native.sqlite3_open_v2(Utf8Z(_path), out db, OpenReadOnly, IntPtr.Zero) != SqliteOk || db == IntPtr.Zero)
                    throw new InvalidOperationException("无法以只读方式打开 GuiGuiAuthorIndex.db");

                string artistSql =
                    "SELECT Id,CanonicalName,RomanName,EHArtistTag,NHArtistTag," +
                    "Source,ExternalId,EntityType,VerificationSource,EHNamespace,EHTag,Verified,UpdatedUtc,NormalizedCanonical," + OptionalDanbooruColumn(db, "") + " " +
                    "FROM Entity WHERE EntityType='Artist'";
                WithStatement(db, artistSql, delegate(IntPtr stmt)
                {
                    while (Native.sqlite3_step(stmt) == SqliteRow)
                    {
                        AuthorEntityRecord entity = ReadArtist(stmt);
                        index.Artists[entity.Id] = entity;
                        AddLookup(mutableLookup, Text(stmt, 13), entity.Id);
                        AddIdentity(index, entity.Id, entity.CanonicalName);
                        AddIdentity(index, entity.Id, entity.RomanName);
                        AddIdentity(index, entity.Id, entity.EHArtistTag);
                        AddIdentity(index, entity.Id, PrettyTag(entity.EHArtistTag));
                        AddIdentity(index, entity.Id, entity.NHArtistTag);
                        AddIdentity(index, entity.Id, PrettyTag(entity.NHArtistTag));
                        AddIdentity(index, entity.Id, entity.DanbooruArtistTag);
                        foreach (string tag in new[] { entity.DanbooruArtistTag, PrettyTag(entity.DanbooruArtistTag) }) foreach (string key in AuthorRules.GetNorms(tag)) AddLookup(mutableLookup, key, entity.Id);
                        AddIdentity(index, entity.Id, PrettyTag(entity.DanbooruArtistTag));
                    }
                });

                const string aliasSql =
                    "SELECT a.EntityId,a.Alias,a.NormalizedAlias FROM EntityAlias a " +
                    "JOIN Entity e ON e.Id=a.EntityId WHERE e.EntityType='Artist'";
                WithStatement(db, aliasSql, delegate(IntPtr stmt)
                {
                    while (Native.sqlite3_step(stmt) == SqliteRow)
                    {
                        int id = Native.sqlite3_column_int(stmt, 0);
                        if (!index.Artists.ContainsKey(id)) continue;
                        string alias = Text(stmt, 1);
                        AddLookup(mutableLookup, Text(stmt, 2), id);
                        AddIdentity(index, id, alias);
                        AddIdentity(index, id, PrettyTag(alias));
                    }
                });

                const string providerSql =
                    "SELECT p.EntityId,p.Tag,p.NormalizedTag FROM ProviderIdentity p " +
                    "JOIN Entity e ON e.Id=p.EntityId " +
                    "WHERE e.EntityType='Artist' AND p.Namespace='artist'";
                WithStatement(db, providerSql, delegate(IntPtr stmt)
                {
                    while (Native.sqlite3_step(stmt) == SqliteRow)
                    {
                        int id = Native.sqlite3_column_int(stmt, 0);
                        if (!index.Artists.ContainsKey(id)) continue;
                        string tag = Text(stmt, 1);
                        AddLookup(mutableLookup, Text(stmt, 2), id);
                        AddIdentity(index, id, tag);
                        AddIdentity(index, id, PrettyTag(tag));
                    }
                });

                const string relationSql =
                    "SELECT r.ArtistId,g.Id,g.CanonicalName,g.RomanName,g.EHTag,g.NHGroupTag,g.Source " +
                    "FROM ArtistGroup r JOIN Entity g ON g.Id=r.GroupId " +
                    "WHERE g.EntityType='Group' ORDER BY r.ArtistId,r.EvidenceCount DESC,g.Id";
                WithStatement(db, relationSql, delegate(IntPtr stmt)
                {
                    while (Native.sqlite3_step(stmt) == SqliteRow)
                    {
                        int artistId = Native.sqlite3_column_int(stmt, 0);
                        if (!index.Artists.ContainsKey(artistId)) continue;
                        CircleEntityRecord group = new CircleEntityRecord();
                        group.Id = Native.sqlite3_column_int(stmt, 1);
                        group.CanonicalName = Text(stmt, 2);
                        group.RomanName = Text(stmt, 3);
                        group.EHGroupTag = Text(stmt, 4);
                        group.NHGroupTag = Text(stmt, 5);
                        group.Source = Text(stmt, 6);
                        List<CircleEntityRecord> groups;
                        if (!index.RelatedGroups.TryGetValue(artistId, out groups))
                        {
                            groups = new List<CircleEntityRecord>();
                            index.RelatedGroups[artistId] = groups;
                        }
                        groups.Add(group);
                        index.Stats.Relations++;
                    }
                });

                foreach (KeyValuePair<string, List<int>> pair in mutableLookup)
                    index.NameToArtistIds[pair.Key] = pair.Value.ToArray();

                index.Stats.Artists = index.Artists.Count;
                index.Stats.LookupKeys = index.NameToArtistIds.Count;
                int identityCount = 0;
                foreach (List<string> names in index.IdentityNames.Values) identityCount += names != null ? names.Count : 0;
                index.Stats.IdentityNames = identityCount;
                index.Stats.Ready = true;
                return index;
            }
            finally
            {
                if (db != IntPtr.Zero) Native.sqlite3_close(db);
                watch.Stop();
                index.Stats.BuildMs = watch.ElapsedMilliseconds;
            }
        }

        private static void AddLookup(Dictionary<string, List<int>> map, string norm, int id)
        {
            if (map == null || id <= 0 || String.IsNullOrWhiteSpace(norm)) return;
            string key = norm.Trim();
            List<int> ids;
            if (!map.TryGetValue(key, out ids))
            {
                ids = new List<int>();
                map[key] = ids;
            }
            if (!ids.Contains(id)) ids.Add(id);
        }

        private static void AddIdentity(PublicAuthorIdentityIndex index, int id, string value)
        {
            if (index == null || id <= 0 || String.IsNullOrWhiteSpace(value)) return;
            List<string> names;
            if (!index.IdentityNames.TryGetValue(id, out names))
            {
                names = new List<string>();
                index.IdentityNames[id] = names;
            }
            AddUnique(names, value);
        }

        private static PublicAuthorIdentityIndexStats CopyStats(PublicAuthorIdentityIndexStats source)
        {
            PublicAuthorIdentityIndexStats result = new PublicAuthorIdentityIndexStats();
            if (source == null) return result;
            result.Ready = source.Ready;
            result.Artists = source.Artists;
            result.LookupKeys = source.LookupKeys;
            result.IdentityNames = source.IdentityNames;
            result.Relations = source.Relations;
            result.BuildMs = source.BuildMs;
            return result;
        }

        private AuthorEntityMatch QueryStructured(StructuredAuthorParts parts)
        {
            if (parts == null || parts.Creators == null ||
                parts.Creators.Count == 0 || !File.Exists(_path))
                return new AuthorEntityMatch();

            IntPtr db = GetResolverDatabase();
            if (db == IntPtr.Zero) return new AuthorEntityMatch();

            {
                HashSet<int> groupIds = new HashSet<int>();
                foreach (string norm in AuthorRules.GetNorms(parts.Society ?? ""))
                {
                    const string groupSql =
                        "WITH matched(Id) AS (" +
                        "SELECT Id FROM Entity WHERE EntityType='Group' AND NormalizedCanonical=?1 " +
                        "UNION SELECT a.EntityId FROM EntityAlias a JOIN Entity e ON e.Id=a.EntityId " +
                        "WHERE e.EntityType='Group' AND a.NormalizedAlias=?1 " +
                        "UNION SELECT p.EntityId FROM ProviderIdentity p JOIN Entity e ON e.Id=p.EntityId " +
                        "WHERE e.EntityType='Group' AND p.Namespace='group' AND p.NormalizedTag=?1) " +
                        "SELECT Id FROM matched";
                    WithStatement(db, groupSql, norm, delegate(IntPtr stmt)
                    {
                        while (Native.sqlite3_step(stmt) == SqliteRow)
                            groupIds.Add(Native.sqlite3_column_int(stmt, 0));
                    });
                }
                if (groupIds.Count == 0) return new AuthorEntityMatch();

                HashSet<string> groupContextNorms = new HashSet<string>(
                    AuthorRules.GetNorms(parts.Society ?? ""),
                    StringComparer.OrdinalIgnoreCase);
                foreach (int groupId in groupIds)
                {
                    WithStatement(db,
                        "SELECT CanonicalName,RomanName FROM Entity WHERE Id=?1 " +
                        "UNION SELECT Alias,'' FROM EntityAlias WHERE EntityId=?1 " +
                        "UNION SELECT Tag,'' FROM ProviderIdentity WHERE EntityId=?1 AND Namespace='group'",
                        groupId,
                        delegate(IntPtr stmt)
                        {
                            while (Native.sqlite3_step(stmt) == SqliteRow)
                            {
                                foreach (string value in new string[] { Text(stmt, 0), Text(stmt, 1) })
                                    foreach (string valueNorm in AuthorRules.GetNorms(value ?? ""))
                                        groupContextNorms.Add(valueNorm);
                            }
                        });
                }

                HashSet<int> creatorIds = new HashSet<int>();
                foreach (string creator in parts.Creators)
                {
                    foreach (string norm in AuthorRules.GetNorms(creator ?? ""))
                    {
                        string artistSql =
                            "WITH matched(Id) AS (" +
                            "SELECT Id FROM Entity WHERE EntityType='Artist' AND NormalizedCanonical=?1 " +
                            "UNION SELECT a.EntityId FROM EntityAlias a JOIN Entity e ON e.Id=a.EntityId " +
                            "WHERE e.EntityType='Artist' AND a.NormalizedAlias=?1 " +
                            "UNION SELECT p.EntityId FROM ProviderIdentity p JOIN Entity e ON e.Id=p.EntityId " +
                            "WHERE e.EntityType='Artist' AND p.Namespace='artist' AND p.NormalizedTag=?1) " +
                            "SELECT Id FROM matched";
                        WithStatement(db, artistSql, norm, delegate(IntPtr stmt)
                        {
                            while (Native.sqlite3_step(stmt) == SqliteRow)
                                creatorIds.Add(Native.sqlite3_column_int(stmt, 0));
                        });
                    }
                }
                if (creatorIds.Count == 0) return new AuthorEntityMatch();

                HashSet<int> relatedArtists = new HashSet<int>();
                foreach (int groupId in groupIds)
                {
                    WithStatement(db,
                        "SELECT ArtistId FROM ArtistGroup WHERE GroupId=?1",
                        groupId,
                        delegate(IntPtr stmt)
                        {
                            while (Native.sqlite3_step(stmt) == SqliteRow)
                            {
                                int artistId = Native.sqlite3_column_int(stmt, 0);
                                if (creatorIds.Contains(artistId))
                                    relatedArtists.Add(artistId);
                            }
                        });
                }

                if (relatedArtists.Count == 0) return new AuthorEntityMatch();
                if (relatedArtists.Count > 1)
                {
                    // Prefer an artist carrying a structured alias whose
                    // society portion matches the already confirmed group.
                    // This disambiguates noisy shared aliases such as a common
                    // pen name without inventing a fuzzy author match.
                    HashSet<int> structuredAliasArtists = new HashSet<int>();
                    foreach (int artistId in relatedArtists)
                    {
                        WithStatement(db,
                            "SELECT Alias FROM EntityAlias WHERE EntityId=?1",
                            artistId,
                            delegate(IntPtr stmt)
                            {
                                while (Native.sqlite3_step(stmt) == SqliteRow)
                                {
                                    StructuredAuthorParts aliasParts =
                                        AuthorRules.GetStructuredAuthorParts(Text(stmt, 0));
                                    if (aliasParts == null) continue;
                                    foreach (string aliasSocietyNorm in AuthorRules.GetNorms(aliasParts.Society ?? ""))
                                    {
                                        if (!groupContextNorms.Contains(aliasSocietyNorm)) continue;
                                        structuredAliasArtists.Add(artistId);
                                        break;
                                    }
                                }
                            });
                    }
                    if (structuredAliasArtists.Count == 1)
                        relatedArtists = structuredAliasArtists;
                    else
                    {
                        HashSet<string> creatorEvidence = new HashSet<string>(
                            StringComparer.OrdinalIgnoreCase);
                        foreach (string creator in parts.Creators)
                            foreach (string creatorNorm in AuthorRules.GetNorms(creator ?? ""))
                                if (creatorNorm.Length >= 2) creatorEvidence.Add(creatorNorm);

                        int bestArtist = 0;
                        int bestScore = 0;
                        bool tied = false;
                        foreach (int artistId in relatedArtists)
                        {
                            int score = 0;
                            WithStatement(db,
                                "SELECT Alias FROM EntityAlias WHERE EntityId=?1",
                                artistId,
                                delegate(IntPtr stmt)
                                {
                                    HashSet<string> hitEvidence = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                                    while (Native.sqlite3_step(stmt) == SqliteRow)
                                    {
                                        string aliasNorm = AuthorRules.NormalizeText(Text(stmt, 0));
                                        foreach (string evidence in creatorEvidence)
                                            if (aliasNorm.IndexOf(evidence, StringComparison.OrdinalIgnoreCase) >= 0)
                                                hitEvidence.Add(evidence);
                                    }
                                    score = hitEvidence.Count;
                                });

                            if (score > bestScore)
                            {
                                bestArtist = artistId;
                                bestScore = score;
                                tied = false;
                            }
                            else if (score > 0 && score == bestScore)
                            {
                                tied = true;
                            }
                        }

                        if (bestScore >= 2 && !tied)
                        {
                            relatedArtists.Clear();
                            relatedArtists.Add(bestArtist);
                        }
                        else
                        {
                            return new AuthorEntityMatch { Ambiguous = true, FromPublicDatabase = true };
                        }
                    }
                }

                foreach (int artistId in relatedArtists)
                {
                    AuthorEntityRecord entity = null;
                    WithStatement(db,
                        "SELECT Id,CanonicalName,RomanName,EHArtistTag,NHArtistTag," +
                        "Source,ExternalId,EntityType,VerificationSource,EHNamespace,EHTag,Verified,UpdatedUtc,NormalizedCanonical," + OptionalDanbooruColumn(db, "") + " " +
                        "FROM Entity WHERE Id=?1 AND EntityType='Artist'",
                        artistId,
                        delegate(IntPtr stmt)
                        {
                            if (Native.sqlite3_step(stmt) != SqliteRow) return;
                            entity = ReadArtist(stmt);
                        });
                    if (entity != null) return LoadArtistMatch(db, entity);
                }
                return new AuthorEntityMatch();
            }
        }

        private IntPtr GetResolverDatabase()
        {
            if (_resolverDb != IntPtr.Zero) return _resolverDb;
            IntPtr opened;
            if (Native.sqlite3_open_v2(Utf8Z(_path), out opened, OpenReadOnly, IntPtr.Zero) != SqliteOk || opened == IntPtr.Zero)
            {
                if (opened != IntPtr.Zero) Native.sqlite3_close(opened);
                return IntPtr.Zero;
            }
            _resolverDb = opened;
            return _resolverDb;
        }

        private static AuthorEntityRecord ReadArtist(IntPtr stmt)
        {
            AuthorEntityRecord e = new AuthorEntityRecord();
            e.Id = Native.sqlite3_column_int(stmt, 0);
            e.CanonicalName = Text(stmt, 1);
            e.RomanName = Text(stmt, 2);
            e.EHArtistTag = Text(stmt, 3);
            e.NHArtistTag = Text(stmt, 4);
            e.Source = Text(stmt, 5);
            e.ExternalId = Text(stmt, 6);
            e.EntityType = Text(stmt, 7);
            e.VerificationSource = Text(stmt, 8);
            e.EHNamespace = Text(stmt, 9);
            e.EHTag = Text(stmt, 10);
            e.Verified = Native.sqlite3_column_int(stmt, 11) != 0;
            e.UpdatedUtc = Text(stmt, 12);
            e.DanbooruArtistTag = Text(stmt, 14);
            return e;
        }

        // Schema v4 exposes compatibility views. Inspect the view columns, never
        // the compressed *Data tables or the builder's maintenance database.
        private static string OptionalDanbooruColumn(IntPtr db, string prefix)
        {
            bool present = false;
            WithStatement(db, "PRAGMA table_info(Entity)", stmt => {
                while (Native.sqlite3_step(stmt) == SqliteRow)
                    if (String.Equals(Text(stmt, 1), "DanbooruArtistTag", StringComparison.OrdinalIgnoreCase)) present = true;
            });
            return present ? prefix + "DanbooruArtistTag" : "''";
        }

        private static AuthorEntityMatch LoadArtistMatch(
            IntPtr db,
            AuthorEntityRecord entity)
        {
                AuthorEntityMatch result = new AuthorEntityMatch
                {
                    Found = true,
                    FromPublicDatabase = true,
                    Entity = entity
                };
                AddUnique(result.IdentityNames, entity.CanonicalName);
                AddUnique(result.IdentityNames, entity.RomanName);
                AddUnique(result.IdentityNames, entity.EHArtistTag);
                AddUnique(result.IdentityNames, PrettyTag(entity.EHArtistTag));
                AddUnique(result.IdentityNames, entity.NHArtistTag);
                AddUnique(result.IdentityNames, PrettyTag(entity.NHArtistTag));

                const string artistNamesSql =
                    "SELECT Alias FROM EntityAlias WHERE EntityId=?1 " +
                    "UNION SELECT Tag FROM ProviderIdentity WHERE EntityId=?1 AND Namespace='artist'";
                WithStatement(db, artistNamesSql, entity.Id, delegate(IntPtr stmt)
                {
                    while (Native.sqlite3_step(stmt) == SqliteRow)
                    {
                        string value = Text(stmt, 0);
                        AddUnique(result.IdentityNames, value);
                        AddUnique(result.IdentityNames, PrettyTag(value));
                    }
                });

                const string groupsSql =
                    "SELECT g.Id,g.CanonicalName,g.RomanName,g.EHTag,g.NHGroupTag,g.Source " +
                    "FROM ArtistGroup r JOIN Entity g ON g.Id=r.GroupId " +
                    "WHERE r.ArtistId=?1 AND g.EntityType='Group' ORDER BY r.EvidenceCount DESC,g.Id";
                WithStatement(db, groupsSql, entity.Id, delegate(IntPtr stmt)
                {
                    while (Native.sqlite3_step(stmt) == SqliteRow)
                    {
                        CircleEntityRecord group = new CircleEntityRecord();
                        group.Id = Native.sqlite3_column_int(stmt, 0);
                        group.CanonicalName = Text(stmt, 1);
                        group.RomanName = Text(stmt, 2);
                        group.EHGroupTag = Text(stmt, 3);
                        group.NHGroupTag = Text(stmt, 4);
                        group.Source = Text(stmt, 5);
                        result.RelatedGroups.Add(group);
                    }
                });
                return result;
        }

        private static void WithStatement(IntPtr db, string sql, string value, Action<IntPtr> action)
        {
            IntPtr stmt;
            if (Native.sqlite3_prepare_v2(db, Utf8Z(sql), -1, out stmt, IntPtr.Zero) != SqliteOk || stmt == IntPtr.Zero)
                throw new InvalidOperationException("Unable to read GuiGuiAuthorIndex.db.");
            try
            {
                byte[] text = Encoding.UTF8.GetBytes(value ?? "");
                if (Native.sqlite3_bind_text(stmt, 1, text, text.Length, new IntPtr(-1)) != SqliteOk)
                    throw new InvalidOperationException("Unable to bind author name.");
                action(stmt);
            }
            finally { Native.sqlite3_finalize(stmt); }
        }

        private static void WithStatement(IntPtr db, string sql, Action<IntPtr> action)
        {
            IntPtr stmt;
            if (Native.sqlite3_prepare_v2(db, Utf8Z(sql), -1, out stmt, IntPtr.Zero) != SqliteOk || stmt == IntPtr.Zero)
                throw new InvalidOperationException("Unable to read GuiGuiAuthorIndex.db.");
            try { action(stmt); }
            finally { Native.sqlite3_finalize(stmt); }
        }

        private static long Scalar(IntPtr db, string sql)
        {
            long value = 0;
            WithStatement(db, sql, delegate(IntPtr stmt)
            {
                if (Native.sqlite3_step(stmt) == SqliteRow) value = Native.sqlite3_column_int64(stmt, 0);
            });
            return value;
        }

        private static void WithStatement(IntPtr db, string sql, int value, Action<IntPtr> action)
        {
            IntPtr stmt;
            if (Native.sqlite3_prepare_v2(db, Utf8Z(sql), -1, out stmt, IntPtr.Zero) != SqliteOk || stmt == IntPtr.Zero)
                throw new InvalidOperationException("Unable to read GuiGuiAuthorIndex.db.");
            try
            {
                if (Native.sqlite3_bind_int(stmt, 1, value) != SqliteOk)
                    throw new InvalidOperationException("Unable to bind entity id.");
                action(stmt);
            }
            finally { Native.sqlite3_finalize(stmt); }
        }

        private static byte[] Utf8Z(string value)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(value ?? "");
            byte[] terminated = new byte[bytes.Length + 1];
            Buffer.BlockCopy(bytes, 0, terminated, 0, bytes.Length);
            return terminated;
        }

        private static string Text(IntPtr stmt, int column)
        {
            IntPtr ptr = Native.sqlite3_column_text(stmt, column);
            int length = Native.sqlite3_column_bytes(stmt, column);
            if (ptr == IntPtr.Zero || length <= 0) return "";
            byte[] bytes = new byte[length];
            Marshal.Copy(ptr, bytes, 0, length);
            return Encoding.UTF8.GetString(bytes);
        }

        private static string PrettyTag(string value)
        {
            return String.IsNullOrWhiteSpace(value) ? "" : value.Replace('_', ' ').Trim();
        }

        private static void AddUnique(List<string> values, string value)
        {
            if (values == null || String.IsNullOrWhiteSpace(value)) return;
            string trimmed = value.Trim();
            foreach (string existing in values)
                if (String.Equals(existing, trimmed, StringComparison.OrdinalIgnoreCase)) return;
            values.Add(trimmed);
        }

        private static class Native
        {
            [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
            internal static extern int sqlite3_open_v2(byte[] filename, out IntPtr db, int flags, IntPtr vfs);
            [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
            internal static extern int sqlite3_close(IntPtr db);
            [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
            internal static extern int sqlite3_prepare_v2(IntPtr db, byte[] sql, int bytes, out IntPtr statement, IntPtr tail);
            [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
            internal static extern int sqlite3_bind_text(IntPtr statement, int index, byte[] value, int bytes, IntPtr destructor);
            [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
            internal static extern int sqlite3_bind_int(IntPtr statement, int index, int value);
            [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
            internal static extern int sqlite3_step(IntPtr statement);
            [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
            internal static extern int sqlite3_finalize(IntPtr statement);
            [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
            internal static extern int sqlite3_column_int(IntPtr statement, int column);
            [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
            internal static extern long sqlite3_column_int64(IntPtr statement, int column);
            [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
            internal static extern IntPtr sqlite3_column_text(IntPtr statement, int column);
            [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
            internal static extern int sqlite3_column_bytes(IntPtr statement, int column);
        }
    }
}
