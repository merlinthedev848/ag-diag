using System.Collections.Generic;
using System.Windows;
using System.Windows.Input;

namespace agilicomsptoolkit
{
    public partial class HardwareReportDialog : Window
    {
        public HardwareReportDialog(List<HardwareItem> items)
        {
            InitializeComponent();
            
            if (Application.Current != null && Application.Current.MainWindow != null && Application.Current.MainWindow.IsLoaded && Application.Current.MainWindow != this)
            {
                this.Owner = Application.Current.MainWindow;
            }

            HardwareList.ItemsSource = items;
        }

        private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
                this.DragMove();
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }
    }
}
