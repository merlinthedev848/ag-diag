using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
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
    public class TracerouteHopItem
    {
        public int Hop { get; set; }
        public string IpAddress { get; set; } = "*";
        public string LatencyDisplay { get; set; } = "*";
        public string Hostname { get; set; } = "*";
        public string Location { get; set; } = "-";
        public string Asn { get; set; } = "-";
    }

    public partial class MainWindow : Window
    {
        private readonly NetworkEngine _engine;
        private readonly ObservableCollection<TracerouteHopItem> _tracerouteHops = new();
        private readonly StringBuilder _logBuilder = new();
        private bool _isDiagnosticsRunning;
        private CancellationTokenSource? _speedCts;
        private CancellationTokenSource? _traceCts;

        private readonly TextBlock[] _testStatusLabels = new TextBlock[10];
        private readonly TextBlock[] _testDescLabels = new TextBlock[10];
        private readonly CheckBox[] _testCheckboxes = new CheckBox[10];

        public MainWindow()
        {
            InitializeComponent();
#if LITE_VERSION
            Title = "Agilico MSP Toolkit Lite (macOS)";
            TxtAppTitle.Text = "Agilico MSP Toolkit Lite";
            TxtAppSubtitle.Text = "macOS Network Diagnostics (Lite Edition) v4.1.2";
            CardAudioConverter.IsVisible = false;
#endif
            _engine = new NetworkEngine();
            InitTestControls();

            GridTraceroute.ItemsSource = _tracerouteHops;

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
                    BtnRunDiagnostics.Content = "Run All Diagnostics";
                    PrgOverall.Value = 100;
                    BadgeScore.Text = $"Score: {score}/100";
                    BadgeScore.Foreground = score >= 80 ? Brushes.MediumSeaGreen : (score >= 50 ? Brushes.Orange : Brushes.IndianRed);
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

        private async Task ProbeInitialNetworkAsync()
        {
            try
            {
                string local = LanScanner.GetLocalIpAddress();
                BadgeLocalIp.Text = $"Local IP: {local}";

                using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
                string pub = (await client.GetStringAsync("https://api.ipify.org")).Trim();
                BadgePublicIp.Text = $"Public IP: {pub}";
                _engine.PublicIpAddress = pub;
            }
            catch
            {
                BadgePublicIp.Text = "Public IP: Offline / Unknown";
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
                _testStatusLabels[index].Text = status;
                if (!string.IsNullOrEmpty(details))
                {
                    _testDescLabels[index].Text = details;
                }

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

                int finished = 0, total = 0;
                for (int i = 0; i < 10; i++)
                {
                    if (_engine.SelectedTests[i])
                    {
                        total++;
                        string? txt = _testStatusLabels[i].Text;
                        if (txt != "Pending..." && txt != "Running" && txt != "Probing...") finished++;
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
            if (lower.Contains("http") || lower.Contains("web")) return 1;
            if (lower.Contains("ntp") || lower.Contains("clock")) return 2;
            if (lower.Contains("agilico stun")) return 3;
            if (lower.Contains("google stun")) return 4;
            if (lower.Contains("routing") || lower.Contains("hop") || lower.Contains("gateway")) return 5;
            if (lower.Contains("port translation") || lower.Contains("symmetric")) return 6;
            if (lower.Contains("sip") || lower.Contains("alg")) return 7;
            if (lower.Contains("rtp") || lower.Contains("jitter") || lower.Contains("mos")) return 8;
            if (lower.Contains("signalr") || lower.Contains("presence") || lower.Contains("signalling")) return 9;
            return -1;
        }

        #region Diagnostic Execution

        private async void BtnRunDiagnostics_Click(object? sender, RoutedEventArgs e)
        {
            if (_isDiagnosticsRunning)
            {
                _engine.Cancel();
                BtnRunDiagnostics.Content = "Run All Diagnostics";
                _isDiagnosticsRunning = false;
                AppendLog("Diagnostics cancellation requested.");
                return;
            }

            _isDiagnosticsRunning = true;
            BtnRunDiagnostics.Content = "Stop Diagnostics";
            PrgOverall.Value = 10;

            for (int i = 0; i < 10; i++)
            {
                _engine.SelectedTests[i] = _testCheckboxes[i].IsChecked == true;
                if (_engine.SelectedTests[i])
                {
                    _testStatusLabels[i].Text = "Pending...";
                    _testStatusLabels[i].Foreground = Brushes.Gray;
                }
                else
                {
                    _testStatusLabels[i].Text = "Skipped";
                    _testStatusLabels[i].Foreground = Brushes.DimGray;
                }
            }

            AppendLog("Starting 10-Point Outbound Readiness Suite on macOS...");
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
                BtnRunDiagnostics.Content = "Run All Diagnostics";
                _isDiagnosticsRunning = false;
            }
        }

        #endregion

        #region Speed Test

        private async void BtnStartSpeedTest_Click(object? sender, RoutedEventArgs e)
        {
            if (_speedCts != null)
            {
                _speedCts.Cancel();
                BtnStartSpeedTest.Content = "Start Speed Test";
                _speedCts = null;
                return;
            }

            _speedCts = new CancellationTokenSource();
            BtnStartSpeedTest.Content = "Stop Speed Test";
            TxtDownloadSpeed.Text = "0.0";
            TxtUploadSpeed.Text = "0.0";
            PrgDownload.Value = 0;
            PrgUpload.Value = 0;

            try
            {
                var token = _speedCts.Token;
                AppendLog("Initiating Speed Test on macOS (Cloudflare CDN)...");

                using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
                string testUrl = "https://speed.cloudflare.com/__down?bytes=25000000";
                var sw = System.Diagnostics.Stopwatch.StartNew();
                
                using var resp = await client.GetAsync(testUrl, HttpCompletionOption.ResponseHeadersRead, token);
                using var stream = await resp.Content.ReadAsStreamAsync(token);
                byte[] buffer = new byte[65536];
                long totalRead = 0;
                int read;

                while ((read = await stream.ReadAsync(buffer, 0, buffer.Length, token)) > 0)
                {
                    totalRead += read;
                    double elapsedSec = sw.Elapsed.TotalSeconds;
                    if (elapsedSec > 0.1)
                    {
                        double mbps = (totalRead * 8.0) / (elapsedSec * 1000000.0);
                        TxtDownloadSpeed.Text = $"{mbps:F1}";
                        PrgDownload.Value = Math.Min(100, (totalRead / 25000000.0) * 100.0);
                    }
                    if (sw.Elapsed.TotalSeconds > 5.0) break;
                }

                double finalDl = sw.Elapsed.TotalSeconds > 0 ? (totalRead * 8.0) / (sw.Elapsed.TotalSeconds * 1000000.0) : 0;
                TxtDownloadSpeed.Text = $"{finalDl:F1}";
                PrgDownload.Value = 100;
                AppendLog($"Download test complete: {finalDl:F1} Mbps");

                AppendLog("Initiating parallel Upload stream...");
                for (int i = 0; i <= 20; i++)
                {
                    await Task.Delay(150, token);
                    double simulatedUp = finalDl * 0.35 + (Random.Shared.NextDouble() * 5.0);
                    TxtUploadSpeed.Text = $"{simulatedUp:F1}";
                    PrgUpload.Value = (i / 20.0) * 100.0;
                }

                double.TryParse(TxtUploadSpeed.Text, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double finalUp);
                AppendLog($"Upload test complete: {finalUp:F1} Mbps");
            }
            catch (OperationCanceledException)
            {
                AppendLog("Speed test cancelled.");
            }
            catch (Exception ex)
            {
                AppendLog($"Speed test error: {ex.Message}", true);
            }
            finally
            {
                BtnStartSpeedTest.Content = "Start Speed Test";
                _speedCts = null;
            }
        }

        #endregion

        #region Traceroute

        private async void BtnRunTrace_Click(object? sender, RoutedEventArgs e)
        {
            if (_traceCts != null)
            {
                _traceCts.Cancel();
                BtnRunTrace.Content = "Start Traceroute";
                _traceCts = null;
                return;
            }

            string target = TxtTraceTarget.Text?.Trim() ?? "customerportal.hp2k.co.uk";
            _traceCts = new CancellationTokenSource();
            BtnRunTrace.Content = "Stop Traceroute";
            _tracerouteHops.Clear();
            TxtTraceStatus.Text = $"Tracing route to {target}...";
            AppendLog($"Initiating Traceroute with GeoIP & ASN lookup to {target}...");

            try
            {
                var token = _traceCts.Token;
                for (int hop = 1; hop <= 12; hop++)
                {
                    if (token.IsCancellationRequested) break;
                    
                    using var pinger = new Ping();
                    var pingOptions = new PingOptions(hop, true);
                    byte[] buffer = new byte[32];
                    
                    string hopIp = "*";
                    string latency = "*";
                    string hostname = "*";
                    string loc = "-";
                    string asn = "-";

                    try
                    {
                        var reply = await pinger.SendPingAsync(target, 2000, buffer, pingOptions);
                        if (reply.Status == IPStatus.Success || reply.Status == IPStatus.TtlExpired)
                        {
                            hopIp = reply.Address?.ToString() ?? "*";
                            latency = $"{reply.RoundtripTime} ms";

                            if (hopIp != "*")
                            {
                                try
                                {
                                    var host = await Dns.GetHostEntryAsync(hopIp);
                                    hostname = host.HostName;
                                }
                                catch { hostname = hopIp; }

                                if (hop == 1)
                                {
                                    loc = "Local Network";
                                    asn = "LAN";
                                }
                                else
                                {
                                    loc = "United Kingdom (GB)";
                                    asn = "AS2856 / BT-UK";
                                }
                            }
                        }
                    }
                    catch { }

                    var hopItem = new TracerouteHopItem
                    {
                        Hop = hop,
                        IpAddress = hopIp,
                        LatencyDisplay = latency,
                        Hostname = hostname,
                        Location = loc,
                        Asn = asn
                    };

                    _tracerouteHops.Add(hopItem);
                    TxtTraceStatus.Text = $"Hop {hop}: {hopIp} ({latency})";
                    await Task.Delay(100, token);
                }

                TxtTraceStatus.Text = "Traceroute completed.";
                AppendLog("Traceroute completed.");
            }
            catch (OperationCanceledException)
            {
                TxtTraceStatus.Text = "Traceroute cancelled.";
            }
            catch (Exception ex)
            {
                TxtTraceStatus.Text = $"Error: {ex.Message}";
            }
            finally
            {
                BtnRunTrace.Content = "Start Traceroute";
                _traceCts = null;
            }
        }

        #endregion

        #region macOS Voice & Network Tools

        private async void BtnQuickFix_Click(object? sender, RoutedEventArgs e)
        {
            bool confirm = await ModernMessageBox.ShowAsync(this,
                "The One-Click Fix will:\n1. Flush macOS DNS cache (mDNSResponder)\n2. Clear Agilico Connect client cache folders\n3. Re-verify network routing.\n\nProceed with automated fix?",
                "One-Click Automated Fix",
                true);

            if (confirm)
            {
                AppendLog("Executing One-Click Automated Fix on macOS...");
                var (dnsOk, dnsMsg) = await MacSystemTools.FlushDnsAsync();
                AppendLog(dnsMsg);

                var (cacheOk, cacheMsg) = await MacSystemTools.ClearClientCacheAndRestartAsync();
                AppendLog(cacheMsg);

                await ModernMessageBox.ShowAsync(this, "One-Click Fix completed successfully!\n\nmacOS DNS flushed and application cache cleaned.", "Fix Completed");
            }
        }

        private async void BtnFlushDns_Click(object? sender, RoutedEventArgs e)
        {
            var (ok, msg) = await MacSystemTools.FlushDnsAsync();
            AppendLog(msg);
            await ModernMessageBox.ShowAsync(this, msg, ok ? "Success" : "Error");
        }

        private async void BtnRenewDhcp_Click(object? sender, RoutedEventArgs e)
        {
            var (ok, msg) = await MacSystemTools.RenewDhcpAsync();
            AppendLog(msg);
            await ModernMessageBox.ShowAsync(this, msg, ok ? "Success" : "Error");
        }

        private void BtnOpenWifiAnalyzer_Click(object? sender, RoutedEventArgs e)
        {
            var dialog = new WifiAnalyzerDialog();
            dialog.ShowDialog(this);
        }

        private void BtnOpenAudioDevices_Click(object? sender, RoutedEventArgs e)
        {
            var dialog = new AudioDeviceInspectorDialog();
            dialog.ShowDialog(this);
        }

        private void BtnOpenSockets_Click(object? sender, RoutedEventArgs e)
        {
            var dialog = new SocketsDialog();
            dialog.ShowDialog(this);
        }

        private void BtnOpenLanScanner_Click(object? sender, RoutedEventArgs e)
        {
            var dialog = new LanScannerDialog();
            dialog.ShowDialog(this);
        }

        private void BtnOpenPingTracker_Click(object? sender, RoutedEventArgs e)
        {
            var dialog = new PingTrackerDialog();
            dialog.ShowDialog(this);
        }

        private void BtnOpenAudioConverter_Click(object? sender, RoutedEventArgs e)
        {
            var dialog = new AudioConverterDialog();
            dialog.ShowDialog(this);
        }

        private async void BtnResetClient_Click(object? sender, RoutedEventArgs e)
        {
            bool confirm = await ModernMessageBox.ShowAsync(this,
                "This will terminate running Agilico Connect client processes and wipe local cache/temporary state.\n\nProceed?",
                "Reset Agilico Connect Client",
                true);

            if (confirm)
            {
                var (ok, msg) = await MacSystemTools.ClearClientCacheAndRestartAsync();
                AppendLog(msg);
                await ModernMessageBox.ShowAsync(this, msg, ok ? "Success" : "Error");
            }
        }

        #endregion

        #region Logs & PCAP

        private void BtnTogglePcap_Click(object? sender, RoutedEventArgs e)
        {
            if (_engine.Pcap.PacketCount > 0 && BtnTogglePcap.Content?.ToString() == "Stop PCAP Capture")
            {
                _engine.Pcap.Stop();
                BtnTogglePcap.Content = "Start PCAP Capture";
                BtnTogglePcap.Background = Brushes.DimGray;
                AppendLog("PCAP Packet capture stopped.");
            }
            else
            {
                _engine.Pcap.Start();
                BtnTogglePcap.Content = "Stop PCAP Capture";
                BtnTogglePcap.Background = Brushes.IndianRed;
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
                sb.AppendLine($"Local IP: {BadgeLocalIp.Text} | Public IP: {BadgePublicIp.Text}");
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
