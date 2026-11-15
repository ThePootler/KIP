# KIP · Keep It Progressing

KIP automatically follows a local Codex chat that calls the Windows computer-use tools. Each red panel shows the owning sidebar project, the chat name, and the latest action or progress message. The top-right age is the time since that update. The bottom-right **Task** clock starts with each computer-use action. It reads **Wait** when KIP sees its wait helper, a Codex sleep call, or a progress message beginning with “Waiting for …” or “I’m waiting for …”. Progress during that wait leaves the clock running. A new action or newly declared wait reason starts it again. The footer reports the system-wide indicator state.

KIP reads project names from Codex’s local sidebar assignments. **No project** means Codex explicitly lists the chat outside a project. **Project unknown** means KIP could identify the chat but could not resolve its project.

## Download and run on Windows

1. [Download KIP 0.4.2 for Windows x64](https://github.com/ThePootler/KIP/releases/download/v0.4.2/KIP-0.4.2-windows-x64.zip) and choose **Extract All**.
2. Double-click **KIP.exe** in the extracted folder. KIP starts in the notification area and shows its panel when Codex uses the desktop. The download includes the .NET runtime. No SDK, Python or build step is needed.
3. To let Codex follow desktop calls and enforce the Esc pause, double-click **Install Codex hooks.cmd** once. In **Codex → Settings → Hooks**, review and trust **KIP: Follow mouse use and block a chat paused by Esc** and **KIP: Resume mouse use on explicit request**.

## Install the local Codex plugin from source

Run `./Install-KIPPlugin.ps1` from this project on Windows with the .NET 10 SDK and the desktop Codex CLI available. The panel and both plugin hooks use the same .NET build. The plugin is `kip@kip-local`. In **Settings > Hooks**, review **KIP: Follow mouse use and block a chat paused by Esc** under **PreToolUse** and **KIP: Resume mouse use on explicit request** under **UserPromptSubmit**. [Codex hook review](https://learn.chatgpt.com/docs/hooks#plugin-bundled-hooks)

The hook runs before a `cua_repl.js` call. For a recognised Windows call, it writes the chat ID (in the filename), turn ID, call ID, time and short action title into local application data and starts its bundled panel if needed. The running panel also reads desktop-call records directly, so existing chats can be followed while the hook awaits trust. Use `codex plugin remove kip@kip-local` to uninstall the plugin and **Exit KIP** to close a running panel.

## Run from source

On Windows with the .NET 10 SDK installed, run `./Start-KIP.ps1` from PowerShell. The script builds the panel outside OneDrive and launches it. To open the existing build directly, run `D:\CodexWork\ChatGPT-KIP\panel-build\artifacts\bin\Kip\release\KIP.exe`. KIP runs for the current sign-in until you quit it.

Automatic following is the default. To pin a chat temporarily, use **Follow chat** or start a fresh panel with `-ThreadId <id>`. Return to automatic mode with **Follow chat > Automatically follow computer use**. Right-click either panel or the notification-area icon for the full update, placement, visibility and exit controls.

The deep red panels appear on monitors covered by the native computer-use indicator and hide when it disappears. KIP keeps following chats in the tray, where **Read full update** is available even while the panels are hidden. The panels stay above ordinary windows and appear without taking keyboard focus. Top placements start 156 logical pixels below each monitor's work-area top to leave the ChatGPT banner and Esc visible. KIP remembers placement under the current user's local application data.

## Signals

With the native indicator visible and one identified desktop chat, KIP listens for physical Esc and forwards the key to Windows. A detected press pauses that chat across turns and KIP restarts. Send **KIP resume mouse** as a message by itself in that same chat to allow computer use again. The pause covers this chat's unified computer-use calls, including browser calls made through the same tool. A completion summary can still release a session. A hidden indicator, a key sent by automation, or an ambiguous chat identity does not by itself create a pause.

When a desktop session must stay open during a wait, the hook supplies the chat with the command for KIP's interruptible wait. It checks the pause every tenth of a second. A third-party tool, ordinary sleep or mouse action already in flight can finish on its own schedule. The hook checks the pause before subsequent computer-use calls.

KIP discovers recently updated local chats. A fresh hook receipt also identifies a chat whose transcript has an old file timestamp, which can happen while Codex keeps the file open on Windows. KIP initially reads up to 512 KiB of recent records per chat and then reads new data as it arrives. It recognises native window selection, Windows inventory and calls on a previously selected native window. Browser-tab calls, including from the same tool, and unrelated progress messages cannot select a new chat. A local chat can report desktop activity even while the Windows indicator is absent.

When several chats have open desktop activity, KIP lists the projects and chats involved. If the Windows indicator is visible and KIP has no matching active chat, it shows **Chat not identified**. If a chat finished desktop use within the last minute, the panel names it as **Last to finish**. Both ambiguous states hide the task clock and leave per-chat Esc pausing disarmed. An already-open session whose identifying event predates the startup scan may remain unidentified until its next recognisable native call. Local record formats and overlay window classes are version-specific. See [the Windows investigation](WINDOWS-COMPUTER-CONTROL.md).

**Computer-use indicator visible** means Windows reports a visible native overlay. **Indicator absent · control unconfirmed** means the scan succeeded and found none. **Indicator unknown · control unconfirmed** means the scan failed or the input desktop was inaccessible. The overlay is PC-wide and can belong to another chat. KIP has no verified signal for return of input ownership.

## Checks

Run `dotnet run --project tests/Kip.Checks.csproj -c Release` from the project root. The checks cover partial records, elapsed time, interrupted turns, switching between chats, simultaneous desktop activity, browser-only calls, waiting messages and the three indicator states. Run `./tests/Check-MousePause.ps1` in PowerShell to check the plugin commands, Esc pause, explicit resume and interruptible wait. Build and test output stays under `D:\CodexWork\ChatGPT-KIP`.

To build the downloadable ZIP, run `./release/Build-Release.ps1` on Windows with the .NET 10 SDK.
