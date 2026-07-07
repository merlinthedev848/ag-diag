using System.Windows;
using System.Windows.Input;

namespace agilicomsptoolkit
{
    public partial class InputDialog : Window
    {
        public string InputText => TxtInput.Text;

        public InputDialog(string title, string instruction, string defaultValue = "")
        {
            InitializeComponent();
            TxtTitle.Text = title;
            TxtInstruction.Text = instruction;
            TxtInput.Text = defaultValue;
            
            Loaded += (s, e) => 
            {
                TxtInput.Focus();
                TxtInput.SelectAll();
            };
        }

        private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            DragMove();
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void BtnOk_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
            Close();
        }
    }
}
