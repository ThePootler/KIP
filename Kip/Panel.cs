using System.Drawing.Drawing2D;
using System.Text.Json;

namespace Kip;

internal sealed class PanelWindow : Form
{
    private readonly Label project = LabelFor(9, FontStyle.Regular);
    private readonly Label heading = LabelFor(10, FontStyle.Bold);
    private readonly Label age = LabelFor(9, FontStyle.Regular);
    private readonly Label message = LabelFor(10, FontStyle.Regular);
    private readonly Label indicator = LabelFor(9, FontStyle.Regular);
    private readonly Label taskClock = LabelFor(9, FontStyle.Bold);
    private static readonly Color Accent = ReadAccent();
    private static readonly Color Surface = Color.FromArgb(132, 20, 38);
    private readonly ToolTip tip = new() { AutoPopDelay = 30000 };
    private string? details;

    public PanelWindow(ContextMenuStrip menu)
    {
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        AutoScaleMode = AutoScaleMode.None;
        ShowInTaskbar = false;
        TopMost = true;
        BackColor = Surface;
        DoubleBuffered = true;
        Text = "KIP · Codex status";
        ContextMenuStrip = menu;
        Controls.AddRange([project, heading, age, message, indicator, taskClock]);
        project.ForeColor = age.ForeColor = indicator.ForeColor = Color.FromArgb(255, 228, 232);
        age.TextAlign = taskClock.TextAlign = ContentAlignment.MiddleRight;
        project.AutoEllipsis = message.AutoEllipsis = heading.AutoEllipsis = indicator.AutoEllipsis = true;
        tip.SetToolTip(age, "Time since the displayed update");
        tip.SetToolTip(taskClock, "Task/Wait times the chat's action. Indicator times the visible banner.");
    }

