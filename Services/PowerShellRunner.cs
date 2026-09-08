using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace agilicomsptoolkit.Services
{
    public interface IPowerShellRunner
    {
        Task<(int exitCode, string stdout, string stderr)> ExecuteScriptAsync(string scriptContent, TimeSpan? timeout = null, CancellationToken cancellationToken = default);
        Task<(int exitCode, string stdout, string stderr)> ExecuteCommandAsync(string commandLine, TimeSpan? timeout = null, CancellationToken cancellationToken = default);
    }

    public sealed class PowerShellRunner : IPowerShellRunner
    {
        private static readonly string SecureTempDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "AgilicoToolkit", "Temp");

        static PowerShellRunner()
        {
            try
            {
                if (!Directory.Exists(SecureTempDir))
                {
                    Directory.CreateDirectory(SecureTempDir);
                }
            }
            catch { /* Best effort */ }
        }

        public async Task<(int exitCode, string stdout, string stderr)> ExecuteCommandAsync(string commandLine, TimeSpan? timeout = null, CancellationToken cancellationToken = default)
        {
            var effectiveTimeout = timeout ?? TimeSpan.FromSeconds(60);
            using var timeoutCts = new CancellationTokenSource(effectiveTimeout);
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

            var psi = new ProcessStartInfo("powershell.exe", $"-NoProfile -ExecutionPolicy Bypass -Command \"{commandLine.Replace("\"", "\\\"")}\"")
            {
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            using var process = new Process { StartInfo = psi };
            try
            {
                process.Start();
                var stdoutTask = process.StandardOutput.ReadToEndAsync(linkedCts.Token);
                var stderrTask = process.StandardError.ReadToEndAsync(linkedCts.Token);

                await process.WaitForExitAsync(linkedCts.Token).ConfigureAwait(false);
                string stdout = await stdoutTask.ConfigureAwait(false);
                string stderr = await stderrTask.ConfigureAwait(false);

                return (process.ExitCode, stdout, stderr);
            }
            catch (OperationCanceledException)
            {
                try { if (!process.HasExited) process.Kill(true); } catch { }
                return (-1, string.Empty, "Process timed out or was cancelled.");
            }
            catch (Exception ex)
            {
                return (-1, string.Empty, ex.Message);
            }
        }

        public async Task<(int exitCode, string stdout, string stderr)> ExecuteScriptAsync(string scriptContent, TimeSpan? timeout = null, CancellationToken cancellationToken = default)
        {
            string scriptPath = Path.Combine(SecureTempDir, $"script_{Guid.NewGuid():N}.ps1");
            try
            {
                await File.WriteAllTextAsync(scriptPath, scriptContent, cancellationToken).ConfigureAwait(false);
                var effectiveTimeout = timeout ?? TimeSpan.FromSeconds(90);
                using var timeoutCts = new CancellationTokenSource(effectiveTimeout);
                using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

                var psi = new ProcessStartInfo("powershell.exe", $"-NoProfile -ExecutionPolicy Bypass -File \"{scriptPath}\"")
                {
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };

                using var process = new Process { StartInfo = psi };
                process.Start();
                var stdoutTask = process.StandardOutput.ReadToEndAsync(linkedCts.Token);
                var stderrTask = process.StandardError.ReadToEndAsync(linkedCts.Token);

                await process.WaitForExitAsync(linkedCts.Token).ConfigureAwait(false);
                string stdout = await stdoutTask.ConfigureAwait(false);
                string stderr = await stderrTask.ConfigureAwait(false);

                return (process.ExitCode, stdout, stderr);
            }
            catch (OperationCanceledException)
            {
                return (-1, string.Empty, "Script execution timed out or was cancelled.");
            }
            catch (Exception ex)
            {
                return (-1, string.Empty, ex.Message);
            }
            finally
            {
                try
                {
                    if (File.Exists(scriptPath)) File.Delete(scriptPath);
                }
                catch { }
            }
        }
    }
}
