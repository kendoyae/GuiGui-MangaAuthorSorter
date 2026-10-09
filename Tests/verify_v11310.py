"""Static project, language, merge-control checks; not a C# compiler substitute."""
from pathlib import Path
from collections import Counter
import json,re,xml.etree.ElementTree as ET
b=Path(__file__).resolve().parents[1]
program=(b/'Program.cs').read_text('utf-8-sig')
for x in ('AssemblyVersion("1.13.10.0")','AssemblyFileVersion("1.13.10.0")','AssemblyInformationalVersion("1.13.10")'):
    assert x in program,x
lang={p.stem:json.loads(p.read_text('utf-8-sig')) for p in (b/'Languages').glob('*.json')}
assert set(lang)=={'zh-CN','en-US','de-DE','_template'}
keys=set(lang['zh-CN']['strings'])
assert len(keys)==1223
for locale,contents in lang.items():
    assert contents['meta']['version']==84 and set(contents['strings'])==keys,locale
    for key in keys:
        expected=tuple(re.findall(r'\{\d+(?::[^}]+)?\}',lang['zh-CN']['strings'][key]))
        found=tuple(re.findall(r'\{\d+(?::[^}]+)?\}',contents['strings'][key]))
        assert found==expected,(locale,key)
print('PASS: official locale packs:',len(lang),'locale keys:',len(keys))
manager=(b/'Services/LanguageManager.cs').read_text('utf-8-sig')
german=(b/'Services/GermanLanguagePack.cs').read_text('utf-8-sig')
assert 'BuiltInLanguageVersion = 84' in manager
zh=manager[manager.index('private static Dictionary<string, string> CreateZhStrings()'):manager.index('private static Dictionary<string, string> CreateEnStrings()')]
en=manager[manager.index('private static Dictionary<string, string> CreateEnStrings()'):]
for langid,src,pattern in [('zh-CN',zh,r'^\s*\{\s*"([\w.-]+)"\s*,'),('en-US',en,r'^\s*\{\s*"([\w.-]+)"\s*,'),('de-DE',german,r'^\s*d\["([\w.-]+)"\]\s*=')]:
    items=re.findall(pattern,src,re.M)
    assert len(items)==len(set(items)) and set(items)==keys,(langid,len(items),len(set(items)),set(items)^keys)
print('PASS: built-in dictionaries: 1223 unique keys each')
pr=ET.parse(b/'MangaAuthorSorter.csproj');ns={'p':'http://schemas.microsoft.com/developer/msbuild/2003'}
sources=[x.attrib['Include'].replace('\\','/') for x in pr.findall('.//p:Compile',ns)]
rsp=[x.strip().replace('\\','/') for x in (b/'Build/CompilerSources.rsp').read_text('utf-8-sig').splitlines() if x.strip()]
assert len(sources)==len(rsp)==79 and set(sources)==set(rsp)
assert all((b/item).is_file() for item in sources)
print('PASS: 79 compiler inputs match project and exist')
merge=(b/'Services/PublicAuthorIndexMergeService.cs').read_text('utf-8-sig')
form=(b/'Forms/AdvancedAuthorBuilderForm.cs').read_text('utf-8-sig')
service=(b/'Services/AuthorReferenceLibraryService.cs').read_text('utf-8-sig')
for item in ['IX_Incoming_NameMatch','IX_Incoming_EntityMatch','AmbiguousIncomingName','sqlite3_progress_handler','BuildPending(string evidenceDatabasePath','ClearProgressHandler()','"ROLLBACK;"']:
    assert item in merge,item
for item in ['AdvancedBuilder.RebuildMerge','RebuildPublicIndexAsync','RunActionAsync(null']:
    assert item in form,item
assert 'RebuildPublicIndexAsync(' in service
assert 'if (!String.Equals(db.GetImportedSha(file.Source,file.Path),file.Sha' in service
assert 'V1.13.10' in (b/'CHANGELOG.md').read_text('utf-8')
print('PASS: staged index, merge cancellation, incremental remerge, version metadata')
print('LIMIT: Windows csc/WinForms tests not available in current environment')
# Lightweight C# delimiter scan, ignoring strings/comments/verbatim literals.
def balanced(text):
    pairs={')':'(',']':'[','}':'{'}; stack=[]; mode='code';i=0
    while i<len(text):
        c=text[i];d=text[i+1] if i+1<len(text) else ''
        if mode=='code':
            if c=='/' and d=='/':mode='line';i+=2;continue
            if c=='/' and d=='*':mode='block';i+=2;continue
            if c=='@' and d=='"':mode='verbatim';i+=2;continue
            if c=='"':mode='string';i+=1;continue
            if c=="'":mode='char';i+=1;continue
            if c in '({[':stack.append(c)
            if c in ')}]':assert stack and stack.pop()==pairs[c]
        elif mode=='line':
            if c=='\n':mode='code'
        elif mode=='block':
            if c=='*' and d=='/':mode='code';i+=2;continue
        elif mode in ('string','char'):
            if c=='\\':i+=2;continue
            if c==('"' if mode=='string' else "'"):mode='code'
        elif mode=='verbatim':
            if c=='"' and d=='"':i+=2;continue
            if c=='"':mode='code'
        i+=1
    assert not stack and mode in ('code','line'),(stack,mode)
for source in sources:
    balanced((b/source).read_text('utf-8-sig'))
print('PASS: C# source delimiter balance for',len(sources),'compilation units')