    protected override bool ShowWithoutActivation => true;
    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= 0x08000000 | 0x00000080; // WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW
            return cp;
        }
    }

    private static Label LabelFor(float points, FontStyle style) => new()
    {
        AutoSize = false, UseMnemonic = false, ForeColor = Color.White,
        BackColor = Color.Transparent, Font = new Font("Segoe UI", points, style),
        TextAlign = ContentAlignment.MiddleLeft, Margin = Padding.Empty
    };

    private static Color ReadAccent()
    {
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(Chats.AccentFile));
            var hex = doc.RootElement.GetProperty("accentColor").GetString();
            if (hex?.Length == 7 && hex[0] == '#') return ColorTranslator.FromHtml(hex);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or
            JsonException or KeyNotFoundException or ArgumentException or FormatException) { }
        return Color.FromArgb(51, 156, 255);
    }

    public void RefreshPanel(Screen screen, FollowResult display, string? problem, Indicator scan, ContentAlignment anchor,
        DateTimeOffset now, DesktopTask? indicatorTime = null)
    {
        float scale = DeviceDpi / 96f;
        int Px(int n) => (int)Math.Round(n * scale);
        int width = Math.Min(Px(410), screen.WorkingArea.Width - Px(12));
        var update = display.Update;
        string chat = display.ChatName;
        bool hasProject = display.Project != null;
        project.Text = display.Project ?? "";
        project.Visible = hasProject;
        heading.Text = chat;
        string elapsed = update.Time.HasValue ? update.Age(now) : "";
        if (age.Text != elapsed) age.Text = elapsed;
        // The visible banner still needs a clock when its owning chat is unknown.
        var clock = problem == null ? display.Task ?? indicatorTime : indicatorTime;
        string taskTime = clock?.Elapsed(now) ?? "";
        if (taskClock.Text != taskTime) taskClock.Text = taskTime;
        string text = update.Message;
        var footer = scan.Caption;
        if (problem != null) footer = "Updates unavailable  ·  " + footer;
        if (indicator.Text != footer)
        {
            indicator.Text = footer;
            tip.SetToolTip(indicator, footer);
        }
        if (message.Text != text) message.Text = text;
        string full = (hasProject ? display.Project + "\n" : "") + $"{chat}\n\n{text}" + (problem == null ? "" : $"\n\n{problem}");
        if (details != full)
        {
            tip.SetToolTip(message, full);
            tip.SetToolTip(heading, chat);
            tip.SetToolTip(project, display.Project);
            details = full;
        }
        int inset = Px(15), textWidth = width - 2 * inset;
        int line = TextRenderer.MeasureText("Ag", message.Font).Height;
        int body = Math.Clamp(TextRenderer.MeasureText(text, message.Font, new Size(textWidth, 2000),
            TextFormatFlags.WordBreak | TextFormatFlags.TextBoxControl | TextFormatFlags.NoPrefix).Height, line, line * 3);
        project.SetBounds(inset, Px(8), textWidth - Px(100), Px(18));
        age.SetBounds(width - inset - Px(100), Px(8), Px(100), Px(19));
        heading.SetBounds(inset, hasProject ? project.Bottom : Px(8),
            hasProject || elapsed.Length == 0 ? textWidth : textWidth - Px(100), Px(22));
        message.SetBounds(inset, heading.Bottom + Px(3), textWidth, body + Px(2));
        int clockWidth = taskTime.Length == 0 ? 0 : TextRenderer.MeasureText(taskTime, taskClock.Font).Width + Px(4);
        indicator.SetBounds(inset + Px(12), message.Bottom + Px(4), textWidth - Px(12) - clockWidth, Px(18));
        taskClock.SetBounds(width - inset - clockWidth, indicator.Top, clockWidth, indicator.Height);
        var size = new Size(width, indicator.Bottom + Px(9));
        var area = screen.WorkingArea;
        // Reserve the native banner's place even while it is hidden.
        int x = anchor switch
        {
            ContentAlignment.TopLeft or ContentAlignment.BottomLeft => area.Left + Px(12),
            ContentAlignment.TopRight or ContentAlignment.BottomRight => area.Right - width - Px(12),
            _ => screen.Bounds.Left + (screen.Bounds.Width - width) / 2
        };
        x = Math.Clamp(x, area.Left, area.Right - width);
        bool bottom = anchor is ContentAlignment.BottomLeft or ContentAlignment.BottomCenter or ContentAlignment.BottomRight;
        int y = Math.Clamp(bottom ? area.Bottom - Px(66) - size.Height : area.Top + Px(156),
            area.Top, Math.Max(area.Top, area.Bottom - size.Height));
        var target = new Rectangle(new Point(x, y), size);
        if (Bounds != target) Bounds = target;
        if (ClientSize != lastSize) RoundCorners(Px(15));
        if (lit != (scan.Known && scan.Bounds.Count > 0))
        {
            lit = scan.Known && scan.Bounds.Count > 0;
            Invalidate();
        }
    }

    private Size lastSize;
    private bool lit;
    private void RoundCorners(int radius)
    {
        lastSize = ClientSize;
        using var path = new GraphicsPath();
        path.AddRoundedRectangle(new Rectangle(0, 0, Width, Height), new Size(radius, radius));
        var old = Region;
        Region = new Region(path);
        old?.Dispose();
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        int radius = (int)Math.Round(15 * DeviceDpi / 96f);
        using var border = new Pen(Color.FromArgb(200, 87, 103));
        e.Graphics.DrawRoundedRectangle(border, new Rectangle(0, 0, Width - 1, Height - 1), new Size(radius, radius));
        using var dot = new SolidBrush(lit ? Accent : Color.FromArgb(159, 159, 161));
        float diameter = 6 * DeviceDpi / 96f;
        e.Graphics.FillEllipse(dot, message.Left, indicator.Top + (indicator.Height - diameter) / 2, diameter, diameter);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) tip.Dispose();
        base.Dispose(disposing);
    }
}
