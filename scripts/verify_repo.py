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


def compatibility_entries(root, version):
    folder = root / 'assets/compatibility'
    manifest = json.loads((folder / 'manifest.json').read_text(encoding='utf-8-sig'))
    if manifest['teamBuilderVersion'] != version:
        raise ValueError('Compatibility bundle version mismatch')
    for name in manifest['files']:
        path = PurePosixPath(name)
        if len(path.parts) != 1 or path.name != name or ':' in name or '\\' in name:
            raise ValueError('Invalid compatibility filename: '+name)
        if name != 'README.txt' and path.suffix not in ('.fbmod', '.fbproject'):
            raise ValueError('Unexpected compatibility file: '+name)
    verify_hashes(folder, manifest['files'])
    wanted = ['README.txt'] + [patch['file'] for patch in manifest['patches']]
    if len(wanted) != len(set(wanted)) or manifest['playerFiles'] != wanted:
        raise ValueError('Compatibility player-file list differs from selected patches')
    for patch in manifest['patches']:
        if patch['file'] not in manifest['files'] or not patch['file'].endswith('.fbmod'):
            raise ValueError('Missing compatibility fbmod')
        if patch['project'] not in manifest['files'] or not patch['project'].endswith('.fbproject'):
            raise ValueError('Missing editable compatibility project')
    return {'compatibility patches/'+name: 'assets/compatibility/'+name for name in wanted}


def player_entries(root, version):
    expected = {
        'OfflineTeamBuilder.fbmod': 'assets/OfflineTeamBuilder.fbmod',
        'Plugins/OfflineTeamBuilderRuntime.dll': f'dist/v{version}/OfflineTeamBuilderRuntime.dll',
        'README.txt': 'runtime/USER-README.txt',
    }
    expected.update(compatibility_entries(root, version))
    return expected


def verify_archive(root, version):
    expected = player_entries(root, version)
    with zipfile.ZipFile(root / f'dist/v{version}/OfflineTeamBuilder-{version}.zip') as archive:
        entries = [entry.filename for entry in archive.infolist() if not entry.is_dir()]
        if len(entries) != len(expected) or set(entries) != set(expected):
            raise ValueError('Player ZIP differs from core files and checked optional compatibility patches')
        for name, source in expected.items():
            if archive.read(name) != (root / source).read_bytes():
                raise ValueError('ZIP/source mismatch: '+name)
        readme = archive.read('README.txt').decode('utf-8-sig')
        if version not in readme:
            raise ValueError('Release README version mismatch')


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



def verify_current_archives(root, version):
    dist = root / f'dist/v{version}'
    player_files = {
        'Plugins/OfflineTeamBuilderRuntime.dll': dist / 'OfflineTeamBuilderRuntime.dll',
        'OfflineTeamBuilder.fbmod': dist / 'OfflineTeamBuilder.fbmod',
        'TeamBuilderSharedUi.fbmod': dist / 'TeamBuilderSharedUi.fbmod',
        'README.txt': root / 'runtime/USER-README.txt',
        'CUSTOM-ARTWORK.txt': root / 'runtime/CUSTOM-ARTWORK.txt',
    }
    with zipfile.ZipFile(dist / f'OfflineTeamBuilder-{version}.zip') as archive:
        if len(archive.namelist()) != len(player_files) or set(archive.namelist()) != player_files.keys():
            raise ValueError('Unexpected player ZIP entries')
        for name, path in player_files.items():
            if archive.read(name) != path.read_bytes():
                raise ValueError('Player ZIP/source mismatch: '+name)

    inputs = {
        'Native/bin/Release/OfflineTeamBuilderNative.dll': dist / 'OfflineTeamBuilderNative.dll',
        'ArtworkNative/bin/OfflineTeamBuilder.ArtworkCodec.dll': dist / 'OfflineTeamBuilder.ArtworkCodec.dll',
        'CREDITS.md': root / 'CREDITS.md',
    }
    with zipfile.ZipFile(dist / f'OfflineTeamBuilder-{version}-source.zip') as archive:
        names = archive.namelist()
        if len(names) != len(set(names)) or not inputs.keys() <= set(names):
            raise ValueError('Unexpected source ZIP entries')
        for name in names:
            parts = PurePosixPath(name).parts
            if not parts or PurePosixPath(name).is_absolute() or '..' in parts or ':' in name or '\\' in name:
                raise ValueError('Unsafe source archive path: '+name)
            path = inputs.get(name, root / 'runtime' / name)
            if not path.is_file():
                raise ValueError('Missing repository source for '+name)
            expected = path.read_bytes()
            archived = archive.read(name)
            if name == 'CREDITS.md':
                expected = expected.replace(b'\r\n', b'\n')
                archived = archived.replace(b'\r\n', b'\n')
            if archived != expected:
                raise ValueError('Source ZIP/repository mismatch: '+name)
        if not {'build.ps1', 'SharedUi/worker.zip', 'Host/Program.cs', 'src/PluginIdentity.cs'} <= set(names):
            raise ValueError('Missing release source')

    resources = clr_resources(player_files['Plugins/OfflineTeamBuilderRuntime.dll'].read_bytes())
    native = inputs['Native/bin/Release/OfflineTeamBuilderNative.dll'].read_bytes()
    native_manifest = (root / 'runtime/Native/native-helper-manifest.json').read_bytes()
    if hashlib.sha256(native).hexdigest() != json.loads(native_manifest.decode('utf-8-sig'))['sha256'].lower():
        raise ValueError('Native helper hash mismatch')
    host_manifest = (root / 'runtime/Host/host-manifest.json').read_bytes()
    worker = (root / 'runtime/SharedUi/worker.zip').read_bytes()
    for payload in (native, native_manifest, host_manifest, worker):
        if struct.pack('<I', len(payload)) + payload not in resources:
            raise ValueError('Release input differs from embedded resources')

    header = load_module('release_mod_header', root / 'runtime/tools/rebrand_fbmod.py').header
    for filename, title in (('OfflineTeamBuilder.fbmod', 'Offline TeamBuilder'),
                            ('TeamBuilderSharedUi.fbmod', 'Offline TeamBuilder Shared UI (Automatic)')):
        if header((dist / filename).read_bytes())['details'][0] != title:
            raise ValueError('Unexpected fbmod identity: '+filename)

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
    if version == '2.4.13':
        verify_current_archives(root, version)
        return version
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
        print('PASS: release '+check()+' file hashes, ZIPs, version and credits.')
        print('DLL/game execution is not part of these repository checks.')
    except Exception as error:
        print('FAIL: '+str(error), file=sys.stderr)
        sys.exit(1)
