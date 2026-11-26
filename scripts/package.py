#!/usr/bin/env python3
import sys, zipfile
from pathlib import Path
root, output = map(Path,sys.argv[1:])
required = {'ImageCutout.Win.exe','ImageCutout.Updater.exe','ImageCutout.install','update-config.json','model-config.json','onnxruntime.dll','DirectML.dll'}
files = {p.relative_to(root).as_posix():p for p in root.rglob('*') if p.is_file() and p.suffix.lower() != '.pdb'}
missing = required - files.keys()
if missing: raise SystemExit('Missing native/runtime files: '+str(missing))
with zipfile.ZipFile(output,'w',zipfile.ZIP_DEFLATED,compresslevel=6) as z:
    for name,p in sorted(files.items()): z.write(p,name)
with zipfile.ZipFile(output) as z:
    assert z.testzip() is None
print(output)
