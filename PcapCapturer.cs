using System;
using System.Buffers.Binary;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace agilicomsptoolkit
{
    /// <summary>
    /// Writes PCAP data using LINKTYPE_RAW for Windows IP raw-socket capture.
    /// RecordPacket is retained for compatibility and creates a synthetic IPv4 packet;
    /// callers should prefer RecordRawIpPacket when a packet was actually captured.
    /// </summary>
    public sealed class PcapCapturer : IDisposable
    {
        private const uint MaxCaptureBytes = 25u * 1024u * 1024u;
        private const int MaxPackets = 50_000;
        private const uint LinkTypeRaw = 101;
        private readonly MemoryStream _outputStream = new();
        private readonly object _lock = new();
        private CancellationTokenSource? _captureCts;
        private Task? _captureTask;
        private Socket? _rawSocket;
        private bool _isWriting;
        private int _packetCount;
        private long _totalBytes;
        private DateTime? _startTime;
        private string? _ipFilter;

        public string? IpFilter { get { lock (_lock) return _ipFilter; } set { lock (_lock) _ipFilter = value; } }
        public int PacketCount { get { lock (_lock) return _packetCount; } }
        public long TotalBytes { get { lock (_lock) return _totalBytes; } }
        public bool ContainsSyntheticPackets { get; private set; }
        public double DurationSeconds { get { lock (_lock) return _startTime.HasValue ? (DateTime.UtcNow - _startTime.Value).TotalSeconds : 0; } }

        public void Start(bool startRawSniffer = false, string? ipFilter = null, string? adapterIp = null)
        {
            Stop();
            lock (_lock)
            {
                _outputStream.SetLength(0);
                WriteGlobalHeader();
                _packetCount = 0;
                _totalBytes = 0;
                ContainsSyntheticPackets = false;
                _startTime = DateTime.UtcNow;
                _ipFilter = string.IsNullOrWhiteSpace(ipFilter) ? null : ipFilter.Trim();
                _isWriting = true;
            }
            if (startRawSniffer) StartRawSocketSniffer(adapterIp);
        }

        public void Stop()
        {
            CancellationTokenSource? cts;
            Socket? socket;
            lock (_lock)
            {
                _isWriting = false;
                cts = _captureCts;
                socket = _rawSocket;
                _captureCts = null;
                _rawSocket = null;
                _captureTask = null;
            }
            try { cts?.Cancel(); } catch (ObjectDisposedException) { }
            try { socket?.Dispose(); } catch (ObjectDisposedException) { }
            try { cts?.Dispose(); } catch (ObjectDisposedException) { }
        }

        public void Dispose() { Stop(); _outputStream.Dispose(); }
        public byte[] GetPcapBytes() { lock (_lock) return _outputStream.ToArray(); }

        private void WriteGlobalHeader()
        {
            Span<byte> header = stackalloc byte[24];
            BinaryPrimitives.WriteUInt32LittleEndian(header[0..4], 0xa1b2c3d4);
            BinaryPrimitives.WriteUInt16LittleEndian(header[4..6], 2);
            BinaryPrimitives.WriteUInt16LittleEndian(header[6..8], 4);
            BinaryPrimitives.WriteInt32LittleEndian(header[8..12], 0);
            BinaryPrimitives.WriteUInt32LittleEndian(header[12..16], 0);
            BinaryPrimitives.WriteUInt32LittleEndian(header[16..20], 65535);
            BinaryPrimitives.WriteUInt32LittleEndian(header[20..24], LinkTypeRaw);
            _outputStream.Write(header);
        }

        /// <summary>Creates a synthetic IPv4 packet for compatibility. It is not a real packet capture.</summary>
        public void RecordPacket(byte[] payload, string srcIp, int srcPort, string destIp, int destPort, bool isUdp)
        {
            if (payload == null) throw new ArgumentNullException(nameof(payload));
            if (!TryParseIpv4(srcIp, out var source) || !TryParseIpv4(destIp, out var destination)) return;
            if (!IsPortValid(srcPort) || !IsPortValid(destPort) || !PassesFilter(source, destination)) return;
            byte[] packet = BuildSyntheticIpv4Packet(payload, source, srcPort, destination, destPort, isUdp);
            ContainsSyntheticPackets = true;
            WritePcapRecord(packet);
        }

        public void RecordRawIpPacket(byte[] ipPacket)
        {
            if (ipPacket == null || ipPacket.Length < 20) return;
            int version = ipPacket[0] >> 4;
            if (version != 4 && version != 6) return;
            if (version == 4)
            {
                int headerLength = (ipPacket[0] & 0x0F) * 4;
                if (headerLength < 20 || headerLength > ipPacket.Length) return;
                var source = new IPAddress(ipPacket.AsSpan(12, 4));
                var destination = new IPAddress(ipPacket.AsSpan(16, 4));
                if (!PassesFilter(source, destination)) return;
            }
            else if (ipPacket.Length < 40) return;
            WritePcapRecord(ipPacket);
        }

        private void WritePcapRecord(byte[] packet)
        {
            lock (_lock)
            {
                if (!_isWriting || packet.Length == 0 || _packetCount >= MaxPackets || _totalBytes + 16L + packet.Length > MaxCaptureBytes) return;
                try
                {
                    long micros = (DateTime.UtcNow.Ticks - DateTime.UnixEpoch.Ticks) / TimeSpan.TicksPerMicrosecond;
                    Span<byte> header = stackalloc byte[16];
                    BinaryPrimitives.WriteUInt32LittleEndian(header[0..4], (uint)(micros / 1_000_000));
                    BinaryPrimitives.WriteUInt32LittleEndian(header[4..8], (uint)(micros % 1_000_000));
                    BinaryPrimitives.WriteUInt32LittleEndian(header[8..12], (uint)packet.Length);
                    BinaryPrimitives.WriteUInt32LittleEndian(header[12..16], (uint)packet.Length);
                    _outputStream.Write(header);
                    _outputStream.Write(packet, 0, packet.Length);
                    _packetCount++;
                    _totalBytes += 16L + packet.Length;
                }
                catch (IOException) { _isWriting = false; }
                catch (ObjectDisposedException) { _isWriting = false; }
            }
        }

        private bool PassesFilter(IPAddress source, IPAddress destination)
        {
            string? filter = IpFilter;
            return string.IsNullOrWhiteSpace(filter) || string.Equals(source.ToString(), filter, StringComparison.OrdinalIgnoreCase) || string.Equals(destination.ToString(), filter, StringComparison.OrdinalIgnoreCase);
        }

        private static bool TryParseIpv4(string value, out IPAddress address) => IPAddress.TryParse(value, out address!) && address.AddressFamily == AddressFamily.InterNetwork;
        private static bool IsPortValid(int port) => port is >= 0 and <= 65535;

        private static byte[] BuildSyntheticIpv4Packet(byte[] payload, IPAddress source, int sourcePort, IPAddress destination, int destinationPort, bool isUdp)
        {
            int transportLength = isUdp ? 8 : 20;
            int totalLength = checked(20 + transportLength + payload.Length);
            if (totalLength > ushort.MaxValue) throw new ArgumentOutOfRangeException(nameof(payload));
            byte[] packet = new byte[totalLength];
            packet[0] = 0x45;
            packet[8] = 64;
            packet[9] = (byte)(isUdp ? 17 : 6);
            packet[2] = (byte)(totalLength >> 8);
            packet[3] = (byte)totalLength;
            source.TryWriteBytes(packet.AsSpan(12, 4), out _);
            destination.TryWriteBytes(packet.AsSpan(16, 4), out _);
            int offset = 20;
            WriteUInt16(packet, offset, (ushort)sourcePort);
            WriteUInt16(packet, offset + 2, (ushort)destinationPort);
            if (isUdp)
            {
                WriteUInt16(packet, offset + 4, (ushort)(8 + payload.Length));
                WriteUInt16(packet, offset + 6, 0);
            }
            else
            {
                packet[offset + 12] = 0x50;
                packet[offset + 13] = 0x18;
                WriteUInt16(packet, offset + 14, 64240);
                WriteUInt16(packet, offset + 16, 0);
            }
            Buffer.BlockCopy(payload, 0, packet, offset + transportLength, payload.Length);
            ushort transportChecksum = ComputeTransportChecksum(packet, offset, transportLength + payload.Length, (byte)(isUdp ? 17 : 6));
            WriteUInt16(packet, offset + (isUdp ? 6 : 16), transportChecksum);
            WriteUInt16(packet, 10, 0);
            WriteUInt16(packet, 10, ComputeIpChecksum(packet, 0, 20));
            return packet;
        }

        private static ushort ComputeTransportChecksum(byte[] packet, int offset, int length, byte protocol)
        {
            uint sum = 0;
            for (int i = 12; i < 20; i += 2) sum += (uint)((packet[i] << 8) | packet[i + 1]);
            sum += protocol;
            sum += (uint)length;
            for (int i = offset; i < offset + length; i += 2)
            {
                ushort word = (ushort)(packet[i] << 8);
                if (i + 1 < offset + length) word |= packet[i + 1];
                sum += word;
            }
            return FoldChecksum(sum);
        }

        private static ushort ComputeIpChecksum(byte[] packet, int offset, int length)
        {
            uint sum = 0;
            for (int i = offset; i < offset + length; i += 2) sum += (uint)((packet[i] << 8) | packet[i + 1]);
            return FoldChecksum(sum);
        }

        private static ushort FoldChecksum(uint sum)
        {
            while ((sum >> 16) != 0) sum = (sum & 0xFFFF) + (sum >> 16);
            return (ushort)~sum;
        }

        private static void WriteUInt16(byte[] buffer, int offset, ushort value)
        {
            buffer[offset] = (byte)(value >> 8);
            buffer[offset + 1] = (byte)value;
        }

        private void StartRawSocketSniffer(string? adapterIp)
        {
            Socket? socket = null;
            CancellationTokenSource? cts = null;
            try
            {
                string localIp = !string.IsNullOrWhiteSpace(adapterIp) ? adapterIp : GetLocalIpAddress();
                if (!TryParseIpv4(localIp, out var address) || IPAddress.IsLoopback(address)) throw new InvalidOperationException("No active IPv4 address was found for packet capture.");
                socket = new Socket(AddressFamily.InterNetwork, SocketType.Raw, ProtocolType.IP);
                socket.Bind(new IPEndPoint(address, 0));
                socket.IOControl(IOControlCode.ReceiveAll, new byte[] { 1, 0, 0, 0 }, new byte[4]);
                cts = new CancellationTokenSource();
                lock (_lock)
                {
                    if (!_isWriting) { socket.Dispose(); cts.Dispose(); return; }
                    _rawSocket = socket;
                    _captureCts = cts;
                    _captureTask = Task.Run(() => CaptureLoopAsync(cts.Token), cts.Token);
                }
            }
            catch (SocketException ex) when (ex.SocketErrorCode == SocketError.AccessDenied || ex.ErrorCode == 10013)
            {
                socket?.Dispose(); cts?.Dispose();
                throw new UnauthorizedAccessException("Administrative privileges are required for raw packet capture on Windows.", ex);
            }
            catch { socket?.Dispose(); cts?.Dispose(); throw; }
        }

        private async Task CaptureLoopAsync(CancellationToken token)
        {
            byte[] buffer = new byte[65_535];
            while (!token.IsCancellationRequested)
            {
                try
                {
                    Socket? socket = _rawSocket;
                    if (socket == null) return;
                    int received = await socket.ReceiveAsync(buffer.AsMemory(), SocketFlags.None, token).ConfigureAwait(false);
                    if (received > 0)
                    {
                        byte[] packet = new byte[received];
                        Buffer.BlockCopy(buffer, 0, packet, 0, received);
                        RecordRawIpPacket(packet);
                    }
                }
                catch (OperationCanceledException) { return; }
                catch (ObjectDisposedException) { return; }
                catch (SocketException) when (token.IsCancellationRequested) { return; }
                catch (SocketException) { await Task.Delay(25, token).ConfigureAwait(false); }
            }
        }

        private static string GetLocalIpAddress()
        {
            foreach (var ip in Dns.GetHostAddresses(Dns.GetHostName())) if (ip.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(ip)) return ip.ToString();
            return string.Empty;
        }
    }
}
