"""Run on Windows with python -B tests/check_mouse_pause.py."""
import json
import os
from pathlib import Path
import subprocess
import sys
import tempfile
import time
from uuid import uuid4

plugin = Path(__file__).resolve().parents[1] / "plugin"
manifest = json.loads((plugin / "hooks/hooks.json").read_bytes())["hooks"]
chat, other, turn = (str(uuid4()) for _ in range(3))
scratch = Path(r"D:\CodexWork\ChatGPT-KIP\panel-checks")
scratch.mkdir(parents=True, exist_ok=True)

with tempfile.TemporaryDirectory(dir=scratch) as temp:
    env = dict(os.environ, PLUGIN_ROOT=str(plugin), LOCALAPPDATA=temp)
    marker = Path(temp, "ChatGPT-KIP", "paused", chat)
    marker.parent.mkdir(parents=True)

    def hook(event, session=chat, **fields):
        command = manifest[event][0]["hooks"][0]["command"]
        payload = dict(hook_event_name=event, session_id=session, turn_id=turn, **fields)
        result = subprocess.run(command, input=json.dumps(payload), text=True, env=env,
                                capture_output=True, check=True)
        return json.loads(result.stdout)["hookSpecificOutput"] if result.stdout else {}

    def tool(args, session=chat):
        return hook("PreToolUse", session, tool_name="mcp__cua_repl__js",
                    tool_use_id="test-call", tool_input=args)

    # The wait must end on a newly written marker, well before the deadline.
    process = subprocess.Popen([sys.executable, "-B", str(plugin/"wait.py"), str(marker), "30"],
                               stdout=subprocess.PIPE, text=True)
    time.sleep(.25)
    marker.touch()
    try:
        output, _ = process.communicate(timeout=3)
        assert process.returncode == 0 and "PAUSED" in output
    finally:
        if process.poll() is None:
            process.kill()
            process.wait()

    click = {"code": "await editor.click([1, 1])"}
    assert tool(click)["permissionDecision"] == "deny"
    assert tool(click, other) == {}
    assert tool({"completion_summary": "Ended mouse use"}) == {}
    assert tool(dict(click, completion_summary="Mixed input"))["permissionDecision"] == "deny"
    for message in ("carry on", "do not KIP resume mouse", "what does KIP resume mouse mean?"):
        hook("UserPromptSubmit", prompt=message)
        assert marker.exists()
    hook("UserPromptSubmit", other, prompt="KIP resume mouse")
    assert marker.exists()
    hook("UserPromptSubmit", prompt="KIP resume mouse")
    assert not marker.exists() and tool(click) == {}
    result = subprocess.check_output([sys.executable, "-B", str(plugin/"wait.py"), str(marker), "0"], text=True)
    assert result.strip() == "Wait elapsed."
print("Passed wait cancellation, per-chat blocking, completion and explicit resume.")
