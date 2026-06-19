using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.NetworkInformation;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace AgilicoConnectChecker
{
    public class PingResult
    {
        public DateTime Timestamp { get; set; }
        public string Target { get; set; } = string.Empty;
        public long? LatencyMs { get; set; } // null means timeout/loss
        public IPStatus Status { get; set; }

        public string LatencyDisplay => LatencyMs.HasValue ? $"{LatencyMs.Value} ms" : "Timeout";
    }

    public class PingStats
    {
        public long Current { get; set; }
        public long Min { get; set; }
        public long Max { get; set; }
        public double Average { get; set; }
        public double LossPercentage { get; set; }
        public double Jitter { get; set; }
    }

    public class PingTracker
    {
        private CancellationTokenSource? _cts;
        private readonly List<PingResult> _allResults = new List<PingResult>();
        private readonly List<PingResult> _recentResults = new List<PingResult>();
        private const int MaxRecentCount = 60;
        private readonly object _lock = new object();

        public event Action<PingResult, PingStats>? OnPingResult;

        public bool IsRunning => _cts != null;
        public string CurrentTarget { get; private set; } = string.Empty;
        public int CurrentIntervalMs { get; private set; } = 1000;

        public void Start(string target, int intervalMs)
        {
            Stop(); // Stop any existing tracker (outside lock to avoid deadlock)

            lock (_lock)
            {
                _cts = new CancellationTokenSource();
                CurrentTarget = target;
                CurrentIntervalMs = intervalMs;
                _allResults.Clear();
                _recentResults.Clear();

                var token = _cts.Token;
                Task.Run(() => RunPingLoopAsync(target, intervalMs, token), token);
            }
        }

        public void Stop()
        {
            CancellationTokenSource? oldCts;
            lock (_lock)
            {
                oldCts = _cts;
                _cts = null;
            }
            if (oldCts != null)
            {
                oldCts.Cancel();
                // Dispose after a delay to let the background loop exit
                Task.Delay(500).ContinueWith(_ => oldCts.Dispose());
            }
        }

        public List<PingResult> GetRecentResults()
        {
            lock (_lock)
            {
                return new List<PingResult>(_recentResults);
            }
        }

        public List<PingResult> GetAllResults()
        {
            lock (_lock)
            {
                return new List<PingResult>(_allResults);
            }
        }

        private async Task RunPingLoopAsync(string target, int intervalMs, CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                var startTime = DateTime.Now;
                PingResult result;

                try
                {
                    using var ping = new Ping();
                    // Ping target with timeout capped to the interval or 2000ms max
                    var timeout = Math.Min(intervalMs, 2000);
                    var reply = await ping.SendPingAsync(target, timeout);
                    result = new PingResult
                    {
                        Timestamp = DateTime.Now,
                        Target = target,
                        LatencyMs = reply.Status == IPStatus.Success ? reply.RoundtripTime : null,
                        Status = reply.Status
                    };
                }
                catch (Exception)
                {
                    result = new PingResult
                    {
                        Timestamp = DateTime.Now,
                        Target = target,
                        LatencyMs = null,
                        Status = IPStatus.Unknown
                    };
                }

                PingStats stats;
                lock (_lock)
                {
                    _allResults.Add(result);
                    _recentResults.Add(result);
                    if (_recentResults.Count > MaxRecentCount)
                    {
                        _recentResults.RemoveAt(0);
                    }

                    stats = CalculateStats();
                }

                if (!token.IsCancellationRequested)
                {
                    OnPingResult?.Invoke(result, stats);
                }

                // Calculate delay to maintain interval precisely
                var elapsed = (DateTime.Now - startTime).TotalMilliseconds;
                var delay = intervalMs - elapsed;
                if (delay > 0)
                {
                    try
                    {
                        await Task.Delay((int)delay, token);
                    }
                    catch (TaskCanceledException)
                    {
                        break;
                    }
                }
                else
                {
                    // If ping took longer than interval, pause briefly to prevent CPU hogging
                    try
                    {
                        await Task.Delay(50, token);
                    }
                    catch (TaskCanceledException)
                    {
                        break;
                    }
                }
            }
        }

        private PingStats CalculateStats()
        {
            var stats = new PingStats();
            var successfulPings = _allResults.Where(r => r.LatencyMs.HasValue).Select(r => r.LatencyMs!.Value).ToList();
            var totalPings = _allResults.Count;

            if (totalPings == 0) return stats;

            stats.LossPercentage = (double)_allResults.Count(r => !r.LatencyMs.HasValue) / totalPings * 100.0;

            if (successfulPings.Count > 0)
            {
                stats.Current = successfulPings.Last();
                stats.Min = successfulPings.Min();
                stats.Max = successfulPings.Max();
                stats.Average = successfulPings.Average();

                // Calculate Jitter (mean absolute deviation of consecutive latencies)
                if (successfulPings.Count > 1)
                {
                    double sumDiff = 0;
                    for (int i = 1; i < successfulPings.Count; i++)
                    {
                        sumDiff += Math.Abs(successfulPings[i] - successfulPings[i - 1]);
                    }
                    stats.Jitter = sumDiff / (successfulPings.Count - 1);
                }
                else
                {
                    stats.Jitter = 0;
                }
            }
            else
            {
                stats.Current = 0;
                stats.Min = 0;
                stats.Max = 0;
                stats.Average = 0;
                stats.Jitter = 0;
            }

            return stats;
        }

        public void ExportLog(string filePath)
        {
            var sb = new StringBuilder();
            sb.AppendLine("Timestamp,Target,LatencyMs,Status");
            lock (_lock)
            {
                foreach (var r in _allResults)
                {
                    var latencyStr = r.LatencyMs.HasValue ? r.LatencyMs.Value.ToString() : "TIMEOUT";
                    sb.AppendLine($"{r.Timestamp:yyyy-MM-dd HH:mm:ss},{CsvEscape(r.Target)},{latencyStr},{r.Status}");
                }
            }
            File.WriteAllText(filePath, sb.ToString());
        }

        private static string CsvEscape(string field)
        {
            if (string.IsNullOrEmpty(field)) return field;
            if (field.Contains(',') || field.Contains('"') || field.Contains('\n') || field.Contains('\r'))
            {
                return "\"" + field.Replace("\"", "\"\"") + "\"";
            }
            return field;
        }
    }
}
