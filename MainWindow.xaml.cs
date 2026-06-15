using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;

namespace AgilicoConnectChecker
{
    public partial class MainWindow : Window
    {
        private readonly NetworkEngine _engine;
        private Button[] _navButtons = Array.Empty<Button>();

        public MainWindow()
        {
            InitializeComponent();
            _engine = new NetworkEngine();
            
            // Wire up engine events
            _engine.OnLog += Engine_OnLog;
            _engine.OnProgress += Engine_OnProgress;
            _engine.OnComplete += Engine_OnComplete;

            Loaded += MainWindow_Loaded;
        }

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            _navButtons = new[] { BtnDashboard, BtnLogs, BtnSettings, BtnHelp };
            
            // Initialize view
            SelectTab(0, BtnDashboard);
            ResetTestStatuses();
            PanelSummaryDefault.Visibility = Visibility.Visible;

            // Sync settings from engine (which auto-loaded from registry where available)
            TxtStunServer.Text = _engine.StunServer;
            TxtStunPort.Text = _engine.StunPort.ToString();
            TxtLocalPort.Text = _engine.LocalSipPort.ToString();
            TxtSipAlgServer.Text = _engine.SipAlgServer;
            TxtSipAlgPort.Text = _engine.SipAlgPort.ToString();
            ChkSimulation.IsChecked = _engine.IsSimulationMode;

            // Trigger firewall permission prompt on load so it doesn't block running tests
            _engine.TriggerFirewallPrompt();
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
        private void BtnLogs_Click(object sender, RoutedEventArgs e) => SelectTab(1, BtnLogs);
        private void BtnSettings_Click(object sender, RoutedEventArgs e) => SelectTab(2, BtnSettings);
        private void BtnHelp_Click(object sender, RoutedEventArgs e) => SelectTab(3, BtnHelp);

        private void BtnExit_Click(object sender, RoutedEventArgs e)
        {
            _engine.Cancel();
            Close();
        }

        #endregion

        #region Diagnostic Test Execution

        private void ResetTestStatuses()
        {
            for (int i = 1; i <= 8; i++)
            {
                UpdateTestUI(i, "pending", "Pending");
            }

            PanelSummaryPass.Visibility = Visibility.Collapsed;
            PanelSummaryFail.Visibility = Visibility.Collapsed;
        }

        private async void BtnStart_Click(object sender, RoutedEventArgs e)
        {
            // Sync settings from UI text fields
            if (!ValidateAndApplySettings()) return;

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

            // Run
            await _engine.RunDiagnosticsAsync();
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
                    "DNS Domain & Resolution" => 1,
                    "HTTP/HTTPS Outbound Probes" => 2,
                    "NTP Subsystem (UDP 123)" => 3,
                    "Agilico STUN Servers" => 4,
                    "Google STUN Servers" => 5,
                    "NAT Routing & Hops Check" => 6,
                    "NAT Port Translation (Random Port)" => 7,
                    "SIP ALG Detection" => 8,
                    _ => 0
                };

                if (testIndex > 0)
                {
                    UpdateTestUI(testIndex, status, details);
                }
            });
        }

        private void Engine_OnComplete(bool success)
        {
            Dispatcher.Invoke(() =>
            {
                RestoreControlButtons();

                PanelSummaryDefault.Visibility = Visibility.Collapsed;

                if (success)
                {
                    PanelSummaryPass.Visibility = Visibility.Visible;
                    PanelSummaryFail.Visibility = Visibility.Collapsed;
                }
                else
                {
                    PanelSummaryPass.Visibility = Visibility.Collapsed;
                    PanelSummaryFail.Visibility = Visibility.Visible;
                    
                    // Construct detailed recommendations
                    var sb = new StringBuilder();
                    sb.AppendLine("Please resolve the following network issues:");
                    
                    if (Test1Details.Text.Contains("Fail"))
                    {
                        sb.AppendLine("• DNS domain resolution failed or Google DNS servers (8.8.8.8/8.8.4.4) are unreachable. Check DNS settings.");
                    }
                    if (Test2Details.Text.Contains("Fail"))
                    {
                        sb.AppendLine("• Outbound TCP port 80 or 443 is blocked. Ensure web requests to customerportal.hp2k.co.uk are allowed.");
                    }
                    if (Test3Details.Text.Contains("Fail"))
                    {
                        sb.AppendLine("• Outbound UDP port 123 (NTP) is blocked. Clients must be able to sync time.");
                    }
                    if (Test4Details.Text.Contains("Fail"))
                    {
                        sb.AppendLine("• One or more Agilico STUN servers (port 3478) failed to respond. Check UDP outbound rules.");
                    }
                    if (Test5Details.Text.Contains("Fail"))
                    {
                        sb.AppendLine("• One or more Google backup STUN servers (port 3478) failed to respond.");
                    }
                    if (Test6Details.Text.Contains("Fail"))
                    {
                        sb.AppendLine("• Double NAT gateway hops detected. Double NAT should be avoided for reliable voice connectivity.");
                    }
                    if (Test7Details.Text.Contains("Fail"))
                    {
                        sb.AppendLine("• NAT port preservation is active. The guidance recommends random/different NAT public ports (not preserved).");
                    }
                    if (Test8Details.Text.Contains("Fail"))
                    {
                        sb.AppendLine("• SIP ALG is active or UDP 5060 is blocked. Disable SIP ALG/SIP Helper in your router settings.");
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
    }
}