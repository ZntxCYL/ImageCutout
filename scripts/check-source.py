#!/usr/bin/env python3
from pathlib import Path
import json,xml.etree.ElementTree as ET,ast
root=Path(__file__).resolve().parents[1];win=root/'src/ImageCutout.Win';up=root/'src/ImageCutout.Updater'
for p in root.rglob('*.xaml'):ET.parse(p)
for p in root.rglob('*.csproj'):ET.parse(p)
for p in root.rglob('*.json'):json.loads(p.read_text(encoding='utf-8'))
for p in (root/'scripts').glob('*.py'):ast.parse(p.read_text(encoding='utf-8'))
s=(win/'Services/UpdateService.cs').read_text(encoding='utf-8');u=(up/'Program.cs').read_text(encoding='utf-8');engine=(win/'Services/CutoutEngine.cs').read_text(encoding='utf-8')
for needle in ('VerifyData','SHA256','Mirrors','latest/','latest <= current','ConfirmHealthyStartup'):assert needle in s,needle
for needle in ('SHA256.HashDataAsync','Directory.Move(backup, install)','ExtractSafely','ImageCutout.install','onnxruntime.dll'):assert needle in u,needle
for needle in ('AppendExecutionProvider_DML','EnableMemoryPattern=false','InferenceSession','Terminate=true','PngBitmapEncoder'):assert needle in engine,needle
assert 'using System.IO;' in (win/'Services/ThemeManager.cs').read_text(encoding='utf-8')
assert ET.parse(win/'ImageCutout.Win.csproj').findtext('.//Version')==ET.parse(up/'ImageCutout.Updater.csproj').findtext('.//Version')
print('Static XML/JSON/Python and source-contract checks passed; not a .NET build.')
