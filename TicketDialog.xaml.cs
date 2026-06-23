using System;
using System.Collections.Generic;
using System.IO;
using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;

namespace AgilicoConnectChecker
{
    public partial class TicketDialog : Window
    {
        private readonly string _logContent;
        private readonly byte[]? _pcapBytes;
        private readonly string _pingTrackerTarget;
        private readonly Action<string>? _pingLogExporter;
        private readonly List<LanDevice>? _lanDevices;

        public TicketDialog(string defaultSubject, string defaultBody, string logContent, byte[]? pcapBytes, string pingTrackerTarget, Action<string>? pingLogExporter, List<LanDevice>? lanDevices)
        {
            InitializeComponent();
            
            _logContent = logContent;
            _pcapBytes = pcapBytes;
            _pingTrackerTarget = pingTrackerTarget;
            _pingLogExporter = pingLogExporter;
            _lanDevices = lanDevices;

            // Pre-fill fields
            TxtSubject.Text = defaultSubject;
            TxtDescription.Text = defaultBody;
            TxtEmail.Text = ""; // User fills their email

            // Update attachments label based on what is available
            var attachmentsText = "Attaching: diagnostic_report.txt";
            if (_pcapBytes != null && _pcapBytes.Length > 0)
            {
                attachmentsText += ", network_capture.pcap";
            }
            if (!string.IsNullOrEmpty(_pingTrackerTarget))
            {
                attachmentsText += ", ping_history.csv";
            }
            if (_lanDevices != null && _lanDevices.Count > 0)
            {
                attachmentsText += ", network_scan_report.csv";
            }
            TxtAttachmentsInfo.Text = attachmentsText;
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        [System.Runtime.Versioning.SupportedOSPlatform("windows")]
        private void BtnCreateDraft_Click(object sender, RoutedEventArgs e)
        {
            var email = TxtEmail.Text.Trim();
            var subject = TxtSubject.Text.Trim();
            var description = TxtDescription.Text.Trim();

            if (string.IsNullOrEmpty(email) || !Regex.IsMatch(email, @"^[^@\s]+@[^@\s]+\.[^@\s]+$"))
            {
                MessageBox.Show("Please enter a valid email address.", "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (string.IsNullOrEmpty(subject))
            {
                MessageBox.Show("Please enter a subject.", "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // Create temp directory and files for attachments
            string tempDir = Path.Combine(Path.GetTempPath(), "Agilico_Diagnostics_Logs");
            try
            {
                if (Directory.Exists(tempDir))
                {
                    Directory.Delete(tempDir, true);
                }
                Directory.CreateDirectory(tempDir);
            }
            catch { /* Ignore clear errors, write to new unique folder if needed */ }

            string logPath = Path.Combine(tempDir, "diagnostic_report.txt");
            string pcapPath = "";
            string pingLogPath = "";
            string lanReportPath = "";

            try
            {
                File.WriteAllText(logPath, _logContent);
                
                if (_pcapBytes != null && _pcapBytes.Length > 0)
                {
                    pcapPath = Path.Combine(tempDir, "network_capture.pcap");
                    File.WriteAllBytes(pcapPath, _pcapBytes);
                }

                if (!string.IsNullOrEmpty(_pingTrackerTarget) && _pingLogExporter != null)
                {
                    pingLogPath = Path.Combine(tempDir, "ping_history.csv");
                    _pingLogExporter(pingLogPath);
                }

                if (_lanDevices != null && _lanDevices.Count > 0)
                {
                    lanReportPath = Path.Combine(tempDir, "network_scan_report.csv");
                    var sb = new StringBuilder();
                    sb.AppendLine("IP Address,MAC Address,Hostname,Manufacturer,Status");
                    foreach (var d in _lanDevices)
                    {
                        sb.AppendLine($"{CsvEscape(d.IpAddress)},{CsvEscape(d.MacAddress)},{CsvEscape(d.Hostname)},{CsvEscape(d.Manufacturer)},{CsvEscape(d.Status)}");
                    }
                    File.WriteAllText(lanReportPath, sb.ToString());
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to prepare attachment files: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            // Construct email body combining user description and basic context
            string emailBody = $"From: {email}\r\n\r\n" +
                               $"User Description:\r\n{description}\r\n\r\n" +
                               $"--------------------------------------------------\r\n" +
                               $"Diagnostic Summary and details are attached to this email.\r\n" +
                               $"Hostname: {Environment.MachineName}\r\n" +
                               $"OS Version: {Environment.OSVersion}\r\n" +
                               $"Local Time: {DateTime.Now:yyyy-MM-dd HH:mm:ss}\r\n";

            // Try to use Outlook COM Interop
            Type? outlookType = Type.GetTypeFromProgID("Outlook.Application");
            if (outlookType != null)
            {
                try
                {
                    object? outlookApp = Activator.CreateInstance(outlookType);
                    if (outlookApp != null)
                    {
                        dynamic app = outlookApp;
                        dynamic mailItem = app.CreateItem(0); // 0 = olMailItem
                        if (mailItem != null)
                        {
                            mailItem.To = "support@tech.agilico.co.uk";
                            mailItem.Subject = subject;
                            mailItem.Body = emailBody;

                            // Add attachments
                            if (File.Exists(logPath)) mailItem.Attachments.Add(logPath);
                            if (!string.IsNullOrEmpty(pcapPath) && File.Exists(pcapPath)) mailItem.Attachments.Add(pcapPath);
                            if (!string.IsNullOrEmpty(pingLogPath) && File.Exists(pingLogPath)) mailItem.Attachments.Add(pingLogPath);
                            if (!string.IsNullOrEmpty(lanReportPath) && File.Exists(lanReportPath)) mailItem.Attachments.Add(lanReportPath);

                            mailItem.Display(false); // Display non-modal
                            DialogResult = true;
                            Close();
                            return;
                        }
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Failed to connect to Outlook client: {ex.Message}. Falling back to default mail link.", "Outlook Connector Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }

            // Fallback to mailto link
            try
            {
                string mailtoUrl = $"mailto:support@tech.agilico.co.uk?subject={Uri.EscapeDataString(subject)}&body={Uri.EscapeDataString(emailBody)}";
                
                MessageBox.Show($"Outlook client was not detected or failed to start. We will open your default email client.\r\n\r\nIMPORTANT: Please manually attach the diagnostic logs from:\r\n{tempDir}", "Outlook Not Detected", MessageBoxButton.OK, MessageBoxImage.Information);

                Process.Start(new ProcessStartInfo(mailtoUrl) { UseShellExecute = true });
                
                // Open the temp folder in File Explorer to make it easy for the user to attach the files
                Process.Start(new ProcessStartInfo(tempDir) { UseShellExecute = true });
                
                DialogResult = true;
                Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to open email client: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private static string CsvEscape(string field)
        {
            if (string.IsNullOrEmpty(field)) return "";
            if (field.Contains(',') || field.Contains('"') || field.Contains('\n') || field.Contains('\r'))
            {
                return "\"" + field.Replace("\"", "\"\"") + "\"";
            }
            return field;
        }
    }
}
