using System;
using System.IO;
using System.Net;
using System.Text;

namespace AgilicoConnectChecker
{
    public class PcapCapturer
    {
        private readonly MemoryStream _outputStream;
        private readonly object _lock = new object();
        private bool _isWriting = false;

        public PcapCapturer()
        {
            _outputStream = new MemoryStream();
        }

        public void Start()
        {
            lock (_lock)
            {
                _outputStream.SetLength(0);
                WriteGlobalHeader();
                _isWriting = true;
            }
        }

        public void Stop()
        {
            lock (_lock)
            {
                _isWriting = false;
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
    }
}
