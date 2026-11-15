using System.Text.Json;
using System.Text.RegularExpressions;

namespace Kip;

internal sealed record Update(string Label, string Message, DateTimeOffset? Time)
{
    public static readonly Update Empty = new("Latest update", "Waiting for Codex to publish an update.", null);

    public string Age(DateTimeOffset now)
    {
        if (Time == null) return "No update yet";
        var age = now - Time.Value;
        if (age.TotalSeconds < 1) return "Just now";
        if (age.TotalMinutes < 1) return $"{(int)age.TotalSeconds}s ago";
        if (age.TotalHours < 1) return $"{(int)age.TotalMinutes}m {age.Seconds}s ago";
        if (age.TotalDays < 1) return $"{(int)age.TotalHours}h {age.Minutes}m ago";
        return $"{(int)age.TotalDays}d {age.Hours}h ago";
    }
}

internal sealed record DesktopTask(string Label, DateTimeOffset Started)
{
    public string Elapsed(DateTimeOffset now)
    {
        long seconds = Math.Max(0L, (long)(now - Started).TotalSeconds);
        return seconds < 3600 ? $"{Label}  {seconds / 60}:{seconds % 60:00}"
            : $"{Label}  {seconds / 3600}:{seconds / 60 % 60:00}:{seconds % 60:00}";
    }
}

// The desktop's local events tell us when a turn starts, publishes text, or finishes.
internal sealed partial class ChatUpdates(string path)
{
    private long position;
    private string? currentTurn;
    private string? waitReason;
    private readonly HashSet<string> windows = new();
    public string? Turn => currentTurn;
    public bool UsingDesktop { get; private set; }
    public bool TurnEnded { get; private set; }
    public DateTimeOffset? LastDesktopUse { get; private set; }
    public DateTimeOffset? DesktopEnded { get; private set; }
    public DesktopTask? CurrentTask { get; private set; }
    public string? LastCallId { get; private set; }
    public Update Latest { get; private set; } = Update.Empty;

    // At startup we only need recent evidence. Do not replay hundreds of MB
    // of historical tool output from every chat that happened to be open.
    public void StartNearEnd()
    {
        using var file = Chats.Open(path);
        if (file.Length < 512 * 1024) return;
        file.Position = file.Length - 512 * 1024;
        while (file.ReadByte() is int b && b != -1 && b != '\n') { }
        position = file.Position;
    }

    public void MarkDesktop(DateTimeOffset time, string? title, string? call)
    {
        // The hook and the transcript report the same action at slightly different times.
        bool sameCall = call != null && call == LastCallId;
        if (!sameCall && CurrentTask?.Started > time) return;
        if (!sameCall) StartAction(time, title);
        LastCallId = call;
        UsingDesktop = true;
        DesktopEnded = null;
        if (LastDesktopUse == null || time > LastDesktopUse) LastDesktopUse = time;
    }

    private void StartAction(DateTimeOffset time, string? title)
    {
        if (CurrentTask?.Started > time) return;
        CurrentTask = new("Task", time);
        waitReason = null;
        if (Latest.Time == null || Latest.Time <= time)
            Latest = new("Computer use", string.IsNullOrWhiteSpace(title) ? "Running a computer-use action." : title, time);
    }

    private void EndDesktop(DateTimeOffset time)
    {
        if (UsingDesktop) DesktopEnded = time;
        UsingDesktop = false;
        CurrentTask = null;
    }

    // KIP's wait helper is invoked directly or through the Codex shell wrapper.
    // Commentary during a wait can change the message without restarting its clock.
    [GeneratedRegex("""\bpython(?:\.exe)?\s+(?:-B\s+)?["']?[^"'\r\n]*/wait\.py["']?\s+["']?[^"'\r\n]*/ChatGPT-KIP/paused/""", RegexOptions.IgnoreCase)]
    private static partial Regex WaitCommand();

    private static bool RunsKipWait(string command)
    {
        // The shell wrapper records its command as JavaScript, sometimes with escaped paths and quotes.
        var path = command.Replace("\\\\", "\\").Replace("\\\"", "\"").Replace("\\'", "'").Replace('\\', '/');
        return WaitCommand().IsMatch(path);
    }

    private void BeginWait(DateTimeOffset time, string? reason = null)
    {
        if (!UsingDesktop || CurrentTask == null || time < CurrentTask.Started) return;
        if (CurrentTask.Label != "Wait" || (reason != null && waitReason != null &&
            !reason.Equals(waitReason, StringComparison.OrdinalIgnoreCase)))
            CurrentTask = new("Wait", time);
        if (reason != null) waitReason = reason;
    }

    public void Read()
    {
        using var file = Chats.Open(path);
        if (file.Length < position)
        {
            Latest = Update.Empty;
            currentTurn = null;
            UsingDesktop = TurnEnded = false;
            LastDesktopUse = null;
            DesktopEnded = null;
            CurrentTask = null;
            LastCallId = null;
            windows.Clear();
            position = 0;
        }
        long end = file.Length;
        if (end == position) return;
        file.Position = position;

        // A writer may pause in the middle of a UTF-8 character. Commit our
        // offset only at a newline, so we read the whole record next time.
        byte[] buffer = new byte[65536];
        using var line = new MemoryStream();
        bool oversized = false;
        while (file.Position < end)
        {
            long start = file.Position;
            int count = file.Read(buffer, 0, (int)Math.Min(buffer.Length, end - start));
            if (count == 0) break;
            int index = 0;
            while (index < count)
            {
                int newline = Array.IndexOf(buffer, (byte)'\n', index, count - index);
                int stop = newline < 0 ? count : newline;
                // Large screenshots and tool results are irrelevant to the panel.
                if (line.Length + stop - index > 1024 * 1024) oversized = true;
                if (!oversized) line.Write(buffer, index, stop - index);
                if (newline < 0) break;
                if (!oversized) Apply(System.Text.Encoding.UTF8.GetString(line.GetBuffer(), 0, (int)line.Length));
                line.SetLength(0);
                oversized = false;
                position = start + newline + 1;
                index = newline + 1;
            }
        }
    }

