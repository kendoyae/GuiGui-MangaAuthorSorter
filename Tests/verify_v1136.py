"""Portable source, language, contract and project manifest checks, no Windows compiler required."""
from pathlib import Path
from collections import Counter
import re,json,xml.etree.ElementTree as ET
p=Path(__file__).resolve().parents[1]

def read(relative):return (p/relative).read_text(encoding='utf-8-sig')

def no_duplicates(items):
    pairs=[]
    def parse(ps):
        keys=[k for k,v in ps]
        assert len(keys)==len(set(keys)),[k for k,n in Counter(keys).items() if n>1]
        return dict(ps)
    return json.loads(items,object_pairs_hook=parse)
langs={n:no_duplicates(read(f'Languages/{n}.json')) for n in ('zh-CN','en-US','de-DE','_template')}
keys=set(langs['zh-CN']['strings'])
assert all(set(v['strings'])==keys for v in langs.values())
assert all(v['meta']['version']==83 for v in langs.values())
for k in keys:
    patterns=[tuple(re.findall(r'\{\d+(?::[^}]+)?\}',v['strings'][k])) for v in langs.values()]
    assert len(set(patterns))==1,(k,patterns)
print('PASS: four language JSON packs have identical keys and placeholders:',len(keys))
manager=read('Services/LanguageManager.cs')
zh=manager[manager.index('private static Dictionary<string, string> CreateZhStrings()'):manager.index('private static Dictionary<string, string> CreateEnStrings()')]
en=manager[manager.index('private static Dictionary<string, string> CreateEnStrings()'):]
de=read('Services/GermanLanguagePack.cs')
for name,text,pattern in [('zh-CN',zh,r'^\s*\{\s*"([\w.-]+)"\s*,'),('en-US',en,r'^\s*\{\s*"([\w.-]+)"\s*,'),('de-DE',de,r'^\s*d\["([\w.-]+)"\]\s*=')]:
    ks=re.findall(pattern,text,re.M)
    assert len(ks)==len(set(ks)) and set(ks)==keys,(name,len(ks),len(keys),set(ks)^keys)
print('PASS: three built-in language dictionaries match and contain no duplicates')
project=ET.fromstring(read('MangaAuthorSorter.csproj'))
ns={'p':'http://schemas.microsoft.com/developer/msbuild/2003'}
files=set(x.attrib['Include'].replace('\\','/') for x in project.findall('.//p:Compile',ns))
rsp=set(l.replace('\\','/') for l in read('Build/CompilerSources.rsp').splitlines() if l.strip())
assert files==rsp,(files^rsp)
assert all((p/x).is_file() for x in files)
print('PASS: project and compiler source manifests:',len(files),'C# files')
assert 'AssemblyVersion("1.13.7.0")' in read('Program.cs')
assert 'BuiltInLanguageVersion = 83' in manager
assert read('CHANGELOG.md').startswith('## V1.13.7')
assert 'return "GuiGui/"' not in read('Services/AuthorReferenceLibraryService.cs')
assert 'AppVersion.UserAgentVersion' in read('Services/AuthorReferenceLibraryService.cs')
conf=no_duplicates(read('SourcesConfig.json'))
assert conf['schemaVersion']==1 and conf['publicDatabase']['manifestUrl']==''
assert 'net8' not in read('MangaAuthorSorter.csproj').lower()
print('PASS: 1.13.7 versions, remote configuration, no .NET 8 runtime dependency')

def balance(s):
    pairs={')':'(',']':'[','}':'{'};stack=[];mode='code';i=0
    while i<len(s):
        c=s[i];d=s[i+1] if i+1<len(s) else ''
        if mode=='code':
            if c=='/' and d=='/':mode='line';i+=2;continue
            if c=='/' and d=='*':mode='block';i+=2;continue
            if c=='@' and d=='"':mode='verbatim';i+=2;continue
            if s[i:i+3]=='"""':mode='raw';i+=3;continue
            if c=='"':mode='string';i+=1;continue
            if c=="'":mode='char';i+=1;continue
            if c in '({[':stack.append(c)
            if c in ')}]':assert stack and stack.pop()==pairs[c],(i,c,s[i-45:i+45])
        elif mode=='line':
            if c=='\n':mode='code'
        elif mode=='block':
            if c=='*' and d=='/':mode='code';i+=2;continue
        elif mode in ['string','char']:
            if c=='\\':i+=2;continue
            if c==('"' if mode=='string' else "'"):mode='code'
        elif mode=='raw':
            if s[i:i+3]=='"""':mode='code';i+=3;continue
        elif mode=='verbatim':
            if c=='"' and d=='"':i+=2;continue
            if c=='"':mode='code'
        i+=1
    assert not stack and mode in ('code','line'),(stack,mode)
for path in files:balance(read(path))
print('PASS: all C# source files have balanced delimiters')
source=read('Services/PublicAuthorIndexMergeService.cs')
for requisite in ['BuildPending(string evidenceDatabasePath, string replacementOfficial = null)', 'public static void StageOfficialIndex(string databasePath)', 'public static bool ActivatePending()', 'CreateEmptyOfficial()', 'VerifyIndex(OfficialNext)', 'File.Replace(Pending, Active','PublicIndexSchema.CreateSql']:
    assert requisite in source,requisite
distribution=read('Services/PublicAuthorDistributionService.cs')
assert 'PublicAuthorIndexMergeService.StageOfficialIndex(dbFile)' in distribution
assert 'EnsureEvidenceStorage()' not in distribution
for requisite in ['CheckOfficialAsync()', 'DownloadOfficialAsync()', 'ImportOfficialAsync()', 'new AdvancedAuthorBuilderForm(_language, Font)']:
    assert requisite in read('Forms/AuthorReferenceLibraryForm.cs'),requisite
print('PASS: three public index acquisition paths and safe staged activation are wired')
print('NOTICE: Windows .NET Framework 4.8 C# compilation and live GUI/network integration not executed on Linux')
