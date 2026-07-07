using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.ServiceProcess;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace agilicomsptoolkit
{
    public class ServiceItem : System.ComponentModel.INotifyPropertyChanged
    {
        public ServiceController Controller { get; set; } = null!;
        public string DisplayName => Controller.DisplayName;
        public string ServiceName => Controller.ServiceName;
        public string StartType => Controller.StartType.ToString();
        
        public string Status => Controller.Status.ToString();
        
        private static readonly Brush RunningStatusBrush = CreateFrozenBrush("#10b981");
        private static readonly Brush StoppedStatusBrush = CreateFrozenBrush("#94a3b8");
        private static readonly Brush StopActionBrush = CreateFrozenBrush("#ef4444");
        private static readonly Brush StartActionBrush = CreateFrozenBrush("#3b82f6");

        private static Brush CreateFrozenBrush(string hexColor)
        {
            var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hexColor));
            brush.Freeze();
            return brush;
        }

        public Brush StatusColorBrush => Status == "Running" ? RunningStatusBrush : StoppedStatusBrush;
            
        public Color StatusGlowColor => Status == "Running" 
            ? (Color)ColorConverter.ConvertFromString("#10b981") 
            : Colors.Transparent;

        public string ActionText => Status == "Running" ? "Stop" : "Start";
        
        public Brush ActionBgBrush => Status == "Running" ? StopActionBrush : StartActionBrush;

        public void RefreshProperties()
        {
            OnPropertyChanged(nameof(Status));
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

    public partial class ServiceManagerDialog : Window
    {
        public ObservableCollection<ServiceItem> Services { get; set; } = new ObservableCollection<ServiceItem>();

        public ServiceManagerDialog()
        {
            InitializeComponent();
            DataContext = this;
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            LoadServices();
        }

        private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
                DragMove();
        }

        private void LoadServices()
        {
            try
            {
                var systemServices = ServiceController.GetServices().OrderBy(s => s.DisplayName).ToList();
                Services.Clear();
                foreach (var s in systemServices)
                {
                    Services.Add(new ServiceItem { Controller = s });
                }
                GridServices.ItemsSource = Services;
                TxtStatus.Text = $"{Services.Count} services loaded.";
            }
            catch (Exception ex)
            {
                ModernMessageBox.Show($"Failed to load services: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnRefresh_Click(object sender, RoutedEventArgs e)
        {
            LoadServices();
        }

        private void BtnAction_Click(object sender, RoutedEventArgs e)
        {
            if (sender is System.Windows.Controls.Button btn && btn.Tag is ServiceItem item)
            {
                if (item.Status == "Running")
                {
                    // STOP SERVICE
                    var result = ModernMessageBox.Show($"Are you sure you want to stop '{item.DisplayName}'?\n\nStopping critical services may cause system instability.", "Confirm Stop", MessageBoxButton.YesNo, MessageBoxImage.Warning);
                    if (result != MessageBoxResult.Yes) return;

                    try
                    {
                        if (!item.Controller.CanStop)
                        {
                            ModernMessageBox.Show("This service cannot be stopped.", "Not Supported", MessageBoxButton.OK, MessageBoxImage.Warning);
                            return;
                        }

                        item.Controller.Stop();
                        item.Controller.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(10));
                        item.Controller.Refresh();
                        item.RefreshProperties();
                        
                        if (Owner is MainWindow mainWindow) mainWindow.LogAuditAction($"Stopped Windows Service: {item.DisplayName}");
                    }
                    catch (Exception ex)
                    {
                        ModernMessageBox.Show($"Failed to stop service. Ensure you are running as Administrator.\n\nDetails: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }
                else
                {
                    // START SERVICE
                    var result = ModernMessageBox.Show($"Are you sure you want to start '{item.DisplayName}'?", "Confirm Start", MessageBoxButton.YesNo, MessageBoxImage.Question);
                    if (result != MessageBoxResult.Yes) return;

                    try
                    {
                        item.Controller.Start();
                        item.Controller.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(10));
                        item.Controller.Refresh();
                        item.RefreshProperties();
                        
                        if (Owner is MainWindow mainWindow) mainWindow.LogAuditAction($"Started Windows Service: {item.DisplayName}");
                    }
                    catch (Exception ex)
                    {
                        ModernMessageBox.Show($"Failed to start service. Ensure you are running as Administrator.\n\nDetails: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }
            }
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
