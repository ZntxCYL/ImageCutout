#!/usr/bin/env python3
import argparse,hashlib,json,re,shutil,subprocess,zipfile,xml.etree.ElementTree as ET
from pathlib import Path
root=Path(__file__).resolve().parents[1]; name='ImageCutout-win-x64-Bundled.zip'
p=argparse.ArgumentParser();p.add_argument('--private-key',type=Path,required=True);p.add_argument('--version',required=True);p.add_argument('--archive',type=Path,default=root/'dist'/name);p.add_argument('--out',type=Path,default=root/'dist/release');a=p.parse_args()
if not re.fullmatch(r'\d+\.\d+\.\d+',a.version):p.error('version must be x.y.z')
for proj in (root/'src').glob('*/*.csproj'):
    if ET.parse(proj).findtext('.//Version')!=a.version:p.error('project version does not match release')
if a.private_key.resolve().is_relative_to(root):p.error('keep private key outside source tree')
config=json.loads((root/'src/ImageCutout.Win/update-config.json').read_text()); pub=subprocess.check_output(['openssl','pkey','-in',str(a.private_key),'-pubout']).decode()
if ''.join(pub.split())!=''.join(config['publicKeyPem'].split()) or not (config['repository'] or config['mirrors']):p.error('configure matching sources/public key first')
with zipfile.ZipFile(a.archive) as z:
    required={'ImageCutout.Win.exe','ImageCutout.Updater.exe','ImageCutout.install','update-config.json','model-config.json','onnxruntime.dll','DirectML.dll'}
    if required-set(z.namelist()) or z.testzip():p.error('invalid Windows package')
    for f in ('update-config.json','model-config.json'):
        if json.loads(z.read(f))!=json.loads((root/'src/ImageCutout.Win'/f).read_text()):p.error('rebuild after config changes')
tag='v'+a.version; version_dir=a.out/tag; latest=a.out/'latest'; version_dir.mkdir(parents=True,exist_ok=True);latest.mkdir(parents=True,exist_ok=True)
with a.archive.open('rb') as f: sha=hashlib.file_digest(f,'sha256').hexdigest()
manifest=dict(product='ImageCutout',repository=config['repository'],version=a.version,tag=tag,archive=name,sha256=sha,size=a.archive.stat().st_size)
if not 0<manifest['size']<=2_000_000_000:p.error('archive size exceeds client limit')
mp=version_dir/'update-manifest.json';sp=version_dir/'update-manifest.sig';mp.write_text(json.dumps(manifest,sort_keys=True,separators=(',',':'))+'\n')
subprocess.run(['openssl','dgst','-sha256','-sign',str(a.private_key),'-out',str(sp),str(mp)],check=True)
import tempfile
with tempfile.TemporaryDirectory() as t:
    public=Path(t)/'pub.pem';public.write_text(pub);subprocess.run(['openssl','dgst','-sha256','-verify',str(public),'-signature',str(sp),str(mp)],check=True)
shutil.copy2(a.archive,version_dir/name)
for f in (mp,sp):shutil.copy2(f,latest/f.name)
print('Publish version directory first; publish latest manifest/signature last:',a.out)
