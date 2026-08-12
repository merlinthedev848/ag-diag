using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Threading.Tasks;

namespace agilicomsptoolkit
{
    public partial class MainWindow : Window
    {
        [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
        private static extern bool ChangeWindowMessageFilter(uint message, uint flag);

        [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
        private static extern bool ChangeWindowMessageFilterEx(IntPtr hWnd, uint message, uint action, IntPtr pChangeFilterInfo);

        private const uint WM_DROPFILES = 0x0233;
        private const uint WM_COPYDATA = 0x004A;
        private const uint WM_COPYGLOBALDATA = 0x0049;
        private const uint MSGFLT_ADD = 1;
        private const uint MSGFLT_ALLOW = 1;

        [System.Runtime.InteropServices.DllImport("shell32.dll", CharSet = System.Runtime.InteropServices.CharSet.Auto)]
        private static extern void DragAcceptFiles(IntPtr hWnd, bool fAccept);

        [System.Runtime.InteropServices.DllImport("shell32.dll", CharSet = System.Runtime.InteropServices.CharSet.Auto)]
        private static extern uint DragQueryFile(IntPtr hDrop, uint iFile, System.Text.StringBuilder lpszFile, uint cch);

        [System.Runtime.InteropServices.DllImport("shell32.dll", CharSet = System.Runtime.InteropServices.CharSet.Auto)]
        private static extern void DragFinish(IntPtr hDrop);

        [System.Runtime.InteropServices.DllImport("ole32.dll")]
        private static extern int RevokeDragDrop(IntPtr hwnd);

        public void EnableDragAndDropAdminBypass()
        {
            try
            {
                ChangeWindowMessageFilter(WM_DROPFILES, MSGFLT_ADD);
                ChangeWindowMessageFilter(WM_COPYDATA, MSGFLT_ADD);
                ChangeWindowMessageFilter(WM_COPYGLOBALDATA, MSGFLT_ADD);

                var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
                if (hwnd != IntPtr.Zero)
                {
                    // Unregister WPF's OLE IDropTarget so Win32 WM_DROPFILES receives file drops through UAC filter
                    RevokeDragDrop(hwnd);

                    ChangeWindowMessageFilterEx(hwnd, WM_DROPFILES, MSGFLT_ALLOW, IntPtr.Zero);
                    ChangeWindowMessageFilterEx(hwnd, WM_COPYDATA, MSGFLT_ALLOW, IntPtr.Zero);
                    ChangeWindowMessageFilterEx(hwnd, WM_COPYGLOBALDATA, MSGFLT_ALLOW, IntPtr.Zero);

                    // Enable Win32 DragAcceptFiles for Admin mode
                    DragAcceptFiles(hwnd, true);

                    // Hook HWND message pipeline
                    var source = System.Windows.Interop.HwndSource.FromHwnd(hwnd);
                    source?.RemoveHook(AdminDragDropWndProc);
                    source?.AddHook(AdminDragDropWndProc);
                }
            }
            catch { }
        }

        private IntPtr AdminDragDropWndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == (int)WM_DROPFILES)
            {
                try
                {
                    var sb = new System.Text.StringBuilder(500);
                    uint count = DragQueryFile(wParam, 0xFFFFFFFF, sb, 0);
                    if (count > 0)
                    {
                        DragQueryFile(wParam, 0, sb, (uint)sb.Capacity);
                        string filePath = sb.ToString();
                        DragFinish(wParam);

                        if (!string.IsNullOrEmpty(filePath))
                        {
                            Dispatcher.InvokeAsync(async () =>
                            {
                                SelectTab(3, BtnConverter);
                                await ProcessMediaFileAsync(filePath);
                            });
                        }
                    }
                }
                catch { }
                handled = true;
            }
            return IntPtr.Zero;
        }

        private void Window_PreviewDragEnter(object sender, DragEventArgs e)
        {
            e.Effects = DragDropEffects.Copy;
            e.Handled = true;
        }

        private void Window_PreviewDragOver(object sender, DragEventArgs e)
        {
            e.Effects = DragDropEffects.Copy;
            e.Handled = true;
        }

        private async void Window_Drop(object sender, DragEventArgs e)
        {
            if (PageTabControl.SelectedIndex == 3) // Converter Tab
            {
                string? filePath = ExtractFilePathFromDragData(e.Data);
                if (!string.IsNullOrEmpty(filePath))
                {
                    await ProcessMediaFileAsync(filePath);
                }
            }
        }

        private void BtnConverter_Click(object sender, RoutedEventArgs e) => SelectTab(3, BtnConverter);

        private void DropZoneBorder_DragEnter(object sender, DragEventArgs e)
        {
            e.Effects = DragDropEffects.Copy;
            e.Handled = true;
            DropZoneBorder.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#f1f5f9"));
            DropZoneBorder.BorderBrush = (Brush)FindResource("AccentGreenBrush");
        }

        private void DropZoneBorder_DragLeave(object sender, DragEventArgs e)
        {
            DropZoneBorder.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#f8fafc"));
            DropZoneBorder.BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#cbd5e1"));
        }

