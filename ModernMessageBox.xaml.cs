using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Input;

namespace agilicomsptoolkit
{
    public partial class ModernMessageBox : Window
    {
        public MessageBoxResult Result { get; private set; } = MessageBoxResult.Cancel;

        public ModernMessageBox(string messageBoxText, string caption, MessageBoxButton button, MessageBoxImage icon)
        {
            InitializeComponent();
            
            Title = caption;
            TxtTitle.Text = caption;
            TxtMessage.Text = messageBoxText;

            ConfigureButtons(button);
            ConfigureIcon(icon);
            
            if (Application.Current != null && Application.Current.MainWindow != null && Application.Current.MainWindow.IsLoaded && Application.Current.MainWindow != this)
            {
                this.Owner = Application.Current.MainWindow;
                this.WindowStartupLocation = WindowStartupLocation.CenterOwner;
            }
            else
            {
                this.WindowStartupLocation = WindowStartupLocation.CenterScreen;
            }
        }

        private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
            {
                this.DragMove();
            }
        }

        private void ConfigureButtons(MessageBoxButton button)
        {
            ButtonPanel.Children.Clear();
            
            switch (button)
            {
                case MessageBoxButton.OK:
                    AddButton("OK", MessageBoxResult.OK, true, false, true);
                    break;
                case MessageBoxButton.OKCancel:
                    AddButton("OK", MessageBoxResult.OK, true, false, true);
                    AddButton("Cancel", MessageBoxResult.Cancel, false, true, false);
                    break;
                case MessageBoxButton.YesNo:
                    AddButton("Yes", MessageBoxResult.Yes, true, false, true);
                    AddButton("No", MessageBoxResult.No, false, true, false);
                    break;
                case MessageBoxButton.YesNoCancel:
                    AddButton("Yes", MessageBoxResult.Yes, true, false, true);
                    AddButton("No", MessageBoxResult.No, false, false, false);
                    AddButton("Cancel", MessageBoxResult.Cancel, false, true, false);
                    break;
            }
        }

        private void AddButton(string text, MessageBoxResult result, bool isDefault, bool isCancel, bool isPrimary)
        {
            var btn = new Button
            {
                Content = text,
                Width = 80,
                Height = 32,
                Margin = new Thickness(10, 0, 0, 0),
                Cursor = System.Windows.Input.Cursors.Hand,
                IsDefault = isDefault,
                IsCancel = isCancel
            };

            if (isPrimary)
            {
                btn.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#2563EB")); // Accent Blue
                btn.Foreground = Brushes.White;
                btn.BorderThickness = new Thickness(0);
            }
            else
            {
                btn.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F3F4F6"));
                btn.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#374151"));
                btn.BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#D1D5DB"));
                btn.BorderThickness = new Thickness(1);
            }

            btn.Click += (s, e) =>
            {
                Result = result;
                this.DialogResult = result == MessageBoxResult.OK || result == MessageBoxResult.Yes;
                this.Close();
            };

            ButtonPanel.Children.Add(btn);
        }

        private void ConfigureIcon(MessageBoxImage icon)
        {
            string pathData = "";
            Brush fillBrush = Brushes.Black;

            switch (icon)
            {
                case MessageBoxImage.Information: // same as Asterisk
                    pathData = "M12,2A10,10 0 0,1 22,12A10,10 0 0,1 12,22A10,10 0 0,1 2,12A10,10 0 0,1 12,2M11,16H13V10H11V16M12,6A1.5,1.5 0 0,0 10.5,7.5A1.5,1.5 0 0,0 12,9A1.5,1.5 0 0,0 13.5,7.5A1.5,1.5 0 0,0 12,6Z";
                    fillBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#2563EB")); // Blue
                    break;
                case MessageBoxImage.Warning: // same as Exclamation
                    pathData = "M12,2L1,21H23M12,6L19.53,19H4.47M11,10V14H13V10M11,16V18H13V16";
                    fillBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F59E0B")); // Amber
                    break;
                case MessageBoxImage.Error: // same as Hand, Stop
                    pathData = "M12,2C17.53,2 22,6.47 22,12C22,17.53 17.53,22 12,22C6.47,22 2,17.53 2,12C2,6.47 6.47,2 12,2M15.59,7L12,10.59L8.41,7L7,8.41L10.59,12L7,15.59L8.41,17L12,13.41L15.59,17L17,15.59L13.41,12L17,8.41L15.59,7Z";
                    fillBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#EF4444")); // Red
                    break;
                case MessageBoxImage.Question:
                    pathData = "M12,2A10,10 0 0,1 22,12A10,10 0 0,1 12,22A10,10 0 0,1 2,12A10,10 0 0,1 12,2M12,4A8,8 0 0,0 4,12A8,8 0 0,0 12,20A8,8 0 0,0 20,12A8,8 0 0,0 12,4M12,16A1.5,1.5 0 0,1 13.5,17.5A1.5,1.5 0 0,1 12,19A1.5,1.5 0 0,1 10.5,17.5A1.5,1.5 0 0,1 12,16M12,6C14.21,6 16,7.79 16,10C16,11.5 15.2,12.7 14,13.4V14H10V12C10,11.23 10.42,10.57 11.08,10.22C11.66,9.9 12,9.45 12,9C12,8.45 11.55,8 11,8C10.45,8 10,8.45 10,9H8C8,6.79 9.79,6 12,6Z";
                    fillBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#10B981")); // Green/Teal
                    break;
                case MessageBoxImage.None:
                default:
                    IconPath.Visibility = Visibility.Collapsed;
                    return;
            }

            IconPath.Data = Geometry.Parse(pathData);
            IconPath.Fill = fillBrush;
            IconPath.Visibility = Visibility.Visible;
        }

        public static MessageBoxResult Show(string messageBoxText, string caption = "", MessageBoxButton button = MessageBoxButton.OK, MessageBoxImage icon = MessageBoxImage.None)
        {
            var msgBox = new ModernMessageBox(messageBoxText, caption, button, icon);
            msgBox.ShowDialog();
            return msgBox.Result;
        }
        
        public static MessageBoxResult Show(string messageBoxText)
        {
            return Show(messageBoxText, "", MessageBoxButton.OK, MessageBoxImage.None);
        }
    }
}
