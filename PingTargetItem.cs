using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows.Media;

namespace agilicomsptoolkit
{
    public class PingTargetItem : INotifyPropertyChanged
    {
        public PingTracker Tracker { get; }
        
        // Status history timeline: last N ping results (true = success, false = fail)
        private const int MaxHistoryLength = 20;
        private readonly List<bool> _statusHistoryList = new List<bool>();
        
        public PingTargetItem(string target, int intervalMs)
        {
            Target = target;
            IntervalMs = intervalMs;
            Tracker = new PingTracker();
            Tracker.OnPingResult += Tracker_OnPingResult;
        }

        private static readonly Brush GreenBrush = CreateFrozenBrush(34, 197, 94);
        private static readonly Brush RedBrush = CreateFrozenBrush(239, 68, 68);
        private static readonly Brush YellowBrush = CreateFrozenBrush(234, 179, 8);
        private static readonly Brush GrayBrush = CreateFrozenBrush(156, 163, 175);
        private static readonly Brush DarkGreenBrush = CreateFrozenBrush(22, 163, 74);
        private static readonly Brush AmberBrush = CreateFrozenBrush(217, 119, 6);
        private static readonly Brush DarkRedBrush = CreateFrozenBrush(220, 38, 38);

        private static Brush CreateFrozenBrush(byte r, byte g, byte b)
        {
            var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
            brush.Freeze();
            return brush;
        }

        private void Tracker_OnPingResult(PingResult result, PingStats stats)
        {
            System.Windows.Application.Current?.Dispatcher.InvokeAsync(() =>
            {
                if (!Tracker.IsRunning) return;
                bool success = result.Status == System.Net.NetworkInformation.IPStatus.Success;
                
                // Calculate status from stats/result
                if (success)
                {
                    Status = "Online";
                    StatusColor = GreenBrush;
                }
                else
                {
                    Status = "Offline/Timeout";
                    StatusColor = RedBrush;
                }

                CurrentLatency = $"{stats.Current} ms";
                AvgLatency = $"{Math.Round(stats.Average, 1)} ms";
                MinMaxLatency = stats.Min == long.MaxValue || stats.Max == long.MinValue || (stats.Min == 0 && stats.Max == 0) ? "- / - ms" : $"{stats.Min} / {stats.Max} ms";
                PacketLoss = $"{Math.Round(stats.LossPercentage, 1)}%";
                Jitter = $"{Math.Round(stats.Jitter, 1)} ms";
                
                // Color-code the current latency
                if (success && stats.Current >= 0)
                {
                    if (stats.Current < 50)
                        LatencyColor = DarkGreenBrush;
                    else if (stats.Current < 150)
                        LatencyColor = AmberBrush;
                    else
                        LatencyColor = DarkRedBrush;
                }
                else
                {
                    LatencyColor = DarkRedBrush;
                }
                
                // Update uptime percentage
                double uptime = 100.0 - stats.LossPercentage;
                UptimePercent = $"{Math.Round(uptime, 1)}%";
                
                // Color-code uptime
                if (uptime >= 99.0) UptimeColor = DarkGreenBrush;
                else if (uptime >= 90.0) UptimeColor = AmberBrush;
                else UptimeColor = DarkRedBrush;
                
                // Update status history timeline (last N results)
                _statusHistoryList.Add(success);
                if (_statusHistoryList.Count > MaxHistoryLength)
                    _statusHistoryList.RemoveAt(0);
                // Create a snapshot copy for UI binding
                StatusHistory = new List<bool>(_statusHistoryList);
                
                // Notify UI to update the graph if it's selected (handled externally)
                OnPingResultReceived?.Invoke(this, new PingResultEventArgs(result, stats));
            });
        }
        
        public event EventHandler<PingResultEventArgs>? OnPingResultReceived;
 
        public void Start()
        {
            if (!Tracker.IsRunning)
            {
                Tracker.Start(Target, IntervalMs);
                Status = "Running...";
                StatusColor = YellowBrush;
            }
        }
 
        public void Stop()
        {
            if (Tracker.IsRunning)
            {
                Tracker.Stop();
                Status = "Stopped";
                StatusColor = GrayBrush;
            }
        }

        private string _target = string.Empty;
        public string Target
        {
            get => _target;
            set { _target = value; OnPropertyChanged(); }
        }

        private int _intervalMs = 1000;
        public int IntervalMs
        {
            get => _intervalMs;
            set { _intervalMs = value; OnPropertyChanged(); }
        }

        private string _status = "Stopped";
        public string Status
        {
            get => _status;
            set { _status = value; OnPropertyChanged(); }
        }

        private Brush _statusColor = GrayBrush;
        public Brush StatusColor
        {
            get => _statusColor;
            set { _statusColor = value; OnPropertyChanged(); }
        }

        private string _currentLatency = "-";
        public string CurrentLatency
        {
            get => _currentLatency;
            set { _currentLatency = value; OnPropertyChanged(); }
        }

        private string _avgLatency = "-";
        public string AvgLatency
        {
            get => _avgLatency;
            set { _avgLatency = value; OnPropertyChanged(); }
        }

        private string _minMaxLatency = "-";
        public string MinMaxLatency
        {
            get => _minMaxLatency;
            set { _minMaxLatency = value; OnPropertyChanged(); }
        }

        private string _packetLoss = "-";
        public string PacketLoss
        {
            get => _packetLoss;
            set { _packetLoss = value; OnPropertyChanged(); }
        }

        private string _jitter = "-";
        public string Jitter
        {
            get => _jitter;
            set { _jitter = value; OnPropertyChanged(); }
        }

        private Brush _latencyColor = GrayBrush;
        public Brush LatencyColor
        {
            get => _latencyColor;
            set { _latencyColor = value; OnPropertyChanged(); }
        }

        private string _uptimePercent = "-";
        public string UptimePercent
        {
            get => _uptimePercent;
            set { _uptimePercent = value; OnPropertyChanged(); }
        }

        private Brush _uptimeColor = GrayBrush;
        public Brush UptimeColor
        {
            get => _uptimeColor;
            set { _uptimeColor = value; OnPropertyChanged(); }
        }

        private List<bool> _statusHistory = new List<bool>();
        public List<bool> StatusHistory
        {
            get => _statusHistory;
            set { _statusHistory = value; OnPropertyChanged(); }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string? name = null)
        {
            if (System.Windows.Application.Current != null && System.Windows.Application.Current.Dispatcher != null && !System.Windows.Application.Current.Dispatcher.CheckAccess())
            {
                System.Windows.Application.Current.Dispatcher.BeginInvoke(new Action(() =>
                {
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
                }));
            }
            else
            {
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
            }
        }
    }
    
    public class PingResultEventArgs : EventArgs
    {
        public PingResult Result { get; }
        public PingStats Stats { get; }

        public PingResultEventArgs(PingResult result, PingStats stats)
        {
            Result = result;
            Stats = stats;
        }
    }
}
