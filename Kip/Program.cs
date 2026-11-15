namespace Kip;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        using var single = new Mutex(true, @"Local\ChatGPT-KIP.Panel", out bool first);
        if (!first) return;
        string? requested = args.Length == 2 && args[0] == "--thread" ? args[1] : null;
        using var panel = new PanelContext(requested);
        Application.Run(panel);
    }
}

internal sealed class PanelContext : ApplicationContext
{
    private readonly Dictionary<string, PanelWindow> windows = new();
    private readonly ContextMenuStrip menu = new();
    private readonly NotifyIcon tray;
    private readonly System.Windows.Forms.Timer timer = new() { Interval = 400 };
    private readonly ToolStripMenuItem chatMenu = new("Follow chat");
    private ContentAlignment placement = ContentAlignment.TopCenter;
    private static readonly string PlacementFile = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ChatGPT-KIP", "placement.txt");
    private readonly ToolStripMenuItem preferred = new("Show panels with computer-use indicator") { Checked = true, CheckOnClick = true };
    private readonly string? requested;
    private readonly AutoFollow tracker = new();
    private readonly EscapeHandoff handoff;
    private Chat? activeDesktopChat;
    private bool reportedWatchFailure;
    private bool automatic;
    private FollowResult display = new("Auto-follow", Update.Empty);
    private bool overlayVisible;
    private int indicatorRevision;
    private Chat? selected;
    private ChatUpdates? reader;
    private string? problem;
    private bool busy;
    private long pollAfter;
    private List<Chat> chats = [];
    private int selectionVersion;
    private bool closed;

    public PanelContext(string? requested)
    {
        handoff = new EscapeHandoff(PauseMouse);
        this.requested = requested;
        automatic = requested == null;
        try
        {
            if (File.Exists(PlacementFile) && Enum.TryParse<ContentAlignment>(File.ReadAllText(PlacementFile), out var saved)
                && Enum.IsDefined(saved)) placement = saved;
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        menu.Items.Add(chatMenu);
        var placeMenu = new ToolStripMenuItem("Native banner position");
        foreach (var (name, anchor) in new[] {
            ("Top left", ContentAlignment.TopLeft), ("Top centre", ContentAlignment.TopCenter),
            ("Top right", ContentAlignment.TopRight), ("Bottom left", ContentAlignment.BottomLeft),
            ("Bottom centre", ContentAlignment.BottomCenter), ("Bottom right", ContentAlignment.BottomRight) })
        {
            var item = new ToolStripMenuItem(name) { Checked = placement == anchor };
            item.Click += (_, _) =>
            {
                placement = anchor;
                foreach (ToolStripMenuItem opt in placeMenu.DropDownItems) opt.Checked = opt == item;
                Draw();
                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(PlacementFile)!);
                    File.WriteAllText(PlacementFile, anchor.ToString());
                }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            };
            placeMenu.DropDownItems.Add(item);
        }
        menu.Items.Add(placeMenu);
        menu.Items.Add(preferred);
        menu.Items.Add("Read full update", null, (_, _) =>
            MessageBox.Show(display.Update.Message, display.Project == null ? display.ChatName : $"{display.Project} · {display.ChatName}",
                MessageBoxButtons.OK, MessageBoxIcon.Information));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Exit KIP", null, (_, _) => ExitThread());
        chatMenu.DropDownOpening += (_, _) => FillChatMenu();
        // A placeholder enables the submenu before its first opening.
        chatMenu.DropDownItems.Add("Loading chats…");
        menu.Opening += async (_, _) =>
        {
            try { chats = await Task.Run(Chats.List); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        };
        tray = new NotifyIcon { Text = "KIP · Keep It Progressing", Icon = SystemIcons.Information,
            ContextMenuStrip = menu, Visible = true };
        preferred.CheckedChanged += (_, _) => Draw();
        tray.DoubleClick += (_, _) => preferred.Checked = !preferred.Checked;
        timer.Tick += (_, _) => Tick();
        timer.Start();
        Tick();
    }

    private void FillChatMenu()
    {
        chatMenu.DropDownItems.Clear();
        var auto = new ToolStripMenuItem("Automatically follow computer use") { Checked = automatic };
        auto.Click += (_, _) =>
        {
            automatic = true;
            selectionVersion++;
            display = new("Auto-follow", Update.Empty);
            pollAfter = 0;
        };
        chatMenu.DropDownItems.Add(auto);
        chatMenu.DropDownItems.Add(new ToolStripSeparator());
        var choices = chats.Take(20).ToList();
        if (selected != null && choices.All(c => c.Id != selected.Id)) choices.Add(selected);
        foreach (var chat in choices)
        {
            var item = new ToolStripMenuItem(chat.FullName.Replace("&", "&&")) { Checked = !automatic && chat.Id == selected?.Id };
            item.Click += (_, _) => SelectChat(chat);
            chatMenu.DropDownItems.Add(item);
        }
        if (choices.Count == 0) chatMenu.DropDownItems.Add("No local chats found").Enabled = false;
    }

