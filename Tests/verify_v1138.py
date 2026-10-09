"""Static release checks for V1.13.8 (portable; does not compile C#)."""
from pathlib import Path
import re
import xml.etree.ElementTree as ET
import json

root = Path(__file__).resolve().parents[1]
program = (root/'Program.cs').read_text(encoding='utf-8-sig')
for clause in ['AssemblyVersion("1.13.8.0")', 'AssemblyFileVersion("1.13.8.0")', 'AssemblyInformationalVersion("1.13.8")']:
    assert clause in program, clause

ns = {'m': 'http://schemas.microsoft.com/developer/msbuild/2003'}
project = ET.parse(root/'MangaAuthorSorter.csproj')
project_sources = [x.attrib['Include'] for x in project.findall('.//m:Compile', ns)]
rsp_sources = [x.strip() for x in (root/'Build/CompilerSources.rsp').read_text(encoding='utf-8-sig').splitlines() if x.strip()]
assert len(project_sources) == len(rsp_sources) == 79
assert len(set(s.casefold() for s in rsp_sources)) == len(rsp_sources)
assert set(s.casefold() for s in project_sources) == set(s.casefold() for s in rsp_sources)
assert not any('/' in s for s in rsp_sources), 'Windows csc.exe response file may misinterpret forward slashes'
assert all((root/Path(s.replace('\\','/'))).is_file() for s in rsp_sources)
for file in ['Services\\AdvancedSourceService.cs','Services\\AdvancedSourcePreparation.cs','Services\\AdvancedSourceSqliteReader.cs',
             'Services\\AdvancedOperationControl.cs','Forms\\AdvancedAuthorBuilderForm.cs']:
    assert file in rsp_sources
build = (root/'BUILD_EXE.cmd').read_text(encoding='utf-8-sig')
validator = (root/'Build/ValidateSources.ps1').read_text(encoding='utf-8-sig')
assert 'Build\\ValidateSources.ps1' in build and 'Build\\ValidateLanguages.ps1' in build
assert build.index('Build\\ValidateSources.ps1') < build.index('Build\\ValidateLanguages.ps1') < build.index('Building GuiGui.exe')
for text in ['CompilerSources.rsp', 'MangaAuthorSorter.csproj', 'Test-Path', 'Windows backslashes', 'Compiler source missing']:
    assert text in validator, text
packs = {p.stem: json.loads(p.read_text(encoding='utf-8-sig')) for p in (root/'Languages').glob('*.json')}
assert len(packs) == 4
sets = [set(p['strings']) for p in packs.values()]
assert all(s == sets[0] for s in sets)
assert len(sets[0]) == 1222
assert 'V1.13.8' in (root/'CHANGELOG.md').read_text(encoding='utf-8-sig')
print(f'[OK] V1.13.8: {len(rsp_sources)} Windows source paths match project and all files exist')
print(f'[OK] Four language packs: {len(sets[0])} consistent keys')
print('[OK] BUILD_EXE.cmd source validation and version metadata')
