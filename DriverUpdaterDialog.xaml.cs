using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Win32;

namespace agilicomsptoolkit
{
    public class DriverItem
    {
        public string DeviceName { get; set; } = string.Empty;
        public string DeviceClass { get; set; } = string.Empty;
        public string Manufacturer { get; set; } = string.Empty;
        public string DriverVersion { get; set; } = string.Empty;
        public string DriverDate { get; set; } = string.Empty;
        public string DriverDateDisplay => FormatDriverDate(DriverDate);

        private static string FormatDriverDate(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return "-";
            if (raw.Length >= 8 && raw[..8].All(char.IsDigit))
                return $"{raw[..4]}-{raw.Substring(4, 2)}-{raw.Substring(6, 2)}";
            return DateTime.TryParse(raw, out var dt) ? dt.ToString("yyyy-MM-dd") : raw;
        }
    }

    public class DriverUpdateItem
    {
        public string Title { get; set; } = string.Empty;
        public string DriverClass { get; set; } = string.Empty;
        public string DriverModel { get; set; } = string.Empty;
        public string DriverDate { get; set; } = string.Empty;
        public string Status { get; set; } = "Available";
    }

    public partial class DriverUpdaterDialog : Window
    {
        private readonly List<DriverItem> _allDrivers = new();
        private readonly ObservableCollection<DriverItem> _filteredDrivers = new();
        private readonly ObservableCollection<DriverUpdateItem> _availableUpdates = new();

        public DriverUpdaterDialog()
        {
            InitializeComponent();
            GridDrivers.ItemsSource = _filteredDrivers;
            GridDriverUpdates.ItemsSource = _availableUpdates;
        }

        private async void Window_Loaded(object sender, RoutedEventArgs e)
        {
            await RefreshInstalledDriversAsync();
            _ = CheckForDriverUpdatesAsync();
        }

