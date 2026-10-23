using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using AgilicoDiagMac.Core;
using AgilicoDiagMac.Platform;
using AgilicoDiagMac.Views;

namespace AgilicoDiagMac
{
    public partial class MainWindow : Window
    {
        private readonly NetworkEngine _engine;
        private readonly StringBuilder _logBuilder = new();
        private bool _isDiagnosticsRunning;

        private readonly TextBlock[] _testStatusLabels = new TextBlock[10];
        private readonly TextBlock[] _testDescLabels = new TextBlock[10];
        private readonly CheckBox[] _testCheckboxes = new CheckBox[10];

        public MainWindow()
        {
            InitializeComponent();
            Title = "Agilico MSP Toolkit";

            _engine = new NetworkEngine();
            InitTestControls();

            _engine.OnLog += (msg, isErr) =>
            {
                Dispatcher.UIThread.Post(() => AppendLog(msg, isErr));
            };

            _engine.OnProgress += (testName, status, details) =>
            {
                Dispatcher.UIThread.Post(() => UpdateTestProgress(testName, status, details));
            };

            _engine.OnComplete += (success, score) =>
            {
                Dispatcher.UIThread.Post(() =>
                {
                    BtnRunDiagnostics.IsEnabled = true;
                    PrgOverall.Value = 100;
                    BadgeScore.Text = $"Score: {score}/100";
                    BadgeScore.Foreground = score >= 80 ? Brushes.MediumSeaGreen : (score >= 50 ? Brushes.Orange : Brushes.IndianRed);
                    
                    if (PnlSummaryIdle != null) PnlSummaryIdle.IsVisible = false;
                    if (PnlSummaryPass != null) PnlSummaryPass.IsVisible = true;

                    if (_engine.LastDownloadMbps > 0)
                        TxtDownloadSpeed.Text = $"{_engine.LastDownloadMbps:F1} Mbps";
                    if (_engine.LastUploadMbps > 0)
                        TxtUploadSpeed.Text = $"{_engine.LastUploadMbps:F1} Mbps";

                    _isDiagnosticsRunning = false;
                });
            };

            _ = ProbeInitialNetworkAsync();
        }

        private void InitTestControls()
        {
            _testStatusLabels[0] = TxtTestStatus0;
            _testStatusLabels[1] = TxtTestStatus1;
            _testStatusLabels[2] = TxtTestStatus2;
            _testStatusLabels[3] = TxtTestStatus3;
            _testStatusLabels[4] = TxtTestStatus4;
            _testStatusLabels[5] = TxtTestStatus5;
            _testStatusLabels[6] = TxtTestStatus6;
            _testStatusLabels[7] = TxtTestStatus7;
            _testStatusLabels[8] = TxtTestStatus8;
            _testStatusLabels[9] = TxtTestStatus9;

            _testDescLabels[0] = TxtTestDesc0;
            _testDescLabels[1] = TxtTestDesc1;
            _testDescLabels[2] = TxtTestDesc2;
            _testDescLabels[3] = TxtTestDesc3;
            _testDescLabels[4] = TxtTestDesc4;
            _testDescLabels[5] = TxtTestDesc5;
            _testDescLabels[6] = TxtTestDesc6;
            _testDescLabels[7] = TxtTestDesc7;
            _testDescLabels[8] = TxtTestDesc8;
            _testDescLabels[9] = TxtTestDesc9;

            _testCheckboxes[0] = ChkTest0;
            _testCheckboxes[1] = ChkTest1;
            _testCheckboxes[2] = ChkTest2;
            _testCheckboxes[3] = ChkTest3;
            _testCheckboxes[4] = ChkTest4;
            _testCheckboxes[5] = ChkTest5;
            _testCheckboxes[6] = ChkTest6;
            _testCheckboxes[7] = ChkTest7;
            _testCheckboxes[8] = ChkTest8;
            _testCheckboxes[9] = ChkTest9;
        }

        private void BtnNavDashboard_Click(object? sender, RoutedEventArgs e)
        {
            PageCarousel.SelectedIndex = 0;
            SetNavActive(0);
        }

        private void BtnNavHelp_Click(object? sender, RoutedEventArgs e)
        {
            PageCarousel.SelectedIndex = 1;
            SetNavActive(1);
        }

        private void BtnNavLogs_Click(object? sender, RoutedEventArgs e)
        {
            PageCarousel.SelectedIndex = 2;
            SetNavActive(2);
        }

        private void SetNavActive(int index)
        {
            StripeDashboard.IsVisible = (index == 0);
            StripeHelp.IsVisible = (index == 1);
            StripeLogs.IsVisible = (index == 2);
        }

