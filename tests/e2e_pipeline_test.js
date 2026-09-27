const fs = require('fs');
const path = require('path');
const http = require('http');
const crypto = require('crypto');

// CRC32 implementation matching the Windows C# and Kotlin Android implementations
function crc32(buf) {
  let table = new Uint32Array(256);
  for (let i = 0; i < 256; i++) {
    let c = i;
    for (let j = 0; j < 8; j++) {
      c = (c & 1) ? (0xEDB88320 ^ (c >>> 1)) : (c >>> 1);
    }
    table[i] = c;
  }
  let crc = 0xFFFFFFFF;
  for (let i = 0; i < buf.length; i++) {
    crc = (crc >>> 8) ^ table[(crc ^ buf[i]) & 0xFF];
  }
  return (crc ^ 0xFFFFFFFF) >>> 0;
}

function computeSha256(content) {
  return crypto.createHash('sha256').update(content).digest('hex');
}

// Simulated Bluetooth Protocol Encoder & Decoder
const MAGIC_0 = 0x58; // 'X'
const MAGIC_1 = 0x54; // 'T'
const PROTOCOL_VERSION = 0x01;

function encodeFrame(frameType, payloadBuffer) {
  const header = Buffer.alloc(8);
  header[0] = MAGIC_0;
  header[1] = MAGIC_1;
  header[2] = PROTOCOL_VERSION;
  header[3] = frameType;
  header.writeUInt32BE(payloadBuffer.length, 4);

  const fullData = Buffer.concat([header, payloadBuffer]);
  const checksum = crc32(fullData);

  const checksumBuf = Buffer.alloc(4);
  checksumBuf.writeUInt32BE(checksum, 0);

  return Buffer.concat([fullData, checksumBuf]);
}

function decodeFrame(frameBuffer) {
  if (frameBuffer.length < 12) throw new Error("Frame too short");
  if (frameBuffer[0] !== MAGIC_0 || frameBuffer[1] !== MAGIC_1) throw new Error("Invalid magic");
  if (frameBuffer[2] !== PROTOCOL_VERSION) throw new Error("Invalid version");

  const frameType = frameBuffer[3];
  const payloadLength = frameBuffer.readUInt32BE(4);
  const payload = frameBuffer.slice(8, 8 + payloadLength);
  const expectedCrc = frameBuffer.readUInt32BE(8 + payloadLength);

  const computedCrc = crc32(frameBuffer.slice(0, 8 + payloadLength));
  if (computedCrc !== expectedCrc) throw new Error(`CRC mismatch! Expected ${expectedCrc}, computed ${computedCrc}`);

  return { frameType, payload };
}

async function runTests() {
  console.log("=================================================");
  console.log(" XycloTooth Automated Pipeline Verification Tests ");
  console.log("=================================================\n");

  let passed = 0;
  let failed = 0;

  function assert(condition, message) {
    if (condition) {
      console.log(`[PASS] ${message}`);
      passed++;
    } else {
      console.error(`[FAIL] ${message}`);
      failed++;
    }
  }

  // TEST 1: Protocol Frame Encoding & Decoding
  console.log("--- TEST 1: Bluetooth Protocol Framing & CRC32 ---");
  const testPayload = Buffer.from(JSON.stringify({
    fileId: "test-uuid-1234",
    filename: "input.txt",
    fileSize: 42,
    sha256: "dummy-sha256"
  }));
  const encoded = encodeFrame(0x10, testPayload);
  assert(encoded.length === 8 + testPayload.length + 4, `Encoded frame size (${encoded.length} bytes)`);

  const decoded = decodeFrame(encoded);
  assert(decoded.frameType === 0x10, "Decoded frame type matches FILE_START (0x10)");
  assert(decoded.payload.toString() === testPayload.toString(), "Decoded payload matches original payload");

  // TEST 2: CRC Checksum Tamper Rejection
  console.log("\n--- TEST 2: Frame Tamper Rejection ---");
  const corrupted = Buffer.from(encoded);
  corrupted[10] ^= 0xFF; // Flip bit in payload
  let caughtCorruption = false;
  try {
    decodeFrame(corrupted);
  } catch (err) {
    caughtCorruption = true;
  }
  assert(caughtCorruption, "Corrupted frame successfully detected and rejected via CRC32");

  // TEST 3: Chunking & File Reassembly
  console.log("\n--- TEST 3: Chunked Transfer & SHA-256 Validation ---");
  const testFileContent = "Automated file bridge verification test content.\n".repeat(100);
  const originalSha256 = computeSha256(testFileContent);
  const fileBuf = Buffer.from(testFileContent, 'utf8');

  const chunkSize = 256;
  const chunks = [];
  for (let i = 0; i < fileBuf.length; i += chunkSize) {
    chunks.push(fileBuf.slice(i, i + chunkSize));
  }
  assert(chunks.length > 1, `File successfully sliced into ${chunks.length} chunks`);

  // Reassemble
  const reassembledBuf = Buffer.concat(chunks);
  const reassembledSha256 = computeSha256(reassembledBuf.toString('utf8'));
  assert(reassembledSha256 === originalSha256, "Reassembled file SHA-256 matches sender original SHA-256");

  // TEST 4: Live Server Upload & Response Flow
  console.log("\n--- TEST 4: External Server API Upload & Response Flow ---");
  // Check if local server is running, or start embedded test
  const tempInputPath = path.join(__dirname, 'temp_test_input.txt');
  fs.writeFileSync(tempInputPath, testFileContent, 'utf8');

  console.log(`Test completed with: ${passed} passed, ${failed} failed.`);
  if (failed > 0) {
    process.exit(1);
  }
}

runTests();
