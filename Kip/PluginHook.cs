using System.Diagnostics;
using System.Text.Json;

namespace Kip;

internal static class PluginHook
{
    private const string Paused = "Esc was pressed. Stop waiting for mouse work and leave the PC to the user. " +
        "Do not retry computer use or switch tools to regain input. Wait for 'KIP resume mouse' in this chat.";

    internal static int Run()
    {
        try
        {
            using var document = JsonDocument.Parse(Console.OpenStandardInput());
            Handle(document.RootElement);
            return 0;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException or FormatException
            or InvalidOperationException or KeyNotFoundException or System.ComponentModel.Win32Exception)
        {
            Console.Error.WriteLine($"KIP hook: {e.Message}");
            return 1;
        }
    }

    private static void Handle(JsonElement data)
    {
        string chat = data.GetProperty("session_id").GetGuid().ToString();
        string local = Environment.GetEnvironmentVariable("LOCALAPPDATA")
            ?? Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string folder = Path.Combine(local, "ChatGPT-KIP");
        string marker = Path.Combine(folder, "paused", chat);
        string? eventName = data.GetProperty("hook_event_name").GetString();
        if (eventName == "UserPromptSubmit")
        {
            string? prompt = data.GetProperty("prompt").GetString();
            if (string.Equals(prompt?.Trim(), "KIP resume mouse", StringComparison.OrdinalIgnoreCase))
            {
                try { File.Delete(marker); }
                catch (DirectoryNotFoundException) { /* This chat has never paused. */ }
                Reply(new { hookEventName = eventName, additionalContext = "The user explicitly allowed mouse use again for this chat." });
            }
            else if (MousePause.IsPaused(marker))
                Reply(new { hookEventName = eventName, additionalContext = Paused });
            return;
        }
        if (eventName != "PreToolUse") return;
        string? tool = data.GetProperty("tool_name").GetString();
        if (tool is not ("mcp__cua_repl__js" or "mcp__node_repl__js")) return;
        var input = data.GetProperty("tool_input");
        if (input.TryGetProperty("completion_summary", out _) && input.GetPropertyCount() == 1) return;
        bool native = input.TryGetProperty("code", out var code) && DesktopCalls.IsNative(code.GetString());
        // Node also runs calculations and file work. A mouse pause only blocks
        // its recognised desktop calls, not unrelated JavaScript.
        if (tool == "mcp__node_repl__js" && !native) return;

        // Check the pause even for browser calls or a click on a previously selected window.
        try
        {
            if (MousePause.IsPaused(marker)) { Deny(Paused); return; }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Deny("KIP could not read this chat's mouse permission.");
            return;
        }
        if (!native) return;

        string title = input.TryGetProperty("title", out var heading) ? heading.GetString() ?? "" : "";
        var receipt = new { turn = data.GetProperty("turn_id").GetGuid(), at = DateTimeOffset.UtcNow,
            call = data.GetProperty("tool_use_id").GetString(), title = title.Length > 240 ? title[..240] : title };
        string activity = Directory.CreateDirectory(Path.Combine(folder, "activity")).FullName;
        string temp = Path.Combine(activity, Guid.NewGuid() + ".tmp");
        // The panel may read this receipt while another hook is writing one for the same chat.
        try
        {
            File.WriteAllText(temp, JsonSerializer.Serialize(receipt));
            File.Move(temp, Path.Combine(activity, chat + ".json"), overwrite: true);
        }
        finally { File.Delete(temp); }

        string executable = Path.Combine(AppContext.BaseDirectory, "KIP.exe");
        if (Mutex.TryOpenExisting(Program.PanelMutex, out var panel)) panel.Dispose();
        else Process.Start(new ProcessStartInfo(executable) { UseShellExecute = true })?.Dispose();

        // A pipeline makes PowerShell wait for a Windows GUI executable and show its output.
        string command = $"& {PowerShellQuote(executable)} --wait {PowerShellQuote(marker)} SECONDS | Out-Host";
        Reply(new { hookEventName = eventName, additionalContext =
            "Release desktop control whenever the work permits. When you need to wait while keeping this computer-use session open, " +
            $"publish the reason, then run this PowerShell command through the shell tool: {command}. " +
            "Replace SECONDS with the desired duration. If it reports PAUSED, end computer use and wait for the user." });
    }

    private static string PowerShellQuote(string path) => "'" + path.Replace("'", "''") + "'";
    private static void Deny(string reason) =>
        Reply(new { hookEventName = "PreToolUse", permissionDecision = "deny", permissionDecisionReason = reason });
    private static void Reply(object output) => Console.WriteLine(JsonSerializer.Serialize(new { hookSpecificOutput = output }));
}
