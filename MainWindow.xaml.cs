using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using System.Linq;
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
        private readonly ObservableCollection<ActiveSocket> _allSockets = new();
        private readonly ObservableCollection<ActiveSocket> _displayedSockets = new();

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
            _navButtons = new[] { BtnDashboard, BtnNetScan, BtnPingTrack, BtnProbe, BtnPcap, BtnHelp, BtnLogs, BtnSettings };
            GridLanDevices.ItemsSource = _lanDevices;
            GridPingLogs.ItemsSource = _pingLogs;
            GridTraceHops.ItemsSource = _traceHops;
            GridSrvRecords.ItemsSource = _srvRecords;
            GridPortProber.ItemsSource = _portProbeResults;
            GridActiveSockets.ItemsSource = _displayedSockets;
            
            // Initialize view
            SelectTab(0, BtnDashboard);
            ResetTestStatuses();
            PanelSummaryDefault.Visibility = Visibility.Visible;
            
            RefreshLocalNetworkInfo();
            InitializePcapAdapters();

            // Sync settings from engine (which auto-loaded from registry where available)
            TxtStunServer.Text = _engine.StunServer;
            TxtStunPort.Text = _engine.StunPort.ToString();
            TxtLocalPort.Text = _engine.LocalSipPort.ToString();
            TxtSipAlgServer.Text = _engine.SipAlgServer;
            TxtSipAlgPort.Text = _engine.SipAlgPort.ToString();
            ChkSimulation.IsChecked = _engine.IsSimulationMode;

            // Trigger firewall permission prompt on load in background so it doesn't block startup
            _ = Task.Run(() => _engine.TriggerFirewallPrompt());

            // Initialize default Probe sub-tab
            SelectProbeTab(0, BtnProbeTrace);

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
        private void BtnProbe_Click(object sender, RoutedEventArgs e)
        {
            SelectTab(3, BtnProbe);
            SelectProbeTab(0, BtnProbeTrace);
        }
        private void BtnPcap_Click(object sender, RoutedEventArgs e) => SelectTab(4, BtnPcap);
        private void BtnHelp_Click(object sender, RoutedEventArgs e) => SelectTab(5, BtnHelp);
        private void BtnLogs_Click(object sender, RoutedEventArgs e) => SelectTab(6, BtnLogs);
        private void BtnSettings_Click(object sender, RoutedEventArgs e) => SelectTab(7, BtnSettings);

        private void SelectProbeTab(int index, Button activeButton)
        {
            ProbeTabControl.SelectedIndex = index;

            var probeButtons = new[] { BtnProbeTrace, BtnProbePorts, BtnProbeDns, BtnProbeSockets };
            foreach (var btn in probeButtons)
            {
                if (btn == activeButton)
                {
                    btn.Background = (Brush)FindResource("SidebarBgBrush");
                    btn.Foreground = Brushes.White;
                }
                else
                {
                    btn.Background = (Brush)FindResource("BorderLightBrush");
                    btn.Foreground = (Brush)FindResource("TextMutedBrush");
                }
            }
        }

        private void BtnProbeTab_Click(object sender, RoutedEventArgs e)
        {
            if (sender == BtnProbeTrace)
            {
                SelectProbeTab(0, BtnProbeTrace);
            }
            else if (sender == BtnProbePorts)
            {
                SelectProbeTab(1, BtnProbePorts);
            }
            else if (sender == BtnProbeDns)
            {
                SelectProbeTab(2, BtnProbeDns);
            }
            else if (sender == BtnProbeSockets)
            {
                SelectProbeTab(3, BtnProbeSockets);
                _ = RefreshSocketsListAsync();
            }
        }

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
            TxtLocalStatus.Text = "Detecting...";
            TxtLocalIp.Text = "Detecting...";
            TxtLocalSubnet.Text = "Detecting...";
            TxtLocalGateway.Text = "Detecting...";
            TxtLocalDns.Text = "Detecting...";
            TxtLocalVlan.Text = "Detecting...";
            TxtPublicIp.Text = "Detecting...";

            _ = Task.Run(async () =>
            {
                var info = _engine.GetLocalNetworkInfo();
                string pubIp = info.PublicIpAddress;

                if (pubIp == "-" || pubIp == "Unknown" || pubIp == "Detecting...")
                {
                    pubIp = await _engine.ResolvePublicIpAsync(CancellationToken.None);
                }

                _ = Dispatcher.BeginInvoke(new Action(() =>
                {
                    TxtLocalStatus.Text = info.Status;
                    TxtLocalIp.Text = info.IpAddress;
                    TxtLocalSubnet.Text = info.SubnetMask;
                    TxtLocalGateway.Text = info.Gateway;
                    TxtLocalDns.Text = info.DnsServers;
                    TxtLocalVlan.Text = info.Vlan;
                    TxtPublicIp.Text = pubIp;

                    if (info.Status.Contains("No ") || info.Status.Contains("Disconnected"))
                    {
                        TxtLocalStatus.Foreground = (System.Windows.Media.Brush)FindResource("AccentRedBrush");
                        TxtLocalStatus.FontWeight = FontWeights.Bold;
                    }
                    else if (info.Status.Contains("VPN"))
                    {
                        TxtLocalStatus.Foreground = (System.Windows.Media.Brush)FindResource("AccentWarningBrush");
                        TxtLocalStatus.FontWeight = FontWeights.Bold;
                    }
                    else
                    {
                        TxtLocalStatus.Foreground = (System.Windows.Media.Brush)FindResource("AccentBlueBrush");
                        TxtLocalStatus.FontWeight = FontWeights.SemiBold;
                    }
                }));
            });
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
                    Dispatcher.BeginInvoke(new Action(() =>
                    {
                        TxtLanScanProgress.Text = $"Scanning subnet ({completed}/{total})...";
                    }));
                }, (dev) =>
                {
                    Dispatcher.BeginInvoke(new Action(() =>
                    {
                        _lanDevices.Add(dev);
                    }));
                }, token);

                // Sort at the end
                _ = Dispatcher.BeginInvoke(new Action(() =>
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
                }));
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
            Dispatcher.BeginInvoke(new Action(() =>
            {
                TxtLogs.AppendText(message + Environment.NewLine);
                TxtLogs.ScrollToEnd();
            }));
        }

        private void Engine_OnProgress(string testName, string status, string details)
        {
            Dispatcher.BeginInvoke(new Action(() =>
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
            }));
        }

        private void Engine_OnComplete(bool success, int score)
        {
            Dispatcher.BeginInvoke(new Action(() =>
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
            }));
        }

        private void UpdateTestUI(int testNum, string status, string details)
        {
            var pending = FindName($"Test{testNum}IconPending") as UIElement;
            var running = FindName($"Test{testNum}IconRunning") as UIElement;
            var spinner = FindName($"Test{testNum}Spinner") as UIElement;
            var pass = FindName($"Test{testNum}IconPass") as UIElement;
            var fail = FindName($"Test{testNum}IconFail") as UIElement;
            var warning = FindName($"Test{testNum}IconWarning") as UIElement;
            var helpLink = FindName($"Test{testNum}InfoLink") as UIElement;
            var text = FindName($"Test{testNum}Details") as TextBlock;

            if (text != null) text.Text = details;

            if (pending != null) pending.Visibility = Visibility.Collapsed;
            if (running != null) running.Visibility = Visibility.Collapsed;
            if (spinner != null) spinner.Visibility = Visibility.Collapsed;
            if (pass != null) pass.Visibility = Visibility.Collapsed;
            if (fail != null) fail.Visibility = Visibility.Collapsed;
            if (warning != null) warning.Visibility = Visibility.Collapsed;
            if (helpLink != null) helpLink.Visibility = Visibility.Collapsed;

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
                case "warning":
                case "warn":
                    if (warning != null) warning.Visibility = Visibility.Visible;
                    if (helpLink != null) helpLink.Visibility = Visibility.Visible;
                    break;
                case "failed":
                case "fail":
                    if (fail != null) fail.Visibility = Visibility.Visible;
                    if (helpLink != null) helpLink.Visibility = Visibility.Visible;
                    break;
            }
        }

        private void BtnTestHelp_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string url)
            {
                try
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = url,
                        UseShellExecute = true
                    });
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Unable to open link: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
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

        #region Settings Repair Actions

        /// <summary>
        /// Shared helper: gracefully close then kill any running Agilico Connect process.
        /// Returns the exe path of the process if one was found.
        /// </summary>
        private static string? ForceCloseAgilicoConnect()
        {
            string? exePath = null;
            foreach (var p in System.Diagnostics.Process.GetProcesses())
            {
                try
                {
                    string name = p.ProcessName.ToLower();
                    if (name.Contains("agilico") && !name.Contains("diagnostic") && !name.Contains("checker"))
                    {
                        try { exePath = p.MainModule?.FileName; } catch { }
                        // Attempt graceful close first
                        p.CloseMainWindow();
                        if (!p.WaitForExit(2000))
                        {
                            // Force-kill if still alive
                            p.Kill();
                            p.WaitForExit(2000);
                        }
                    }
                }
                catch { }
            }
            return exePath;
        }

        private void BtnResetConnect_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // 1. Force-close any running Agilico Connect process
                ForceCloseAgilicoConnect();

                // 2. Clear AppData Local cache directory
                string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                string targetDir = System.IO.Path.Combine(localAppData, "AgilicoConnectV5forWindows");
                bool cacheCleared = false;

                if (System.IO.Directory.Exists(targetDir))
                {
                    try
                    {
                        System.IO.Directory.Delete(targetDir, true);
                        cacheCleared = true;
                    }
                    catch (System.IO.IOException)
                    {
                        // Fallback: delete files individually, skip locked files
                        DeleteDirectoryContents(targetDir);
                        cacheCleared = true;
                    }
                }

                string msg = cacheCleared
                    ? "Agilico Connect has been closed and its cache cleared successfully.\n\nPlease restart Agilico Connect manually."
                    : "Agilico Connect has been closed.\n\nNo cache directory was found to clear.";

                MessageBox.Show(msg, "Reset Connect", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Reset Connect failed: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnClearCache_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // 1. Gracefully close then force-kill running instances and record path
                string? exePath = ForceCloseAgilicoConnect();

                // 2. Clear AppData Local folder
                string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                string targetDir = System.IO.Path.Combine(localAppData, "AgilicoConnectV5forWindows");

                if (System.IO.Directory.Exists(targetDir))
                {
                    try
                    {
                        System.IO.Directory.Delete(targetDir, true);
                    }
                    catch (System.IO.IOException)
                    {
                        // Fallback: delete files individually, skip locked files
                        DeleteDirectoryContents(targetDir);
                    }
                }

                // 3. Restart if path was found
                bool restarted = false;
                if (!string.IsNullOrEmpty(exePath) && System.IO.File.Exists(exePath))
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = exePath,
                        UseShellExecute = true
                    });
                    restarted = true;
                }

                string msg = restarted 
                    ? "Agilico Connect desktop application was closed, cache cleared, and application restarted successfully."
                    : "Agilico Connect cache cleared successfully.\n\nPlease start the Agilico Connect desktop application manually.";
                
                MessageBox.Show(msg, "Cache Cleared", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to clear cache: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private static void DeleteDirectoryContents(string path)
        {
            foreach (string file in System.IO.Directory.GetFiles(path))
            {
                try { System.IO.File.Delete(file); } catch { }
            }
            foreach (string dir in System.IO.Directory.GetDirectories(path))
            {
                try { System.IO.Directory.Delete(dir, true); } catch { }
            }
        }

        private void BtnFlushDns_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var psi = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "ipconfig",
                    Arguments = "/flushdns",
                    CreateNoWindow = true,
                    UseShellExecute = false
                };
                using var p = System.Diagnostics.Process.Start(psi);
                p?.WaitForExit();
                MessageBox.Show("DNS resolver cache flushed successfully.", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to flush DNS cache: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnResetStack_Click(object sender, RoutedEventArgs e)
        {
            var result = MessageBox.Show(
                "Resetting the TCP/IP stack and Winsock catalog requires administrator privileges and a system restart to take effect.\n\nDo you want to proceed?",
                "Administrator Authorization Required",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result != MessageBoxResult.Yes) return;

            try
            {
                var psi = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = "/c netsh int ip reset & netsh winsock reset",
                    Verb = "runas",
                    CreateNoWindow = true,
                    UseShellExecute = true
                };
                using var p = System.Diagnostics.Process.Start(psi);
                p?.WaitForExit();
                MessageBox.Show("Winsock catalog and TCP/IP stack reset successfully. Please restart your computer for changes to take effect.", "Reset Complete", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Action cancelled or failed: {ex.Message}", "Status", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void BtnRepairFirewall_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var psi = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = "/c netsh advfirewall firewall add rule name=\"Agilico Connect Diagnostic Tool\" dir=in action=allow protocol=UDP localport=5060,5061,10000-20000 profile=any",
                    Verb = "runas",
                    CreateNoWindow = true,
                    UseShellExecute = true
                };
                using var p = System.Diagnostics.Process.Start(psi);
                p?.WaitForExit();
                MessageBox.Show("Inbound Windows Defender Firewall rules for SIP and RTP ports have been created successfully.", "Firewall Repaired", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Action cancelled or failed: {ex.Message}", "Status", MessageBoxButton.OK, MessageBoxImage.Warning);
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
            Dispatcher.BeginInvoke(new Action(() =>
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
            }));
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
            client.Timeout = TimeSpan.FromSeconds(15);
            // Use a standard browser User-Agent to avoid blocks/403s on Cloudflare
            client.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");

            // 1. Download Test
            try
            {
                using var ctsDownload = CancellationTokenSource.CreateLinkedTokenSource(token);
                ctsDownload.CancelAfter(TimeSpan.FromSeconds(3));
                var downloadToken = ctsDownload.Token;

                long totalDownloaded = 0;
                var sw = System.Diagnostics.Stopwatch.StartNew();

                // Periodic UI update task
                var uiUpdateTask = Task.Run(async () =>
                {
                    while (!downloadToken.IsCancellationRequested)
                    {
                        try
                        {
                            await Task.Delay(200, downloadToken);
                            double elapsed = sw.Elapsed.TotalSeconds;
                            if (elapsed > 0)
                            {
                                long currentBytes = System.Threading.Interlocked.Read(ref totalDownloaded);
                                double mbps = (currentBytes * 8.0) / (elapsed * 1000000.0);
                                _ = Dispatcher.BeginInvoke(() =>
                                {
                                    TxtLocalDownloadSpeed.Text = $"{mbps:F1} Mbps (Testing...)";
                                });
                            }
                        }
                        catch (OperationCanceledException) { break; }
                        catch { }
                    }
                }, downloadToken);

                // Run 4 concurrent download workers with dynamic failover support
                string[] downloadUrls = new string[]
                {
                    "https://speed.cloudflare.com/__down?bytes=52428800",
                    "https://cachefly.cachefly.net/50mb.test",
                    "http://ipv4.download.thinkbroadband.com/50MB.zip"
                };
                int currentUrlIndex = 0;
                object urlLock = new object();

                var downloadTasks = new List<Task>();
                for (int i = 0; i < 4; i++)
                {
                    int workerId = i;
                    downloadTasks.Add(Task.Run(async () =>
                    {
                        while (!downloadToken.IsCancellationRequested)
                        {
                            string targetUrl;
                            lock (urlLock)
                            {
                                targetUrl = downloadUrls[currentUrlIndex];
                            }

                            try
                            {
                                using var response = await client.GetAsync(targetUrl, System.Net.Http.HttpCompletionOption.ResponseHeadersRead, downloadToken);
                                
                                if (!response.IsSuccessStatusCode)
                                {
                                    lock (urlLock)
                                    {
                                        if (currentUrlIndex < downloadUrls.Length - 1 && downloadUrls[currentUrlIndex] == targetUrl)
                                        {
                                            currentUrlIndex++;
                                            System.Diagnostics.Debug.WriteLine($"[DL Worker {workerId}] Failover status {response.StatusCode} on {targetUrl}. Falling back to: {downloadUrls[currentUrlIndex]}");
                                        }
                                    }
                                    continue;
                                }

                                using var stream = await response.Content.ReadAsStreamAsync(downloadToken);
                                byte[] buffer = new byte[65536]; // 64KB
                                int bytesRead;
                                while ((bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length, downloadToken)) > 0)
                                {
                                    System.Threading.Interlocked.Add(ref totalDownloaded, bytesRead);
                                }
                            }
                            catch (OperationCanceledException) { break; }
                            catch (Exception ex)
                            {
                                System.Diagnostics.Debug.WriteLine($"[DL Worker {workerId}] Error: {ex.Message}");
                                lock (urlLock)
                                {
                                    if (currentUrlIndex < downloadUrls.Length - 1 && downloadUrls[currentUrlIndex] == targetUrl)
                                    {
                                        currentUrlIndex++;
                                        System.Diagnostics.Debug.WriteLine($"[DL Worker {workerId}] Failover on exception on {targetUrl}. Falling back to: {downloadUrls[currentUrlIndex]}");
                                    }
                                }
                                try { await Task.Delay(100, downloadToken); } catch { break; } // Avoid tight loop on error
                            }
                        }
                    }, downloadToken));
                }

                await Task.WhenAll(downloadTasks);
                sw.Stop();
                try { await uiUpdateTask; } catch { }

                double finalElapsed = sw.Elapsed.TotalSeconds;
                if (finalElapsed > 0)
                {
                    downloadMbps = (System.Threading.Interlocked.Read(ref totalDownloaded) * 8.0) / (finalElapsed * 1000000.0);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Download test failed: {ex.Message}");
            }

            // Update UI to show final download speed or prepare upload
            _ = Dispatcher.BeginInvoke(() =>
            {
                TxtLocalDownloadSpeed.Text = $"{downloadMbps:F1} Mbps";
                TxtLocalDownloadSpeed.Foreground = (Brush)FindResource("TextDarkBrush");
            });

            // 2. Upload Test (4 streams * 25MB = 100MB)
            try
            {
                using var ctsUpload = CancellationTokenSource.CreateLinkedTokenSource(token);
                ctsUpload.CancelAfter(TimeSpan.FromSeconds(3));
                var uploadToken = ctsUpload.Token;

                long totalUploaded = 0;
                var sw = System.Diagnostics.Stopwatch.StartNew();

                // Periodic UI update task
                var uiUpdateTask = Task.Run(async () =>
                {
                    while (!uploadToken.IsCancellationRequested)
                    {
                        try
                        {
                            await Task.Delay(200, uploadToken);
                            double elapsed = sw.Elapsed.TotalSeconds;
                            if (elapsed > 0)
                            {
                                long currentBytes = System.Threading.Interlocked.Read(ref totalUploaded);
                                double mbps = (currentBytes * 8.0) / (elapsed * 1000000.0);
                                _ = Dispatcher.BeginInvoke(() =>
                                {
                                    TxtLocalUploadSpeed.Text = $"{mbps:F1} Mbps (Testing...)";
                                });
                            }
                        }
                        catch (OperationCanceledException) { break; }
                        catch { }
                    }
                }, uploadToken);

                // Run 4 concurrent upload workers using the shared client
                var uploadTasks = new List<Task>();
                byte[] uploadBuffer = new byte[26214400]; // 25MB
                Random.Shared.NextBytes(uploadBuffer);

                for (int i = 0; i < 4; i++)
                {
                    int workerId = i;
                    uploadTasks.Add(Task.Run(async () =>
                    {
                        try
                        {
                            using var memoryStream = new System.IO.MemoryStream(uploadBuffer);
                            using var progressStream = new ProgressStream(memoryStream, (bytesRead) =>
                            {
                                System.Threading.Interlocked.Add(ref totalUploaded, bytesRead);
                            });
                            using var content = new System.Net.Http.StreamContent(progressStream);
                            var response = await client.PostAsync("https://speed.cloudflare.com/__up", content, uploadToken);
                            response.EnsureSuccessStatusCode();
                        }
                        catch (OperationCanceledException) { }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Debug.WriteLine($"Upload worker {workerId} error: {ex.Message}");
                        }
                    }, uploadToken));
                }

                await Task.WhenAll(uploadTasks);
                sw.Stop();
                try { await uiUpdateTask; } catch { }

                double finalElapsed = sw.Elapsed.TotalSeconds;
                if (finalElapsed > 0)
                {
                    uploadMbps = (System.Threading.Interlocked.Read(ref totalUploaded) * 8.0) / (finalElapsed * 1000000.0);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Upload test failed: {ex.Message}");
            }

            _ = Dispatcher.BeginInvoke(() =>
            {
                TxtLocalUploadSpeed.Text = $"{uploadMbps:F1} Mbps";
                TxtLocalUploadSpeed.Foreground = (Brush)FindResource("TextDarkBrush");
            });

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

                        _ = Dispatcher.BeginInvoke(new Action(() => _traceHops.Add(hopResult)));

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
                                        var ipStr = reply.Address.ToString();
                                        var hostTask = System.Net.Dns.GetHostEntryAsync(reply.Address);
                                        var geoTask = LookupGeoIpAndAsnAsync(ipStr);

                                        string hostname = "-";
                                        try
                                        {
                                            var entry = await hostTask;
                                            hostname = entry.HostName;
                                        }
                                        catch { }

                                        var (asn, location) = await geoTask;

                                        _ = Dispatcher.BeginInvoke(new Action(() =>
                                        {
                                            hopResult.Hostname = hostname;
                                            hopResult.Asn = asn;
                                            hopResult.Location = location;
                                            GridTraceHops.Items.Refresh();
                                        }));
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

                        _ = Dispatcher.BeginInvoke(new Action(() =>
                        {
                            GridTraceHops.Items.Refresh();
                        }));

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
                BtnPcap.Visibility = Visibility.Collapsed;
                BtnSettings.Visibility = Visibility.Collapsed;
                BtnProbe.Visibility = Visibility.Collapsed;
                
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

            var mainPanel = new StackPanel { Background = new SolidColorBrush(Color.FromRgb(0, 0, 52)) };
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
                Background = new SolidColorBrush(Color.FromRgb(0, 0, 52))
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
                BtnPcap.Visibility = Visibility.Visible;
                BtnLogs.Visibility = Visibility.Visible;
                BtnSettings.Visibility = Visibility.Visible;
                BtnProbe.Visibility = Visibility.Visible;
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
                CboPcapAdapter.IsEnabled = true;
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
                string? selectedIp = null;
                bool isInactive = false;
                if (CboPcapAdapter.SelectedItem is AdapterItem selectedItem)
                {
                    selectedIp = selectedItem.IpAddress;
                    isInactive = !selectedItem.IsActive && !selectedItem.IsAutomatic;
                    
                    if (!selectedItem.IsAutomatic && string.IsNullOrEmpty(selectedIp))
                    {
                        MessageBox.Show("The selected network adapter does not have a configured IPv4 address.", "No IPv4 Configured", MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }
                }
 
                if (isInactive)
                {
                    MessageBox.Show("The selected network adapter is inactive. Please choose an active adapter to capture traffic.", "Adapter Inactive", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                try
                {
                    _engine.Pcap.Start(true, string.IsNullOrEmpty(filterIp) ? null : filterIp, string.IsNullOrEmpty(selectedIp) ? null : selectedIp);
                    _isManualCapturing = true;
                    TxtTogglePcap.Text = "STOP CAPTURE";
                    BtnTogglePcap.Background = (Brush)FindResource("AccentRedBrush");
                    TxtPcapFilterIp.IsEnabled = false;
                    CboPcapAdapter.IsEnabled = false;
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

        private void InitializePcapAdapters()
        {
            try
            {
                CboPcapAdapter.Items.Clear();
                
                // Add default/automatic option
                CboPcapAdapter.Items.Add(new AdapterItem 
                { 
                    Name = "Automatic (Detect Active)", 
                    IpAddress = "", 
                    StatusColor = "#3b82f6", // Blue
                    IsActive = true,
                    IsAutomatic = true 
                });

                foreach (var ni in System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (ni.NetworkInterfaceType == System.Net.NetworkInformation.NetworkInterfaceType.Loopback) continue;

                    bool isActive = ni.OperationalStatus == System.Net.NetworkInformation.OperationalStatus.Up;
                    string color = isActive ? "#22c55e" : "#ef4444"; // Green or Red

                    var ips = ni.GetIPProperties().UnicastAddresses;
                    bool hasIpv4 = false;

                    foreach (var ua in ips)
                    {
                        if (ua.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                        {
                            hasIpv4 = true;
                            CboPcapAdapter.Items.Add(new AdapterItem
                            {
                                Name = $"{ni.Name} ({ua.Address})",
                                IpAddress = ua.Address.ToString(),
                                StatusColor = color,
                                IsActive = isActive,
                                IsAutomatic = false
                            });
                        }
                    }

                    if (!hasIpv4)
                    {
                        CboPcapAdapter.Items.Add(new AdapterItem
                        {
                            Name = $"{ni.Name} (No IPv4)",
                            IpAddress = "",
                            StatusColor = color,
                            IsActive = isActive,
                            IsAutomatic = false
                        });
                    }
                }

                if (CboPcapAdapter.Items.Count > 0)
                {
                    CboPcapAdapter.SelectedIndex = 0;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to load network adapters: {ex.Message}");
            }
        }

        public class AdapterItem
        {
            public string Name { get; set; } = string.Empty;
            public string IpAddress { get; set; } = string.Empty;
            public string StatusColor { get; set; } = "#94a3b8"; // Slate gray default
            public bool IsActive { get; set; }
            public bool IsAutomatic { get; set; }
            public override string ToString() => Name;
        }

        private async Task RunStartupSpeedTestAsync()
        {
            if (BtnRecheckSpeed != null) BtnRecheckSpeed.IsEnabled = false;
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
            finally
            {
                if (BtnRecheckSpeed != null) BtnRecheckSpeed.IsEnabled = true;
            }
        }

        private async void BtnRecheckSpeed_Click(object sender, RoutedEventArgs e)
        {
            await RunStartupSpeedTestAsync();
        }

        private void BtnRaiseTicket_Click(object sender, RoutedEventArgs e)
        {
            string subject = $"Agilico Connect Check Failure - {Environment.MachineName}";
            
            string body = "Please detail your issue here:\n\n\n\n" +
                          "--------------------------------------------------\n" +
                          "DIAGNOSTIC TEST SUMMARY:\n" +
                          $"{TxtScoreFail.Text}\n\n" +
                          $"{TxtFailInstructions.Text.Replace("\r", "")}\n" +
                          "--------------------------------------------------\n";
            
            string logContent = TxtLogs.Text;
            byte[] pcapBytes = _engine.Pcap.GetPcapBytes();
            string pingTarget = _pingTracker.CurrentTarget;
            Action<string> pingLogExporter = (path) => _pingTracker.ExportLog(path);

            var ticketDialog = new TicketDialog(subject, body, logContent, pcapBytes, pingTarget, pingLogExporter, new System.Collections.Generic.List<LanDevice>(_lanDevices))
            {
                Owner = this
            };

            ticketDialog.ShowDialog();
        }

        #region VoIP and Advanced IT Tools

        private async void BtnResolveSrv_Click(object sender, RoutedEventArgs e)
        {
            var domain = TxtSrvDomain.Text.Trim();
            var serviceItem = CbSrvService.SelectedItem as ComboBoxItem;
            var service = serviceItem?.Tag?.ToString() ?? "_sip._udp";

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
                    _ = Dispatcher.BeginInvoke(new Action(() => _portProbeResults.Add(res)));
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

        #region Active Socket Monitor & GeoIP Lookup Actions

        private static async Task<(string Asn, string Location)> LookupGeoIpAndAsnAsync(string ipAddress)
        {
            if (string.IsNullOrEmpty(ipAddress) || ipAddress == "*" || ipAddress == "-")
                return ("-", "-");

            if (IsPrivateIp(ipAddress))
                return ("Private Address", "Local Network");

            try
            {
                using var client = new System.Net.Http.HttpClient();
                client.Timeout = TimeSpan.FromSeconds(3);
                client.DefaultRequestHeaders.Add("User-Agent", "AgilicoNetworkDiagnosticTool/3.5.9");

                string url = $"http://ip-api.com/json/{ipAddress}?fields=status,message,country,city,as";
                string json = await client.GetStringAsync(url);

                using var jsonDoc = System.Text.Json.JsonDocument.Parse(json);
                var root = jsonDoc.RootElement;

                if (root.TryGetProperty("status", out var statusProp) && statusProp.GetString() == "success")
                {
                    string country = root.TryGetProperty("country", out var countryProp) ? countryProp.GetString() ?? "" : "";
                    string city = root.TryGetProperty("city", out var cityProp) ? cityProp.GetString() ?? "" : "";
                    string asField = root.TryGetProperty("as", out var asProp) ? asProp.GetString() ?? "" : "";

                    string location = (!string.IsNullOrEmpty(city) && !string.IsNullOrEmpty(country))
                        ? $"{city}, {country}"
                        : (!string.IsNullOrEmpty(country) ? country : "-");

                    string asn = "-";
                    if (!string.IsNullOrEmpty(asField))
                    {
                        int spaceIndex = asField.IndexOf(' ');
                        if (spaceIndex > 0)
                        {
                            string asnPart = asField.Substring(0, spaceIndex);
                            string orgPart = asField.Substring(spaceIndex + 1);
                            asn = $"{asnPart} ({orgPart})";
                        }
                        else
                        {
                            asn = asField;
                        }
                    }

                    return (asn, location);
                }
            }
            catch
            {
                // Silence exceptions and fall back
            }

            return ("-", "-");
        }

        private static bool IsPrivateIp(string ipAddress)
        {
            if (System.Net.IPAddress.TryParse(ipAddress, out var ip))
            {
                if (ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                {
                    byte[] bytes = ip.GetAddressBytes();
                    if (bytes.Length == 4)
                    {
                        if (bytes[0] == 10) return true;
                        if (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31) return true;
                        if (bytes[0] == 192 && bytes[1] == 168) return true;
                        if (bytes[0] == 127) return true;
                        if (bytes[0] == 169 && bytes[1] == 254) return true;
                    }
                }
                else if (ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6)
                {
                    if (ip.IsIPv6LinkLocal || System.Net.IPAddress.IsLoopback(ip) || ip.IsIPv6SiteLocal)
                        return true;

                    string ipStr = ip.ToString().ToLower();
                    if (ipStr.StartsWith("fc00") || ipStr.StartsWith("fd00"))
                        return true;
                }
            }
            return false;
        }

        private async Task<List<ActiveSocket>> GetActiveSocketsAsync()
        {
            var sockets = new List<ActiveSocket>();

            try
            {
                var psi = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "netstat.exe",
                    Arguments = "-ano",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    CreateNoWindow = true
                };

                using var process = System.Diagnostics.Process.Start(psi);
                if (process == null) return sockets;

                string output = await process.StandardOutput.ReadToEndAsync();
                await process.WaitForExitAsync();

                // Build a cache of PIDs to Process Names
                var processes = System.Diagnostics.Process.GetProcesses();
                var pidMap = new Dictionary<int, string>();
                foreach (var p in processes)
                {
                    pidMap[p.Id] = p.ProcessName;
                }

                string[] lines = output.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);
                foreach (string line in lines)
                {
                    string trimmed = line.Trim();
                    if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith("Active") || trimmed.StartsWith("Proto"))
                        continue;

                    string[] parts = trimmed.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length < 4) continue;

                    string proto = parts[0].ToUpper();
                    if (proto != "TCP" && proto != "UDP") continue;

                    string localEp = parts[1];
                    string remoteEp = parts[2];
                    string state = string.Empty;
                    int pid = 0;

                    if (proto == "TCP")
                    {
                        if (parts.Length >= 5)
                        {
                            state = parts[3];
                            int.TryParse(parts[4], out pid);
                        }
                    }
                    else // UDP
                    {
                        state = "-";
                        int.TryParse(parts[3], out pid);
                    }

                    // Split IP and Port
                    string localIp = localEp;
                    int localPort = 0;
                    int lastColonLocal = localEp.LastIndexOf(':');
                    if (lastColonLocal >= 0)
                    {
                        localIp = localEp.Substring(0, lastColonLocal);
                        int.TryParse(localEp.Substring(lastColonLocal + 1), out localPort);
                    }

                    string remoteIp = remoteEp;
                    string remotePort = "*";
                    int lastColonRemote = remoteEp.LastIndexOf(':');
                    if (lastColonRemote >= 0)
                    {
                        remoteIp = remoteEp.Substring(0, lastColonRemote);
                        remotePort = remoteEp.Substring(lastColonRemote + 1);
                    }

                    pidMap.TryGetValue(pid, out string? procName);
                    if (string.IsNullOrEmpty(procName))
                    {
                        if (pid == 0) procName = "System Idle Process";
                        else if (pid == 4) procName = "System";
                        else procName = "Unknown";
                    }

                    sockets.Add(new ActiveSocket
                    {
                        Protocol = proto,
                        LocalAddress = localIp,
                        LocalPort = localPort,
                        RemoteAddress = remoteIp,
                        RemotePort = remotePort,
                        State = state,
                        Pid = pid,
                        ProcessName = procName
                    });
                }
            }
            catch { }

            return sockets;
        }

        private async Task RefreshSocketsListAsync()
        {
            if (BtnRefreshSockets != null) BtnRefreshSockets.IsEnabled = false;

            var list = await GetActiveSocketsAsync();

            _allSockets.Clear();
            foreach (var s in list)
            {
                _allSockets.Add(s);
            }

            ApplySocketFilters();

            if (BtnRefreshSockets != null) BtnRefreshSockets.IsEnabled = true;
        }

        private void ApplySocketFilters()
        {
            if (GridActiveSockets == null) return;

            string search = TxtSocketSearch.Text.Trim().ToLower();
            string protoFilter = (ComboSocketProtocol.SelectedItem is ComboBoxItem selectedItem)
                ? selectedItem.Content.ToString()?.ToUpper() ?? "ALL"
                : "ALL";
            bool agilicoOnly = ChkAgilicoOnly.IsChecked == true;

            var filtered = _allSockets.Where(s =>
            {
                if (protoFilter != "ALL" && s.Protocol != protoFilter) return false;
                if (agilicoOnly && !s.ProcessName.ToLower().Contains("agilico")) return false;

                if (!string.IsNullOrEmpty(search))
                {
                    bool match = s.ProcessName.ToLower().Contains(search) ||
                                 s.LocalAddress.Contains(search) ||
                                 s.LocalPort.ToString().Contains(search) ||
                                 s.RemoteAddress.Contains(search) ||
                                 s.RemotePort.Contains(search) ||
                                 s.Pid.ToString().Contains(search) ||
                                 s.State.ToLower().Contains(search);
                    if (!match) return false;
                }

                return true;
            }).ToList();

            _displayedSockets.Clear();
            foreach (var s in filtered)
            {
                _displayedSockets.Add(s);
            }
        }

        private void TxtSocketSearch_TextChanged(object sender, TextChangedEventArgs e)
        {
            ApplySocketFilters();
        }

        private void ComboSocketProtocol_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            ApplySocketFilters();
        }

        private void ChkAgilicoOnly_Changed(object sender, RoutedEventArgs e)
        {
            ApplySocketFilters();
        }

        private async void BtnRefreshSockets_Click(object sender, RoutedEventArgs e)
        {
            await RefreshSocketsListAsync();
        }

        #endregion
    }

    public class TraceHop
    {
        public int HopNumber { get; set; }
        public string IpAddress { get; set; } = string.Empty;
        public string Hostname { get; set; } = string.Empty;
        public string RttDisplay { get; set; } = string.Empty;
        public string Asn { get; set; } = string.Empty;
        public string Location { get; set; } = string.Empty;
    }

    public class ActiveSocket
    {
        public string ProcessName { get; set; } = "System";
        public int Pid { get; set; }
        public string Protocol { get; set; } = "TCP";
        public string LocalAddress { get; set; } = string.Empty;
        public int LocalPort { get; set; }
        public string RemoteAddress { get; set; } = string.Empty;
        public string RemotePort { get; set; } = string.Empty;
        public string State { get; set; } = string.Empty;
    }

    public class ProgressStream : System.IO.Stream
    {
        private readonly System.IO.Stream _innerStream;
        private readonly System.Action<long> _onBytesRead;

        public ProgressStream(System.IO.Stream innerStream, System.Action<long> onBytesRead)
        {
            _innerStream = innerStream;
            _onBytesRead = onBytesRead;
        }

        public override bool CanRead => _innerStream.CanRead;
        public override bool CanSeek => _innerStream.CanSeek;
        public override bool CanWrite => _innerStream.CanWrite;
        public override long Length => _innerStream.Length;
        public override long Position { get => _innerStream.Position; set => _innerStream.Position = value; }

        public override void Flush() => _innerStream.Flush();
        public override int Read(byte[] buffer, int offset, int count)
        {
            int read = _innerStream.Read(buffer, offset, count);
            _onBytesRead?.Invoke(read);
            return read;
        }
        public override async System.Threading.Tasks.Task<int> ReadAsync(byte[] buffer, int offset, int count, System.Threading.CancellationToken cancellationToken)
        {
            int read = await _innerStream.ReadAsync(buffer, offset, count, cancellationToken);
            _onBytesRead?.Invoke(read);
            return read;
        }
        public override long Seek(long offset, System.IO.SeekOrigin origin) => _innerStream.Seek(offset, origin);
        public override void SetLength(long value) => _innerStream.SetLength(value);
        public override void Write(byte[] buffer, int offset, int count) => _innerStream.Write(buffer, offset, count);
    }
}