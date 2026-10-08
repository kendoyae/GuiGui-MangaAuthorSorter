using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Web.Script.Serialization;

namespace MangaAuthorSorter
{
    internal sealed class CircleAliasRecord
    {
        public int CircleId;
        public string Alias = "";
        public bool Disabled;
        public string Source = "User";
    }
    internal sealed class AuthorNameDecision
    {
        public string Name = "";
        public string EntityType = "Artist";
        public int EntityId;
        public string Status = "Pending"; // Confirmed / Independent / Disabled / Pending
    }
    internal sealed class PublicEntitySnapshot
    {
        public int Id;
        public string EntityType = "";
        public string CanonicalName = "";
        public string RomanName = "";
        public string StableKey = "";
        public string DatabaseVersion = "";
        public string Source = "";
        public List<string> Aliases = new List<string>();
        public List<string> Identities = new List<string>();
        public List<int> RelatedGroups = new List<int>();
        public List<PublicNameLookup> LookupNames = new List<PublicNameLookup>();
    }
    internal sealed class PublicNameLookup
    {
        public string Value = "";
        public string NormalizedName = "";
    }
    internal sealed class PublicEntityOverride
    {
        public string Id = "";
        public string PublicEntityKey = "";
        public string DatabaseVersion = "";
        public int OriginalPublicId; // Diagnostic only, never cross-version identity.
        public string EntityType = "Artist";
        public string OriginalCanonicalName = "";
        public string CanonicalName = "";
        public List<string> OriginalNames = new List<string>();
        public List<string> AddedAliases = new List<string>();
        public List<string> DisabledNames = new List<string>();
        public string Status = "Active";
        public bool UserConfirmed = true;
        public List<PublicUserGroupRelation> UserGroups = new List<PublicUserGroupRelation>();
    }
    internal sealed class PublicUserGroupRelation
    {
        public int CircleId;
        public bool UserConfirmed;
        public bool Disabled;
    }
    internal sealed class AuthorLibraryConflict
    {
        public string Kind = "";
        public string Name = "";
        public string EntityType = "Artist";
        public string Status = "Pending";
        public List<int> Candidates = new List<int>();
        public List<int> PublicCandidates = new List<int>();
        public string OverrideId = "";
    }
    internal sealed partial class AuthorEntityStore
    {
        private Dictionary<int, PublicEntitySnapshot> _publicSnapshots;
        private string _publicSnapshotVersion = "";
        private string _publicStamp = "";
        private string _publicContentVersion = "";

