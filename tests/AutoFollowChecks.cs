using System.Text.Json;

namespace Kip;

internal static class AutoFollowChecks
{
    public static void Run(string scratch)
    {
        string folder = Directory.CreateDirectory(Path.Combine(scratch, Guid.NewGuid().ToString())).FullName;
        var a = new Chat("a", "First chat", Path.Combine(folder, "a.jsonl"), "KIP");
        var b = new Chat("b", "Second chat", Path.Combine(folder, "b.jsonl"), "The.Pootler");
        var follow = new AutoFollow(() => [a, b], folder);
        void Event(Chat chat, string type, object payload) => File.AppendAllText(chat.Path,
            JsonSerializer.Serialize(new { timestamp = DateTimeOffset.UtcNow, type, payload }) + "\n");
        void Tool(Chat c, object args) => Event(c, "response_item", new { type = "function_call",
            @namespace = "mcp__cua_repl", name = "js", arguments = JsonSerializer.Serialize(args) });
        void Update(Chat c, string text) => Event(c, "response_item", new {type = "message", role = "assistant",
            phase = "commentary", content = new[] { new { text } } });
        void Expect(bool matches) { if (!matches) throw new Exception("Automatic chat selection failed."); }
        try
        {
            foreach (var c in new[] {a,b}) Event(c, "event_msg", new { type = "task_started", turn_id = c.Id });
            Tool(b, new { code = "let tab = await cua.getTab(3); // cua.getApp()", title = "Reading a browser tab" });
            var unknown = follow.Poll(true);
            Expect(unknown.ChatName == "Chat not identified" && unknown.ActiveChat == null && unknown.Task == null && unknown.Project == null);
            Tool(a, new { code = "let editor = await cua.getApp({windowId:7})", title = "Reading the editor" });
            Update(a, "Waiting for import to finish.");
            Update(b, "Unrelated activity");
            var first = follow.Poll(true);
            Expect(first.ActiveChat?.Id == a.Id && first.ChatName == a.Name && first.Update.Message == "Waiting for import to finish.");
            Expect(first.Project == a.Project && first.Task?.Label == "Wait");
            Update(a, "The progress bar has reached 60%.");
            Expect(follow.Poll(true).Task == first.Task);
            Update(a, "Waiting for the login screen.");
            Expect(follow.Poll(true).Task?.Started > first.Task!.Started);
            Tool(a, new { code = "await buttons()", title = "Reading scheduler actions" });
            var helper = follow.Poll(true);
            Expect(helper.ActiveChat?.Id == a.Id && helper.Update.Message == "Reading scheduler actions" && helper.Task?.Label == "Task");
            Tool(b, new { code = "await cua.listWindows()", title = "Listing windows" });
            var multiple = follow.Poll(true);
            Expect(multiple.ChatName.StartsWith("Multiple") && multiple.ActiveChat == null);
            Expect(multiple.Task == null && multiple.Project == null && multiple.Update.Message.Contains(b.FullName));
            Tool(b, new { completion_summary = "Finished window inspection" });
            Expect(follow.Poll(true).ChatName == a.Name);
            Tool(a, new { completion_summary = "Done" });
            var finished = follow.Poll(true);
            Expect(finished.ChatName == "Chat not identified" && finished.ActiveChat == null && finished.Task == null &&
                finished.Update.Message.Contains("Last to finish: " + a.FullName));
            Tool(a, new { code = "await buttons()", title = "Reading scheduler actions" });
            Expect(follow.Poll(true).ActiveChat == null);
            Tool(a, new { code = "await editor.pressKey('Tab')", title = "Changing focus" });
            Expect(follow.Poll(true).Update.Message == "Changing focus");
            Event(a, "event_msg", new { type = "turn_aborted", turn_id = a.Id });
            Expect(follow.Poll(true).ActiveChat == null && follow.Poll(true).Task == null);
            Tool(b, new { code = "await cua.getState()", title = "Inspecting available windows" });
            Expect(follow.Poll(true).ChatName == b.Name);
            Event(b, "event_msg", new { type = "task_complete", turn_id = b.Id });
            Expect(follow.Poll(false).ChatName.StartsWith("Last"));
            Event(b, "event_msg", new { type = "task_started", turn_id = "next" });
            Expect(follow.Poll(false).ChatName == "Auto-follow");

            // A real, open Windows rollout kept its creation-time mtime for over two hours.
            // Its current turn header can also be outside the 512 KiB startup read.
            File.AppendAllText(a.Path, new string(' ', 530_000) + "\n");
            File.SetLastWriteTimeUtc(a.Path, DateTime.UtcNow.AddHours(-3));
            string receipt = Path.Combine(folder, a.Id + ".json");
            File.WriteAllText(receipt, JsonSerializer.Serialize(new { turn = "long-turn",
                at = DateTimeOffset.UtcNow, call = "cold-start", title = "Checking Ian’s PC" }));
            var cold = new AutoFollow(() => [a, b], folder);
            var active = cold.Poll(false);
            Expect(active.ChatName == a.Name && active.Update.Message == "Checking Ian’s PC");
            Event(a, "response_item", new { type = "function_call", @namespace = "mcp__cua_repl",
                name = "js", call_id = "cold-start", arguments = JsonSerializer.Serialize(new {
                    code = "await cua.listWindows()", title = "Checking Ian’s PC" }) });
            Expect(cold.Poll(true).Task == active.Task);
            Tool(a, new { completion_summary = "Finished checking" });
            File.SetLastWriteTimeUtc(a.Path, DateTime.UtcNow.AddHours(-3));
            Expect(cold.Poll(false).ChatName == "Last · " + a.Name);
            var afterCompletion = new AutoFollow(() => [a, b], folder);
            Expect(afterCompletion.Poll(false).ChatName == "Last · " + a.Name);
            Expect(afterCompletion.Poll(true).ChatName == "Chat not identified");
        }
        finally
        {
            File.Delete(Path.Combine(folder, a.Id + ".json"));
            File.Delete(a.Path); File.Delete(b.Path); Directory.Delete(folder);
        }
    }
}
