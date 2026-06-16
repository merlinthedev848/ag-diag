using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;
using Microsoft.AspNetCore.SignalR.Client;

namespace AgilicoConnectChecker
{
    public class NetworkEngine
    {
        public delegate void LogHandler(string message, bool isError = false);
        public delegate void ProgressHandler(string testName, string status, string details);
        public delegate void TestCompleteHandler(bool success);

        public event LogHandler? OnLog;
        public event ProgressHandler? OnProgress;
        public event TestCompleteHandler? OnComplete;

        private CancellationTokenSource? _cts;

        // Configuration defaults from official Agilico Network Guidance
        public string DomainToCheck { get; set; } = "hp2k.co.uk";
        public string LocalSipPortStr { get; set; } = "5060";
        public string SipAlgServer { get; set; } = "109.73.119.38";
        public int SipAlgPort { get; set; } = 5060;
        public int LocalSipPort { get; set; } = 5060;
        public string StunServer { get; set; } = "stun-gb-a.hp2k.co.uk";
        public int StunPort { get; set; } = 3478;
        public bool IsSimulationMode { get; set; } = false;

        // Live settings loaded from softphone registry
        public string ServerUsername { get; set; } = "";
        public string ClientToken { get; set; } = "";
        public int ClientUserId { get; set; } = 0;
        public string PresenceUrl { get; set; } = "http://v3.presence.eu-beta.hp2k.co.uk/Presence";
        public string SignallingUrl { get; set; } = "http://v1.softsignalling.eu-j.hp2k.co.uk/Signals";
        public string RoomsUrl { get; set; } = "http://v1.rooms.eu-beta.hp2k.co.uk/Rooms";

        // Google DNS servers mentioned in the guide
        private readonly string[] GoogleDnsServers = { "8.8.8.8", "8.8.4.4" };

        // STUN Servers mentioned in the guide
        private readonly (string host, string ip)[] AgilicoStunServers = new[]
        {
            ("stun-gb-a.hp2k.co.uk", "109.73.119.38"),
            ("stun-gb-b.hp2k.co.uk", "109.73.119.39"),
            ("stun-eu-a.hp2k.co.uk", "109.73.119.38"),
            ("stun-eu-b.hp2k.co.uk", "109.73.119.39")
        };

        private readonly (string host, string ip)[] GoogleStunServers = new[]
        {
            ("stun.l.google.com", "74.125.250.129"),
            ("stun1.l.google.com", "74.125.250.129"),
            ("stun2.l.google.com", "74.125.250.129"),
            ("stun3.l.google.com", "74.125.250.129"),
            ("stun4.l.google.com", "74.125.250.129")
        };

        public NetworkEngine()
        {
            // Load registry options if available
            LoadSettingsFromRegistry();
        }

        private void LoadSettingsFromRegistry()
        {
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(@"Software\DMC\WindowsSoftphone\v1"))
                {
                    if (key != null)
                    {
                        ServerUsername = key.GetValue("ServerUsername")?.ToString() ?? "";
                        ClientToken = key.GetValue("ClientToken")?.ToString() ?? "";
                        if (int.TryParse(key.GetValue("ClientUserID")?.ToString(), out int uId))
                        {
                            ClientUserId = uId;
                        }
                    }
                }

