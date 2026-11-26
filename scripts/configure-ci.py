#!/usr/bin/env python3
"""Build-time config from GitHub Actions repository variables; no private key."""
import os, subprocess, sys, tempfile
from pathlib import Path
root=Path(__file__).resolve().parents[1]
key=os.environ.get('UPDATE_PUBLIC_KEY','').strip()
tag=os.environ.get('GITHUB_REF_TYPE')=='tag'
mirrors=os.environ.get('UPDATE_MIRRORS','').splitlines()
model_mirrors=os.environ.get('MODEL_MIRRORS','').splitlines()
hashes=os.environ.get('MODEL_SHA256','').splitlines()
if not key:
    if tag or any(v.strip() for v in mirrors+model_mirrors+hashes):
        raise SystemExit('Set repository variable UPDATE_PUBLIC_KEY before configuring mirrors or publishing a tag.')
    print('No update key configured; preserving repository config (default: updates disabled).')
    sys.exit(0)
repo=os.environ.get('GITHUB_REPOSITORY','') if os.environ.get('REPOSITORY_PRIVATE','').lower()!='true' else ''
with tempfile.TemporaryDirectory() as tmp:
    pub=Path(tmp)/'public.pem';pub.write_text(key+'\n',encoding='ascii')
    args=[sys.executable,str(root/'scripts/configure-updates.py'),'--public-key',str(pub)]
    if repo: args+=['--repo',repo]
    for opt,items in [('--mirror',mirrors),('--model-mirror',model_mirrors),('--model-sha256',hashes)]:
        for v in items:
            if v.strip(): args += [opt,v.strip()]
    subprocess.run(args,check=True)
