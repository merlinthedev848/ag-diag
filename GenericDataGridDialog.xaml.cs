using System;
using System.Data;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Diagnostics;

namespace agilicomsptoolkit
{
    public partial class GenericDataGridDialog : Window
    {
        public GenericDataGridDialog(string title, string description, string jsonPayload)
        {
            InitializeComponent();
            TxtTitle.Text = title;
            TxtDescription.Text = description;
            
            if (title.Contains("Disk Management", StringComparison.OrdinalIgnoreCase))
            {
                DiskActionsPanel.Visibility = Visibility.Visible;
            }
            else if (title.Contains("Software Audit", StringComparison.OrdinalIgnoreCase))
            {
                CheckForUpdatesAsync();
            }

            LoadData(jsonPayload);
        }

        private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
                DragMove();
        }

        private void LoadData(string jsonPayload)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(jsonPayload))
                {
                    ModernMessageBox.Show("No data was returned.", "Info", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                DataTable table = new DataTable();
                using (JsonDocument doc = JsonDocument.Parse(jsonPayload))
                {
                    JsonElement root = doc.RootElement;

                    if (root.ValueKind == JsonValueKind.Array)
                    {
                        if (root.GetArrayLength() == 0) return;

                        // Create columns from the first element
                        JsonElement firstObj = root[0];
                        foreach (JsonProperty prop in firstObj.EnumerateObject())
                        {
                            table.Columns.Add(prop.Name, typeof(string));
                        }

                        // Add rows
                        foreach (JsonElement rowObj in root.EnumerateArray())
                        {
                            DataRow row = table.NewRow();
                            foreach (JsonProperty prop in rowObj.EnumerateObject())
                            {
                                row[prop.Name] = prop.Value.ToString();
                            }
                            table.Rows.Add(row);
                        }
                    }
                    else if (root.ValueKind == JsonValueKind.Object)
                    {
                        // Single object
                        foreach (JsonProperty prop in root.EnumerateObject())
                        {
                            table.Columns.Add(prop.Name, typeof(string));
                        }
                        DataRow row = table.NewRow();
                        foreach (JsonProperty prop in root.EnumerateObject())
                        {
                            row[prop.Name] = prop.Value.ToString();
                        }
                        table.Rows.Add(row);
                    }
                }

                GridGeneric.ItemsSource = table.DefaultView;
            }
            catch (Exception ex)
            {
                ModernMessageBox.Show($"Failed to parse output data: {ex.Message}", "Parsing Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void BtnRunDiskCleanup_Click(object sender, RoutedEventArgs e)
        {
            var result = ModernMessageBox.Show("Are you sure you want to run Disk Cleanup?", "Confirm Action", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (result != MessageBoxResult.Yes) return;

            try
            {
                if (Owner is MainWindow mainWindow) mainWindow.LogAuditAction("Launched Disk Cleanup utility.");
                
                Process.Start(new ProcessStartInfo
                {
                    FileName = "cleanmgr.exe",
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                ModernMessageBox.Show($"Failed to launch Disk Cleanup: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnRunDiskDefrag_Click(object sender, RoutedEventArgs e)
        {
            var result = ModernMessageBox.Show("Are you sure you want to run Disk Defragmenter?", "Confirm Action", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (result != MessageBoxResult.Yes) return;

            try
            {
                if (Owner is MainWindow mainWindow) mainWindow.LogAuditAction("Launched Disk Defragmenter utility.");

                Process.Start(new ProcessStartInfo
                {
                    FileName = "dfrgui.exe",
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                ModernMessageBox.Show($"Failed to launch Disk Defragmenter: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void CheckForUpdatesAsync()
        {
            try
            {
                SoftwareActionsPanel.Visibility = Visibility.Visible;
                TxtSoftwareUpdateStatus.Text = "Checking for updates...";
                BtnUpdateSoftware.IsEnabled = false;

                var procInfo = new ProcessStartInfo
                {
                    FileName = "winget",
                    Arguments = "upgrade",
                    RedirectStandardOutput = true,
                    CreateNoWindow = true,
                    UseShellExecute = false
                };

                using var proc = Process.Start(procInfo);
                if (proc == null) return;
                
                string output = await proc.StandardOutput.ReadToEndAsync();
                await proc.WaitForExitAsync();

                if (output.Contains("upgrades available", StringComparison.OrdinalIgnoreCase))
                {
                    TxtSoftwareUpdateStatus.Text = "Updates available via winget";
                    BtnUpdateSoftware.IsEnabled = true;
                }
                else
                {
                    SoftwareActionsPanel.Visibility = Visibility.Collapsed;
                }
            }
            catch
            {
                SoftwareActionsPanel.Visibility = Visibility.Collapsed;
            }
        }

        private void BtnUpdateSoftware_Click(object sender, RoutedEventArgs e)
        {
            var result = ModernMessageBox.Show("Are you sure you want to update all available software?\n\nThis may close running applications and take several minutes.", "Confirm Update", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (result != MessageBoxResult.Yes) return;

            try
            {
                if (Owner is MainWindow mainWindow) mainWindow.LogAuditAction("Initiated global software update via winget.");

                Process.Start(new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = "/c winget upgrade --all & pause",
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                ModernMessageBox.Show($"Failed to launch winget: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}
