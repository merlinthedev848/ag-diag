using System;
using System.IO;
using System.Security.Cryptography;
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

namespace agilicomsptoolkit
{
    public partial class MainWindow : Window
    {
        private readonly NetworkEngine _engine;
        private readonly LanScanner _lanScanner;
        private readonly ObservableCollection<LanDevice> _lanDevices;
        private ObservableCollection<PingTargetItem> _pingTargets = new ObservableCollection<PingTargetItem>();
        private PingTargetItem? _selectedPingTarget;
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

        private double _lastDownloadMbps = 0.0;
        private double _lastUploadMbps = 0.0;

        public MainWindow()
        {
            InitializeComponent();
            Logger.Log("MainWindow loaded.");
            _engine = new NetworkEngine();
            _lanScanner = new LanScanner();
            _lanDevices = new ObservableCollection<LanDevice>();

            
            _pcapTimer = new System.Windows.Threading.DispatcherTimer();
            _pcapTimer.Interval = TimeSpan.FromMilliseconds(500);
            _pcapTimer.Tick += PcapTimer_Tick;
            
            // Wire up engine events
            _engine.OnLog += Engine_OnLog;
            _engine.OnProgress += Engine_OnProgress;
            _engine.OnComplete += Engine_OnComplete;


            Loaded += MainWindow_Loaded;
        }

        // Custom Titlebar Handlers for Glassmorphism 
        private void Window_MouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (e.ChangedButton == System.Windows.Input.MouseButton.Left)
                this.DragMove();
        }

        private void BtnMinimize_Click(object sender, RoutedEventArgs e)
        {
            this.WindowState = WindowState.Minimized;
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            LogAuditAction("Application started successfully. MainWindow loaded.");
            try
            {
#if FULL_VERSION
                _navButtons = new[] { BtnDashboard, BtnItTools, BtnNetTools, BtnConverter, BtnHelp, BtnLogs, BtnSettings };
#else
                _navButtons = new[] { BtnDashboard, BtnItTools, BtnNetTools, BtnHelp, BtnLogs, BtnSettings };
                TabConverter.Visibility = Visibility.Collapsed;
#endif
                GridLanDevices.ItemsSource = _lanDevices;
                GridPingTargets.ItemsSource = _pingTargets;
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

                // Trigger startup speed test asynchronously
                _ = RunStartupSpeedTestAsync();

                // Initialize default Probe sub-tab
                SelectProbeTab(0, BtnProbeTrace);

                // Detect and set version/title based on Standard vs Lite mode
                try
                {
                    string procName = System.Diagnostics.Process.GetCurrentProcess().ProcessName;
                    bool isLite = procName.Contains("Lite", StringComparison.OrdinalIgnoreCase);
                    string mode = isLite ? "Lite" : "Standard";
                    TxtVersion.Text = $"v4.1.0 ({mode})";
                    TxtTitleBar.Text = "Agilico MSP Toolkit";
                }
                catch { }
            }
            catch (Exception ex)
            {
                ModernMessageBox.Show($"Failed to initialize the application on startup.\n\nError: {ex.Message}", "Startup Error", MessageBoxButton.OK, MessageBoxImage.Error);
                System.Diagnostics.Debug.WriteLine($"Startup Error: {ex.Message}");
            }
        }

        #region Navigation

