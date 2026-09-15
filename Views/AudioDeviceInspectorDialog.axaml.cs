using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using AgilicoDiagMac.Platform;

namespace AgilicoDiagMac.Views
{
    public partial class AudioDeviceInspectorDialog : Window
    {
        private readonly ObservableCollection<MacAudioDeviceItem> _audioDevices = new();

        public AudioDeviceInspectorDialog()
        {
            InitializeComponent();
            GridAudio.ItemsSource = _audioDevices;
            _ = LoadDevicesAsync();
        }

        private async Task LoadDevicesAsync()
        {
            _audioDevices.Clear();
            var list = await MacSystemTools.GetAudioDevicesAsync();
            foreach (var item in list)
            {
                _audioDevices.Add(item);
            }
        }

        private async void BtnRefresh_Click(object? sender, RoutedEventArgs e)
        {
            await LoadDevicesAsync();
        }

        private void BtnClose_Click(object? sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
