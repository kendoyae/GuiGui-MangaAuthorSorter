// Schema v4 SQL extracted from GuiGui-AuthorDbBuilder V1.13.5.
// Keep this schema contract in sync with the builder when changing the index format.
namespace MangaAuthorSorter
{
    internal static class PublicIndexSchema
    {
        internal const string CreateSql = @"            CREATE TABLE IF NOT EXISTS DataSource(Id INTEGER PRIMARY KEY,SourceKey TEXT NOT NULL UNIQUE,DisplayName TEXT NOT NULL UNIQUE);
            CREATE TABLE IF NOT EXISTS EntityData(Id INTEGER PRIMARY KEY,CanonicalName TEXT NOT NULL,RomanName TEXT NOT NULL DEFAULT '',NormalizedCanonical TEXT NOT NULL,EHArtistTag TEXT NOT NULL DEFAULT '',NHArtistTag TEXT NOT NULL DEFAULT '',NHGroupTag TEXT NOT NULL DEFAULT '',DanbooruArtistTag TEXT NOT NULL DEFAULT '',SourceId INTEGER REFERENCES DataSource(Id),ExternalId TEXT NOT NULL DEFAULT '',EntityType TEXT NOT NULL CHECK(EntityType IN ('Artist','Group')),VerificationSource TEXT NOT NULL DEFAULT 'Unverified',EHNamespace TEXT NOT NULL DEFAULT '',EHTag TEXT NOT NULL DEFAULT '',Verified INTEGER NOT NULL DEFAULT 0 CHECK(Verified IN (0,1)),UpdatedUtc TEXT NOT NULL);
            CREATE INDEX IF NOT EXISTS IX_Entity_NormalizedName ON EntityData(NormalizedCanonical);
            CREATE INDEX IF NOT EXISTS IX_Entity_TypeName ON EntityData(EntityType,NormalizedCanonical);
            CREATE INDEX IF NOT EXISTS IX_Entity_ExternalId ON EntityData(ExternalId);
            DROP INDEX IF EXISTS IX_Entity_SourceId;
            DROP VIEW IF EXISTS Entity;
            CREATE VIEW Entity AS SELECT e.Id,e.CanonicalName,e.RomanName,e.NormalizedCanonical,e.EHArtistTag,e.NHArtistTag,e.NHGroupTag,e.DanbooruArtistTag,COALESCE(s.DisplayName,'') AS Source,e.ExternalId,e.EntityType,e.VerificationSource,e.EHNamespace,e.EHTag,e.Verified,e.UpdatedUtc,e.SourceId FROM EntityData e LEFT JOIN DataSource s ON s.Id=e.SourceId;
            CREATE TABLE IF NOT EXISTS EntityAliasData(EntityId INTEGER NOT NULL REFERENCES EntityData(Id) ON DELETE CASCADE,Alias TEXT NOT NULL,NormalizedAlias TEXT NOT NULL,AliasType TEXT NOT NULL,SourceId INTEGER REFERENCES DataSource(Id),PRIMARY KEY(EntityId,NormalizedAlias)) WITHOUT ROWID;
            CREATE INDEX IF NOT EXISTS IX_EntityAlias_Normalized ON EntityAliasData(NormalizedAlias);
            DROP INDEX IF EXISTS IX_EntityAlias_SourceId;
            DROP VIEW IF EXISTS EntityAlias;
            CREATE VIEW EntityAlias AS SELECT a.EntityId,a.Alias,a.NormalizedAlias,a.AliasType,COALESCE(s.DisplayName,'') AS Source,a.SourceId FROM EntityAliasData a LEFT JOIN DataSource s ON s.Id=a.SourceId;
            CREATE TABLE IF NOT EXISTS ProviderIdentityData(EntityId INTEGER NOT NULL REFERENCES EntityData(Id) ON DELETE CASCADE,Provider TEXT NOT NULL,Namespace TEXT NOT NULL CHECK(Namespace IN ('artist','group')),Tag TEXT NOT NULL,NormalizedTag TEXT NOT NULL,ExternalId TEXT NOT NULL,SourceId INTEGER REFERENCES DataSource(Id),EvidenceCount INTEGER NOT NULL DEFAULT 1,PRIMARY KEY(Provider,Namespace,NormalizedTag)) WITHOUT ROWID;
            CREATE INDEX IF NOT EXISTS IX_ProviderIdentity_Entity ON ProviderIdentityData(EntityId);
            CREATE INDEX IF NOT EXISTS IX_ProviderIdentity_NamespaceTag ON ProviderIdentityData(Namespace,NormalizedTag,EntityId);
            DROP INDEX IF EXISTS IX_ProviderIdentity_SourceId;
            DROP VIEW IF EXISTS ProviderIdentity;
            CREATE VIEW ProviderIdentity AS SELECT p.EntityId,p.Provider,p.Namespace,p.Tag,p.NormalizedTag,p.ExternalId,COALESCE(s.DisplayName,'') AS Source,p.EvidenceCount,p.SourceId FROM ProviderIdentityData p LEFT JOIN DataSource s ON s.Id=p.SourceId;
            CREATE TABLE IF NOT EXISTS ArtistGroup(ArtistId INTEGER NOT NULL REFERENCES EntityData(Id),GroupId INTEGER NOT NULL REFERENCES EntityData(Id),EvidenceCount INTEGER NOT NULL DEFAULT 1,PRIMARY KEY(ArtistId,GroupId),CHECK(ArtistId<>GroupId)) WITHOUT ROWID;
            CREATE TABLE IF NOT EXISTS ArtistGroupSource(ArtistId INTEGER NOT NULL,GroupId INTEGER NOT NULL,SourceId TEXT NOT NULL,EvidenceCount INTEGER NOT NULL DEFAULT 1,PRIMARY KEY(ArtistId,GroupId,SourceId),FOREIGN KEY(ArtistId,GroupId) REFERENCES ArtistGroup(ArtistId,GroupId) ON DELETE CASCADE) WITHOUT ROWID;
            CREATE TRIGGER IF NOT EXISTS TR_ArtistGroup_Types_Insert BEFORE INSERT ON ArtistGroup BEGIN
                SELECT CASE WHEN (SELECT EntityType FROM Entity WHERE Id=NEW.ArtistId)<>'Artist' OR (SELECT EntityType FROM Entity WHERE Id=NEW.GroupId)<>'Group' THEN RAISE(ABORT,'ArtistGroup requires Artist -> Group') END;
            END;
            CREATE TRIGGER IF NOT EXISTS TR_ArtistGroup_Types_Update BEFORE UPDATE OF ArtistId,GroupId ON ArtistGroup BEGIN
                SELECT CASE WHEN (SELECT EntityType FROM Entity WHERE Id=NEW.ArtistId)<>'Artist' OR (SELECT EntityType FROM Entity WHERE Id=NEW.GroupId)<>'Group' THEN RAISE(ABORT,'ArtistGroup requires Artist -> Group') END;
            END;
            PRAGMA user_version=4;";
    }
}
