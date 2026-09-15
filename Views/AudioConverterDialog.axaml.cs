using System;
using System.IO;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using AgilicoDiagMac.Core;

namespace AgilicoDiagMac.Views
{
    public partial class AudioConverterDialog : Window
    {
        public AudioConverterDialog()
        {
            InitializeComponent();
        }

        private async void BtnBrowse_Click(object? sender, RoutedEventArgs e)
        {
            var topLevel = TopLevel.GetTopLevel(this);
            if (topLevel != null)
            {
                var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
                {
                    Title = "Select Audio File",
                    AllowMultiple = false,
                    FileTypeFilter = new[]
                    {
                        new FilePickerFileType("Audio Files")
                        {
                            Patterns = new[] { "*.mp3", "*.wav", "*.m4a", "*.ogg", "*.aac", "*.flac", "*.wma" }
                        }
                    }
                });

                if (files.Count > 0)
                {
                    TxtInputPath.Text = files[0].Path.LocalPath;
                }
            }
        }

        private async void BtnConvert_Click(object? sender, RoutedEventArgs e)
        {
            string path = TxtInputPath.Text?.Trim() ?? "";
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
            {
                await ModernMessageBox.ShowAsync(this, "Please select a valid existing audio file.", "Notice");
                return;
            }

            BtnConvert.IsEnabled = false;
            BtnBrowse.IsEnabled = false;
            PrgConvert.Value = 0;
            TxtProgress.Text = "Converting audio...";

            var result = await AudioConverter.ConvertAsync(path, p =>
            {
                Dispatcher.UIThread.Post(() =>
                {
                    PrgConvert.Value = p;
                    TxtProgress.Text = $"Converting: {p:F0}%";
                });
            });

            BtnConvert.IsEnabled = true;
            BtnBrowse.IsEnabled = true;

            if (result.success)
            {
                TxtProgress.Text = "Conversion complete!";
                TxtOutputInfo.Text = $"Saved to: {result.outputPath}";
                await ModernMessageBox.ShowAsync(this, $"Converted successfully!\n\nFile saved to:\n{result.outputPath}", "Success");
            }
            else
            {
                TxtProgress.Text = "Conversion failed.";
                await ModernMessageBox.ShowAsync(this, $"Conversion failed:\n{result.message}", "Error");
            }
        }

        private void BtnClose_Click(object? sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
