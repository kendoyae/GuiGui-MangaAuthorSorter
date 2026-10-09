from pathlib import Path
import re,json,xml.etree.ElementTree as ET,zipfile
root=Path(__file__).resolve().parents[1]
program=(root/'Program.cs').read_text(encoding='utf-8-sig')
assert 'AssemblyVersion("1.13.7.0")' in program
assert 'AssemblyFileVersion("1.13.7.0")' in program
assert 'AssemblyInformationalVersion("1.13.7")' in program
lang={p.stem:json.loads(p.read_text(encoding='utf-8-sig')) for p in (root/'Languages').glob('*.json')}
assert len(lang)==4
ref=set(lang['zh-CN']['strings'])
for name,pack in lang.items():
 assert pack['meta']['version']==83,(name,pack['meta']['version'])
 assert set(pack['strings'])==ref,(name,len(ref^set(pack['strings'])))
assert 'BuiltInLanguageVersion = 83' in (root/'Services/LanguageManager.cs').read_text()
for loc in ['zh-CN','en-US','de-DE','_template']:
 assert len(lang[loc]['strings'])==1222
# Code project inventory
project=(root/'MangaAuthorSorter.csproj').read_text(encoding='utf-8-sig')
projectpaths=set(x.replace('\\','/') for x in re.findall(r'<Compile Include="([^"]+)"',project))
rsp=set(x.strip().replace('\\','/') for x in (root/'Build/CompilerSources.rsp').read_text().splitlines() if x.strip())
assert rsp==projectpaths,(sorted(projectpaths-rsp), sorted(rsp-projectpaths))
assert all((root/x).is_file() for x in rsp)
for fragment in ['Forms/AdvancedAuthorBuilderForm.cs','Services/AdvancedSourceSqliteReader.cs','Services/AdvancedSourceService.cs','Services/AdvancedSourcePreparation.cs']:
 assert fragment in rsp
form=(root/'Forms/AdvancedAuthorBuilderForm.cs').read_text()
for source in ['eh-current','eh-tag-aggregate','nh-metadata-archive']:
 assert source in (root/'Services/AdvancedSourceService.cs').read_text()
assert 'PublicAuthorIndexMergeService.PrepareFreshBase()' in form
# translated built-in key uniqueness, including direct German map
manager=(root/'Services/LanguageManager.cs').read_text()
de=(root/'Services/GermanLanguagePack.cs').read_text()
for source in [manager,de]:
 for key in ['AdvancedBuilder.Title','AdvancedBuilder.Download','AdvancedBuilder.FreshBase']:
  assert source.count('"'+key+'"')== (2 if source is manager else 1),(key,source is manager)
# No external runtime loaded
assert 'net8' not in project.lower() and 'WPF' not in project
print('PASS: V1.13.7, multilingual packs, 3 sources, compile manifests, zero .NET 8 dependency')
print('compiled units:',len(rsp),'language keys:',len(ref))