        public string PublicDatabaseVersion
        {
            get
            {
                lock (_sync)
                {
                    string path = _publicDatabase.PathName;
                    FileInfo info = new FileInfo(path);
                    string stamp = info.Exists ? info.Length + ":" + info.LastWriteTimeUtc.Ticks : "missing";
                    if (stamp != _publicStamp)
                    {
                        string contentVersion;
                        try { contentVersion = info.Exists ? ContentHash(File.ReadAllBytes(path)) : "missing"; }
                        catch (IOException) { contentVersion = "unavailable"; }
                        catch (UnauthorizedAccessException) { contentVersion = "unavailable"; }
                        _publicStamp = stamp;
                        if (contentVersion != _publicContentVersion) {
                            _publicContentVersion = contentVersion;
                            _publicSnapshots = null;
                            // The old immutable index and resolver handle cannot follow a replacement DB.
                            _publicDatabase.Dispose();
                            _publicDatabase = new GuiGuiAuthorIndexDatabase(path);
                        }
                    }
                    return _publicContentVersion;
                }
            }
        }
        internal static string ContentHash(byte[] bytes)
        {
            using (System.Security.Cryptography.SHA256 hash = System.Security.Cryptography.SHA256.Create())
                return Convert.ToBase64String(hash.ComputeHash(bytes));
        }
        public Dictionary<int, PublicEntitySnapshot> GetPublicSnapshots()
        {
            lock (_sync)
            {
                string version = PublicDatabaseVersion;
                if (_publicSnapshots == null || _publicSnapshotVersion != version)
                {
                    _publicSnapshots = version == "missing" ? new Dictionary<int, PublicEntitySnapshot>()
                        : _publicDatabase.LoadSnapshots(version);
                    _publicSnapshotVersion = version;
                }
                return _publicSnapshots;
            }
        }
        public void RefreshPublicDatabase()
        {
            lock (_sync) { _publicStamp = ""; string version = PublicDatabaseVersion; }
        }
        public PublicEntitySnapshot GetPublicSnapshot(int id)
        {
            PublicEntitySnapshot snapshot;
            return GetPublicSnapshots().TryGetValue(id, out snapshot) ? snapshot : null;
        }
        internal static List<PublicEntitySnapshot> BindOverride(PublicEntityOverride record, IEnumerable<PublicEntitySnapshot> snapshots)
        {
            return snapshots.Where(x => x.EntityType == record.EntityType &&
                (!String.IsNullOrEmpty(record.PublicEntityKey) ? x.StableKey == record.PublicEntityKey :
                    x.DatabaseVersion == record.DatabaseVersion && x.Id == record.OriginalPublicId)).ToList();
        }
        public void SavePublicOverride(PublicEntitySnapshot snapshot, string name, IEnumerable<string> added,
            IEnumerable<string> disabled, string overrideId)
        {
            if (snapshot == null) throw new InvalidOperationException("Select a public entity.");
            lock (_sync)
            {
                PublicEntitySnapshot current = GetPublicSnapshot(snapshot.Id);
                if (current == null || current.DatabaseVersion != snapshot.DatabaseVersion)
                    throw new InvalidOperationException("Public database changed. Refresh and select the entity again.");
                AuthorEntityDatabase db = LoadUnlocked();
                PublicEntityOverride record = String.IsNullOrEmpty(overrideId) ? db.PublicOverrides.FirstOrDefault(x =>
                    x.EntityType == snapshot.EntityType && (!String.IsNullOrEmpty(snapshot.StableKey) ?
                        x.PublicEntityKey == snapshot.StableKey : x.DatabaseVersion == snapshot.DatabaseVersion && x.OriginalPublicId == snapshot.Id))
                    : db.PublicOverrides.FirstOrDefault(x => x.Id == overrideId);
                if (record == null) { record = new PublicEntityOverride { Id = Guid.NewGuid().ToString("N") }; db.PublicOverrides.Add(record); }
                record.PublicEntityKey = !String.IsNullOrEmpty(snapshot.StableKey) &&
                    GetPublicSnapshots().Values.Count(x => x.EntityType == snapshot.EntityType && x.StableKey == snapshot.StableKey) == 1 ? snapshot.StableKey : "";
                record.DatabaseVersion = snapshot.DatabaseVersion;
                record.OriginalPublicId = snapshot.Id;
                record.EntityType = snapshot.EntityType;
                record.OriginalCanonicalName = snapshot.CanonicalName;
                record.OriginalNames = snapshot.Aliases.Concat(snapshot.LookupNames.Select(x => x.Value)).Concat(new[] { snapshot.CanonicalName, snapshot.RomanName }).Distinct().ToList();
                record.CanonicalName = (name ?? "").Trim();
                record.AddedAliases = CleanNames(added);
                record.DisabledNames = CleanNames(disabled);
                record.Status = "Active";
                if (db.PublicOverrides.Any(x => x != record && x.Status == "Active" &&
                    BindOverride(x, GetPublicSnapshots().Values).Any(s => s.Id == snapshot.Id && s.EntityType == snapshot.EntityType)))
                    throw new InvalidOperationException("This public entity already has an active override. Restore the duplicate before rebinding.");
                SaveInternal(db);
            }
            RaiseChanged();
        }
        public void RemovePublicOverride(string id)
        {
            Mutate(db => db.PublicOverrides.RemoveAll(x => x.Id == id));
        }
        public void SetOverrideStatus(string id, string status)
        {
            if (status != "Active" && status != "Pending") throw new InvalidDataException("Invalid override status.");
            Mutate(db => { PublicEntityOverride record = db.PublicOverrides.First(x => x.Id == id); record.Status = status; });
        }

