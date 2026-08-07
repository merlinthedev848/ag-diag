using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;

namespace agilicomsptoolkit;

public partial class App : Application
{
    // kernel32.dll is a Windows Known DLL — always loaded from System32
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AttachConsole(int dwProcessId);
    private const int ATTACH_PARENT_PROCESS = -1;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Global Exception Handling
        AppDomain.CurrentDomain.UnhandledException += (s, args) =>
        {
            var ex = args.ExceptionObject as Exception;
            string errorMsg = ex?.Message ?? "Unknown fatal error";
            System.Diagnostics.Debug.WriteLine($"AppDomain Unhandled Exception: {errorMsg}\n{ex?.StackTrace}");
            
            // ModernMessageBox is a WPF window and requires STA thread.
            // Check if we can safely dispatch to the UI thread, otherwise fallback to standard MessageBox.Show.
            if (Application.Current != null && Application.Current.Dispatcher != null && !Application.Current.Dispatcher.HasShutdownStarted)
            {
                try
                {
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        ModernMessageBox.Show($"A critical error occurred and the application must close.\n\nError: {errorMsg}", "Fatal Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    });
                    return;
                }
                catch { }
            }
            
            MessageBox.Show($"A critical error occurred and the application must close.\n\nError: {errorMsg}", "Fatal Error", MessageBoxButton.OK, MessageBoxImage.Error);
        };

        DispatcherUnhandledException += (s, args) =>
        {
            System.Diagnostics.Debug.WriteLine($"UI Unhandled Exception: {args.Exception.Message}\n{args.Exception.StackTrace}");
            ModernMessageBox.Show($"An unexpected error occurred.\n\nError: {args.Exception.Message}", "Application Error", MessageBoxButton.OK, MessageBoxImage.Error);
            args.Handled = true; // Prevent app from closing if possible
        };

        TaskScheduler.UnobservedTaskException += (s, args) =>
        {
            System.Diagnostics.Debug.WriteLine($"Unobserved Task Exception: {args.Exception.Message}\n{args.Exception.StackTrace}");
            // Don't show message box for unobserved background task exceptions, just log them
            args.SetObserved();
        };

        bool silentMode = false;
        foreach (var arg in e.Args)
        {
            if (arg.Equals("--silent", StringComparison.OrdinalIgnoreCase))
            {
                silentMode = true;
            }
        }

        if (silentMode)
        {
            RunSilentModeAsync(e);
        }
        else
        {
            // Prevent WPF from initiating shutdown when TermsDialog closes
            this.ShutdownMode = ShutdownMode.OnExplicitShutdown;

            // Launch GUI
            var terms = new TermsDialog();
            if (terms.ShowDialog() == true)
            {
                var mainWindow = new MainWindow();
                this.MainWindow = mainWindow;
                this.ShutdownMode = ShutdownMode.OnLastWindowClose;
                mainWindow.Show();
            }
            else
            {
                Shutdown();
            }
        }
    }

    private void RunSilentModeAsync(StartupEventArgs e)
    {
        Task.Run(async () =>
        {
            try
            {
                bool isOutputRedirected = Console.IsOutputRedirected;
                StreamWriter? standardOutput = null;

                if (isOutputRedirected)
                {
                    // Re-initialize Console.Out to point to the redirected standard output handle
                    standardOutput = new StreamWriter(Console.OpenStandardOutput(), System.Text.Encoding.UTF8) { AutoFlush = true };
                    Console.SetOut(standardOutput);
                    Console.SetError(standardOutput);
                }
                else
                {
                    // Attach to the parent console to output stdout
                    if (AttachConsole(ATTACH_PARENT_PROCESS))
                    {
                        standardOutput = new StreamWriter(Console.OpenStandardOutput(), System.Text.Encoding.UTF8) { AutoFlush = true };
                        Console.SetOut(standardOutput);
                        Console.SetError(standardOutput);
                    }
                }

                try
                {
                    Console.WriteLine();
                    Console.WriteLine("Agilico MSP Toolkit - Silent Mode Started");

                    var engine = new NetworkEngine();
                    
                    // Hook logs to console
                    engine.OnLog += (msg, isErr) =>
                    {
                        Console.WriteLine($"[{(isErr ? "FAIL" : "INFO")}] {msg}");
                    };

                    engine.OnProgress += (test, status, details) =>
                    {
                        Console.WriteLine($"[PROGRESS] {test}: {details}");
                    };

                    bool success = await engine.RunDiagnosticsAsync();
                    
                    Console.WriteLine();
                    Console.WriteLine(success ? "Result: PASS" : "Result: FAIL");
                    Console.WriteLine("Exiting silent mode.");
                    
                    Environment.Exit(success ? 0 : 1);
                }
                finally
                {
                    standardOutput?.Dispose();
                }
            }
            catch (Exception ex)
            {
                try
                {
                    Console.Error.WriteLine($"Fatal error in silent mode: {ex}");
                }
                catch { }
                Environment.Exit(1);
            }
        });
    }
}

