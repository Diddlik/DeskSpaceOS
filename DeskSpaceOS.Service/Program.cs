using DeskSpaceOS.Service;
using DeskSpaceOS.Core.Storage;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Win32;
using Velopack;

// Must be first — intercepts Velopack's own install/update/uninstall CLI verbs
// before any app logic runs.
VelopackApp.Build()
    .SetArgs(args)
    .OnAfterInstallFastCallback(_ => RegisterStartup())
    .OnAfterUpdateFastCallback(_ => RegisterStartup())
    .OnBeforeUninstallFastCallback(_ => UnregisterStartup())
    .Run();

// Keep legacy CLI args so the SettingsApp "Start with Windows" toggle still works
// when running outside of a Velopack install (dev builds, portable).
if (args.Length > 0)
{
    switch (args[0].ToLowerInvariant())
    {
        case "--install":
            RegisterStartup();
            return;
        case "--uninstall":
            UnregisterStartup();
            return;
    }
}

// Content root must be the executable's directory: the service autostarts from the
// HKCU Run key, where the working directory is System32 — a relative appsettings.json
// (which carries Updates:Url) would otherwise never be found.
var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
{
    Args = args,
    ContentRootPath = AppContext.BaseDirectory,
});
builder.Services.AddHostedService<Worker>();
builder.Services.AddHostedService<UpdateService>();

var host = builder.Build();
host.Run();

static void RegisterStartup()
{
    const string keyPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
    const string valueName = "DeskSpaceOS";

    string? exePath = Environment.ProcessPath
        ?? System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName;
    if (exePath is null) return;

    using var key = Registry.CurrentUser.OpenSubKey(keyPath, writable: true);
    key?.SetValue(valueName, $"\"{exePath}\"");

    var settings = AppSettingsStore.Load();
    settings.StartWithWindows = true;
    AppSettingsStore.Save(settings);
}

static void UnregisterStartup()
{
    const string keyPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
    const string valueName = "DeskSpaceOS";

    using var key = Registry.CurrentUser.OpenSubKey(keyPath, writable: true);
    key?.DeleteValue(valueName, throwOnMissingValue: false);

    var settings = AppSettingsStore.Load();
    settings.StartWithWindows = false;
    AppSettingsStore.Save(settings);
}