        public void SaveFolderRule(string id, string canonical, IEnumerable<string> names, bool delete)
        {
            Mutate(delegate(AuthorEntityDatabase db)
            {
                db.LegacyAliasGroups.RemoveAll(x => x.Id == id && x.AuthorId == 0);
                if (!delete) db.LegacyAliasGroups.Add(new LegacyAliasGroupRecord { Id = String.IsNullOrEmpty(id) ? Guid.NewGuid().ToString("N") : id,
                    Canonical = canonical, Names = CleanNames(new[] { canonical }.Concat(names)), Status = "Confirmed" });
            });
        }
        private void Mutate(Action<AuthorEntityDatabase> edit, Action afterSave = null, bool identityChanged = true)
        {
            lock (_sync) { AuthorEntityDatabase db = LoadUnlocked(); edit(db); SaveInternal(db); if (afterSave != null) afterSave(); }
            RaiseChanged(identityChanged);
        }
        private static List<string> CleanNames(IEnumerable<string> names)
        {
            return (names ?? new string[0]).Where(x => !String.IsNullOrWhiteSpace(x)).Select(x => x.Trim())
                .GroupBy(x => AuthorRules.NormalizeText(x), StringComparer.OrdinalIgnoreCase).Select(x => x.First()).ToList();
        }
        public int SaveUserEntity(int id, string type, string canonical, string roman, IEnumerable<string> names,
            IEnumerable<string> disabled, bool confirmed)
        {
            if (String.IsNullOrWhiteSpace(canonical)) throw new InvalidDataException("Canonical name is required.");
            if (type != "Artist" && type != "Group") throw new InvalidDataException("Invalid entity type.");
            List<string> aliases = CleanNames(names), disabledNames = CleanNames(disabled);
            int sessionId = id < 0 ? id : 0;
            Mutate(delegate(AuthorEntityDatabase db)
            {
                if (type == "Artist")
                {
                    AuthorEntityRecord entity = db.Authors.FirstOrDefault(x => x.Id == id);
                    if (id > 0 && entity == null) throw new InvalidDataException("Entity was removed; refresh the library.");
                    if (entity == null) {
                        AuthorEntityRecord temporary = _sessionOverlay.Authors.FirstOrDefault(x => x.Id == sessionId);
                        if (sessionId != 0 && temporary == null) throw new InvalidDataException("Temporary entity was removed; refresh the library.");
                        entity = temporary == null ? new AuthorEntityRecord { Source = "User" } : new JavaScriptSerializer().Deserialize<AuthorEntityRecord>(new JavaScriptSerializer().Serialize(temporary));
                        entity.Id = NextAuthorId(db); entity.EntityId = "user:" + Guid.NewGuid().ToString("N"); db.Authors.Add(entity);
                    }
                    id = entity.Id;
                    entity.CanonicalName = canonical.Trim(); entity.RomanName = (roman ?? "").Trim();
                    entity.EntityType = "Artist"; entity.UserConfirmed = confirmed; entity.Verified = confirmed;
                    entity.VerificationSource = confirmed ? "UserConfirmed" : "Unverified";
                    entity.UpdatedUtc = DateTime.UtcNow.ToString("o");
                    db.Aliases.RemoveAll(x => x.AuthorId == entity.Id);
                    foreach (string alias in aliases.Concat(disabledNames).Distinct())
                        db.Aliases.Add(new AuthorAliasRecord { AuthorId = id, Alias = alias, AliasType = "user", Source = "User",
                            Disabled = disabledNames.Any(x => AuthorRules.NormalizeText(x) == AuthorRules.NormalizeText(alias)) });
                    // Ordinary migrated groups are projections of the entity, never an independent editable master.
                    db.LegacyAliasGroups.RemoveAll(x => x.AuthorId == entity.Id);
                }
                else
                {
                    CircleEntityRecord entity = db.Circles.FirstOrDefault(x => x.Id == id);
                    if (id > 0 && entity == null) throw new InvalidDataException("Group was removed; refresh the library.");
                    if (entity == null) {
                        CircleEntityRecord temporary = _sessionOverlay.Circles.FirstOrDefault(x => x.Id == sessionId);
                        if (sessionId != 0 && temporary == null) throw new InvalidDataException("Temporary group was removed; refresh the library.");
                        entity = temporary == null ? new CircleEntityRecord { Source = "User" } : new JavaScriptSerializer().Deserialize<CircleEntityRecord>(new JavaScriptSerializer().Serialize(temporary));
                        entity.Id = NextCircleId(db); entity.EntityId = "group:" + Guid.NewGuid().ToString("N"); db.Circles.Add(entity);
                    }
                    id = entity.Id;
                    entity.CanonicalName = canonical.Trim(); entity.RomanName = (roman ?? "").Trim();
                    entity.UserConfirmed = confirmed; entity.UpdatedUtc = DateTime.UtcNow.ToString("o");
                    db.CircleAliases.RemoveAll(x => x.CircleId == id);
                    foreach (string alias in aliases.Concat(disabledNames).Distinct())
                        db.CircleAliases.Add(new CircleAliasRecord { CircleId = id, Alias = alias,
                            Disabled = disabledNames.Any(x => AuthorRules.NormalizeText(x) == AuthorRules.NormalizeText(alias)) });
                }
                if (sessionId != 0) foreach (AuthorCircleRecord relation in _sessionOverlay.AuthorCircles.Where(x => type == "Artist" ? x.AuthorId == sessionId : x.CircleId == sessionId)) {
                    int author = type == "Artist" ? id : relation.AuthorId, group = type == "Group" ? id : relation.CircleId;
                    if (author > 0 && group > 0 && !db.AuthorCircles.Any(x => x.AuthorId == author && x.CircleId == group))
                        db.AuthorCircles.Add(new AuthorCircleRecord { AuthorId = author, CircleId = group, Source = relation.Source, UserConfirmed = relation.UserConfirmed, Disabled = relation.Disabled });
                }
            }, delegate {
                if (sessionId == 0) return;
                if (type == "Artist") { _sessionOverlay.Authors.RemoveAll(x => x.Id == sessionId); _sessionOverlay.Aliases.RemoveAll(x => x.AuthorId == sessionId); _sessionOverlay.LookupCache.RemoveAll(x => x.AuthorId == sessionId); }
                else { _sessionOverlay.Circles.RemoveAll(x => x.Id == sessionId); _sessionOverlay.CircleAliases.RemoveAll(x => x.CircleId == sessionId); }
                foreach (AuthorCircleRecord relation in _sessionOverlay.AuthorCircles) { if (type == "Artist" && relation.AuthorId == sessionId) relation.AuthorId = id; if (type == "Group" && relation.CircleId == sessionId) relation.CircleId = id; }
                _sessionOverlay.AuthorCircles.RemoveAll(x => x.AuthorId > 0 && x.CircleId > 0);
            });
            return id;
        }
        public void SaveLegacyGroup(int authorId, string canonical, string roman, IEnumerable<string> names, IEnumerable<string> disabled, bool confirmed)
        {
            if (String.IsNullOrWhiteSpace(canonical)) throw new InvalidDataException("Canonical name is required.");
            Mutate(delegate(AuthorEntityDatabase db) {
                AuthorEntityRecord legacy = db.Authors.First(x => x.Id == authorId && x.EntityType == "Group");
                if (db.AuthorCircles.Any(x => x.AuthorId == authorId)) throw new InvalidOperationException("Unlink the legacy record's relationships before changing its storage type.");
                int id = NextCircleId(db);
                db.Circles.Add(new CircleEntityRecord { Id = id, EntityId = legacy.EntityId, CanonicalName = canonical.Trim(), RomanName = (roman ?? "").Trim(),
                    PublicEntityKey = legacy.PublicEntityKey, ProviderIdentities = new List<string>(legacy.ProviderIdentities),
                    EHGroupTag = legacy.EHNamespace == "group" ? legacy.EHTag : "", Source = legacy.Source, UserConfirmed = confirmed, UpdatedUtc = DateTime.UtcNow.ToString("o") });
                List<string> blocked = CleanNames(disabled);
                foreach (string alias in CleanNames(names).Concat(blocked).Distinct()) db.CircleAliases.Add(new CircleAliasRecord { CircleId = id, Alias = alias,
                    Disabled = blocked.Any(x => AuthorRules.NormalizeText(x) == AuthorRules.NormalizeText(alias)), Source = "User" });
                db.Authors.Remove(legacy); db.Aliases.RemoveAll(x => x.AuthorId == authorId); db.LookupCache.RemoveAll(x => x.AuthorId == authorId);
                db.NameDecisions.RemoveAll(x => x.EntityType == "Artist" && x.EntityId == authorId); db.LegacyAliasGroups.RemoveAll(x => x.AuthorId == authorId);
            });
        }
        public void DeleteUserEntity(int id, string type)
        {
            lock (_sync) if (_sessionOverlay.AuthorCircles.Any(x => type == "Artist" ? x.AuthorId == id : x.CircleId == id))
                throw new InvalidOperationException("Unlink relationships before deleting the entity.");
            if (id < 0) {
                lock (_sync) {
                    if (_sessionOverlay.AuthorCircles.Any(x => type == "Artist" ? x.AuthorId == id : x.CircleId == id)) throw new InvalidOperationException("Unlink relationships before deleting the temporary entity.");
                    if (type == "Artist") { _sessionOverlay.Authors.RemoveAll(x => x.Id == id); _sessionOverlay.Aliases.RemoveAll(x => x.AuthorId == id); _sessionOverlay.LookupCache.RemoveAll(x => x.AuthorId == id); }
                    else { _sessionOverlay.Circles.RemoveAll(x => x.Id == id); _sessionOverlay.CircleAliases.RemoveAll(x => x.CircleId == id); }
                }
                RaiseChanged(); return;
            }
            Mutate(delegate(AuthorEntityDatabase db)
            {
                if (db.AuthorCircles.Any(x => type == "Artist" ? x.AuthorId == id : x.CircleId == id) ||
                    (type == "Group" && db.PublicOverrides.Any(x => x.UserGroups.Any(r => r.CircleId == id))))
                    throw new InvalidOperationException("Entity has relationships. Unlink them before deleting it.");
                if (type == "Artist")
                {
                    db.Authors.RemoveAll(x => x.Id == id); db.Aliases.RemoveAll(x => x.AuthorId == id);
                    db.LookupCache.RemoveAll(x => x.AuthorId == id); db.LegacyAliasGroups.RemoveAll(x => x.AuthorId == id);
                }
                else { db.Circles.RemoveAll(x => x.Id == id); db.CircleAliases.RemoveAll(x => x.CircleId == id); }
                db.NameDecisions.RemoveAll(x => x.EntityType == type && x.EntityId == id);
            });
        }
        public void SaveRelationship(int authorId, int groupId, bool confirmed, bool disabled)
        {
            if (authorId < 0 || groupId < 0) throw new InvalidOperationException("Save both temporary entities to the user library before editing their relationship.");
            Mutate(delegate(AuthorEntityDatabase db)
            {
                if (!db.Authors.Any(x => x.Id == authorId && x.EntityType == "Artist") || !db.Circles.Any(x => x.Id == groupId))
                    throw new InvalidDataException("Relationship endpoint is missing or has the wrong type.");
                AuthorCircleRecord relation = db.AuthorCircles.FirstOrDefault(x => x.AuthorId == authorId && x.CircleId == groupId);
                if (relation == null) { relation = new AuthorCircleRecord { AuthorId = authorId, CircleId = groupId }; db.AuthorCircles.Add(relation); }
                relation.Source = "User"; relation.UserConfirmed = confirmed; relation.Disabled = disabled;
            });
        }
        public void RemoveRelationship(int authorId, int groupId)
        {
            if (authorId < 0 || groupId < 0) { lock (_sync) _sessionOverlay.AuthorCircles.RemoveAll(x => x.AuthorId == authorId && x.CircleId == groupId); RaiseChanged(); return; }
            Mutate(db => db.AuthorCircles.RemoveAll(x => x.AuthorId == authorId && x.CircleId == groupId));
        }
        public void SavePublicRelationship(string overrideId, int groupId, bool confirmed, bool disabled)
        {
            Mutate(delegate(AuthorEntityDatabase db) {
                PublicEntityOverride record = db.PublicOverrides.First(x => x.Id == overrideId && x.EntityType == "Artist");
                if (!db.Circles.Any(x => x.Id == groupId)) throw new InvalidDataException("Group is missing.");
                PublicUserGroupRelation relation = record.UserGroups.FirstOrDefault(x => x.CircleId == groupId);
                if (relation == null) { relation = new PublicUserGroupRelation { CircleId = groupId }; record.UserGroups.Add(relation); }
                relation.UserConfirmed = confirmed; relation.Disabled = disabled;
            });
        }
        public void RemovePublicRelationship(string overrideId, int groupId)
        {
            Mutate(db => db.PublicOverrides.First(x => x.Id == overrideId).UserGroups.RemoveAll(x => x.CircleId == groupId));
        }
        public void ClearLookupCache() { Mutate(db => db.LookupCache.Clear(), () => _sessionOverlay.LookupCache.Clear(), false); }
        public void ExportUserLibrary(string path)
        {
            if (String.Equals(System.IO.Path.GetFullPath(path), System.IO.Path.GetFullPath(_path), StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Choose an export path outside the active entity file.");
            AuthorEntityDatabase db = Load();
            new AuthorEntityStore(path).SaveInternal(db);
        }
        public void ImportUserLibrary(string path)
        {
            if (String.Equals(System.IO.Path.GetExtension(path), ".txt", StringComparison.OrdinalIgnoreCase))
            {
                List<AliasGroup> imported = LegacyAliasImporter.Read(path);
                string source = "import:" + ContentHash(File.ReadAllBytes(path));
                string sourceBackup = _path + ".import-" + source.Substring(7).Replace('/', '_').Replace('+', '-') + ".bak";
                if (!File.Exists(sourceBackup)) File.Copy(path, sourceBackup, false);
                Mutate(delegate(AuthorEntityDatabase db) {
                    int offset = 0;
                    foreach (AliasGroup group in imported) {
                        string identity = source + ":" + offset++;
                        if (AuthorRules.GetStructuredAuthorParts(group.Canonical) != null) {
                            if (!db.LegacyAliasGroups.Any(x => x.Id == identity)) db.LegacyAliasGroups.Add(new LegacyAliasGroupRecord { Id = identity, Canonical = group.Canonical, Names = group.Names, Status = "Pending" });
                        } else if (!db.Authors.Any(x => x.EntityId == identity)) {
                            int id = NextAuthorId(db);
                            db.Authors.Add(new AuthorEntityRecord { Id = id, EntityId = identity, CanonicalName = group.Canonical, Source = "User/Import", UpdatedUtc = DateTime.UtcNow.ToString("o") });
                            foreach (string name in group.Names) AddAlias(db, id, name, "import", "User/Import");
                        }
                    }
                    foreach (string line in File.ReadAllLines(path, Encoding.UTF8))
                        if (!String.IsNullOrWhiteSpace(line) && !line.TrimStart().StartsWith("#") && line.Split('|').Count(x => !String.IsNullOrWhiteSpace(x)) < 2 && !db.LegacyUnparsedLines.Contains(line)) db.LegacyUnparsedLines.Add(line);
                });
                return;
            }
            AuthorEntityDatabase incoming = ReadDatabase(path);
            string importIdentity = "import:" + ContentHash(File.ReadAllBytes(path));
            // Legacy integer-derived IDs are scoped to their source library;
            // two separate libraries may both contain user:1.
            foreach (AuthorEntityRecord record in incoming.Authors)
                if (record.EntityId == "user:" + record.Id) record.EntityId = importIdentity + ":artist:" + record.Id;
            foreach (CircleEntityRecord record in incoming.Circles)
                if (record.EntityId == "group:" + record.Id || record.EntityId == "user:" + record.Id) record.EntityId = importIdentity + ":group:" + record.Id;
            // Preserve the complete source so conflicting imported decisions can be reviewed without loss.
            string backup = _path + ".import-" + ContentHash(File.ReadAllBytes(path)).Replace('/', '_').Replace('+', '-') + ".bak";
            if (!File.Exists(backup)) File.Copy(path, backup, false);
            Mutate(delegate(AuthorEntityDatabase db)
            {
                Dictionary<int, int> artists = new Dictionary<int, int>(), groups = new Dictionary<int, int>();
                foreach (AuthorEntityRecord record in incoming.Authors)
                {
                    AuthorEntityRecord existing = db.Authors.FirstOrDefault(x => x.EntityId == record.EntityId);
                    if (existing != null) { artists[record.Id] = existing.Id; continue; }
                    int originalId = record.Id; record.Id = NextAuthorId(db); artists[originalId] = record.Id; db.Authors.Add(record);
                }
                foreach (CircleEntityRecord record in incoming.Circles)
                {
                    CircleEntityRecord existing = db.Circles.FirstOrDefault(x => x.EntityId == record.EntityId);
                    if (existing != null) { groups[record.Id] = existing.Id; continue; }
                    int originalId = record.Id; record.Id = NextCircleId(db); groups[originalId] = record.Id; db.Circles.Add(record);
                }
                foreach (AuthorAliasRecord record in incoming.Aliases)
                {
                    record.AuthorId = artists[record.AuthorId];
                    if (!db.Aliases.Any(x => x.AuthorId == record.AuthorId && x.Alias == record.Alias)) db.Aliases.Add(record);
                }
                foreach (CircleAliasRecord record in incoming.CircleAliases)
                {
                    record.CircleId = groups[record.CircleId];
                    if (!db.CircleAliases.Any(x => x.CircleId == record.CircleId && x.Alias == record.Alias)) db.CircleAliases.Add(record);
                }
                foreach (AuthorCircleRecord record in incoming.AuthorCircles)
                {
                    record.AuthorId = artists[record.AuthorId]; record.CircleId = groups[record.CircleId];
                    if (!db.AuthorCircles.Any(x => x.AuthorId == record.AuthorId && x.CircleId == record.CircleId)) db.AuthorCircles.Add(record);
                }
                foreach (LegacyAliasGroupRecord record in incoming.LegacyAliasGroups.Where(x => x.AuthorId == 0))
                    if (!db.LegacyAliasGroups.Any(x => x.Id == record.Id)) db.LegacyAliasGroups.Add(record);
                foreach (PublicEntityOverride record in incoming.PublicOverrides)
                    if (!db.PublicOverrides.Any(x => x.Id == record.Id)) {
                        foreach (PublicUserGroupRelation relation in record.UserGroups) relation.CircleId = groups[relation.CircleId];
                        record.Status = "Pending"; db.PublicOverrides.Add(record);
                    }
                foreach (AuthorNameDecision record in incoming.NameDecisions)
                {
                    record.EntityId = record.EntityType == "Artist" ? (artists.ContainsKey(record.EntityId) ? artists[record.EntityId] : 0) : (groups.ContainsKey(record.EntityId) ? groups[record.EntityId] : 0);
                    if (!db.NameDecisions.Any(x => x.EntityType == record.EntityType && AuthorRules.NormalizeText(x.Name) == AuthorRules.NormalizeText(record.Name))) db.NameDecisions.Add(record);
                }
                db.LegacyUnparsedLines.AddRange(incoming.LegacyUnparsedLines.Where(x => !db.LegacyUnparsedLines.Contains(x)));
            });
        }
        public void SetNameDecision(string name, string type, int id, string status)
        {
            if (status != "Confirmed" && status != "Pending" && status != "Independent" && status != "Disabled")
                throw new InvalidDataException("Invalid decision status.");
            Mutate(delegate(AuthorEntityDatabase db)
            {
                if (status == "Confirmed" && !(type == "Artist" ? db.Authors.Any(x => x.Id == id && x.EntityType == "Artist") : db.Circles.Any(x => x.Id == id)))
                    throw new InvalidDataException("Conflict candidate is missing.");
                string norm = AuthorRules.NormalizeText(name);
                db.NameDecisions.RemoveAll(x => x.EntityType == type && AuthorRules.NormalizeText(x.Name) == norm);
                db.NameDecisions.Add(new AuthorNameDecision { Name = name, EntityType = type, EntityId = id, Status = status });
            });
        }
        public void ConfirmPublicName(string name, PublicEntitySnapshot snapshot)
        {
            if (snapshot == null) throw new InvalidDataException("Public candidate is missing.");
            PublicEntitySnapshot current = GetPublicSnapshot(snapshot.Id);
            if (current == null || current.DatabaseVersion != snapshot.DatabaseVersion) throw new InvalidOperationException("Public database changed; refresh candidates.");
            // The explicit decision becomes standalone user data. Later public
            // updates cannot overwrite the user's choice or remove this mapping.
            Mutate(delegate(AuthorEntityDatabase db)
            {
                int id;
                if (snapshot.EntityType == "Artist") {
                    id = NextAuthorId(db);
                    db.Authors.Add(new AuthorEntityRecord { Id = id, EntityId = "user:" + Guid.NewGuid().ToString("N"),
                        CanonicalName = snapshot.CanonicalName, RomanName = snapshot.RomanName, UserConfirmed = true, Verified = true,
                        PublicEntityKey = snapshot.StableKey, ProviderIdentities = new List<string>(snapshot.Identities),
                        VerificationSource = "UserConfirmed", Source = "User/PublicSelection", UpdatedUtc = DateTime.UtcNow.ToString("o") });
                    AddAlias(db, id, name, "user", "User/PublicSelection");
                } else {
                    id = NextCircleId(db);
                    db.Circles.Add(new CircleEntityRecord { Id = id, EntityId = "group:" + Guid.NewGuid().ToString("N"),
                        CanonicalName = snapshot.CanonicalName, RomanName = snapshot.RomanName, UserConfirmed = true,
                        PublicEntityKey = snapshot.StableKey, ProviderIdentities = new List<string>(snapshot.Identities),
                        Source = "User/PublicSelection", UpdatedUtc = DateTime.UtcNow.ToString("o") });
                    db.CircleAliases.Add(new CircleAliasRecord { CircleId = id, Alias = name, Source = "User/PublicSelection" });
                }
                db.NameDecisions.RemoveAll(x => x.EntityType == snapshot.EntityType && AuthorRules.NormalizeText(x.Name) == AuthorRules.NormalizeText(name));
                db.NameDecisions.Add(new AuthorNameDecision { Name = name, EntityType = snapshot.EntityType, EntityId = id, Status = "Confirmed" });
            });
        }
        public List<AuthorLibraryConflict> GetConflicts()
        {
            AuthorEntityDatabase db = Load();
            List<AuthorLibraryConflict> result = new List<AuthorLibraryConflict>();
            var authorAliases = db.Aliases.ToLookup(x => x.AuthorId);
            var circleAliases = db.CircleAliases.ToLookup(x => x.CircleId);
            var authorDisabled = db.Aliases.Where(x => x.Disabled).ToLookup(x => x.AuthorId, x => AuthorRules.NormalizeText(x.Alias));
            var circleDisabled = db.CircleAliases.Where(x => x.Disabled).ToLookup(x => x.CircleId, x => AuthorRules.NormalizeText(x.Alias));
            var entries = db.Authors.Where(x => x.EntityType == "Artist").SelectMany(x => new[] { x.CanonicalName, x.RomanName, x.EHArtistTag, x.NHArtistTag, x.EHTag }.Concat(authorAliases[x.Id].Where(a => !a.Disabled).Select(a => a.Alias))
                .Where(n => !authorDisabled[x.Id].Contains(AuthorRules.NormalizeText(n)))
                .Select(n => new { Name = n, Id = x.Id, Type = "Artist" }))
                .Concat(db.Circles.SelectMany(x => new[] { x.CanonicalName, x.RomanName, x.EHGroupTag, x.NHGroupTag }.Concat(circleAliases[x.Id].Where(a => !a.Disabled).Select(a => a.Alias))
                .Where(n => !circleDisabled[x.Id].Contains(AuthorRules.NormalizeText(n)))
                .Select(n => new { Name = n, Id = x.Id, Type = "Group" }))).ToList();
            foreach (var group in entries.Where(x => !String.IsNullOrWhiteSpace(x.Name)).GroupBy(x => x.Type + ":" + AuthorRules.NormalizeText(x.Name)))
            {
                List<int> ids = group.Select(x => x.Id).Distinct().ToList();
                if (ids.Count < 2) continue;
                var first = group.First();
                AuthorNameDecision decision = db.NameDecisions.FirstOrDefault(x => x.EntityType == first.Type && AuthorRules.NormalizeText(x.Name) == AuthorRules.NormalizeText(first.Name));
                result.Add(new AuthorLibraryConflict { Kind = "Name", Name = first.Name, EntityType = first.Type, Candidates = ids,
                    Status = decision == null ? "Pending" : decision.Status });
            }
            Dictionary<int, PublicEntitySnapshot> snapshots;
            try { snapshots = GetPublicSnapshots(); } catch { snapshots = new Dictionary<int, PublicEntitySnapshot>(); }
            var publicNames = snapshots.Values.SelectMany(x => new[] { x.CanonicalName, x.RomanName }.Concat(x.Aliases).Concat(x.LookupNames.Select(n => n.Value)).Distinct().Select(n => new { Name = n, Id = x.Id, Type = x.EntityType }))
                .Where(x => !String.IsNullOrWhiteSpace(x.Name)).GroupBy(x => x.Type + ":" + AuthorRules.NormalizeText(x.Name));
            var userNames = entries.Where(x => !String.IsNullOrWhiteSpace(x.Name)).ToLookup(x => x.Type + ":" + AuthorRules.NormalizeText(x.Name));
            foreach (var group in publicNames)
            {
                List<int> publicIds = group.Select(x => x.Id).Distinct().ToList();
                List<int> userIds = userNames[group.Key].Select(x => x.Id).Distinct().ToList();
                if (publicIds.Count < 2 && userIds.Count == 0) continue;
                var first = group.First();
                AuthorNameDecision decision = db.NameDecisions.FirstOrDefault(x => x.EntityType == first.Type && AuthorRules.NormalizeText(x.Name) == AuthorRules.NormalizeText(first.Name));
                AuthorLibraryConflict conflict = result.FirstOrDefault(x => x.Kind == "Name" && x.EntityType == first.Type && AuthorRules.NormalizeText(x.Name) == AuthorRules.NormalizeText(first.Name));
                if (conflict == null) { conflict = new AuthorLibraryConflict { Kind = "PublicVsUser", Name = first.Name, EntityType = first.Type, Candidates = userIds, Status = decision == null ? "Pending" : decision.Status }; result.Add(conflict); }
                conflict.PublicCandidates = publicIds;
            }
            foreach (PublicEntityOverride record in db.PublicOverrides)
            {
                List<PublicEntitySnapshot> matches = BindOverride(record, snapshots.Values);
                if (matches.Count != 1 || record.Status != "Active")
                    result.Add(new AuthorLibraryConflict { Kind = "PublicIdentity", Name = record.OriginalCanonicalName,
                        EntityType = record.EntityType, Status = matches.Count == 0 ? "Missing" : "Conflict", OverrideId = record.Id });
            }
            return result;
        }
        public void MergeManualAuthorNames(string canonical, IEnumerable<string> names)
        {
            if (AuthorRules.GetStructuredAuthorParts(canonical) != null)
            {
                LegacyAliasGroupRecord rule = Load().LegacyAliasGroups.FirstOrDefault(x => x.AuthorId == 0 && x.Canonical == canonical);
                SaveFolderRule(rule == null ? "" : rule.Id, canonical,
                    (rule == null ? (IEnumerable<string>)new string[0] : rule.Names).Concat(names ?? new string[0]), false);
                return;
            }
            // Explicit user target selection is evidence; a coincident alias is not.
            AuthorEntityDatabase db = Load();
            List<AuthorEntityRecord> matches = db.Authors.Where(x => x.EntityType == "Artist" && x.UserConfirmed &&
                AuthorRules.NormalizeText(x.CanonicalName) == AuthorRules.NormalizeText(canonical)).ToList();
            AuthorEntityRecord entity = matches.Count == 1 ? matches[0] : null;
            IEnumerable<string> existing = entity == null ? new string[0] : db.Aliases.Where(x => x.AuthorId == entity.Id && !x.Disabled).Select(x => x.Alias);
            SaveUserEntity(entity == null ? 0 : entity.Id, "Artist", canonical, entity == null ? "" : entity.RomanName,
                existing.Concat(names ?? new string[0]), entity == null ? new string[0] : db.Aliases.Where(x => x.AuthorId == entity.Id && x.Disabled).Select(x => x.Alias), true);
        }
    }
}
