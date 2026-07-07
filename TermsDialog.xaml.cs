using System.Windows;

namespace agilicomsptoolkit;

public partial class TermsDialog : Window
{
    public TermsDialog()
    {
        InitializeComponent();
    }

    private void BtnAgree_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
    }

    private void BtnCancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}
