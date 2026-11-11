"""Check the published release without MMC, game binaries or network access."""
import hashlib
import importlib.util
import json
from pathlib import Path, PurePosixPath
import re
import struct
import sys
import zipfile

ROOT = Path(__file__).resolve().parents[1]
AUTHORS = 'JDHalfRack / Ace / Timberline Tex'


def verify_hashes(root, expected):
    for name, digest in expected.items():
        path = PurePosixPath(name)
        if path.is_absolute() or '..' in path.parts or ':' in name or '\\' in name:
            raise ValueError('Invalid release path: '+name)
        actual = hashlib.sha256((root / name).read_bytes()).hexdigest()
        if actual.lower() != digest.lower():
            raise ValueError('Release file changed: '+name)


def verify_identity(source, version):
    for name, value in [('Version', version), ('AssemblyVersion', version+'.0'), ('Authors', AUTHORS)]:
        match = re.search(r'const string '+name+r'\s*=\s*"([^"]+)"', source)
        if match is None or match.group(1) != value:
            raise ValueError('Runtime identity mismatch: '+name)


def verify_archive(root, version):
    expected = {
        'OfflineTeamBuilder.fbmod': 'assets/OfflineTeamBuilder.fbmod',
        'OfflineTeamBuilder.fbproject': 'assets/OfflineTeamBuilder.fbproject',
        'Plugins/OfflineTeamBuilderRuntime.dll': f'dist/v{version}/OfflineTeamBuilderRuntime.dll',
        'README.txt': 'runtime/USER-README.txt',
        'TESTING.txt': 'runtime/TESTING.txt',
        'VALIDATION.txt': 'runtime/VALIDATION.txt',
        'Collect-Support.ps1': 'runtime/collect-diagnostics.ps1',
    }
    with zipfile.ZipFile(root / f'dist/v{version}/OfflineTeamBuilder-{version}.zip') as archive:
        entries = [entry.filename for entry in archive.infolist() if not entry.is_dir()]
        if len(entries) != len(expected)+1 or set(entries) != set(expected) | {'release.json'}:
            raise ValueError('Unexpected release ZIP contents')
        for name, source in expected.items():
            if archive.read(name) != (root / source).read_bytes():
                raise ValueError('ZIP/source mismatch: '+name)
        readme = archive.read('README.txt').decode('utf-8-sig')
        if AUTHORS not in readme or version not in readme:
            raise ValueError('Release README identity mismatch')
        record=json.loads(archive.read('release.json').decode('utf-8-sig'))
        if record['version']!=version or record['authors']!=AUTHORS:
            raise ValueError('Player manifest identity mismatch')
        if {item['path'] for item in record['files']}!=set(expected):
            raise ValueError('Player manifest file allow-list mismatch')
        for item in record['files']:
            if hashlib.sha256(archive.read(item['path'])).hexdigest().lower()!=item['sha256'].lower():
                raise ValueError('Player manifest payload mismatch')
        if [name for name in entries if name.endswith('.dll')]!=['Plugins/OfflineTeamBuilderRuntime.dll']:
            raise ValueError('Player package must contain one installation DLL')


def clr_resources(image):
    pe=struct.unpack_from('<I',image,0x3c)[0]
    if image[:2]!=b'MZ' or image[pe:pe+4]!=b'PE\0\0':
        raise ValueError('Invalid managed PE image')
    count,optional_size=struct.unpack_from('<H',image,pe+6)[0],struct.unpack_from('<H',image,pe+20)[0]
    optional=pe+24
    if struct.unpack_from('<H',image,optional)[0]!=0x20b:
        raise ValueError('Expected AMD64 PE32+ plugin')
    sections=optional+optional_size
    def raw(rva,size):
        for i in range(count):
            p=sections+i*40
            virtual_size,va,raw_size,offset=struct.unpack_from('<IIII',image,p+8)
            if va<=rva and rva+size<=va+raw_size:
                start=offset+rva-va
                if start+size>len(image):raise ValueError('PE resource exceeds file')
                return image[start:start+size]
        raise ValueError('PE resource RVA not backed by file bytes')
    cli_rva,cli_size=struct.unpack_from('<II',image,optional+112+14*8)
    cli=raw(cli_rva,72)
    resources_rva,resources_size=struct.unpack_from('<II',cli,24)
    return raw(resources_rva,resources_size)


