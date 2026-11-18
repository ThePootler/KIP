using System.Text;
using System.Text.Json;
using Kip;

string scratch = @"D:\CodexWork\ChatGPT-KIP\panel-checks";
Directory.CreateDirectory(scratch);
string file = Path.Combine(scratch, $"{Guid.NewGuid()}.jsonl");
DateTimeOffset stamp = DateTimeOffset.Parse("2026-11-14T16:00:00Z");
string Line(object payload, string kind = "event_msg") => JsonSerializer.Serialize(new
    { timestamp = stamp, type = kind, payload }) + "\r\n";
void Check(bool ok, string explanation)
{
    if (!ok) throw new Exception(explanation);
}
try
{
    File.WriteAllText(file, Line(new { type = "task_started", turn_id = "one" }));
    var reader = new ChatUpdates(file);
    reader.Read();
    Check(reader.Latest.Label == "New request", "Expected a new request");

    string reason = "Waiting for café results";
    byte[] bytes = Encoding.UTF8.GetBytes(Line(new { type = "message", phase = "commentary",
        role = "assistant", content = new[] { new { text = reason } } }, "response_item"));
    int cut = bytes.Length - 6;
    using (var stream = new FileStream(file, FileMode.Append)) stream.Write(bytes.AsSpan(0, cut));
    reader.Read();
    Check(reader.Latest.Label == "New request", "An incomplete message must wait for a newline");
    using (var stream = new FileStream(file, FileMode.Append)) stream.Write(bytes.AsSpan(cut));
    reader.Read();
    Check(reader.Latest.Message == reason, "Lost the completed reason");
    var expected = reader.Latest;
    File.AppendAllText(file, Line(new { type = "function_call_output", output = reason }, "response_item"));
    reader.Read();
    Check(reader.Latest == expected && reader.Latest.Age(stamp.AddSeconds(65)) == "1m 5s ago",
        "Polling and unrelated tools must preserve message age");

    File.AppendAllText(file, Line(new { type = "task_complete", turn_id = "one" })
        + Line(new { type = "task_started", turn_id = "two" })
        + Line(new { type = "task_complete", turn_id = "one" }));
    reader.Read();
    Check(reader.Latest.Label == "New request", "An old turn's end leaked into the new one");
    File.AppendAllText(file, Line(new { type = "turn_aborted", turn_id = "two" }));
    reader.Read();
    Check(reader.Latest.Label == "Turn stopped", "Interrupted turn still looks active");
    File.WriteAllText(file, "");
    reader.Read();
    Check(reader.Latest == Update.Empty, "Truncated file retained stale state");
    Check(new Indicator(false, []).Caption.Contains("unknown") && new Indicator(true, []).Caption.Contains("unconfirmed") &&
        new Indicator(true, [new Rectangle(10, 10, 20, 20)]).Caption.EndsWith("visible"),
        "Indicator state overstates input ownership");
    AutoFollowChecks.Run(scratch);
    DesktopRouteChecks.Run(scratch);
    var indicatorTimer = new IndicatorTimer();
    var visibleIndicator = new Indicator(true, [new Rectangle(0, 0, 100, 100)]);
    var indicatorTime = indicatorTimer.Read(visibleIndicator, stamp);
    Check(indicatorTime?.Elapsed(stamp.AddSeconds(65)) == "Indicator  1:05", "Unknown chat still needs an indicator clock");
    Check(indicatorTimer.Read(visibleIndicator, stamp.AddMinutes(3)) == indicatorTime,
        "Polling must not restart the indicator clock");
    Check(indicatorTimer.Read(new Indicator(true, []), stamp.AddMinutes(4)) == null,
        "An absent indicator must end its clock");
    Check(indicatorTimer.Read(visibleIndicator, stamp.AddMinutes(5))?.Started == stamp.AddMinutes(5),
        "A new indicator appearance must start a fresh clock");
    Check(indicatorTimer.Read(new Indicator(false, []), stamp.AddMinutes(6)) == null,
        "An unknown scan cannot establish continuous indicator visibility");
    Check(indicatorTimer.Read(visibleIndicator, stamp.AddMinutes(7))?.Started == stamp.AddMinutes(7),
        "Resume timing from the next confirmed observation");
    File.WriteAllText(file, "");
    reader = new ChatUpdates(file);
    reader.MarkDesktop(stamp, "Checking progress", "first");
    stamp = stamp.AddSeconds(12);
    const string waitCommand = """dotnet 'C:\Codex plugins\kip\bin\KIP.dll' --wait 'C:\Local\ChatGPT-KIP\paused\chat' 45""";
    File.AppendAllText(file, Line(new { type = "custom_tool_call", name = "exec",
        input = $"text(await tools.exec_command({JsonSerializer.Serialize(new { cmd = waitCommand })}))".Replace("\\u0027", "'") }, "response_item"));
    reader.Read();
    var wait = reader.CurrentTask;
    Check(wait?.Started == stamp && wait.Elapsed(stamp.AddSeconds(65)) == "Wait  1:05", "A KIP wait should start its own clock");
    Check(wait!.Elapsed(stamp.AddSeconds(-1)) == "Wait  0:00" && wait.Elapsed(stamp.AddSeconds(3661)) == "Wait  1:01:01", "Invalid clock formatting");
    stamp = stamp.AddSeconds(4);
    File.AppendAllText(file, Line(new { type = "message", phase = "commentary", role = "assistant",
        content = new[] { new { text = "The import has reached 80%." } } }, "response_item"));
    reader.Read();
    Check(reader.Latest.Time == stamp && reader.CurrentTask == wait, "A progress update restarted the wait clock");
    reader.MarkDesktop(stamp, "Checking progress", "second");
    Check(reader.CurrentTask?.Label == "Task" && reader.CurrentTask.Started == stamp, "A new action with a reused title must restart the timer");
    stamp = stamp.AddSeconds(30);
    File.AppendAllText(file, Line(new { type = "function_call", name = "exec_command",
        arguments = JsonSerializer.Serialize(new { cmd = """& 'C:\Codex plugins\kip\bin\KIP.exe' --wait 'C:\Local\ChatGPT-KIP\paused\chat' 45 | Out-Host""" }) }, "response_item"));
    reader.Read();
    Check(reader.CurrentTask?.Label == "Wait" && reader.CurrentTask.Started == stamp, "A direct helper invocation must start the wait clock");
    reader.MarkDesktop(stamp, "Checking an older chat", "third");
    stamp = stamp.AddSeconds(1);
    const string oldWait = """python -B "C:\Codex\kip\0.4.0\wait.py" "C:\Local\ChatGPT-KIP\paused\chat" 45""";
    File.AppendAllText(file, Line(new { type = "function_call", name = "exec_command",
        arguments = JsonSerializer.Serialize(new { cmd = oldWait }) }, "response_item"));
    reader.Read();
    Check(reader.CurrentTask?.Label == "Wait", "Existing transcripts must still recognise the old wait helper");
    var projects = new ProjectNames(file);
    File.WriteAllText(file, """{"local-projects":{"one":{"name":"The.Pootler"}},"thread-project-assignments":{"chat":{"projectKind":"local","projectId":"one"},"missing":{"projectKind":"local","projectId":"unknown"}},"projectless-thread-ids":["loose"]}""");
    var names = projects.Read();
    Check(names["chat"] == "The.Pootler" && names["loose"] == "No project" && !names.ContainsKey("missing"), "Wrong project assignment");
    File.WriteAllText(file, "{");
    Check(projects.Read()["chat"] == "The.Pootler", "An incomplete sidebar write discarded a known project");
    File.WriteAllText(file, """{"local-projects":{"two":{"name":"KIP"}},"thread-project-assignments":{"chat":{"projectKind":"local","projectId":"two"}}}""");
    Check(projects.Read()["chat"] == "KIP" && !projects.Read().ContainsKey("loose"), "A moved chat kept its old project");
    using var menu = new ContextMenuStrip();
    using var panel = new PanelWindow(menu);
    var unknown = new FollowResult("Chat not identified",
        new Update("Computer use", "Windows shows the indicator. KIP hasn't identified an active desktop chat.", null));
    var observed = new DesktopTask("Indicator", stamp);
    var screen = Screen.PrimaryScreen!;
    panel.RefreshPanel(screen, unknown, null, visibleIndicator, ContentAlignment.TopCenter, stamp.AddHours(2), observed);
    Check(panel.Controls.OfType<Label>().Any(label => label.Text == "Indicator  2:00:00" && label.Width > 0),
        "The unknown-chat panel must display elapsed indicator time");
    var identified = unknown with { Task = new DesktopTask("Wait", stamp.AddSeconds(30)) };
    panel.RefreshPanel(screen, identified, null, visibleIndicator, ContentAlignment.TopCenter, stamp.AddSeconds(65), observed);
    Check(panel.Controls.OfType<Label>().Any(label => label.Text == "Wait  0:35"),
        "An identified chat must retain its own wait clock");
    panel.RefreshPanel(screen, identified, "Could not read updates", visibleIndicator,
        ContentAlignment.TopCenter, stamp.AddSeconds(65), observed);
    Check(panel.Controls.OfType<Label>().Any(label => label.Text == "Indicator  1:05"),
        "An update failure must still display the indicator clock");
    Console.WriteLine("Passed chat updates, project assignments, task, wait and indicator timers, panel clocks, interruptions and automatic chat following.");
}
finally { File.Delete(file); }
