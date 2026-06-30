using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;

namespace AgilicoConnectChecker;

public partial class App : Application
{
    // kernel32.dll is a Windows Known DLL — always loaded from System32
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AttachConsole(int dwProcessId);
    private const int ATTACH_PARENT_PROCESS = -1;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Global Exception Handling
        AppDomain.CurrentDomain.UnhandledException += (s, args) =>
        {
            var ex = args.ExceptionObject as Exception;
            System.Diagnostics.Debug.WriteLine($"AppDomain Unhandled Exception: {ex?.Message}\n{ex?.StackTrace}");
            MessageBox.Show($"A critical error occurred and the application must close.\n\nError: {ex?.Message}", "Fatal Error", MessageBoxButton.OK, MessageBoxImage.Error);
        };

        DispatcherUnhandledException += (s, args) =>
        {
            System.Diagnostics.Debug.WriteLine($"UI Unhandled Exception: {args.Exception.Message}\n{args.Exception.StackTrace}");
            MessageBox.Show($"An unexpected error occurred.\n\nError: {args.Exception.Message}", "Application Error", MessageBoxButton.OK, MessageBoxImage.Error);
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
            if (Console.IsOutputRedirected)
            {
                // Re-initialize Console.Out to point to the redirected standard output handle
                var standardOutput = new StreamWriter(Console.OpenStandardOutput(), System.Text.Encoding.UTF8) { AutoFlush = true };
                Console.SetOut(standardOutput);
                Console.SetError(standardOutput);
            }
            else
            {
                // Attach to the parent console to output stdout
                AttachConsole(ATTACH_PARENT_PROCESS);
                var standardOutput = new StreamWriter(Console.OpenStandardOutput(), System.Text.Encoding.UTF8) { AutoFlush = true };
                Console.SetOut(standardOutput);
                Console.SetError(standardOutput);
            }

            Console.WriteLine();
            Console.WriteLine("Agilico Network Diagnostic Tool - Silent Mode Started");

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
        else
        {
            // Launch GUI
            var mainWindow = new MainWindow();
            mainWindow.Show();
        }
    }
}

