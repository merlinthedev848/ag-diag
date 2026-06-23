using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace AgilicoConnectChecker
{
    public class PcapCapturer
    {
        private readonly MemoryStream _outputStream;
        private readonly object _lock = new object();
        private bool _isWriting = false;

        private int _packetCount = 0;
        private int _totalBytes = 0;
        private DateTime? _startTime;

        private Socket? _rawSocket;
        private Task? _captureTask;
        private CancellationTokenSource? _captureCts;
        private string? _ipFilter;

        public string? IpFilter
        {
            get { lock (_lock) return _ipFilter; }
            set { lock (_lock) _ipFilter = value; }
        }

        public int PacketCount
        {
            get { lock (_lock) return _packetCount; }
        }

        public int TotalBytes
        {
            get { lock (_lock) return _totalBytes; }
        }

        public double DurationSeconds
        {
            get
            {
                lock (_lock)
                {
                    if (!_isWriting || !_startTime.HasValue) return 0;
                    return (DateTime.UtcNow - _startTime.Value).TotalSeconds;
                }
            }
        }

        public PcapCapturer()
        {
            _outputStream = new MemoryStream();
        }

        public void Start(bool startRawSniffer = false, string? ipFilter = null)
        {
            Stop();

            lock (_lock)
            {
                _outputStream.SetLength(0);
                WriteGlobalHeader();
                _packetCount = 0;
                _totalBytes = 0;
                _startTime = DateTime.UtcNow;
                _ipFilter = ipFilter;
                _isWriting = true;

                if (startRawSniffer)
                {
                    StartRawSocketSniffer();
                }
            }
        }

        public void Stop()
        {
            lock (_lock)
            {
                _isWriting = false;
                try
                {
                    _captureCts?.Cancel();
                    _rawSocket?.Close();
                }
                catch { }

                _captureCts = null;
                _rawSocket = null;
                _captureTask = null;
            }
        }

        public byte[] GetPcapBytes()
        {
            lock (_lock)
            {
                return _outputStream.ToArray();
            }
        }

        private void WriteGlobalHeader()
        {
            // PCAP Global Header (24 bytes)
            WriteUInt32(0xa1b2c3d4); // Magic Number
            WriteUInt16(2);          // Major Version
            WriteUInt16(4);          // Minor Version
            WriteInt32(0);           // Timezone offset (GMT)
            WriteUInt32(0);          // Accuracy
            WriteUInt32(65535);      // Snaplen (max packet length captured)
            WriteUInt32(1);          // LinkType (1 = Ethernet DLT_EN10MB)
        }

        public void RecordPacket(byte[] payload, string srcIp, int srcPort, string destIp, int destPort, bool isUdp)
        {
            lock (_lock)
            {
                if (!_isWriting) return;

                // Apply IP filter
                string? filter = _ipFilter;
                if (!string.IsNullOrEmpty(filter))
                {
                    if (srcIp != filter && destIp != filter)
                    {
                        return; // Skip
                    }
                }

                try
                {
                    IPAddress srcAddr = IPAddress.TryParse(srcIp, out var s) ? s : IPAddress.Loopback;
                    IPAddress destAddr = IPAddress.TryParse(destIp, out var d) ? d : IPAddress.Loopback;

                    // Build Dummy Ethernet Header (14 bytes)
                    byte[] ethHeader = new byte[14];
                    // Destination MAC: 00:11:22:33:44:55
                    ethHeader[0] = 0x00; ethHeader[1] = 0x11; ethHeader[2] = 0x22; ethHeader[3] = 0x33; ethHeader[4] = 0x44; ethHeader[5] = 0x55;
                    // Source MAC: 66:77:88:99:AA:BB
                    ethHeader[6] = 0x66; ethHeader[7] = 0x77; ethHeader[8] = 0x88; ethHeader[9] = 0x99; ethHeader[10] = 0xAA; ethHeader[11] = 0xBB;
                    // Type: 0x0800 (IPv4)
                    ethHeader[12] = 0x08; ethHeader[13] = 0x00;

                    // Build IPv4 Header (20 bytes)
                    byte[] ipHeader = new byte[20];
                    ipHeader[0] = 0x45; // Version 4, IHL 5 (20 bytes)
                    ipHeader[1] = 0x00; // DSCP / ECN

                    int transportLength = isUdp ? 8 : 20;
                    int totalLength = 20 + transportLength + payload.Length;
                    ipHeader[2] = (byte)((totalLength >> 8) & 0xFF);
                    ipHeader[3] = (byte)(totalLength & 0xFF);

                    ipHeader[4] = 0x00; ipHeader[5] = 0x00; // Identification
                    ipHeader[6] = 0x40; ipHeader[7] = 0x00; // Don't Fragment flag set
                    ipHeader[8] = 64;   // TTL
                    ipHeader[9] = (byte)(isUdp ? 17 : 6); // Protocol (17=UDP, 6=TCP)
                    ipHeader[10] = 0x00; ipHeader[11] = 0x00; // Checksum placeholder

                    // IPs
                    Array.Copy(srcAddr.GetAddressBytes(), 0, ipHeader, 12, 4);
                    Array.Copy(destAddr.GetAddressBytes(), 0, ipHeader, 16, 4);

                    // Compute IPv4 Checksum
                    ushort ipChecksum = ComputeIpChecksum(ipHeader);
                    ipHeader[10] = (byte)((ipChecksum >> 8) & 0xFF);
                    ipHeader[11] = (byte)(ipChecksum & 0xFF);

                    // Build Transport Header (UDP/TCP)
                    byte[] transportHeader = new byte[transportLength];
                    // Source Port
                    transportHeader[0] = (byte)((srcPort >> 8) & 0xFF);
                    transportHeader[1] = (byte)(srcPort & 0xFF);
                    // Destination Port
                    transportHeader[2] = (byte)((destPort >> 8) & 0xFF);
                    transportHeader[3] = (byte)(destPort & 0xFF);

                    if (isUdp)
                    {
                        // UDP Length (8 + payload_length)
                        int udpLen = 8 + payload.Length;
                        transportHeader[4] = (byte)((udpLen >> 8) & 0xFF);
                        transportHeader[5] = (byte)(udpLen & 0xFF);
                        // Checksum (0x0000 = disabled/none)
                        transportHeader[6] = 0x00;
                        transportHeader[7] = 0x00;
                    }
                    else
                    {
                        // TCP Fields (Dummy sequence, flags = PSH|ACK)
                        // Seq Num
                        transportHeader[4] = 0x00; transportHeader[5] = 0x00; transportHeader[6] = 0x00; transportHeader[7] = 0x01;
                        // Ack Num
                        transportHeader[8] = 0x00; transportHeader[9] = 0x00; transportHeader[10] = 0x00; transportHeader[11] = 0x01;
                        // Data Offset (5 = 20 bytes)
                        transportHeader[12] = 0x50;
                        // Flags (PSH | ACK = 0x18)
                        transportHeader[13] = 0x18;
                        // Window Size (64240)
                        transportHeader[14] = 0xFA; transportHeader[15] = 0xF0;
                        // Checksum & Urgent pointer
                        transportHeader[16] = 0x00; transportHeader[17] = 0x00;
                        transportHeader[18] = 0x00; transportHeader[19] = 0x00;
                    }

                    // Build PCAP Packet Header (16 bytes)
                    long microsec = DateTime.UtcNow.Ticks / 10;
                    long seconds = microsec / 1000000;
                    long useconds = microsec % 1000000;

                    int capLength = ethHeader.Length + ipHeader.Length + transportHeader.Length + payload.Length;

                    WriteUInt32((uint)seconds);
                    WriteUInt32((uint)useconds);
                    WriteUInt32((uint)capLength); // Captured length
                    WriteUInt32((uint)capLength); // Original length

                    // Write packet data
                    _outputStream.Write(ethHeader, 0, ethHeader.Length);
                    _outputStream.Write(ipHeader, 0, ipHeader.Length);
                    _outputStream.Write(transportHeader, 0, transportHeader.Length);
                    _outputStream.Write(payload, 0, payload.Length);

                    _packetCount++;
                    _totalBytes += 16 + capLength;
                }
                catch { }
            }
        }

        private ushort ComputeIpChecksum(byte[] header)
        {
            uint sum = 0;
            for (int i = 0; i < header.Length; i += 2)
            {
                ushort temp = (ushort)((header[i] << 8) + header[i + 1]);
                sum += temp;
            }
            while ((sum >> 16) != 0)
            {
                sum = (sum & 0xFFFF) + (sum >> 16);
            }
            return (ushort)~sum;
        }

        private void WriteUInt32(uint val)
        {
            byte[] bytes = BitConverter.GetBytes(val);
            if (!BitConverter.IsLittleEndian) Array.Reverse(bytes);
            _outputStream.Write(bytes, 0, 4);
        }

        private void WriteInt32(int val)
        {
            byte[] bytes = BitConverter.GetBytes(val);
            if (!BitConverter.IsLittleEndian) Array.Reverse(bytes);
            _outputStream.Write(bytes, 0, 4);
        }

        private void WriteUInt16(ushort val)
        {
            byte[] bytes = BitConverter.GetBytes(val);
            if (!BitConverter.IsLittleEndian) Array.Reverse(bytes);
            _outputStream.Write(bytes, 0, 2);
        }
        private void StartRawSocketSniffer()
        {
            try
            {
                string localIp = GetLocalIpAddress();
                if (localIp == "127.0.0.1" || string.IsNullOrEmpty(localIp))
                {
                    throw new InvalidOperationException("No active local IP address found to bind packet sniffer.");
                }

                _rawSocket = new Socket(AddressFamily.InterNetwork, SocketType.Raw, ProtocolType.IP);
                _rawSocket.Bind(new IPEndPoint(IPAddress.Parse(localIp), 0));
                
                byte[] inVal = new byte[] { 1, 0, 0, 0 };
                byte[] outVal = new byte[] { 0, 0, 0, 0 };
                _rawSocket.IOControl(IOControlCode.ReceiveAll, inVal, outVal);

                _captureCts = new CancellationTokenSource();
                var token = _captureCts.Token;

                _captureTask = Task.Run(() => CaptureLoopAsync(token), token);
            }
            catch (SocketException ex)
            {
                if (ex.SocketErrorCode == SocketError.AccessDenied || ex.ErrorCode == 10013)
                {
                    throw new UnauthorizedAccessException("Administrative privileges are required to perform raw packet capture on Windows. Please run the application as Administrator.", ex);
                }
                throw;
            }
        }

        private async Task CaptureLoopAsync(CancellationToken token)
        {
            byte[] buffer = new byte[65535];
            while (!token.IsCancellationRequested)
            {
                try
                {
                    var socket = _rawSocket;
                    if (socket == null) break;

                    int bytesReceived = await socket.ReceiveAsync(buffer.AsMemory(), SocketFlags.None, token);
                    if (bytesReceived <= 0) continue;

                    byte[] ipPacket = new byte[bytesReceived];
                    Array.Copy(buffer, 0, ipPacket, 0, bytesReceived);

                    if (ipPacket.Length >= 20)
                    {
                        var srcIp = new IPAddress(new[] { ipPacket[12], ipPacket[13], ipPacket[14], ipPacket[15] }).ToString();
                        var destIp = new IPAddress(new[] { ipPacket[16], ipPacket[17], ipPacket[18], ipPacket[19] }).ToString();

                        string? filter = IpFilter;
                        if (!string.IsNullOrEmpty(filter))
                        {
                            if (srcIp != filter && destIp != filter)
                            {
                                continue;
                            }
                        }

                        RecordRawIpPacket(ipPacket);
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception)
                {
                    if (token.IsCancellationRequested || _rawSocket == null)
                        break;
                    await Task.Delay(10, token);
                }
            }
        }

        public void RecordRawIpPacket(byte[] ipPacket)
        {
            lock (_lock)
            {
                if (!_isWriting) return;

                try
                {
                    byte[] ethHeader = new byte[14];
                    ethHeader[0] = 0x00; ethHeader[1] = 0x11; ethHeader[2] = 0x22; ethHeader[3] = 0x33; ethHeader[4] = 0x44; ethHeader[5] = 0x55;
                    ethHeader[6] = 0x66; ethHeader[7] = 0x77; ethHeader[8] = 0x88; ethHeader[9] = 0x99; ethHeader[10] = 0xAA; ethHeader[11] = 0xBB;
                    ethHeader[12] = 0x08; ethHeader[13] = 0x00; // Type: IPv4

                    int capLength = ethHeader.Length + ipPacket.Length;

                    long microsec = DateTime.UtcNow.Ticks / 10;
                    long seconds = microsec / 1000000;
                    long useconds = microsec % 1000000;

                    WriteUInt32((uint)seconds);
                    WriteUInt32((uint)useconds);
                    WriteUInt32((uint)capLength);
                    WriteUInt32((uint)capLength);

                    _outputStream.Write(ethHeader, 0, ethHeader.Length);
                    _outputStream.Write(ipPacket, 0, ipPacket.Length);

                    _packetCount++;
                    _totalBytes += 16 + capLength;
                }
                catch { }
            }
        }

        private string GetLocalIpAddress()
        {
            try
            {
                using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, 0);
                socket.Connect("8.8.8.8", 65530);
                var endPoint = socket.LocalEndPoint as IPEndPoint;
                return endPoint?.Address.ToString() ?? "";
            }
            catch
            {
                return "";
            }
        }
    }
}
