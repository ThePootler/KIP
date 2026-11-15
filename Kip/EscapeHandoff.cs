using System.ComponentModel;
using System.Runtime.InteropServices;

namespace Kip;

// Install after the native indicator appears so we can observe physical Esc
// before the computer-use helper consumes it. Always forward the key to Windows.
internal sealed class EscapeHandoff(Action<Chat> paused) : IDisposable
{
    private delegate nint KeyboardProc(int code, nint message, nint info);
    [DllImport("user32", SetLastError = true)]
    private static extern nint SetWindowsHookExW(int id, KeyboardProc callback, nint module, uint thread);
    [DllImport("kernel32", CharSet = CharSet.Unicode)]
    private static extern nint GetModuleHandleW(string? module);
    [DllImport("user32")] private static extern bool UnhookWindowsHookEx(nint hook);
    [DllImport("user32")] private static extern nint CallNextHookEx(nint hook, int code, nint message, nint info);

    internal static readonly string PauseFolder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ChatGPT-KIP", "paused");
    private nint hook;
    private KeyboardProc? callback;
    private Chat? active;
    private bool held;
    private SynchronizationContext? ui;

    public void Watch(Chat? chat)
    {
        active = chat;
        if (chat == null) { Dispose(); return; }
        if (hook != 0) return;
        ui = SynchronizationContext.Current ?? throw new InvalidOperationException("No Windows message loop.");
        callback ??= OnKey;
        hook = SetWindowsHookExW(13, callback, GetModuleHandleW(null), 0);
        if (hook == 0) throw new Win32Exception(Marshal.GetLastWin32Error());
    }

    private nint OnKey(int code, nint message, nint info)
    {
        if (code >= 0 && Marshal.ReadInt32(info) == 0x1B && (Marshal.ReadInt32(info, 8) & 0x12) == 0)
        {
            if (message == 0x101 || message == 0x105) held = false;
            if ((message == 0x100 || message == 0x104) && !held && active is Chat owner)
            {
                held = true;
                // Leave the native Esc handler free to release the mouse immediately.
                ui!.Post(_ => paused(owner), null);
            }
        }
        return CallNextHookEx(0, code, message, info);
    }

    public void Dispose()
    {
        if (hook != 0) UnhookWindowsHookEx(hook);
        hook = 0;
        held = false;
        active = null;
    }
}