def verify_native_and_source(root,version):
    native=(root/f'dist/v{version}/OfflineTeamBuilderNative.dll').read_bytes()
    manifest_bytes=(root/'runtime/Native/native-helper-manifest.json').read_bytes()
    manifest=json.loads(manifest_bytes.decode('utf-8-sig'))
    if hashlib.sha256(native).hexdigest()!=manifest['sha256'].lower():
        raise ValueError('Native build input differs from manifest')
    resources=clr_resources((root/f'dist/v{version}/OfflineTeamBuilderRuntime.dll').read_bytes())
    if struct.pack('<I',len(native))+native not in resources or struct.pack('<I',len(manifest_bytes))+manifest_bytes not in resources:
        raise ValueError('Checked native payload/manifest not present in CLR resources')
    with zipfile.ZipFile(root/f'dist/v{version}/OfflineTeamBuilder-{version}-source.zip') as archive:
        names=[item.filename for item in archive.infolist() if not item.is_dir()]
        if len(names)!=len(set(names)) or any(PurePosixPath(n).is_absolute() or '..' in PurePosixPath(n).parts or ':' in n or '\\' in n for n in names):
            raise ValueError('Unsafe source archive paths')
        binaries=[n for n in names if n.endswith(('.dll','.exe','.pdb','.dmp','.log'))]
        if binaries!=['Native/bin/Release/OfflineTeamBuilderNative.dll']:
            raise ValueError('Unexpected source archive binary/capture')
        if archive.read(binaries[0])!=native:raise ValueError('Source native build input differs')
        for name in names:
            path=root/'runtime'/name
            if name.startswith(('src/','Profiles/','tests/','tools/','Native/')) and path.is_file() and archive.read(name)!=path.read_bytes():
                raise ValueError('Source ZIP/repository mismatch: '+name)


def load_module(name, path):
    spec = importlib.util.spec_from_file_location(name, path)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


def check(root=ROOT):
    manifest = json.loads((root / 'release-files.json').read_text(encoding='utf-8-sig'))
    if manifest['authors'] != AUTHORS or not re.fullmatch(r'\d+\.\d+\.\d+', manifest['version']):
        raise ValueError('Invalid release metadata')
    version = manifest['version']
    verify_hashes(root, manifest['files'])
    verify_identity((root / 'runtime/src/PluginIdentity.cs').read_text(encoding='utf-8-sig'), version)
    profile_name = manifest['nativeProfile']
    if not re.fullmatch(r'cfb27-\d{4}-\d{2}-\d{2}', profile_name):
        raise ValueError('Invalid native profile name')
    profile = json.loads((root / ('runtime/Profiles/'+profile_name+'.json')).read_text(encoding='utf-8-sig'))
    if profile['id'] != manifest['nativeProfile']:
        raise ValueError('Native profile mismatch')
    mod = load_module('release_mod_header', root / 'runtime/tools/rebrand_fbmod.py')
    details = mod.header((root / 'assets/OfflineTeamBuilder.fbmod').read_bytes())['details']
    if details[:4] != ['Offline TeamBuilder', AUTHORS, 'TeamBuilder', version]:
        raise ValueError('Fbmod identity mismatch')
    project = load_module('release_project_verifier', root / 'runtime/tools/verify_fbproject.py')
    report = json.loads((root / 'assets/project-verification.json').read_text(encoding='utf-8-sig'))
    if report['version'] != version or report['authors'] != AUTHORS:
        raise ValueError('Project identity mismatch')
    project.verify((root / 'assets/OfflineTeamBuilder.fbproject').read_bytes(), report)
    verify_archive(root, version)
    verify_native_and_source(root,version)
    return version


if __name__ == '__main__':
    try:
        print('PASS: release '+check()+' file hashes, ZIP, project, version and credits.')
        print('DLL/game execution is not part of these repository checks.')
    except Exception as error:
        print('FAIL: '+str(error), file=sys.stderr)
        sys.exit(1)
