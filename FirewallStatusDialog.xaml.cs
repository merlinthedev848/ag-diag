using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Text.Json;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace agilicomsptoolkit
{
    public class FirewallProfileInfo
    {
        public string Name { get; set; } = string.Empty;
        public string StatusText { get; set; } = string.Empty;
        public string InboundAction { get; set; } = string.Empty;
        public string OutboundAction { get; set; } = string.Empty;

        private static readonly Brush ActiveIconBg = CreateFrozenBrush("#064e3b");
        private static readonly Brush ActiveIconFg = CreateFrozenBrush("#10b981");
        private static readonly Brush ActiveBadgeBg = CreateFrozenBrush("#10b981");
        
        private static readonly Brush InactiveIconBg = CreateFrozenBrush("#7f1d1d");
        private static readonly Brush InactiveIconFg = CreateFrozenBrush("#ef4444");
        private static readonly Brush InactiveBadgeBg = CreateFrozenBrush("#ef4444");

        public static Brush CreateFrozenBrush(string hexColor)
        {
            var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hexColor));
            brush.Freeze();
            return brush;
        }

        public Brush IconBgBrush => StatusText == "ON" ? ActiveIconBg : InactiveIconBg;
        public Brush IconFgBrush => StatusText == "ON" ? ActiveIconFg : InactiveIconFg;
        public Brush BadgeBgBrush => StatusText == "ON" ? ActiveBadgeBg : InactiveBadgeBg;
        public Brush BadgeFgBrush => Brushes.White;
        public Color GlowColor => StatusText == "ON" ? (Color)ColorConverter.ConvertFromString("#10b981") : (Color)ColorConverter.ConvertFromString("#ef4444");
    }

    public partial class FirewallStatusDialog : Window
    {
        public ObservableCollection<FirewallProfileInfo> Profiles { get; set; } = new ObservableCollection<FirewallProfileInfo>();

        public FirewallStatusDialog()
        {
            InitializeComponent();
            DataContext = this;
        }

        private async void Window_Loaded(object sender, RoutedEventArgs e)
        {
            await LoadFirewallStatusAsync();
        }

        private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
                DragMove();
        }

        private async System.Threading.Tasks.Task LoadFirewallStatusAsync()
        {
            Profiles.Clear();
            try
            {
                string jsonCommand = "Get-NetFirewallProfile | Select-Object Name, Enabled, DefaultInboundAction, DefaultOutboundAction | ConvertTo-Json -Compress";
                
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
                    int activeCount = 0;
                    var profileList = new System.Collections.Generic.List<FirewallProfileInfo>();

                    if (doc.RootElement.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var element in doc.RootElement.EnumerateArray())
                        {
                            ParseProfileElement(element, profileList, ref activeCount);
                        }
                    }
                    else if (doc.RootElement.ValueKind == JsonValueKind.Object)
                    {
                        ParseProfileElement(doc.RootElement, profileList, ref activeCount);
                    }

                    foreach (var p in profileList)
                    {
                        Profiles.Add(p);
                    }

                    ItemsProfiles.ItemsSource = Profiles;

                    if (activeCount == 3)
                        TxtOverallStatus.Text = "System is fully protected. All firewall profiles are active.";
                    else if (activeCount > 0)
                        TxtOverallStatus.Text = $"System is partially protected. {activeCount} of 3 profiles active.";
                    else
                    {
                        TxtOverallStatus.Text = "WARNING: Windows Firewall is completely disabled!";
                        TxtOverallStatus.Foreground = FirewallProfileInfo.CreateFrozenBrush("#ef4444");
                    }
                }
            }
            catch (Exception ex)
            {
                ModernMessageBox.Show($"Failed to load firewall status: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ParseProfileElement(JsonElement element, System.Collections.Generic.List<FirewallProfileInfo> list, ref int activeCount)
        {
            string name = element.GetProperty("Name").GetString() ?? "Unknown";
            int enabledVal = element.GetProperty("Enabled").GetInt32();
            bool isEnabled = enabledVal == 1;
            
            string inbound = element.GetProperty("DefaultInboundAction").GetInt32() == 1 ? "Allow" : "Block";
            string outbound = element.GetProperty("DefaultOutboundAction").GetInt32() == 1 ? "Allow" : "Block";

            if (isEnabled) activeCount++;

            var info = new FirewallProfileInfo
            {
                Name = name,
                InboundAction = inbound,
                OutboundAction = outbound,
                StatusText = isEnabled ? "ON" : "OFF"
            };

            list.Add(info);
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
