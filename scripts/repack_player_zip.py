"""Repack the checked release with optional patches; do not rebuild runtime code."""
import hashlib,json,zipfile
from pathlib import Path
from verify_repo import ROOT,player_entries,verify_hashes,verify_archive

def digest(path):return hashlib.sha256(path.read_bytes()).hexdigest().upper()

def main():
    path=ROOT/'release-files.json';manifest=json.loads(path.read_text(encoding='utf-8-sig'));version=manifest['version']
    player=f'dist/v{version}/OfflineTeamBuilder-{version}.zip'
    source=f'dist/v{version}/OfflineTeamBuilder-{version}-source.zip'
    verify_hashes(ROOT,{k:v for k,v in manifest['files'].items()if k not in [player,source]})
    entries=player_entries(ROOT,version)
    target=ROOT/player;temporary=target.with_suffix('.zip.tmp')
    with zipfile.ZipFile(temporary,'w',compression=zipfile.ZIP_DEFLATED,compresslevel=9)as archive:
        for name,file in entries.items():
            info=zipfile.ZipInfo(name,(2026,11,11,0,0,0));info.compress_type=zipfile.ZIP_DEFLATED
            archive.writestr(info,(ROOT/file).read_bytes())
    temporary.replace(target)
    # Keep the source archive's install instructions in sync; code/native inputs
    # stay byte-for-byte identical and are independently checked by verify_repo.
    target=ROOT/source;temporary=target.with_suffix('.zip.tmp')
    with zipfile.ZipFile(target)as before,zipfile.ZipFile(temporary,'w',compression=zipfile.ZIP_DEFLATED,compresslevel=9)as after:
        for info in before.infolist():
            data=(ROOT/'runtime/USER-README.txt').read_bytes()if info.filename=='USER-README.txt'else before.read(info.filename)
            after.writestr(info,data)
    temporary.replace(target)
    compatibility=ROOT/'assets/compatibility'
    for file in compatibility.iterdir():
        if file.is_file():manifest['files'][file.relative_to(ROOT).as_posix()]=digest(file)
    manifest['files'][player]=digest(ROOT/player);manifest['files'][source]=digest(ROOT/source)
    path.write_text(json.dumps(manifest,indent=2)+'\n')
    verify_archive(ROOT,version)
    print('Repacked',player,'with',len(entries),'checked files; core DLL/fbmod unchanged.')

if __name__=='__main__':main()
