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

        // Running statistics to avoid O(N) calculations per ping
        private long _minLatency = long.MaxValue;
        private long _maxLatency = long.MinValue;
        private double _sumLatencies = 0;
        private int _successfulPingsCount = 0;
        private int _totalPings = 0;
        private int _failedPings = 0;
        private double _sumJitterDiff = 0;
        private long? _lastSuccessfulLatency = null;

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

                // Reset running stats
                _minLatency = long.MaxValue;
                _maxLatency = long.MinValue;
                _sumLatencies = 0;
                _successfulPingsCount = 0;
                _totalPings = 0;
                _failedPings = 0;
                _sumJitterDiff = 0;
                _lastSuccessfulLatency = null;

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

                    // Update running statistics
                    _totalPings++;
                    if (result.LatencyMs.HasValue)
                    {
                        long val = result.LatencyMs.Value;
                        _successfulPingsCount++;
                        _sumLatencies += val;

                        if (val < _minLatency) _minLatency = val;
                        if (val > _maxLatency) _maxLatency = val;

                        if (_lastSuccessfulLatency.HasValue)
                        {
                            _sumJitterDiff += Math.Abs(val - _lastSuccessfulLatency.Value);
                        }
                        _lastSuccessfulLatency = val;
                    }
                    else
                    {
                        _failedPings++;
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
            if (_totalPings == 0) return stats;

            stats.LossPercentage = (double)_failedPings / _totalPings * 100.0;

            if (_successfulPingsCount > 0)
            {
                stats.Current = _lastSuccessfulLatency ?? 0;
                stats.Min = _minLatency;
                stats.Max = _maxLatency;
                stats.Average = _sumLatencies / _successfulPingsCount;

                if (_successfulPingsCount > 1)
                {
                    stats.Jitter = _sumJitterDiff / (_successfulPingsCount - 1);
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
