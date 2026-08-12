using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Text.Json;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace agilicomsptoolkit
{
    public class LocalUserInfo
    {
        public string Name { get; set; } = string.Empty;
        public string FullName { get; set; } = string.Empty;
        public Visibility FullNameVisibility => string.IsNullOrWhiteSpace(FullName) ? Visibility.Collapsed : Visibility.Visible;
        public string Description { get; set; } = string.Empty;
        
        public string StatusText { get; set; } = string.Empty;
        
        private static readonly Brush EnabledBg = CreateFrozenBrush("#064e3b");
        private static readonly Brush EnabledFg = CreateFrozenBrush("#10b981");
        private static readonly Brush DisabledBg = CreateFrozenBrush("#7f1d1d");
        private static readonly Brush DisabledFg = CreateFrozenBrush("#ef4444");

        private static Brush CreateFrozenBrush(string hexColor)
        {
            var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hexColor));
            brush.Freeze();
            return brush;
        }

        public Brush StatusBgBrush => StatusText == "ENABLED" ? EnabledBg : DisabledBg;
        public Brush StatusFgBrush => StatusText == "ENABLED" ? EnabledFg : DisabledFg;
    }

    public partial class LocalUsersDialog : Window
    {
        public ObservableCollection<LocalUserInfo> Users { get; set; } = new ObservableCollection<LocalUserInfo>();

        public LocalUsersDialog()
        {
            InitializeComponent();
            DataContext = this;
        }

        private async void Window_Loaded(object sender, RoutedEventArgs e)
        {
            await LoadUsersAsync();
        }

        private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
                DragMove();
        }

        private async System.Threading.Tasks.Task LoadUsersAsync()
        {
            Users.Clear();
            try
            {
                string jsonCommand = "Get-LocalUser | Select-Object Name, Enabled, FullName, Description | ConvertTo-Json -Compress";
                
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

                if (!string.IsNullOrWhiteSpace(output) && (output.TrimStart().StartsWith("[") || output.TrimStart().StartsWith("{")))
                {
                    using var doc = JsonDocument.Parse(output);
                    int activeCount = 0;
                    var userList = new System.Collections.Generic.List<LocalUserInfo>();

                    if (doc.RootElement.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var element in doc.RootElement.EnumerateArray())
                        {
                            ParseUserElement(element, userList, ref activeCount);
                        }
                    }
                    else if (doc.RootElement.ValueKind == JsonValueKind.Object)
                    {
                        ParseUserElement(doc.RootElement, userList, ref activeCount);
                    }

                    foreach (var u in userList)
                    {
                        Users.Add(u);
                    }

                    ItemsUsers.ItemsSource = Users;
                    TxtStatus.Text = $"{Users.Count} Local Accounts ({activeCount} Enabled)";
                }
            }
            catch (Exception ex)
            {
                ModernMessageBox.Show($"Failed to load users: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                TxtStatus.Text = "Error loading users";
            }
        }

        private void ParseUserElement(JsonElement element, System.Collections.Generic.List<LocalUserInfo> list, ref int activeCount)
        {
            string name = element.GetProperty("Name").GetString() ?? "Unknown";
            string fullName = "";
            if (element.TryGetProperty("FullName", out var fnProp) && fnProp.ValueKind == JsonValueKind.String)
                fullName = fnProp.GetString() ?? "";
                
            string desc = "";
            if (element.TryGetProperty("Description", out var dProp) && dProp.ValueKind == JsonValueKind.String)
                desc = dProp.GetString() ?? "";

            bool isEnabled = false;
            if (element.TryGetProperty("Enabled", out var eProp))
            {
                if (eProp.ValueKind == JsonValueKind.True) isEnabled = true;
                else if (eProp.ValueKind == JsonValueKind.False) isEnabled = false;
                else if (eProp.ValueKind == JsonValueKind.Number) isEnabled = eProp.GetInt32() == 1;
            }

            var userInfo = new LocalUserInfo
            {
                Name = name,
                FullName = fullName,
                Description = desc,
                StatusText = isEnabled ? "ENABLED" : "DISABLED"
            };

            if (isEnabled) activeCount++;

            list.Add(userInfo);
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
