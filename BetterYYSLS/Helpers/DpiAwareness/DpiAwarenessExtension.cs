using System.Windows;

namespace BetterYYSLS.Helpers.DpiAwareness;

internal static class DpiAwarenessExtension
{
    public static void InitializeDpiAwareness(this Window window)
    {
        _ = new DpiAwarenessController(window);
    }
}