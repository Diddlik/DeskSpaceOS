using System;
using System.Diagnostics;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;

namespace DeskSpaceOS_SettingsApp;

/// <summary>
/// Provides application-specific behavior to supplement the default Application class.
/// </summary>
public partial class App : Application
{
    private const string SingleInstanceMutexName = "Local\\DeskSpaceOS.SettingsApp";
    private const string ActivationPipeName = "DeskSpaceOS.SettingsApp.Activation";

    private readonly Mutex _singleInstanceMutex;
    private readonly bool _ownsSingleInstanceMutex;
    private CancellationTokenSource? _activationListenerCancellation;
    private Window? _window;

    public App()
    {
        _singleInstanceMutex = new Mutex(true, SingleInstanceMutexName, out _ownsSingleInstanceMutex);
        InitializeComponent();
    }

    protected override void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
    {
        if (!_ownsSingleInstanceMutex)
        {
            SignalExistingInstance();
            Environment.Exit(0);
            return;
        }

        _window = new MainWindow();
        _window.Activate();
        BringSettingsWindowToFront();
        StartActivationListener();
    }

    public Window? GetWindow() => _window;

    private void StartActivationListener()
    {
        _activationListenerCancellation = new CancellationTokenSource();
        _ = ListenForActivationRequestsAsync(_activationListenerCancellation.Token);
    }

    private async Task ListenForActivationRequestsAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                using var pipe = new NamedPipeServerStream(
                    ActivationPipeName,
                    PipeDirection.In,
                    1,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous);

                await pipe.WaitForConnectionAsync(cancellationToken);
                _window?.DispatcherQueue.TryEnqueue(BringSettingsWindowToFront);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch
            {
                await Task.Delay(250, cancellationToken);
            }
        }
    }

    private static void SignalExistingInstance()
    {
        try
        {
            using var pipe = new NamedPipeClientStream(".", ActivationPipeName, PipeDirection.Out);
            pipe.Connect(750);
        }
        catch
        {
            // The mutex still prevents a duplicate settings window while the first instance starts.
        }

        BringExistingProcessWindowToFront();
    }

    private static void BringExistingProcessWindowToFront()
    {
        var currentProcess = Process.GetCurrentProcess();
        foreach (var process in Process.GetProcessesByName(currentProcess.ProcessName))
        {
            if (process.Id == currentProcess.Id || process.MainWindowHandle == IntPtr.Zero)
                continue;

            ShowWindow(process.MainWindowHandle, ShowWindowCommand.Restore);
            SetForegroundWindow(process.MainWindowHandle);
            return;
        }
    }

    private void BringSettingsWindowToFront()
    {
        if (_window == null)
            return;

        _window.Activate();

        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(_window);
        if (hwnd == IntPtr.Zero)
            return;

        ShowWindow(hwnd, ShowWindowCommand.Restore);
        SetForegroundWindow(hwnd);
    }

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, ShowWindowCommand nCmdShow);

    private enum ShowWindowCommand
    {
        Restore = 9
    }
}
