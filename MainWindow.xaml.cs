using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Collections.ObjectModel;
using System.Windows.Input;
using Microsoft.Win32;

namespace AgilicoConnectChecker
{
    public partial class MainWindow : Window
    {
        private readonly NetworkEngine _engine;
        private readonly LanScanner _lanScanner;
        private readonly ObservableCollection<LanDevice> _lanDevices;
        private readonly PingTracker _pingTracker;
        private readonly ObservableCollection<PingResult> _pingLogs;
        private PingStats _currentPingStats = new PingStats();
        private CancellationTokenSource? _lanScanCts;
        private Button[] _navButtons = Array.Empty<Button>();
        private CancellationTokenSource? _traceCts;
        private readonly ObservableCollection<TraceHop> _traceHops = new();
        private CancellationTokenSource? _speedTestCts;
        private readonly System.Windows.Threading.DispatcherTimer _pcapTimer;
        private bool _isManualCapturing = false;
        private readonly ObservableCollection<SrvRecord> _srvRecords = new();
        private readonly ObservableCollection<PortProbeResult> _portProbeResults = new();
        private CancellationTokenSource? _portProbeCts;

        public MainWindow()
        {
            InitializeComponent();
            _engine = new NetworkEngine();
            _lanScanner = new LanScanner();
            _lanDevices = new ObservableCollection<LanDevice>();
            _pingTracker = new PingTracker();
            _pingLogs = new ObservableCollection<PingResult>();
            
            _pcapTimer = new System.Windows.Threading.DispatcherTimer();
            _pcapTimer.Interval = TimeSpan.FromMilliseconds(500);
            _pcapTimer.Tick += PcapTimer_Tick;
            
            // Wire up engine events
            _engine.OnLog += Engine_OnLog;
            _engine.OnProgress += Engine_OnProgress;
            _engine.OnComplete += Engine_OnComplete;

            // Wire up ping tracker events
            _pingTracker.OnPingResult += PingTracker_OnPingResult;

            Loaded += MainWindow_Loaded;
        }

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            _navButtons = new[] { BtnDashboard, BtnNetScan, BtnPingTrack, BtnTraceroute, BtnPcap, BtnHelp, BtnLogs, BtnSettings, BtnVoipTools };
            GridLanDevices.ItemsSource = _lanDevices;
            GridPingLogs.ItemsSource = _pingLogs;
            GridTraceHops.ItemsSource = _traceHops;
            GridSrvRecords.ItemsSource = _srvRecords;
            GridPortProber.ItemsSource = _portProbeResults;
            
            // Initialize view
            SelectTab(0, BtnDashboard);
            ResetTestStatuses();
            PanelSummaryDefault.Visibility = Visibility.Visible;
            
            RefreshLocalNetworkInfo();

            // Sync settings from engine (which auto-loaded from registry where available)
            TxtStunServer.Text = _engine.StunServer;
            TxtStunPort.Text = _engine.StunPort.ToString();
            TxtLocalPort.Text = _engine.LocalSipPort.ToString();
            TxtSipAlgServer.Text = _engine.SipAlgServer;
            TxtSipAlgPort.Text = _engine.SipAlgPort.ToString();
            ChkSimulation.IsChecked = _engine.IsSimulationMode;

            // Trigger firewall permission prompt on load so it doesn't block running tests
            _engine.TriggerFirewallPrompt();

            // Run connection speed test at startup
            _ = RunStartupSpeedTestAsync();
        }

        #region Navigation

        private void SelectTab(int index, Button activeButton)
        {
            PageTabControl.SelectedIndex = index;

            foreach (var btn in _navButtons)
            {
                btn.Background = Brushes.Transparent;
                SetStripeVisibility(btn, Visibility.Collapsed);
            }

            activeButton.Background = (Brush)FindResource("SidebarItemHoverBrush");
            SetStripeVisibility(activeButton, Visibility.Visible);

            // Manage PCAP stats real-time polling
            if (index == 4)
            {
                UpdatePcapStats();
                _pcapTimer.Start();
            }
            else
            {
                _pcapTimer.Stop();
            }
        }

        private void SetStripeVisibility(Button button, Visibility visibility)
        {
            // ApplyTemplate ensures the visual tree is built
            button.ApplyTemplate();
            var stripe = button.Template.FindName("stripe", button) as Border;
            if (stripe != null)
            {
                stripe.Visibility = visibility;
            }
        }

