using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace AgilicoDiagMac.Views
{
    public partial class EngineerPasswordDialog : Window
    {
        public string Password => TxtPassword.Text ?? string.Empty;

        public EngineerPasswordDialog()
        {
            InitializeComponent();
            TxtPassword.Focus();
        }

        private void BtnOk_Click(object? sender, RoutedEventArgs e)
        {
            Close(true);
        }

        private void BtnCancel_Click(object? sender, RoutedEventArgs e)
        {
            Close(false);
        }
    }
}