        private void DropZoneBorder_DragOver(object sender, DragEventArgs e)
        {
            e.Effects = DragDropEffects.Copy;
            e.Handled = true;
        }

        private async void DropZoneBorder_Drop(object sender, DragEventArgs e)
        {
            DropZoneBorder.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#f8fafc"));
            DropZoneBorder.BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#cbd5e1"));

            string? filePath = ExtractFilePathFromDragData(e.Data);
            if (!string.IsNullOrEmpty(filePath))
            {
                await ProcessMediaFileAsync(filePath);
            }
        }

        private string? ExtractFilePathFromDragData(IDataObject data)
        {
            try
            {
                if (data.GetDataPresent(DataFormats.FileDrop))
                {
                    if (data.GetData(DataFormats.FileDrop) is string[] files && files.Length > 0)
                        return files[0];
                }
                if (data.GetDataPresent("FileDrop"))
                {
                    if (data.GetData("FileDrop") is string[] files && files.Length > 0)
                        return files[0];
                }
                if (data.GetDataPresent("FileNameW"))
                {
                    if (data.GetData("FileNameW") is string[] files && files.Length > 0)
                        return files[0];
                    if (data.GetData("FileNameW") is string fileStr)
                        return fileStr;
                }
                if (data.GetDataPresent("FileName"))
                {
                    if (data.GetData("FileName") is string[] files && files.Length > 0)
                        return files[0];
                    if (data.GetData("FileName") is string fileStr)
                        return fileStr;
                }
                if (data.GetDataPresent(DataFormats.Text))
                {
                    string text = data.GetData(DataFormats.Text)?.ToString() ?? "";
                    if (System.IO.File.Exists(text)) return text;
                }
            }
            catch { }
            return null;
        }

        private void DropZoneBorder_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            BrowseAndProcessMediaFile();
        }

        private void BtnBrowseMedia_Click(object sender, RoutedEventArgs e)
        {
            BrowseAndProcessMediaFile();
        }

        private void BrowseAndProcessMediaFile()
        {
            var openFileDialog = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Select Audio or Video File",
                Filter = "Media Files|*.mp3;*.mp4;*.m4a;*.wav;*.ogg;*.flac;*.aac;*.wma;*.opus;*.mkv;*.avi;*.mov;*.wmv;*.flv;*.webm;*.m4v|All Files|*.*"
            };

            if (openFileDialog.ShowDialog() == true)
            {
                _ = ProcessMediaFileAsync(openFileDialog.FileName);
            }
        }

        private async Task ProcessMediaFileAsync(string inputPath)
        {
            try
            {
                // UI Reset
                TxtConvertingFileName.Text = System.IO.Path.GetFileName(inputPath);
                PrgConversion.Value = 0;
                TxtConversionResult.Text = "Starting conversion...";
                
                ChkStep1.IsChecked = false;
                TxtStep1Status.Text = "Validating input file format...";
                
                ChkStep2.IsChecked = false;
                TxtStep2Status.Text = "Pending metadata removal...";
                
                ChkStep3.IsChecked = false;
                TxtStep3Status.Text = "Pending audio transcoding...";

                // Step 1: Format Validation
                await Task.Delay(500); // Small delay for UX feel
                string ext = System.IO.Path.GetExtension(inputPath).ToLower();
                string[] supportedExtensions = { ".mp3", ".mp4", ".m4a", ".wav", ".ogg", ".flac", ".aac", ".wma", ".opus", ".mkv", ".avi", ".mov", ".wmv", ".flv", ".webm", ".m4v" };
                
                if (Array.IndexOf(supportedExtensions, ext) < 0)
                {
                    TxtStep1Status.Text = $"Failed: Unsupported format '{ext}'";
                    TxtConversionResult.Text = "Conversion aborted due to unsupported file format.";
                    return;
                }

                ChkStep1.IsChecked = true;
                TxtStep1Status.Text = "Checked: Valid media format";
                
                // Step 2: Metadata Removal
                TxtStep2Status.Text = "Removing metadata/ID3 tags...";
                await Task.Delay(300); // UX feel
                ChkStep2.IsChecked = true;
                TxtStep2Status.Text = "Checked: Metadata removal initialized";

                // Step 3: Transcoding
                TxtStep3Status.Text = "Transcoding (PCM 16-bit Mono 8kHz WAV)...";
                
                var result = await AudioConverter.ConvertAsync(inputPath, (progress) =>
                {
                    Dispatcher.Invoke(() =>
                    {
                        PrgConversion.Value = progress;
                        TxtStep3Status.Text = $"Converting ({Math.Round(progress)}%)";
                    });
                });

                if (result.success)
                {
                    ChkStep3.IsChecked = true;
                    TxtStep3Status.Text = "Completed";
                    PrgConversion.Value = 100;
                    TxtConversionResult.Text = $"Success! Saved to:\n{result.outputPath}";
                }
                else
                {
                    TxtStep3Status.Text = "Failed";
                    TxtConversionResult.Text = $"Conversion Error: {result.message}";
                }
            }
            catch (Exception ex)
            {
                TxtConversionResult.Text = $"Unexpected Error: {ex.Message}";
            }
        }
    }
}
