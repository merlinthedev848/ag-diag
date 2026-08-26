using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;
using System.Collections.Generic;

namespace agilicomsptoolkit
{
    public class SoftwareInfo : System.ComponentModel.INotifyPropertyChanged
    {
        public string Name { get; set; } = string.Empty;
        public string Version { get; set; } = string.Empty;
        
        private string _availableVersion = "-";
        public string AvailableVersion
        {
            get => _availableVersion;
            set { _availableVersion = value; OnPropertyChanged(nameof(AvailableVersion)); }
        }

        private Brush _updateBgBrush = Brushes.Transparent;
        public Brush UpdateBgBrush
        {
            get => _updateBgBrush;
            set { _updateBgBrush = value; OnPropertyChanged(nameof(UpdateBgBrush)); }
        }

        private static readonly Brush DefaultUpdateFgBrush = CreateFrozenBrush("#94a3b8");
        private Brush _updateFgBrush = DefaultUpdateFgBrush;
        public Brush UpdateFgBrush
        {
            get => _updateFgBrush;
            set { _updateFgBrush = value; OnPropertyChanged(nameof(UpdateFgBrush)); }
        }

        public static Brush CreateFrozenBrush(string hexColor)
        {
            var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hexColor));
            brush.Freeze();
            return brush;
        }

        public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(propertyName));
        }
    }

    public partial class SoftwareAuditDialog : Window
    {
        public ObservableCollection<SoftwareInfo> SoftwareList { get; set; } = new ObservableCollection<SoftwareInfo>();
        private bool _updatesAvailable = false;

        public SoftwareAuditDialog()
        {
            InitializeComponent();
            DataContext = this;
        }

        private async void Window_Loaded(object sender, RoutedEventArgs e)
        {
            await LoadInstalledSoftwareAsync();
            _ = CheckForUpdatesAsync(); // run in background
        }

        private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
                DragMove();
        }

        private async Task LoadInstalledSoftwareAsync()
        {
            SoftwareList.Clear();
            try
            {
                await Task.Run(() =>
                {
                    var list = new List<SoftwareInfo>();
                    ReadUninstallRegistry(Registry.LocalMachine, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall", list);
                    ReadUninstallRegistry(Registry.LocalMachine, @"SOFTWARE\Wow6432Node\Microsoft\Windows\CurrentVersion\Uninstall", list);
                    ReadUninstallRegistry(Registry.CurrentUser, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall", list);

                    var sortedList = list.OrderBy(s => s.Name).ToList();

                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        foreach (var item in sortedList)
                        {
                            SoftwareList.Add(item);
                        }
                        GridSoftware.ItemsSource = SoftwareList;
                        TxtStatus.Text = $"Loaded {SoftwareList.Count} applications. Checking for updates...";
                    });
                });
            }
            catch (Exception ex)
            {
                ModernMessageBox.Show($"Failed to load software: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ReadUninstallRegistry(RegistryKey baseKey, string subKeyPath, List<SoftwareInfo> list)
        {
            try
            {
                using var key = baseKey.OpenSubKey(subKeyPath);
                if (key == null) return;

                foreach (var subkeyName in key.GetSubKeyNames())
                {
                    try
                    {
                        using var subkey = key.OpenSubKey(subkeyName);
                        if (subkey == null) continue;

                        string name = subkey.GetValue("DisplayName")?.ToString() ?? "";
                        string version = subkey.GetValue("DisplayVersion")?.ToString() ?? "-";

                        if (!string.IsNullOrWhiteSpace(name))
                        {
                            if (!list.Any(s => s.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
                            {
                                list.Add(new SoftwareInfo { Name = name, Version = version });
                            }
                        }
                    }
                    catch { /* skip unreadable entries */ }
                }
            }
            catch { /* skip unreadable parent keys */ }
        }

        private async Task CheckForUpdatesAsync()
        {
            try
            {
                using var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = "winget.exe",
                        Arguments = "upgrade",
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    }
                };

                process.Start();
                var errorTask = process.StandardError.ReadToEndAsync();
                string output = await process.StandardOutput.ReadToEndAsync();
                await process.WaitForExitAsync();
                _ = await errorTask;

                int updateCount = 0;
                string[] lines = output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                
                bool parsing = false;
                foreach (var line in lines)
                {
                    if (line.StartsWith("Name ") && line.Contains("Id ") && line.Contains("Version ") && line.Contains("Available "))
                    {
                        parsing = true;
                        continue;
                    }

                    if (parsing && !string.IsNullOrWhiteSpace(line))
                    {
                        if (line.StartsWith("-") || line.Contains("upgrades available")) continue;

                        var match = Regex.Match(line, @"^(.+?)\s+([A-Za-z0-9\.\-_]+)\s+([0-9\.\-]+)\s+([0-9\.\-]+)\s+");
                        if (match.Success)
                        {
                            string name = match.Groups[1].Value.Trim();
                            string availableVersion = match.Groups[4].Value.Trim();

                            // Find in our list
                            SoftwareInfo? item = null;
                            Application.Current.Dispatcher.Invoke(() =>
                            {
                                item = SoftwareList.FirstOrDefault(s => s.Name.StartsWith(name, StringComparison.OrdinalIgnoreCase) || name.StartsWith(s.Name, StringComparison.OrdinalIgnoreCase));
                                if (item != null)
                                {
                                    var orangeBrush = SoftwareInfo.CreateFrozenBrush("#f59e0b");
                                    item.AvailableVersion = availableVersion;
                                    item.UpdateBgBrush = orangeBrush;
                                    item.UpdateFgBrush = Brushes.White;
                                    updateCount++;
                                    _updatesAvailable = true;
                                }
                            });
                        }
                    }
                }

                Application.Current.Dispatcher.Invoke(() =>
                {
                    if (_updatesAvailable)
                    {
                        TxtStatus.Text = $"{updateCount} updates available.";
                        SoftwareActionsPanel.Visibility = Visibility.Visible;
                    }
                    else
                    {
                        TxtStatus.Text = "All software is up to date.";
                    }
                });
            }
            catch (Exception)
            {
                Application.Current.Dispatcher.Invoke(() =>
                {
                    TxtStatus.Text = "Failed to check for updates.";
                });
            }
        }

        private async void BtnUpdateSoftware_Click(object sender, RoutedEventArgs e)
        {
            var result = ModernMessageBox.Show("Are you sure you want to update all available software?\n\nThis may close running applications and take several minutes.", "Confirm Update", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (result != MessageBoxResult.Yes) return;

            try
            {
                if (Owner is MainWindow mainWindow) mainWindow.LogAuditAction("Initiated global software update via winget.");

                BtnUpdateSoftware.IsEnabled = false;
                BtnUpdateSoftware.Content = "Updating...";

                var proc = Process.Start(new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = "/c winget upgrade --all & pause",
                    UseShellExecute = true
                });

                if (proc != null)
                {
                    await proc.WaitForExitAsync();
                }
            }
            catch (Exception ex)
            {
                ModernMessageBox.Show($"Failed to launch winget: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                BtnUpdateSoftware.IsEnabled = true;
                BtnUpdateSoftware.Content = "Update All Software";
            }
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
