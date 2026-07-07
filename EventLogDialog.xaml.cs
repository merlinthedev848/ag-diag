using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Linq;

namespace agilicomsptoolkit
{
    public class EventLogInfo
    {
        public string Source { get; set; } = string.Empty;
        public string TimeGenerated { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        
        public string IconGlyph { get; set; } = string.Empty;
        public Brush IconBgBrush { get; set; } = Brushes.Transparent;
        public Brush IconFgBrush { get; set; } = Brushes.Transparent;

        private static readonly Brush ErrorBg = CreateFrozenBrush("#7f1d1d");
        private static readonly Brush ErrorFg = CreateFrozenBrush("#ef4444");
        private static readonly Brush WarnBg = CreateFrozenBrush("#78350f");
        private static readonly Brush WarnFg = CreateFrozenBrush("#f59e0b");
        private static readonly Brush InfoBg = CreateFrozenBrush("#1e3a8a");
        private static readonly Brush InfoFg = CreateFrozenBrush("#3b82f6");

        private static Brush CreateFrozenBrush(string hexColor)
        {
            var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hexColor));
            brush.Freeze();
            return brush;
        }

        public void SetStatusStyle(string entryTypeStr, int entryTypeInt)
        {
            if (entryTypeStr.Contains("Error", StringComparison.OrdinalIgnoreCase) || entryTypeInt == 1)
            {
                IconGlyph = "✖";
                IconBgBrush = ErrorBg;
                IconFgBrush = ErrorFg;
            }
            else if (entryTypeStr.Contains("Warning", StringComparison.OrdinalIgnoreCase) || entryTypeInt == 2)
            {
                IconGlyph = "⚠";
                IconBgBrush = WarnBg;
                IconFgBrush = WarnFg;
            }
            else
            {
                IconGlyph = "ℹ";
                IconBgBrush = InfoBg;
                IconFgBrush = InfoFg;
            }
        }
    }

    public partial class EventLogDialog : Window
    {
        public ObservableCollection<EventLogInfo> Logs { get; set; } = new ObservableCollection<EventLogInfo>();

        public EventLogDialog()
        {
            InitializeComponent();
            DataContext = this;
        }

        private async void Window_Loaded(object sender, RoutedEventArgs e)
        {
            await LoadEventLogsAsync();
        }

        private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
                DragMove();
        }

        private async System.Threading.Tasks.Task LoadEventLogsAsync()
        {
            Logs.Clear();
            int errCount = 0;
            int warnCount = 0;
            int infoCount = 0;

            try
            {
                string jsonCommand = "Get-EventLog -LogName System -Newest 50 | Select-Object TimeGenerated, EntryType, Source, Message | ConvertTo-Json -Compress";
                
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
                    var logList = new System.Collections.Generic.List<EventLogInfo>();

                    if (doc.RootElement.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var element in doc.RootElement.EnumerateArray())
                        {
                            ParseLogElement(element, logList, ref errCount, ref warnCount, ref infoCount);
                        }
                    }
                    else if (doc.RootElement.ValueKind == JsonValueKind.Object)
                    {
                        ParseLogElement(doc.RootElement, logList, ref errCount, ref warnCount, ref infoCount);
                    }

                    foreach (var log in logList)
                    {
                        Logs.Add(log);
                    }

                    TxtErrors.Text = errCount.ToString();
                    TxtWarnings.Text = warnCount.ToString();
                    TxtInfos.Text = infoCount.ToString();
                    ItemsLogs.ItemsSource = Logs;
                }
            }
            catch (Exception ex)
            {
                ModernMessageBox.Show($"Failed to load event logs: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnSearchSolution_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is EventLogInfo log)
            {
                try
                {
                    // Escape query string - get first line of message for better search
                    string cleanMessage = log.Message.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "";
                    string query = Uri.EscapeDataString($"Windows Event Log {log.Source} {cleanMessage}");
                    string url = $"https://www.google.com/search?q={query}";
                    
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = url,
                        UseShellExecute = true
                    });
                }
                catch (Exception ex)
                {
                    ModernMessageBox.Show($"Failed to open search: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void ParseLogElement(JsonElement element, System.Collections.Generic.List<EventLogInfo> list, ref int errCount, ref int warnCount, ref int infoCount)
        {
            string source = element.GetProperty("Source").GetString() ?? "Unknown";
            string msg = element.GetProperty("Message").GetString() ?? "";
            
            string timeStr = "";
            if (element.TryGetProperty("TimeGenerated", out var timeProp))
            {
                if (timeProp.ValueKind == JsonValueKind.String)
                {
                    if (DateTime.TryParse(timeProp.GetString(), out DateTime dt))
                        timeStr = dt.ToString("g");
                    else
                        timeStr = timeProp.GetString() ?? "";
                }
                else if (timeProp.ValueKind == JsonValueKind.Object && timeProp.TryGetProperty("DateTime", out var dtObj))
                {
                    timeStr = dtObj.GetString() ?? "";
                }
            }

            int entryTypeInt = 4;
            string entryTypeStr = "Information";
            
            if (element.TryGetProperty("EntryType", out var entryTypeProp))
            {
                if (entryTypeProp.ValueKind == JsonValueKind.Number)
                {
                    entryTypeInt = entryTypeProp.GetInt32();
                    if (entryTypeInt == 1) entryTypeStr = "Error";
                    else if (entryTypeInt == 2) entryTypeStr = "Warning";
                }
                else if (entryTypeProp.ValueKind == JsonValueKind.String)
                {
                    entryTypeStr = entryTypeProp.GetString() ?? "Information";
                }
            }

            var logInfo = new EventLogInfo
            {
                Source = source,
                TimeGenerated = timeStr,
                Message = msg
            };

            logInfo.SetStatusStyle(entryTypeStr, entryTypeInt);

            if (entryTypeStr.Contains("Error", StringComparison.OrdinalIgnoreCase) || entryTypeInt == 1) errCount++;
            else if (entryTypeStr.Contains("Warning", StringComparison.OrdinalIgnoreCase) || entryTypeInt == 2) warnCount++;
            else infoCount++;

            list.Add(logInfo);
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
