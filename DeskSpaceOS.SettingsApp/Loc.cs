using System.Globalization;
using Microsoft.Windows.ApplicationModel.Resources;

namespace DeskSpaceOS_SettingsApp;

/// <summary>
/// Thin wrapper over the app's default <c>Resources.resw</c> map so code-behind
/// can resolve localized strings without hard-coding UI text.
/// </summary>
internal static class Loc
{
    private static readonly ResourceLoader Loader = new();

    /// <summary>Returns the localized string for <paramref name="key"/> (empty if absent).</summary>
    public static string Get(string key) => Loader.GetString(key);

    /// <summary>Formats a localized string using the current culture.</summary>
    public static string Format(string key, params object?[] args) =>
        string.Format(CultureInfo.CurrentCulture, Get(key), args);
}
