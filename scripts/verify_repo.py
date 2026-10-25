"""Check the published release without MMC, game binaries or network access."""
import hashlib
import importlib.util
import json
from pathlib import Path, PurePosixPath
import re
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
    }
    with zipfile.ZipFile(root / f'dist/v{version}/OfflineTeamBuilder-{version}.zip') as archive:
        entries = [entry.filename for entry in archive.infolist() if not entry.is_dir()]
        if len(entries) != 4 or set(entries) != set(expected) | {'README.txt'}:
            raise ValueError('Unexpected release ZIP contents')
        for name, source in expected.items():
            if archive.read(name) != (root / source).read_bytes():
                raise ValueError('ZIP/source mismatch: '+name)
        readme = archive.read('README.txt').decode('utf-8-sig')
        if AUTHORS not in readme or version not in readme:
            raise ValueError('Release README identity mismatch')


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
    profile = json.loads((root / 'runtime/Profiles/cfb27-2026-10-19.json').read_text(encoding='utf-8-sig'))
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
    return version


if __name__ == '__main__':
    try:
        print('PASS: release '+check()+' file hashes, ZIP, project, version and credits.')
        print('DLL/game execution is not part of these repository checks.')
    except Exception as error:
        print('FAIL: '+str(error), file=sys.stderr)
        sys.exit(1)
