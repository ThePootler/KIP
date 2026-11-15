param([string]$WorkRoot)
$ErrorActionPreference = 'Stop'
$readyBuilt = Test-Path -LiteralPath (Join-Path $PSScriptRoot 'KIP.exe') -PathType Leaf
if (-not $WorkRoot) {
    if ($readyBuilt) { $WorkRoot = Join-Path $env:LOCALAPPDATA 'ChatGPT-KIP' }
    else { $WorkRoot = 'D:\CodexWork\ChatGPT-KIP' }
}
$cli = (Get-Command codex.exe -ErrorAction SilentlyContinue).Source
if (-not $cli) {
    $desktop = Get-AppxPackage -Name OpenAI.Codex | Select-Object -First 1
    if ($desktop) { $cli = Join-Path $desktop.InstallLocation 'app\resources\codex.exe' }
    if (-not $cli -or -not (Test-Path -LiteralPath $cli)) { throw 'Install Codex for Windows before adding the KIP hooks.' }
}
# Codex copies local marketplace packages into its cache. Stage the built
# Windows panel with the hook so the installed plugin can start it by itself.
$stage = Join-Path $WorkRoot 'local-marketplace'
$artifacts = Join-Path $WorkRoot 'plugin-build\artifacts'
$package = Join-Path $stage 'plugins\kip'
if (-not $readyBuilt) {
    dotnet build (Join-Path $PSScriptRoot 'Kip\Kip.csproj') -c Release --artifacts-path $artifacts --nologo
    if ($LASTEXITCODE -ne 0) { throw 'KIP build failed.' }
}
foreach ($folder in @('.agents\plugins','plugins\kip\.codex-plugin','plugins\kip\hooks','plugins\kip\bin')) {
    New-Item -ItemType Directory -Force -Path (Join-Path $stage $folder) | Out-Null
}
# An upgrade reuses this staging folder. Drop the scripts shipped by 0.4.0.
foreach ($oldScript in @('signal.py','wait.py')) {
    $oldPath = Join-Path $package $oldScript
    if (Test-Path -LiteralPath $oldPath -PathType Leaf) { Remove-Item -LiteralPath $oldPath }
}
foreach ($file in @('.codex-plugin\plugin.json','hooks\hooks.json')) {
    Copy-Item -LiteralPath (Join-Path "$PSScriptRoot\plugin" $file) -Destination (Join-Path $package $file) -Force
}
if ($readyBuilt) {
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'KIP.exe') -Destination (Join-Path $package 'bin\KIP.exe') -Force
} else {
    foreach ($file in @('KIP.exe','KIP.dll','KIP.deps.json','KIP.runtimeconfig.json')) {
        Copy-Item -LiteralPath (Join-Path "$artifacts\bin\Kip\release" $file) -Destination (Join-Path "$package\bin" $file) -Force
    }
}
$market = @{ name = 'kip-local'; plugins = @(@{ name = 'kip'; source = @{ source = 'local'; path = './plugins/kip' };
    policy = @{ installation = 'AVAILABLE'; authentication = 'ON_USE' }; category = 'Productivity' }) }
[IO.File]::WriteAllText((Join-Path $stage '.agents\plugins\marketplace.json'), ($market | ConvertTo-Json -Depth 6))
& $cli plugin marketplace add $stage
if ($LASTEXITCODE -ne 0) { throw 'Could not register the KIP local marketplace.' }
& $cli plugin add 'kip@kip-local'
if ($LASTEXITCODE -ne 0) { throw 'Could not install KIP.' }
Write-Host 'In Codex Settings > Hooks, review and trust the two KIP hooks.'
