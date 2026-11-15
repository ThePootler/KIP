# Build the downloadable Windows x64 ZIP. Each run gets a clean staging folder.
param(
    [string]$WorkRoot = 'D:\CodexWork\ChatGPT-KIP\releases',
    [ValidatePattern('^[0-9]+\.[0-9]+\.[0-9]+$')][string]$Version
)
$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$manifest = Get-Content -LiteralPath "$repo\plugin\.codex-plugin\plugin.json" -Raw -Encoding utf8 | ConvertFrom-Json
if (-not $Version) { $Version = $manifest.version }
$work = Join-Path $WorkRoot ([guid]::NewGuid().ToString())
$stage = Join-Path $work 'package'
$artifacts = Join-Path $work 'artifacts'
dotnet publish "$repo\Kip\Kip.csproj" -c Release -r win-x64 --self-contained true --artifacts-path $artifacts -o $stage `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=None -p:DebugSymbols=false "-p:Version=$Version" --nologo
if ($LASTEXITCODE -ne 0) { throw 'KIP publish failed.' }

Copy-Item -LiteralPath "$repo\plugin" -Destination $stage -Recurse
# Codex reads the ZIP's plugin version from this manifest. Use the same version as KIP.exe.
$manifest.version = $Version
[IO.File]::WriteAllText("$stage\plugin\.codex-plugin\plugin.json", ($manifest | ConvertTo-Json -Depth 10))
Copy-Item -LiteralPath "$repo\Install-KIPPlugin.ps1","$repo\README.md","$PSScriptRoot\Install Codex hooks.cmd" -Destination $stage

# Ship the notices from the same runtime packs that went into the executable.
$licenses = New-Item -ItemType Directory -Path "$stage\licenses"
$assets = Get-Content -LiteralPath "$artifacts\obj\Kip\project.assets.json" -Raw | ConvertFrom-Json
$config = Get-Content -LiteralPath "$artifacts\bin\Kip\release_win-x64\KIP.runtimeconfig.json" -Raw | ConvertFrom-Json
foreach ($framework in $config.runtimeOptions.includedFrameworks) {
    $relative = "$($framework.name).Runtime.win-x64\$($framework.version)"
    $pack = $assets.packageFolders.PSObject.Properties.Name | ForEach-Object { Join-Path $_ $relative } |
        Where-Object { Test-Path -LiteralPath $_ -PathType Container } | Select-Object -First 1
    if (-not $pack) { throw "Missing runtime pack $relative" }
    $notices = Get-ChildItem -LiteralPath $pack -File | Where-Object { $_.Name -match '^(LICENSE|THIRD-PARTY-NOTICES)\b' }
    if (-not $notices) { throw "Missing notices for $relative" }
    foreach ($file in $notices) {
        Copy-Item -LiteralPath $file.FullName -Destination (Join-Path $licenses.FullName "$($framework.name)-$($file.Name)")
    }
}

Add-Type -AssemblyName System.IO.Compression.FileSystem
# The README links to this filename on the latest GitHub release.
$zip = Join-Path $work 'KIP-windows-x64.zip'
[IO.Compression.ZipFile]::CreateFromDirectory($stage, $zip)
Write-Output $zip
