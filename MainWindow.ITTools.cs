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
                    await Task.Run(() =>
                    {
                        using var p1 = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("ipconfig", "/flushdns") { CreateNoWindow = true, UseShellExecute = false });
                        p1?.WaitForExit(5000);
                        using var p2 = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("ipconfig", "/release") { CreateNoWindow = true, UseShellExecute = false });
                        p2?.WaitForExit(5000);
                        using var p3 = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("ipconfig", "/renew") { CreateNoWindow = true, UseShellExecute = false });
                        p3?.WaitForExit(10000);
                    });
                    
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
            var dialog = new ResourceMonitorDialog { Owner = this };
            dialog.ShowDialog();
        }

        private void BtnActiveDirectory_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new UserDomainInfoDialog { Owner = this };
            dialog.ShowDialog();
        }

        private void BtnSoftwareAudit_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new SoftwareAuditDialog { Owner = this };
            dialog.ShowDialog();
        }

        private void BtnServicesManager_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new ServiceManagerDialog { Owner = this };
            dialog.ShowDialog();
        }

        private void BtnDiskCleanup_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new DiskManagementDialog { Owner = this };
            dialog.ShowDialog();
        }

        private void BtnEventLog_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new EventLogDialog { Owner = this };
            dialog.ShowDialog();
        }

        private async void BtnGroupPolicy_Click(object sender, RoutedEventArgs e)
        {
            // Keeping plain text dump for unstructured tools
            await RunITToolAsync("Group Policy Utility", "gpresult /R");
        }

        private void BtnFirewall_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new FirewallStatusDialog { Owner = this };
            dialog.ShowDialog();
        }

        private async void BtnPowerManager_Click(object sender, RoutedEventArgs e)
        {
            // Keeping plain text dump for unstructured tools
            await RunITToolAsync("System Uptime & Power", "powercfg /requests");
        }

        private void BtnStartupApps_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new StartupManagerDialog { Owner = this };
            dialog.ShowDialog();
        }

        private void BtnLocalUsers_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new LocalUsersDialog { Owner = this };
            dialog.ShowDialog();
        }

        private void BtnNicOptimizer_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new NicOptimizerDialog { Owner = this };
            dialog.ShowDialog();
        }

        private async Task RunGenericAuditAsync(string title, string description, string jsonCommand)
        {
            try
            {
                string exe = "powershell.exe";
                string args = $"-NoProfile -Command \"{jsonCommand}\"";
                
                var startInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = exe,
                    Arguments = args,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using var process = System.Diagnostics.Process.Start(startInfo);
                if (process == null) return;
                
                var outTask = process.StandardOutput.ReadToEndAsync();
                var errTask = process.StandardError.ReadToEndAsync();
                await process.WaitForExitAsync();
                
                string output = await outTask;
                string err = await errTask;
                
                if (string.IsNullOrWhiteSpace(output))
                {
                    if (!string.IsNullOrWhiteSpace(err))
                    {
                        ModernMessageBox.Show($"Command failed with error:\n{err}", "Audit Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                    else
                    {
                        ModernMessageBox.Show("Command returned no tabular data.", "Info", MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                    return;
                }

                var dialog = new GenericDataGridDialog(title, description, output) { Owner = this };
                dialog.ShowDialog();
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
                // Run via PowerShell to support rich formatting and modern cmdlets
                string exe = "powershell.exe";
                string args = $"-NoProfile -Command \"{command}\"";
                
                var startInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = exe,
                    Arguments = args,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using var process = System.Diagnostics.Process.Start(startInfo);
                if (process == null) return;
                
                var outTask = process.StandardOutput.ReadToEndAsync();
                var errTask = process.StandardError.ReadToEndAsync();
                await process.WaitForExitAsync();
                
                string output = await outTask;
                string err = await errTask;
                
                if (string.IsNullOrWhiteSpace(output) && !string.IsNullOrWhiteSpace(err))
                    output = $"Error Output:\n{err}";
                else if (string.IsNullOrWhiteSpace(output))
                    output = "Command completed successfully (no output).";

                var dialog = new ResultDialog(title, output) { Owner = this };
                dialog.ShowDialog();
            }
            catch (Exception ex)
            {
                ModernMessageBox.Show($"Failed to execute tool:\n{ex.Message}", "Tool Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnM365Manager_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new M365ManagerDialog { Owner = this };
            dialog.ShowDialog();
        }
    }
}
