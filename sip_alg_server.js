/**
 * Agilico Connect - SIP ALG Detection Server
 * 
 * This is a lightweight Node.js server that listens for incoming SIP UDP packets (normally on port 5060),
 * calculates the CRC32 hash of the raw received packet, and returns a SIP "200 OK" response 
 * with the "X-CSREQ" header containing the calculated hash.
 * 
 * Usage:
 *   node sip_alg_server.js [port]
 */

const dgram = require('dgram');

const port = process.argv[2] ? parseInt(process.argv[2]) : 5060;
const server = dgram.createSocket('udp4');

// CRC32 Table & Calculator
const crcTable = [];
for (let n = 0; n < 256; n++) {
    let c = n;
    for (let k = 0; k < 8; k++) {
        c = ((c & 1) ? (0xEDB88320 ^ (c >>> 1)) : (c >>> 1));
    }
    crcTable[n] = c;
}

function calculateCrc32(buf) {
    let crc = 0 ^ (-1);
    for (let i = 0; i < buf.length; i++) {
        crc = (crc >>> 8) ^ crcTable[(crc ^ buf[i]) & 0xFF];
    }
    return (crc ^ (-1)) >>> 0;
}

server.on('error', (err) => {
    console.error(`Server error:\n${err.stack}`);
    server.close();
});

server.on('message', (msg, rinfo) => {
    const messageStr = msg.toString('utf8');
    console.log(`Received packet from ${rinfo.address}:${rinfo.port} (Length: ${msg.length} bytes)`);

    // Only handle SIP requests (e.g. INVITE, REGISTER, etc.)
    if (!messageStr.startsWith('INVITE') && !messageStr.startsWith('REGISTER') && !messageStr.startsWith('OPTIONS')) {
        console.log("Not a standard SIP request. Ignoring.");
        return;
    }

    // Calculate CRC32 of received bytes
    const receivedCrc = calculateCrc32(msg);
    const hexCrc = receivedCrc.toString(16).toUpperCase().padStart(8, '0');
    console.log(`Calculated CRC32 of received packet: ${hexCrc}`);

    // Parse Call-ID, From, To, Via, CSeq headers to build a valid SIP response
    const viaLine = messageStr.match(/Via:[^\r\n]+/i);
    const fromLine = messageStr.match(/From:[^\r\n]+/i);
    const toLine = messageStr.match(/To:[^\r\n]+/i);
    const callIdLine = messageStr.match(/Call-ID:[^\r\n]+/i);
    const cseqLine = messageStr.match(/CSeq:[^\r\n]+/i);

    const via = viaLine ? viaLine[0] : `Via: SIP/2.0/UDP ${rinfo.address}:${rinfo.port}`;
    const from = fromLine ? fromLine[0] : 'From: <sip:anonymous@anonymous.invalid>';
    const to = toLine ? toLine[0] : 'To: <sip:anonymous@anonymous.invalid>';
    const callId = callIdLine ? callIdLine[0] : 'Call-ID: unknown';
    const cseq = cseqLine ? cseqLine[0] : 'CSeq: 1 INVITE';

    // Construct SIP 200 OK response with the X-CSREQ header
    const response = 
        `SIP/2.0 200 OK\r\n` +
        `${via}\r\n` +
        `${from}\r\n` +
        `${to}\r\n` +
        `${callId}\r\n` +
        `${cseq}\r\n` +
        `User-Agent: Agilico Connect SIP ALG Verification Server\r\n` +
        `X-CSREQ: ${hexCrc}\r\n` +
        `Content-Length: 0\r\n\r\n`;

    const responseBuffer = Buffer.from(response, 'utf8');

    server.send(responseBuffer, 0, responseBuffer.length, rinfo.port, rinfo.address, (err) => {
        if (err) {
            console.error(`Error sending response to ${rinfo.address}:${rinfo.port}: ${err.message}`);
        } else {
            console.log(`Sent 200 OK response to ${rinfo.address}:${rinfo.port} containing X-CSREQ: ${hexCrc}`);
        }
    });
});

server.on('listening', () => {
    const address = server.address();
    console.log(`Agilico Connect SIP ALG Verification Server listening on ${address.address}:${address.port}`);
});

server.bind(port);
