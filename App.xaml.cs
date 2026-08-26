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
        // Enable Multicore JIT (ProfileOptimization) to accelerate startup
        try
        {
            string profileDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AgilicoToolkit", "JitProfiles");
            Directory.CreateDirectory(profileDir);
            System.Runtime.ProfileOptimization.SetProfileRoot(profileDir);
            System.Runtime.ProfileOptimization.StartProfile("Startup.profile");
        }
        catch { /* Ignore profile optimization failures */ }

        base.OnStartup(e);
        LogStartupMessage("OnStartup triggered.");

        // Global Exception Handling
        AppDomain.CurrentDomain.UnhandledException += (s, args) =>
        {
            var ex = args.ExceptionObject as Exception;
            string errorMsg = ex?.Message ?? "Unknown fatal error";
            System.Diagnostics.Debug.WriteLine($"AppDomain Unhandled Exception: {errorMsg}\n{ex?.StackTrace}");
            LogStartupError("AppDomain.UnhandledException", ex);
            
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
            LogStartupError("DispatcherUnhandledException", args.Exception);
            string errorMsg = args.Exception.Message;
            Exception inner = args.Exception.InnerException;
            while (inner != null)
            {
                errorMsg += "\nInner: " + inner.Message;
                if (inner is System.Windows.Markup.XamlParseException xamlEx)
                {
                    errorMsg += $"\nLine: {xamlEx.LineNumber}, Pos: {xamlEx.LinePosition}";
                }
                inner = inner.InnerException;
            }

            MessageBox.Show("Error: " + errorMsg, "Agilico MSP Toolkit", MessageBoxButton.OK, MessageBoxImage.Error);
            args.Handled = true;
            if (this.MainWindow == null || !this.MainWindow.IsVisible)
            {
                Shutdown(1);
            }
        };

        TaskScheduler.UnobservedTaskException += (s, args) =>
        {
            System.Diagnostics.Debug.WriteLine($"Unobserved Task Exception: {args.Exception.Message}\n{args.Exception.StackTrace}");
            LogStartupError("TaskScheduler.UnobservedTaskException", args.Exception);
            // Don't show message box for unobserved background task exceptions, just log them
            args.SetObserved();
        };

        bool silentMode = false;
        bool testXaml = false;
        foreach (var arg in e.Args)
        {
            if (arg.Equals("--silent", StringComparison.OrdinalIgnoreCase))
            {
                silentMode = true;
            }
            if (arg.Equals("--test-xaml", StringComparison.OrdinalIgnoreCase))
            {
                testXaml = true;
            }
        }

        LogStartupMessage($"Silent mode: {silentMode}, Test XAML mode: {testXaml}");

        if (testXaml)
        {
            try
            {
                AttachConsole(ATTACH_PARENT_PROCESS);
                Console.WriteLine("\n[TEST-XAML] Instantiating MainWindow...");
                var mainWindow = new MainWindow();
                Console.WriteLine("[TEST-XAML] MainWindow instantiated successfully!");
                Shutdown(0);
                return;
            }
            catch (Exception ex)
            {
                AttachConsole(ATTACH_PARENT_PROCESS);
                Console.WriteLine("\n[TEST-XAML] FAILED to instantiate MainWindow!");
                Console.WriteLine(ex.ToString());
                LogStartupError("TestXamlException", ex);
                Shutdown(1);
                return;
            }
        }

        if (silentMode)
        {
            RunSilentModeAsync(e);
        }
        else
        {
            // Prevent WPF from initiating shutdown while TermsDialog is showing
            this.ShutdownMode = ShutdownMode.OnExplicitShutdown;

            // Launch GUI
            LogStartupMessage("Instantiating TermsDialog.");
            var terms = new TermsDialog();
            LogStartupMessage("Showing TermsDialog.");
            bool? result = terms.ShowDialog();
            LogStartupMessage($"TermsDialog result: {result}");
            if (result == true)
            {
                try
                {
                    LogStartupMessage("Instantiating MainWindow.");
                    var mainWindow = new MainWindow();
                    this.MainWindow = mainWindow;
                    this.ShutdownMode = ShutdownMode.OnMainWindowClose;
                    LogStartupMessage("Showing MainWindow.");
                    mainWindow.Show();
                }
                catch (Exception ex)
                {
                    LogStartupError("MainWindowInstantiation", ex);
                    ModernMessageBox.Show($"Failed to initialize main window.\n\nError: {ex.Message}", "Initialization Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    Shutdown(1);
                }
            }
            else
            {
                LogStartupMessage("User declined terms or closed dialog. Shutting down.");
                Shutdown(0);
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

    private static readonly object _startupLogLock = new();

    private static void LogStartupError(string context, Exception? ex)
    {
        try
        {
            string appDataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AgilicoToolkit");
            Directory.CreateDirectory(appDataDir);
            string logFile = Path.Combine(appDataDir, "startup_log.txt");
            string msg = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] ERROR ({context}): {ex?.ToString()}{Environment.NewLine}{Environment.NewLine}";
            lock (_startupLogLock)
            {
                File.AppendAllText(logFile, msg);
            }
        }
        catch { }
    }

    private static void LogStartupMessage(string message)
    {
        try
        {
            string appDataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AgilicoToolkit");
            Directory.CreateDirectory(appDataDir);
            string logFile = Path.Combine(appDataDir, "startup_log.txt");
            string msg = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] INFO: {message}{Environment.NewLine}";
            lock (_startupLogLock)
            {
                File.AppendAllText(logFile, msg);
            }
        }
        catch { }
    }
}

