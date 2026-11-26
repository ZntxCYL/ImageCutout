#!/usr/bin/env python3
import argparse,json,re,subprocess
from pathlib import Path
from urllib.parse import urlparse
root=Path(__file__).resolve().parents[1]
p=argparse.ArgumentParser();p.add_argument('--repo',default='');p.add_argument('--mirror',action='append',default=[]);p.add_argument('--public-key',type=Path,required=True);p.add_argument('--model-mirror',action='append',default=[]);p.add_argument('--model-sha256',action='append',default=[],metavar='ID=SHA256');a=p.parse_args()
if a.repo and not re.fullmatch(r'[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+',a.repo):p.error('repo must be OWNER/REPO')
if not (a.repo or a.mirror):p.error('at least one release source required')
for value in a.mirror+a.model_mirror:
    u=urlparse(value)
    if u.scheme!='https' or not u.hostname or u.username or u.password or u.query or u.fragment:p.error('HTTPS base URL required')
key=subprocess.check_output(['openssl','pkey','-pubin','-in',str(a.public_key),'-pubout']).decode()
if not key.startswith('-----BEGIN PUBLIC KEY-----'):p.error('public PEM required')
# Ensure the trust anchor is RSA, at least 2048 bits.
info=subprocess.check_output(['openssl','rsa','-pubin','-in',str(a.public_key),'-text','-noout']).decode()
bits=re.search(r'\((\d+) bit\)',info)
if not bits or int(bits[1])<2048:p.error('RSA >=2048 bits required')
hashes={}
for item in a.model_sha256:
    ident,sep,value=item.partition('=')
    if ident not in ('birefnet-general','birefnet-general-lite') or not sep or not re.fullmatch('[a-fA-F0-9]{64}',value):p.error('invalid model hash')
    hashes[ident]=value.lower()
folder=root/'src/ImageCutout.Win'
for file,obj in [('update-config.json',dict(repository=a.repo,publicKeyPem=key,mirrors=a.mirror)),('model-config.json',dict(mirrors=a.model_mirror,sha256=hashes))]:
    (folder/file).write_text(json.dumps(obj,indent=2)+'\n')
print('Configured; rebuild before signing.')
