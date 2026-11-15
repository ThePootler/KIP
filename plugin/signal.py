"""Coordinate the Windows panel and honour a per-chat mouse pause."""
import ctypes
from datetime import datetime, timezone
import json
import os
from pathlib import Path
import re
import subprocess
import sys
from uuid import UUID, uuid4

PAUSED = ("Ian pressed Esc. Stop waiting for mouse work and leave the PC to Ian. "
          "Do not retry computer use or switch tools to regain input. "
          "Wait for Ian to send 'KIP resume mouse' in this chat.")

def reply(event, **fields):
    print(json.dumps({"hookSpecificOutput": {"hookEventName": event, **fields}}))

def record(data):
    if os.name != "nt":
        return
    chat = str(UUID(data["session_id"]))
    base = Path(os.environ["LOCALAPPDATA"], "ChatGPT-KIP")
    marker = base / "paused" / chat
    event = data.get("hook_event_name")
    if event == "UserPromptSubmit":
        if data.get("prompt", "").strip().casefold() == "kip resume mouse":
            marker.unlink(missing_ok=True)
            reply(event, additionalContext="Ian explicitly allowed mouse use again for this chat.")
        elif marker.is_file():
            reply(event, additionalContext=PAUSED)
        return
    if data.get("tool_name") != "mcp__cua_repl__js":
        return
    args = data.get("tool_input") or {}
    if set(args) == {"completion_summary"}:
        return
    # Check before parsing code, including calls on previously selected windows.
    try:
        marker.stat()
    except FileNotFoundError:
        pass
    except OSError:
        reply("PreToolUse", permissionDecision="deny", permissionDecisionReason="KIP could not read this chat's mouse permission.")
        return
    else:
        reply("PreToolUse", permissionDecision="deny", permissionDecisionReason=PAUSED)
        return
    code = args.get("code", "")
    # Discard quoted examples and comments before recognising a native surface.
    code = re.sub(r"""//[^\r\n]*|/\*[\s\S]*?\*/|"(?:\\.|[^"\\])*"|'(?:\\.|[^'\\])*'|`(?:\\.|[^`\\])*`""", " ", code)
    if not re.search(r"\bcua\s*\.\s*(?:(?:getApp|getState|listApps|listWindows)\s*\(|computer\s*\.\s*launch_app\s*\()", code):
        return
    turn = str(UUID(data["turn_id"]))
    folder = base / "activity"
    folder.mkdir(parents=True, exist_ok=True)
    target = folder / (chat + ".json")
    temp = folder / (str(uuid4()) + ".tmp")
    try:
        temp.write_text(json.dumps({"turn": turn, "at": datetime.now(timezone.utc).isoformat(),
                        "call": data["tool_use_id"], "title": str(args.get("title", ""))[:240]}), encoding="utf8")
        os.replace(temp, target)
    finally:
        temp.unlink(missing_ok=True)

    kernel = ctypes.windll.kernel32
    kernel.OpenMutexW.argtypes = [ctypes.c_uint32, ctypes.c_int, ctypes.c_wchar_p]
    kernel.OpenMutexW.restype = ctypes.c_void_p
    mutex = kernel.OpenMutexW(0x100000, False, r"Local\ChatGPT-KIP.Panel")
    if mutex:
        kernel.CloseHandle(ctypes.c_void_p(mutex))
    else:
        panel = Path(os.environ["PLUGIN_ROOT"], "bin", "KIP.exe")
        subprocess.Popen([str(panel)], stdin=subprocess.DEVNULL, stdout=subprocess.DEVNULL,
                         stderr=subprocess.DEVNULL, close_fds=True)

    wait = Path(os.environ["PLUGIN_ROOT"], "wait.py")
    reply("PreToolUse", additionalContext=(
        "Release desktop control whenever the work permits. When you need to wait while "
        "keeping this computer-use session open, publish the reason, then run this "
        f'PowerShell command through the shell tool: python -B "{wait}" "{marker}" SECONDS. '
        "Replace SECONDS with the desired duration. This wait returns early on physical "
        "Esc. If it reports PAUSED, end computer use and wait for Ian."))

if __name__ == "__main__":
    record(json.load(sys.stdin.buffer))
