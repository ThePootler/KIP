# Run from PowerShell on Windows. Use -Package to check an installed plugin instead.
param([string]$Package)
$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$scratch = 'D:\CodexWork\ChatGPT-KIP\panel-checks'
$work = Join-Path $scratch ([guid]::NewGuid().ToString())
$chat = [guid]::NewGuid().ToString()
$other = [guid]::NewGuid().ToString()
$turn = [guid]::NewGuid().ToString()
$marker = Join-Path $work "ChatGPT-KIP\paused\$chat"
$null = New-Item -ItemType Directory -Force -Path (Split-Path $marker)

function Start-CheckProcess($file, $arguments, $inputJson) {
    $info = [Diagnostics.ProcessStartInfo]::new($file, $arguments)
    $info.UseShellExecute = $false
    $info.CreateNoWindow = $true
    $info.RedirectStandardInput = $info.RedirectStandardOutput = $info.RedirectStandardError = $true
    $info.EnvironmentVariables['PLUGIN_ROOT'] = $Package
    $info.EnvironmentVariables['LOCALAPPDATA'] = $work
    # Use only Windows and .NET commands, even when Python is installed on the test PC.
    $info.EnvironmentVariables['PATH'] = "$env:SystemRoot\System32;$dotnetDir"
    $process = [Diagnostics.Process]::Start($info)
    if ($inputJson) { $process.StandardInput.WriteLine($inputJson) }
    $process.StandardInput.Close()
    return $process
}

function Read-Result($process, [int]$timeout = 4000) {
    try {
        if (-not $process.WaitForExit($timeout)) { throw 'KIP command timed out.' }
        if ($process.ExitCode -ne 0) { throw $process.StandardError.ReadToEnd() }
        return $process.StandardOutput.ReadToEnd().Trim()
    } finally {
        if (-not $process.HasExited) { $process.Kill(); $process.WaitForExit() }
        $process.Dispose()
    }
}

function Invoke-Hook($event, $session, $fields) {
    $payload = @{ hook_event_name = $event; session_id = $session; turn_id = $turn } + $fields
    $command = $hooks.$event[0].hooks[0].command
    $process = Start-CheckProcess $env:ComSpec "/d /c $command" ($payload | ConvertTo-Json -Depth 6 -Compress)
    $answer = Read-Result $process
    if ($answer) { return ($answer | ConvertFrom-Json).hookSpecificOutput }
}

function Invoke-Tool($inputFields, $session = $chat) {
    Invoke-Hook 'PreToolUse' $session @{ tool_name = 'mcp__cua_repl__js'; tool_use_id = 'test-call'; tool_input = $inputFields }
}

$dotnetDir = Split-Path (Get-Command dotnet.exe).Source
try {
    if (-not $Package) {
        $Package = Join-Path $work "test user's plugin"
        dotnet build (Join-Path $repo 'Kip\Kip.csproj') -c Release --artifacts-path "$work\artifacts" --nologo
        if ($LASTEXITCODE -ne 0) { throw 'KIP build failed.' }
        $null = New-Item -ItemType Directory -Path $Package
        Copy-Item -LiteralPath "$work\artifacts\bin\Kip\release" -Destination "$Package\bin" -Recurse
        Copy-Item -LiteralPath (Join-Path $repo 'plugin\hooks') -Destination $Package -Recurse
    }
    $hooks = (Get-Content -Raw -LiteralPath "$Package\hooks\hooks.json" | ConvertFrom-Json).hooks
    $dll = Join-Path $Package 'bin\KIP.dll'
    $waitArgs = '"{0}" --wait "{1}"' -f $dll,$marker
    $wait = Start-CheckProcess "$dotnetDir\dotnet.exe" "$waitArgs 30" $null
    Start-Sleep -Milliseconds 250
    [IO.File]::WriteAllText($marker, '')
    if ((Read-Result $wait) -notlike 'PAUSED*') { throw 'Wait missed a new pause.' }
    $click = @{ code = 'await editor.click([1, 1])' }
    if ((Invoke-Tool $click).permissionDecision -ne 'deny') { throw 'Paused click passed.' }
    if (Invoke-Tool $click $other) { throw 'Blocked a different chat.' }
    if (Invoke-Tool @{ completion_summary = 'Ended mouse use' }) { throw 'Blocked completion.' }
    if ((Invoke-Tool ($click + @{ completion_summary = 'Mixed' })).permissionDecision -ne 'deny') { throw 'Mixed input passed.' }
    foreach ($message in @('carry on','do not KIP resume mouse','what does KIP resume mouse mean?')) {
        $reply = Invoke-Hook 'UserPromptSubmit' $chat @{ prompt = $message }
        if (-not $reply.additionalContext -or -not [IO.File]::Exists($marker)) { throw 'Ordinary prompt lost the pause.' }
    }
    $null = Invoke-Hook 'UserPromptSubmit' $other @{ prompt = 'KIP resume mouse' }
    if (-not [IO.File]::Exists($marker)) { throw 'Another chat resumed this one.' }
    $null = Invoke-Hook 'UserPromptSubmit' $chat @{ prompt = ' KIP resume mouse ' }
    if ([IO.File]::Exists($marker) -or (Invoke-Tool $click)) { throw 'Explicit resume failed.' }
    if ((Read-Result (Start-CheckProcess "$dotnetDir\dotnet.exe" "$waitArgs 0" $null)) -ne 'Wait elapsed.') { throw 'Wait did not expire.' }

    # Stand in for the running tray panel so the test never opens a window.
    $panel = [Threading.Mutex]::new($false, 'Local\ChatGPT-KIP.Panel')
    try {
        $reply = Invoke-Tool @{ code = 'await cua.getState()'; title = 'Read the desktop' }
        $receipt = Get-Content -LiteralPath "$work\ChatGPT-KIP\activity\$chat.json" -Raw | ConvertFrom-Json
        if ($receipt.turn -ne $turn -or $receipt.call -ne 'test-call' -or $receipt.title -ne 'Read the desktop') { throw 'Wrong activity receipt.' }
        $expected = "dotnet '" + $dll.Replace("'","''") + "' --wait '" + $marker.Replace("'","''") + "' SECONDS"
        if (-not $reply.additionalContext.Contains($expected)) { throw 'Wrong wait command in hook reply.' }
    } finally { $panel.Dispose() }
    'Passed the installed hook commands, pause, resume, wait and activity receipt checks.'
} finally {
    if ((Split-Path ([IO.Path]::GetFullPath($work)) -Parent) -ne $scratch) { throw 'Unexpected scratch path.' }
    Remove-Item -LiteralPath $work -Recurse -Force
}
