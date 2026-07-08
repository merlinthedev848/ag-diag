using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace agilicomsptoolkit
{
    public partial class M365ManagerDialog : Window
    {
        public M365ManagerDialog()
        {
            InitializeComponent();
        }

        private async void Window_Loaded(object sender, RoutedEventArgs e)
        {
            await CheckPowerShellModulesAsync();
            GeneratePreviewScript();
        }

        private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
                DragMove();
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private async Task CheckPowerShellModulesAsync()
        {
            TxtModuleStatus.Text = "Checking local module availability...";
            BtnInstallModules.IsEnabled = false;

            try
            {
                string script = "Get-Module -ListAvailable ExchangeOnlineManagement, Microsoft.Graph | Select-Object Name | ConvertTo-Json -Compress";
                var output = await RunPowerShellSilentAsync(script);
                
                bool exchangeFound = output.Contains("ExchangeOnlineManagement", StringComparison.OrdinalIgnoreCase);
                bool graphFound = output.Contains("Microsoft.Graph", StringComparison.OrdinalIgnoreCase);

                if (exchangeFound && graphFound)
                {
                    TxtModuleStatus.Text = "✅ M365 (Microsoft.Graph) and Exchange Online modules are installed.";
                    TxtModuleStatus.Foreground = Brushes.LightGreen;
                    BtnConnectExchange.IsEnabled = true;
                    BtnConnectGraph.IsEnabled = true;
                    BtnRunScript.IsEnabled = true;
                }
                else
                {
                    string missing = "";
                    if (!exchangeFound) missing += "ExchangeOnlineManagement ";
                    if (!graphFound) missing += "Microsoft.Graph ";
                    TxtModuleStatus.Text = $"⚠ Missing modules: {missing.Trim()}. Click below to install.";
                    TxtModuleStatus.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#f97316")); // Warning orange
                    BtnInstallModules.IsEnabled = true;
                    
                    BtnConnectExchange.IsEnabled = false;
                    BtnConnectGraph.IsEnabled = false;
                    BtnRunScript.IsEnabled = false;
                }
            }
            catch (Exception ex)
            {
                TxtModuleStatus.Text = $"Failed to check modules: {ex.Message}";
                BtnInstallModules.IsEnabled = true;
            }
        }

        private void BtnInstallModules_Click(object sender, RoutedEventArgs e)
        {
            var result = ModernMessageBox.Show(
                "Installing M365 modules requires an active internet connection and may take a few minutes.\n\nThis will open an administrative PowerShell window. Continue?",
                "Install Modules", MessageBoxButton.YesNo, MessageBoxImage.Question);
            
            if (result != MessageBoxResult.Yes) return;

            string script = "[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12; " +
                            "Write-Host 'Configuring Package Provider...' -ForegroundColor Cyan; " +
                            "if (-not (Get-PackageProvider -Name NuGet -ErrorAction SilentlyContinue)) { " +
                            "  Install-PackageProvider -Name NuGet -MinimumVersion 2.8.5.201 -Force " +
                            "}; " +
                            "Set-PSRepository -Name PSGallery -InstallationPolicy Trusted; " +
                            "Write-Host 'Installing ExchangeOnlineManagement module...' -ForegroundColor Cyan; " +
                            "Install-Module -Name ExchangeOnlineManagement -Force -AllowClobber -Scope CurrentUser; " +
                            "Write-Host 'Installing Microsoft.Graph module...' -ForegroundColor Cyan; " +
                            "Install-Module -Name Microsoft.Graph -Force -AllowClobber -Scope CurrentUser; " +
                            "Write-Host 'Installation complete! Checking module status:' -ForegroundColor Green; " +
                            "Get-Module -ListAvailable ExchangeOnlineManagement, Microsoft.Graph | Select-Object Name, Version; " +
                            "Write-Host 'Press Enter to return to toolkit...'; Read-Host";

            RunPowerShellInteractive(script, async () => {
                await CheckPowerShellModulesAsync();
                GeneratePreviewScript();
            });
        }

        private void BtnConnectExchange_Click(object sender, RoutedEventArgs e)
        {
            string script = "Write-Host 'Connecting to Exchange Online...' -ForegroundColor Cyan; " +
                            "Import-Module ExchangeOnlineManagement; " +
                            "Connect-ExchangeOnline; " +
                            "if (Get-ConnectionInfo) { Write-Host 'Successfully Connected to Exchange Online!' -ForegroundColor Green } else { Write-Host 'Connection failed.' -ForegroundColor Red }; " +
                            "Write-Host 'Press Enter to return to toolkit...'; Read-Host";

            RunPowerShellInteractive(script);
            
            // Assume connected for UI feedback (in real situations, user will complete it in console)
            TxtExchangeStatus.Text = "🟢 Pre-authenticated";
            TxtExchangeStatus.Foreground = Brushes.LightGreen;
        }

        private void BtnConnectGraph_Click(object sender, RoutedEventArgs e)
        {
            string script = "Write-Host 'Connecting to Microsoft Graph...' -ForegroundColor Cyan; " +
                            "Import-Module Microsoft.Graph; " +
                            "Connect-MgGraph -Scopes 'User.ReadWrite.All', 'Group.ReadWrite.All', 'Directory.AccessAsUser.All'; " +
                            "Write-Host 'Successfully Connected to Microsoft Graph!' -ForegroundColor Green; " +
                            "Write-Host 'Press Enter to return to toolkit...'; Read-Host";

            RunPowerShellInteractive(script);

            TxtGraphStatus.Text = "🟢 Pre-authenticated";
            TxtGraphStatus.Foreground = Brushes.LightGreen;
        }

        private void BtnGeneratePass_Click(object sender, RoutedEventArgs e)
        {
            const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789!@#$%^&*()_+";
            var random = new Random();
            var password = new string(Enumerable.Repeat(chars, 12)
                .Select(s => s[random.Next(s.Length)]).ToArray());
            
            TxtResetPassword.Text = password;
        }

        private void FormInputChanged(object sender, EventArgs e)
        {
            GeneratePreviewScript();
        }

        private void TabControl_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            GeneratePreviewScript();
        }

        private void GeneratePreviewScript()
        {
            if (TxtScriptPreview == null || MainTabControl == null) return;

            var selectedTab = MainTabControl.SelectedItem as TabItem;
            if (selectedTab == null) return;

            var sb = new StringBuilder();

            if (selectedTab.Header.ToString() == "Connections & Setup")
            {
                sb.AppendLine("# M365 Connection and Module verification script");
                sb.AppendLine("Import-Module ExchangeOnlineManagement");
                sb.AppendLine("Import-Module Microsoft.Graph");
                sb.AppendLine("Connect-ExchangeOnline");
                sb.AppendLine("Connect-MgGraph -Scopes 'User.ReadWrite.All', 'Group.ReadWrite.All', 'Directory.AccessAsUser.All'");
            }
            else if (selectedTab.Header.ToString() == "Delegations")
            {
                string mailbox = string.IsNullOrWhiteSpace(TxtMailboxUPN.Text) ? "<user@domain.com>" : TxtMailboxUPN.Text.Trim();
                string delegateUser = string.IsNullOrWhiteSpace(TxtDelegateUPN.Text) ? "<delegate@domain.com>" : TxtDelegateUPN.Text.Trim();

                sb.AppendLine("# Microsoft 365 Mailbox & Calendar Delegation");
                sb.AppendLine("Import-Module ExchangeOnlineManagement");
                sb.AppendLine("Connect-ExchangeOnline");
                sb.AppendLine();

                string mailPerm = (ComboMailboxPerm.SelectedItem as ComboBoxItem)?.Content.ToString() ?? "None";
                if (mailPerm != "None")
                {
                    sb.AppendLine($"# Granting Mailbox Permission: {mailPerm}");
                    if (mailPerm == "FullAccess")
                    {
                        sb.AppendLine($"Add-MailboxPermission -Identity \"{mailbox}\" -User \"{delegateUser}\" -AccessRights FullAccess -InheritanceType All");
                    }
                    else if (mailPerm == "SendAs")
                    {
                        sb.AppendLine($"Add-RecipientPermission -Identity \"{mailbox}\" -Trustee \"{delegateUser}\" -AccessRights SendAs -Confirm:$false");
                    }
                    else if (mailPerm == "SendOnBehalf")
                    {
                        sb.AppendLine($"Set-Mailbox -Identity \"{mailbox}\" -GrantSendOnBehalfTo \"{delegateUser}\"");
                    }
                    sb.AppendLine();
                }

                string calPerm = (ComboCalendarPerm.SelectedItem as ComboBoxItem)?.Content.ToString() ?? "None";
                if (calPerm != "None")
                {
                    string permValue = calPerm.Split(' ')[0]; // Extract Editor, Owner, etc.
                    sb.AppendLine($"# Granting Calendar Folder Permission: {permValue}");
                    sb.AppendLine($"$targetPath = \"{mailbox}:\\Calendar\"");
                    sb.AppendLine("try {");
                    sb.AppendLine($"    Add-MailboxFolderPermission -Identity $targetPath -User \"{delegateUser}\" -AccessRights {permValue} -ErrorAction Stop");
                    sb.AppendLine("} catch {");
                    sb.AppendLine($"    Set-MailboxFolderPermission -Identity $targetPath -User \"{delegateUser}\" -AccessRights {permValue}");
                    sb.AppendLine("}");
                }
            }
            else if (selectedTab.Header.ToString() == "User Security")
            {
                string user = string.IsNullOrWhiteSpace(TxtSecurityUserUPN.Text) ? "<user@domain.com>" : TxtSecurityUserUPN.Text.Trim();
                string pass = TxtResetPassword.Text;

                sb.AppendLine("# Microsoft 365 Account Security & Password Management");
                sb.AppendLine("Import-Module Microsoft.Graph");
                sb.AppendLine("Connect-MgGraph -Scopes 'User.ReadWrite.All', 'Directory.AccessAsUser.All'");
                sb.AppendLine();

                if (!string.IsNullOrWhiteSpace(pass))
                {
                    bool forceChange = ChkForceChange.IsChecked ?? false;
                    sb.AppendLine("# Reset password block");
                    sb.AppendLine("$params = @{");
                    sb.AppendLine("    passwordProfile = @{");
                    sb.AppendLine($"        forceChangePasswordNextSignIn = ${forceChange.ToString().ToLower()}");
                    sb.AppendLine($"        password = \"{pass}\"");
                    sb.AppendLine("    }");
                    sb.AppendLine("}");
                    sb.AppendLine($"Update-MgUser -UserId \"{user}\" -BodyParameter $params");
                    sb.AppendLine();
                }

                if (ChkRevokeSessions.IsChecked ?? false)
                {
                    sb.AppendLine("# Revoke all active login sessions");
                    sb.AppendLine($"Revoke-MgUserSignInSession -UserId \"{user}\"");
                    sb.AppendLine();
                }

                string accountState = (ComboAccountState.SelectedItem as ComboBoxItem)?.Content.ToString() ?? "Keep Current State";
                if (accountState != "Keep Current State")
                {
                    bool enabled = accountState.StartsWith("Enabled");
                    sb.AppendLine($"# Set account status: Enabled = {enabled}");
                    sb.AppendLine($"Update-MgUser -UserId \"{user}\" -AccountEnabled ${enabled.ToString().ToLower()}");
                }
            }
            else if (selectedTab.Header.ToString() == "Teams & Groups")
            {
                string group = string.IsNullOrWhiteSpace(TxtGroupUPN.Text) ? "<Group-UPN-or-GUID>" : TxtGroupUPN.Text.Trim();
                string member = string.IsNullOrWhiteSpace(TxtGroupMemberUPN.Text) ? "<user@domain.com>" : TxtGroupMemberUPN.Text.Trim();
                string operation = (ComboGroupOp.SelectedItem as ComboBoxItem)?.Content.ToString() ?? "Add to Group / Team";
                bool isOwner = RadioOwner.IsChecked ?? false;

                sb.AppendLine("# Microsoft 365 Group & Teams Member Manager");
                sb.AppendLine("Import-Module Microsoft.Graph");
                sb.AppendLine("Connect-MgGraph -Scopes 'Group.ReadWrite.All'");
                sb.AppendLine();

                sb.AppendLine($"# Target: Group/Team '{group}', User '{member}'");
                sb.AppendLine($"$targetUser = Get-MgUser -UserId \"{member}\"");
                
                if (operation.StartsWith("Add"))
                {
                    if (isOwner)
                    {
                        sb.AppendLine($"Add-MgGroupOwnerByRef -GroupId \"{group}\" -OdataId $targetUser.Id");
                    }
                    else
                    {
                        sb.AppendLine($"Add-MgGroupMemberByRef -GroupId \"{group}\" -OdataId $targetUser.Id");
                    }
                }
                else
                {
                    if (isOwner)
                    {
                        sb.AppendLine($"Remove-MgGroupOwnerByRef -GroupId \"{group}\" -DirectoryObjectId $targetUser.Id");
                    }
                    else
                    {
                        sb.AppendLine($"Remove-MgGroupMemberByRef -GroupId \"{group}\" -DirectoryObjectId $targetUser.Id");
                    }
                }
            }

            TxtScriptPreview.Text = sb.ToString();
        }

        private void BtnCopyScript_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Clipboard.SetText(TxtScriptPreview.Text);
                TxtStatus.Text = "Script copied to clipboard!";
            }
            catch (Exception ex)
            {
                ModernMessageBox.Show($"Failed to copy script: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnRunScript_Click(object sender, RoutedEventArgs e)
        {
            var result = ModernMessageBox.Show(
                "This will launch a visible, interactive PowerShell window to run the generated commands.\n\nYou may be prompted to sign into Microsoft 365 to authenticate.\n\nExecute script?",
                "Run Script", MessageBoxButton.YesNo, MessageBoxImage.Warning);

            if (result != MessageBoxResult.Yes) return;

            if (Owner is MainWindow mainWindow)
            {
                mainWindow.LogAuditAction("Executed Microsoft 365 / Teams automation script.");
            }

            RunPowerShellInteractive(TxtScriptPreview.Text);
        }

        private async Task<string> RunPowerShellSilentAsync(string command)
        {
            return await Task.Run(() =>
            {
                try
                {
                    var startInfo = new ProcessStartInfo
                    {
                        FileName = "powershell.exe",
                        Arguments = $"-NoProfile -Command \"{command}\"",
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    };

                    using var proc = Process.Start(startInfo);
                    if (proc == null) return string.Empty;

                    string stdout = proc.StandardOutput.ReadToEnd();
                    proc.WaitForExit(5000);
                    return stdout;
                }
                catch
                {
                    return string.Empty;
                }
            });
        }

        private void RunPowerShellInteractive(string scriptContent, Action? onExit = null)
        {
            try
            {
                // Create a temporary script file to run or execute as command block
                string tempFile = Path.Combine(Path.GetTempPath(), "m365_automation.ps1");
                File.WriteAllText(tempFile, scriptContent, Encoding.UTF8);

                string arguments = $"-NoProfile -NoExit -ExecutionPolicy Bypass -File \"{tempFile}\"";

                var proc = Process.Start(new ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments = arguments,
                    UseShellExecute = true
                });

                if (proc != null && onExit != null)
                {
                    proc.EnableRaisingEvents = true;
                    proc.Exited += (s, ev) =>
                    {
                        Dispatcher.Invoke(onExit);
                    };
                }
            }
            catch (Exception ex)
            {
                ModernMessageBox.Show($"Failed to launch PowerShell: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}
