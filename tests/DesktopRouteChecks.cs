using System.Text.Json;

namespace Kip;

internal static class DesktopRouteChecks
{
    public static void Run(string scratch)
    {
        string folder = Directory.CreateDirectory(Path.Combine(scratch, Guid.NewGuid().ToString())).FullName;
        var chat = new Chat("sky-chat", "Desktop review", Path.Combine(folder, "chat.jsonl"), "Test project");
        var follow = new AutoFollow(() => [chat], folder);
        var stamp = DateTimeOffset.UtcNow.AddMinutes(-1);
        void Row(object payload, string type = "event_msg") => File.AppendAllText(chat.Path,
            JsonSerializer.Serialize(new { timestamp = stamp.AddSeconds(4), type, payload }) + "\n");
        void Direct(string code) => Row(new { type = "function_call", @namespace = "mcp__node_repl",
            name = "js", call_id = "direct", arguments = JsonSerializer.Serialize(new { code, title = "Direct desktop call" }) }, "response_item");
        void Nested(string server, string code, string turn = "one", string id = "nested") => Row(new {
            type = "item_completed", turn_id = turn, started_at_ms = stamp.ToUnixTimeMilliseconds(),
            item = new { type = "McpToolCall", server, tool = "js", id,
                arguments = new { code, title = "Reading the test window" },
                result = new { text = "await sky.click({window: example})" } }
        });
        void Check(bool ok, string why) { if (!ok) throw new Exception(why); }
        try
        {
            foreach (string method in new[] { "list_windows", "list_apps", "get_window", "get_window_state",
                "launch_app", "click", "press_key", "type_text", "scroll", "set_value", "drag",
                "perform_secondary_action", "activate_window" })
                Check(DesktopCalls.IsNative($"await globalThis.sky . {method} ({{window: selected}})"), "Missed sky method: " + method);
            foreach (string code in new[] { "const example = 'await sky.click()';", "// sky.list_windows()\nnodeRepl.write(2+2)",
                "/* sky.press_key() */", "const example = `sky.get_window()`;", "await browser.tabs.list()",
                "await tab.click([1,2])", "await sky.unrelated()", "const sky = await import('@oai/sky')" })
                Check(!DesktopCalls.IsNative(code), "Mistook text or unrelated work for desktop use: " + code);

            Row(new { type = "task_started", turn_id = "one" });
            Nested("node_repl", "nodeRepl.write(2 + 2)");
            Nested("cua_repl", "await cua.getTab(1)");
            Check(follow.Poll(true).ActiveChat == null, "Ordinary Node work, browser calls or tool output claimed the desktop");
            Direct("await sky.list_windows()");
            Check(follow.Poll(true).ActiveChat?.Id == chat.Id, "Direct node_repl call was not attributed");
            Row(new { type = "function_call", @namespace = "mcp__node_repl", name = "js_reset", arguments = "{}" }, "response_item");
            Check(follow.Poll(true).ActiveChat == null, "Node reset left its desktop session active");

            // This is the structured event written for real functions.exec -> node_repl calls.
            stamp = stamp.AddSeconds(10);
            Nested("node_repl", "globalThis.state = await sky.get_window_state({window: selected, include_text:true});");
            var active = follow.Poll(true);
            Check(active.ActiveChat?.Id == chat.Id && active.Project == chat.Project && active.Update.Message == "Reading the test window",
                "Wrapped sky call did not select the correct chat, project and title");
            Check(active.Task?.Started == DateTimeOffset.FromUnixTimeMilliseconds(stamp.ToUnixTimeMilliseconds()),
                "Wrapped call was timed from completion rather than its actual start");
            stamp = stamp.AddSeconds(5);
            Nested("node_repl", "nodeRepl.write('calculation')");
            Check(follow.Poll(true).Task == active.Task, "Unrelated Node work restarted the desktop clock");
            Row(new { type = "task_complete", turn_id = "one" });
            Nested("node_repl", "await sky.click({window:selected})");
            Check(follow.Poll(true).ActiveChat == null, "A late tool result reopened a finished turn");
            Row(new { type = "task_started", turn_id = "two" });
            Nested("node_repl", "await sky.click({window:selected})");
            Check(follow.Poll(true).ActiveChat == null, "An old turn's tool result claimed the new turn");

            // A hook receipt can precede the structured result. The clock must not jump.
            var reader = new ChatUpdates(chat.Path);
            reader.Read();
            stamp = stamp.AddSeconds(10);
            reader.MarkDesktop(stamp.AddMilliseconds(20), "Reading the test window", "receipt");
            var receiptClock = reader.CurrentTask;
            Nested("node_repl", "await sky.get_window_state({window:selected})", "two", "receipt");
            reader.Read();
            Check(reader.CurrentTask == receiptClock, "Transcript duplicated the hook's task clock");
            Row(new { type = "turn_aborted", turn_id = "two" });
            reader.Read();
            Check(!reader.UsingDesktop && reader.CurrentTask == null, "Interrupt left sky desktop activity armed");
        }
        finally { File.Delete(chat.Path); Directory.Delete(folder); }
    }
}
