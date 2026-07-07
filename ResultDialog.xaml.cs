using System.Windows;
using System.Windows.Input;

namespace agilicomsptoolkit
{
    public partial class ResultDialog : Window
    {
        public ResultDialog(string title, string output)
        {
            InitializeComponent();
            TxtTitle.Text = title;
            TxtOutput.Text = output;
        }

        private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
                DragMove();
        }

        private void BtnCopy_Click(object sender, RoutedEventArgs e)
        {
            Clipboard.SetText(TxtOutput.Text);
            ModernMessageBox.Show("Results copied to clipboard.", "Copied", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
