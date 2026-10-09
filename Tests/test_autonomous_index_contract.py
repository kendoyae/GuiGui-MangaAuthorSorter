"""Portable SQL acceptance for V1.13.6; does not replace Windows C# tests.

Run: python Tests/test_autonomous_index_contract.py
"""
from pathlib import Path
import re,sqlite3,tempfile,hashlib,json,shutil
root=Path(__file__).resolve().parents[1]
schema_src=(root/'Services/PublicIndexSchema.cs').read_text()
merge_src=(root/'Services/PublicAuthorIndexMergeService.cs').read_text()
schema=re.search(r'CreateSql = @"(.*?)";',schema_src,re.S).group(1).replace('""','"')
merge=re.search(r'private const string MergeSql = @"(.*?)";',merge_src,re.S).group(1)
with tempfile.TemporaryDirectory() as temp:
    home=Path(temp)
    evidence=home/'GuiGuiReferenceEvidence.db'
    idx=home/'GuiGuiAuthorIndex.db'
    # Empty Schema v4 matches builder's compatibility views and requires no .NET 8.
    c=sqlite3.connect(idx)
    c.executescript('PRAGMA foreign_keys=ON;'+schema)
    assert c.execute('PRAGMA user_version').fetchone()[0]==4
    assert c.execute('SELECT COUNT(*) FROM Entity').fetchone()[0]==0
    assert set(row[0] for row in c.execute("SELECT name FROM sqlite_master WHERE type='view'")) >= {'Entity','EntityAlias','ProviderIdentity'}
    assert c.execute('PRAGMA quick_check').fetchone()[0]=='ok'
    c.commit();c.close()
    official=home/'official.db'
    shutil.copyfile(idx, official)
    def evidence_write():
        e=sqlite3.connect(evidence)
        e.executescript('''
        CREATE TABLE SourceFile(SourceId TEXT, Path TEXT, Sha TEXT, State TEXT, UpdatedUtc TEXT, PRIMARY KEY(SourceId,Path));
        CREATE TABLE EntityEvidence(SourceId TEXT,SourceFile TEXT,GalleryId TEXT,EntityType TEXT,Name TEXT,NormalizedName TEXT,PrettyName TEXT,NormalizedPretty TEXT,Status TEXT,EntityKey TEXT);
        CREATE TABLE WorkEvidence(SourceId TEXT,SourceFile TEXT,GalleryId TEXT);
        INSERT INTO SourceFile VALUES('nh-metadata-archive','by_month/2026-09.csv','abc','imported','today');
        INSERT INTO EntityEvidence VALUES('nh-metadata-archive','by_month/2026-09.csv','100','Artist','artist_a','artist_a','artist a','artist a','candidate','');
        INSERT INTO EntityEvidence VALUES('nh-metadata-archive','by_month/2026-09.csv','100','Group','group_b','group_b','group b','group b','candidate','');
        INSERT INTO WorkEvidence VALUES('nh-metadata-archive','by_month/2026-09.csv','100');
        ''');e.commit();e.close()
    evidence_write()
    def rebuild(base, output):
        shutil.copyfile(base, output)
        c=sqlite3.connect(output)
        c.execute('PRAGMA foreign_keys=ON')
        c.execute('ATTACH DATABASE ? AS refdata',(str(evidence),))
        c.executescript('BEGIN;'+merge+' COMMIT;')
        assert c.execute('PRAGMA quick_check').fetchone()[0]=='ok'
        assert c.execute('PRAGMA foreign_key_check').fetchall()==[]
        names=c.execute('SELECT EntityType,CanonicalName FROM Entity ORDER BY EntityType,CanonicalName').fetchall()
        groups=c.execute('SELECT ArtistId,GroupId FROM ArtistGroup').fetchall()
        c.close()
        return names,groups
    result1=rebuild(official, home/'first.db')
    result2=rebuild(official, home/'second.db')
    assert result1==result2
    assert result1[0]==[('Artist','artist a'),('Group','group b')]
    assert len(result1[1])==1
    # New official baseline contains a verified EH artist; local evidence remaps and preserves EH identity.
    c=sqlite3.connect(official)
    c.execute("INSERT INTO DataSource(SourceKey,DisplayName) VALUES('eh','E-Hentai')")
    c.execute("INSERT INTO EntityData(Id,CanonicalName,RomanName,NormalizedCanonical,EHArtistTag,SourceId,ExternalId,EntityType,VerificationSource,Verified,UpdatedUtc) VALUES(5,'artist a','artist a','artist a','artist_a',1,'eh:artist:artist_a','Artist','E-Hentai',1,'2026')")
    c.execute("INSERT INTO ProviderIdentityData(EntityId,Provider,Namespace,Tag,NormalizedTag,ExternalId,SourceId,EvidenceCount) VALUES(5,'E-Hentai','artist','artist_a','artist_a','eh:artist:artist_a',1,4)")
    c.commit();c.close()
    res,_=rebuild(official,home/'official-merged.db')
    c=sqlite3.connect(home/'official-merged.db')
    # Same canonical spelling across providers is not proof of a shared identity.
    assert c.execute("SELECT COUNT(*) FROM Entity WHERE EntityType='Artist'").fetchone()[0]==2
    assert c.execute("SELECT EHArtistTag FROM Entity WHERE Id=5").fetchone()[0]=='artist_a'
    assert c.execute("SELECT COUNT(*) FROM ProviderIdentity WHERE Provider='nHentai' AND Namespace='artist' AND EntityId<>5").fetchone()[0]==1
    assert c.execute("SELECT COUNT(*) FROM ArtistGroup").fetchone()[0]==1
    c.close()
    # Rebuild after upstream withdrawal revokes local evidence, but official data remains.
    e=sqlite3.connect(evidence);e.execute("UPDATE SourceFile SET State='withdrawn'");e.commit();e.close()
    no_local,_=rebuild(official,home/'revoked.db')
    assert no_local==[('Artist','artist a')]
    # Personal user's JSON is not opened by any merger SQL.
    assert 'AuthorEntities.json' not in merge
    print('PASS: Schema v4 from zero; deterministic replay; official refresh; verified EH identity preserved; withdraw; SQLite integrity')