    private void Apply(string line)
    {
        try
        {
            using var doc = JsonDocument.Parse(line);
            var row = doc.RootElement;
            if (!row.TryGetProperty("payload", out var payload) || payload.ValueKind != JsonValueKind.Object ||
                !payload.TryGetProperty("type", out var kind) || !row.TryGetProperty("timestamp", out var stamp) ||
                stamp.ValueKind != JsonValueKind.String || !stamp.TryGetDateTimeOffset(out var time)) return;
            var type = kind.GetString();
            string? turn = payload.TryGetProperty("turn_id", out var tid) ? tid.GetString() : null;
            switch (row.GetProperty("type").GetString(), type)
            {
                case ("event_msg", "task_started"):
                    currentTurn = turn;
                    UsingDesktop = TurnEnded = false;
                    LastDesktopUse = null;
                    DesktopEnded = null;
                    CurrentTask = null;
                    LastCallId = null;
                    Latest = new("New request", "Waiting for the first progress update.", time);
                    break;
                case ("event_msg", "task_complete") when currentTurn == null || currentTurn == turn:
                    EndDesktop(time);
                    TurnEnded = true;
                    Latest = new("Turn complete", "Codex finished its reply.", time);
                    break;
                case ("event_msg", "turn_aborted") when currentTurn == null || currentTurn == turn:
                    EndDesktop(time);
                    TurnEnded = true;
                    Latest = new("Turn stopped", "The turn was interrupted.", time);
                    break;
                case ("response_item", "function_call"):
                    if (UsingDesktop && payload.TryGetProperty("name", out var tool))
                    {
                        if (tool.GetString() == "sleep" && payload.TryGetProperty("namespace", out var owner) &&
                            owner.GetString() == "clock") BeginWait(time);
                        if (tool.GetString() == "exec_command" && payload.TryGetProperty("arguments", out var arguments))
                        {
                            using var shell = JsonDocument.Parse(arguments.GetString() ?? "{}");
                            if (shell.RootElement.TryGetProperty("cmd", out var cmd) && RunsKipWait(cmd.GetString() ?? ""))
                                BeginWait(time);
                        }
                    }
                    if (payload.TryGetProperty("namespace", out var ns) && ns.GetString() == "mcp__cua_repl" &&
                        payload.TryGetProperty("name", out var name))
                    {
                        if (name.GetString() is "js_reset" or "turn_ended")
                        {
                            EndDesktop(time);
                            windows.Clear();
                        }
                        if (name.GetString() == "js" && payload.TryGetProperty("arguments", out var args))
                        {
                            using var call = JsonDocument.Parse(args.GetString() ?? "{}");
                            var input = call.RootElement;
                            if (input.TryGetProperty("completion_summary", out _)) EndDesktop(time);
                            else if (input.TryGetProperty("code", out var code) && code.GetString() is string js)
                            {
                                string? title = input.TryGetProperty("title", out var heading) ? heading.GetString() : null;
                                if (DesktopCalls.IsNative(js, windows))
                                    MarkDesktop(time, title, payload.TryGetProperty("call_id", out var id) ? id.GetString() : null);
                                // A chat can call its own window helper, e.g. await buttons(). Show its
                                // action title only inside an established desktop session. It cannot claim input.
                                else if (UsingDesktop && !string.IsNullOrWhiteSpace(title)) StartAction(time, title);
                            }
                        }
                    }
                    break;
                case ("response_item", "custom_tool_call") when UsingDesktop:
                    if (payload.TryGetProperty("name", out var wrapper) && wrapper.GetString() == "exec" &&
                        payload.TryGetProperty("input", out var script) && RunsKipWait(script.GetString() ?? ""))
                        BeginWait(time);
                    break;
                case ("response_item", "message"):
                    if (payload.GetProperty("role").GetString() != "assistant" ||
                        !payload.TryGetProperty("phase", out var phase) || phase.GetString() != "commentary") break;
                    var pieces = payload.GetProperty("content").EnumerateArray()
                        .Where(p => p.TryGetProperty("text", out _)).Select(p => p.GetProperty("text").GetString());
                    string message = string.Join(" ", pieces).Trim();
                    if (message.Length > 0)
                    {
                        foreach (string prefix in WaitPrefixes)
                        {
                            if (!message.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) continue;
                            string reason = message[prefix.Length..].Split(['.', '!', '?', '\n'], 2)[0].Trim();
                            BeginWait(time, reason);
                            break;
                        }
                        Latest = new("Latest update", message, time);
                    }
                    break;
            }
        }
        catch (JsonException) { /* Ignore a damaged record and continue with later updates. */ }
    }

    private static readonly string[] WaitPrefixes = ["Waiting for ", "I'm waiting for ", "I’m waiting for "];
}
