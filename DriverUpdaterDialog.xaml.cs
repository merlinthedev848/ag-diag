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
            // WMI CIM format: yyyymmddhhmmss.xxxxxx+zzz
            if (raw.Length >= 8 && raw.Substring(0, 8).All(char.IsDigit))
            {
                string y = raw.Substring(0, 4);
                string m = raw.Substring(4, 2);
                string d = raw.Substring(6, 2);
                return $"{d}/{m}/{y}";
            }
            if (DateTime.TryParse(raw, out var dt))
            {
                return dt.ToString("dd/MM/yyyy");
            }
            return raw;
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
            _ = CheckForDriverUpdatesAsync(); // Run update check in background
        }

        private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
                DragMove();
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
                        var name = obj["DeviceName"]?.ToString()?.Trim() ?? "";
                        if (string.IsNullOrEmpty(name)) continue;

                        var item = new DriverItem
                        {
                            DeviceName = name,
                            DeviceClass = obj["DeviceClass"]?.ToString()?.Trim() ?? "Other",
                            Manufacturer = obj["Manufacturer"]?.ToString()?.Trim() ?? "Generic / Microsoft",
                            DriverVersion = obj["DriverVersion"]?.ToString()?.Trim() ?? "Unknown",
                            DriverDate = obj["DriverDate"]?.ToString()?.Trim() ?? ""
                        };
                        _allDrivers.Add(item);
                    }
                });

                ApplyFilter();
                TxtStatus.Text = "Installed drivers loaded";
                TxtSummary.Text = $"Total Installed Drivers: {_allDrivers.Count}";
            }
            catch (Exception ex)
            {
                TxtStatus.Text = "Scan error";
                ModernMessageBox.Show($"Failed to query installed drivers: {ex.Message}", "Driver Scan Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ApplyFilter()
        {
            if (TxtSummary == null || _allDrivers == null || _filteredDrivers == null || _availableUpdates == null) return;

            string search = TxtDriverSearch?.Text?.Trim().ToLowerInvariant() ?? "";
            string selectedClass = (CmbDriverClass?.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "All Classes";

            var filtered = _allDrivers.Where(d =>
            {
                bool matchesSearch = string.IsNullOrEmpty(search) ||
                                     d.DeviceName.ToLowerInvariant().Contains(search) ||
                                     d.Manufacturer.ToLowerInvariant().Contains(search) ||
                                     d.DeviceClass.ToLowerInvariant().Contains(search);

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
            foreach (var item in filtered)
            {
                _filteredDrivers.Add(item);
            }

            TxtSummary.Text = $"Showing {_filteredDrivers.Count} of {_allDrivers.Count} Installed Drivers | Available Updates: {_availableUpdates.Count}";
        }

        private void TxtDriverSearch_TextChanged(object sender, TextChangedEventArgs e)
        {
            ApplyFilter();
        }

        private void CmbDriverClass_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            ApplyFilter();
        }

        private async void BtnScanHardware_Click(object sender, RoutedEventArgs e)
        {
            BtnScanHardware.IsEnabled = false;
            TxtStatus.Text = "Scanning PnP bus for hardware changes...";

            try
            {
                await Task.Run(async () =>
                {
                    var psi = new ProcessStartInfo("pnputil", "/scan-devices")
                    {
                        CreateNoWindow = true,
                        UseShellExecute = false,
                        RedirectStandardOutput = true
                    };
                    using var p = Process.Start(psi);
                    if (p != null) await p.WaitForExitAsync();
                });

                await RefreshInstalledDriversAsync();
                ModernMessageBox.Show("PnP hardware bus scan completed.", "Scan Complete", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                ModernMessageBox.Show($"Failed to rescan PnP devices: {ex.Message}", "Scan Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                BtnScanHardware.IsEnabled = true;
                TxtStatus.Text = "Ready";
            }
        }

        private async void BtnCheckUpdates_Click(object sender, RoutedEventArgs e)
        {
            await CheckForDriverUpdatesAsync();
        }

        private async Task CheckForDriverUpdatesAsync()
        {
            BtnCheckUpdates.IsEnabled = false;
            TxtStatus.Text = "Checking Microsoft Update for driver packages...";
            _availableUpdates.Clear();
            PanelNoUpdates.Visibility = Visibility.Collapsed;
            GridDriverUpdates.Visibility = Visibility.Visible;

            try
            {
                string script = @"
$Session = New-Object -ComObject Microsoft.Update.Session
$Searcher = $Session.CreateUpdateSearcher()
$Searcher.ServerSelection = 2
try {
    $Results = $Searcher.Search(""IsInstalled=0 and Type='Driver'"")
    $List = @()
    foreach ($Update in $Results.Updates) {
        $List += [PSCustomObject]@{
            Title = $Update.Title
            DriverClass = if ($Update.DriverClass) { $Update.DriverClass } else { 'Driver' }
            DriverModel = if ($Update.DriverModel) { $Update.DriverModel } else { '-' }
            DriverDate = if ($Update.DriverVerDate) { $Update.DriverVerDate.ToString('dd/MM/yyyy') } else { '-' }
        }
    }
    $List | ConvertTo-Json -Compress
} catch {
    Write-Output '[]'
}
";
                string jsonOutput = string.Empty;
                await Task.Run(async () =>
                {
                    var psi = new ProcessStartInfo("powershell", $"-NoProfile -ExecutionPolicy Bypass -Command \"{script.Replace("\"", "\\\"")}\"")
                    {
                        CreateNoWindow = true,
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true
                    };
                    using var p = Process.Start(psi);
                    if (p != null)
                    {
                        jsonOutput = await p.StandardOutput.ReadToEndAsync();
                        await p.WaitForExitAsync();
                    }
                });

                if (!string.IsNullOrWhiteSpace(jsonOutput) && jsonOutput.Trim() != "[]")
                {
                    try
                    {
                        if (jsonOutput.Trim().StartsWith("["))
                        {
                            var updates = JsonSerializer.Deserialize<List<DriverUpdateItem>>(jsonOutput);
                            if (updates != null)
                            {
                                foreach (var u in updates) _availableUpdates.Add(u);
                            }
                        }
                        else if (jsonOutput.Trim().StartsWith("{"))
                        {
                            var single = JsonSerializer.Deserialize<DriverUpdateItem>(jsonOutput);
                            if (single != null) _availableUpdates.Add(single);
                        }
                    }
                    catch { }
                }

                if (_availableUpdates.Count > 0)
                {
                    TxtStatus.Text = $"{_availableUpdates.Count} driver update(s) available";
                    TxtUpdateProgress.Text = $"Found {_availableUpdates.Count} driver update(s) ready to install.";
                    BtnInstallAllDrivers.Visibility = Visibility.Visible;
                    GridDriverUpdates.Visibility = Visibility.Visible;
                    PanelNoUpdates.Visibility = Visibility.Collapsed;
                    DriverTabControl.SelectedIndex = 1; // Switch to Updates tab
                }
                else
                {
                    TxtStatus.Text = "All drivers up to date";
                    TxtUpdateProgress.Text = "No pending driver packages found via Windows Update.";
                    BtnInstallAllDrivers.Visibility = Visibility.Collapsed;
                    GridDriverUpdates.Visibility = Visibility.Collapsed;
                    PanelNoUpdates.Visibility = Visibility.Visible;
                }

                ApplyFilter();
            }
            catch (Exception ex)
            {
                TxtStatus.Text = "Update check failed";
                TxtUpdateProgress.Text = $"Update check failed: {ex.Message}";
            }
            finally
            {
                BtnCheckUpdates.IsEnabled = true;
            }
        }

        private async void BtnInstallAllDrivers_Click(object sender, RoutedEventArgs e)
        {
            if (_availableUpdates.Count == 0) return;

            var confirm = ModernMessageBox.Show(
                $"This will download and install {_availableUpdates.Count} driver updates via Windows Update.\n\nNote: Graphics or network adapter drivers may cause momentary display flashing or temporary network reconnect.\n\nContinue with installation?",
                "Install Driver Updates",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (confirm != MessageBoxResult.Yes) return;

            BtnInstallAllDrivers.IsEnabled = false;
            BtnCheckUpdates.IsEnabled = false;
            TxtStatus.Text = "Downloading and installing driver packages...";
            TxtUpdateProgress.Text = "Starting Windows Update Agent driver installer...";

            string installScript = @"
$Session = New-Object -ComObject Microsoft.Update.Session
$Searcher = $Session.CreateUpdateSearcher()
$Searcher.ServerSelection = 2
$Results = $Searcher.Search(""IsInstalled=0 and Type='Driver'"")

if ($Results.Updates.Count -eq 0) {
    Write-Output 'NO_UPDATES'
    exit
}

$Downloader = $Session.CreateUpdateDownloader()
$Downloader.Updates = $Results.Updates
Write-Output 'DOWNLOADING'
$Downloader.Download()

$UpdatesToInstall = New-Object -ComObject Microsoft.Update.UpdateColl
foreach ($Update in $Results.Updates) {
    if ($Update.IsDownloaded) {
        $UpdatesToInstall.Add($Update) | Out-Null
    }
}

if ($UpdatesToInstall.Count -gt 0) {
    $Installer = $Session.CreateUpdateInstaller()
    $Installer.Updates = $UpdatesToInstall
    Write-Output 'INSTALLING'
    $Result = $Installer.Install()
    Write-Output ""RESULT::$($Result.ResultCode)""
} else {
    Write-Output 'DOWNLOAD_FAILED'
}
";

            try
            {
                string output = string.Empty;
                await Task.Run(async () =>
                {
                    var psi = new ProcessStartInfo("powershell", $"-NoProfile -ExecutionPolicy Bypass -Command \"{installScript.Replace("\"", "\\\"")}\"")
                    {
                        CreateNoWindow = true,
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true
                    };
                    using var p = Process.Start(psi);
                    if (p != null)
                    {
                        output = await p.StandardOutput.ReadToEndAsync();
                        await p.WaitForExitAsync();
                    }
                });

                bool rebootRequired = output.Contains("True", StringComparison.OrdinalIgnoreCase);

                if (output.Contains("RESULT:2", StringComparison.OrdinalIgnoreCase)) // 2 = OperationResultCode.orcSucceeded
                {
                    TxtStatus.Text = "Drivers installed successfully";
                    TxtUpdateProgress.Text = rebootRequired
                        ? "Installation completed successfully. A system restart is required to apply changes."
                        : "Installation completed successfully.";

                    string msg = "Driver updates installed successfully.";
                    if (rebootRequired)
                    {
                        msg += "\n\nA system restart is recommended to complete driver initialization.";
                    }
                    ModernMessageBox.Show(msg, "Driver Update Complete", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else
                {
                    TxtStatus.Text = "Installation finished";
                    TxtUpdateProgress.Text = "Driver update process completed.";
                    ModernMessageBox.Show("Driver installation routine completed. Re-checking updates...", "Update Complete", MessageBoxButton.OK, MessageBoxImage.Information);
                }

                await RefreshInstalledDriversAsync();
                await CheckForDriverUpdatesAsync();
            }
            catch (Exception ex)
            {
                TxtStatus.Text = "Installation error";
                TxtUpdateProgress.Text = $"Error installing drivers: {ex.Message}";
                ModernMessageBox.Show($"Error during driver installation: {ex.Message}", "Installation Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                BtnInstallAllDrivers.IsEnabled = true;
                BtnCheckUpdates.IsEnabled = true;
            }
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

                if (sfd.ShowDialog(this) == true)
                {
                    var sb = new StringBuilder();
                    sb.AppendLine("Device Name,Class,Manufacturer,Driver Version,Driver Date");

                    foreach (var d in _filteredDrivers)
                    {
                        string escape(string s) => $"\"{s.Replace("\"", "\"\"")}\"";
                        sb.AppendLine($"{escape(d.DeviceName)},{escape(d.DeviceClass)},{escape(d.Manufacturer)},{escape(d.DriverVersion)},{escape(d.DriverDateDisplay)}");
                    }

                    File.WriteAllText(sfd.FileName, sb.ToString(), Encoding.UTF8);
                    ModernMessageBox.Show($"Successfully exported {_filteredDrivers.Count} driver records to:\n\n{sfd.FileName}", "Export Successful", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                ModernMessageBox.Show($"Failed to export CSV: {ex.Message}", "Export Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
