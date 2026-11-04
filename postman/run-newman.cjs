const fs = require('node:fs');
const path = require('node:path');
const newman = require('newman');
const collection = require('./CoreBanking.postman_collection.json');
const environment = require('./Local.postman_environment.json');
const origin = process.env.NEWMAN_ORIGIN || 'http://localhost:5000';
const responses = [];
const escape = value => String(value).replace(/[&<>"']/g, symbol => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[symbol]));
function sanitize(text) {
  try {
    const body = JSON.parse(text);
    if (body.access_token) body.access_token = '[REDACTED TOKEN]';
    return JSON.stringify(body, null, 2);
  } catch { return text; }
}
function removeManual(entries) {
  return entries.filter(entry => !entry.name.startsWith('Rate limit')).map(entry => entry.item ? { ...entry, item: removeManual(entry.item) } : entry);
}
collection.item = removeManual(collection.item);
environment.values.find(variable => variable.key === 'origin').value = origin;
if (process.env.NEWMAN_CLIENT_SECRET) environment.values.find(variable => variable.key === 'clientSecret').value = process.env.NEWMAN_CLIENT_SECRET;
environment.values.find(variable => variable.key === 'replayKey').value = require('node:crypto').randomUUID();
const run = newman.run({ collection, environment, reporters: process.env.NEWMAN_QUIET === '1' ? [] : ['cli'], timeoutRequest: 15000 }, (error, summary) => {
  const executionErrors = summary?.run.failures.map(failure => ({ source: failure.source?.name, message: failure.error.message })) || [];
  const differences = responses.filter(row => row.actual !== row.expected || row.expectedErrorCode && row.errorCode !== row.expectedErrorCode);
  const statuses = responses.reduce((result, row) => { result[row.actual] = (result[row.actual] || 0) + 1; return result; }, {});
  const report = { tool: 'Newman 6.2.1', generatedAt: new Date().toISOString(), origin, requests: responses.length,
    assertions: 0, differences: differences.length, executionErrors, statuses,
    excluded: ['Manual 429 request requires exhausting the configured rate limiter; not part of this happy/unhappy/input/scopes run.'], responses };
  const directory = process.env.NEWMAN_REPORT_DIR ? path.resolve(process.env.NEWMAN_REPORT_DIR) : path.join(__dirname, 'reports');
  fs.mkdirSync(directory, { recursive: true });
  fs.writeFileSync(path.join(directory, 'newman-responses.json'), JSON.stringify(report, null, 2) + '\n');
  fs.writeFileSync(path.join(directory, 'newman-responses.html'), `<!doctype html><html lang="es"><meta charset="utf-8"><meta name="viewport" content="width=device-width, initial-scale=1"><title>Newman - Core Banking</title><style>body{font:15px Consolas,monospace;margin:24px;background:#f5f7f8;color:#172023}h1{font-size:24px}details{padding:12px 0;border-bottom:1px solid #c3cbd0}summary{cursor:pointer;overflow-wrap:anywhere}pre{white-space:pre-wrap;overflow-wrap:anywhere;background:white;padding:12px;border:1px solid #ced8dd}.ok{color:#176839}.bad{color:#a52222}</style><h1>Newman - Core Banking</h1><p>${responses.length} solicitudes | ${differences.length} diferencias | ${executionErrors.length} errores de ejecucion | 0 assertions</p><p>32 combinaciones de scopes y seis operaciones. Tokens y secretos ocultos. Caso manual 429 excluido.</p>${responses.map(row => `<details><summary class="${row.actual === row.expected ? 'ok' : 'bad'}">HTTP ${row.actual} / esperado ${row.expected} | ${escape(row.name)}</summary><p>${escape(row.method)} ${escape(row.url)} | scopes: ${escape(row.scopes)}</p><h3>Entrada</h3><pre>${escape(row.input)}</pre><h3>Respuesta</h3><pre>${escape(row.body)}</pre></details>`).join('')}</html>`);
  console.log(JSON.stringify({ tool: report.tool, requests: responses.length, differences: differences.length, executionErrors: executionErrors.length, statuses }));
  for (const row of differences) console.log(JSON.stringify({ name: row.name, expected: row.expected, actual: row.actual, body: row.body }));
  if (error || executionErrors.length || differences.length) process.exitCode = 1;
});
run.on('request', (error, args) => {
  const entry = args.item.toJSON();
  const expected = entry.response?.[0]?.code;
  let expectedErrorCode;
  try { expectedErrorCode = JSON.parse(entry.response?.[0]?.body || '{}').errorCode; } catch { }
  const body = sanitize(args.response?.stream?.toString() || error?.message || '');
  let errorCode;
  try { errorCode = JSON.parse(body).errorCode; } catch { }
  let scopes = '(sin token)';
  const bearer = args.request.headers.get('Authorization');
  if (bearer?.startsWith('Bearer ')) {
    try { scopes = JSON.parse(Buffer.from(bearer.slice(7).split('.')[1], 'base64url').toString()).scope || '(sin scopes)'; } catch { scopes = '(token invalido)'; }
  }
  const requestBody = args.request.body;
  const input = requestBody?.mode === 'urlencoded' ? JSON.stringify(requestBody.urlencoded.toJSON().map(field => ({ key: field.key, value: field.key === 'client_secret' ? '[REDACTED]' : field.value })), null, 2) : requestBody?.raw || '(sin body)';
  responses.push({ name: args.item.name, method: args.request.method, url: args.request.url.toString(), scopes,
    expected, actual: args.response?.code || 0, expectedErrorCode, errorCode, input, body,
    durationMs: args.response?.responseTime, correlationId: args.response?.headers.get('Correlation-Id') });
});