using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace AgilicoDiagMac.Views
{
    public partial class ModernMessageBox : Window
    {
        public bool Result { get; private set; }

        public ModernMessageBox()
        {
            InitializeComponent();
        }

        public static async Task<bool> ShowAsync(Window owner, string message, string title = "Information", bool showCancel = false)
        {
            var box = new ModernMessageBox();
            box.TxtTitle.Text = title;
            box.TxtMessage.Text = message;
            box.BtnCancel.IsVisible = showCancel;
            if (showCancel)
            {
                box.BtnOk.Content = "Confirm";
            }
            await box.ShowDialog(owner);
            return box.Result;
        }

        private void BtnOk_Click(object? sender, RoutedEventArgs e)
        {
            Result = true;
            Close();
        }

        private void BtnCancel_Click(object? sender, RoutedEventArgs e)
        {
            Result = false;
            Close();
        }
    }
}
