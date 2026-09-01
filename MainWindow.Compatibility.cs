using System.Windows;
using System.Windows.Controls;

namespace agilicomsptoolkit
{
    public partial class MainWindow
    {
        // These controls were removed from MainWindow.xaml when startup speed testing
        // became part of the diagnostic flow. The old handler remains in the large
        // code-behind for compatibility with older layouts; keep it a harmless no-op.
        private Button? BtnRecheckSpeed => null;
        private UIElement? SpeedTestProgress => null;
    }
}
