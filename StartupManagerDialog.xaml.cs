using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;
using System.Threading.Tasks;

namespace agilicomsptoolkit
{
    public class StartupItem : System.ComponentModel.INotifyPropertyChanged
    {
        public string Name { get; set; } = string.Empty;
        public string Command { get; set; } = string.Empty;
        public string User { get; set; } = string.Empty;
        
        // Internal tracking
        public string RegPath { get; set; } = string.Empty;
        public RegistryHive Hive { get; set; }
        
        private string _state = string.Empty;
        public string State 
        { 
            get => _state; 
            set { _state = value; OnPropertyChanged(nameof(State)); RefreshProperties(); } 
        }

        private static readonly Brush EnabledStatusBrush = CreateFrozenBrush("#10b981");
        private static readonly Brush DisabledStatusBrush = CreateFrozenBrush("#94a3b8");
        private static readonly Brush DisableActionBgBrush = CreateFrozenBrush("#ef4444");
        private static readonly Brush EnableActionBgBrush = CreateFrozenBrush("#3b82f6");

        private static Brush CreateFrozenBrush(string hexColor)
        {
            var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hexColor));
            brush.Freeze();
            return brush;
        }

        public Brush StatusColorBrush => State == "Enabled" ? EnabledStatusBrush : DisabledStatusBrush;
            
        public Color StatusGlowColor => State == "Enabled" 
            ? (Color)ColorConverter.ConvertFromString("#10b981") 
            : Colors.Transparent;

        public string ActionText => State == "Enabled" ? "Disable" : "Enable";
        
        public Brush ActionBgBrush => State == "Enabled" ? DisableActionBgBrush : EnableActionBgBrush;

        public void RefreshProperties()
        {
            OnPropertyChanged(nameof(StatusColorBrush));
            OnPropertyChanged(nameof(StatusGlowColor));
            OnPropertyChanged(nameof(ActionText));
            OnPropertyChanged(nameof(ActionBgBrush));
        }

        public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(propertyName));
        }
    }

    public partial class StartupManagerDialog : Window
    {
        public ObservableCollection<StartupItem> StartupApps { get; set; } = new ObservableCollection<StartupItem>();

        public StartupManagerDialog()
        {
            InitializeComponent();
            DataContext = this;
            Loaded += Window_Loaded;
        }

        private async void Window_Loaded(object sender, RoutedEventArgs e)
        {
            await LoadStartupAppsAsync();
        }

        private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
                DragMove();
        }

        private async Task LoadStartupAppsAsync()
        {
            StartupApps.Clear();
            try
            {
                var tempApps = new List<StartupItem>();
                await Task.Run(() =>
                {
                    LoadRegistryKey(Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Run", RegistryHive.CurrentUser, "Enabled", tempApps);
                    LoadRegistryKey(Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Run-Disabled", RegistryHive.CurrentUser, "Disabled", tempApps);
                    
                    LoadRegistryKey(Registry.LocalMachine, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", RegistryHive.LocalMachine, "Enabled", tempApps);
                    LoadRegistryKey(Registry.LocalMachine, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run-Disabled", RegistryHive.LocalMachine, "Disabled", tempApps);
                });

                foreach (var app in tempApps)
                {
                    StartupApps.Add(app);
                }
                
                GridStartup.ItemsSource = StartupApps;
                TxtStatus.Text = $"{StartupApps.Count} startup apps loaded.";
            }
            catch (Exception ex)
            {
                ModernMessageBox.Show($"Failed to load startup apps: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void LoadRegistryKey(RegistryKey baseKey, string subKeyPath, RegistryHive hive, string state, List<StartupItem> list)
        {
            try
            {
                using var key = baseKey.OpenSubKey(subKeyPath);
                if (key == null) return;

                foreach (var valueName in key.GetValueNames())
                {
                    var value = key.GetValue(valueName)?.ToString() ?? string.Empty;
                    list.Add(new StartupItem
                    {
                        Name = valueName,
                        Command = value,
                        User = hive == RegistryHive.CurrentUser ? "Current User" : "System (All Users)",
                        RegPath = subKeyPath,
                        Hive = hive,
                        State = state
                    });
                }
            }
            catch { /* Ignore access denied errors on some keys */ }
        }

        private async void BtnRefresh_Click(object sender, RoutedEventArgs e)
        {
            await LoadStartupAppsAsync();
        }

        private void BtnAction_Click(object sender, RoutedEventArgs e)
        {
            if (sender is System.Windows.Controls.Button btn && btn.Tag is StartupItem item)
            {
                try
                {
                    string targetState = item.State == "Enabled" ? "Disabled" : "Enabled";
                    string currentKeyPath = item.RegPath;
                    string targetKeyPath = targetState == "Enabled" 
                        ? currentKeyPath.Replace("-Disabled", "")
                        : currentKeyPath.EndsWith("-Disabled") ? currentKeyPath : currentKeyPath + "-Disabled";

                    RegistryKey baseKey = item.Hive == RegistryHive.CurrentUser ? Registry.CurrentUser : Registry.LocalMachine;

                    // Ensure target key exists
                    using (var createKey = baseKey.CreateSubKey(targetKeyPath)) { }

                    // Move value
                    using (var sourceKey = baseKey.OpenSubKey(currentKeyPath, true))
                    using (var targetKey = baseKey.OpenSubKey(targetKeyPath, true))
                    {
                        if (sourceKey != null && targetKey != null)
                        {
                            var value = sourceKey.GetValue(item.Name);
                            if (value != null)
                            {
                                targetKey.SetValue(item.Name, value, sourceKey.GetValueKind(item.Name));
                                sourceKey.DeleteValue(item.Name);
                                
                                item.RegPath = targetKeyPath;
                                item.State = targetState;
                                
                                if (Owner is MainWindow mainWindow) 
                                {
                                    mainWindow.LogAuditAction($"Successfully {item.State.ToLower()} startup app: {item.Name}");
                                }
                            }
                        }
                    }
                }
                catch (UnauthorizedAccessException)
                {
                    ModernMessageBox.Show("Administrator privileges are required to modify system-wide (All Users) startup apps.", "Access Denied", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
                catch (Exception ex)
                {
                    ModernMessageBox.Show($"Failed to toggle startup app: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
