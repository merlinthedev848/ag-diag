using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Text;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using AgilicoDiagMac.Core;

namespace AgilicoDiagMac.Views
{
    public partial class PingTrackerDialog : Window
    {
        private readonly ObservableCollection<PingResult> _pings = new();
        private readonly PingTracker _tracker = new();

        public PingTrackerDialog()
        {
            InitializeComponent();
            GridPings.ItemsSource = _pings;
            _tracker.OnPingResult += (res, stats) =>
            {
                Dispatcher.UIThread.Post(() =>
                {
                    _pings.Insert(0, res);
                    if (_pings.Count > 300) _pings.RemoveAt(_pings.Count - 1);

                    TxtCurrentRtt.Text = $"Current: {stats.Current} ms";
                    TxtAvgRtt.Text = $"Avg: {stats.Average:F1} ms";
                    TxtJitter.Text = $"Jitter: {stats.Jitter:F1} ms";
                    TxtLoss.Text = $"Loss: {stats.LossPercentage:F1}%";
                    TxtTotalPings.Text = $"{_pings.Count} packets recorded";
                });
            };
        }

        private void BtnTogglePing_Click(object? sender, RoutedEventArgs e)
        {
            if (_tracker.IsRunning)
            {
                _tracker.Stop();
                BtnTogglePing.Content = "Start Ping";
                BtnTogglePing.Background = Avalonia.Media.Brushes.RoyalBlue;
                TxtFooter.Text = "Ping paused.";
            }
            else
            {
                string target = TxtTarget.Text?.Trim() ?? "";
                if (string.IsNullOrEmpty(target)) target = "8.8.8.8";
                _tracker.Start(target, 1000);
                BtnTogglePing.Content = "Stop Ping";
                BtnTogglePing.Background = Avalonia.Media.Brushes.IndianRed;
                TxtFooter.Text = $"Continuously pinging {target}...";
            }
        }

        private void BtnClear_Click(object? sender, RoutedEventArgs e)
        {
            _pings.Clear();
            TxtTotalPings.Text = "0 packets recorded";
        }

        private async void BtnExportCsv_Click(object? sender, RoutedEventArgs e)
        {
            if (_pings.Count == 0)
            {
                await ModernMessageBox.ShowAsync(this, "No ping data to export.", "Export Notice");
                return;
            }

            try
            {
                string desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
                string filePath = Path.Combine(desktop, $"PingLog_{DateTime.Now:yyyyMMdd_HHmmss}.csv");
                var sb = new StringBuilder();
                sb.AppendLine("TimestampUtc,Target,LatencyMs,Status");
                foreach (var p in _pings)
                {
                    sb.AppendLine($"\"{p.Timestamp:O}\",\"{p.Target}\",\"{p.LatencyMs}\",\"{p.Status}\"");
                }
                await File.WriteAllTextAsync(filePath, sb.ToString());
                await ModernMessageBox.ShowAsync(this, $"Exported successfully to:\n{filePath}", "Export Complete");
            }
            catch (Exception ex)
            {
                await ModernMessageBox.ShowAsync(this, $"Failed to export: {ex.Message}", "Error");
            }
        }

        private void BtnClose_Click(object? sender, RoutedEventArgs e)
        {
            _tracker.Stop();
            Close();
        }

        protected override void OnClosed(EventArgs e)
        {
            _tracker.Stop();
            base.OnClosed(e);
        }
    }
}
