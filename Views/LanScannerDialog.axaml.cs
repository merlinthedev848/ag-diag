using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using AgilicoDiagMac.Core;

namespace AgilicoDiagMac.Views
{
    public partial class LanScannerDialog : Window
    {
        private readonly ObservableCollection<LanDevice> _devices = new();
        private readonly LanScanner _scanner = new();
        private CancellationTokenSource? _cts;

        public LanScannerDialog()
        {
            InitializeComponent();
            GridDevices.ItemsSource = _devices;
        }

        private async void BtnStartScan_Click(object? sender, RoutedEventArgs e)
        {
            if (_cts != null)
            {
                _cts.Cancel();
                BtnStartScan.Content = "Start Subnet Scan";
                BtnStartScan.Background = Avalonia.Media.Brushes.MediumSeaGreen;
                _cts = null;
                TxtStatus.Text = "Scan cancelled.";
                return;
            }

            _cts = new CancellationTokenSource();
            BtnStartScan.Content = "Stop Scan";
            BtnStartScan.Background = Avalonia.Media.Brushes.IndianRed;
            _devices.Clear();
            PrgScan.Value = 0;
            TxtStatus.Text = "Scanning local subnet...";
            TxtCount.Text = "0 devices found";

            try
            {
                var token = _cts.Token;
                await _scanner.ScanNetworkAsync(
                    (completed, total) =>
                    {
                        Dispatcher.UIThread.Post(() =>
                        {
                            if (total > 0)
                            {
                                PrgScan.Value = ((double)completed / total) * 100.0;
                                TxtStatus.Text = $"Scanned {completed} / {total} hosts...";
                            }
                        });
                    },
                    device =>
                    {
                        Dispatcher.UIThread.Post(() =>
                        {
                            _devices.Add(device);
                            TxtCount.Text = $"{_devices.Count} devices found";
                        });
                    },
                    token);

                TxtStatus.Text = $"Scan finished. Found {_devices.Count} active devices.";
            }
            catch (OperationCanceledException)
            {
                TxtStatus.Text = "Scan cancelled.";
            }
            catch (Exception ex)
            {
                TxtStatus.Text = $"Error: {ex.Message}";
            }
            finally
            {
                BtnStartScan.Content = "Start Subnet Scan";
                BtnStartScan.Background = Avalonia.Media.Brushes.MediumSeaGreen;
                _cts = null;
            }
        }

        private async void BtnExportCsv_Click(object? sender, RoutedEventArgs e)
        {
            if (_devices.Count == 0)
            {
                await ModernMessageBox.ShowAsync(this, "No devices to export.", "Export Notice");
                return;
            }

            try
            {
                string desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
                string filePath = Path.Combine(desktop, $"LAN_Scan_{DateTime.Now:yyyyMMdd_HHmmss}.csv");
                var sb = new StringBuilder();
                sb.AppendLine("IP Address,MAC Address,Hostname,Manufacturer,Status");
                foreach (var d in _devices)
                {
                    sb.AppendLine($"\"{d.IpAddress}\",\"{d.MacAddress}\",\"{d.Hostname}\",\"{d.Manufacturer}\",\"{d.Status}\"");
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
            _cts?.Cancel();
            Close();
        }

        protected override void OnClosed(EventArgs e)
        {
            _cts?.Cancel();
            _scanner.Dispose();
            base.OnClosed(e);
        }
    }
}
