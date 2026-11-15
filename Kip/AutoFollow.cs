using System.Text.Json;
using System.Text.RegularExpressions;

namespace Kip;

internal static partial class DesktopCalls
{
    // Match actual API calls only. Quoted snippets and comments can contain examples.
    [GeneratedRegex("""//[^\r\n]*|/\*[\s\S]*?\*/|"(?:\\.|[^"\\])*"|'(?:\\.|[^'\\])*'|`(?:\\.|[^`\\])*`""")]
    private static partial Regex Literals();
    [GeneratedRegex(@"\bcua\s*\.\s*(?:(?:getApp|getState|listWindows|listApps)\s*\(|computer\s*\.\s*launch_app\s*\()")]
    private static partial Regex NativeApi();
    [GeneratedRegex(@"\b(?:const|let|var)\s+(?<name>[a-zA-Z_$][\w$]*)\s*=\s*await\s+cua\s*\.\s*getApp\s*\(")]
    private static partial Regex SelectedWindow();
    [GeneratedRegex(@"\b(?<name>[a-zA-Z_$][\w$]*)\s*\.\s*(?:pressKey|click|drag|scroll|paste|typeText|setValue|selectText|getAXState|getScreenshot|getAXStateAndScreenshot|performSecondaryAction)\s*\(")]
    private static partial Regex WindowAction();
    public static bool IsNative(string? code) =>
        code != null && NativeApi().IsMatch(Literals().Replace(code, " "));

    public static bool IsNative(string code, HashSet<string> windows)
    {
        var calls = Literals().Replace(code, " ");
        foreach (Match m in SelectedWindow().Matches(calls)) windows.Add(m.Groups["name"].Value);
        return NativeApi().IsMatch(calls) || WindowAction().Matches(calls)
            .Any(m => windows.Contains(m.Groups["name"].Value));
    }
}

internal sealed record FollowResult(string ChatName, Update Update, Chat? ActiveChat = null,
    string? Project = null, DesktopTask? Task = null);

internal sealed class AutoFollow(Func<List<Chat>>? listChats = null, string? hintDirectory = null)
{
    private readonly Func<List<Chat>> discover = listChats ?? Chats.List;
    private readonly Dictionary<string, (Chat Chat, ChatUpdates Reader)> watched = new();
    private long nextDiscovery;
    private string? lastChat;
    private readonly Dictionary<string, string> appliedHooks = new();
    private readonly string HintFolder = hintDirectory ?? Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ChatGPT-KIP", "activity");

    public FollowResult Poll(bool overlayVisible)
    {
        var now = DateTimeOffset.UtcNow;
        var hints = Directory.Exists(HintFolder)
            ? Directory.EnumerateFiles(HintFolder, "*.json")
                .Where(path => now - File.GetLastWriteTimeUtc(path) < TimeSpan.FromSeconds(15))
                .ToDictionary(path => Path.GetFileNameWithoutExtension(path))
            : new Dictionary<string, string>();
        if (Environment.TickCount64 >= nextDiscovery)
        {
            var since = DateTime.UtcNow.AddHours(-2);
            foreach (var chat in discover())
            {
                if (watched.TryGetValue(chat.Id, out var existing))
                {
                    watched[chat.Id] = (chat, existing.Reader);
                    continue;
                }
                // Windows can keep the original write time while Codex holds the log open.
                if (!hints.ContainsKey(chat.Id) && File.GetLastWriteTimeUtc(chat.Path) < since) continue;
                var reader = new ChatUpdates(chat.Path);
                reader.StartNearEnd();
                watched.Add(chat.Id, (chat, reader));
            }
            nextDiscovery = Environment.TickCount64 + 3000;
        }
        foreach (var entry in watched.Values.ToArray())
        {
            try { entry.Reader.Read(); }
            catch (FileNotFoundException) { watched.Remove(entry.Chat.Id); }
            catch (DirectoryNotFoundException) { watched.Remove(entry.Chat.Id); }
        }
        // Plugin hooks may arrive before the local transcript writer flushes.
        // The turn ID and timestamp keep delayed hints out of subsequent turns.
        foreach (var (id, path) in hints)
        {
            if (!watched.TryGetValue(id, out var entry)) continue;
            using var file = Chats.Open(path);
            using var doc = JsonDocument.Parse(file);
            var hint = doc.RootElement;
            var token = hint.GetProperty("at").GetDateTimeOffset();
            string? call = hint.GetProperty("call").GetString();
            if (call == null || appliedHooks.GetValueOrDefault(id) == call) continue;
            if (entry.Reader.Turn != null && hint.GetProperty("turn").GetString() != entry.Reader.Turn) continue;
            if (call == entry.Reader.LastCallId)
            {
                // A short call can finish before the first paint. Remember it as Last.
                if (entry.Reader.LastDesktopUse != null && (lastChat == null ||
                    !watched.TryGetValue(lastChat, out var previous) ||
                    previous.Reader.LastDesktopUse == null || entry.Reader.LastDesktopUse > previous.Reader.LastDesktopUse))
                    lastChat = id;
            }
            else
            {
                if (entry.Reader.LastDesktopUse >= token || entry.Reader.DesktopEnded >= token || entry.Reader.TurnEnded) continue;
                entry.Reader.MarkDesktop(token, hint.GetProperty("title").GetString(), call);
            }
            appliedHooks[id] = call;
        }
        var candidates = watched.Values.Where(w => w.Reader.UsingDesktop &&
            (overlayVisible || now - w.Reader.LastDesktopUse < TimeSpan.FromMinutes(2))).ToArray();
        if (candidates.Length > 1)
        {
            lastChat = null;
            string names = string.Join(", ", candidates.Select(x => x.Chat.FullName));
            return new("Multiple computer-use chats", new("Computer use", $"Desktop activity in {names}. The indicator does not identify which chat has input.", null));
        }
        if (candidates.Length == 1)
        {
            lastChat = candidates[0].Chat.Id;
            var (chat, reader) = candidates[0];
            return new(chat.Name, reader.Latest, chat, chat.Project, reader.CurrentTask);
        }
        if (overlayVisible)
        {
            const string unknown = "Windows shows the indicator. KIP hasn't identified an active desktop chat.";
            // A completion is evidence about the previous session only. Never arm Esc for it.
            var finished = watched.Values.Where(w => w.Reader.DesktopEnded >= now.AddMinutes(-1))
                .OrderByDescending(w => w.Reader.DesktopEnded).FirstOrDefault();
            if (finished.Chat != null)
                return new("Chat not identified", new("Computer use",
                    $"Last to finish: {finished.Chat.FullName}. Windows still shows the indicator.", finished.Reader.DesktopEnded));
            return new("Chat not identified", new("Computer use", unknown, null));
        }
        if (lastChat != null && watched.TryGetValue(lastChat, out var prior) && prior.Reader.LastDesktopUse != null)
            return new("Last · " + prior.Chat.Name, prior.Reader.Latest, Project: prior.Chat.Project);
        return new("Auto-follow", new("Auto-follow", "Listening for a Codex chat to use this PC.", null));
    }
}
