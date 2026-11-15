using System.Text.Json;

namespace Kip;

// Resolve the desktop app's sidebar assignment. A chat's working directory
// can be a subfolder or a worktree and can differ from its owning project.
internal sealed class ProjectNames(string path)
{
    private readonly object gate = new();
    private Dictionary<string, string> names = new();
    private (DateTime Written, long Size)? loaded;

    public IReadOnlyDictionary<string, string> Read()
    {
        lock (gate)
        {
            try
            {
                var file = new FileInfo(path);
                if (!file.Exists) return new Dictionary<string, string>();
                var version = (file.LastWriteTimeUtc, file.Length);
                // This file is several MB. Tray and auto-follow polls can share one parsed snapshot.
                if (loaded == version) return names;
                using var input = Chats.Open(path);
                using var doc = JsonDocument.Parse(input);
                var root = doc.RootElement;
                if (root.ValueKind != JsonValueKind.Object) return names;
                var byChat = new Dictionary<string, string>();
                if (root.TryGetProperty("local-projects", out var projects) && projects.ValueKind == JsonValueKind.Object &&
                    root.TryGetProperty("thread-project-assignments", out var assignments) && assignments.ValueKind == JsonValueKind.Object)
                {
                    foreach (var chat in assignments.EnumerateObject())
                    {
                        if (Text(chat.Value, "projectKind") != "local") continue;
                        var id = Text(chat.Value, "projectId");
                        if (id == null || !projects.TryGetProperty(id, out var project)) continue;
                        var name = Text(project, "name");
                        if (!string.IsNullOrWhiteSpace(name)) byChat[chat.Name] = name;
                    }
                }
                if (root.TryGetProperty("projectless-thread-ids", out var unassigned) && unassigned.ValueKind == JsonValueKind.Array)
                    foreach (var id in unassigned.EnumerateArray())
                        if (id.ValueKind == JsonValueKind.String) byChat.TryAdd(id.GetString()!, "No project");
                names = byChat;
                loaded = version;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
            {
                // A partial replacement of the app's state should not interrupt tracking or Esc.
            }
            return names;
        }
    }

    private static string? Text(JsonElement row, string key) =>
        row.ValueKind == JsonValueKind.Object && row.TryGetProperty(key, out var value) &&
        value.ValueKind == JsonValueKind.String ? value.GetString() : null;
}
