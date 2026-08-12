using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Input;

namespace agilicomsptoolkit
{
    public partial class ResourceMonitorDialog : Window
    {
        public class ProcessInfo
        {
            public string Name { get; set; } = "";
            public int Id { get; set; }
            public double MemoryMB { get; set; }
            public string Title { get; set; } = "";
        }

        private ObservableCollection<ProcessInfo> _processes = new();

        public ResourceMonitorDialog()
        {
            InitializeComponent();
            GridProcesses.ItemsSource = _processes;
            LoadProcesses();
        }

        private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
                DragMove();
        }

        private async void LoadProcesses()
        {
            try
            {
                var procs = await Task.Run(() =>
                {
                    return Process.GetProcesses()
                        .Select(p => {
                            double mem = 0;
                            try { mem = Math.Round(p.WorkingSet64 / 1024.0 / 1024.0, 1); } catch { }
                            string title = "";
                            try { title = p.MainWindowTitle; } catch { }
                            return new ProcessInfo
                            {
                                Name = p.ProcessName,
                                Id = p.Id,
                                MemoryMB = mem,
                                Title = title
                            };
                        })
                        .Where(p => p.MemoryMB > 10) // Filter out tiny background tasks for cleaner view
                        .OrderByDescending(p => p.MemoryMB)
                        .Take(50) // Show top 50
                        .ToList();
                });

                _processes.Clear();
                foreach (var p in procs)
                {
                    _processes.Add(p);
                }
            }
            catch (Exception ex)
            {
                ModernMessageBox.Show($"Failed to load processes: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnRefresh_Click(object sender, RoutedEventArgs e)
        {
            Logger.Log("Action: Resource Monitor - refreshed process list.");
            LoadProcesses();
        }

        private void BtnKill_Click(object sender, RoutedEventArgs e)
        {
            if (GridProcesses.SelectedItem is ProcessInfo selected)
            {
                var result = ModernMessageBox.Show($"Are you sure you want to forcibly terminate {selected.Name} (PID {selected.Id})?\n\nUnsaved data will be lost.", "Confirm End Task", MessageBoxButton.YesNo, MessageBoxImage.Warning);
                if (result == MessageBoxResult.Yes)
                {
                    try
                    {
                        var p = Process.GetProcessById(selected.Id);
                        p.Kill();
                        Logger.Log($"Action: Resource Monitor - terminated process '{selected.Name}' (PID {selected.Id}).");
                        ModernMessageBox.Show($"Successfully terminated {selected.Name}.", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
                        LoadProcesses();
                    }
                    catch (Exception ex)
                    {
                        Logger.Log($"Error: Resource Monitor - failed to terminate '{selected.Name}' - {ex.Message}");
                        ModernMessageBox.Show($"Could not terminate process: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }
            }
            else
            {
                ModernMessageBox.Show("Please select a process from the list first.", "No Selection", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            Logger.Log("Dialog: Resource Monitor closed.");
            Close();
        }
    }
}
