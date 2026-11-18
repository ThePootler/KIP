using System.Runtime.InteropServices;
using System.Text;

namespace Kip;

internal sealed class IndicatorTimer
{
    private DesktopTask? visibleSince;

    public DesktopTask? Read(Indicator indicator, DateTimeOffset now)
    {
        if (!indicator.Known || indicator.Bounds.Count == 0) visibleSince = null;
        else visibleSince ??= new("Indicator", now);
        return visibleSince;
    }
}

internal sealed record Indicator(bool Known, List<Rectangle> Bounds)
{
    public string Caption => !Known ? "Indicator unknown · control unconfirmed" :
        Bounds.Count > 0 ? "Computer-use indicator visible" : "Indicator absent · control unconfirmed";

    private delegate bool EnumWindow(nint hwnd, nint param);
    [StructLayout(LayoutKind.Sequential)]
    private struct Rect { public int Left, Top, Right, Bottom; }
    [DllImport("user32")] private static extern bool EnumWindows(EnumWindow callback, nint state);
    [DllImport("user32")] private static extern bool IsWindowVisible(nint hwnd);
    [DllImport("user32", CharSet = CharSet.Unicode)] private static extern int GetClassName(nint hwnd, StringBuilder name, int length);
    [DllImport("user32")] private static extern bool GetWindowRect(nint hwnd, out Rect rect);
    [DllImport("user32", SetLastError = true)] private static extern nint OpenInputDesktop(uint flags, bool inherit, uint access);
    [DllImport("user32")] private static extern bool CloseDesktop(nint desktop);
    [DllImport("user32")] private static extern nint GetThreadDesktop(uint thread);
    [DllImport("kernel32")] private static extern uint GetCurrentThreadId();
    [DllImport("user32", CharSet = CharSet.Unicode)]
    private static extern bool GetUserObjectInformation(nint obj, int index, StringBuilder name, int size, out int needed);

    private static string? DesktopName(nint desktop)
    {
        var name = new StringBuilder(256);
        return GetUserObjectInformation(desktop, 2, name, name.Capacity * 2, out _) ? name.ToString() : null;
    }

    public static Indicator Read()
    {
        var bounds = new List<Rectangle>();
        // A locked or secure desktop must not be mistaken for a successful empty scan.
        nint input = OpenInputDesktop(0, false, 1);
        if (input == 0) return new(false, bounds);
        try
        {
            var name = DesktopName(input);
            if (name == null || name != DesktopName(GetThreadDesktop(GetCurrentThreadId()))) return new(false, bounds);
        }
        finally { CloseDesktop(input); }

        var cls = new StringBuilder(100);
        bool located = true;
        bool success = EnumWindows((hwnd, _) =>
        {
            if (IsWindowVisible(hwnd) && GetClassName(hwnd, cls, cls.Capacity) > 0 &&
                cls.ToString() == "CodexComputerUseCursorOverlay")
            {
                if (GetWindowRect(hwnd, out var rect))
                    bounds.Add(Rectangle.FromLTRB(rect.Left, rect.Top, rect.Right, rect.Bottom));
                else located = false;
            }
            return true;
        }, 0);
        return new(success && located, bounds);
    }
}
