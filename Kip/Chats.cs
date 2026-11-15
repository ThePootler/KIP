using System.Text.Json;

namespace Kip;

internal sealed record Chat(string Id, string Name, string Path, string Project = "Project unknown")
{
    public string FullName => $"{Project} · {Name}";
}

internal static class Chats
{
    private static readonly string Home = Environment.GetEnvironmentVariable("CODEX_HOME")
        ?? System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex");

    public static string AccentFile => System.IO.Path.Combine(Home, "computer-use", "config.json");
    private static readonly ProjectNames Projects = new(System.IO.Path.Combine(Home, ".codex-global-state.json"));

    public static List<Chat> List()
    {
        var projects = Projects.Read();
        var titles = new Dictionary<string, string>();
        string index = System.IO.Path.Combine(Home, "session_index.jsonl");
        if (File.Exists(index))
        {
            using var reader = new StreamReader(Open(index));
            while (reader.ReadLine() is { } line)
            {
                try
                {
                    using var doc = JsonDocument.Parse(line);
                    var row = doc.RootElement;
                    var id = row.GetProperty("id").GetString();
                    if (id != null && row.TryGetProperty("thread_name", out var name))
                        titles[id] = name.GetString() ?? id;
                }
                catch (JsonException) { /* The index can be in the middle of an append. */ }
            }
        }

        var folder = new DirectoryInfo(System.IO.Path.Combine(Home, "sessions"));
        if (!folder.Exists) return [];
        var chats = new List<Chat>();
        foreach (var file in folder.EnumerateFiles("rollout-*.jsonl", SearchOption.AllDirectories)
                     .OrderByDescending(f => f.LastWriteTimeUtc))
        {
            var stem = System.IO.Path.GetFileNameWithoutExtension(file.Name);
            if (stem.Length < 36) continue;
            string id = stem[^36..];
            if (!Guid.TryParse(id, out _)) continue;
            chats.Add(new Chat(id, titles.GetValueOrDefault(id, id), file.FullName, projects.GetValueOrDefault(id, "Project unknown")));
        }
        return chats;
    }

    internal static FileStream Open(string path) =>
        new(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
}
