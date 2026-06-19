# Agilico Connect Diagnostic Logic and Pseudocode

This document defines the heuristics, thresholds, and execution logic of the Agilico Connect diagnostic engine (`NetworkEngine.cs`).

---

## 1. Weighted Scoring System

Each check in the diagnostic suite is assigned a weight based on its critical nature for Agilico Connect VoIP operations. The total possible score is 100. If a test is skipped by the user, the total possible score is scaled proportionally to ensure accurate percentages.

| Test ID | Diagnostic Check | Protocol / Port | Severity | Weight |
|---|---|---|---|---|
| **1** | DNS Domain & Resolution | UDP 53 | Critical | 15 |
| **2** | HTTP/HTTPS Outbound | TCP 80/443 | Critical | 15 |
| **3** | NTP Time Sync | UDP 123 | Warning | 5 |
| **4** | Agilico STUN Servers | UDP 3478 | Critical | 15 |
| **5** | Google STUN Backup | UDP 3478 | Warning | 5 |
| **6** | NAT Routing & Hops | ICMP / IP Header | Warning | 5 |
| **7** | NAT Port Randomness | STUN mapped-ports | Warning | 5 |
| **8** | SIP ALG Detection | UDP 5060 | Critical | 15 |
| **9** | RTP Media Quality | UDP (various) | Critical | 15 |
| **10** | WebSocket Signalling | TCP 80/443 | Warning | 10 |
| **Total**| | | | **100** |

### Score Calculation Formula
$$\text{Score} = \text{Round}\left( \frac{\sum \text{Weights of Passed Selected Tests}}{\sum \text{Weights of All Selected Tests}} \times 100 \right)$$

---

## 2. Test-by-Test Diagnostics

### Test 1: DNS Domain & Resolution
*   **Target Domains**:
    *   `customerportal.hp2k.co.uk`
    *   `v1.softsignalling.eu-j.hp2k.co.uk`
    *   `v3.presence.eu-beta.hp2k.co.uk`
    *   `v1.rooms.eu-beta.hp2k.co.uk`
*   **Logic**:
    1.  Perform DNS resolution on all four domains using the local system DNS settings.
    2.  Query Google DNS (`8.8.8.8`) directly over UDP port 53 for transaction confirmation.
    3.  *Heuristics*: Uses randomized transaction IDs in DNS queries and validates them in responses to prevent cache poisoning/spoofing.
*   **Timeouts**: 1.5 seconds per query.

### Test 2: HTTP/HTTPS Outbound Web Requests
*   **Target Endpoint**: `https://customerportal.hp2k.co.uk`
*   **Logic**:
    1.  Initiate standard HTTPS request to verify web connectivity and port 443 compliance.
    2.  *Heuristics*: Verifies TLS validity strictly; invalid certificates are rejected to enforce secure routing.
*   **Timeouts**: 5 seconds.

### Test 3: NTP Time Sync
*   **Target Server**: `uk.pool.ntp.org`
*   **Logic**:
    1.  Transmit NTP client query payload over UDP port 123.
    2.  Receive response and validate header.
    3.  *Heuristics*: strictly checks that the returned packet is at least 48 bytes long and the mode field (bits 0–2 of byte 0) matches `4` (Server response).

### Test 4: Agilico STUN Servers
*   **Target Servers**:
    *   `stun-gb-a.hp2k.co.uk` (UDP 3478)
    *   `stun-gb-b.hp2k.co.uk` (UDP 3478)
    *   `stun-eu-a.hp2k.co.uk` (UDP 3478)
    *   `stun-eu-b.hp2k.co.uk` (UDP 3478)
*   **Logic**:
    1.  Send RFC 5389 STUN binding requests to each server.
    2.  Parse XOR-MAPPED-ADDRESS attributes.
    3.  *Heuristics*: strictly isolated queries with no Google fallback. If none of these servers respond, the test fails, exposing localized UDP blocking.

### Test 5: Google STUN Backup
*   **Target Servers**:
    *   `stun.l.google.com` (UDP 3478)
    *   `stun1.l.google.com` (UDP 3478)
    *   `stun2.l.google.com` (UDP 3478)
*   **Logic**: Query Google STUN backup nodes using standard binding requests.

### Test 6: NAT Routing & Hops
*   **Logic**:
    1.  Determine public IP from STUN tests.
    2.  Query internal gateway/external hops.
    3.  *Heuristics*: Detects if more than one layer of network address translation exists between the host and the WAN interface.

### Test 7: NAT Port Randomness
*   **Logic**:
    1.  Query STUN servers from different local port sockets.
    2.  Analyze mapped ports returned by STUN servers.
    3.  *Heuristics*: If mapped WAN ports equal the local source ports, port preservation is active (preferred for WebRTC). If ports are scrambled, warn about possible symmetric NAT.

### Test 8: SIP ALG Detection
*   **Target Server**: `customerportal.hp2k.co.uk` (UDP 5060)
*   **Logic**:
    1.  Send a mock SIP `OPTIONS` request.
    2.  Include local endpoint information in the `Via` and `Contact` header strings.
    3.  Analyze the incoming response.
    4.  *Heuristics*: If the returned header fields are altered (e.g. local IP changed to public IP by the router), SIP ALG is active. Also checks Call-ID correlation to reject spoofed/stale packets.

### Test 9: RTP Media Quality
*   **Target Paths**:
    *   Path A: SIP Port 5060 to SipAlgServer
    *   Path B: Agilico STUN Port 3478 to stun-gb-a.hp2k.co.uk
    *   Path C: Google STUN Port 19302 to stun.l.google.com
*   **Logic**:
    1.  Transmit burst streams (e.g., 50 UDP packets, 20ms spacing) simulating G.711 RTP voice audio.
    2.  *Heuristics*: Measures packet loss, jitter (using RFC 3550 variance formulas), and round-trip time (RTT). Uses isolated per-packet cancellation timeouts to ensure robust tracking.

### Test 10: WebSocket Signalling
*   **Target URL**: Hub endpoints at `customerportal.hp2k.co.uk`
*   **Logic**:
    1.  Connect to Hub using ASP.NET Core SignalR client.
    2.  *Heuristics*: Forces **WebSockets-only** transport to verify that stateful connection upgrade protocols are not stripped or blocked by firewall DPI.