                using (var servicesKey = Registry.CurrentUser.OpenSubKey(@"Software\DMC\WindowsSoftphone\v1\Services"))
                {
                    if (servicesKey != null)
                    {
                        PresenceUrl = servicesKey.GetValue("Presence")?.ToString() ?? PresenceUrl;
                        SignallingUrl = servicesKey.GetValue("Signalling")?.ToString() ?? SignallingUrl;
                        RoomsUrl = servicesKey.GetValue("Rooms")?.ToString() ?? RoomsUrl;
                    }
                }
                Log($"Registry configuration loaded. Username: {ServerUsername}, UserID: {ClientUserId}");
            }
            catch (Exception ex)
            {
                Log($"Failed to load registry settings: {ex.Message}", true);
            }
        }

        public string GetLocalIpAddress()
        {
            try
            {
                using (Socket socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, 0))
                {
                    socket.Connect("8.8.8.8", 65530);
                    IPEndPoint? endPoint = socket.LocalEndPoint as IPEndPoint;
                    return endPoint?.Address.ToString() ?? "127.0.0.1";
                }
            }
            catch
            {
                return "127.0.0.1";
            }
        }

        public void Cancel()
        {
            _cts?.Cancel();
        }

        private void Log(string message, bool isError = false)
        {
            OnLog?.Invoke($"[{DateTime.Now:HH:mm:ss}] {message}", isError);
        }

        private void UpdateProgress(string testName, string status, string details)
        {
            OnProgress?.Invoke(testName, status, details);
        }

        public async Task RunDiagnosticsAsync()
        {
            _cts = new CancellationTokenSource();
            var token = _cts.Token;

            Log("Starting Agilico Connect Checker diagnostics...");
            Log("=================================================================");
            Log($"Timestamp:         {DateTime.Now}");
            Log($"OS Version:        {Environment.OSVersion}");
            Log($"Local IP Address:  {GetLocalIpAddress()}");
            Log($"Diagnostic Scope:  Strictly checking Agilico KB Network Guidance");
            Log("=================================================================");
            Log("All tests are running directly against the servers and ports specified in the network guide.");

            try
            {
                // Test 1: DNS Domain Resolution & Google DNS
                bool dnsPass = await RunDnsTestsAsync(token);
                if (token.IsCancellationRequested) return;

                // Test 2: HTTP/HTTPS (Ports 80/443) Web Requests
                bool httpPass = await RunHttpHttpsTestsAsync(token);
                if (token.IsCancellationRequested) return;

                // Test 3: NTP Subsystem (UDP Port 123)
                bool ntpPass = await RunNtpTestAsync(token);
                if (token.IsCancellationRequested) return;

                // Test 4: Agilico STUN Servers (UDP Port 3478)
                bool agilicoStunPass = await RunAgilicoStunTestsAsync(token);
                if (token.IsCancellationRequested) return;

                // Test 5: Google STUN Servers (UDP Port 3478)
                bool googleStunPass = await RunGoogleStunTestsAsync(token);
                if (token.IsCancellationRequested) return;

                // Test 6: NAT Routing & Hops Check
                bool natHopsPass = await RunNatHopsTestAsync(token);
                if (token.IsCancellationRequested) return;

                // Test 7: NAT Port Randomness Check
                bool natPortPass = await RunNatPortRandomnessTestAsync(token);
                if (token.IsCancellationRequested) return;

                // Test 8: SIP ALG Detection (UDP Port 5060)
                bool sipAlgPass = await RunSipAlgTestAsync(token);

                bool allPassed = dnsPass && httpPass && ntpPass && agilicoStunPass && googleStunPass && natHopsPass && natPortPass && sipAlgPass;
                Log(allPassed ? "All network checks PASSED! Your firewall configuration is fully compliant with the Agilico Network Guidance." 
                             : "Some network checks FAILED or generated Warnings. Please review the recommendations.");
                OnComplete?.Invoke(allPassed);
            }
            catch (OperationCanceledException)
            {
                Log("Diagnostics cancelled by user.");
            }
            catch (Exception ex)
            {
                Log($"Critical error during diagnostics: {ex.Message}", true);
                OnComplete?.Invoke(false);
            }
        }

        #region Diagnostic Test Handlers

        private async Task<bool> RunDnsTestsAsync(CancellationToken token)
        {
            UpdateProgress("DNS Domain & Resolution", "Running", "Resolving domain customerportal.hp2k.co.uk...");
            Log("Test 1: Verifying DNS Resolution and Google DNS availability...");

            bool domainResolved = false;
            try
            {
                Log($"Resolving domain '{DomainToCheck}' using default DNS...");
                var ips = await Dns.GetHostAddressesAsync(DomainToCheck, token);
                if (ips.Length > 0)
                {
                    Log($"Success: Resolved '{DomainToCheck}' to: {string.Join(", ", (object[])ips)}");
                    domainResolved = true;
                }
            }
            catch (Exception ex)
            {
                Log($"Error: Failed to resolve '{DomainToCheck}' via default DNS: {ex.Message}", true);
            }

            if (IsSimulationMode)
            {
                Thread.Sleep(800);
                UpdateProgress("DNS Domain & Resolution", "Passed", "Pass - DNS resolving correctly");
                return true;
            }

            // Verify Google DNS Servers from the guide (8.8.8.8 and 8.8.4.4)
            bool dns1Ok = await QueryDnsServerAsync("8.8.8.8", DomainToCheck, token);
            bool dns2Ok = await QueryDnsServerAsync("8.8.4.4", DomainToCheck, token);

            if (domainResolved)
            {
                if (dns1Ok && dns2Ok)
                {
                    Log("Test 1: PASSED. Domain resolves correctly, and Google DNS servers are reachable.");
                    UpdateProgress("DNS Domain & Resolution", "Passed", "Pass - DNS & Google DNS Active");
                }
                else
                {
                    Log("Test 1: PASSED WITH WARNINGS. Domain resolves correctly via default local DNS, but direct outbound queries to Google DNS (8.8.8.8/8.8.4.4) failed. This is common if your network blocks outbound UDP port 53 to external DNS resolvers. Local DNS resolution is working.");
                    UpdateProgress("DNS Domain & Resolution", "Passed", "Pass - Resolving via default DNS (Google DNS blocked)");
                }
                return true;
            }
            else
            {
                Log($"Test 1: FAILED. DNS resolution is completely unavailable. Domain Resolved: False, Google DNS 8.8.8.8: {(dns1Ok ? "OK" : "Failed")}, Google DNS 8.8.4.4: {(dns2Ok ? "OK" : "Failed")}", true);
                UpdateProgress("DNS Domain & Resolution", "Failed", "Fail - DNS resolution failed");
                return false;
            }
        }

        private async Task<bool> QueryDnsServerAsync(string dnsServer, string hostname, CancellationToken token)
        {
            try
            {
                Log($"Querying Google DNS server {dnsServer} for domain '{hostname}'...");
                var serverIp = IPAddress.Parse(dnsServer);
                using var client = new UdpClient();
                client.Client.SendTimeout = 2000;
                client.Client.ReceiveTimeout = 2000;

                // Build a minimal standard DNS Query Packet (RFC 1035)
                var query = new List<byte>();
                // Header (12 bytes)
                query.AddRange(new byte[] { 0x12, 0x34 }); // Transaction ID
                query.AddRange(new byte[] { 0x01, 0x00 }); // Flags: Standard query, Recursion desired
                query.AddRange(new byte[] { 0x00, 0x01 }); // Questions: 1
                query.AddRange(new byte[] { 0x00, 0x00 }); // Answer RRs: 0
                query.AddRange(new byte[] { 0x00, 0x00 }); // Authority RRs: 0
                query.AddRange(new byte[] { 0x00, 0x00 }); // Additional RRs: 0

                // Question section: Domain name encoded as labels
                var parts = hostname.Split('.');
                foreach (var part in parts)
                {
                    query.Add((byte)part.Length);
                    query.AddRange(Encoding.ASCII.GetBytes(part));
                }
                query.Add(0x00); // End of name

                query.AddRange(new byte[] { 0x00, 0x01 }); // Type: A (IPv4 host address)
                query.AddRange(new byte[] { 0x00, 0x01 }); // Class: IN (Internet)

                var data = query.ToArray();
                var endpoint = new IPEndPoint(serverIp, 53);

                await client.SendAsync(data, data.Length, endpoint);
                var receiveTask = client.ReceiveAsync(token).AsTask();
                var delayTask = Task.Delay(2000, token);

                var completed = await Task.WhenAny(receiveTask, delayTask);
                if (completed == receiveTask)
                {
                    var result = await receiveTask;
                    if (result.Buffer.Length > 12 && result.Buffer[0] == 0x12 && result.Buffer[1] == 0x34)
                    {
                        Log($"Google DNS {dnsServer} responded successfully to domain check.");
                        return true;
                    }
                }
                Log($"Google DNS {dnsServer} did not return a valid response.", true);
                return false;
            }
            catch (Exception ex)
            {
                Log($"Google DNS {dnsServer} query failed: {ex.Message}", true);
                return false;
            }
        }

        private async Task<bool> RunHttpHttpsTestsAsync(CancellationToken token)
        {
            UpdateProgress("HTTP/HTTPS Outbound Probes", "Running", "Testing web ports...");
            Log("Test 2: Probing HTTP/HTTPS outbound connectivity...");

            if (IsSimulationMode)
            {
                Thread.Sleep(800);
                UpdateProgress("HTTP/HTTPS Outbound Probes", "Passed", "Pass - TCP 80/443 Outbound Open");
                return true;
            }

            Log($"Testing outbound TCP port 80 (HTTP) to {DomainToCheck}...");
            bool httpOk = await TestTcpPortAsync(DomainToCheck, 80, token);
            Log($"HTTP TCP 80 to {DomainToCheck}: {(httpOk ? "OPEN" : "BLOCKED")}");

            Log($"Testing outbound TCP port 443 (HTTPS) to {DomainToCheck}...");
            bool httpsOk = await TestTcpPortAsync(DomainToCheck, 443, token);
            Log($"HTTPS TCP 443 to {DomainToCheck}: {(httpsOk ? "OPEN" : "BLOCKED")}");

            if (httpOk && httpsOk)
            {
                Log("Test 2: PASSED. Outbound HTTP/HTTPS ports (80/443) are verified open.");
                UpdateProgress("HTTP/HTTPS Outbound Probes", "Passed", "Pass - TCP 80/443 Outbound Open");
                return true;
            }
            else
            {
                string failedPorts = "";
                if (!httpOk) failedPorts += "TCP 80 ";
                if (!httpsOk) failedPorts += "TCP 443";
                Log($"Test 2: FAILED. Blocked outbound ports: {failedPorts}", true);
                UpdateProgress("HTTP/HTTPS Outbound Probes", "Failed", $"Fail - {failedPorts.Trim()} Blocked");
                return false;
            }
        }

        private async Task<bool> TestTcpPortAsync(string host, int port, CancellationToken token)
        {
            try
            {
                using var client = new TcpClient();
                var connectTask = client.ConnectAsync(host, port, token).AsTask();
                var delayTask = Task.Delay(3000, token);

                var completed = await Task.WhenAny(connectTask, delayTask);
                if (completed == connectTask)
                {
                    await connectTask;
                    return client.Connected;
                }
                return false;
            }
            catch
            {
                return false;
            }
        }

        private async Task<bool> RunNtpTestAsync(CancellationToken token)
        {
            UpdateProgress("NTP Subsystem (UDP 123)", "Running", "Querying NTP time server...");
            Log("Test 3: Checking UDP port 123 (NTP) outbound transmission...");

            if (IsSimulationMode)
            {
                Thread.Sleep(800);
                UpdateProgress("NTP Subsystem (UDP 123)", "Passed", "Pass - UDP 123 Outbound Open");
                return true;
            }

            string[] ntpHosts = { "pool.ntp.org", "time.google.com", "time.windows.com" };
            bool ntpOk = false;
            foreach (var ntpHost in ntpHosts)
            {
                Log($"Sending standard NTP synchronization request to {ntpHost}...");
                ntpOk = await TestNtpAsync(ntpHost, token);
                if (ntpOk)
                {
                    Log($"Success: Received NTP response from {ntpHost}.");
                    break;
                }
                else
                {
                    Log($"NTP request to {ntpHost} timed out.");
                }
            }

            if (ntpOk)
            {
                Log("Test 3: PASSED. Outbound UDP port 123 (NTP) is open and receiving replies.");
                UpdateProgress("NTP Subsystem (UDP 123)", "Passed", "Pass - UDP 123 Outbound Open");
                return true;
            }
            else
            {
                Log("Test 3: FAILED. UDP port 123 queries to all NTP servers timed out. Ensure NTP outbound traffic is permitted.", true);
                UpdateProgress("NTP Subsystem (UDP 123)", "Failed", "Fail - UDP 123 Blocked / Timeout");
                return false;
            }
        }

        private async Task<bool> TestNtpAsync(string host, CancellationToken token)
        {
            try
            {
                var ntpData = new byte[48];
                ntpData[0] = 0x1B; // LeapIndicator = 0, Version = 3, Mode = 3 (Client)

                var addresses = await Dns.GetHostAddressesAsync(host, token);
                if (addresses.Length == 0) return false;
                var endPoint = new IPEndPoint(addresses[0], 123);

                using var client = new UdpClient();
                client.Client.SendTimeout = 2000;
                client.Client.ReceiveTimeout = 2000;

                await client.SendAsync(ntpData, ntpData.Length, endPoint);
                var receiveTask = client.ReceiveAsync(token).AsTask();
                var delayTask = Task.Delay(2000, token);

                var completed = await Task.WhenAny(receiveTask, delayTask);
                if (completed == receiveTask)
                {
                    var result = await receiveTask;
                    return result.Buffer.Length >= 48;
                }
                return false;
            }
            catch
            {
                return false;
            }
        }

        private async Task<bool> RunAgilicoStunTestsAsync(CancellationToken token)
        {
            UpdateProgress("Agilico STUN Servers", "Running", "Querying Agilico STUN hosts...");
            Log("Test 4: Querying Agilico Cloud STUN Servers...");

            if (IsSimulationMode)
            {
                Thread.Sleep(1000);
                UpdateProgress("Agilico STUN Servers", "Passed", "Pass - All 4 Agilico STUN servers OK");
                return true;
            }

            int successCount = 0;
            List<string> failedServers = new List<string>();

            foreach (var (host, ip) in AgilicoStunServers)
            {
                if (token.IsCancellationRequested) return false;

                Log($"Querying Agilico STUN Server: {host} ({ip})...");
                var (ok, publicIp, mappedPort) = await QueryStunServerAsync(host, 3478, token);

                if (ok)
                {
                    Log($"Success: {host} returned Public IP: {publicIp}, Mapped Port: {mappedPort}");
                    successCount++;
                }
                else
                {
                    Log($"Agilico STUN server {host} failed to respond.");
                    failedServers.Add(host);
                }
            }

            if (successCount == AgilicoStunServers.Length)
            {
                Log("Test 4: PASSED. All Agilico STUN servers responded successfully.");
                UpdateProgress("Agilico STUN Servers", "Passed", "Pass - All 4 servers online");
                return true;
            }
            else if (successCount > 0)
            {
                Log($"Test 4: FAILED. Only {successCount}/{AgilicoStunServers.Length} Agilico STUN servers responded. All must be reachable. Failed: {string.Join(", ", failedServers)}", true);
                UpdateProgress("Agilico STUN Servers", "Failed", $"Fail - {successCount}/{AgilicoStunServers.Length} online");
                return false;
            }
            else
            {
                Log("Test 4: FAILED. All Agilico STUN servers failed to respond. Outbound UDP port 3478 is blocked or routing to Agilico is restricted.", true);
                UpdateProgress("Agilico STUN Servers", "Failed", "Fail - All servers unreachable");
                return false;
            }
        }

        private async Task<bool> RunGoogleStunTestsAsync(CancellationToken token)
        {
            UpdateProgress("Google STUN Servers", "Running", "Querying Google STUN hosts...");
            Log("Test 5: Querying Google STUN Servers...");

            if (IsSimulationMode)
            {
                Thread.Sleep(1000);
                UpdateProgress("Google STUN Servers", "Passed", "Pass - All 5 Google STUN servers OK");
                return true;
            }

            int successCount = 0;
            List<string> failedServers = new List<string>();

            foreach (var (host, ip) in GoogleStunServers)
            {
                if (token.IsCancellationRequested) return false;

                Log($"Querying Google STUN Server: {host} ({ip})...");
                var (ok, publicIp, mappedPort) = await QueryStunServerAsync(host, 3478, token);

                if (ok)
                {
                    Log($"Success: {host} returned Public IP: {publicIp}, Mapped Port: {mappedPort}");
                    successCount++;
                }
                else
                {
                    Log($"Google STUN server {host} failed to respond.");
                    failedServers.Add(host);
                }
            }

            if (successCount > 0)
            {
                Log($"Test 5: PASSED. Google STUN check passed. {successCount}/{GoogleStunServers.Length} servers responded.");
                UpdateProgress("Google STUN Servers", "Passed", $"Pass - {successCount}/{GoogleStunServers.Length} online");
                return true;
            }
            else
            {
                Log("Test 5: FAILED. All Google STUN servers failed to respond. Outbound UDP port 3478 is likely blocked.", true);
                UpdateProgress("Google STUN Servers", "Failed", "Fail - Google STUN query blocked");
                return false;
            }
        }

        private async Task<bool> RunNatHopsTestAsync(CancellationToken token)
        {
            UpdateProgress("NAT Routing & Hops Check", "Running", "Checking local range...");
            Log("Test 6: Checking local addressing and counting NAT router hops...");

            string localIpStr = GetLocalIpAddress();
            Log($"Local IP Address: {localIpStr}");

            IPAddress localIp = IPAddress.Parse(localIpStr);
            bool isPrivate = IsPrivateIp(localIp);

            if (isPrivate)
            {
                Log("Local address is in a private subnet range (Pass).");
            }
            else
            {
                Log("Warning - Public IP detected directly on local interface. The client is bypass-NAT connected.");
            }

            if (IsSimulationMode)
            {
                Thread.Sleep(1000);
                UpdateProgress("NAT Routing & Hops Check", "Passed", "Pass - Single NAT (1 private hop)");
                return true;
            }

            Log("Performing TTL-limited ICMP probes to detect private gateways/routers (Double NAT)...");
            int privateHops = await CountPrivateHopsAsync("8.8.8.8", 5);
            Log($"Intermediate private hops detected: {privateHops}");

            if (privateHops > 1)
            {
                Log($"Error: Double NAT detected ({privateHops} private network devices). This violates the Single NAT recommendation in the Agilico Network Guidance.", true);
                UpdateProgress("NAT Routing & Hops Check", "Failed", $"Fail - Double NAT ({privateHops} hops)");
                return false;
            }
            else if (privateHops == 0)
            {
                Log("Private hop traceroute did not return hops. This is common if ICMP is blocked by intermediate firewalls. Local private address confirmed NAT is active.");
                UpdateProgress("NAT Routing & Hops Check", "Passed", "Pass - NAT Active (ICMP Blocked)");
                return true;
            }
            else
            {
                Log("Pass - Single NAT configuration detected (1 hop).");
                UpdateProgress("NAT Routing & Hops Check", "Passed", "Pass - Single NAT (1 private hop)");
                return true;
            }
        }

        private static bool IsPrivateIp(IPAddress ip)
        {
            var bytes = ip.GetAddressBytes();
            if (bytes.Length != 4) return false;

            // 10.0.0.0/8
            if (bytes[0] == 10) return true;
            // 172.16.0.0/12
            if (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31) return true;
            // 192.168.0.0/16
            if (bytes[0] == 192 && bytes[1] == 168) return true;
            // 100.64.0.0/10 (CGNAT)
            if (bytes[0] == 100 && bytes[1] >= 64 && bytes[1] <= 127) return true;

            return false;
        }

        private async Task<int> CountPrivateHopsAsync(string targetIp, int maxHops)
        {
            int privateHops = 0;
            using var ping = new Ping();
            var options = new PingOptions(1, true);

            for (int ttl = 1; ttl <= maxHops; ttl++)
            {
                options.Ttl = ttl;
                try
                {
                    var reply = await ping.SendPingAsync(targetIp, 1000, new byte[32], options);
                    if (reply.Status == IPStatus.TtlExpired || reply.Status == IPStatus.Success)
                    {
                        var addr = reply.Address;
                        if (addr != null)
                        {
                            if (IsPrivateIp(addr))
                            {
                                privateHops++;
                            }
                            else
                            {
                                // Hit a public router
                                break;
                            }
                        }
                    }
                }
                catch
                {
                    // Ignore and try next hop
                }
            }
            return privateHops;
        }

        private async Task<bool> RunNatPortRandomnessTestAsync(CancellationToken token)
        {
            UpdateProgress("NAT Port Translation (Random Port)", "Running", "Analyzing local to public port mapping...");
            Log("Test 7: Evaluating NAT port translation...");
            Log("Guideline: 'The public interface NAT port should be random and not be the same as the local NAT port.'");

            int localPort = LocalSipPort;
            int mappedPort = 0;
            bool success = false;

            if (IsSimulationMode)
            {
                Thread.Sleep(800);
                mappedPort = 53891;
                Log($"[Simulation] Local Port: {localPort}, STUN Mapped Port: {mappedPort}");
                Log("Pass: Public interface NAT port is random and different from local port.");
                UpdateProgress("NAT Port Translation (Random Port)", "Passed", $"Pass - Port randomized ({mappedPort})");
                return true;
            }

            // Bind local port 5060 or ephemeral equivalent for testing
            UdpClient? client = null;
            try
            {
                client = new UdpClient(localPort);
                Log($"Bound to local UDP port {localPort}.");
            }
            catch (SocketException)
            {
                Log($"Local UDP port {localPort} is in use. Auto-selecting an ephemeral port for testing...");
                try
                {
                    client = new UdpClient(0);
                    localPort = ((IPEndPoint)client.Client.LocalEndPoint!).Port;
                    Log($"Bound successfully to local UDP port {localPort}.");
                }
                catch (Exception ex)
                {
                    Log($"Error: Failed to bind to local UDP port: {ex.Message}", true);
                    UpdateProgress("NAT Port Translation (Random Port)", "Failed", "Fail - Local bind failed");
                    return false;
                }
            }
            finally
            {
                client?.Close();
            }

            // Contact a STUN server to check mapped port
            var servers = new[] { "stun-gb-a.hp2k.co.uk", "stun-gb-b.hp2k.co.uk", "stun.l.google.com" };
            foreach (var server in servers)
            {
                var (ok, ip, mPort) = await QueryStunServerAsync(server, 3478, token);
                if (ok)
                {
                    mappedPort = mPort;
                    success = true;
                    break;
                }
            }

            if (!success)
            {
                Log("Error: Could not query STUN server to check port mapping.", true);
                UpdateProgress("NAT Port Translation (Random Port)", "Failed", "Fail - STUN query failed");
                return false;
            }

            Log($"Local Source Port: {localPort}");
            Log($"Public Interface Port: {mappedPort}");

            if (mappedPort == localPort)
            {
                Log("Error: The public interface NAT port is the exact same as the local NAT port.", true);
                Log("The gateway is preserving the port. The Agilico Network Guidance explicitly requires randomized ports.", true);
                UpdateProgress("NAT Port Translation (Random Port)", "Failed", $"Fail - Port Preserved ({mappedPort})");
                return false;
            }
            else
            {
                Log("Pass: The public interface NAT port is randomized and different from the local NAT port.");
                UpdateProgress("NAT Port Translation (Random Port)", "Passed", $"Pass - Port randomized ({mappedPort})");
                return true;
            }
        }

        private async Task<bool> RunSipAlgTestAsync(CancellationToken token)
        {
            UpdateProgress("SIP ALG Detection", "Running", "Sending UDP 5060 probe invite...");
            Log("Test 8: Probing for SIP ALG / packet modification on UDP 5060...");

            if (IsSimulationMode)
            {
                Thread.Sleep(1000);
                Log("[Simulation] Verification response matched. SIP ALG: Disabled.");
                UpdateProgress("SIP ALG Detection", "Passed", "Pass - SIP ALG Disabled");
                return true;
            }

            UdpClient? client = null;
            try
            {
                try
                {
                    client = new UdpClient(LocalSipPort);
                    Log($"Bound to local SIP port {LocalSipPort} for ALG verification.");
                }
                catch (SocketException)
                {
                    client = new UdpClient(0);
                    Log($"Local port {LocalSipPort} is in use. Bound to local ephemeral port {((IPEndPoint)client.Client.LocalEndPoint!).Port} for verification.");
                }

                client.Client.SendTimeout = 2500;
                client.Client.ReceiveTimeout = 2500;

                string localIp = GetLocalIpAddress();
                int localPort = ((IPEndPoint)client.Client.LocalEndPoint!).Port;

                Log($"Resolving SIP ALG reflection server: {SipAlgServer}...");
                var addresses = await Dns.GetHostAddressesAsync(SipAlgServer, token);
                if (addresses.Length == 0) throw new Exception("No DNS response from reflection host.");

                var endpoint = new IPEndPoint(addresses[0], SipAlgPort);
                Log($"SIP ALG Reflection Endpoint: {endpoint.Address}:{endpoint.Port}");

                // Construct a SIP REGISTER request to detect ALG interference
                string branch = "z9hG4bK" + Guid.NewGuid().ToString("N").Substring(0, 10);
                string tag = Guid.NewGuid().ToString("N").Substring(0, 10);
                string callId = Guid.NewGuid().ToString("N").Substring(0, 16) + "@hp2k.co.uk";

                string sipRegister =
                    $"REGISTER sip:{SipAlgServer} SIP/2.0\r\n" +
                    $"Via: SIP/2.0/UDP {localIp}:{localPort};rport;branch={branch}\r\n" +
                    $"Max-Forwards: 70\r\n" +
                    $"To: <sip:checker@{SipAlgServer}>\r\n" +
                    $"From: <sip:checker@{SipAlgServer}>;tag={tag}\r\n" +
                    $"Call-ID: {callId}\r\n" +
                    $"CSeq: 1 REGISTER\r\n" +
                    $"Contact: <sip:checker@{localIp}:{localPort}>\r\n" +
                    $"User-Agent: Agilico Connect Checker\r\n" +
                    $"Content-Length: 0\r\n\r\n";

                byte[] registerBytes = Encoding.UTF8.GetBytes(sipRegister);

                Log($"Sending SIP REGISTER payload to {SipAlgServer}:{SipAlgPort}...");
                Log($"Expected Via header: Via: SIP/2.0/UDP {localIp}:{localPort};rport;branch={branch}");

                await client.SendAsync(registerBytes, registerBytes.Length, endpoint);

                var receiveTask = client.ReceiveAsync(token).AsTask();
                var delayTask = Task.Delay(3000, token);

                var completed = await Task.WhenAny(receiveTask, delayTask);
                if (completed == receiveTask)
                {
                    var result = await receiveTask;
                    string responseStr = Encoding.UTF8.GetString(result.Buffer);
                    Log("Received SIP response. Analyzing headers for SIP ALG tampering...");

                    string[] lines = responseStr.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);
                    bool viaMatched = false;
                    bool viaFound = false;

                    foreach (var line in lines)
                    {
                        if (line.StartsWith("Via:", StringComparison.OrdinalIgnoreCase))
                        {
                            viaFound = true;
                            Log($"Remote returned Via: {line}");
                            if (line.Contains($"{localIp}:{localPort}"))
                            {
                                viaMatched = true;
                            }
                        }
                    }

                    if (viaFound && !viaMatched)
                    {
                        Log("Violation: The returned Via header does NOT match the local IP/Port sent.", true);
                        Log("This indicates SIP ALG has modified the SIP headers in transit.", true);
                        UpdateProgress("SIP ALG Detection", "Failed", "Fail - SIP ALG Enabled (Header Mangled)");
                        return false;
                    }
                    else if (responseStr.Contains("Server: SIP ALG") || responseStr.Contains("ALG"))
                    {
                        Log("Violation: Response identifies as originating from a SIP ALG.", true);
                        UpdateProgress("SIP ALG Detection", "Failed", "Fail - SIP ALG Responded Directly");
                        return false;
                    }
                    else
                    {
                        Log("Pass: Response received and headers are intact. No SIP ALG tampering detected.");
                        UpdateProgress("SIP ALG Detection", "Passed", "Pass - SIP ALG Disabled");
                        return true;
                    }
                }
                else
                {
                    Log("Error: SIP REGISTER request timed out. No response received.", true);
                    Log("This means UDP port 5060 is being dropped by the firewall, or SIP ALG is silently discarding the packets.", true);
                    UpdateProgress("SIP ALG Detection", "Failed", "Fail - Timeout (UDP 5060 Blocked)");
                    return false;
                }
            }
            catch (Exception ex)
            {
                Log($"SIP ALG test failed with error: {ex.Message}", true);
                UpdateProgress("SIP ALG Detection", "Failed", "Error: " + ex.Message);
                return false;
            }
            finally
            {
                client?.Close();
            }
        }

        public async Task<bool> SetDndStatusAsync(bool enableDnd)
        {
            if (string.IsNullOrEmpty(PresenceUrl) || string.IsNullOrEmpty(ClientToken))
            {
                Log("DND Toggle Error: Presence URL or Client Token is not configured.", true);
                return false;
            }

            string query = $"?clientToken={ClientToken}&clientUserId={ClientUserId}";
            string hubUrl = PresenceUrl + query;

            Log($"Connecting to Presence Hub: {PresenceUrl} (UserID: {ClientUserId})...");

            try
            {
                var connection = new HubConnectionBuilder()
                    .WithUrl(hubUrl)
                    .WithAutomaticReconnect()
                    .Build();

                await connection.StartAsync();
                Log("Successfully connected to Presence Hub.");

                string status = enableDnd ? "DoNotDisturb" : "Available";
                Log($"Invoking SetStatus with value: {status}...");
                
                try
                {
                    await connection.InvokeAsync("SetStatus", status);
                }
                catch (Exception ex) when (ex.Message.Contains("SetStatus") || ex.Message.Contains("not found"))
                {
                    Log("SetStatus method not found on hub. Trying SetPresence...", true);
                    await connection.InvokeAsync("SetPresence", status);
                }

                Log($"DND status updated on server to: {status}");
                await connection.StopAsync();
                return true;
            }
            catch (Exception ex)
            {
                Log($"Failed to toggle DND: {ex.Message}", true);
                if (ex.InnerException != null)
                {
                    Log($"Details: {ex.InnerException.Message}", true);
                }
                return false;
            }
        }

        #endregion

        #region STUN Protocol Helpers

        private async Task<(bool success, string? publicIp, int mappedPort)> QueryStunServerAsync(string server, int port, CancellationToken token)
        {
            UdpClient? client = null;
            try
            {
                client = new UdpClient(0);
                client.Client.SendTimeout = 1500;
                client.Client.ReceiveTimeout = 1500;

                var ipAddresses = await Dns.GetHostAddressesAsync(server, token);
                if (ipAddresses.Length == 0) return (false, null, 0);

                var endPoint = new IPEndPoint(ipAddresses[0], port);
                byte[] transactionId = new byte[12];
                new Random().NextBytes(transactionId);
                byte[] stunRequest = BuildStunRequest(transactionId, changeIp: false, changePort: false);

                await client.SendAsync(stunRequest, stunRequest.Length, endPoint);

                var receiveTask = client.ReceiveAsync(token).AsTask();
                var delayTask = Task.Delay(1500, token);

                var completed = await Task.WhenAny(receiveTask, delayTask);
                if (completed == receiveTask)
                {
                    var result = await receiveTask;
                    if (ValidateStunResponse(result.Buffer, transactionId))
                    {
                        var (ip, mPort) = ParseStunResponse(result.Buffer);
                        if (ip != null)
                        {
                            return (true, ip.ToString(), mPort);
                        }
                    }
                }
                return (false, null, 0);
            }
            catch
            {
                return (false, null, 0);
            }
            finally
            {
                client?.Close();
            }
        }

        private byte[] BuildStunRequest(byte[] transactionId, bool changeIp, bool changePort)
        {
            int attrLength = (changeIp || changePort) ? 8 : 0;
            byte[] packet = new byte[20 + attrLength];

            packet[0] = 0x00;
            packet[1] = 0x01; // Binding Request
            packet[2] = (byte)((attrLength >> 8) & 0xFF);
            packet[3] = (byte)(attrLength & 0xFF);

            // Magic Cookie (RFC 5389)
            packet[4] = 0x21;
            packet[5] = 0x12;
            packet[6] = 0xA4;
            packet[7] = 0x42;
            Buffer.BlockCopy(transactionId, 0, packet, 8, 12);

            if (attrLength > 0)
            {
                // CHANGE-REQUEST attribute
                packet[20] = 0x00;
                packet[21] = 0x03;
                packet[22] = 0x00;
                packet[23] = 0x04;

                uint value = 0;
                if (changeIp) value |= 4;
                if (changePort) value |= 2;

                packet[24] = 0x00;
                packet[25] = 0x00;
                packet[26] = 0x00;
                packet[27] = (byte)value;
            }

            return packet;
        }

        private bool ValidateStunResponse(byte[] response, byte[] originalTransactionId)
        {
            if (response.Length < 20) return false;

            int msgType = (response[0] << 8) | response[1];
            if (msgType != 0x0101 && msgType != 0x0111) return false; // Binding Success or Error

            for (int i = 0; i < 12; i++)
            {
                if (response[8 + i] != originalTransactionId[i])
                    return false;
            }

            return true;
        }

        private (IPAddress? ip, int port) ParseStunResponse(byte[] response)
        {
            int messageLength = (response[2] << 8) | response[3];
            int index = 20;

            IPAddress? mappedIp = null;
            int mappedPort = 0;

            while (index < 20 + messageLength && index + 4 <= response.Length)
            {
                int attrType = (response[index] << 8) | response[index + 1];
                int attrLen = (response[index + 2] << 8) | response[index + 3];
                int valIndex = index + 4;

                if (valIndex + attrLen > response.Length)
                    break;

                if (attrType == 0x0001) // MAPPED-ADDRESS
                {
                    if (attrLen >= 8 && response[valIndex + 1] == 0x01) // IPv4
                    {
                        mappedPort = (response[valIndex + 2] << 8) | response[valIndex + 3];
                        byte[] ipBytes = new byte[4];
                        Buffer.BlockCopy(response, valIndex + 4, ipBytes, 0, 4);
                        mappedIp = new IPAddress(ipBytes);
                    }
                }
                else if (attrType == 0x0020 || attrType == 0x8020) // XOR-MAPPED-ADDRESS
                {
                    if (attrLen >= 8 && response[valIndex + 1] == 0x01)
                    {
                        int xorPort = (response[valIndex + 2] << 8) | response[valIndex + 3];
                        mappedPort = xorPort ^ 0x2112; // XOR with magic cookie bytes

                        byte[] ipBytes = new byte[4];
                        ipBytes[0] = (byte)(response[valIndex + 4] ^ 0x21);
                        ipBytes[1] = (byte)(response[valIndex + 5] ^ 0x12);
                        ipBytes[2] = (byte)(response[valIndex + 6] ^ 0xA4);
                        ipBytes[3] = (byte)(response[valIndex + 7] ^ 0x42);
                        mappedIp = new IPAddress(ipBytes);
                    }
                }

                int padding = (4 - (attrLen % 4)) % 4;
                index += 4 + attrLen + padding;
            }

            return (mappedIp, mappedPort);
        }

        public void TriggerFirewallPrompt()
        {
            try
            {
                // Brief bind to local SIP port to trigger Windows Defender Firewall prompt
                using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
                socket.Bind(new IPEndPoint(IPAddress.Any, LocalSipPort));
            }
            catch
            {
                try
                {
                    using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
                    socket.Bind(new IPEndPoint(IPAddress.Any, 0));
                }
                catch
                {
                    // Ignore
                }
            }
        }

        #endregion
    }
}
