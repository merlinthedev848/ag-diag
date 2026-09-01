using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace agilicomsptoolkit
{
    public static class AudioConverter
    {
        private static readonly Regex DurationRegex = new(@"Duration:\s*(\d{2}):(\d{2}):(\d{2})\.(\d{2})", RegexOptions.Compiled | RegexOptions.CultureInvariant);
        private static readonly Regex ProgressRegex = new(@"out_time_ms=(\d+)", RegexOptions.Compiled | RegexOptions.CultureInvariant);

        public static async Task<(bool success, string outputPath, string message)> ConvertAsync(
            string inputPath, Action<double>? progressCallback)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(inputPath))
                    return (false, string.Empty, "No input file was supplied.");
                if (!File.Exists(inputPath))
                    return (false, string.Empty, "Input file does not exist.");

                string desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
                if (string.IsNullOrWhiteSpace(desktopPath)) desktopPath = Path.GetTempPath();

                string baseName = Path.GetFileNameWithoutExtension(inputPath);
                string outputPath = Path.Combine(desktopPath, $"{baseName}-AG.wav");
                if (Path.GetFullPath(inputPath).Equals(Path.GetFullPath(outputPath), StringComparison.OrdinalIgnoreCase))
                    return (false, string.Empty, "Input and output paths must be different.");

                string ffmpegPath = GetFFmpegPath();
                var psi = new ProcessStartInfo
                {
                    FileName = ffmpegPath,
                    WorkingDirectory = Path.GetDirectoryName(ffmpegPath) ?? AppContext.BaseDirectory,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardError = true,
                    RedirectStandardOutput = true
                };
                psi.ArgumentList.Add("-hide_banner");
                psi.ArgumentList.Add("-nostdin");
                psi.ArgumentList.Add("-i");
                psi.ArgumentList.Add(inputPath);
                psi.ArgumentList.Add("-ac");
                psi.ArgumentList.Add("1");
                psi.ArgumentList.Add("-ar");
                psi.ArgumentList.Add("8000");
                psi.ArgumentList.Add("-acodec");
                psi.ArgumentList.Add("pcm_s16le");
                psi.ArgumentList.Add("-map_metadata");
                psi.ArgumentList.Add("-1");
                psi.ArgumentList.Add("-fflags");
                psi.ArgumentList.Add("+bitexact");
                psi.ArgumentList.Add("-y");
                psi.ArgumentList.Add("-progress");
                psi.ArgumentList.Add("pipe:2");
                psi.ArgumentList.Add(outputPath);

                using var process = new Process { StartInfo = psi, EnableRaisingEvents = true };
                double totalSeconds = 0;
                string stderr = string.Empty;
                process.ErrorDataReceived += (_, e) =>
                {
                    if (string.IsNullOrEmpty(e.Data)) return;
                    var duration = DurationRegex.Match(e.Data);
                    if (duration.Success)
                    {
                        totalSeconds = int.Parse(duration.Groups[1].Value) * 3600 +
                                       int.Parse(duration.Groups[2].Value) * 60 +
                                       int.Parse(duration.Groups[3].Value) +
                                       int.Parse(duration.Groups[4].Value) / 100.0;
                    }
                    var progress = ProgressRegex.Match(e.Data);
                    if (progress.Success && totalSeconds > 0 && long.TryParse(progress.Groups[1].Value, out long ms))
                    {
                        double percent = Math.Clamp((ms / 1000.0) / totalSeconds * 100.0, 0, 100);
                        progressCallback?.Invoke(percent);
                    }
                    stderr += e.Data + Environment.NewLine;
                };

                if (!process.Start()) return (false, string.Empty, "Failed to start FFmpeg.");
                process.BeginErrorReadLine();
                await process.WaitForExitAsync().ConfigureAwait(false);

                if (process.ExitCode == 0 && File.Exists(outputPath))
                {
                    progressCallback?.Invoke(100);
                    return (true, outputPath, "Conversion completed successfully.");
                }

                try { if (File.Exists(outputPath)) File.Delete(outputPath); } catch { }
                string detail = stderr.Length > 1000 ? stderr[^1000..] : stderr;
                return (false, string.Empty, $"Conversion failed (FFmpeg exited with code {process.ExitCode}). {detail.Trim()}");
            }
            catch (Exception ex)
            {
                return (false, string.Empty, $"Error: {ex.Message}");
            }
        }

        private static string GetFFmpegPath()
        {
            string baseDir = AppContext.BaseDirectory;
            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string appDataDir = Path.Combine(localAppData, "AgilicoToolkit");
            string[] candidates =
            {
                Path.Combine(baseDir, "ffmpeg.exe"),
                Path.Combine(appDataDir, "ffmpeg.exe")
            };

            foreach (string path in candidates)
            {
                if (File.Exists(path)) return path;
            }

            // Embedded FFmpeg is used only for the full build. Extract atomically so a failed
            // write can never leave a partially written executable that gets launched later.
            try
            {
                var assembly = typeof(AudioConverter).Assembly;
                string? resourceName = assembly.GetManifestResourceNames()
                    .FirstOrDefault(n => n.EndsWith("ffmpeg.exe", StringComparison.OrdinalIgnoreCase));
                using Stream? stream = resourceName == null ? null : assembly.GetManifestResourceStream(resourceName);
                if (stream != null)
                {
                    Directory.CreateDirectory(appDataDir);
                    string tempPath = Path.Combine(appDataDir, $"ffmpeg.{Guid.NewGuid():N}.tmp");
                    try
                    {
                        using (var fs = new FileStream(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                            stream.CopyTo(fs);
                        File.Move(tempPath, candidates[1], true);
                        return candidates[1];
                    }
                    finally
                    {
                        try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch { }
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Warning($"FFmpeg extraction failed: {ex.Message}", "AudioConverter");
            }

            throw new FileNotFoundException("ffmpeg.exe was not found in the application installation.");
        }
    }
}
