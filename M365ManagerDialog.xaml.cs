using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
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
        private readonly List<string> _tempFiles = new();

        public M365ManagerDialog()
        {
            InitializeComponent();
            DataContext = this;
        }

        protected override void OnClosed(EventArgs e)
        {
            base.OnClosed(e);
            foreach (string file in _tempFiles.ToArray())
            {
                try { if (File.Exists(file)) File.Delete(file); } catch { }
            }
        }

        private async Task<string> RunPowerShellSilentAsync(string script)
        {
            string tempPath = CreateTempScript(script);
            try
            {
                using var process = new Process();
                process.StartInfo.FileName = GetPowerShellPath();
                process.StartInfo.ArgumentList.Add("-NoProfile");
                process.StartInfo.ArgumentList.Add("-ExecutionPolicy");
                process.StartInfo.ArgumentList.Add("RemoteSigned");
                process.StartInfo.ArgumentList.Add("-File");
                process.StartInfo.ArgumentList.Add(tempPath);
                process.StartInfo.UseShellExecute = false;
                process.StartInfo.RedirectStandardOutput = true;
                process.StartInfo.RedirectStandardError = true;
                process.StartInfo.CreateNoWindow = true;
                if (!process.Start()) throw new InvalidOperationException("PowerShell could not be started.");
                Task<string> readOut = process.StandardOutput.ReadToEndAsync();
                Task<string> readErr = process.StandardError.ReadToEndAsync();
                await Task.WhenAll(readOut, readErr).ConfigureAwait(false);
                await process.WaitForExitAsync().ConfigureAwait(false);
                return readOut.Result + readErr.Result;
            }
            finally { DeleteTempScript(tempPath); }
        }

        private void RunPowerShellInteractive(string script, Action? callback = null)
        {
            string tempPath = CreateTempScript(script);
            try
            {
                ProcessStartInfo psi = new() { FileName = GetPowerShellPath(), UseShellExecute = true };
                psi.ArgumentList.Add("-NoProfile");
                psi.ArgumentList.Add("-ExecutionPolicy");
                psi.ArgumentList.Add("RemoteSigned");
                psi.ArgumentList.Add("-NoExit");
                psi.ArgumentList.Add("-File");
                psi.ArgumentList.Add(tempPath);
                Process? proc = Process.Start(psi);
                if (proc == null) throw new InvalidOperationException("PowerShell could not be started.");
                _ = Task.Run(async () =>
                {
                    try { await proc.WaitForExitAsync().ConfigureAwait(false); }
                    catch { }
                    finally
                    {
                        DeleteTempScript(tempPath);
                        if (callback != null && !Dispatcher.HasShutdownStarted) Dispatcher.Invoke(callback);
                    }
                });
            }
            catch
            {
                DeleteTempScript(tempPath);
                throw;
            }
        }

        private string CreateTempScript(string script)
        {
            string tempDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AgilicoToolkit", "Temp");
            Directory.CreateDirectory(tempDir);
            string tempPath = Path.Combine(tempDir, $"m365-{Guid.NewGuid():N}.ps1");
            using (var stream = new FileStream(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.Read))
            using (var writer = new StreamWriter(stream, new UTF8Encoding(false))) writer.Write(script);
            _tempFiles.Add(tempPath);
            return tempPath;
        }

        private void DeleteTempScript(string path)
        {
            try { if (File.Exists(path)) File.Delete(path); } catch { }
            _tempFiles.Remove(path);
        }

        private static string GetPowerShellPath() =>
            Environment.GetEnvironmentVariable("PROCESSOR_ARCHITEW6432") != null || Environment.Is64BitOperatingSystem
                ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0", "powershell.exe")
                : "powershell.exe";

        private async void Window_Loaded(object sender, RoutedEventArgs e)
        {
            await CheckPowerShellModulesAsync();
            GeneratePreviewScript();
        }

        private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left) DragMove();
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e) => Close();

        private async Task CheckPowerShellModulesAsync()
        {
            TxtModuleStatus.Text = "Checking local module availability...";
            BtnInstallModules.IsEnabled = false;
            try
            {
                const string script = "Get-Module -ListAvailable ExchangeOnlineManagement, Microsoft.Graph | Select-Object Name | ConvertTo-Json -Compress";
                string output = await RunPowerShellSilentAsync(script);
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
                    string missing = string.Join(" ", new[] { exchangeFound ? null : "ExchangeOnlineManagement", graphFound ? null : "Microsoft.Graph" }.Where(x => x != null));
                    TxtModuleStatus.Text = $"⚠ Missing modules: {missing}. Click below to install.";
                    TxtModuleStatus.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#f97316"));
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
                Logger.Warning($"M365 module check failed: {ex.Message}", "M365");
            }
        }

        private void BtnInstallModules_Click(object sender, RoutedEventArgs e)
        {
            var result = ModernMessageBox.Show("Installing M365 modules requires an active internet connection and may take a few minutes.\n\nThis opens a user-level PowerShell window. Continue?", "Install Modules", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (result != MessageBoxResult.Yes) return;
            string script =
                "Set-StrictMode -Version Latest; $ErrorActionPreference = 'Stop'; " +
                "Write-Host 'Installing ExchangeOnlineManagement...' -ForegroundColor Cyan; " +
                "Install-Module -Name ExchangeOnlineManagement -Scope CurrentUser -Repository PSGallery -Force -AllowClobber; " +
                "Write-Host 'Installing Microsoft.Graph...' -ForegroundColor Cyan; " +
                "Install-Module -Name Microsoft.Graph -Scope CurrentUser -Repository PSGallery -Force -AllowClobber; " +
                "Write-Host 'Installation complete.' -ForegroundColor Green; " +
                "Get-Module -ListAvailable ExchangeOnlineManagement, Microsoft.Graph | Select-Object Name, Version; " +
                "Write-Host 'Press Enter to return to toolkit...'; Read-Host";
            try
            {
                RunPowerShellInteractive(script, async () => { await CheckPowerShellModulesAsync(); GeneratePreviewScript(); });
            }
            catch (Exception ex)
            {
                ModernMessageBox.Show($"Unable to start PowerShell: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnConnectExchange_Click(object sender, RoutedEventArgs e)
        {
            string script =
                "Set-StrictMode -Version Latest; $ErrorActionPreference = 'Stop'; " +
                "Write-Host 'Connecting to Exchange Online...' -ForegroundColor Cyan; " +
                "Import-Module ExchangeOnlineManagement; " +
                "Connect-ExchangeOnline; " +
                "Write-Host 'Exchange Online authentication completed in this PowerShell session.' -ForegroundColor Green; " +
                "Write-Host 'The toolkit will reconnect when executing a generated script.'; " +
                "Write-Host 'Press Enter to return to toolkit...'; Read-Host";
            try
            {
                RunPowerShellInteractive(script);
                TxtExchangeStatus.Text = "🟡 Authentication window opened — session is external to toolkit";
                TxtExchangeStatus.Foreground = Brushes.Khaki;
            }
            catch (Exception ex)
            {
                TxtExchangeStatus.Text = "🔴 Failed to start authentication";
                TxtExchangeStatus.Foreground = Brushes.IndianRed;
                Logger.Error("Exchange authentication launch failed", ex, "M365");
            }
        }

        private void BtnConnectGraph_Click(object sender, RoutedEventArgs e)
        {
            const string script =
                "Set-StrictMode -Version Latest; $ErrorActionPreference = 'Stop'; " +
                "Write-Host 'Connecting to Microsoft Graph...' -ForegroundColor Cyan; " +
                "Import-Module Microsoft.Graph; " +
                "Connect-MgGraph -Scopes 'User.Read'; " +
                "Write-Host 'Microsoft Graph authentication completed in this PowerShell session.' -ForegroundColor Green; " +
                "Write-Host 'The toolkit will request operation-specific permissions when executing a generated script.'; " +
                "Write-Host 'Press Enter to return to toolkit...'; Read-Host";
            try
            {
                RunPowerShellInteractive(script);
                TxtGraphStatus.Text = "🟡 Authentication window opened — session is external to toolkit";
                TxtGraphStatus.Foreground = Brushes.Khaki;
            }
            catch (Exception ex)
            {
                TxtGraphStatus.Text = "🔴 Failed to start authentication";
                TxtGraphStatus.Foreground = Brushes.IndianRed;
                Logger.Error("Graph authentication launch failed", ex, "M365");
            }
        }

        private void BtnGeneratePass_Click(object sender, RoutedEventArgs e)
        {
            const string upper = "ABCDEFGHIJKLMNOPQRSTUVWXYZ";
            const string lower = "abcdefghijklmnopqrstuvwxyz";
            const string digits = "0123456789";
            const string symbols = "!@#$%^&*()_+";
            const string all = upper + lower + digits + symbols;
            char[] password = new char[16];
            password[0] = System.Security.Cryptography.RandomNumberGenerator.GetInt32(upper.Length) >= 0 ? upper[System.Security.Cryptography.RandomNumberGenerator.GetInt32(upper.Length)] : 'A';
            password[1] = lower[System.Security.Cryptography.RandomNumberGenerator.GetInt32(lower.Length)];
            password[2] = digits[System.Security.Cryptography.RandomNumberGenerator.GetInt32(digits.Length)];
            password[3] = symbols[System.Security.Cryptography.RandomNumberGenerator.GetInt32(symbols.Length)];
            for (int i = 4; i < password.Length; i++) password[i] = all[System.Security.Cryptography.RandomNumberGenerator.GetInt32(all.Length)];
            for (int i = password.Length - 1; i > 0; i--)
            {
                int j = System.Security.Cryptography.RandomNumberGenerator.GetInt32(i + 1);
                (password[i], password[j]) = (password[j], password[i]);
            }
            TxtResetPassword.Text = new string(password);
        }

        private void FormInputChanged(object sender, EventArgs e) => GeneratePreviewScript();
        private void TabControl_SelectionChanged(object sender, SelectionChangedEventArgs e) => GeneratePreviewScript();

        private static string EscapePowerShellString(string input)
        {
            if (string.IsNullOrEmpty(input)) return string.Empty;
            return input.Replace("`", "``").Replace("\"", "`\"").Replace("$", "`$");
        }

        private void GeneratePreviewScript()
        {
            if (TxtScriptPreview == null || MainTabControl == null) return;
            var selectedTab = MainTabControl.SelectedItem as TabItem;
            if (selectedTab == null) return;
            var sb = new StringBuilder();
            string header = selectedTab.Header?.ToString() ?? string.Empty;

            if (header == "Connections & Setup")
            {
                sb.AppendLine("# M365 Connection and module verification");
                sb.AppendLine("Import-Module ExchangeOnlineManagement");
                sb.AppendLine("Import-Module Microsoft.Graph");
                sb.AppendLine("Connect-ExchangeOnline");
                sb.AppendLine("Connect-MgGraph -Scopes 'User.Read'");
            }
            else if (header == "Delegations")
            {
                string mailbox = string.IsNullOrWhiteSpace(TxtMailboxUPN.Text) ? "<user@domain.com>" : EscapePowerShellString(TxtMailboxUPN.Text.Trim());
                string delegateUser = string.IsNullOrWhiteSpace(TxtDelegateUPN.Text) ? "<delegate@domain.com>" : EscapePowerShellString(TxtDelegateUPN.Text.Trim());
                sb.AppendLine("# Microsoft 365 Mailbox & Calendar Delegation");
                sb.AppendLine("Import-Module ExchangeOnlineManagement");
                sb.AppendLine("Connect-ExchangeOnline");
                string mailPerm = (ComboMailboxPerm.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "None";
                if (mailPerm != "None")
                {
                    sb.AppendLine($"# Granting Mailbox Permission: {mailPerm}");
                    if (mailPerm == "FullAccess") sb.AppendLine($"Add-MailboxPermission -Identity \"{mailbox}\" -User \"{delegateUser}\" -AccessRights FullAccess -InheritanceType All");
                    else if (mailPerm == "SendAs") sb.AppendLine($"Add-RecipientPermission -Identity \"{mailbox}\" -Trustee \"{delegateUser}\" -AccessRights SendAs -Confirm:$false");
                    else if (mailPerm == "SendOnBehalf") sb.AppendLine($"Set-Mailbox -Identity \"{mailbox}\" -GrantSendOnBehalfTo \"{delegateUser}\"");
                }
                string calPerm = (ComboCalendarPerm.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "None";
                if (calPerm != "None")
                {
                    string permValue = calPerm.Split(' ')[0];
                    sb.AppendLine($"$targetPath = \"{mailbox}:\\Calendar\"");
                    sb.AppendLine("try {");
                    sb.AppendLine($"    Add-MailboxFolderPermission -Identity $targetPath -User \"{delegateUser}\" -AccessRights {permValue} -ErrorAction Stop");
                    sb.AppendLine("} catch {");
                    sb.AppendLine($"    Set-MailboxFolderPermission -Identity $targetPath -User \"{delegateUser}\" -AccessRights {permValue}");
                    sb.AppendLine("}");
                }
            }
            else if (header == "User Security")
            {
                string user = string.IsNullOrWhiteSpace(TxtSecurityUserUPN.Text) ? "<user@domain.com>" : EscapePowerShellString(TxtSecurityUserUPN.Text.Trim());
                string pass = EscapePowerShellString(TxtResetPassword.Text);
                sb.AppendLine("# Microsoft 365 Account Security & Password Management");
                sb.AppendLine("Import-Module Microsoft.Graph");
                sb.AppendLine("Connect-MgGraph -Scopes 'User.ReadWrite.All'");
                if (!string.IsNullOrWhiteSpace(pass))
                {
                    bool forceChange = ChkForceChange.IsChecked ?? false;
                    sb.AppendLine("$params = @{");
                    sb.AppendLine("    passwordProfile = @{");
                    sb.AppendLine($"        forceChangePasswordNextSignIn = ${forceChange.ToString().ToLowerInvariant()}");
                    sb.AppendLine($"        password = \"{pass}\"");
                    sb.AppendLine("    }");
                    sb.AppendLine("}");
                    sb.AppendLine($"Update-MgUser -UserId \"{user}\" -BodyParameter $params");
                }
                if (ChkRevokeSessions.IsChecked ?? false) sb.AppendLine($"Revoke-MgUserSignInSession -UserId \"{user}\"");
                string accountState = (ComboAccountState.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Keep Current State";
                if (accountState != "Keep Current State")
                {
                    bool enabled = accountState.StartsWith("Enabled", StringComparison.OrdinalIgnoreCase);
                    sb.AppendLine($"Update-MgUser -UserId \"{user}\" -AccountEnabled ${enabled.ToString().ToLowerInvariant()}");
                }
            }
            else if (header == "Teams & Groups")
            {
                string group = string.IsNullOrWhiteSpace(TxtGroupUPN.Text) ? "<Group-UPN-or-GUID>" : EscapePowerShellString(TxtGroupUPN.Text.Trim());
                string member = string.IsNullOrWhiteSpace(TxtGroupMemberUPN.Text) ? "<user@domain.com>" : EscapePowerShellString(TxtGroupMemberUPN.Text.Trim());
                string operation = (ComboGroupOp.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Add to Group / Team";
                bool isOwner = RadioOwner.IsChecked ?? false;
                sb.AppendLine("# Microsoft 365 Group & Teams Member Manager");
                sb.AppendLine("Import-Module Microsoft.Graph");
                sb.AppendLine("Connect-MgGraph -Scopes 'Group.ReadWrite.All'");
                sb.AppendLine($"$targetUser = Get-MgUser -UserId \"{member}\"");
                if (operation.StartsWith("Add", StringComparison.OrdinalIgnoreCase))
                    sb.AppendLine(isOwner ? $"Add-MgGroupOwnerByRef -GroupId \"{group}\" -OdataId $targetUser.Id" : $"Add-MgGroupMemberByRef -GroupId \"{group}\" -OdataId $targetUser.Id");
                else
                    sb.AppendLine(isOwner ? $"Remove-MgGroupOwnerByRef -GroupId \"{group}\" -DirectoryObjectId $targetUser.Id" : $"Remove-MgGroupMemberByRef -GroupId \"{group}\" -DirectoryObjectId $targetUser.Id");
            }
            TxtScriptPreview.Text = sb.ToString();
        }

        private void BtnCopyScript_Click(object sender, RoutedEventArgs e)
        {
            try { Clipboard.SetText(TxtScriptPreview.Text); TxtStatus.Text = "Script copied to clipboard!"; }
            catch (Exception ex) { ModernMessageBox.Show($"Failed to copy script: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error); }
        }

        private void BtnRunScript_Click(object sender, RoutedEventArgs e)
        {
            var result = ModernMessageBox.Show("This will launch a visible PowerShell window to run the generated commands.\n\nYou may be prompted to sign into Microsoft 365.\n\nExecute script?", "Run Script", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (result != MessageBoxResult.Yes) return;
            if (Owner is MainWindow mainWindow) mainWindow.LogAuditAction("Executed Microsoft 365 / Teams automation script.");
            try { RunPowerShellInteractive(TxtScriptPreview.Text); }
            catch (Exception ex)
            {
                Logger.Error("Microsoft 365 script launch failed", ex, "M365");
                ModernMessageBox.Show($"Failed to start PowerShell: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}
