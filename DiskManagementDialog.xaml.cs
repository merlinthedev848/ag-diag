using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace agilicomsptoolkit
{
    public class LogicalDiskInfo
    {
        public string DeviceID { get; set; } = string.Empty;
        public string VolumeName { get; set; } = string.Empty;
        public string SizeText { get; set; } = string.Empty;
        public string PercentageText { get; set; } = string.Empty;
        public GridLength FillWidthStar { get; set; }
        public GridLength EmptyWidthStar { get; set; }
        public Brush FillBrush { get; set; } = Brushes.Transparent;
        public Brush TextBrush { get; set; } = Brushes.Transparent;
    }

    public partial class DiskManagementDialog : Window
    {
        public ObservableCollection<LogicalDiskInfo> Disks { get; set; } = new ObservableCollection<LogicalDiskInfo>();

        public DiskManagementDialog()
        {
            InitializeComponent();
            DataContext = this;
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            LoadDisks();
        }

        private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
                DragMove();
        }

        private async void LoadDisks()
        {
            Disks.Clear();
            try
            {
                var diskInfos = await Task.Run(() =>
                {
                    var list = new System.Collections.Generic.List<LogicalDiskInfo>();
                    DriveInfo[] drives = DriveInfo.GetDrives();
                    foreach (DriveInfo d in drives)
                    {
                        if (d.IsReady && d.DriveType == DriveType.Fixed)
                        {
                            double totalGb = Math.Round(d.TotalSize / 1024.0 / 1024.0 / 1024.0, 2);
                            double freeGb = Math.Round(d.AvailableFreeSpace / 1024.0 / 1024.0 / 1024.0, 2);
                            double usedGb = totalGb - freeGb;
                            
                            double usedPercent = totalGb > 0 ? (usedGb / totalGb) * 100.0 : 0;
                            double freePercent = 100.0 - usedPercent;

                            Brush fillBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#3b82f6")); // Blue
                            Brush textBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#94a3b8")); // Slate 400

                            if (usedPercent >= 90)
                            {
                                fillBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#ef4444")); // Red
                                textBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#ef4444"));
                            }
                            else if (usedPercent >= 80)
                            {
                                fillBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#f59e0b")); // Orange
                                textBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#f59e0b"));
                            }
                            
                            fillBrush.Freeze();
                            textBrush.Freeze();

                            list.Add(new LogicalDiskInfo
                            {
                                DeviceID = d.Name.TrimEnd('\\'),
                                VolumeName = string.IsNullOrWhiteSpace(d.VolumeLabel) ? "Local Disk" : d.VolumeLabel,
                                SizeText = $"{usedGb:F1} GB used of {totalGb:F1} GB",
                                PercentageText = $"{Math.Round(usedPercent)}% Used ({freeGb:F1} GB Free)",
                                FillWidthStar = new GridLength(usedPercent, GridUnitType.Star),
                                EmptyWidthStar = new GridLength(freePercent, GridUnitType.Star),
                                FillBrush = fillBrush,
                                TextBrush = textBrush
                            });
                        }
                    }
                    return list;
                });
                
                foreach (var di in diskInfos)
                {
                    Disks.Add(di);
                }
                ItemsDisks.ItemsSource = Disks;
            }
            catch (Exception ex)
            {
                ModernMessageBox.Show($"Failed to load disk info: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
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

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
