#!/usr/bin/env python3
"""Sign the exact Windows artifact and generate GitHub + mirror layout."""
import json, os, pathlib, re, subprocess, sys, tempfile, zipfile
root=pathlib.Path(__file__).resolve().parents[1]
tag=os.environ['GITHUB_REF_NAME']
if not re.fullmatch(r'v\d+\.\d+\.\d+',tag): raise SystemExit('Tag must be vX.Y.Z')
archive=root/'dist/ImageCutout-win-x64-Bundled.zip'
config_dir=root/'src/ImageCutout.Win'
with zipfile.ZipFile(archive) as z:
    for name in ('update-config.json','model-config.json'):
        raw=z.read(name);json.loads(raw);(config_dir/name).write_bytes(raw)
conf=json.loads((config_dir/'update-config.json').read_text())
if conf['repository'] not in ('',os.environ['GITHUB_REPOSITORY']):raise SystemExit('Artifact repository mismatch')
secret=os.environ.get('UPDATE_SIGNING_PRIVATE_KEY','').strip()
if not secret:raise SystemExit('Missing UPDATE_SIGNING_PRIVATE_KEY Actions secret')
with tempfile.TemporaryDirectory() as tmp:
    key=pathlib.Path(tmp)/'private.pem'
    fd=os.open(key,os.O_CREAT|os.O_EXCL|os.O_WRONLY,0o600)
    with os.fdopen(fd,'w') as f:f.write(secret+'\n')
    env=os.environ.copy();env.pop('UPDATE_SIGNING_PRIVATE_KEY',None)
    subprocess.run([sys.executable,str(root/'scripts/make-update-release.py'),'--version',tag[1:],'--private-key',str(key),'--archive',str(archive)],check=True,env=env)
