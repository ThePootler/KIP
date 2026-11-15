using System.Diagnostics;
using System.Globalization;

namespace Kip;

internal static class MousePause
{
    // File.Exists also returns false for access errors. Only a missing marker grants permission.
    internal static bool IsPaused(string marker)
    {
        try { File.GetAttributes(marker); return true; }
        catch (FileNotFoundException) { return false; }
        catch (DirectoryNotFoundException) { return false; }
    }

    internal static int Wait(string[] args)
    {
        if (args.Length != 3 || !double.TryParse(args[2], NumberStyles.Float, CultureInfo.InvariantCulture, out double seconds)
            || !double.IsFinite(seconds) || seconds < 0)
        {
            Console.Error.WriteLine("Usage: --wait PAUSE_FILE SECONDS (finite and nonnegative)");
            return 1;
        }

        var clock = Stopwatch.StartNew();
        try
        {
            while (true)
            {
                if (IsPaused(args[1]))
                {
                    Console.WriteLine("PAUSED: Esc was pressed. End computer use and await explicit permission.");
                    return 0;
                }
                double remaining = seconds - clock.Elapsed.TotalSeconds;
                if (remaining <= 0) break;
                Thread.Sleep(TimeSpan.FromSeconds(Math.Min(0.1, remaining)));
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Console.WriteLine("KIP could not read mouse permission. Stop desktop work and tell the user.");
            return 1;
        }
        Console.WriteLine("Wait elapsed.");
        return 0;
    }
}
