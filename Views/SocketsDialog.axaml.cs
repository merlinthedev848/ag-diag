using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using AgilicoDiagMac.Platform;

namespace AgilicoDiagMac.Views
{
    public partial class SocketsDialog : Window
    {
        private List<SocketInfoItem> _allSockets = new();
        private readonly ObservableCollection<SocketInfoItem> _displayedSockets = new();

        public SocketsDialog()
        {
            InitializeComponent();
            GridSockets.ItemsSource = _displayedSockets;
            _ = LoadSocketsAsync();
        }

        private async Task LoadSocketsAsync()
        {
            TxtStatus.Text = "Querying active network sockets...";
            _displayedSockets.Clear();
            _allSockets = await MacSocketMonitor.GetActiveSocketsAsync();
            ApplyFilter();
            TxtStatus.Text = $"{_displayedSockets.Count} active sockets detected.";
        }

        private void ApplyFilter()
        {
            string filter = TxtSearch.Text?.Trim() ?? "";
            _displayedSockets.Clear();
            var filtered = string.IsNullOrEmpty(filter)
                ? _allSockets
                : _allSockets.Where(s =>
                    s.LocalEndpoint.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                    s.RemoteEndpoint.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                    s.ProcessName.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                    s.Pid.Contains(filter, StringComparison.OrdinalIgnoreCase));

            foreach (var item in filtered)
            {
                _displayedSockets.Add(item);
            }
        }

        private void TxtSearch_TextChanged(object? sender, TextChangedEventArgs e)
        {
            ApplyFilter();
        }

        private async void BtnRefresh_Click(object? sender, RoutedEventArgs e)
        {
            await LoadSocketsAsync();
        }

        private void BtnClose_Click(object? sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
