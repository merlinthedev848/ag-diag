# Agilico MSP Toolkit for macOS (v4.1.2)

[![Build macOS DMG Installers](https://github.com/merlinthedev848/ag-diag/actions/workflows/build_dmg.yml/badge.svg?branch=macos)](https://github.com/merlinthedev848/ag-diag/actions/workflows/build_dmg.yml)
[![.NET 8.0](https://img.shields.io/badge/.NET-8.0-blue.svg)](https://dotnet.microsoft.com/)
[![Avalonia UI 11.2](https://img.shields.io/badge/Avalonia-11.2.8-purple.svg)](https://avaloniaui.net/)

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

## 🛠️ Multi-Architecture Editions & Builds

The toolkit provides two build editions for macOS:
- **Full Standalone Edition**: Self-contained runtime requiring zero host dependencies.
- **Lite Edition**: Framework-dependent distribution (~28 MB DMG vs ~104 MB Full) focused on rapid deployment and core network diagnostics.

| Edition | Target Platform | Bundle Name | DMG Installer | Size | Architecture |
| :--- | :--- | :--- | :--- | :--- | :--- |
| **Full** | Apple Silicon | `Agilico_MSP_Toolkit_arm64.app` | `Agilico_MSP_Toolkit_arm64.dmg` | ~104.7 MB | Apple M1/M2/M3/M4 |
| **Full** | Intel Mac | `Agilico_MSP_Toolkit_x64.app` | `Agilico_MSP_Toolkit_x64.dmg` | ~98.2 MB | x86_64 Intel Macs |
| **Lite** | Apple Silicon | `Agilico_MSP_Toolkit_Lite_arm64.app` | `Agilico_MSP_Toolkit_Lite_arm64.dmg` | ~28.8 MB | Apple M1/M2/M3/M4 |
| **Lite** | Intel Mac | `Agilico_MSP_Toolkit_Lite_x64.app` | `Agilico_MSP_Toolkit_Lite_x64.dmg` | ~28.8 MB | x86_64 Intel Macs |

### Building & Packaging DMG Installers

#### On Windows:
Build all editions (Full + Lite):
```powershell
.\build_macos.ps1 -Edition All
```
Or build only the Lite edition:
```powershell
.\build_macos.ps1 -Edition Lite
```
Or repackage `.dmg` disk images from existing bundles:
```powershell
.\build_dmg.ps1
```

#### On macOS:
Build all editions with native Apple `hdiutil` UDZO disk images:
```bash
chmod +x ./build_macos.sh
./build_macos.sh all
```
Or build only the Lite edition:
```bash
./build_macos.sh lite
```

#### Automated GitHub Actions CI/CD:
The repository includes `.github/workflows/build_dmg.yml` which automatically compiles and packages all 4 DMG installers (Full & Lite for Apple Silicon and Intel) on GitHub's `macos-latest` runners.

---

## 💾 Installation & Usage on macOS

1. Double-click the generated `.dmg` file (`Agilico_MSP_Toolkit_arm64.dmg` or `Agilico_MSP_Toolkit_x64.dmg`).
2. Drag **Agilico MSP Toolkit** into the **Applications** folder shortcut.
3. Launch **Agilico MSP Toolkit** from Applications or Spotlight.

---

## 💻 Tech Stack
- **UI Framework**: Avalonia UI 11 (Fluent Dark Theme)
- **Runtime**: .NET 8.0
- **Protocols**: UDP, TCP, ICMP, SIP, RTP, SignalR, HTTP/S
- **macOS Integrations**: `networksetup`, `scutil`, `dscacheutil`, `airport`, `afconvert`, `lsof`
