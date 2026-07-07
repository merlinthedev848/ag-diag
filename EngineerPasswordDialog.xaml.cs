using System.Windows;
using System.Windows.Input;

namespace agilicomsptoolkit
{
    public partial class EngineerPasswordDialog : Window
    {
        public string Password => TxtPassword.Password;

        public EngineerPasswordDialog()
        {
            InitializeComponent();
            TxtPassword.Focus();
        }

        private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
                DragMove();
        }

        private void BtnOk_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
            Close();
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