        private void SelectTab(int index, Button activeButton)
        {
            PageTabControl.SelectedIndex = index;
            
            var fadeIn = new System.Windows.Media.Animation.DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(250));
            var slideUp = new System.Windows.Media.Animation.ThicknessAnimation(new Thickness(0, 15, 0, -15), new Thickness(0), TimeSpan.FromMilliseconds(250))
            {
                EasingFunction = new System.Windows.Media.Animation.CubicEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut }
            };
            
            PageTabControl.BeginAnimation(UIElement.OpacityProperty, fadeIn);
            PageTabControl.BeginAnimation(FrameworkElement.MarginProperty, slideUp);

            foreach (var btn in _navButtons)
            {
                btn.Background = Brushes.Transparent;
                SetStripeVisibility(btn, Visibility.Collapsed);
            }

            if (activeButton != null)
            {
                activeButton.Background = (Brush)FindResource("SidebarItemHoverBrush");
                SetStripeVisibility(activeButton, Visibility.Visible);
            }

            // Manage PCAP stats real-time polling. PCAP is active if Network Tools tab (2) is open AND sub-tab PCAP (3) is open.
            if (index == 2 && NetToolsTabControl != null && NetToolsTabControl.SelectedIndex == 3)
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

        private void BtnDashboard_Click(object sender, RoutedEventArgs e) { Logger.Log("Nav: Dashboard"); SelectTab(0, BtnDashboard); }
        private void BtnItTools_Click(object sender, RoutedEventArgs e) { Logger.Log("Nav: IT Tools"); SelectTab(1, BtnItTools); }
        private void BtnNetTools_Click(object sender, RoutedEventArgs e) { Logger.Log("Nav: Network Tools"); SelectTab(2, BtnNetTools); }

        private async void BtnHardwareScan_Click(object sender, RoutedEventArgs e)
        {
            Logger.Log("Action: Hardware Scan initiated.");
            try
            {
                var items = await HardwareChecker.RunDiagnosticsAsync();
                Dispatcher.Invoke(() =>
                {
                    var hwDialog = new HardwareReportDialog(items);
                    hwDialog.Owner = this;
                    hwDialog.ShowDialog();
                });
            }
            catch (Exception ex)
            {
                Logger.Log($"Error: Hardware Scan failed - {ex.Message}");
                ModernMessageBox.Show($"Failed to run hardware check.\n\nError: {ex.Message}", "Hardware Scan Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        
        private void BtnSubNetScan_Click(object sender, RoutedEventArgs e)
        {
            LogAuditAction("Nav: Network Tools > Subnet Scan");
            NetToolsTabControl.SelectedIndex = 0;
            UpdateSubNavButtons((System.Windows.Controls.Button)sender);
        }

        private void BtnSubPingTrack_Click(object sender, RoutedEventArgs e)
        {
            LogAuditAction("Nav: Network Tools > Ping Tracker");
            NetToolsTabControl.SelectedIndex = 1;
            UpdateSubNavButtons((System.Windows.Controls.Button)sender);
        }

        private void BtnSubProbe_Click(object sender, RoutedEventArgs e)
        {
            LogAuditAction("Nav: Network Tools > Probe");
            NetToolsTabControl.SelectedIndex = 2;
            UpdateSubNavButtons((System.Windows.Controls.Button)sender);
        }

        private void BtnSubPcap_Click(object sender, RoutedEventArgs e)
        {
            LogAuditAction("Nav: Network Tools > Packet Capture");
            NetToolsTabControl.SelectedIndex = 3;
            UpdateSubNavButtons((System.Windows.Controls.Button)sender);
            
            // Start PCAP polling if we switched to it
            UpdatePcapStats();
            _pcapTimer.Start();
        }

        private void UpdateSubNavButtons(System.Windows.Controls.Button activeButton)
        {
            var buttons = new[] { BtnSubNetScan, BtnSubPingTrack, BtnSubProbe, BtnSubPcap };
            foreach (var btn in buttons)
            {
                if (btn != null)
                {
                    btn.Style = btn == activeButton 
                        ? (Style)FindResource("ActionButtonStyle") 
                        : (Style)FindResource("SecondaryButtonStyle");
                }
            }
        }
        private void BtnHelp_Click(object sender, RoutedEventArgs e) { Logger.Log("Nav: Help"); SelectTab(4, BtnHelp); }
        private void BtnLogs_Click(object sender, RoutedEventArgs e) { Logger.Log("Nav: Logs"); SelectTab(5, BtnLogs); }
        private void BtnSettings_Click(object sender, RoutedEventArgs e) { Logger.Log("Nav: Settings"); SelectTab(6, BtnSettings); }

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
            Logger.Log("Application closing.");
            try
            {
                _engine.Cancel();
                foreach (var t in _pingTargets) t.Stop();
                _pcapTimer.Stop();
                _lanScanCts?.Cancel();
                _lanScanCts?.Dispose();
                _speedTestCts?.Cancel();
                _speedTestCts?.Dispose();
                _portProbeCts?.Cancel();
                _portProbeCts?.Dispose();
            }
            catch { }

            Environment.Exit(0);
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
            TxtLocalWifi.Text = "Detecting...";
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
                    TxtLocalWifi.Text = info.WifiInfo;
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
            try
            {
                LogAuditAction("Started Network Readiness Diagnostics.");
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

                // Pass last speed test result into engine so it appears in the diagnostic log
                _engine.LastDownloadMbps = _lastDownloadMbps;
                _engine.LastUploadMbps = _lastUploadMbps;

                // Run
                await _engine.RunDiagnosticsAsync();
            }
            catch (Exception ex)
            {
                ModernMessageBox.Show($"Failed to run diagnostics.\n\nError: {ex.Message}", "Diagnostics Error", MessageBoxButton.OK, MessageBoxImage.Error);
                RestoreControlButtons();
            }
        }

        private async void BtnStartLanScan_Click(object sender, RoutedEventArgs e)
        {
            LogAuditAction("Started local network (LAN) scan.");
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
                LogAuditAction($"LAN scan completed. Found {_lanDevices.Count} devices.");
            }
            catch (OperationCanceledException)
            {
                LogAuditAction("Cancelled local network (LAN) scan.");
            }

            PanelLanScanProgress.Visibility = Visibility.Collapsed;
            BtnStartLanScan.IsEnabled = true;
        }

        private void BtnStop_Click(object sender, RoutedEventArgs e)
        {
            LogAuditAction("Cancelled Network Readiness Diagnostics.");
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
                ModernMessageBox.Show("Please enter a valid STUN Server address.", "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }
            _engine.StunServer = TxtStunServer.Text.Trim();

            // STUN Port
            if (!int.TryParse(TxtStunPort.Text, out int stunPort) || stunPort <= 0 || stunPort > 65535)
            {
                ModernMessageBox.Show("Please enter a valid STUN Port (1-65535).", "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }
            _engine.StunPort = stunPort;

            // Local SIP Port
            if (!int.TryParse(TxtLocalPort.Text, out int localPort) || localPort <= 0 || localPort > 65535)
            {
                ModernMessageBox.Show("Please enter a valid Local SIP Port (1-65535).", "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }
            _engine.LocalSipPort = localPort;

            // SIP ALG Host
            if (string.IsNullOrWhiteSpace(TxtSipAlgServer.Text))
            {
                ModernMessageBox.Show("Please enter a valid SIP ALG Server address.", "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }
            _engine.SipAlgServer = TxtSipAlgServer.Text.Trim();

            // SIP ALG Port
            if (!int.TryParse(TxtSipAlgPort.Text, out int sipAlgPort) || sipAlgPort <= 0 || sipAlgPort > 65535)
            {
                ModernMessageBox.Show("Please enter a valid SIP ALG Port (1-65535).", "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }
            _engine.SipAlgPort = sipAlgPort;

            // Simulation mode
            _engine.IsSimulationMode = ChkSimulation.IsChecked == true;

            LogAuditAction($"Applied Network Settings - STUN: {_engine.StunServer}:{_engine.StunPort}, Local SIP Port: {_engine.LocalSipPort}, SIP ALG Server: {_engine.SipAlgServer}:{_engine.SipAlgPort}, Simulation: {_engine.IsSimulationMode}");
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
            LogAuditAction($"Network Readiness Diagnostics completed. Score: {score}/100, Success: {success}");
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
                    ModernMessageBox.Show($"Unable to open link: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        #endregion

        #region Audit Logging

        public void LogAuditAction(string actionText)
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                string logMsg = $"[{DateTime.Now:HH:mm:ss}]   AUDIT: {actionText}";
                TxtLogs.AppendText(logMsg + Environment.NewLine);
                TxtLogs.ScrollToEnd();
            }));

            try
            {
                string appDataDir = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AgilicoToolkit");
                Directory.CreateDirectory(appDataDir);
                string logFile = System.IO.Path.Combine(appDataDir, "audit_log.txt");
                string fileMsg = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] AUDIT: {actionText}{Environment.NewLine}";
                File.AppendAllText(logFile, fileMsg);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to write to audit log file: {ex.Message}");
            }
        }

        #endregion

        #region Log Tab Actions

        private void BtnClearLog_Click(object sender, RoutedEventArgs e)
        {
            LogAuditAction("Action: Cleared in-app log.");
            TxtLogs.Clear();
        }

        private void BtnSaveLog_Click(object sender, RoutedEventArgs e)
        {
            LogAuditAction("Action: Save log initiated.");
            if (string.IsNullOrWhiteSpace(TxtLogs.Text))
            {
                ModernMessageBox.Show("Log is empty. Run a diagnostic test first to generate logs.", "Information", MessageBoxButton.OK, MessageBoxImage.Information);
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
                    LogAuditAction($"Action: Log saved to {dialog.FileName}");
                    ModernMessageBox.Show("Log saved successfully.", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    LogAuditAction($"Error: Failed to save log - {ex.Message}");
                    ModernMessageBox.Show($"Failed to save log: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
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
            int currentPid = System.Diagnostics.Process.GetCurrentProcess().Id;
            foreach (var p in System.Diagnostics.Process.GetProcesses())
            {
                try
                {
                    if (p.Id == currentPid) continue;

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
                LogAuditAction("Executed Reset Agilico Connect application routine (terminated softphone process, deleted local AppData cache, and cleared registry subkeys).");
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
                    catch
                    {
                        // Fallback: delete files individually, skip locked files
                        DeleteDirectoryContents(targetDir);
                        cacheCleared = true;
                    }
                }

                // 3. Clear softphone registry configuration
                bool registryCleared = false;
                try
                {
                    Microsoft.Win32.Registry.CurrentUser.DeleteSubKeyTree(@"Software\DMC\WindowsSoftphone\v1", false);
                    registryCleared = true;
                }
                catch { }

                string msg = "";
                if (cacheCleared || registryCleared)
                {
                    msg = "Agilico Connect has been closed, registry settings removed, and its cache cleared successfully.\n\nPlease restart Agilico Connect manually.";
                }
                else
                {
                    msg = "Agilico Connect has been closed.\n\nNo cache directory or registry configuration was found to clear.";
                }

                ModernMessageBox.Show(msg, "Reset Connect", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                ModernMessageBox.Show($"Reset Connect failed: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
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



        #endregion

        #region Ping Track Tab Actions

        private static readonly System.Text.RegularExpressions.Regex HostnameRegex = new(
            @"^([a-zA-Z0-9]([a-zA-Z0-9\-]{0,61}[a-zA-Z0-9])?\.)*[a-zA-Z0-9]([a-zA-Z0-9\-]{0,61}[a-zA-Z0-9])?$",
            System.Text.RegularExpressions.RegexOptions.Compiled);

        private void BtnAddPingTarget_Click(object sender, RoutedEventArgs e)
        {
            var target = TxtAddPingTarget.Text.Trim();
            if (string.IsNullOrWhiteSpace(target))
            {
                ModernMessageBox.Show("Please enter a valid IP address or hostname.", "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // Validate hostname or IP address format
            bool isValidIp = System.Net.IPAddress.TryParse(target, out _);
            bool isValidHostname = !isValidIp && target.Length <= 253 && HostnameRegex.IsMatch(target);
            if (!isValidIp && !isValidHostname)
            {
                ModernMessageBox.Show("Please enter a valid IPv4/IPv6 address or RFC-compliant hostname.", "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            int intervalMs = 1000;
            if (ComboAddPingInterval.SelectedItem is ComboBoxItem selectedItem && selectedItem.Tag != null)
            {
                if (int.TryParse(selectedItem.Tag.ToString(), out int parsedInterval))
                {
                    intervalMs = parsedInterval;
                }
            }

            // Check if already exists
            if (_pingTargets.Any(t => t.Target.Equals(target, StringComparison.OrdinalIgnoreCase)))
            {
                ModernMessageBox.Show("This host is already in the tracking list.", "Duplicate", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var newItem = new PingTargetItem(target, intervalMs);
            newItem.OnPingResultReceived += TargetItem_OnPingResultReceived;
            _pingTargets.Add(newItem);
            LogAuditAction($"Added ping target: {target}");
            
            // Start pinging immediately — don't wait for the user to press "Start All"
            newItem.Start();

            // Auto-select the newly added host so the graph appears straight away
            GridPingTargets.SelectedItem = newItem;

            TxtAddPingTarget.Text = string.Empty;
            UpdatePingKpis();
        }
        
        private void TargetItem_OnPingResultReceived(object? sender, PingResultEventArgs e)
        {
            if (sender is PingTargetItem item)
            {
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    if (GridPingTargets.SelectedItem == item)
                    {
                        _currentPingStats = e.Stats;
                        DrawPingGraph(item.Tracker.GetRecentResults(), e.Stats);
                    }
                }));
                UpdatePingKpis();
            }
        }

        private void GridPingTargets_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            _selectedPingTarget = GridPingTargets.SelectedItem as PingTargetItem;
            if (_selectedPingTarget != null)
            {
                TxtPingGraphTarget.Text = _selectedPingTarget.Target;
                // Load the real accumulated stats from the tracker for this host
                var allResults = _selectedPingTarget.Tracker.GetAllResults();
                _currentPingStats = allResults.Count > 0
                    ? _selectedPingTarget.Tracker.GetCurrentStats()
                    : new PingStats();
                DrawPingGraph(_selectedPingTarget.Tracker.GetRecentResults(), _currentPingStats);
            }
            else
            {
                TxtPingGraphTarget.Text = "Select a host above";
                PingGraphCanvas.Children.Clear();
            }
        }

        private void BtnPingTargetToggle_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is PingTargetItem item)
            {
                if (item.Tracker.IsRunning)
                {
                    LogAuditAction($"Stopped continuous ping to target: {item.Target}");
                    item.Stop();
                }
                else
                {
                    LogAuditAction($"Started continuous ping to target: {item.Target}");
                    item.Start();
                }
                UpdatePingKpis();
            }
        }
        
        private void BtnPingTargetRemove_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is PingTargetItem item)
            {
                LogAuditAction($"Removed ping target: {item.Target}");
                item.Stop();
                item.OnPingResultReceived -= TargetItem_OnPingResultReceived;
                _pingTargets.Remove(item);
                
                if (_selectedPingTarget == item)
                {
                    _selectedPingTarget = null;
                    TxtPingGraphTarget.Text = "Select a host above";
                    PingGraphCanvas.Children.Clear();
                }
                UpdatePingKpis();
            }
        }
        
        private void BtnPingStartAll_Click(object sender, RoutedEventArgs e)
        {
            LogAuditAction("Started continuous pinging on all targets.");
            foreach (var item in _pingTargets) item.Start();
            UpdatePingKpis();
        }

        private void BtnPingStopAll_Click(object sender, RoutedEventArgs e)
        {
            LogAuditAction("Stopped continuous pinging on all targets.");
            foreach (var item in _pingTargets) item.Stop();
            UpdatePingKpis();
        }

        private void UpdatePingKpis()
        {
            if (Dispatcher.CheckAccess() == false)
            {
                Dispatcher.BeginInvoke(new Action(UpdatePingKpis));
                return;
            }

            int total = _pingTargets.Count;
            int online = _pingTargets.Count(t => t.Status == "Online");
            int offline = _pingTargets.Count(t => t.Status == "Offline/Timeout" || t.Status == "Timeout" || (t.Tracker.IsRunning && t.CurrentLatency.Contains("Timeout")));
            
            // Calculate average latency from all active pings
            double totalLatency = 0;
            int latencyCount = 0;
            foreach (var target in _pingTargets)
            {
                var recent = target.Tracker.GetRecentResults();
                if (recent.Count > 0)
                {
                    var valid = recent.Where(r => r.LatencyMs.HasValue).Select(r => (double)r.LatencyMs!.Value).ToList();
                    if (valid.Count > 0)
                    {
                        totalLatency += valid.Average();
                        latencyCount++;
                    }
                }
            }
            double overallAvg = latencyCount > 0 ? (totalLatency / latencyCount) : 0;

            if (TxtKpiTotalTargets != null) TxtKpiTotalTargets.Text = total.ToString();
            if (TxtKpiOnlineTargets != null) TxtKpiOnlineTargets.Text = online.ToString();
            if (TxtKpiOfflineTargets != null) TxtKpiOfflineTargets.Text = offline.ToString();
            if (TxtKpiAvgLatency != null) TxtKpiAvgLatency.Text = overallAvg > 0 ? $"{Math.Round(overallAvg, 1)} ms" : "0.0 ms";
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
            if (_selectedPingTarget != null)
            {
                DrawPingGraph(_selectedPingTarget.Tracker.GetRecentResults(), _currentPingStats);
            }
        }

        private void BtnPingExportSelected_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedPingTarget == null)
            {
                ModernMessageBox.Show("Please select a host from the list first.", "Information", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            
            var pings = _selectedPingTarget.Tracker.GetAllResults();
            if (pings.Count == 0)
            {
                ModernMessageBox.Show("No ping tracking data to download for this host.", "Information", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                Filter = "CSV Files (*.csv)|*.csv|All Files (*.*)|*.*",
                FileName = $"PingLog_{_selectedPingTarget.Target}_{DateTime.Now:yyyyMMdd_HHmmss}.csv"
            };

            if (dialog.ShowDialog() == true)
            {
                try
                {
                    _selectedPingTarget.Tracker.ExportLog(dialog.FileName);
                    ModernMessageBox.Show("Ping track log saved successfully.", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    ModernMessageBox.Show($"Error saving log: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        #endregion

        #region Extra Tools and Handlers

        private void BtnDownloadPcap_Click(object sender, RoutedEventArgs e)
        {
            if (_engine.Pcap.PacketCount == 0)
            {
                ModernMessageBox.Show("There are no captured packets to save. Please run a packet capture first.", "No Packets Captured", MessageBoxButton.OK, MessageBoxImage.Warning);
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
                    LogAuditAction($"Exported captured network packets (PCAP) to: {sfd.FileName}");
                    ModernMessageBox.Show("PCAP log saved successfully.", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    ModernMessageBox.Show($"Failed to save PCAP log: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private async Task<(double downloadMbps, double uploadMbps)> RunSpeedTestAsync(CancellationToken token)
        {
            if (_engine.IsSimulationMode)
            {
                await Task.Delay(2000, token);
                var rand = new Random();
                return (Math.Round(500 + rand.NextDouble() * 100, 1), Math.Round(500 + rand.NextDouble() * 100, 1));
            }

            double downloadMbps = 0;
            double uploadMbps = 0;

            // Configure HttpClient to disable automatic decompression (prevents Accept-Encoding CPU compression bottleneck)
            var handler = new System.Net.Http.HttpClientHandler
            {
                AutomaticDecompression = System.Net.DecompressionMethods.None
            };
            using var client = new System.Net.Http.HttpClient(handler);
            client.Timeout = TimeSpan.FromSeconds(15);
            client.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");

            // 1. Download Test
            try
            {
                using var ctsDownload = CancellationTokenSource.CreateLinkedTokenSource(token);
                ctsDownload.CancelAfter(TimeSpan.FromSeconds(6));
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

                // Run 4 concurrent download workers distributed across different CDNs to prevent rate-limiting
                string[] downloadUrls = new string[]
                {
                    "https://speed.cloudflare.com/__down?bytes=100000000",
                    "https://cachefly.cachefly.net/100mb.test",
                    "http://ipv4.download.thinkbroadband.com/100MB.zip",
                    "https://speed.cloudflare.com/__down?bytes=100000000"
                };

                var downloadTasks = new List<Task>();
                for (int i = 0; i < 4; i++)
                {
                    int workerId = i;
                    string targetUrl = downloadUrls[workerId % downloadUrls.Length];

                    downloadTasks.Add(Task.Run(async () =>
                    {
                        byte[] buffer = new byte[131072]; // 128KB buffer for high throughput
                        while (!downloadToken.IsCancellationRequested)
                        {
                            try
                            {
                                using var response = await client.GetAsync(targetUrl, System.Net.Http.HttpCompletionOption.ResponseHeadersRead, downloadToken);
                                if (!response.IsSuccessStatusCode)
                                {
                                    await Task.Delay(100, downloadToken);
                                    continue;
                                }

                                using var stream = await response.Content.ReadAsStreamAsync(downloadToken);
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
                                try { await Task.Delay(200, downloadToken); } catch { break; }
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

            // Update UI to show final download speed
            _ = Dispatcher.BeginInvoke(() =>
            {
                TxtLocalDownloadSpeed.Text = $"{downloadMbps:F1} Mbps";
                TxtLocalDownloadSpeed.Foreground = (Brush)FindResource("TextDarkBrush");
            });

            // 2. Upload Test (continuous 6-second upload window)
            try
            {
                using var ctsUpload = CancellationTokenSource.CreateLinkedTokenSource(token);
                ctsUpload.CancelAfter(TimeSpan.FromSeconds(6));
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

                // Run 4 concurrent upload workers using the shared client in a continuous loop
                var uploadTasks = new List<Task>();
                byte[] uploadBuffer = new byte[16777216]; // 16MB buffer payload per request
                Random.Shared.NextBytes(uploadBuffer);

                for (int i = 0; i < 4; i++)
                {
                    int workerId = i;
                    uploadTasks.Add(Task.Run(async () =>
                    {
                        while (!uploadToken.IsCancellationRequested)
                        {
                            try
                            {
                                using var memoryStream = new System.IO.MemoryStream(uploadBuffer);
                                using var progressStream = new ProgressStream(memoryStream, (bytesRead) =>
                                {
                                    System.Threading.Interlocked.Add(ref totalUploaded, bytesRead);
                                });
                                using var content = new System.Net.Http.StreamContent(progressStream);
                                using var request = new System.Net.Http.HttpRequestMessage(System.Net.Http.HttpMethod.Post, "https://speed.cloudflare.com/__up")
                                {
                                    Content = content
                                };
                                
                                // Do NOT set TransferEncodingChunked = true (enables Content-Length header for high-performance streaming)
                                using var response = await client.SendAsync(request, uploadToken);
                                response.EnsureSuccessStatusCode();
                            }
                            catch (OperationCanceledException) { break; }
                            catch (Exception ex)
                            {
                                System.Diagnostics.Debug.WriteLine($"Upload worker {workerId} error: {ex.Message}");
                                try { await Task.Delay(200, uploadToken); } catch { break; }
                            }
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
                ModernMessageBox.Show("Please enter a valid IP address or hostname.", "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            bool isValidIp = System.Net.IPAddress.TryParse(target, out _);
            bool isValidHostname = !isValidIp && target.Length <= 253 && HostnameRegex.IsMatch(target);
            if (!isValidIp && !isValidHostname)
            {
                ModernMessageBox.Show("Please enter a valid IPv4/IPv6 address or RFC-compliant hostname.", "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            BtnStartTrace.Visibility = Visibility.Collapsed;
            BtnStopTrace.Visibility = Visibility.Visible;
            TxtTraceTarget.IsEnabled = false;
            _traceHops.Clear();

            _traceCts?.Cancel();
            _traceCts?.Dispose();
            var localCts = new CancellationTokenSource();
            _traceCts = localCts;
            var token = localCts.Token;

            try
            {
                LogAuditAction($"Started network path trace to: {target}");
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
                                    if (token.IsCancellationRequested) return;
                                    if (reply.Address != null)
                                    {
                                        var ipStr = reply.Address.ToString();
                                        try
                                        {
                                            var hostTask = System.Net.Dns.GetHostEntryAsync(reply.Address);
                                            var geoTask = LookupGeoIpAndAsnAsync(ipStr);

                                            string hostname = "-";
                                            try
                                            {
                                                var entry = await hostTask;
                                                hostname = entry.HostName;
                                            }
                                            catch { }

                                            if (token.IsCancellationRequested) return;

                                            var (asn, location) = await geoTask;

                                            if (token.IsCancellationRequested) return;

                                            _ = Dispatcher.BeginInvoke(new Action(() =>
                                            {
                                                if (token.IsCancellationRequested) return;
                                                hopResult.Hostname = hostname;
                                                hopResult.Asn = asn;
                                                hopResult.Location = location;
                                                GridTraceHops.Items.Refresh();
                                            }));
                                        }
                                        catch { }
                                    }
                                }, token);

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
                LogAuditAction($"Network path trace to {target} completed.");
            }
            catch (OperationCanceledException)
            {
                LogAuditAction("Cancelled network path trace.");
            }
            finally
            {
                localCts.Dispose();
                if (_traceCts == localCts)
                {
                    _traceCts = null;
                }
                BtnStartTrace.Visibility = Visibility.Visible;
                BtnStopTrace.Visibility = Visibility.Collapsed;
                TxtTraceTarget.IsEnabled = true;
            }
        }

        private void BtnStopTrace_Click(object sender, RoutedEventArgs e)
        {
            _traceCts?.Cancel();
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
                ModernMessageBox.Show($"Unable to open link: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ImgLogo_MouseUp(object sender, MouseButtonEventArgs e)
        {
            if (BtnItTools.Visibility == Visibility.Visible)
            {
                BtnItTools.Visibility = Visibility.Collapsed;
                BtnNetTools.Visibility = Visibility.Collapsed;
                BtnSettings.Visibility = Visibility.Collapsed;
#if FULL_VERSION
                BtnConverter.Visibility = Visibility.Collapsed;
#endif
                
                if (PageTabControl.SelectedIndex == 1 || PageTabControl.SelectedIndex == 2 || 
                    PageTabControl.SelectedIndex == 3 || PageTabControl.SelectedIndex == 6)
                {
                    SelectTab(0, BtnDashboard);
                }
                
                ModernMessageBox.Show("Engineer Mode deactivated.", "Lock", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var dialog = new EngineerPasswordDialog
            {
                Owner = this
            };

            if (dialog.ShowDialog() == true)
            {
                // Compare against SHA-256 hash of the engineer password (not the plaintext)
                const string HashedEngineerPassword = "d71dd957a89911bd0b4e8182e2ffe6284b89e4354eb0b0a36ae0e8c9ee4dfa7d";
                using var sha = SHA256.Create();
                byte[] inputHash = sha.ComputeHash(Encoding.UTF8.GetBytes(dialog.Password));
                string inputHex = BitConverter.ToString(inputHash).Replace("-", "").ToLowerInvariant();

                if (inputHex == HashedEngineerPassword)
                {
                    BtnItTools.Visibility = Visibility.Visible;
                    BtnNetTools.Visibility = Visibility.Visible;
                    BtnLogs.Visibility = Visibility.Visible;
                    BtnSettings.Visibility = Visibility.Visible;
#if FULL_VERSION
                    BtnConverter.Visibility = Visibility.Visible;
#endif
                    ModernMessageBox.Show("Engineer Mode activated.", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else
                {
                    ModernMessageBox.Show("Invalid password.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        #endregion

        private void BtnTogglePcap_Click(object sender, RoutedEventArgs e)
        {
            if (_isManualCapturing)
            {
                // Stop Capture
                _engine.Pcap.Stop();
                LogAuditAction("Stopped manual packet capture (PCAP) sniffer.");
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
                        ModernMessageBox.Show("Please enter a valid IP address to filter by, or leave it blank.", "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
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
                        ModernMessageBox.Show("The selected network adapter does not have a configured IPv4 address.", "No IPv4 Configured", MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }
                }
 
                if (isInactive)
                {
                    ModernMessageBox.Show("The selected network adapter is inactive. Please choose an active adapter to capture traffic.", "Adapter Inactive", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                try
                {
                    _engine.Pcap.Start(true, string.IsNullOrEmpty(filterIp) ? null : filterIp, string.IsNullOrEmpty(selectedIp) ? null : selectedIp);
                    LogAuditAction("Started manual packet capture (PCAP) sniffer.");
                    _isManualCapturing = true;
                    TxtTogglePcap.Text = "STOP CAPTURE";
                    BtnTogglePcap.Background = (Brush)FindResource("AccentRedBrush");
                    TxtPcapFilterIp.IsEnabled = false;
                    CboPcapAdapter.IsEnabled = false;
                }
                catch (UnauthorizedAccessException ex)
                {
                    ModernMessageBox.Show(ex.Message, "Permission Denied", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
                catch (Exception ex)
                {
                    ModernMessageBox.Show($"Failed to start packet capture: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
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
                CboPcapAdapter.SelectedIndex = 0;

                // Load the rest asynchronously so we don't block the UI thread during startup
                _ = Task.Run(() => 
                {
                    try
                    {
                        var interfaces = System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces();
                        var adapterItems = new System.Collections.Generic.List<AdapterItem>();

                        foreach (var ni in interfaces)
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
                                    adapterItems.Add(new AdapterItem
                                    {
                                        Name = ni.Name,
                                        IpAddress = ua.Address.ToString(),
                                        StatusColor = color,
                                        IsActive = isActive,
                                        IsAutomatic = false
                                    });
                                }
                            }

                            if (!hasIpv4)
                            {
                                adapterItems.Add(new AdapterItem
                                {
                                    Name = ni.Name,
                                    IpAddress = "No IPv4",
                                    StatusColor = color,
                                    IsActive = isActive,
                                    IsAutomatic = false
                                });
                            }
                        }

                        _ = Dispatcher.BeginInvoke(new Action(() => 
                        {
                            foreach (var item in adapterItems)
                            {
                                CboPcapAdapter.Items.Add(item);
                            }
                        }));
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Failed to async load PCAP adapters: {ex.Message}");
                    }
                });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to init PCAP adapters: {ex.Message}");
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
            if (SpeedTestProgress != null) SpeedTestProgress.Visibility = Visibility.Visible;

            try
            {
                _speedTestCts?.Cancel();
                _speedTestCts?.Dispose();
                _speedTestCts = new CancellationTokenSource();
                var token = _speedTestCts.Token;

                var (downloadMbps, uploadMbps) = await RunSpeedTestAsync(token);

                _lastDownloadMbps = downloadMbps;
                _lastUploadMbps = uploadMbps;

                LogAuditAction($"Bandwidth speed test completed. Download: {downloadMbps:F1} Mbps, Upload: {uploadMbps:F1} Mbps");

                TxtLocalDownloadSpeed.Text = $"{downloadMbps:F1} Mbps";
                TxtLocalUploadSpeed.Text = $"{uploadMbps:F1} Mbps";
                TxtLocalDownloadSpeed.Foreground = (Brush)FindResource("TextDarkBrush");
                TxtLocalUploadSpeed.Foreground = (Brush)FindResource("TextDarkBrush");
                
                string logMsg = $"[{DateTime.Now:HH:mm:ss}]   Speed Test: Download {downloadMbps:F1} Mbps | Upload {uploadMbps:F1} Mbps";
                TxtLogs.AppendText(logMsg + Environment.NewLine);
                TxtLogs.ScrollToEnd();
            }
            catch (Exception ex)
            {
                TxtLocalDownloadSpeed.Text = "Skipped/Failed";
                TxtLocalUploadSpeed.Text = "Skipped/Failed";
                TxtLocalDownloadSpeed.Foreground = (Brush)FindResource("AccentRedBrush");
                TxtLocalUploadSpeed.Foreground = (Brush)FindResource("AccentRedBrush");
                
                string logMsg = $"[{DateTime.Now:HH:mm:ss}]   Speed Test: Skipped/Failed ({ex.Message})";
                TxtLogs.AppendText(logMsg + Environment.NewLine);
                TxtLogs.ScrollToEnd();
            }
            finally
            {
                if (BtnRecheckSpeed != null) BtnRecheckSpeed.IsEnabled = true;
                if (SpeedTestProgress != null) SpeedTestProgress.Visibility = Visibility.Collapsed;
            }
        }

        private async void BtnRecheckSpeed_Click(object sender, RoutedEventArgs e)
        {
            await RunStartupSpeedTestAsync();
        }

        private void BtnRaiseTicket_Click(object sender, RoutedEventArgs e)
        {
            LogAuditAction("Launched Raise Support Ticket dialog.");
            string subject = $"Agilico Connect Check Failure - {Environment.MachineName}";
            
            string body = "Please detail your issue here:\n\n\n\n" +
                          "--------------------------------------------------\n" +
                          "DIAGNOSTIC TEST SUMMARY:\n" +
                          $"{TxtScoreFail.Text}\n\n" +
                          $"{TxtFailInstructions.Text.Replace("\r", "")}\n" +
                          "--------------------------------------------------\n";
            
            string logContent = TxtLogs.Text;
            byte[] pcapBytes = _engine.Pcap.GetPcapBytes();
            string pingTarget = _selectedPingTarget != null ? _selectedPingTarget.Target : "None";
            Action<string> pingLogExporter = (path) => _selectedPingTarget?.Tracker.ExportLog(path);

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
                ModernMessageBox.Show("Please enter a valid domain to resolve.", "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            BtnResolveSrv.IsEnabled = false;
            BtnResolveSrv.Content = "RESOLVING...";
            _srvRecords.Clear();
            LogAuditAction($"Executed DNS SRV resolution for Service: {service}, Domain: {domain}");

            try
            {
                var records = await VoipTools.ResolveSrvAsync(service, domain);
                foreach (var rec in records)
                {
                    _srvRecords.Add(rec);
                }

                if (records.Count == 0)
                {
                    ModernMessageBox.Show("No DNS SRV records found for the specified domain and service.", "Results", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                ModernMessageBox.Show($"Failed to resolve SRV records: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                BtnResolveSrv.IsEnabled = true;
                BtnResolveSrv.Content = "RESOLVE SRV";
            }
        }

        private void CbPortProfile_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (TxtPortProfileDesc == null) return;

            var profileItem = CbPortProfile.SelectedItem as ComboBoxItem;
            var profileName = profileItem?.Content?.ToString() ?? "";

            TxtPortProfileDesc.Text = profileName switch
            {
                string s when s == "Agilico Connect" =>
                    "Tests HTTP (TCP 80), HTTPS (TCP 443), STUN (UDP 3478), SIP signalling (UDP 5060/5061), and NTP (UDP 123) against Agilico Connect endpoints.",
                string s when s.Contains("Linphone") =>
                    "Runs a PBX-style firewall check against Agilico service hosts and Linphone SIP targets: DNS, STUN, SIP ALG, SIP signalling, web services, NTP, and summarized even-port RTP media ranges.",
                string s when s.Contains("Teams") =>
                    "Tests SIP TLS signalling (TCP 5061) to three Microsoft PSTN hubs, and STUN/TURN media ports (UDP 3478–3481) to the Teams transport relay.",
                string s when s.Contains("Custom") =>
                    "Enter a custom target host and a list of ports with protocols (e.g. TCP 443, UDP 53) when prompted.",
                _ => ""
            };
        }

        private static string GetHostFromUrl(string value)
        {
            if (Uri.TryCreate(value, UriKind.Absolute, out var uri) && !string.IsNullOrWhiteSpace(uri.Host))
            {
                return uri.Host;
            }

            return value.Trim()
                .Replace("https://", "", StringComparison.OrdinalIgnoreCase)
                .Replace("http://", "", StringComparison.OrdinalIgnoreCase)
                .Split('/')[0];
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
            LogAuditAction($"Started port connectivity probe using profile: {profileName}");

            try
            {
                List<Func<CancellationToken, Task<PortProbeResult>>> probes = new();

                if (profileName == "Agilico Connect")
                {
                    probes.Add(ct => VoipTools.ProbeTcpPortAsync("customerportal.hp2k.co.uk", 80, "Web Portal HTTP", ct));
                    probes.Add(ct => VoipTools.ProbeTcpPortAsync("customerportal.hp2k.co.uk", 443, "Web Portal HTTPS", ct));
                    probes.Add(ct => VoipTools.ProbeUdpPortAsync("stun-gb-a.hp2k.co.uk", 3478, "STUN Service Primary", ct));
                    probes.Add(ct => VoipTools.ProbeUdpPortAsync("sip.linphone.org", 5060, "Linphone SIP UDP Signalling", ct));
                    probes.Add(ct => VoipTools.ProbeTcpPortAsync("sip.linphone.org", 5060, "Linphone SIP TCP Signalling", ct));
                    probes.Add(ct => VoipTools.ProbeUdpPortAsync("uk.pool.ntp.org", 123, "NTP Time Server", ct, treatTimeoutAsOpen: true));
                }
                else if (profileName.Contains("Linphone"))
                {
                    string[] agilicoHosts =
                    {
                        "customerportal.hp2k.co.uk",
                        "stun-gb-a.hp2k.co.uk",
                        "stun-gb-b.hp2k.co.uk",
                        "stun-eu-a.hp2k.co.uk",
                        "stun-eu-b.hp2k.co.uk",
                        GetHostFromUrl(_engine.PresenceUrl),
                        GetHostFromUrl(_engine.SignallingUrl),
                        GetHostFromUrl(_engine.RoomsUrl)
                    };
                    string[] linphoneHosts = { "sip.linphone.org", "sip2sip.info" };

                    foreach (var host in agilicoHosts.Concat(linphoneHosts).Where(h => !string.IsNullOrWhiteSpace(h)).Distinct(StringComparer.OrdinalIgnoreCase))
                    {
                        string resolvedHost = host;
                        probes.Add(ct => VoipTools.ProbeDnsResolutionAsync(resolvedHost, "DNS resolution", ct));
                    }

                    probes.Add(ct => VoipTools.ProbeTcpPortAsync("customerportal.hp2k.co.uk", 80, "Agilico Portal HTTP", ct));
                    probes.Add(ct => VoipTools.ProbeTcpPortAsync("customerportal.hp2k.co.uk", 443, "Agilico Portal HTTPS", ct));
                    probes.Add(ct => VoipTools.ProbeTcpPortAsync(GetHostFromUrl(_engine.PresenceUrl), 80, "Presence Service HTTP", ct));
                    probes.Add(ct => VoipTools.ProbeTcpPortAsync(GetHostFromUrl(_engine.SignallingUrl), 80, "Soft Signalling HTTP", ct));
                    probes.Add(ct => VoipTools.ProbeTcpPortAsync(GetHostFromUrl(_engine.RoomsUrl), 80, "Rooms Service HTTP", ct));

                    foreach (var stunHost in agilicoHosts.Where(h => h.StartsWith("stun-", StringComparison.OrdinalIgnoreCase)))
                    {
                        string resolvedHost = stunHost;
                        probes.Add(ct => VoipTools.ProbeUdpPortAsync(resolvedHost, 3478, "Agilico STUN UDP 3478", ct));
                    }

                    foreach (var sipHost in linphoneHosts)
                    {
                        string resolvedHost = sipHost;
                        probes.Add(ct => VoipTools.ProbeUdpPortAsync(resolvedHost, 5060, "Linphone SIP UDP 5060 / SIP ALG", ct));
                        probes.Add(ct => VoipTools.ProbeTcpPortAsync(resolvedHost, 5060, "Linphone SIP TCP 5060", ct));
                        probes.Add(ct => VoipTools.ProbeTcpPortAsync(resolvedHost, 5061, "Linphone SIP TLS TCP 5061", ct));
                    }

                    probes.Add(ct => VoipTools.ProbeUdpPortAsync("uk.pool.ntp.org", 123, "NTP UDP 123", ct, treatTimeoutAsOpen: true));
                    probes.Add(ct => VoipTools.ProbeUdpPortRangeAsync("stun-gb-a.hp2k.co.uk", 9000, 9398, 2, "Agilico RTP media range", ct));
                    probes.Add(ct => VoipTools.ProbeUdpPortRangeAsync("sip.linphone.org", 10600, 10998, 2, "Linphone RTP media range", ct));
                }
                else if (profileName.Contains("Microsoft Teams"))
                {
                    probes.Add(ct => VoipTools.ProbeTcpPortAsync("sip.pstnhub.microsoft.com", 5061, "Teams SIP TLS Primary", ct));
                    probes.Add(ct => VoipTools.ProbeTcpPortAsync("sip2.pstnhub.microsoft.com", 5061, "Teams SIP TLS Backup 1", ct));
                    probes.Add(ct => VoipTools.ProbeTcpPortAsync("sip3.pstnhub.microsoft.com", 5061, "Teams SIP TLS Backup 2", ct));
                    probes.Add(ct => VoipTools.ProbeUdpPortAsync("world.tr.teams.microsoft.com", 3478, "Teams Media STUN 3478", ct));
                    probes.Add(ct => VoipTools.ProbeUdpPortAsync("world.tr.teams.microsoft.com", 3479, "Teams Media STUN 3479", ct));
                    probes.Add(ct => VoipTools.ProbeUdpPortAsync("world.tr.teams.microsoft.com", 3480, "Teams Media STUN 3480", ct));
                    probes.Add(ct => VoipTools.ProbeUdpPortAsync("world.tr.teams.microsoft.com", 3481, "Teams Media STUN 3481", ct));
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
                                probes.Add(proto == "TCP"
                                    ? ct => VoipTools.ProbeTcpPortAsync(customTarget, p, $"Custom Service {proto}", ct)
                                    : ct => VoipTools.ProbeUdpPortAsync(customTarget, p, $"Custom Service {proto}", ct));
                            }
                        }
                        else if (tokens.Length == 1 && int.TryParse(tokens[0], out int p) && p > 0 && p <= 65535)
                        {
                            probes.Add(ct => VoipTools.ProbeTcpPortAsync(customTarget, p, "Custom Service TCP", ct));
                        }
                    }

                    if (probes.Count == 0)
                    {
                        ModernMessageBox.Show("No valid ports parsed. Format should be: Protocol Port (e.g., TCP 80).", "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                        RestorePortProbeBtn();
                        return;
                    }
                }

                // Run all probes concurrently and stream results to UI
                var probeTasks = probes.Select(probe => probe(token)).ToList();

                while (probeTasks.Count > 0)
                {
                    var completedTask = await Task.WhenAny(probeTasks);
                    probeTasks.Remove(completedTask);

                    var res = await completedTask;
                    _ = Dispatcher.BeginInvoke(new Action(() => _portProbeResults.Add(res)));
                }
            }
            catch (OperationCanceledException)
            {
                LogAuditAction("Cancelled port connectivity probe.");
            }
            catch (Exception ex)
            {
                ModernMessageBox.Show($"Error running port probes: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                LogAuditAction("Completed port connectivity probe.");
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
            var dialog = new InputDialog(title, instruction, defaultValue)
            {
                Owner = this
            };

            if (dialog.ShowDialog() == true)
            {
                return dialog.InputText;
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
                client.DefaultRequestHeaders.Add("User-Agent", "AgilicoMSPToolkit/4.0.0");

                string url = $"https://ip-api.com/json/{ipAddress}?fields=status,message,country,city,as";
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
            BtnRefreshSockets.IsEnabled = false;
            BtnRefreshSockets.Content = "REFRESHING...";
            try
            {
                LogAuditAction("Refreshed active network sockets list.");
                await RefreshSocketsListAsync();
            }
            catch (Exception ex)
            {
                ModernMessageBox.Show($"Failed to refresh socket list.\n\nError: {ex.Message}", "Socket Monitor Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                BtnRefreshSockets.IsEnabled = true;
                BtnRefreshSockets.Content = "REFRESH SOCKETS";
            }
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