    private void SelectChat(Chat chat)
    {
        automatic = false;
        selected = chat;
        selectionVersion++;
        reader = new(chat.Path);
        display = new(chat.Name, Update.Empty, Project: "Pinned · " + chat.Project);
        problem = null;
        pollAfter = 0;
        Draw();
    }

    private async void Tick()
    {
        Draw();
        if (busy || closed || Environment.TickCount64 < pollAfter) return;
        busy = true;
        try
        {
            // Track actual desktop activity even when the tray is pinned to a different chat.
            int modeVersion = selectionVersion;
            bool visible = overlayVisible;
            int revision = indicatorRevision;
            var match = await Task.Run(() => tracker.Poll(visible));
            if (closed) return;
            if (visible && revision == indicatorRevision) activeDesktopChat = match.ActiveChat;
            if (automatic)
            {
                if (modeVersion != selectionVersion || revision != indicatorRevision) return;
                display = match;
                problem = null;
                pollAfter = Environment.TickCount64 + 350;
                Draw();
                return;
            }
            if (reader == null)
            {
                chats = await Task.Run(Chats.List);
                if (closed) return;
                var chat = requested == null ? chats.FirstOrDefault() : chats.FirstOrDefault(c => c.Id == requested);
                if (chat == null)
                {
                    display = new("Follow chat", new("Follow chat", "Choose a local Codex chat from the KIP tray menu.", null));
                    pollAfter = Environment.TickCount64 + 5000;
                    return;
                }
                SelectChat(chat);
            }
            int version = selectionVersion;
            var source = reader!;
            await Task.Run(source.Read);
            if (closed || version != selectionVersion) return;
            display = new(selected!.Name, source.Latest, Project: "Pinned · " + selected.Project,
                Task: source.CurrentTask);
            problem = null;
            pollAfter = Environment.TickCount64 + 650;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
        {
            activeDesktopChat = null;
            handoff.Watch(null);
            problem = e.Message;
            pollAfter = Environment.TickCount64 + 3000;
        }
        finally { busy = false; }
    }

    private void PauseMouse(Chat chat)
    {
        try
        {
            Directory.CreateDirectory(EscapeHandoff.PauseFolder);
            // Presence alone blocks the hook. The marker survives restarts and later turns.
            using (File.Create(Path.Combine(EscapeHandoff.PauseFolder, chat.Id))) { }
            tray.ShowBalloonTip(3500, "KIP paused mouse use", $"{chat.Name} · Send “KIP resume mouse” in that chat to allow it again.", ToolTipIcon.Info);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            tray.ShowBalloonTip(5000, "KIP could not save the mouse pause", e.Message, ToolTipIcon.Error);
        }
    }

    private void Draw()
    {
        if (closed) return;
        var scan = Indicator.Read();
        bool visible = scan.Known && scan.Bounds.Count > 0;
        if (visible != overlayVisible)
        {
            overlayVisible = visible;
            activeDesktopChat = null;
            indicatorRevision++;
            pollAfter = 0;
            if (visible && automatic)
                display = new("Identifying chat…", new("Computer use", "Checking which chat is using the desktop.", null));
        }
        try
        {
            handoff.Watch(overlayVisible ? activeDesktopChat : null);
            reportedWatchFailure = false;
        }
        catch (System.ComponentModel.Win32Exception e)
        {
            if (!reportedWatchFailure)
                tray.ShowBalloonTip(5000, "KIP cannot detect Esc", e.Message, ToolTipIcon.Warning);
            reportedWatchFailure = true;
        }
        // The current native overlay spans the virtual desktop and paints a
        // banner per monitor. Smaller overlay HWNDs only cover their own screens.
        var shown = overlayVisible && preferred.Checked
            ? Screen.AllScreens.Where(s => scan.Bounds.Any(r => r.IntersectsWith(s.Bounds))).ToArray()
            : Array.Empty<Screen>();
        foreach (string name in windows.Keys.ToArray())
        {
            if (shown.Any(s => s.DeviceName == name)) continue;
            windows[name].Dispose();
            windows.Remove(name);
        }
        var now = DateTimeOffset.UtcNow;
        foreach (var screen in shown)
        {
            if (!windows.TryGetValue(screen.DeviceName, out var window))
            {
                window = new PanelWindow(menu) { Location = screen.WorkingArea.Location };
                windows.Add(screen.DeviceName, window);
            }
            window.RefreshPanel(screen, display, problem, scan, placement, now);
            if (!window.Visible) window.Show();
        }
    }

    protected override void ExitThreadCore()
    {
        closed = true;
        handoff.Dispose();
        timer.Stop();
        tray.Visible = false;
        foreach (var window in windows.Values) window.Dispose();
        tray.Dispose();
        timer.Dispose();
        menu.Dispose();
        base.ExitThreadCore();
    }
}
