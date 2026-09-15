using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace AgilicoDiagMac.Core
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

                string? ffmpegPath = FindExecutable("ffmpeg");
                if (!string.IsNullOrEmpty(ffmpegPath))
                {
                    return await ConvertWithFFmpegAsync(inputPath, outputPath, ffmpegPath, progressCallback);
                }

                if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX) && File.Exists("/usr/bin/afconvert"))
                {
                    return await ConvertWithAfconvertAsync(inputPath, outputPath, progressCallback);
                }

                return (false, string.Empty, "FFmpeg was not found. Please install ffmpeg (e.g. 'brew install ffmpeg').");
            }
            catch (Exception ex)
            {
                Logger.Error($"Audio conversion failed: {ex.Message}", ex, "AudioConverter");
                return (false, string.Empty, ex.Message);
            }
        }

        private static async Task<(bool success, string outputPath, string message)> ConvertWithFFmpegAsync(
            string inputPath, string outputPath, string ffmpegPath, Action<double>? progressCallback)
        {
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

            using var process = new Process { StartInfo = psi };
            double totalDurationMs = 0;

            process.ErrorDataReceived += (s, e) =>
            {
                if (string.IsNullOrWhiteSpace(e.Data)) return;

                if (totalDurationMs <= 0)
                {
                    var match = DurationRegex.Match(e.Data);
                    if (match.Success)
                    {
                        int hours = int.Parse(match.Groups[1].Value);
                        int mins = int.Parse(match.Groups[2].Value);
                        int secs = int.Parse(match.Groups[3].Value);
                        int centis = int.Parse(match.Groups[4].Value);
                        totalDurationMs = ((hours * 3600) + (mins * 60) + secs) * 1000 + (centis * 10);
                    }
                }

                var progressMatch = ProgressRegex.Match(e.Data);
                if (progressMatch.Success && totalDurationMs > 0)
                {
                    if (long.TryParse(progressMatch.Groups[1].Value, out long currentMs))
                    {
                        double percent = Math.Clamp((double)currentMs / (totalDurationMs * 1000.0) * 100.0, 0, 99.0);
                        progressCallback?.Invoke(percent);
                    }
                }
            };

            process.Start();
            process.BeginErrorReadLine();
            await process.WaitForExitAsync();

            if (process.ExitCode == 0 && File.Exists(outputPath))
            {
                progressCallback?.Invoke(100.0);
                return (true, outputPath, "Audio converted successfully (PCM 16-bit 8000Hz Mono).");
            }

            return (false, string.Empty, $"Conversion failed with exit code {process.ExitCode}.");
        }

        private static async Task<(bool success, string outputPath, string message)> ConvertWithAfconvertAsync(
            string inputPath, string outputPath, Action<double>? progressCallback)
        {
            var psi = new ProcessStartInfo
            {
                FileName = "/usr/bin/afconvert",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardError = true,
                RedirectStandardOutput = true
            };
            // afconvert -f WAVE -c 1 -d LEI16@8000 input output
            psi.ArgumentList.Add("-f");
            psi.ArgumentList.Add("WAVE");
            psi.ArgumentList.Add("-c");
            psi.ArgumentList.Add("1");
            psi.ArgumentList.Add("-d");
            psi.ArgumentList.Add("LEI16@8000");
            psi.ArgumentList.Add(inputPath);
            psi.ArgumentList.Add(outputPath);

            progressCallback?.Invoke(30.0);
            using var process = new Process { StartInfo = psi };
            process.Start();
            await process.WaitForExitAsync();

            if (process.ExitCode == 0 && File.Exists(outputPath))
            {
                progressCallback?.Invoke(100.0);
                return (true, outputPath, "Audio converted successfully via macOS afconvert.");
            }

            return (false, string.Empty, "macOS afconvert failed to process audio file.");
        }

        private static string? FindExecutable(string name)
        {
            string[] commonPaths = new[]
            {
                Path.Combine(AppContext.BaseDirectory, name),
                Path.Combine(AppContext.BaseDirectory, name + ".exe"),
                $"/opt/homebrew/bin/{name}",
                $"/usr/local/bin/{name}",
                $"/usr/bin/{name}"
            };

            foreach (var path in commonPaths)
            {
                if (File.Exists(path)) return path;
            }

            var envPath = Environment.GetEnvironmentVariable("PATH");
            if (!string.IsNullOrEmpty(envPath))
            {
                foreach (var dir in envPath.Split(Path.PathSeparator))
                {
                    try
                    {
                        var candidate = Path.Combine(dir, name);
                        if (File.Exists(candidate)) return candidate;
                        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows) && File.Exists(candidate + ".exe")) return candidate + ".exe";
                    }
                    catch { }
                }
            }

            return null;
        }
    }
}
