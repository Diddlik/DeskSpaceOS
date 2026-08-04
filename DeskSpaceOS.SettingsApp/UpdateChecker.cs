using System;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;
using Velopack;
using Velopack.Locators;
using Velopack.Sources;

namespace DeskSpaceOS_SettingsApp;

internal enum UpdateCheckStatus
{
    DevelopmentBuild,
    UpToDate,
    UpdateAvailable
}

internal readonly record struct UpdateCheckResult(
    UpdateCheckStatus Status,
    string CurrentVersion,
    string? AvailableVersion = null);

internal static class UpdateChecker
{
    private const string DefaultUpdateUrl = "https://github.com/Diddlik/DeskSpaceOS";
    private static readonly IVelopackLocator Locator =
        VelopackLocator.CreateDefaultForPlatform(null);

    public static string CurrentVersion
    {
        get
        {
            var installedVersion = Locator.CurrentlyInstalledVersion;
            if (installedVersion is not null)
                return installedVersion.ToString();

            var assembly = typeof(UpdateChecker).Assembly;
            var informationalVersion = assembly
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
                .InformationalVersion
                .Split('+')[0]
                ?? assembly.GetName().Version?.ToString(3)
                ?? "0.0.0";

#if DEBUG
            return $"{informationalVersion}-dev";
#else
            return informationalVersion;
#endif
        }
    }

    public static async Task<UpdateCheckResult> CheckAsync()
    {
        var manager = CreateManager();
        if (!manager.IsInstalled)
            return new(UpdateCheckStatus.DevelopmentBuild, CurrentVersion);

        var update = await manager.CheckForUpdatesAsync();
        return update is null
            ? new(UpdateCheckStatus.UpToDate, manager.CurrentVersion!.ToString())
            : new(
                UpdateCheckStatus.UpdateAvailable,
                manager.CurrentVersion!.ToString(),
                update.TargetFullRelease.Version.ToString());
    }

    private static UpdateManager CreateManager() =>
        new(BuildUpdateSource(), null, Locator);

    private static IUpdateSource BuildUpdateSource()
    {
        string url = GetUpdateUrl();
        if (url.StartsWith("https://github.com", StringComparison.OrdinalIgnoreCase))
            return new GithubSource(url, null, prerelease: false);

        string localPath = url.StartsWith("file://", StringComparison.OrdinalIgnoreCase)
            ? new Uri(url).LocalPath
            : url;
        return new SimpleFileSource(new DirectoryInfo(localPath));
    }

    private static string GetUpdateUrl()
    {
        string settingsPath = Path.Combine(AppContext.BaseDirectory, "appsettings.json");
        if (!File.Exists(settingsPath))
            return DefaultUpdateUrl;

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(settingsPath));
            if (document.RootElement.TryGetProperty("Updates", out var updates)
                && updates.TryGetProperty("Url", out var urlProperty)
                && urlProperty.ValueKind is JsonValueKind.String
                && !string.IsNullOrWhiteSpace(urlProperty.GetString()))
            {
                return urlProperty.GetString()!;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return DefaultUpdateUrl;
        }

        return DefaultUpdateUrl;
    }
}
