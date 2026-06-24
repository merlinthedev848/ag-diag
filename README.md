# Agilico Network Diagnostic Tool (v3.5.6)

An advanced, premium-themed desktop diagnostic application built for Windows, designed to verify local and outbound network readiness for the **Agilico Connect** application. It conducts deep packet analysis, network path tracing, port connectivity probing, and VoIP simulation, providing IT engineers and end-users with instant, actionable diagnostics.

---

## Key Features

### 1. Core Outbound Probing Engine (10 Critical Tests)
The tool executes a comprehensive suite of ten parallelized outbound checks:
- **DNS Domain & Resolution**: Probes and resolves critical service domains (`*.hp2k.co.uk`) and verifies public DNS servers.
- **HTTP/HTTPS Outbound Probes**: Validates secure web connectivity and SSL/TLS handshakes on ports 80/443.
- **NTP Subsystem Check (UDP 123)**: Probes NTP time servers to ensure correct local system time synchronization.
- **Agilico STUN Servers**: Queries Agilico's STUN infrastructure to identify firewall NAT blockages.
- **Google STUN Servers**: Queries backup Google STUN endpoints for baseline connectivity validation.
- **NAT Routing & Hops Check**: Traces network routes to default gateways to detect Double NAT configurations.
- **NAT Port Translation**: Checks if outbound ports are randomized or preserved.
- **SIP ALG Detection**: Sends raw SIP OPTIONS requests to detect SIP inspection/tampering engines.
- **RTP Jitter/Loss Check**: Simulates real-time G.711 media path traffic to calculate packet loss, jitter, and Estimated MOS scores.
- **Inbound Signalling & Presence (SignalR)**: Verifies direct WebSocket connection status with the core SignalR hubs.

---

### 2. High-Performance Speed Test
A strictly timed speed test providing real-world network throughput statistics in under 6 seconds:
- **Dynamic CDN Failover (Download)**: Employs a 3-second test starting with Cloudflare. If it encounters a rate limit (`429`), access block (`403`), or network exception, it seamlessly fails over to Cachefly or ThinkBroadband in under `0.2` seconds.
- **Non-Looping Real-Time Upload**: Uploads exactly 4 parallel POST requests of 25MB each (eliminating resource-intensive request looping). Tracks progress in real-time as bytes are pushed using a custom `ProgressStream` wrapper.
- **Live Updates**: Displays dynamic, smooth throughput speeds on the UI every 200ms.

---

### 3. AS Number & GeoIP Hop Mapping (Traceroute)
An enhanced traceroute module:
- Measures hop-by-hop latency and packet travel paths.
- Performs reverse DNS lookups on intermediate hop routers.
- Integrates real-time **AS Number (ASN)** and **GeoIP location mapping** (Country, City) for every router hop.

---

### 4. Active Socket Monitor (Netstat Viewer)
A real-time socket inspection tool:
- Lists all active TCP/UDP sockets, matching local address/port to remote destination.
- Maps connections to their owning Windows **Process Name** and **PID**.
- Supports real-time text filtering and protocol search.

---

### 5. Real-Time Packet Capturer (PCAP Capturer)
A native packet capture module:
- Captures raw inbound and outbound network frames on the active network interface.
- Filters and structures data.
- Exports captures to standard Wireshark-compatible `.pcap` files for deep network analysis.

---

### 6. Subnet Scanner (LAN Scanner)
- Identifies active devices connected to the local subnet.
- Dynamically queries active adapter network interfaces to retrieve the **actual CIDR subnet mask** (rather than assuming `/24`).
- Resolves device Hostnames and queries MAC addresses to retrieve manufacturer names.

---

### 7. Ping Latency Tracker
- Continuously pings any host to monitor network stability.
- Charts latency changes over time.
- Exports timestamped latency logs to a standard `.csv` file.

---

### 8. Automated Repair Toolkit ("One-Click Fix")
A self-healing utility built directly into the Settings panel:
- **Application Cache Cleaner**: Automatically wipes the Agilico Connect Windows client configuration cache folder (`%LOCALAPPDATA%\AgilicoConnectV5forWindows`).
- **Client App Restarter**: Terminates any hung or active Agilico Connect client processes and restarts the application cleanly.

---

## Technology Stack

- **Framework**: WPF (Windows Presentation Foundation)
- **Language**: C# / .NET 8.0
- **UI Styling**: Premium Dark Slate Navy design system with custom interactive CheckBoxes, layout grids, animations, and typography.
- **Outbound protocols**: UDP, TCP, ICMP, SIP, RTP, SignalR.

---

## Deployment Packages

The project publishes two distinct binaries to make it versatile for all environments:

1. **Standalone Version (`AgilicoNetworkDiagnosticTool-Standalone.exe`)**
   - Self-contained executable including all runtime dependencies.
   - Run immediately on any Windows machine without pre-installed runtimes.
   - Packaged as a `.zip` archive (`AgilicoNetworkDiagnosticTool-Standalone.zip`).
   
2. **Lite Version (`AgilicoNetworkDiagnosticTool-Lite.exe`)**
   - Framework-dependent executable.
   - Extremely small footprint (requires .NET 8 Desktop Runtime to be installed on the machine).
   - Packaged as a `.zip` archive (`AgilicoNetworkDiagnosticTool-Lite.zip`).

---

## How to Build

Run the following commands inside the repository to publish both versions:

```powershell
# Standalone publish
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o "bin\Publish\Standalone"

# Lite publish
dotnet publish -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -p:PublishReadyToRun=false -p:IncludeNativeLibrariesForSelfExtract=false -o "bin\Publish\Lite"
```
