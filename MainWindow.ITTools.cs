using System;
using System.Windows;
using System.Threading.Tasks;

namespace agilicomsptoolkit
{
    public partial class MainWindow : Window
    {
        private async void BtnIpConfig_Click(object sender, RoutedEventArgs e)
        {
            var result = ModernMessageBox.Show("This will flush your DNS cache, release your current IP, and renew it. Your connection may drop temporarily.\n\nContinue?", "Network Config Manager", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (result == MessageBoxResult.Yes)
            {
                try
                {
                    this.IsEnabled = false;
                    LogAuditAction("Executed Network Configuration Reset (ipconfig flushdns/release/renew).");
                    
                    using var p1 = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("ipconfig", "/flushdns") { CreateNoWindow = true, UseShellExecute = false });
                    if (p1 != null)
                    {
                        using var cts1 = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(15));
                        try { await p1.WaitForExitAsync(cts1.Token); } catch (OperationCanceledException) { try { if (!p1.HasExited) p1.Kill(true); } catch { } }
                    }
                    
                    using var p2 = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("ipconfig", "/release") { CreateNoWindow = true, UseShellExecute = false });
                    if (p2 != null)
                    {
                        using var cts2 = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(15));
                        try { await p2.WaitForExitAsync(cts2.Token); } catch (OperationCanceledException) { try { if (!p2.HasExited) p2.Kill(true); } catch { } }
                    }
                    
                    using var p3 = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("ipconfig", "/renew") { CreateNoWindow = true, UseShellExecute = false });
                    if (p3 != null)
                    {
                        using var cts3 = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(30));
                        try { await p3.WaitForExitAsync(cts3.Token); } catch (OperationCanceledException) { try { if (!p3.HasExited) p3.Kill(true); } catch { } }
                    }
                    
                    ModernMessageBox.Show("Network configuration successfully reset and renewed.", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    ModernMessageBox.Show($"Failed to execute network commands: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
                finally
                {
                    this.IsEnabled = true;
                }
            }
        }

        private void BtnResourceMonitor_Click(object sender, RoutedEventArgs e)
        {
            LogAuditAction("Launched Resource Monitor dialog.");
            var dialog = new ResourceMonitorDialog { Owner = this };
            dialog.ShowDialog();
        }

        private void BtnActiveDirectory_Click(object sender, RoutedEventArgs e)
        {
            LogAuditAction("Launched Active Directory & Domain Information dialog.");
            var dialog = new UserDomainInfoDialog { Owner = this };
            dialog.ShowDialog();
        }

        private void BtnSoftwareAudit_Click(object sender, RoutedEventArgs e)
        {
            LogAuditAction("Launched Software Audit dialog.");
            var dialog = new SoftwareAuditDialog { Owner = this };
            dialog.ShowDialog();
        }

        private void BtnDriverUpdater_Click(object sender, RoutedEventArgs e)
        {
            LogAuditAction("Launched Driver Management & Updates dialog.");
            var dialog = new DriverUpdaterDialog { Owner = this };
            dialog.ShowDialog();
        }

        private void BtnServicesManager_Click(object sender, RoutedEventArgs e)
        {
            LogAuditAction("Launched Windows Services Manager dialog.");
            var dialog = new ServiceManagerDialog { Owner = this };
            dialog.ShowDialog();
        }

        private void BtnDiskCleanup_Click(object sender, RoutedEventArgs e)
        {
            LogAuditAction("Launched Disk Management & Cleanup dialog.");
            var dialog = new DiskManagementDialog { Owner = this };
            dialog.ShowDialog();
        }

        private void BtnEventLog_Click(object sender, RoutedEventArgs e)
        {
            LogAuditAction("Launched Windows Event Log dialog.");
            var dialog = new EventLogDialog { Owner = this };
            dialog.ShowDialog();
        }

        private async void BtnGroupPolicy_Click(object sender, RoutedEventArgs e)
        {
            LogAuditAction("Executed Group Policy analysis report.");
            // Keeping plain text dump for unstructured tools
            await RunITToolAsync("Group Policy Utility", "gpresult /R");
        }

        private void BtnFirewall_Click(object sender, RoutedEventArgs e)
        {
            LogAuditAction("Launched Firewall Status dialog.");
            var dialog = new FirewallStatusDialog { Owner = this };
            dialog.ShowDialog();
        }

        private async void BtnPowerManager_Click(object sender, RoutedEventArgs e)
        {
            LogAuditAction("Executed System Power & Uptime analysis report.");
            // Keeping plain text dump for unstructured tools
            await RunITToolAsync("System Uptime & Power", "powercfg /requests");
        }

        private void BtnStartupApps_Click(object sender, RoutedEventArgs e)
        {
            LogAuditAction("Launched Windows Startup Manager dialog.");
            var dialog = new StartupManagerDialog { Owner = this };
            dialog.ShowDialog();
        }

        private void BtnLocalUsers_Click(object sender, RoutedEventArgs e)
        {
            LogAuditAction("Launched Local Users & Groups Manager dialog.");
            var dialog = new LocalUsersDialog { Owner = this };
            dialog.ShowDialog();
        }

        private void BtnNicOptimizer_Click(object sender, RoutedEventArgs e)
        {
            LogAuditAction("Launched Network Adapter (NIC) Optimizer dialog.");
            var dialog = new NicOptimizerDialog { Owner = this };
            dialog.ShowDialog();
        }

        private readonly Services.IPowerShellRunner _powerShellRunner = new Services.PowerShellRunner();

        private async Task RunGenericAuditAsync(string title, string description, string jsonCommand)
        {
            try
            {
                var (exitCode, stdout, stderr) = await _powerShellRunner.ExecuteCommandAsync(jsonCommand, TimeSpan.FromSeconds(30));

                if (string.IsNullOrWhiteSpace(stdout))
                {
                    if (!string.IsNullOrWhiteSpace(stderr))
                    {
                        ModernMessageBox.Show($"Command failed with error:\n{stderr}", "Audit Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                    else
                    {
                        ModernMessageBox.Show("Command returned no tabular data.", "Info", MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                    return;
                }

                Dispatcher.Invoke(() =>
                {
                    var dialog = new GenericDataGridDialog(title, description, stdout) { Owner = this };
                    dialog.ShowDialog();
                });
            }
            catch (Exception ex)
            {
                ModernMessageBox.Show($"Failed to generate audit report:\n{ex.Message}", "Tool Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async Task RunITToolAsync(string title, string command)
        {
            try
            {
                var (exitCode, stdout, stderr) = await _powerShellRunner.ExecuteCommandAsync(command, TimeSpan.FromSeconds(30));

                string output;
                if (string.IsNullOrWhiteSpace(stdout) && !string.IsNullOrWhiteSpace(stderr))
                    output = $"Error Output:\n{stderr}";
                else if (string.IsNullOrWhiteSpace(stdout))
                    output = "Command completed successfully (no output).";
                else
                    output = stdout;

                Dispatcher.Invoke(() =>
                {
                    var dialog = new ResultDialog(title, output) { Owner = this };
                    dialog.ShowDialog();
                });
            }
            catch (Exception ex)
            {
                ModernMessageBox.Show($"Failed to execute tool:\n{ex.Message}", "Tool Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnM365Manager_Click(object sender, RoutedEventArgs e)
        {
            LogAuditAction("Launched Microsoft 365 / Teams Management dialog.");
            var dialog = new M365ManagerDialog { Owner = this };
            dialog.ShowDialog();
        }
    }
}
