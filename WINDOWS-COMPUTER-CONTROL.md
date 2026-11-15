# What KIP can see on Ian's Windows PC

Tested on 14 November 2026 with Windows package `OpenAI.Codex 26.1111.2228.0`, its bundled `codex-cli 0.169.0-alpha.11.1`, and computer-use plugin `26.1111.21308`.

KIP can display the latest explanation Codex writes and detect when the Windows computer-use overlay is visible. I could not verify a public signal that confirms Ian has his keyboard and mouse back. The foreground test showed why that distinction matters.

## What Codex is waiting for

| Source | Result on this PC | Meaning for KIP |
| --- | --- | --- |
| Desktop app's `read_thread` tool | During this turn it returned `active`, an empty `activeFlags` list and `inProgress`, with assistant messages and tool items. | Useful inside Codex. The empty list gives no explanation for an ordinary wait. |
| Installed app-server schema | The only defined active flags were `waitingOnApproval` and `waitingOnUserInput`. The schema also defines tool, model, permission, subagent and stop hooks. | The two flags name specific waits when available from the running server. Hook names describe activity. A waiting reason, dependency or ETA still needs a source. Live firing of hooks was not tested. [App-server status](https://learn.chatgpt.com/docs/app-server#track-thread-status-changes) · [Hooks and their coverage](https://learn.chatgpt.com/docs/hooks) |
| Separate Windows process using the bundled CLI | Started `codex.exe app-server --stdio`, initialised it, and used `thread/read` and `thread/items/list` for this chat. It returned a progress message from the current turn, and the newest items. The separate server reported `notLoaded` and described the current turn as `interrupted` while the desktop continued working. | A local panel can read published commentary using this interface. Never translate this reader's snapshot into a claim that the desktop stopped. [App-server protocol](https://learn.chatgpt.com/docs/app-server) |
| Default CLI proxy | `codex.exe app-server proxy` could not attach to the default control socket. Outside the sandbox Windows returned connection refused (10061). | A live subscription to the desktop's own server was not established on this setup. |
| This chat's local transcript | An independent read found timestamped assistant messages with `phase: commentary`. It also found tool titles, completion summaries and turn boundaries. | A diagnostic fallback. OpenAI documents the transcript format as subject to change. [Hook input and transcript format](https://learn.chatgpt.com/docs/hooks#common-input-fields) |

A message such as “Waiting for the build to finish, usually about 30 seconds” gives KIP the reason and an expected duration. When Codex has only reported “Checking the build”, display that as its last update with its age. Silence supplies neither a wait reason nor an ETA.

## What Windows showed during computer use

A passive observer in the interactive Windows session enumerated visible top-level windows about every 50 ms. It recorded class names and changes in visibility. During the second test it also recorded the overlay's process ID and bounds. The observer sent no input. Times below are UK time (UTC on the test date).

| Test | What happened |
| --- | --- |
| Read-only inventory and native window inspection | Two windows of class `CodexComputerUseCursorOverlay` appeared at 16:24:58.964 and disappeared together at 16:24:59.678. The computer-use tool acknowledged its completion summary at 16:25:08.056. The overlay had already disappeared while the investigation was still using the tool. |
| Foreground input | Codex sent one Tab to change focus in an untitled Bambu Studio window and read its accessibility tree. Two matching overlay windows appeared at 16:26:44.091. The input call completed at 16:26:45.123. The summary acknowledgement arrived at 16:26:53.750. Both windows were still visible at 16:27:21, over 27 seconds later. |
| Closing the JavaScript session | A fresh check still saw both windows at 16:27:40 after the session reset. Both were visible again at the final sample, 16:29:14, 2 minutes 21 seconds after the summary acknowledgement. The samples after 16:26:55 were intermittent. Both rectangles covered the full 4480 × 1440 desktop. Those rectangles cannot locate the small banner for positioning a panel. |

The installation contains the banner strings “ChatGPT is using your computer” and “Esc to cancel”. A local process can inspect the visible Windows class without scraping screen pixels or modifying the app. The class name is an observed implementation detail. Several windows may represent one indicator, and an observed indicator has no verified chat ID. The constant is worth rechecking after app updates.

The computer-use tool acknowledged receipt of its summary. Its Windows API and the generated app-server protocol supplied no verified external query for keyboard/mouse ownership. The protocol's `remoteControl/status/read` concerns connection to a remote-control server. The test did not establish the exact instant physical input became available, whether any other chat held control, or the result of Ian pressing Esc. OpenAI documents foreground input on Windows and the option to stop or take over. [Computer use on Windows](https://learn.chatgpt.com/docs/computer-use)

## Smallest useful prototype

1. Read one selected chat through the bundled CLI's stdio app server. Discover its recent turn with `thread/turns/list`, then poll a small page from `thread/items/list` for new visible assistant commentary. Display the latest words and the time since that update. The experiment read recent items and commentary. Turn discovery, polling latency and restart recovery remain prototype checks.
2. Give the PC its own status line. Show **Computer-use indicator visible** when any matching window is visible. After a successful scan with zero matches, show **Indicator absent · control unconfirmed**. Show **Control status unknown** when observation fails. A task ending or a completion summary arriving cannot turn this line green.
3. Put the small panel in a user-chosen corner without activating it or covering Esc. Keep the last published reason visible during a justified wait and update it when Codex reports a change. Defer the wording **You have control back** until a supported ownership acknowledgement, or an independently validated equivalent, is available.

For the next validation, compare the indicator and task messages against Ian's first successful physical input in a disposable test window after normal completion and after Esc. Also check two overlapping chats and a lost observer. These checks can establish what the indicator predicts on this build and when KIP must keep reporting uncertainty.
