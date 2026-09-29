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

// Automatically load .env if present (for local testing/standalone execution)
const envFile = path.join(__dirname, '.env');
if (fs.existsSync(envFile)) {
  try {
    const raw = fs.readFileSync(envFile, 'utf8');
    raw.split(/\r?\n/).forEach(line => {
      const match = line.trim().match(/^([^#=]+)=(.*)$/);
      if (match) {
        const key = match[1].trim();
        const value = match[2].trim().replace(/^["']|["']$/g, '');
        if (!process.env[key]) {
          process.env[key] = value;
        }
      }
    });
  } catch (e) {
    console.warn('Could not parse .env file:', e.message);
  }
}

// Configuration
const OPENAI_API_KEY = process.env.OPENAI_API_KEY;
const OPENAI_MODEL = process.env.OPENAI_MODEL || 'gpt-5.6-sol';
const GROQ_API_KEY = process.env.GROQ_API_KEY;
const GROQ_MODEL = process.env.GROQ_MODEL || 'openai/gpt-oss-120b';

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

// Call OpenAI LLM
async function callOpenAILlm(userPrompt, filename) {
  const apiKey = process.env.OPENAI_API_KEY || OPENAI_API_KEY;
  const model = process.env.OPENAI_MODEL || OPENAI_MODEL;

  if (!apiKey) {
    throw new Error('OPENAI_API_KEY is not configured');
  }

  console.log(`[${new Date().toISOString()}] [LLM] Sending prompt (${userPrompt.length} chars) from "${filename}" to OpenAI (${model})...`);

  const response = await fetch('https://api.openai.com/v1/chat/completions', {
    method: 'POST',
    headers: {
      'Authorization': `Bearer ${apiKey}`,
      'Content-Type': 'application/json'
    },
    body: JSON.stringify({
      model: model,
      messages: [
        {
          role: 'system',
          content: process.env.LLM_SYSTEM_PROMPT || 
            'You are an intelligent, helpful, and highly accurate AI assistant. The user has sent text via the XycloTooth Automated Pipeline. Carefully analyze the user prompt/document and provide a direct, comprehensive, and well-structured answer.'
        },
        {
          role: 'user',
          content: userPrompt
        }
      ]
    })
  });

  const data = await response.json();
  if (!response.ok || data.error) {
    const errorMsg = data.error ? data.error.message : `HTTP ${response.status}: ${response.statusText}`;
    throw new Error(`OpenAI API error: ${errorMsg}`);
  }

  if (!data.choices || data.choices.length === 0 || !data.choices[0].message) {
    throw new Error('OpenAI returned empty completion choices');
  }

  const answer = data.choices[0].message.content;
  console.log(`[${new Date().toISOString()}] [LLM] Successfully received answer (${answer.length} chars) from ${model}`);
  return {
    answer,
    model: data.model || model,
    usage: data.usage
  };
}

// Generate LLM answer with fallback
async function generateLlmAnswer(content, filename) {
  // 1. Primary: OpenAI (gpt-5.6-sol)
  try {
    return await callOpenAILlm(content, filename);
  } catch (openaiErr) {
    console.error(`[LLM] OpenAI call failed: ${openaiErr.message}`);

    // 2. Fallback: Groq if configured
    const groqKey = process.env.GROQ_API_KEY || GROQ_API_KEY;
    if (groqKey) {
      try {
        console.log(`[LLM] Attempting fallback to Groq (${GROQ_MODEL})...`);
        const groqRes = await fetch('https://api.groq.com/openai/v1/chat/completions', {
          method: 'POST',
          headers: {
            'Authorization': `Bearer ${groqKey}`,
            'Content-Type': 'application/json'
          },
          body: JSON.stringify({
            model: process.env.GROQ_MODEL || GROQ_MODEL,
            messages: [
              {
                role: 'system',
                content: 'You are an intelligent AI assistant. Provide a direct, comprehensive, and accurate answer to the user prompt.'
              },
              { role: 'user', content: content }
            ]
          })
        });
        const groqData = await groqRes.json();
        if (groqRes.ok && groqData.choices && groqData.choices[0]?.message?.content) {
          console.log(`[LLM] Groq fallback answered successfully (${groqData.choices[0].message.content.length} chars)`);
          return {
            answer: groqData.choices[0].message.content,
            model: groqData.model || GROQ_MODEL,
            usage: groqData.usage
          };
        }
      } catch (groqErr) {
        console.error(`[LLM] Groq fallback error: ${groqErr.message}`);
      }
    }

    throw openaiErr;
  }
}

// Health check endpoints (compatible with Render, load balancers, and clients)
app.get(['/', '/healthz', '/api/health'], (req, res) => {
  res.status(200).json({
    status: 'healthy',
    service: 'XycloTooth Automated Text File Bridge Server',
    llm: {
      provider: 'OpenAI',
      model: process.env.OPENAI_MODEL || OPENAI_MODEL,
      configured: Boolean(process.env.OPENAI_API_KEY || OPENAI_API_KEY)
    },
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

    // Generate response filename
    const responseFileName = originalName.startsWith('input')
      ? originalName.replace('input', 'response')
      : `response_${originalName}`;

    // Call LLM with user content
    let responseContent;
    let modelUsed = process.env.OPENAI_MODEL || OPENAI_MODEL;

    try {
      const llmResult = await generateLlmAnswer(content, originalName);
      responseContent = llmResult.answer;
      modelUsed = llmResult.model;
    } catch (llmErr) {
      console.error(`[LLM] Error processing file "${originalName}":`, llmErr);
      responseContent = `[XycloTooth AI Error]\nFailed to generate AI response for "${originalName}".\nReason: ${llmErr.message}\nTimestamp: ${new Date().toISOString()}\n`;
    }

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
      res.setHeader('X-AI-Model', modelUsed);
      return res.status(200).json({
        success: true,
        requestId,
        filename: responseFileName,
        sha256: responseSha256,
        model: modelUsed,
        size: Buffer.byteLength(responseContent, 'utf8'),
        downloadUrl: `/api/download/${requestId}`,
        answer: responseContent
      });
    }

    // Direct text/plain file attachment response
    res.setHeader('Content-Type', 'text/plain; charset=utf-8');
    res.setHeader('Content-Disposition', `attachment; filename="${responseFileName}"`);
    res.setHeader('X-Request-ID', requestId);
    res.setHeader('X-File-SHA256', responseSha256);
    res.setHeader('X-AI-Model', modelUsed);
    res.status(200).send(responseContent);

    console.log(`[${new Date().toISOString()}] Response sent for requestId: ${requestId} (${responseFileName}) using ${modelUsed}`);
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
