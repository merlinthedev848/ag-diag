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
        private static readonly Regex DurationRegex = new Regex(@"Duration:\s*(\d{2}):(\d{2}):(\d{2})\.(\d{2})", RegexOptions.Compiled);
        private static readonly Regex TimeRegex = new Regex(@"time=\s*(\d{2}):(\d{2}):(\d{2})\.(\d{2})", RegexOptions.Compiled);

        public static async Task<(bool success, string outputPath, string message)> ConvertAsync(string inputPath, Action<double> progressCallback)
        {
            try
            {
                if (!File.Exists(inputPath))
                {
                    return (false, string.Empty, "Input file does not exist.");
                }

                string desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
                string baseName = Path.GetFileNameWithoutExtension(inputPath);
                string outputPath = Path.Combine(desktopPath, $"{baseName}-AG.wav");

                string ffmpegPath = GetFFmpegPath();

                // FFmpeg arguments for telephony conversion:
                // -ac 1 (Mono), -ar 8000 (8kHz), -acodec pcm_s16le (16-bit PCM), -y (overwrite), 
                // -map_metadata -1 (strip all tags), -fflags +bitexact (remove software metadata headers)
                string arguments = $"-i \"{inputPath}\" -ac 1 -ar 8000 -acodec pcm_s16le -y -map_metadata -1 -fflags +bitexact \"{outputPath}\"";

                var startInfo = new ProcessStartInfo
                {
                    FileName = ffmpegPath,
                    Arguments = arguments,
                    WorkingDirectory = Path.GetDirectoryName(ffmpegPath) ?? AppDomain.CurrentDomain.BaseDirectory,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardError = true
                };

                using var process = new Process { StartInfo = startInfo };
                
                double totalSeconds = 0;

                process.ErrorDataReceived += (sender, e) =>
                {
                    if (string.IsNullOrEmpty(e.Data)) return;

                    // Parse Duration: 00:01:23.45 to find total length
                    var durationMatch = DurationRegex.Match(e.Data);
                    if (durationMatch.Success)
                    {
                        int hours = int.Parse(durationMatch.Groups[1].Value);
                        int minutes = int.Parse(durationMatch.Groups[2].Value);
                        int seconds = int.Parse(durationMatch.Groups[3].Value);
                        totalSeconds = (hours * 3600) + (minutes * 60) + seconds;
                    }

                    // Parse time=00:00:12.34 to calculate progress percentage
                    if (totalSeconds > 0)
                    {
                        var timeMatch = TimeRegex.Match(e.Data);
                        if (timeMatch.Success)
                        {
                            int hours = int.Parse(timeMatch.Groups[1].Value);
                            int minutes = int.Parse(timeMatch.Groups[2].Value);
                            int seconds = int.Parse(timeMatch.Groups[3].Value);
                            double currentSeconds = (hours * 3600) + (minutes * 60) + seconds;

                            double percentage = (currentSeconds / totalSeconds) * 100.0;
                            if (percentage > 100) percentage = 100;
                            progressCallback?.Invoke(percentage);
                        }
                    }
                };

                process.Start();
                process.BeginErrorReadLine();

                await process.WaitForExitAsync();

                if (process.ExitCode == 0 && File.Exists(outputPath))
                {
                    return (true, outputPath, "Conversion completed successfully.");
                }
                else
                {
                    return (false, string.Empty, $"Conversion failed (FFmpeg exited with code {process.ExitCode}).");
                }
            }
            catch (Exception ex)
            {
                return (false, string.Empty, $"Error: {ex.Message}");
            }
        }

        private static string GetFFmpegPath()
        {
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            
            // 1. Check in application base directory
            string path1 = Path.Combine(baseDir, "ffmpeg.exe");
            if (File.Exists(path1)) return path1;

            // 2. Check in current working directory
            string path2 = Path.Combine(Directory.GetCurrentDirectory(), "ffmpeg.exe");
            if (File.Exists(path2)) return path2;

            // 3. Check AppData local folder (%LocalAppData%\AgilicoToolkit\ffmpeg.exe)
            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string appDataDir = Path.Combine(localAppData, "AgilicoToolkit");
            string appDataPath = Path.Combine(appDataDir, "ffmpeg.exe");

            if (File.Exists(appDataPath)) return appDataPath;

            // 4. Try extracting from Embedded Assembly Resource if missing
            try
            {
                var assembly = typeof(AudioConverter).Assembly;
                string[] manifestNames = assembly.GetManifestResourceNames();
                string resourceName = manifestNames
                    .FirstOrDefault(n => n.EndsWith("ffmpeg.exe", StringComparison.OrdinalIgnoreCase)) ?? "";

                if (!string.IsNullOrEmpty(resourceName))
                {
                    using Stream? stream = assembly.GetManifestResourceStream(resourceName);
                    if (stream != null)
                    {
                        Directory.CreateDirectory(appDataDir);
                        
                        // Extract if missing or size differs
                        if (!File.Exists(appDataPath) || new FileInfo(appDataPath).Length != stream.Length)
                        {
                            using FileStream fs = new FileStream(appDataPath, FileMode.Create, FileAccess.Write, FileShare.None);
                            stream.CopyTo(fs);
                        }
                        
                        if (File.Exists(appDataPath)) return appDataPath;
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Embedded FFmpeg extraction error: {ex.Message}");
            }

            if (File.Exists(appDataPath)) return appDataPath;

            // 5. Check in parent/source directories
            try
            {
                string path3 = Path.GetFullPath(Path.Combine(baseDir, "..", "ffmpeg.exe"));
                if (File.Exists(path3)) return path3;

                string path4 = Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "ffmpeg.exe"));
                if (File.Exists(path4)) return path4;
            }
            catch { }

            // 6. Check System path fallback
            string systemPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "ffmpeg.exe");
            if (File.Exists(systemPath)) return systemPath;

            throw new FileNotFoundException(
                $"ffmpeg.exe not found at '{path1}' or '{appDataPath}'. Please ensure ffmpeg.exe is present in the application folder.");
        }
    }
}