        private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left) DragMove();
        }

        private async Task RefreshInstalledDriversAsync()
        {
            TxtStatus.Text = "Scanning installed hardware drivers...";
            _allDrivers.Clear();
            _filteredDrivers.Clear();
            try
            {
                await Task.Run(() =>
                {
                    using var searcher = new ManagementObjectSearcher(
                        "SELECT DeviceName, DeviceClass, Manufacturer, DriverVersion, DriverDate FROM Win32_PnPSignedDriver WHERE DeviceName IS NOT NULL AND DriverVersion IS NOT NULL");
                    foreach (ManagementObject obj in searcher.Get())
                    {
                        string name = obj["DeviceName"]?.ToString()?.Trim() ?? string.Empty;
                        if (name.Length == 0) continue;
                        _allDrivers.Add(new DriverItem
                        {
                            DeviceName = name,
                            DeviceClass = obj["DeviceClass"]?.ToString()?.Trim() ?? "Other",
                            Manufacturer = obj["Manufacturer"]?.ToString()?.Trim() ?? "Generic / Microsoft",
                            DriverVersion = obj["DriverVersion"]?.ToString()?.Trim() ?? "Unknown",
                            DriverDate = obj["DriverDate"]?.ToString()?.Trim() ?? string.Empty
                        });
                    }
                });
                ApplyFilter();
                TxtStatus.Text = "Installed drivers loaded";
                TxtSummary.Text = $"Total Installed Drivers: {_allDrivers.Count}";
            }
            catch (Exception ex)
            {
                TxtStatus.Text = "Scan error";
                Logger.Error("Driver inventory failed", ex, "Drivers");
                ModernMessageBox.Show($"Failed to query installed drivers: {ex.Message}", "Driver Scan Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ApplyFilter()
        {
            if (TxtSummary == null) return;
            string search = TxtDriverSearch?.Text?.Trim() ?? string.Empty;
            string selectedClass = (CmbDriverClass?.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "All Classes";
            var filtered = _allDrivers.Where(d =>
            {
                bool matchesSearch = search.Length == 0 || d.DeviceName.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                                     d.Manufacturer.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                                     d.DeviceClass.Contains(search, StringComparison.OrdinalIgnoreCase);
                bool matchesClass = selectedClass switch
                {
                    "Display / GPU" => d.DeviceClass.Equals("Display", StringComparison.OrdinalIgnoreCase),
                    "Network / Wi-Fi" => d.DeviceClass.Equals("Net", StringComparison.OrdinalIgnoreCase) || d.DeviceClass.Contains("Network", StringComparison.OrdinalIgnoreCase),
                    "Bluetooth" => d.DeviceClass.Contains("Bluetooth", StringComparison.OrdinalIgnoreCase),
                    "Audio / Media" => d.DeviceClass.Equals("MEDIA", StringComparison.OrdinalIgnoreCase) || d.DeviceClass.Equals("AudioEndpoint", StringComparison.OrdinalIgnoreCase),
                    "Storage / Disk" => d.DeviceClass.Equals("DiskDrive", StringComparison.OrdinalIgnoreCase) || d.DeviceClass.Equals("SCSIAdapter", StringComparison.OrdinalIgnoreCase) || d.DeviceClass.Equals("HDC", StringComparison.OrdinalIgnoreCase),
                    "System / Chipset" => d.DeviceClass.Equals("System", StringComparison.OrdinalIgnoreCase) || d.DeviceClass.Equals("Processor", StringComparison.OrdinalIgnoreCase),
                    _ => true
                };
                return matchesSearch && matchesClass;
            }).OrderBy(d => d.DeviceName).ToList();

            _filteredDrivers.Clear();
            foreach (var item in filtered) _filteredDrivers.Add(item);
            TxtSummary.Text = $"Showing {_filteredDrivers.Count} of {_allDrivers.Count} Installed Drivers | Available Updates: {_availableUpdates.Count}";
        }

        private void TxtDriverSearch_TextChanged(object sender, TextChangedEventArgs e) => ApplyFilter();
        private void CmbDriverClass_SelectionChanged(object sender, SelectionChangedEventArgs e) => ApplyFilter();

        private async void BtnScanHardware_Click(object sender, RoutedEventArgs e)
        {
            BtnScanHardware.IsEnabled = false;
            TxtStatus.Text = "Scanning PnP bus for hardware changes...";
            try
            {
                var psi = new ProcessStartInfo("pnputil.exe") { UseShellExecute = false, CreateNoWindow = true };
                psi.ArgumentList.Add("/scan-devices");
                using var p = Process.Start(psi);
                if (p == null) throw new InvalidOperationException("pnputil.exe could not be started.");
                await p.WaitForExitAsync();
                if (p.ExitCode != 0) throw new InvalidOperationException($"pnputil exited with code {p.ExitCode}.");
                await RefreshInstalledDriversAsync();
                ModernMessageBox.Show("PnP hardware bus scan completed.", "Scan Complete", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                Logger.Error("PnP device scan failed", ex, "Drivers");
                ModernMessageBox.Show($"Failed to rescan PnP devices: {ex.Message}", "Scan Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                BtnScanHardware.IsEnabled = true;
                TxtStatus.Text = "Ready";
            }
        }

        private async void BtnCheckUpdates_Click(object sender, RoutedEventArgs e) => await CheckForDriverUpdatesAsync();

        private async Task<string> RunPowerShellScriptAsync(string script)
        {
            string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AgilicoToolkit", "Temp");
            Directory.CreateDirectory(dir);
            string path = Path.Combine(dir, $"driver-{Guid.NewGuid():N}.ps1");
            try
            {
                await File.WriteAllTextAsync(path, script, new UTF8Encoding(false));
                var psi = new ProcessStartInfo
                {
                    FileName = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0", "powershell.exe"),
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };
                psi.ArgumentList.Add("-NoProfile");
                psi.ArgumentList.Add("-ExecutionPolicy");
                psi.ArgumentList.Add("RemoteSigned");
                psi.ArgumentList.Add("-File");
                psi.ArgumentList.Add(path);
                using var process = Process.Start(psi) ?? throw new InvalidOperationException("PowerShell could not be started.");
                Task<string> stdout = process.StandardOutput.ReadToEndAsync();
                Task<string> stderr = process.StandardError.ReadToEndAsync();
                await Task.WhenAll(stdout, stderr);
                await process.WaitForExitAsync();
                if (process.ExitCode != 0)
                    throw new InvalidOperationException(stderr.Result.Trim().Length > 0 ? stderr.Result.Trim() : $"PowerShell exited with code {process.ExitCode}.");
                return stdout.Result;
            }
            finally
            {
                try { if (File.Exists(path)) File.Delete(path); } catch { }
            }
        }

        private async Task CheckForDriverUpdatesAsync()
        {
            BtnCheckUpdates.IsEnabled = false;
            TxtStatus.Text = "Checking Microsoft Update for driver packages...";
            _availableUpdates.Clear();
            PanelNoUpdates.Visibility = Visibility.Collapsed;
            GridDriverUpdates.Visibility = Visibility.Visible;
            const string script = @"
$ErrorActionPreference = 'Stop'
$session = New-Object -ComObject Microsoft.Update.Session
$searcher = $session.CreateUpdateSearcher()
$searcher.ServerSelection = 2
$results = $searcher.Search(\"IsInstalled=0 and Type='Driver'\")
$list = @($results.Updates | ForEach-Object {
    [PSCustomObject]@{
        Title = $_.Title
        DriverClass = if ($_.DriverClass) { $_.DriverClass } else { 'Driver' }
        DriverModel = if ($_.DriverModel) { $_.DriverModel } else { '-' }
        DriverDate = if ($_.DriverVerDate) { $_.DriverVerDate.ToString('dd/MM/yyyy') } else { '-' }
        Status = 'Available'
    }
})
$list | ConvertTo-Json -Compress
";
            try
            {
                string json = await RunPowerShellScriptAsync(script);
                if (!string.IsNullOrWhiteSpace(json) && json.Trim() != "[]")
                {
                    if (json.TrimStart().StartsWith("["))
                    {
                        foreach (var item in JsonSerializer.Deserialize<List<DriverUpdateItem>>(json) ?? new()) _availableUpdates.Add(item);
                    }
                    else
                    {
                        var item = JsonSerializer.Deserialize<DriverUpdateItem>(json);
                        if (item != null) _availableUpdates.Add(item);
                    }
                }

                bool found = _availableUpdates.Count > 0;
                TxtStatus.Text = found ? $"{_availableUpdates.Count} driver update(s) available" : "All drivers up to date";
                TxtUpdateProgress.Text = found ? $"Found {_availableUpdates.Count} driver update(s) ready to install." : "No pending driver packages found via Windows Update.";
                BtnInstallAllDrivers.Visibility = found ? Visibility.Visible : Visibility.Collapsed;
                GridDriverUpdates.Visibility = found ? Visibility.Visible : Visibility.Collapsed;
                PanelNoUpdates.Visibility = found ? Visibility.Collapsed : Visibility.Visible;
                if (found) DriverTabControl.SelectedIndex = 1;
                ApplyFilter();
            }
            catch (Exception ex)
            {
                TxtStatus.Text = "Update check failed";
                TxtUpdateProgress.Text = ex.Message;
                Logger.Error("Driver update check failed", ex, "Drivers");
            }
            finally { BtnCheckUpdates.IsEnabled = true; }
        }

        private async void BtnInstallAllDrivers_Click(object sender, RoutedEventArgs e)
        {
            if (_availableUpdates.Count == 0) return;
            var confirm = ModernMessageBox.Show(
                $"This will download and install {_availableUpdates.Count} driver updates via Windows Update.\n\nNetwork or display drivers can temporarily disconnect the device. Continue?",
                "Install Driver Updates", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (confirm != MessageBoxResult.Yes) return;

            BtnInstallAllDrivers.IsEnabled = false;
            BtnCheckUpdates.IsEnabled = false;
            TxtStatus.Text = "Downloading and installing driver packages...";
            const string script = @"
$ErrorActionPreference = 'Stop'
$session = New-Object -ComObject Microsoft.Update.Session
$searcher = $session.CreateUpdateSearcher()
$searcher.ServerSelection = 2
$results = $searcher.Search(\"IsInstalled=0 and Type='Driver'\")
if ($results.Updates.Count -eq 0) { [PSCustomObject]@{ ResultCode = -1; RebootRequired = $false; Message = 'NO_UPDATES' } | ConvertTo-Json -Compress; exit }
$downloader = $session.CreateUpdateDownloader()
$downloader.Updates = $results.Updates
$download = $downloader.Download()
$updatesToInstall = New-Object -ComObject Microsoft.Update.UpdateColl
foreach ($update in $results.Updates) { if ($update.IsDownloaded) { [void]$updatesToInstall.Add($update) } }
if ($updatesToInstall.Count -eq 0) { [PSCustomObject]@{ ResultCode = -2; RebootRequired = $false; Message = 'DOWNLOAD_FAILED' } | ConvertTo-Json -Compress; exit }
$installer = $session.CreateUpdateInstaller()
$installer.Updates = $updatesToInstall
$result = $installer.Install()
[PSCustomObject]@{ ResultCode = [int]$result.ResultCode; RebootRequired = [bool]$result.RebootRequired; Message = 'COMPLETED' } | ConvertTo-Json -Compress
";
            try
            {
                string output = await RunPowerShellScriptAsync(script);
                var result = JsonSerializer.Deserialize<DriverInstallResult>(output);
                if (result?.ResultCode == 2)
                {
                    TxtStatus.Text = "Drivers installed successfully";
                    TxtUpdateProgress.Text = result.RebootRequired ? "Installation completed. A restart is required." : "Installation completed successfully.";
                    ModernMessageBox.Show(result.RebootRequired ? "Driver updates installed. Please restart Windows to complete initialization." : "Driver updates installed successfully.", "Driver Update Complete", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else
                {
                    TxtStatus.Text = "Driver installation did not complete";
                    TxtUpdateProgress.Text = result?.Message ?? "No structured result was returned.";
                    ModernMessageBox.Show($"Driver update result: {result?.Message ?? "Unknown"}", "Driver Update", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
                await RefreshInstalledDriversAsync();
                await CheckForDriverUpdatesAsync();
            }
            catch (Exception ex)
            {
                TxtStatus.Text = "Installation error";
                TxtUpdateProgress.Text = ex.Message;
                Logger.Error("Driver installation failed", ex, "Drivers");
                ModernMessageBox.Show($"Error during driver installation: {ex.Message}", "Installation Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                BtnInstallAllDrivers.IsEnabled = true;
                BtnCheckUpdates.IsEnabled = true;
            }
        }

        private sealed class DriverInstallResult
        {
            public int ResultCode { get; set; }
            public bool RebootRequired { get; set; }
            public string Message { get; set; } = string.Empty;
        }

        private void BtnExportCsv_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var sfd = new SaveFileDialog
                {
                    FileName = $"Driver_Inventory_{Environment.MachineName}_{DateTime.Now:yyyyMMdd_HHmm}.csv",
                    Filter = "CSV files (*.csv)|*.csv|All files (*.*)|*.*"
                };
                if (sfd.ShowDialog(this) != true) return;
                var sb = new StringBuilder("Device Name,Class,Manufacturer,Driver Version,Driver Date\r\n");
                foreach (var d in _filteredDrivers)
                {
                    static string Escape(string value) => $"\"{value.Replace("\"", "\"\"")}\"";
                    sb.AppendLine($"{Escape(d.DeviceName)},{Escape(d.DeviceClass)},{Escape(d.Manufacturer)},{Escape(d.DriverVersion)},{Escape(d.DriverDateDisplay)}");
                }
                File.WriteAllText(sfd.FileName, sb.ToString(), Encoding.UTF8);
                ModernMessageBox.Show($"Successfully exported {_filteredDrivers.Count} driver records.", "Export Successful", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                Logger.Error("Driver CSV export failed", ex, "Drivers");
                ModernMessageBox.Show($"Failed to export CSV: {ex.Message}", "Export Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e) => Close();
    }
}
