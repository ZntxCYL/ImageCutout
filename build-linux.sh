#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")"
command -v dotnet >/dev/null || { echo '需要 .NET 10 SDK'; exit 1; }
python3 scripts/check-source.py
mkdir -p dist
out="$PWD/dist/ImageCutout-win-x64"
rm -rf "$out" "$PWD/dist/updater"
# 保持原生 DLL 外置：DirectML/ONNX Runtime 不适合盲目塞进单文件。
dotnet publish src/ImageCutout.Win/ImageCutout.Win.csproj -c Release -r win-x64 --self-contained true -p:EnableWindowsTargeting=true -p:PublishSingleFile=false -o "$out"
dotnet publish src/ImageCutout.Updater/ImageCutout.Updater.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o "$PWD/dist/updater"
cp "$PWD/dist/updater/ImageCutout.Updater.exe" "$out/"
python3 scripts/package.py "$out" "$PWD/dist/ImageCutout-win-x64-Bundled.zip"
echo "发布包：$PWD/dist/ImageCutout-win-x64-Bundled.zip"
