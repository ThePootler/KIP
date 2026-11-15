param([string]$ThreadId)
$ErrorActionPreference = 'Stop'
$project = Join-Path $PSScriptRoot 'Kip\Kip.csproj'
$buildRoot = 'D:\CodexWork\ChatGPT-KIP\panel-build\artifacts'
dotnet build $project -c Release --artifacts-path $buildRoot --nologo
if ($LASTEXITCODE -ne 0) { throw 'KIP build failed.' }
$exe = Join-Path $buildRoot 'bin\Kip\release\KIP.exe'
if ($ThreadId) {
    $id = [guid]::Parse($ThreadId).ToString()
    Start-Process -FilePath $exe -ArgumentList @('--thread', $id)
} else {
    Start-Process -FilePath $exe
}