        private void BtnDashboard_Click(object sender, RoutedEventArgs e) => SelectTab(0, BtnDashboard);
        private void BtnNetScan_Click(object sender, RoutedEventArgs e) => SelectTab(1, BtnNetScan);
        private void BtnPingTrack_Click(object sender, RoutedEventArgs e) => SelectTab(2, BtnPingTrack);
        private void BtnTraceroute_Click(object sender, RoutedEventArgs e) => SelectTab(3, BtnTraceroute);
        private void BtnPcap_Click(object sender, RoutedEventArgs e) => SelectTab(4, BtnPcap);
        private void BtnHelp_Click(object sender, RoutedEventArgs e) => SelectTab(5, BtnHelp);
        private void BtnLogs_Click(object sender, RoutedEventArgs e) => SelectTab(6, BtnLogs);
        private void BtnSettings_Click(object sender, RoutedEventArgs e) => SelectTab(7, BtnSettings);
        private void BtnVoipTools_Click(object sender, RoutedEventArgs e) => SelectTab(8, BtnVoipTools);

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            _engine.Cancel();
            _pingTracker.Stop();
            _pcapTimer.Stop();
            _lanScanCts?.Cancel();
            _lanScanCts?.Dispose();
            _speedTestCts?.Cancel();
            _speedTestCts?.Dispose();
            _portProbeCts?.Cancel();
            _portProbeCts?.Dispose();
        }

        #endregion

        #region Diagnostic Test Execution

        private void ResetTestStatuses()
        {
            if (ChkTest1 != null) UpdateTestUI(1, ChkTest1.IsChecked == true ? "pending" : "skipped", ChkTest1.IsChecked == true ? "Pending" : "Skipped by user");
            if (ChkTest2 != null) UpdateTestUI(2, ChkTest2.IsChecked == true ? "pending" : "skipped", ChkTest2.IsChecked == true ? "Pending" : "Skipped by user");
            if (ChkTest3 != null) UpdateTestUI(3, ChkTest3.IsChecked == true ? "pending" : "skipped", ChkTest3.IsChecked == true ? "Pending" : "Skipped by user");
            if (ChkTest4 != null) UpdateTestUI(4, ChkTest4.IsChecked == true ? "pending" : "skipped", ChkTest4.IsChecked == true ? "Pending" : "Skipped by user");
            if (ChkTest5 != null) UpdateTestUI(5, ChkTest5.IsChecked == true ? "pending" : "skipped", ChkTest5.IsChecked == true ? "Pending" : "Skipped by user");
            if (ChkTest6 != null) UpdateTestUI(6, ChkTest6.IsChecked == true ? "pending" : "skipped", ChkTest6.IsChecked == true ? "Pending" : "Skipped by user");
            if (ChkTest7 != null) UpdateTestUI(7, ChkTest7.IsChecked == true ? "pending" : "skipped", ChkTest7.IsChecked == true ? "Pending" : "Skipped by user");
            if (ChkTest8 != null) UpdateTestUI(8, ChkTest8.IsChecked == true ? "pending" : "skipped", ChkTest8.IsChecked == true ? "Pending" : "Skipped by user");
            if (ChkTest9 != null) UpdateTestUI(9, ChkTest9.IsChecked == true ? "pending" : "skipped", ChkTest9.IsChecked == true ? "Pending" : "Skipped by user");
            if (ChkTest10 != null) UpdateTestUI(10, ChkTest10.IsChecked == true ? "pending" : "skipped", ChkTest10.IsChecked == true ? "Pending" : "Skipped by user");

            PanelSummaryPass.Visibility = Visibility.Collapsed;
            PanelSummaryFail.Visibility = Visibility.Collapsed;
        }

        private void RefreshLocalNetworkInfo()
        {
            var info = _engine.GetLocalNetworkInfo();
            TxtLocalStatus.Text = info.Status;
            TxtLocalIp.Text = info.IpAddress;
            TxtLocalSubnet.Text = info.SubnetMask;
            TxtLocalGateway.Text = info.Gateway;
            TxtLocalDns.Text = info.DnsServers;
            TxtLocalVlan.Text = info.Vlan;
            TxtPublicIp.Text = info.PublicIpAddress;

            // Fetch public IP in background if it's currently not detected
            if (info.PublicIpAddress == "-" || info.PublicIpAddress == "Unknown" || info.PublicIpAddress == "Detecting...")
            {
                TxtPublicIp.Text = "Detecting...";
                _ = Task.Run(async () =>
                {
                    string pubIp = await _engine.ResolvePublicIpAsync(CancellationToken.None);
                    Dispatcher.Invoke(() =>
                    {
                        TxtPublicIp.Text = pubIp;
                    });
                });
            }

            if (info.Status.Contains("No ") || info.Status.Contains("Disconnected"))
            {
                TxtLocalStatus.Foreground = (System.Windows.Media.Brush)FindResource("AccentRedBrush");
                TxtLocalStatus.FontWeight = FontWeights.Bold;
            }
            else if (info.Status.Contains("VPN"))
            {
                TxtLocalStatus.Foreground = (System.Windows.Media.Brush)FindResource("AccentYellowBrush");
                TxtLocalStatus.FontWeight = FontWeights.Bold;
            }
            else
            {
                TxtLocalStatus.Foreground = (System.Windows.Media.Brush)FindResource("AccentBlueBrush");
                TxtLocalStatus.FontWeight = FontWeights.SemiBold;
            }
        }

        private async void BtnStart_Click(object sender, RoutedEventArgs e)
        {
            // Sync settings from UI text fields
            if (!ValidateAndApplySettings()) return;

            // Copy checkbox states to engine
            _engine.SelectedTests[0] = ChkTest1.IsChecked == true;
            _engine.SelectedTests[1] = ChkTest2.IsChecked == true;
            _engine.SelectedTests[2] = ChkTest3.IsChecked == true;
            _engine.SelectedTests[3] = ChkTest4.IsChecked == true;
            _engine.SelectedTests[4] = ChkTest5.IsChecked == true;
            _engine.SelectedTests[5] = ChkTest6.IsChecked == true;
            _engine.SelectedTests[6] = ChkTest7.IsChecked == true;
            _engine.SelectedTests[7] = ChkTest8.IsChecked == true;
            _engine.SelectedTests[8] = ChkTest9.IsChecked == true;
            _engine.SelectedTests[9] = ChkTest10.IsChecked == true;

            // Update Controls UI
            BtnStart.Visibility = Visibility.Collapsed;
            BtnStop.Visibility = Visibility.Visible;
            ProgressArea.Visibility = Visibility.Visible;
            TxtProgressStatus.Text = "Initializing...";
            
            // Clear previous summary and logs
            PanelSummaryDefault.Visibility = Visibility.Collapsed;
            PanelSummaryPass.Visibility = Visibility.Collapsed;
            PanelSummaryFail.Visibility = Visibility.Collapsed;
            TxtLogs.Clear();

            ResetTestStatuses();
            RefreshLocalNetworkInfo();

            // Run
            await _engine.RunDiagnosticsAsync();
        }

        private async void BtnStartLanScan_Click(object sender, RoutedEventArgs e)
        {
            BtnStartLanScan.IsEnabled = false;
            PanelLanScanProgress.Visibility = Visibility.Visible;
            _lanDevices.Clear();

            _lanScanCts?.Cancel();
            _lanScanCts?.Dispose();
            _lanScanCts = new CancellationTokenSource();
            var token = _lanScanCts.Token;
            
            try
            {
                var devices = await _lanScanner.ScanNetworkAsync((completed, total) =>
                {
                    Dispatcher.Invoke(() =>
                    {
                        TxtLanScanProgress.Text = $"Scanning subnet ({completed}/{total})...";
                    });
                }, (dev) =>
                {
                    Dispatcher.Invoke(() =>
                    {
                        _lanDevices.Add(dev);
                    });
                }, token);

                // Sort at the end
                Dispatcher.Invoke(() =>
                {
                    var sorted = _lanDevices.OrderBy(d => 
                    {
                        if (System.Net.IPAddress.TryParse(d.IpAddress, out var ip))
                        {
                            var bytes = ip.GetAddressBytes();
                            return (uint)(bytes[0] << 24 | bytes[1] << 16 | bytes[2] << 8 | bytes[3]);
                        }
                        return 0u;
                    }).ToList();
                    _lanDevices.Clear();
                    foreach (var d in sorted)
                    {
                        _lanDevices.Add(d);
                    }
                });
            }
            catch (OperationCanceledException) { /* scan was cancelled */ }

            PanelLanScanProgress.Visibility = Visibility.Collapsed;
            BtnStartLanScan.IsEnabled = true;
        }

        private void BtnStop_Click(object sender, RoutedEventArgs e)
        {
            _engine.Cancel();
            RestoreControlButtons();
        }

        private void RestoreControlButtons()
        {
            BtnStart.Visibility = Visibility.Visible;
            BtnStop.Visibility = Visibility.Collapsed;
            ProgressArea.Visibility = Visibility.Collapsed;
        }

        private bool ValidateAndApplySettings()
        {
            // STUN Host
            if (string.IsNullOrWhiteSpace(TxtStunServer.Text))
            {
                MessageBox.Show("Please enter a valid STUN Server address.", "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }
            _engine.StunServer = TxtStunServer.Text.Trim();

            // STUN Port
            if (!int.TryParse(TxtStunPort.Text, out int stunPort) || stunPort <= 0 || stunPort > 65535)
            {
                MessageBox.Show("Please enter a valid STUN Port (1-65535).", "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }
            _engine.StunPort = stunPort;

            // Local SIP Port
            if (!int.TryParse(TxtLocalPort.Text, out int localPort) || localPort <= 0 || localPort > 65535)
            {
                MessageBox.Show("Please enter a valid Local SIP Port (1-65535).", "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }
            _engine.LocalSipPort = localPort;

            // SIP ALG Host
            if (string.IsNullOrWhiteSpace(TxtSipAlgServer.Text))
            {
                MessageBox.Show("Please enter a valid SIP ALG Server address.", "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }
            _engine.SipAlgServer = TxtSipAlgServer.Text.Trim();

            // SIP ALG Port
            if (!int.TryParse(TxtSipAlgPort.Text, out int sipAlgPort) || sipAlgPort <= 0 || sipAlgPort > 65535)
            {
                MessageBox.Show("Please enter a valid SIP ALG Port (1-65535).", "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }
            _engine.SipAlgPort = sipAlgPort;

            // Simulation mode
            _engine.IsSimulationMode = ChkSimulation.IsChecked == true;

            return true;
        }

        #endregion

        #region Engine Callbacks

        private void Engine_OnLog(string message, bool isError)
        {
            Dispatcher.Invoke(() =>
            {
                TxtLogs.AppendText(message + Environment.NewLine);
                TxtLogs.ScrollToEnd();
            });
        }

        private void Engine_OnProgress(string testName, string status, string details)
        {
            Dispatcher.Invoke(() =>
            {
                TxtProgressStatus.Text = $"{testName}: {details}";
                
                int testIndex = testName switch
                {
                    "DNS Domain & Resolution Check" => 1,
                    "HTTP/HTTPS Outbound Probes" => 2,
                    "NTP Subsystem (UDP 123)" => 3,
                    "Agilico STUN Servers" => 4,
                    "Google STUN Servers" => 5,
                    "NAT Routing & Hops Check" => 6,
                    "NAT Port Translation (Random Port)" => 7,
                    "SIP ALG Detection" => 8,
                    "RTP Jitter/Loss Check" => 9,
                    "Inbound Signalling & Presence" => 10,
                    _ => 0
                };

                if (testIndex > 0)
                {
                    UpdateTestUI(testIndex, status, details);
                }
            });
        }

        private void Engine_OnComplete(bool success, int score)
        {
            Dispatcher.Invoke(() =>
            {
                RestoreControlButtons();
                RefreshLocalNetworkInfo();

                PanelSummaryDefault.Visibility = Visibility.Collapsed;

                // Update Results tab
                PanelResultsDefault.Visibility = Visibility.Collapsed;

                // Collect test detail TextBlocks and their corresponding Result cards
                var testDetails = new[] { Test1Details, Test2Details, Test3Details, Test4Details, Test5Details, Test6Details, Test7Details, Test8Details, Test9Details, Test10Details };
                var resultCards = new[] { Result1Card, Result2Card, Result3Card, Result4Card, Result5Card, Result6Card, Result7Card, Result8Card, Result9Card, Result10Card };
                var resultDetailTexts = new[] { Result1Detail, Result2Detail, Result3Detail, Result4Detail, Result5Detail, Result6Detail, Result7Detail, Result8Detail, Result9Detail, Result10Detail };

                bool anyFailed = false;
                for (int i = 0; i < testDetails.Length; i++)
                {
                    if (i == 3) continue; // Skip Test 4 (Agilico STUN is hidden and informational only)
                    bool failed = testDetails[i].Text.Contains("Fail");
                    resultCards[i].Visibility = failed ? Visibility.Visible : Visibility.Collapsed;
                    if (failed)
                    {
                        resultDetailTexts[i].Text = testDetails[i].Text;
                        anyFailed = true;
                    }
                }

                if (success || !anyFailed)
                {
                    TxtScorePass.Text = $"Score: {score}/100";
                    PanelSummaryPass.Visibility = Visibility.Visible;
                    PanelSummaryFail.Visibility = Visibility.Collapsed;
                    PanelResultsAllPass.Visibility = Visibility.Visible;
                    PanelResultsFailed.Visibility = Visibility.Collapsed;
                }
                else
                {
                    TxtScoreFail.Text = $"Score: {score}/100";
                    PanelSummaryPass.Visibility = Visibility.Collapsed;
                    PanelSummaryFail.Visibility = Visibility.Visible;
                    PanelResultsAllPass.Visibility = Visibility.Collapsed;
                    PanelResultsFailed.Visibility = Visibility.Visible;
                    
                    // Build summary text for the Dashboard panel
                    var sb = new StringBuilder();
                    sb.AppendLine("Please resolve the following network issues:");
                    
                    for (int i = 0; i < testDetails.Length; i++)
                    {
                        if (testDetails[i].Text.Contains("Fail"))
                        {
                            sb.AppendLine($"• Test {i + 1}: {testDetails[i].Text}");
                        }
                    }
                    
                    TxtFailInstructions.Text = sb.ToString();
                }
            });
        }

        private void UpdateTestUI(int testNum, string status, string details)
        {
            var pending = FindName($"Test{testNum}IconPending") as UIElement;
            var running = FindName($"Test{testNum}IconRunning") as UIElement;
            var spinner = FindName($"Test{testNum}Spinner") as UIElement;
            var pass = FindName($"Test{testNum}IconPass") as UIElement;
            var fail = FindName($"Test{testNum}IconFail") as UIElement;
            var text = FindName($"Test{testNum}Details") as TextBlock;

            if (text != null) text.Text = details;

            if (pending != null) pending.Visibility = Visibility.Collapsed;
            if (running != null) running.Visibility = Visibility.Collapsed;
            if (spinner != null) spinner.Visibility = Visibility.Collapsed;
            if (pass != null) pass.Visibility = Visibility.Collapsed;
            if (fail != null) fail.Visibility = Visibility.Collapsed;

            switch (status.ToLower())
            {
                case "pending":
                    if (pending != null) pending.Visibility = Visibility.Visible;
                    break;
                case "running":
                    if (running != null) running.Visibility = Visibility.Visible;
                    if (spinner != null) spinner.Visibility = Visibility.Visible;
                    break;
                case "passed":
                case "pass":
                    if (pass != null) pass.Visibility = Visibility.Visible;
                    break;
                case "failed":
                case "fail":
                    if (fail != null) fail.Visibility = Visibility.Visible;
                    break;
            }
        }

        #endregion

        #region Log Tab Actions

        private void BtnClearLog_Click(object sender, RoutedEventArgs e)
        {
            TxtLogs.Clear();
        }

        private void BtnSaveLog_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(TxtLogs.Text))
            {
                MessageBox.Show("Log is empty. Run a diagnostic test first to generate logs.", "Information", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var dialog = new SaveFileDialog
            {
                Filter = "Text Files (*.txt)|*.txt|All Files (*.*)|*.*",
                FileName = $"Agilico_Connect_Check_{DateTime.Now:yyyyMMdd_HHmmss}.txt"
            };

            if (dialog.ShowDialog() == true)
            {
                try
                {
                    File.WriteAllText(dialog.FileName, TxtLogs.Text);
                    MessageBox.Show("Log saved successfully.", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Failed to save log: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }



        #endregion

        #region Ping Track Tab Actions

        private static readonly System.Text.RegularExpressions.Regex HostnameRegex = new(
            @"^([a-zA-Z0-9]([a-zA-Z0-9\-]{0,61}[a-zA-Z0-9])?\.)*[a-zA-Z0-9]([a-zA-Z0-9\-]{0,61}[a-zA-Z0-9])?$",
            System.Text.RegularExpressions.RegexOptions.Compiled);

        private void BtnStartPingTrack_Click(object sender, RoutedEventArgs e)
        {
            var target = TxtPingTarget.Text.Trim();
            if (string.IsNullOrWhiteSpace(target))
            {
                MessageBox.Show("Please enter a valid IP address or hostname.", "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // Validate hostname or IP address format
            bool isValidIp = System.Net.IPAddress.TryParse(target, out _);
            bool isValidHostname = !isValidIp && target.Length <= 253 && HostnameRegex.IsMatch(target);
            if (!isValidIp && !isValidHostname)
            {
                MessageBox.Show("Please enter a valid IPv4/IPv6 address or RFC-compliant hostname.", "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            int intervalMs = 1000;
            if (ComboPingInterval.SelectedItem is ComboBoxItem selectedItem && selectedItem.Tag != null)
            {
                if (int.TryParse(selectedItem.Tag.ToString(), out int parsedInterval))
                {
                    intervalMs = parsedInterval;
                }
            }

            // UI changes
            TxtPingTarget.IsEnabled = false;
            ComboPingInterval.IsEnabled = false;
            BtnStartPingTrack.Visibility = Visibility.Collapsed;
            BtnStopPingTrack.Visibility = Visibility.Visible;

            _pingLogs.Clear();
            ResetPingUIStats();

            // Start Ping Tracker
            _pingTracker.Start(target, intervalMs);
        }

        private void BtnStopPingTrack_Click(object sender, RoutedEventArgs e)
        {
            _pingTracker.Stop();
            RestorePingControlUI();
        }

        private void RestorePingControlUI()
        {
            TxtPingTarget.IsEnabled = true;
            ComboPingInterval.IsEnabled = true;
            BtnStartPingTrack.Visibility = Visibility.Visible;
            BtnStopPingTrack.Visibility = Visibility.Collapsed;
        }

        private void ResetPingUIStats()
        {
            TxtPingCurrent.Text = "-";
            TxtPingAverage.Text = "-";
            TxtPingMinMax.Text = "-";
            TxtPingJitter.Text = "-";
            TxtPingLoss.Text = "0.0%";
            TxtPingLoss.Foreground = (Brush)FindResource("TextDarkBrush");
            PingGraphCanvas.Children.Clear();
        }

        private void PingTracker_OnPingResult(PingResult result, PingStats stats)
        {
            Dispatcher.Invoke(() =>
            {
                _currentPingStats = stats;

                // Update metrics labels
                TxtPingCurrent.Text = result.LatencyMs.HasValue ? $"{result.LatencyMs.Value} ms" : "Timeout";
                TxtPingCurrent.Foreground = result.LatencyMs.HasValue ? (Brush)FindResource("AccentBlueBrush") : (Brush)FindResource("AccentRedBrush");

                TxtPingAverage.Text = $"{stats.Average:F1} ms";
                TxtPingMinMax.Text = $"{stats.Min} / {stats.Max} ms";
                TxtPingJitter.Text = $"{stats.Jitter:F1} ms";
                
                TxtPingLoss.Text = $"{stats.LossPercentage:F1}%";
                if (stats.LossPercentage > 0)
                {
                    TxtPingLoss.Foreground = (Brush)FindResource("AccentRedBrush");
                }
                else
                {
                    TxtPingLoss.Foreground = (Brush)FindResource("TextDarkBrush");
                }

                // Add to scrolling list
                _pingLogs.Insert(0, result);
                if (_pingLogs.Count > 50)
                {
                    _pingLogs.RemoveAt(_pingLogs.Count - 1);
                }

                // Draw Graph
                DrawPingGraph(_pingTracker.GetRecentResults(), stats);
            });
        }

        private void DrawPingGraph(List<PingResult> recentPings, PingStats stats)
        {
            PingGraphCanvas.Children.Clear();

            double width = PingGraphCanvas.ActualWidth;
            double height = PingGraphCanvas.ActualHeight;

            if (width <= 0 || height <= 0 || recentPings.Count == 0) return;

            // Find the max value to scale Y axis. We want at least 100ms as max scale, or the actual max rounded up.
            double maxVal = 100.0;
            var validLatencies = recentPings.Where(p => p.LatencyMs.HasValue).Select(p => (double)p.LatencyMs!.Value).ToList();
            if (validLatencies.Count > 0)
            {
                double currentMax = validLatencies.Max();
                if (currentMax > maxVal)
                {
                    maxVal = Math.Ceiling(currentMax / 50.0) * 50.0; // Round up to nearest 50ms
                }
            }

            // Grid lines every 25% of maxVal
            double gridStep = maxVal / 4.0;
            for (double val = gridStep; val <= maxVal; val += gridStep)
            {
                double y = height - (val / maxVal * height);
                
                // Grid Line
                var line = new Line
                {
                    X1 = 0,
                    Y1 = y,
                    X2 = width,
                    Y2 = y,
                    Stroke = new SolidColorBrush(Color.FromArgb(20, 148, 163, 184)), // subtle grid line
                    StrokeThickness = 1
                };
                PingGraphCanvas.Children.Add(line);

                // Label
                var text = new TextBlock
                {
                    Text = $"{val:0} ms",
                    Foreground = new SolidColorBrush(Color.FromRgb(148, 163, 184)), // slate-400
                    FontSize = 9,
                    Margin = new Thickness(5, y - 12, 0, 0)
                };
                PingGraphCanvas.Children.Add(text);
            }

            // Prepare line points
            int maxPoints = 60;
            int pointCount = recentPings.Count;
            double xStep = width / (maxPoints - 1);

            var points = new PointCollection();
            var lossBars = new List<double>(); // X coordinates for packet loss

            for (int i = 0; i < pointCount; i++)
            {
                var ping = recentPings[i];
                double x = (maxPoints - pointCount + i) * xStep;

                if (ping.LatencyMs.HasValue)
                {
                    double latency = ping.LatencyMs.Value;
                    double y = height - (latency / maxVal * height);
                    y = Math.Max(0, Math.Min(height, y));
                    points.Add(new Point(x, y));
                }
                else
                {
                    lossBars.Add(x);
                }
            }

            // Draw Packet Loss red bars
            foreach (var x in lossBars)
            {
                var lossLine = new Line
                {
                    X1 = x,
                    Y1 = 0,
                    X2 = x,
                    Y2 = height,
                    Stroke = (Brush)FindResource("AccentRedBrush"),
                    StrokeThickness = Math.Max(1.5, xStep),
                    Opacity = 0.4
                };
                PingGraphCanvas.Children.Add(lossLine);
            }

            // Draw the Line Path
            if (points.Count > 0)
            {
                // 1. Draw area gradient underneath the line
                var areaPoints = new PointCollection();
                areaPoints.Add(new Point(points[0].X, height));
                foreach (var p in points) areaPoints.Add(p);
                areaPoints.Add(new Point(points[points.Count - 1].X, height));

                var polygon = new Polygon
                {
                    Points = areaPoints,
                    Fill = new LinearGradientBrush
                    {
                        StartPoint = new Point(0, 0),
                        EndPoint = new Point(0, 1),
                        GradientStops = new GradientStopCollection
                        {
                            new GradientStop(Color.FromArgb(50, 59, 130, 246), 0.0), // Semi-transparent blue
                            new GradientStop(Color.FromArgb(0, 59, 130, 246), 1.0)  // Fully transparent
                        }
                    }
                };
                PingGraphCanvas.Children.Add(polygon);

                // 2. Draw the line itself
                var polyline = new Polyline
                {
                    Points = points,
                    Stroke = (Brush)FindResource("AccentBlueBrush"),
                    StrokeThickness = 2
                };
                PingGraphCanvas.Children.Add(polyline);

                // Draw circle dot for current point
                if (recentPings.Last().LatencyMs.HasValue)
                {
                    var lastPoint = points.Last();
                    var dot = new Ellipse
                    {
                        Width = 6,
                        Height = 6,
                        Fill = (Brush)FindResource("AccentBlueBrush"),
                        Margin = new Thickness(lastPoint.X - 3, lastPoint.Y - 3, 0, 0)
                    };
                    PingGraphCanvas.Children.Add(dot);
                }
            }
        }

        private void PingGraphCanvas_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (_pingTracker != null && _pingTracker.IsRunning)
            {
                DrawPingGraph(_pingTracker.GetRecentResults(), _currentPingStats);
            }
        }

        private void BtnDownloadPingLog_Click(object sender, RoutedEventArgs e)
        {
            var pings = _pingTracker.GetAllResults();
            if (pings.Count == 0)
            {
                MessageBox.Show("No ping tracking data to download. Start tracking first.", "Information", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var dialog = new SaveFileDialog
            {
                Filter = "CSV Files (*.csv)|*.csv|All Files (*.*)|*.*",
                FileName = $"Ping_Track_Log_{DateTime.Now:yyyyMMdd_HHmmss}.csv"
            };

            if (dialog.ShowDialog() == true)
            {
                try
                {
                    _pingTracker.ExportLog(dialog.FileName);
                    MessageBox.Show("Ping track log saved successfully.", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Failed to save ping log: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        #endregion

        #region Extra Tools and Handlers

        private void BtnDownloadPcap_Click(object sender, RoutedEventArgs e)
        {
            if (_engine.Pcap.PacketCount == 0)
            {
                MessageBox.Show("There are no captured packets to save. Please run a packet capture first.", "No Packets Captured", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var sfd = new SaveFileDialog
            {
                Filter = "PCAP Files (*.pcap)|*.pcap",
                FileName = $"agilico_diagnostics_{DateTime.Now:yyyyMMdd_HHmmss}.pcap"
            };

            if (sfd.ShowDialog() == true)
            {
                try
                {
                    var bytes = _engine.Pcap.GetPcapBytes();
                    System.IO.File.WriteAllBytes(sfd.FileName, bytes);
                    MessageBox.Show("PCAP log saved successfully.", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Failed to save PCAP log: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private async Task<(double downloadMbps, double uploadMbps)> RunSpeedTestAsync(CancellationToken token)
        {
            if (_engine.IsSimulationMode)
            {
                await Task.Delay(2000, token);
                var rand = new Random();
                return (Math.Round(50 + rand.NextDouble() * 30, 1), Math.Round(15 + rand.NextDouble() * 10, 1));
            }

            double downloadMbps = 0;
            double uploadMbps = 0;
            using var client = new System.Net.Http.HttpClient();
            client.Timeout = TimeSpan.FromSeconds(30);

            // 1. Download Test (Cloudflare 50MB Endpoint)
            try
            {
                var sw = System.Diagnostics.Stopwatch.StartNew();
                var response = await client.GetAsync("https://speed.cloudflare.com/__down?bytes=50000000", token);
                response.EnsureSuccessStatusCode();
                var bytes = await response.Content.ReadAsByteArrayAsync(token);
                sw.Stop();
                
                double seconds = sw.Elapsed.TotalSeconds;
                if (seconds > 0)
                {
                    downloadMbps = (bytes.Length * 8.0) / (seconds * 1000000.0);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Download speed test failed: {ex.Message}");
            }

            // 2. Upload Test (Cloudflare Upload Endpoint)
            try
            {
                // 15 MB random payload
                var payload = new byte[15000000];
                new Random().NextBytes(payload);
                var content = new System.Net.Http.ByteArrayContent(payload);

                var sw = System.Diagnostics.Stopwatch.StartNew();
                var response = await client.PostAsync("https://speed.cloudflare.com/__up", content, token);
                response.EnsureSuccessStatusCode();
                sw.Stop();

                double seconds = sw.Elapsed.TotalSeconds;
                if (seconds > 0)
                {
                    uploadMbps = (payload.Length * 8.0) / (seconds * 1000000.0);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Upload speed test failed: {ex.Message}");
            }

            return (Math.Round(downloadMbps, 1), Math.Round(uploadMbps, 1));
        }

        private async void BtnStartTrace_Click(object sender, RoutedEventArgs e)
        {
            var target = TxtTraceTarget.Text.Trim();
            if (string.IsNullOrWhiteSpace(target))
            {
                MessageBox.Show("Please enter a valid IP address or hostname.", "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            bool isValidIp = System.Net.IPAddress.TryParse(target, out _);
            bool isValidHostname = !isValidIp && target.Length <= 253 && HostnameRegex.IsMatch(target);
            if (!isValidIp && !isValidHostname)
            {
                MessageBox.Show("Please enter a valid IPv4/IPv6 address or RFC-compliant hostname.", "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            BtnStartTrace.Visibility = Visibility.Collapsed;
            BtnStopTrace.Visibility = Visibility.Visible;
            TxtTraceTarget.IsEnabled = false;
            _traceHops.Clear();

            _traceCts?.Cancel();
            _traceCts?.Dispose();
            _traceCts = new CancellationTokenSource();
            var token = _traceCts.Token;

            try
            {
                await Task.Run(async () =>
                {
                    int maxHops = 30;
                    int timeoutMs = 1500;
                    bool destinationReached = false;

                    for (int hop = 1; hop <= maxHops; hop++)
                    {
                        if (token.IsCancellationRequested) break;

                        var currentHop = hop;
                        var hopResult = new TraceHop { HopNumber = currentHop, IpAddress = "*", Hostname = "*", RttDisplay = "Timeout" };

                        Dispatcher.Invoke(() => _traceHops.Add(hopResult));

                        try
                        {
                            using var ping = new System.Net.NetworkInformation.Ping();
                            var options = new System.Net.NetworkInformation.PingOptions(currentHop, true);
                            byte[] buffer = new byte[32];

                            var sw = System.Diagnostics.Stopwatch.StartNew();
                            var reply = await ping.SendPingAsync(target, timeoutMs, buffer, options);
                            sw.Stop();

                            if (token.IsCancellationRequested) break;

                            if (reply.Status == System.Net.NetworkInformation.IPStatus.TtlExpired || reply.Status == System.Net.NetworkInformation.IPStatus.Success)
                            {
                                hopResult.IpAddress = reply.Address?.ToString() ?? "*";
                                hopResult.RttDisplay = $"{sw.ElapsedMilliseconds} ms";

                                _ = Task.Run(async () =>
                                {
                                    if (reply.Address != null)
                                    {
                                        try
                                        {
                                            var entry = await System.Net.Dns.GetHostEntryAsync(reply.Address);
                                            Dispatcher.Invoke(() =>
                                            {
                                                hopResult.Hostname = entry.HostName;
                                            });
                                        }
                                        catch
                                        {
                                            Dispatcher.Invoke(() =>
                                            {
                                                hopResult.Hostname = "-";
                                            });
                                        }
                                    }
                                });

                                if (reply.Status == System.Net.NetworkInformation.IPStatus.Success)
                                {
                                    destinationReached = true;
                                }
                            }
                            else
                            {
                                hopResult.RttDisplay = reply.Status.ToString();
                            }
                        }
                        catch (Exception ex)
                        {
                            hopResult.RttDisplay = "Error";
                            hopResult.Hostname = ex.Message;
                        }

                        Dispatcher.Invoke(() =>
                        {
                            GridTraceHops.Items.Refresh();
                        });

                        if (destinationReached)
                        {
                            break;
                        }
                    }
                }, token);
            }
            catch (OperationCanceledException) { }
            finally
            {
                BtnStartTrace.Visibility = Visibility.Visible;
                BtnStopTrace.Visibility = Visibility.Collapsed;
                TxtTraceTarget.IsEnabled = true;
            }
        }

        private void BtnStopTrace_Click(object sender, RoutedEventArgs e)
        {
            _traceCts?.Cancel();
            _traceCts?.Dispose();
            _traceCts = null;
        }

        private void Hyperlink_RequestNavigate(object sender, System.Windows.Navigation.RequestNavigateEventArgs e)
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = e.Uri.AbsoluteUri,
                    UseShellExecute = true
                });
                e.Handled = true;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Unable to open link: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ImgLogo_MouseUp(object sender, MouseButtonEventArgs e)
        {
            if (BtnNetScan.Visibility == Visibility.Visible)
            {
                BtnNetScan.Visibility = Visibility.Collapsed;
                BtnPingTrack.Visibility = Visibility.Collapsed;
                BtnTraceroute.Visibility = Visibility.Collapsed;
                BtnPcap.Visibility = Visibility.Collapsed;
                BtnSettings.Visibility = Visibility.Collapsed;
                BtnVoipTools.Visibility = Visibility.Collapsed;
                
                if (PageTabControl.SelectedIndex != 0 && PageTabControl.SelectedIndex != 5 && PageTabControl.SelectedIndex != 6)
                {
                    SelectTab(0, BtnDashboard);
                }
                
                MessageBox.Show("Engineer Mode deactivated.", "Lock", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var passwordBox = new PasswordBox { Margin = new Thickness(10), Width = 200, VerticalAlignment = VerticalAlignment.Center };
            var okButton = new Button { Content = "OK", IsDefault = true, Margin = new Thickness(5), Padding = new Thickness(15, 5, 15, 5) };
            var cancelButton = new Button { Content = "Cancel", IsCancel = true, Margin = new Thickness(5), Padding = new Thickness(15, 5, 15, 5) };
            
            var buttonPanel = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(10) };
            buttonPanel.Children.Add(okButton);
            buttonPanel.Children.Add(cancelButton);

            var mainPanel = new StackPanel { Background = new SolidColorBrush(Color.FromRgb(30, 41, 59)) };
            mainPanel.Children.Add(new TextBlock { Text = "Enter Agilico Engineer Password:", Foreground = Brushes.White, Margin = new Thickness(10), FontSize = 13, FontWeight = FontWeights.SemiBold });
            mainPanel.Children.Add(passwordBox);
            mainPanel.Children.Add(buttonPanel);

            var dialog = new Window
            {
                Title = "Engineer Verification",
                Content = mainPanel,
                SizeToContent = SizeToContent.WidthAndHeight,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = this,
                ResizeMode = ResizeMode.NoResize,
                WindowStyle = WindowStyle.ToolWindow,
                Background = new SolidColorBrush(Color.FromRgb(30, 41, 59))
            };

            okButton.Click += (s, ev) =>
            {
                if (passwordBox.Password.Equals("agilico", StringComparison.OrdinalIgnoreCase))
                {
                    dialog.DialogResult = true;
                    dialog.Close();
                }
                else
                {
                    MessageBox.Show("Invalid password.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            };

            cancelButton.Click += (s, ev) =>
            {
                dialog.DialogResult = false;
                dialog.Close();
            };

            if (dialog.ShowDialog() == true)
            {
                BtnNetScan.Visibility = Visibility.Visible;
                BtnPingTrack.Visibility = Visibility.Visible;
                BtnTraceroute.Visibility = Visibility.Visible;
                BtnPcap.Visibility = Visibility.Visible;
                BtnLogs.Visibility = Visibility.Visible;
                BtnSettings.Visibility = Visibility.Visible;
                BtnVoipTools.Visibility = Visibility.Visible;
                MessageBox.Show("Engineer Mode activated.", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        #endregion

        private void BtnTogglePcap_Click(object sender, RoutedEventArgs e)
        {
            if (_isManualCapturing)
            {
                // Stop Capture
                _engine.Pcap.Stop();
                _isManualCapturing = false;
                TxtTogglePcap.Text = "START CAPTURE";
                BtnTogglePcap.Background = (Brush)FindResource("AccentGreenBrush");
                TxtPcapFilterIp.IsEnabled = true;
            }
            else
            {
                // Start Capture
                string filterIp = TxtPcapFilterIp.Text.Trim();
                if (!string.IsNullOrEmpty(filterIp))
                {
                    // Validate IP address
                    if (!System.Net.IPAddress.TryParse(filterIp, out _))
                    {
                        MessageBox.Show("Please enter a valid IP address to filter by, or leave it blank.", "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }
                }

                try
                {
                    _engine.Pcap.Start(true, string.IsNullOrEmpty(filterIp) ? null : filterIp);
                    _isManualCapturing = true;
                    TxtTogglePcap.Text = "STOP CAPTURE";
                    BtnTogglePcap.Background = (Brush)FindResource("AccentRedBrush");
                    TxtPcapFilterIp.IsEnabled = false;
                }
                catch (UnauthorizedAccessException ex)
                {
                    MessageBox.Show(ex.Message, "Permission Denied", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Failed to start packet capture: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void PcapTimer_Tick(object? sender, EventArgs e)
        {
            UpdatePcapStats();
        }

        private void UpdatePcapStats()
        {
            var pcap = _engine.Pcap;
            TxtPcapPackets.Text = pcap.PacketCount.ToString("N0");
            
            long bytes = pcap.TotalBytes;
            if (bytes < 1024)
                TxtPcapSize.Text = $"{bytes} Bytes";
            else if (bytes < 1024 * 1024)
                TxtPcapSize.Text = $"{(bytes / 1024.0):F1} KB";
            else
                TxtPcapSize.Text = $"{(bytes / (1024.0 * 1024.0)):F2} MB";

            TxtPcapDuration.Text = $"{pcap.DurationSeconds:F1}s";
        }

        private async Task RunStartupSpeedTestAsync()
        {
            TxtLocalDownloadSpeed.Text = "Testing...";
            TxtLocalUploadSpeed.Text = "Testing...";
            TxtLocalDownloadSpeed.Foreground = (Brush)FindResource("TextMutedBrush");
            TxtLocalUploadSpeed.Foreground = (Brush)FindResource("TextMutedBrush");

            try
            {
                _speedTestCts?.Cancel();
                _speedTestCts?.Dispose();
                _speedTestCts = new CancellationTokenSource();
                var token = _speedTestCts.Token;

                var (downloadMbps, uploadMbps) = await RunSpeedTestAsync(token);

                TxtLocalDownloadSpeed.Text = $"{downloadMbps} Mbps";
                TxtLocalUploadSpeed.Text = $"{uploadMbps} Mbps";
                TxtLocalDownloadSpeed.Foreground = (Brush)FindResource("TextDarkBrush");
                TxtLocalUploadSpeed.Foreground = (Brush)FindResource("TextDarkBrush");
            }
            catch (Exception)
            {
                TxtLocalDownloadSpeed.Text = "Skipped/Failed";
                TxtLocalUploadSpeed.Text = "Skipped/Failed";
                TxtLocalDownloadSpeed.Foreground = (Brush)FindResource("AccentRedBrush");
                TxtLocalUploadSpeed.Foreground = (Brush)FindResource("AccentRedBrush");
            }
        }

        #region VoIP and Advanced IT Tools

        private async void BtnResolveSrv_Click(object sender, RoutedEventArgs e)
        {
            var domain = TxtSrvDomain.Text.Trim();
            var serviceItem = CbSrvService.SelectedItem as ComboBoxItem;
            var service = serviceItem?.Content?.ToString() ?? "_sip._udp";

            if (string.IsNullOrWhiteSpace(domain))
            {
                MessageBox.Show("Please enter a valid domain to resolve.", "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            BtnResolveSrv.IsEnabled = false;
            BtnResolveSrv.Content = "RESOLVING...";
            _srvRecords.Clear();

            try
            {
                var records = await VoipTools.ResolveSrvAsync(service, domain);
                foreach (var rec in records)
                {
                    _srvRecords.Add(rec);
                }

                if (records.Count == 0)
                {
                    MessageBox.Show("No DNS SRV records found for the specified domain and service.", "Results", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to resolve SRV records: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                BtnResolveSrv.IsEnabled = true;
                BtnResolveSrv.Content = "RESOLVE SRV";
            }
        }

        private void CbPortProfile_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
        }

        private async void BtnStartPortProbe_Click(object sender, RoutedEventArgs e)
        {
            BtnStartPortProbe.IsEnabled = false;
            BtnStartPortProbe.Content = "PROBING...";
            _portProbeResults.Clear();

            _portProbeCts?.Cancel();
            _portProbeCts?.Dispose();
            _portProbeCts = new CancellationTokenSource();
            var token = _portProbeCts.Token;

            var profileItem = CbPortProfile.SelectedItem as ComboBoxItem;
            var profileName = profileItem?.Content?.ToString() ?? "Agilico Connect Profile";

            try
            {
                List<(string target, int port, string serviceName, string protocol)> probes = new();

                if (profileName.Contains("Agilico"))
                {
                    probes.Add(("customerportal.hp2k.co.uk", 80, "Web Portal HTTP", "TCP"));
                    probes.Add(("customerportal.hp2k.co.uk", 443, "Web Portal HTTPS", "TCP"));
                    probes.Add(("stun-gb-a.hp2k.co.uk", 3478, "STUN Service Primary", "UDP"));
                    probes.Add(("customerportal.hp2k.co.uk", 5060, "SIP UDP Signaling", "UDP"));
                    probes.Add(("customerportal.hp2k.co.uk", 5061, "Secure SIP TLS Signaling", "UDP"));
                    probes.Add(("uk.pool.ntp.org", 123, "NTP Time Server", "UDP"));
                }
                else if (profileName.Contains("3CX"))
                {
                    var target3cx = ShowInputDialog("3CX Server Target", "Enter FQDN or IP of your 3CX Phone System:", "company.3cx.co.uk");
                    if (string.IsNullOrEmpty(target3cx))
                    {
                        RestorePortProbeBtn();
                        return;
                    }
                    probes.Add((target3cx, 5060, "SIP TCP Signaling", "TCP"));
                    probes.Add((target3cx, 5060, "SIP UDP Signaling", "UDP"));
                    probes.Add((target3cx, 5061, "Secure SIP TLS Signaling", "TCP"));
                    probes.Add((target3cx, 5090, "3CX Tunnel (TCP)", "TCP"));
                    probes.Add((target3cx, 5090, "3CX Tunnel (UDP)", "UDP"));
                    probes.Add((target3cx, 443, "Web Client / Provisioning", "TCP"));
                    probes.Add((target3cx, 5015, "3CX System Management", "TCP"));
                }
                else if (profileName.Contains("Microsoft Teams"))
                {
                    probes.Add(("sip.pstnhub.microsoft.com", 5061, "Teams SIP TLS Primary", "TCP"));
                    probes.Add(("sip2.pstnhub.microsoft.com", 5061, "Teams SIP TLS Backup 1", "TCP"));
                    probes.Add(("sip3.pstnhub.microsoft.com", 5061, "Teams SIP TLS Backup 2", "TCP"));
                    probes.Add(("world.tr.teams.microsoft.com", 3478, "Teams Media STUN 3478", "UDP"));
                    probes.Add(("world.tr.teams.microsoft.com", 3479, "Teams Media STUN 3479", "UDP"));
                    probes.Add(("world.tr.teams.microsoft.com", 3480, "Teams Media STUN 3480", "UDP"));
                    probes.Add(("world.tr.teams.microsoft.com", 3481, "Teams Media STUN 3481", "UDP"));
                }
                else // Custom Port Range
                {
                    var customTarget = ShowInputDialog("Custom Probe Target", "Enter target host (IP or FQDN):", "8.8.8.8");
                    if (string.IsNullOrEmpty(customTarget))
                    {
                        RestorePortProbeBtn();
                        return;
                    }
                    var customPortsStr = ShowInputDialog("Custom Ports", "Enter comma-separated ports with protocol (e.g. TCP 80, UDP 53, TCP 443):", "TCP 80, UDP 53");
                    if (string.IsNullOrEmpty(customPortsStr))
                    {
                        RestorePortProbeBtn();
                        return;
                    }

                    var parts = customPortsStr.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
                    foreach (var part in parts)
                    {
                        var trimmed = part.Trim();
                        var tokens = trimmed.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                        if (tokens.Length == 2)
                        {
                            var proto = tokens[0].ToUpper();
                            if ((proto == "TCP" || proto == "UDP") && int.TryParse(tokens[1], out int p) && p > 0 && p <= 65535)
                            {
                                probes.Add((customTarget, p, $"Custom Service {proto}", proto));
                            }
                        }
                        else if (tokens.Length == 1 && int.TryParse(tokens[0], out int p) && p > 0 && p <= 65535)
                        {
                            probes.Add((customTarget, p, "Custom Service TCP", "TCP"));
                        }
                    }

                    if (probes.Count == 0)
                    {
                        MessageBox.Show("No valid ports parsed. Format should be: Protocol Port (e.g., TCP 80).", "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                        RestorePortProbeBtn();
                        return;
                    }
                }

                // Run all probes concurrently and stream results to UI
                var probeTasks = new List<Task<PortProbeResult>>();
                foreach (var p in probes)
                {
                    if (p.protocol == "TCP")
                    {
                        probeTasks.Add(VoipTools.ProbeTcpPortAsync(p.target, p.port, p.serviceName, token));
                    }
                    else
                    {
                        probeTasks.Add(VoipTools.ProbeUdpPortAsync(p.target, p.port, p.serviceName, token));
                    }
                }

                while (probeTasks.Count > 0)
                {
                    var completedTask = await Task.WhenAny(probeTasks);
                    probeTasks.Remove(completedTask);

                    var res = await completedTask;
                    Dispatcher.Invoke(() => _portProbeResults.Add(res));
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                MessageBox.Show($"Error running port probes: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                RestorePortProbeBtn();
            }
        }

        private void RestorePortProbeBtn()
        {
            BtnStartPortProbe.IsEnabled = true;
            BtnStartPortProbe.Content = "PROBE PORTS";
        }

        private string? ShowInputDialog(string title, string instruction, string defaultValue = "")
        {
            var textBox = new TextBox { Margin = new Thickness(10), Width = 250, Text = defaultValue, Padding = new Thickness(5), VerticalAlignment = VerticalAlignment.Center };
            var okButton = new Button { Content = "OK", IsDefault = true, Margin = new Thickness(5), Padding = new Thickness(15, 5, 15, 5) };
            var cancelButton = new Button { Content = "Cancel", IsCancel = true, Margin = new Thickness(5), Padding = new Thickness(15, 5, 15, 5) };
            
            var buttonPanel = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(10) };
            buttonPanel.Children.Add(okButton);
            buttonPanel.Children.Add(cancelButton);

            var mainPanel = new StackPanel { Background = new SolidColorBrush(Color.FromRgb(30, 41, 59)) };
            mainPanel.Children.Add(new TextBlock { Text = instruction, Foreground = Brushes.White, Margin = new Thickness(10), FontSize = 13, FontWeight = FontWeights.SemiBold });
            mainPanel.Children.Add(textBox);
            mainPanel.Children.Add(buttonPanel);

            var dialog = new Window
            {
                Title = title,
                Content = mainPanel,
                SizeToContent = SizeToContent.WidthAndHeight,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = this,
                ResizeMode = ResizeMode.NoResize,
                WindowStyle = WindowStyle.ToolWindow,
                Background = new SolidColorBrush(Color.FromRgb(30, 41, 59))
            };

            okButton.Click += (s, ev) =>
            {
                dialog.DialogResult = true;
                dialog.Close();
            };

            cancelButton.Click += (s, ev) =>
            {
                dialog.DialogResult = false;
                dialog.Close();
            };

            if (dialog.ShowDialog() == true)
            {
                return textBox.Text.Trim();
            }
            return null;
        }

        private void TestRow_MouseUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (sender is Border border && border.Child is Grid grid)
            {
                foreach (var child in grid.Children)
                {
                    if (child is CheckBox chk)
                    {
                        chk.IsChecked = !chk.IsChecked;
                        break;
                    }
                }
            }
        }

        private void ChkSelectAll_Checked(object sender, RoutedEventArgs e)
        {
            SetAllCheckboxes(true);
        }

        private void ChkSelectAll_Unchecked(object sender, RoutedEventArgs e)
        {
            SetAllCheckboxes(false);
        }

        private void SetAllCheckboxes(bool isChecked)
        {
            if (ChkTest1 != null) ChkTest1.IsChecked = isChecked;
            if (ChkTest2 != null) ChkTest2.IsChecked = isChecked;
            if (ChkTest3 != null) ChkTest3.IsChecked = isChecked;
            if (ChkTest4 != null) ChkTest4.IsChecked = isChecked;
            if (ChkTest5 != null) ChkTest5.IsChecked = isChecked;
            if (ChkTest6 != null) ChkTest6.IsChecked = isChecked;
            if (ChkTest7 != null) ChkTest7.IsChecked = isChecked;
            if (ChkTest8 != null) ChkTest8.IsChecked = isChecked;
            if (ChkTest9 != null) ChkTest9.IsChecked = isChecked;
            if (ChkTest10 != null) ChkTest10.IsChecked = isChecked;
        }

        #endregion
    }

    public class TraceHop
    {
        public int HopNumber { get; set; }
        public string IpAddress { get; set; } = string.Empty;
        public string Hostname { get; set; } = string.Empty;
        public string RttDisplay { get; set; } = string.Empty;
    }
}