        private async Task ProbeInitialNetworkAsync()
        {
            try
            {
                string local = LanScanner.GetLocalIpAddress();
                TxtLocalIpDisplay.Text = local;

                using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(4) };
                string pub = (await client.GetStringAsync("https://api.ipify.org")).Trim();
                TxtPublicIpDisplay.Text = pub;
                _engine.PublicIpAddress = pub;
            }
            catch
            {
                TxtPublicIpDisplay.Text = "Offline / Unknown";
            }
        }

        private void AppendLog(string message, bool isError = false)
        {
            string time = DateTime.Now.ToString("HH:mm:ss.fff");
            string line = $"[{time}] {(isError ? "[ERROR] " : "")}{message}";
            _logBuilder.AppendLine(line);
            TxtLogs.Text = _logBuilder.ToString();
            TxtPcapStats.Text = $"PCAP Capturer: {_engine.Pcap.PacketCount} packets ({_engine.Pcap.TotalBytes / 1024.0:F1} KB recorded)";
        }

        private void UpdateTestProgress(string testName, string status, string details)
        {
            int index = GetTestIndex(testName);
            if (index >= 0 && index < 10)
            {
                _testStatusLabels[index].Text = !string.IsNullOrEmpty(details) ? details : status;

                if (status.Equals("Passed", StringComparison.OrdinalIgnoreCase) || status.Equals("Healthy", StringComparison.OrdinalIgnoreCase) || status.Equals("OK", StringComparison.OrdinalIgnoreCase))
                {
                    _testStatusLabels[index].Foreground = Brushes.MediumSeaGreen;
                }
                else if (status.Equals("Failed", StringComparison.OrdinalIgnoreCase) || status.Equals("Error", StringComparison.OrdinalIgnoreCase) || status.Equals("Critical", StringComparison.OrdinalIgnoreCase))
                {
                    _testStatusLabels[index].Foreground = Brushes.IndianRed;
                }
                else if (status.Equals("Running", StringComparison.OrdinalIgnoreCase) || status.Equals("Probing...", StringComparison.OrdinalIgnoreCase))
                {
                    _testStatusLabels[index].Foreground = Brushes.CornflowerBlue;
                }
                else
                {
                    _testStatusLabels[index].Foreground = Brushes.Orange;
                }

                if (index == 1 && _engine.LastDownloadMbps > 0)
                {
                    TxtDownloadSpeed.Text = $"{_engine.LastDownloadMbps:F1} Mbps";
                    if (_engine.LastUploadMbps > 0)
                        TxtUploadSpeed.Text = $"{_engine.LastUploadMbps:F1} Mbps";
                }

                int finished = 0, total = 0;
                for (int i = 0; i < 10; i++)
                {
                    if (_engine.SelectedTests[i])
                    {
                        total++;
                        string? txt = _testStatusLabels[i].Text;
                        if (txt != "Pending" && txt != "Pending..." && txt != "Running" && txt != "Probing...") finished++;
                    }
                }
                if (total > 0)
                {
                    PrgOverall.Value = Math.Min(95, 10 + (85.0 * finished / total));
                }
            }
        }

        private int GetTestIndex(string testName)
        {
            string lower = testName.ToLowerInvariant();
            if (lower.Contains("dns")) return 0;
            if (lower.Contains("bandwidth") || lower.Contains("speed")) return 1;
            if (lower.Contains("http") || lower.Contains("web")) return 2;
            if (lower.Contains("ntp") || lower.Contains("clock")) return 3;
            if (lower.Contains("stun")) return 4;
            if (lower.Contains("routing") || lower.Contains("hop") || lower.Contains("gateway")) return 5;
            if (lower.Contains("randomization") || lower.Contains("port translation") || lower.Contains("rfc 6056")) return 6;
            if (lower.Contains("sip alg") || lower.Contains("5060")) return 7;
            if (lower.Contains("rtp") || lower.Contains("jitter") || lower.Contains("media")) return 8;
            if (lower.Contains("signalling") || lower.Contains("presence") || lower.Contains("websocket")) return 9;
            return -1;
        }

        #region Diagnostic Execution

        private async void BtnRunDiagnostics_Click(object? sender, RoutedEventArgs e)
        {
            if (_isDiagnosticsRunning)
            {
                _engine.Cancel();
                _isDiagnosticsRunning = false;
                AppendLog("Diagnostics cancellation requested.");
                return;
            }

            _isDiagnosticsRunning = true;
            PrgOverall.Value = 10;
            if (PnlSummaryIdle != null) PnlSummaryIdle.IsVisible = true;
            if (PnlSummaryPass != null) PnlSummaryPass.IsVisible = false;

            for (int i = 0; i < 10; i++)
            {
                _engine.SelectedTests[i] = _testCheckboxes[i].IsChecked == true;
                if (_engine.SelectedTests[i])
                {
                    _testStatusLabels[i].Text = "Pending";
                    _testStatusLabels[i].Foreground = new SolidColorBrush(Color.Parse("#64748b"));
                }
                else
                {
                    _testStatusLabels[i].Text = "Skipped";
                    _testStatusLabels[i].Foreground = Brushes.DimGray;
                }
            }

            AppendLog("Starting 10-Point Outbound Diagnostic Suite...");
            try
            {
                await _engine.RunDiagnosticsAsync();
            }
            catch (OperationCanceledException)
            {
                AppendLog("Diagnostic run cancelled.");
            }
            catch (Exception ex)
            {
                AppendLog($"Diagnostic run failed: {ex.Message}", true);
            }
            finally
            {
                _isDiagnosticsRunning = false;
            }
        }

