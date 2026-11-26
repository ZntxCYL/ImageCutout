#!/usr/bin/env python3
"""Simulate public build configuration + signing job, using non-executable fixtures."""
import json, os, pathlib, shutil, subprocess, sys, tempfile, zipfile, xml.etree.ElementTree as ET
root=pathlib.Path(__file__).resolve().parents[1]
version=ET.parse(root/'src/ImageCutout.Win/ImageCutout.Win.csproj').findtext('.//Version')
with tempfile.TemporaryDirectory() as d:
    tmp=pathlib.Path(d);work=tmp/'source';shutil.copytree(root,work,ignore=shutil.ignore_patterns('dist','.git','bin','obj','__pycache__'))
    key=tmp/'private.pem';pub=tmp/'public.pem'
    subprocess.run(['openssl','genpkey','-algorithm','RSA','-pkeyopt','rsa_keygen_bits:2048','-out',str(key)],check=True,stdout=subprocess.DEVNULL,stderr=subprocess.DEVNULL)
    subprocess.run(['openssl','pkey','-in',str(key),'-pubout','-out',str(pub)],check=True)
    env={**os.environ,'GITHUB_REF_TYPE':'tag','GITHUB_REF_NAME':'v'+version,'GITHUB_REPOSITORY':'example/imagecutout','REPOSITORY_PRIVATE':'false','UPDATE_PUBLIC_KEY':pub.read_text(encoding='utf-8'),'UPDATE_MIRRORS':'https://example.invalid/updates/','MODEL_MIRRORS':'https://example.invalid/models/','MODEL_SHA256':''}
    env.pop('UPDATE_SIGNING_PRIVATE_KEY',None)
    subprocess.run([sys.executable,str(work/'scripts/configure-ci.py')],env=env,check=True)
    win=work/'src/ImageCutout.Win';config=json.loads((win/'update-config.json').read_text(encoding='utf-8'));assert config['repository']=='example/imagecutout' and config['mirrors']==['https://example.invalid/updates/']
    fake=tmp/'fake';fake.mkdir()
    for n in ['ImageCutout.Win.exe','ImageCutout.Updater.exe','onnxruntime.dll','DirectML.dll']:(fake/n).write_bytes(b'CI fixture, not executable')
    for n in ['update-config.json','model-config.json','ImageCutout.install']:shutil.copy2(win/n,fake/n)
    dist=work/'dist';dist.mkdir();archive=dist/'ImageCutout-win-x64-Bundled.zip'
    subprocess.run([sys.executable,str(work/'scripts/package.py'),str(fake),str(archive)],check=True)
    # Mimic a fresh checkout in the signing job.
    for n in ['update-config.json','model-config.json']:shutil.copy2(root/'src/ImageCutout.Win'/n,win/n)
    env['UPDATE_SIGNING_PRIVATE_KEY']=key.read_text(encoding='utf-8')
    subprocess.run([sys.executable,str(work/'scripts/sign-ci-release.py')],env=env,check=True)
    signed=dist/'release'/('v'+version);assert (signed/archive.name).read_bytes()==archive.read_bytes()
    assert json.loads((signed/'update-manifest.json').read_text(encoding='utf-8'))['repository']=='example/imagecutout'
    subprocess.run(['openssl','dgst','-sha256','-verify',str(pub),'-signature',str(signed/'update-manifest.sig'),str(signed/'update-manifest.json')],check=True)
    env.pop('UPDATE_SIGNING_PRIVATE_KEY');assert subprocess.run([sys.executable,str(work/'scripts/sign-ci-release.py')],env=env).returncode!=0
    env['UPDATE_PUBLIC_KEY']='';assert subprocess.run([sys.executable,str(work/'scripts/configure-ci.py')],env=env).returncode!=0
    print('CI configuration/signing simulation passed; not a real Actions run.')
