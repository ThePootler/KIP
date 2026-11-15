$ErrorActionPreference = 'Stop'
# Codex copies local marketplace packages into its cache. Stage the built
# Windows panel with the hook so the installed plugin can start it by itself.
$stage = 'D:\CodexWork\ChatGPT-KIP\local-marketplace'
$artifacts = 'D:\CodexWork\ChatGPT-KIP\plugin-build\artifacts'
$package = Join-Path $stage 'plugins\kip'
dotnet build (Join-Path $PSScriptRoot 'Kip\Kip.csproj') -c Release --artifacts-path $artifacts --nologo
if ($LASTEXITCODE -ne 0) { throw 'KIP build failed.' }
foreach ($folder in @('.agents\plugins','plugins\kip\.codex-plugin','plugins\kip\hooks','plugins\kip\bin')) {
    New-Item -ItemType Directory -Force -Path (Join-Path $stage $folder) | Out-Null
}
foreach ($file in @('.codex-plugin\plugin.json','hooks\hooks.json','signal.py','wait.py')) {
    Copy-Item -LiteralPath (Join-Path "$PSScriptRoot\plugin" $file) -Destination (Join-Path $package $file) -Force
}
foreach ($file in @('KIP.exe','KIP.dll','KIP.deps.json','KIP.runtimeconfig.json')) {
    Copy-Item -LiteralPath (Join-Path "$artifacts\bin\Kip\release" $file) -Destination (Join-Path "$package\bin" $file) -Force
}
$market = @{ name = 'kip-local'; plugins = @(@{ name = 'kip'; source = @{ source = 'local'; path = './plugins/kip' };
    policy = @{ installation = 'AVAILABLE'; authentication = 'ON_USE' }; category = 'Productivity' }) }
[IO.File]::WriteAllText((Join-Path $stage '.agents\plugins\marketplace.json'), ($market | ConvertTo-Json -Depth 6))
$cli = (Get-Command codex.exe -ErrorAction Stop).Source
& $cli plugin marketplace add $stage
if ($LASTEXITCODE -ne 0) { throw 'Could not register the KIP local marketplace.' }
& $cli plugin add 'kip@kip-local'
if ($LASTEXITCODE -ne 0) { throw 'Could not install KIP.' }
