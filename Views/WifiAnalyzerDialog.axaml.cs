using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using AgilicoDiagMac.Platform;

namespace AgilicoDiagMac.Views
{
    public partial class WifiAnalyzerDialog : Window
    {
        public WifiAnalyzerDialog()
        {
            InitializeComponent();
            _ = LoadWifiMetricsAsync();
        }

        private async Task LoadWifiMetricsAsync()
        {
            var info = await MacSystemTools.GetWifiMetricsAsync();
            TxtSsid.Text = info.Ssid;
            TxtRssiNoise.Text = $"{info.Rssi} (Noise: {info.Noise})";
            TxtQuality.Text = info.SignalQuality;
            TxtChannelTx.Text = $"Channel {info.Channel} | Tx Rate: {info.TxRate}";
            TxtRouterSecurity.Text = $"{info.RouterIp} | {info.Security}";
        }

        private async void BtnRefresh_Click(object? sender, RoutedEventArgs e)
        {
            await LoadWifiMetricsAsync();
        }

        private void BtnClose_Click(object? sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
