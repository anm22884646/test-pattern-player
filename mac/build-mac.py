"""Package official Electron distributions, preserving Mac executable modes and symlinks.
On macOS, also ad-hoc sign and verify the resulting app before packaging.
"""
import argparse, hashlib, json, os, pathlib, plistlib, shutil, stat, subprocess, sys, urllib.request, zipfile

VERSION = '44.7.0'
ROOT = pathlib.Path(__file__).resolve().parent
def download(url, target):
    if not target.exists():
        print('Downloading', target.name, flush=True)
        with urllib.request.urlopen(urllib.request.Request(url,headers={'User-Agent':'PatternPlayer-Build'})) as src, open(target,'wb') as dest:
            shutil.copyfileobj(src,dest)
def package(arch, cache, dest):
    name=f'electron-v{VERSION}-darwin-{arch}.zip'
    upstream=f'https://github.com/electron/electron/releases/download/v{VERSION}/'
    checksum=cache/'SHASUMS256.txt'
    download(upstream+'SHASUMS256.txt',checksum)
    archive=cache/name;download(upstream+name,archive)
    expected=next(line.split()[0] for line in checksum.read_text().splitlines() if line.split()[-1].lstrip('*')==name)
    if hashlib.file_digest(open(archive,'rb'),'sha256').hexdigest()!=expected:raise RuntimeError('Electron checksum mismatch')
    appname='Pattern Player.app'
    appfiles={}
    for p in ROOT.iterdir():
        if p.is_file() and p.suffix in ['.cjs','.js','.html','.css','.json']:
            appfiles[f'{appname}/Contents/Resources/app/{p.name}']=p.read_bytes()
    for folder in ['TestPatterns','Audio']:
        for p in (ROOT/folder).iterdir():
            appfiles[f'{appname}/Contents/Resources/app/{folder}/{p.name}']=p.read_bytes()
    if (ROOT/'app.icns').exists():appfiles[f'{appname}/Contents/Resources/electron.icns']=(ROOT/'app.icns').read_bytes()
    out=dest/f'PatternPlayer-v0.4.0-mac-{arch}.zip'
    with zipfile.ZipFile(archive) as src, zipfile.ZipFile(out,'w',zipfile.ZIP_DEFLATED,compresslevel=6) as dst:
        for item in src.infolist():
            if not item.filename.startswith('Electron.app/') or '/_CodeSignature/' in item.filename:continue
            data=src.read(item)
            filename=item.filename.replace('Electron.app/',appname+'/',1)
            if filename in appfiles:continue
            if item.filename=='Electron.app/Contents/Info.plist':
                info=plistlib.loads(data);info.update(CFBundleIdentifier='com.anm.patternplayer',CFBundleName='Pattern Player',CFBundleDisplayName='Pattern Player',CFBundleShortVersionString='0.4.0',CFBundleVersion='0.4.0',NSMicrophoneUsageDescription='Audio Check uses output devices only; microphone capture is not required.')
                data=plistlib.dumps(info)
            entry=zipfile.ZipInfo(filename,item.date_time);entry.create_system=3;entry.external_attr=item.external_attr;entry.compress_type=zipfile.ZIP_DEFLATED;dst.writestr(entry,data)
        for filename,data in appfiles.items():
            entry=zipfile.ZipInfo(filename);entry.create_system=3;entry.external_attr=(stat.S_IFREG|0o644)<<16;entry.compress_type=zipfile.ZIP_DEFLATED;dst.writestr(entry,data)
        for filename in ['LICENSE','LICENSES.chromium.html']:
            if filename in src.namelist():dst.writestr(filename,src.read(filename))
        dst.writestr('README.md',(ROOT/'README.md').read_bytes())
    with zipfile.ZipFile(out) as check:
        executable=check.getinfo(appname+'/Contents/MacOS/Electron')
        if not ((executable.external_attr>>16)&0o111):raise RuntimeError('Executable permission missing')
        if len([i for i in check.infolist() if stat.S_ISLNK(i.external_attr>>16)])<1:raise RuntimeError('Framework symlinks missing')
    if sys.platform=='darwin':
        stage=dest/f'stage-{arch}';stage.mkdir(exist_ok=True)
        subprocess.run(['ditto','-x','-k',str(out),str(stage)],check=True)
        bundle=stage/appname
        subprocess.run(['codesign','--force','--deep','--sign','-','--entitlements',str(ROOT/'entitlements.plist'),str(bundle)],check=True)
        subprocess.run(['codesign','--verify','--deep','--strict',str(bundle)],check=True)
        subprocess.run(['ditto','-c','-k','--sequesterRsrc','--keepParent',str(bundle),str(out)],check=True)
        # Include usage and third-party license documents at ZIP root.
        with zipfile.ZipFile(out,'a',zipfile.ZIP_DEFLATED) as z:
            z.write(ROOT/'README.md','README.md')
            for filename in ['LICENSE','LICENSES.chromium.html']:
                p=stage/filename
                if p.exists():z.write(p,filename)
    print(json.dumps({'archive':str(out),'bytes':out.stat().st_size,'sha256':hashlib.file_digest(open(out,'rb'),'sha256').hexdigest(),'signed':sys.platform=='darwin'}),flush=True)
    return out
if __name__=='__main__':
    ap=argparse.ArgumentParser();ap.add_argument('--arch',choices=['arm64','x64','both'],default='both');ap.add_argument('--cache',default=str(ROOT/'build-cache'));ap.add_argument('--output',default=str(ROOT/'dist'));args=ap.parse_args()
    cache=pathlib.Path(args.cache);dest=pathlib.Path(args.output);cache.mkdir(parents=True,exist_ok=True);dest.mkdir(parents=True,exist_ok=True)
    for arch in (['arm64','x64'] if args.arch=='both' else [args.arch]):package(arch,cache,dest)
