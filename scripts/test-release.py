#!/usr/bin/env python3
"""Exercise signing, package metadata and tamper detection without .NET binaries."""
import sys, xml.etree.ElementTree as ET, json, pathlib, shutil, subprocess, tempfile, hashlib, zipfile
root=pathlib.Path(__file__).resolve().parents[1]
version=ET.parse(root/'src/ImageCutout.Win/ImageCutout.Win.csproj').findtext('.//Version')
with tempfile.TemporaryDirectory() as t:
    t=pathlib.Path(t); work=t/'source'; shutil.copytree(root,work,ignore=shutil.ignore_patterns('dist','.git','bin','obj','__pycache__'))
    private=t/'private.pem'; public=t/'public.pem'
    subprocess.run(['openssl','genpkey','-algorithm','RSA','-pkeyopt','rsa_keygen_bits:2048','-out',str(private)],check=True,stdout=subprocess.DEVNULL,stderr=subprocess.DEVNULL)
    subprocess.run(['openssl','pkey','-in',str(private),'-pubout','-out',str(public)],check=True)
    subprocess.run([sys.executable,str(work/'scripts/configure-updates.py'),'--repo','example/imagecutout','--mirror','https://example.invalid/updates/','--model-mirror','https://example.invalid/models/','--public-key',str(public)],check=True)
    fake=t/'fake';fake.mkdir(); win=work/'src/ImageCutout.Win'
    for name in ['ImageCutout.Win.exe','ImageCutout.Updater.exe','onnxruntime.dll','DirectML.dll']:(fake/name).write_bytes(b'fixture, not executable')
    for name in ['update-config.json','model-config.json','ImageCutout.install']:shutil.copy2(win/name,fake/name)
    archive=t/'ImageCutout-win-x64-Bundled.zip';out=t/'release'
    subprocess.run([sys.executable,str(work/'scripts/package.py'),str(fake),str(archive)],check=True)
    subprocess.run([sys.executable,str(work/'scripts/make-update-release.py'),'--version',version,'--private-key',str(private),'--archive',str(archive),'--out',str(out)],check=True)
    d=out/('v'+version); m=d/'update-manifest.json'; s=d/'update-manifest.sig'; meta=json.loads(m.read_text(encoding='utf-8')); data=archive.read_bytes()
    assert meta['sha256']==hashlib.sha256(data).hexdigest() and meta['size']==len(data)
    assert hashlib.sha256(data+b'X').hexdigest()!=meta['sha256']
    assert (out/'latest'/m.name).read_bytes()==m.read_bytes()
    assert (out/'latest'/s.name).read_bytes()==s.read_bytes()
    verify=['openssl','dgst','-sha256','-verify',str(public),'-signature',str(s),str(m)]
    subprocess.run(verify,check=True)
    m.write_bytes(m.read_bytes()+b' ')
    assert subprocess.run(verify,stdout=subprocess.DEVNULL,stderr=subprocess.DEVNULL).returncode!=0
    print('Simulated release: mirror layout, signature, SHA256, size, tampering checks passed. No real executable or Windows runtime tested.')
