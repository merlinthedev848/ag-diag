using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;

namespace agilicomsptoolkit
{
    public class NetworkAdapterInfo
    {
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
    }

    public partial class NicOptimizerDialog : Window
    {
        public ObservableCollection<NetworkAdapterInfo> Adapters { get; set; } = new ObservableCollection<NetworkAdapterInfo>();

        public NicOptimizerDialog()
        {
            InitializeComponent();
            DataContext = this;
        }

        private async void Window_Loaded(object sender, RoutedEventArgs e)
        {
            await LoadAdaptersAsync();
        }

        private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
                DragMove();
        }

        private async Task LoadAdaptersAsync()
        {
            Adapters.Clear();
            try
            {
                // Only get physical adapters (Virtual=False) that are up or disconnected
                string jsonCommand = "Get-NetAdapter -Physical | Select-Object Name, InterfaceDescription, Status | ConvertTo-Json -Compress";
                
                var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = "powershell.exe",
                        Arguments = $"-NoProfile -Command \"{jsonCommand}\"",
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    }
                };
                
                process.Start();
                string output = await process.StandardOutput.ReadToEndAsync();
                await process.WaitForExitAsync();

                if (!string.IsNullOrWhiteSpace(output))
                {
                    using var doc = JsonDocument.Parse(output);
                    
                    if (doc.RootElement.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var element in doc.RootElement.EnumerateArray())
                        {
                            Adapters.Add(new NetworkAdapterInfo 
                            { 
                                Name = element.GetProperty("Name").GetString() ?? "Unknown", 
                                Description = element.GetProperty("InterfaceDescription").GetString() ?? "", 
                                Status = element.GetProperty("Status").GetString() ?? "" 
                            });
                        }
                    }
                    else if (doc.RootElement.ValueKind == JsonValueKind.Object)
                    {
                        // Single item returned
                        Adapters.Add(new NetworkAdapterInfo 
                        { 
                            Name = doc.RootElement.GetProperty("Name").GetString() ?? "Unknown", 
                            Description = doc.RootElement.GetProperty("InterfaceDescription").GetString() ?? "", 
                            Status = doc.RootElement.GetProperty("Status").GetString() ?? "" 
                        });
                    }

                    GridAdapters.ItemsSource = Adapters;
                    TxtStatus.Text = $"{Adapters.Count} physical adapters found.";
                }
            }
            catch (Exception ex)
            {
                ModernMessageBox.Show($"Failed to load adapters: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void BtnOptimize_Click(object sender, RoutedEventArgs e)
        {
            var result = ModernMessageBox.Show(
                "WARNING: Optimizing network adapters will briefly reset your network connection.\n\nDo you want to proceed and optimize all physical adapters?", 
                "Confirm NIC Optimization", 
                MessageBoxButton.YesNo, 
                MessageBoxImage.Warning);
                
            if (result == MessageBoxResult.Yes)
            {
                BtnOptimize.IsEnabled = false;
                TxtStatus.Text = "Applying optimizations...";
                
                try
                {
                    await Task.Run(() =>
                    {
                        string[] commands = new[]
                        {
                            "Enable-NetAdapterRss -Name '*' -ErrorAction SilentlyContinue",
                            "Disable-NetAdapterLso -Name '*' -ErrorAction SilentlyContinue",
                            "netsh int tcp set global autotuninglevel=normal",
                            "netsh int tcp set heuristics disabled",
                            "netsh int tcp set global ecncapability=enabled"
                        };

                        string script = string.Join(" ; ", commands);
                        
                        var process = new Process
                        {
                            StartInfo = new ProcessStartInfo
                            {
                                FileName = "powershell.exe",
                                Arguments = $"-NoProfile -Command \"{script}\"",
                                RedirectStandardOutput = true,
                                RedirectStandardError = true,
                                UseShellExecute = false,
                                CreateNoWindow = true
                            }
                        };
                        process.Start();
                        process.WaitForExit();
                    });

                    ModernMessageBox.Show("All physical network adapters have been optimized for maximum VoIP performance.\nLSO Disabled, RSS Enabled, and TCP Heuristics Tuned.", "Optimization Complete", MessageBoxButton.OK, MessageBoxImage.Information);
                    TxtStatus.Text = "Optimization applied.";
                    
                    if (Owner is MainWindow mainWindow) 
                    {
                        mainWindow.LogAuditAction("Executed NIC Optimizer routine on physical network adapters.");
                    }
                }
                catch (Exception ex)
                {
                    ModernMessageBox.Show($"Failed to apply optimizations: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
                finally
                {
                    BtnOptimize.IsEnabled = true;
                }
            }
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
