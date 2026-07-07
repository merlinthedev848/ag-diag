using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;

namespace agilicomsptoolkit
{
    public class UserGroupInfo
    {
        public string Name { get; set; } = string.Empty;
        public string Type { get; set; } = string.Empty;
        public string Sid { get; set; } = string.Empty;
    }

    public class UserPrivilegeInfo
    {
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
    }

    public partial class UserDomainInfoDialog : Window
    {
        public ObservableCollection<UserGroupInfo> UserGroups { get; set; } = new ObservableCollection<UserGroupInfo>();
        public ObservableCollection<UserPrivilegeInfo> UserPrivileges { get; set; } = new ObservableCollection<UserPrivilegeInfo>();

        public UserDomainInfoDialog()
        {
            InitializeComponent();
            DataContext = this;
        }

        private async void Window_Loaded(object sender, RoutedEventArgs e)
        {
            await LoadUserDomainInfoAsync();
        }

        private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
                DragMove();
        }

        private async Task LoadUserDomainInfoAsync()
        {
            try
            {
                // 1. Gather User Name & SID
                string userCsv = await RunCommandAsync("whoami /user /fo csv");
                var userLines = userCsv.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                if (userLines.Length >= 2)
                {
                    var userParts = ParseCsvLine(userLines[1]);
                    if (userParts.Length >= 2)
                    {
                        string fullUser = userParts[0];
                        TxtUserName.Text = fullUser;
                        TxtUserSid.Text = userParts[1];

                        if (fullUser.Contains("\\"))
                        {
                            TxtDomain.Text = fullUser.Split('\\')[0];
                        }
                    }
                }

                // Logon Server info
                string logonServer = Environment.GetEnvironmentVariable("LOGONSERVER") ?? "Local Machine";
                TxtLogonServer.Text = logonServer;

                if (!string.IsNullOrEmpty(logonServer) && logonServer != "Local Machine")
                {
                    try
                    {
                        var cleanServerName = logonServer.TrimStart('\\');
                        var ips = await Dns.GetHostAddressesAsync(cleanServerName);
                        if (ips.Length > 0)
                        {
                            TxtLogonServerIp.Text = ips[0].ToString();
                        }
                    }
                    catch
                    {
                        TxtLogonServerIp.Text = "Could not resolve IP";
                    }
                }
                else
                {
                    TxtLogonServerIp.Text = "N/A";
                }

                // 2. Gather Group Membership
                string groupsCsv = await RunCommandAsync("whoami /groups /fo csv");
                var groupLines = groupsCsv.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                UserGroups.Clear();
                foreach (var line in groupLines.Skip(1)) // Skip header
                {
                    var parts = ParseCsvLine(line);
                    if (parts.Length >= 3)
                    {
                        UserGroups.Add(new UserGroupInfo
                        {
                            Name = parts[0],
                            Type = parts[1],
                            Sid = parts[2]
                        });
                    }
                }
                GridGroups.ItemsSource = UserGroups;

                // 3. Gather Privileges
                string privsCsv = await RunCommandAsync("whoami /priv /fo csv");
                var privLines = privsCsv.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                UserPrivileges.Clear();
                foreach (var line in privLines.Skip(1)) // Skip header
                {
                    var parts = ParseCsvLine(line);
                    if (parts.Length >= 3)
                    {
                        UserPrivileges.Add(new UserPrivilegeInfo
                        {
                            Name = parts[0],
                            Description = parts[1],
                            Status = parts[2]
                        });
                    }
                }
                GridPrivileges.ItemsSource = UserPrivileges;

                TxtStatus.Text = $"Audit complete. {UserGroups.Count} groups & {UserPrivileges.Count} privileges analyzed.";
            }
            catch (Exception ex)
            {
                TxtStatus.Text = "Audit failed.";
                ModernMessageBox.Show($"Failed to audit user and domain information: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async Task<string> RunCommandAsync(string command)
        {
            var parts = command.Split(new[] { ' ' }, 2);
            var exe = parts[0];
            var args = parts.Length > 1 ? parts[1] : "";

            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = exe,
                    Arguments = args,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };

            process.Start();
            string output = await process.StandardOutput.ReadToEndAsync();
            await process.WaitForExitAsync();
            return output;
        }

        private static string[] ParseCsvLine(string line)
        {
            var result = new List<string>();
            bool inQuotes = false;
            var currentToken = new System.Text.StringBuilder();

            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];
                if (c == '"')
                {
                    inQuotes = !inQuotes;
                }
                else if (c == ',' && !inQuotes)
                {
                    result.Add(currentToken.ToString().Trim(' ', '"'));
                    currentToken.Clear();
                }
                else
                {
                    currentToken.Append(c);
                }
            }
            result.Add(currentToken.ToString().Trim(' ', '"'));
            return result.ToArray();
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