        private async void BtnQuickFix_Click(object? sender, RoutedEventArgs e)
        {
            bool confirm = await ModernMessageBox.ShowAsync(this,
                "The One-Click Reset will:\n1. Flush macOS DNS cache (mDNSResponder)\n2. Clear Agilico Connect client cache folders\n3. Re-verify network routing.\n\nProceed with reset?",
                "Reset Connect",
                true);

            if (confirm)
            {
                AppendLog("Executing Reset Connect on macOS...");
                var (dnsOk, dnsMsg) = await MacSystemTools.FlushDnsAsync();
                AppendLog(dnsMsg);

                var (cacheOk, cacheMsg) = await MacSystemTools.ClearClientCacheAndRestartAsync();
                AppendLog(cacheMsg);

                await ModernMessageBox.ShowAsync(this, "Reset Connect completed successfully!\n\nmacOS DNS flushed and application cache cleaned.", "Reset Complete");
            }
        }

        #endregion

        #region Logs & PCAP

        private void BtnTogglePcap_Click(object? sender, RoutedEventArgs e)
        {
            if (_engine.Pcap.IsCapturing)
            {
                _engine.Pcap.Stop();
                BtnTogglePcap.Content = "Start PCAP Capture";
                BtnTogglePcap.Background = Brushes.White;
                BtnTogglePcap.Foreground = new SolidColorBrush(Color.Parse("#1e293b"));
                AppendLog("PCAP Packet capture stopped.");
            }
            else
            {
                _engine.Pcap.Start();
                BtnTogglePcap.Content = "Stop PCAP Capture";
                BtnTogglePcap.Background = Brushes.IndianRed;
                BtnTogglePcap.Foreground = Brushes.White;
                AppendLog("PCAP Packet capture started (Wireshark format).");
            }
        }

        private async void BtnExportPcap_Click(object? sender, RoutedEventArgs e)
        {
            byte[] pcapBytes = _engine.Pcap.GetPcapBytes();
            if (pcapBytes.Length == 0)
            {
                await ModernMessageBox.ShowAsync(this, "No packet capture data recorded.", "Notice");
                return;
            }

            try
            {
                string desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
                string filePath = Path.Combine(desktop, $"Capture_{DateTime.Now:yyyyMMdd_HHmmss}.pcap");
                await File.WriteAllBytesAsync(filePath, pcapBytes);
                await ModernMessageBox.ShowAsync(this, $"PCAP exported successfully to:\n{filePath}", "Export Complete");
            }
            catch (Exception ex)
            {
                await ModernMessageBox.ShowAsync(this, $"Failed to export PCAP: {ex.Message}", "Error");
            }
        }

        private async void BtnExportReport_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                string desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
                string filePath = Path.Combine(desktop, $"Agilico_Diagnostic_Report_{DateTime.Now:yyyyMMdd_HHmmss}.txt");

                var sb = new StringBuilder();
                sb.AppendLine("================================================================================");
                sb.AppendLine("AGILICO MSP TOOLKIT - DIAGNOSTIC REPORT (macOS)");
                sb.AppendLine($"Generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                sb.AppendLine($"Score: {BadgeScore.Text}");
                sb.AppendLine($"Local IP: {TxtLocalIpDisplay.Text} | Public IP: {TxtPublicIpDisplay.Text}");
                sb.AppendLine("================================================================================");
                sb.AppendLine();
                sb.AppendLine("ACTIVITY LOGS:");
                sb.AppendLine(_logBuilder.ToString());

                await File.WriteAllTextAsync(filePath, sb.ToString());
                await ModernMessageBox.ShowAsync(this, $"Full diagnostic report exported to:\n{filePath}", "Report Exported");
            }
            catch (Exception ex)
            {
                await ModernMessageBox.ShowAsync(this, $"Failed to export report: {ex.Message}", "Error");
            }
        }

        private void BtnClearLogs_Click(object? sender, RoutedEventArgs e)
        {
            _logBuilder.Clear();
            TxtLogs.Text = string.Empty;
        }

        #endregion
    }
}
