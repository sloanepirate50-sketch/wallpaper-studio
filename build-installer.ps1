<#
  Builds the Wallpaper Studio installer.

    .\build-installer.ps1                # version 1.0.0
    .\build-installer.ps1 -Version 1.1.0

  Output: artifacts\WallpaperStudio-<version>-Setup.msi

  Needs the .NET 10 SDK and the WiX Toolset 5 dotnet tool. It's installed automatically if missing:
    dotnet tool install --global wix --version 5.0.2
    wix extension add -g WixToolset.UI.wixext/5.0.2
#>
param([string]$Version = "1.0.0")
$ErrorActionPreference = "Stop"

$root = $PSScriptRoot
$artifacts = Join-Path $root "artifacts"
$publish = Join-Path $artifacts "publish"

# 1. Self-contained, single-file app (no .NET install needed on the target PC).
Remove-Item -Recurse -Force $publish -ErrorAction SilentlyContinue
dotnet publish (Join-Path $root "WallpaperStudio.csproj") -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true `
    -p:PublishReadyToRun=true -p:DebugType=none -p:Version=$Version -o $publish
if ($LASTEXITCODE) { throw "dotnet publish failed" }

# 2. WiX tooling.
$env:PATH += ";$env:USERPROFILE\.dotnet\tools"
if (-not (Get-Command wix -ErrorAction SilentlyContinue)) {
    dotnet tool install --global wix --version 5.0.2
}
if (-not (wix extension list -g | Select-String "WixToolset.UI.wixext")) {
    wix extension add -g WixToolset.UI.wixext/5.0.2
}

# 3. The MSI.
$msi = Join-Path $artifacts "WallpaperStudio-$Version-Setup.msi"
Push-Location (Join-Path $root "installer")
try {
    wix build "WallpaperStudio.wxs" -arch x64 -d Version=$Version -d PublishDir=$publish `
        -ext WixToolset.UI.wixext -o $msi
    if ($LASTEXITCODE) { throw "wix build failed" }
}
finally { Pop-Location }

Get-Item $msi | Select-Object Name, @{ n = "MB"; e = { [math]::Round($_.Length / 1MB, 1) } }, FullName
