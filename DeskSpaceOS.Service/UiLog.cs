using Microsoft.Extensions.Logging;

namespace DeskSpaceOS.Service;

/// <summary>
/// Log sink for the WPF controls that XAML instantiates through a parameterless
/// constructor and therefore cannot receive an <see cref="ILogger"/> by injection.
/// <see cref="OverlayWindow"/> assigns the host logger once at startup.
/// </summary>
internal static class UiLog
{
    internal static ILogger? Logger { get; set; }

    internal static void Warn(Exception exception, string message, params object?[] args) =>
        Logger?.LogWarning(exception, message, args);
}
