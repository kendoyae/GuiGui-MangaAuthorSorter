"""Public-index merge regression against the actual builder Schema v4, portable Python/SQLite."""
import re,sqlite3,tempfile,shutil
from pathlib import Path
root=Path(__file__).resolve().parents[1]
code=(root/'Services/PublicAuthorIndexMergeService.cs').read_text('utf-8')
schema=re.search(r'CreateSql = @"(.*?)";', (root/'Services/PublicIndexSchema.cs').read_text('utf-8'), re.S).group(1).replace('""','"')
merge=re.search(r'private const string MergeSql = @"(.*?)";',code,re.S).group(1)

def merge_into(dbfile,evidence):
    c=sqlite3.connect(dbfile)
    c.execute('PRAGMA foreign_keys=ON')
    c.execute('ATTACH DATABASE ? AS refdata',(str(evidence),))
    c.executescript('BEGIN IMMEDIATE;'+merge+'COMMIT;')
    assert c.execute('PRAGMA quick_check').fetchone()[0]=='ok'
    assert c.execute('PRAGMA foreign_key_check').fetchall()==[]
    result={name:c.execute('SELECT COUNT(*) FROM '+name).fetchone()[0] for name in ('EntityData','ProviderIdentityData','ArtistGroup','ArtistGroupSource','PublicMergeConflict')}
    c.close()
    return result

with tempfile.TemporaryDirectory() as home:
    home=Path(home); base=home/'official.db'; evidence=home/'evidence.db'
    c=sqlite3.connect(base);c.executescript(schema)
    c.execute("INSERT INTO DataSource(SourceKey,DisplayName) VALUES('eh-current','E-Hentai')")
    c.execute("INSERT INTO EntityData(Id,CanonicalName,RomanName,NormalizedCanonical,EHArtistTag,SourceId,ExternalId,EntityType,VerificationSource,Verified,UpdatedUtc) VALUES(5,'artist a','artist a','artist a','artist_a',1,'eh:artist:artist_a','Artist','E-Hentai',1,'now')")
    c.execute("INSERT INTO ProviderIdentityData(EntityId,Provider,Namespace,Tag,NormalizedTag,ExternalId,SourceId,EvidenceCount) VALUES(5,'E-Hentai','artist','artist_a','artist_a','eh:artist:artist_a',1,4)")
    c.commit();c.close()
    e=sqlite3.connect(evidence)
    e.executescript('''CREATE TABLE SourceFile(SourceId TEXT,Path TEXT,Sha TEXT,State TEXT,UpdatedUtc TEXT,PRIMARY KEY(SourceId,Path));
      CREATE TABLE WorkEvidence(SourceId TEXT,SourceFile TEXT,GalleryId TEXT,JapaneseTitle TEXT,EnglishTitle TEXT,ArtistRaw TEXT,GroupRaw TEXT);
      CREATE TABLE EntityEvidence(SourceId TEXT,SourceFile TEXT,GalleryId TEXT,EntityType TEXT,Name TEXT,NormalizedName TEXT,PrettyName TEXT,NormalizedPretty TEXT,Status TEXT,EntityKey TEXT);
      INSERT INTO SourceFile VALUES('nh-metadata-archive','by_month/2026-09.csv','sha','imported','now');
      INSERT INTO EntityEvidence VALUES('nh-metadata-archive','by_month/2026-09.csv','101','Artist','artist a','artist a','artist a','artist a','candidate','');
      INSERT INTO EntityEvidence VALUES('nh-metadata-archive','by_month/2026-09.csv','101','Group','circle c','circle c','circle c','circle c','candidate','');
      INSERT INTO WorkEvidence VALUES('nh-metadata-archive','by_month/2026-09.csv','101','','','','');
      INSERT INTO SourceFile VALUES('eh-tag-aggregate','tag-aggregate','sha','imported','now');
      INSERT INTO EntityEvidence VALUES('eh-tag-aggregate','tag-aggregate','tag:artist:artist_a','Artist','artist_a','artist_a','artist a','artist a','candidate','');
      INSERT INTO EntityEvidence VALUES('eh-tag-aggregate','tag-aggregate','tag:group:circle c','Group','circle c','circle c','circle c','circle c','candidate','');''')
    e.close()
    staging=home/'staging.db';shutil.copy2(base,staging)
    result=merge_into(staging,evidence)
    # Public EH identity is recognized while a separate nH identity remains separate.
    assert result['EntityData']==4,result
    assert result['ProviderIdentityData']==4,result
    assert result['ArtistGroup']==1,result
    assert result['ArtistGroupSource']==1,result
    assert result['PublicMergeConflict']==2,result
    c=sqlite3.connect(staging)
    assert c.execute("SELECT EHArtistTag FROM EntityData WHERE Id=5").fetchone()[0]=='artist_a'
    assert c.execute("SELECT COUNT(*) FROM ProviderIdentityData WHERE Provider='E-Hentai' AND Namespace='artist'").fetchone()[0]==1
    assert c.execute("SELECT COUNT(*) FROM ProviderIdentityData WHERE Provider='nHentai' AND Namespace='artist' AND EntityId<>5").fetchone()[0]==1
    assert c.execute("SELECT COUNT(*) FROM ArtistGroup WHERE EvidenceCount > 0").fetchone()[0]==1
    c.close()
    # Repeat same evidence without duplication.
    assert merge_into(staging,evidence)==result
    # Withdraw both sources and rebuild from base: only original EH author remains.
    e=sqlite3.connect(evidence);e.execute("UPDATE SourceFile SET State='withdrawn'");e.commit();e.close()
    shutil.copy2(base,staging)
    withdrawn=merge_into(staging,evidence)
    assert withdrawn['EntityData']==1 and withdrawn['ArtistGroup']==0 and withdrawn['ProviderIdentityData']==1,withdrawn
    print('PASS: builder Schema v4, EH identity preservation, distinct provider identity, relations, idempotence, withdrawal')
