const express = require('express');
const multer = require('multer');
const cors = require('cors');
const fs = require('fs');
const path = require('path');
const crypto = require('crypto');
const { v4: uuidv4 } = require('uuid');

const app = express();
const PORT = process.env.PORT || 3000;
const AUTH_TOKEN = process.env.API_TOKEN || 'xyclo-secret-token-2026';

// Middleware
app.use(cors());
app.use(express.json());

// Directories for storage
const UPLOAD_DIR = path.join(__dirname, 'uploads');
const RESPONSE_DIR = path.join(__dirname, 'responses');
if (!fs.existsSync(UPLOAD_DIR)) fs.mkdirSync(UPLOAD_DIR, { recursive: true });
if (!fs.existsSync(RESPONSE_DIR)) fs.mkdirSync(RESPONSE_DIR, { recursive: true });

// In-memory store for downloads
const responseStore = new Map();

// Multer storage configuration
const storage = multer.diskStorage({
  destination: (req, file, cb) => cb(null, UPLOAD_DIR),
  filename: (req, file, cb) => {
    const uniqueSuffix = `${Date.now()}-${uuidv4().substring(0, 8)}`;
    cb(null, `${uniqueSuffix}-${file.originalname}`);
  }
});
const upload = multer({
  storage,
  limits: { fileSize: 50 * 1024 * 1024 } // 50MB limit
});

// Authentication middleware
function authenticate(req, res, next) {
  // If API_TOKEN is empty or 'none', allow all
  if (!AUTH_TOKEN || AUTH_TOKEN === 'none') {
    return next();
  }
  const authHeader = req.headers['authorization'];
  if (!authHeader) {
    return res.status(401).json({ error: 'Missing Authorization header' });
  }
  const parts = authHeader.split(' ');
  if (parts.length !== 2 || parts[0] !== 'Bearer' || parts[1] !== AUTH_TOKEN) {
    return res.status(403).json({ error: 'Invalid or unauthorized Bearer token' });
  }
  next();
}

// Compute SHA-256 hash of a file
function computeSha256(filePath) {
  return new Promise((resolve, reject) => {
    const hash = crypto.createHash('sha256');
    const stream = fs.createReadStream(filePath);
    stream.on('data', data => hash.update(data));
    stream.on('end', () => resolve(hash.digest('hex')));
    stream.on('error', err => reject(err));
  });
}

// Health check endpoints (compatible with Render, load balancers, and clients)
app.get(['/', '/healthz', '/api/health'], (req, res) => {
  res.status(200).json({
    status: 'healthy',
    service: 'XycloTooth Automated Text File Bridge Server',
    timestamp: new Date().toISOString()
  });
});

/**
 * Upload Endpoint:
 * POST /api/upload
 * Headers:
 *   Authorization: Bearer <TOKEN>
 *   X-Request-ID: <UUID> (optional, generated if missing)
 * Body:
 *   multipart/form-data with field "file"
 */
app.post('/api/upload', authenticate, upload.single('file'), async (req, res) => {
  try {
    if (!req.file) {
      return res.status(400).json({ error: 'No file uploaded. Ensure field name is "file".' });
    }

    const requestId = req.headers['x-request-id'] || uuidv4();
    const uploadedPath = req.file.path;
    const originalName = req.file.originalname || 'input.txt';
    const sha256 = await computeSha256(uploadedPath);

    console.log(`[${new Date().toISOString()}] Received file: ${originalName} (size: ${req.file.size} bytes, sha256: ${sha256}, requestId: ${requestId})`);

    // Read the uploaded file contents
    const content = fs.readFileSync(uploadedPath, 'utf8');

    // Generate response content
    const timestamp = new Date().toISOString();
    const responseFileName = originalName.startsWith('input')
      ? originalName.replace('input', 'response')
      : `response_${originalName}`;

    const responseContent = 
`========================================
XYCLOTOOTH BRIDGE RESPONSE
========================================
Request ID:    ${requestId}
Processed At:  ${timestamp}
Source File:   ${originalName}
Source SHA256: ${sha256}
Size (bytes):  ${req.file.size}
----------------------------------------
Original Content Preview:
${content.substring(0, 500)}${content.length > 500 ? '\n...[truncated]' : ''}
----------------------------------------
Server Status: PROCESSED_SUCCESSFULLY
========================================
`;

    // Save response file
    const responseFilePath = path.join(RESPONSE_DIR, `${requestId}_${responseFileName}`);
    fs.writeFileSync(responseFilePath, responseContent, 'utf8');
    const responseSha256 = await computeSha256(responseFilePath);

    // Save to memory store for download-by-ID if needed
    responseStore.set(requestId, {
      path: responseFilePath,
      filename: responseFileName,
      sha256: responseSha256,
      createdAt: Date.now()
    });

    // Check if client prefers JSON with download URL or direct file attachment
    const acceptHeader = req.headers['accept'] || '';
    const wantsJson = req.query.format === 'json' || acceptHeader.includes('application/json');

    if (wantsJson) {
      res.setHeader('X-Request-ID', requestId);
      return res.status(200).json({
        success: true,
        requestId,
        filename: responseFileName,
        sha256: responseSha256,
        size: Buffer.byteLength(responseContent, 'utf8'),
        downloadUrl: `/api/download/${requestId}`
      });
    }

    // Direct text/plain file attachment response
    res.setHeader('Content-Type', 'text/plain; charset=utf-8');
    res.setHeader('Content-Disposition', `attachment; filename="${responseFileName}"`);
    res.setHeader('X-Request-ID', requestId);
    res.setHeader('X-File-SHA256', responseSha256);
    res.status(200).send(responseContent);

    console.log(`[${new Date().toISOString()}] Response sent for requestId: ${requestId} (${responseFileName})`);
  } catch (err) {
    console.error('Error handling upload:', err);
    res.status(500).json({ error: 'Internal server error processing file upload', details: err.message });
  }
});

/**
 * Download Endpoint:
 * GET /api/download/:id
 */
app.get('/api/download/:id', authenticate, (req, res) => {
  const item = responseStore.get(req.params.id);
  if (!item || !fs.existsSync(item.path)) {
    return res.status(404).json({ error: 'Response file not found or expired' });
  }

  res.setHeader('Content-Type', 'text/plain; charset=utf-8');
  res.setHeader('Content-Disposition', `attachment; filename="${item.filename}"`);
  res.setHeader('X-Request-ID', req.params.id);
  res.setHeader('X-File-SHA256', item.sha256);
  res.sendFile(item.path);
});

// Start Server
app.listen(PORT, () => {
  console.log(`=================================================`);
  console.log(` XycloTooth Automated Text File Bridge Server `);
  console.log(` Listening on port: ${PORT}`);
  console.log(` Bearer Token: ${AUTH_TOKEN}`);
  console.log(` Health check: http://localhost:${PORT}/api/health`);
  console.log(` Upload URL:   http://localhost:${PORT}/api/upload`);
  console.log(`=================================================`);
});
