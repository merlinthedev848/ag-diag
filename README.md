# Agilico MSP Toolkit for macOS (v4.1.2)

An advanced cross-platform desktop diagnostic and voice engineering application built specifically for macOS (Apple Silicon M1/M2/M3/M4 & Intel Macs) using **Avalonia UI (v11)** and **.NET 8.0**.

Designed to verify local and outbound network readiness for the **Agilico Connect** application on macOS, it performs deep packet inspection, network route tracing with GeoIP/ASN, port connectivity probing, speed testing, and VoIP simulation.

---

## 🚀 Key Features on macOS

### 1. 10-Point Outbound Diagnostic Suite
- **DNS Domain & Resolution**: Probes `*.hp2k.co.uk` and public DNS resolvers.
- **HTTP/HTTPS Probes**: Validates secure web connectivity and SSL/TLS handshakes on ports 80/443.
- **NTP Subsystem (UDP 123)**: Probes NTP time synchronization servers.
- **Agilico STUN Infrastructure**: Identifies NAT type and firewall blockages.
- **Google STUN Servers**: Backup STUN endpoint connectivity validation.
- **NAT Routing & Hops**: Traces routes to default gateways to detect Double NAT configurations.
- **NAT Port Translation**: Checks if outbound ports are preserved (Full Cone) or randomized (Symmetric).
- **SIP ALG Detection**: Sends raw SIP OPTIONS requests to detect SIP inspection/tampering engines.
- **RTP Jitter & Loss (MOS Scoring)**: Simulates real-time G.711 voice media path traffic to calculate packet loss, jitter, and Estimated MOS scores (1.0 to 4.4).
- **SignalR Signalling & Presence**: Verifies direct WebSocket connection status with core SignalR hubs.

---

### 2. High-Performance Speed Test
- **Multi-CDN Failover (Download)**: Ultra-fast throughput testing starting with Cloudflare and failing over to secondary CDNs seamlessly.
- **Parallel Upload Streaming**: Streams parallel POST buffers with real-time throughput metrics.
- **Live Visual Metrics**: Live Mbps readouts and progress gauges updated in real-time.

---

### 3. Traceroute with AS Number (ASN) & GeoIP Mapping
- Measures hop-by-hop latency and router IP travel paths.
- Reverse DNS lookups on intermediate hop routers.
- AS Number (ASN) and GeoIP country/city resolution for every network hop.

---

### 4. macOS Voice & Network Engineering Tools
- **Flush macOS DNS**: Flushes macOS DNS cache and restarts `mDNSResponder`.
- **Renew DHCP Lease**: Renews local IP lease with gateway via `networksetup`.
- **Wi-Fi Signal & RF Quality Analyzer**: Inspects RSSI dBm signal, RF noise floor, channel, and voice readiness.
- **CoreAudio Device & Microphone Inspector**: Inspects default voice input/output devices and headsets.
- **Active Socket Monitor (Netstat Viewer)**: Real-time inspection of open sockets, matching local/remote endpoints to macOS Process Name and PID.
- **Subnet LAN Scanner**: Scans local CIDR subnets, parses macOS ARP tables (`arp -a`), and resolves hardware manufacturers via built-in OUI database.
- **Continuous Ping Latency Tracker**: Real-time ping stability tracker with jitter, loss %, and CSV export.
- **VoIP Audio Converter**: Converts MP3/WAV/M4A audio to PCM 8kHz 16-bit Mono WAV using `afconvert` or `ffmpeg`.
- **Agilico Connect Client Reset**: Cleans local caches and restarts the macOS softphone client cleanly.

---

### 5. Wireshark-Compatible PCAP Capturer
- Captures and writes raw packet buffers directly to standard `.pcap` files for deep analysis in Wireshark.

---

## 🛠️ Multi-Architecture Builds

The toolkit compiles into self-contained macOS `.app` bundles for both Apple Silicon and Intel architectures:

| Target Platform | Runtime Identifier | Bundle Name | Architecture |
| :--- | :--- | :--- | :--- |
| **Apple Silicon** | `osx-arm64` | `Agilico_MSP_Toolkit_arm64.app` | Apple M1/M2/M3/M4 |
| **Intel Mac** | `osx-x64` | `Agilico_MSP_Toolkit_x64.app` | x86_64 Intel Macs |

### Building from Source

#### On Windows (Cross-compilation):
```powershell
.\build_macos.ps1
```

#### On macOS / Linux:
```bash
chmod +x ./build_macos.sh
./build_macos.sh
```

---

## 💻 Tech Stack
- **UI Framework**: Avalonia UI 11 (Fluent Dark Theme)
- **Runtime**: .NET 8.0
- **Protocols**: UDP, TCP, ICMP, SIP, RTP, SignalR, HTTP/S
- **macOS Integrations**: `networksetup`, `scutil`, `dscacheutil`, `airport`, `afconvert`, `lsof`
