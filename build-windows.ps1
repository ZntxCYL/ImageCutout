$ErrorActionPreference = 'Stop'
$env:PYTHONUTF8 = '1'
Set-Location $PSScriptRoot
function Invoke-Checked([string]$Command, [string[]]$Arguments) {
    & $Command @Arguments
    if ($LASTEXITCODE -ne 0) { throw "$Command failed ($LASTEXITCODE)" }
}
Invoke-Checked 'python' @('scripts/check-source.py')
$out = Join-Path $PSScriptRoot 'dist/ImageCutout-win-x64'
$updater = Join-Path $PSScriptRoot 'dist/updater'
$archive = Join-Path $PSScriptRoot 'dist/ImageCutout-win-x64-Bundled.zip'
foreach ($path in @($out,$updater,$archive)) { if (Test-Path -LiteralPath $path) { Remove-Item -LiteralPath $path -Recurse -Force } }
Invoke-Checked 'dotnet' @('publish','src/ImageCutout.Win/ImageCutout.Win.csproj','-c','Release','-r','win-x64','--self-contained','true','-p:PublishSingleFile=false','-o',$out)
Invoke-Checked 'dotnet' @('publish','src/ImageCutout.Updater/ImageCutout.Updater.csproj','-c','Release','-r','win-x64','--self-contained','true','-p:PublishSingleFile=true','-p:IncludeNativeLibrariesForSelfExtract=true','-o',$updater)
Copy-Item -LiteralPath (Join-Path $updater 'ImageCutout.Updater.exe') -Destination $out
Invoke-Checked 'python' @('scripts/package.py',$out,$archive)
