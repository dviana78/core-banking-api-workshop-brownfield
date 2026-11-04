const fs = require('node:fs');
const path = require('node:path');
const { STATUS_CODES } = require('node:http');

const scopes = ['accounts.read', 'balances.read', 'accounts.write', 'accounts.close', 'balances.adjust'];
const tokens = ['tokenAccountsRead', 'tokenBalancesRead', 'tokenAccountsWrite', 'tokenAccountsClose', 'tokenBalancesAdjust'];
const types = ['CURRENT_ACCOUNT', 'REGULAR_SAVINGS', 'FIXED_DEPOSIT', 'SAVINGS_PLAN', 'INVESTOR_ACCOUNT'];
const prefix = '/open-finance/system/accounts/v1';
const reference = { accountId: 'acc-50001', accountType: 'CURRENT_ACCOUNT', currency: 'EUR', accountState: 'ACTIVE' };
const balance = { accountId: 'acc-50001', asOf: '2026-11-01T00:00:00Z', currency: 'EUR', availableBalance: 5400.25, bookBalance: 5650.25 };
const createBody = { name: 'Postman account', accountType: 'CURRENT_ACCOUNT', currency: 'EUR' };
const adjustmentBody = { amount: 1, currency: 'EUR', reason: 'Postman workshop adjustment' };
const auth = token => token ? { type: 'bearer', bearer: [{ key: 'token', value: `{{${token}}}`, type: 'string' }] } : { type: 'noauth' };
const problem = status => ({ type: 'about:blank', title: STATUS_CODES[status], status, correlationId: '{{correlationId}}' });
function item(name, method, route, token, body, status = 200, response, extra = {}) {
  const header = [{ key: 'Correlation-Id', value: '{{correlationId}}' }];
  const request = { method, auth: auth(token), header, url: `{{origin}}${route}`, description: `Expected HTTP ${status}. ${extra.description || 'Use the seeded workshop data and the indicated scope.'}` };
  if (body !== undefined) {
    header.push({ key: 'Content-Type', value: 'application/json' });
    request.body = { mode: 'raw', raw: JSON.stringify(body, null, 2), options: { raw: { language: 'json' } } };
  }
  if (method === 'POST' && body === undefined && route.endsWith('/accounts')) {
    header.push({ key: 'Content-Type', value: 'application/json' });
    request.body = { mode: 'raw', raw: '', options: { raw: { language: 'json' } } };
  }
  if (route.endsWith('/balance-adjustments')) header.push({ key: 'Idempotency-Key', value: extra.key || '{{$guid}}' });
  if (extra.scenario) header.push({ key: 'X-Workshop-Scenario', value: extra.scenario });
  if (extra.key === '') header.splice(header.findIndex(value => value.key === 'Idempotency-Key'), 1);
  const result = { name, request, response: [{ name: `HTTP ${status}`, originalRequest: request, status: STATUS_CODES[status], code: status,
    header: [{ key: 'Content-Type', value: status >= 400 ? 'application/problem+json' : 'application/json' }, { key: 'Correlation-Id', value: '{{correlationId}}' }],
    body: status === 202 ? '' : JSON.stringify(response === undefined ? status >= 400 ? problem(status) : reference : response, null, 2) }] };
  if (extra.capture) result.event = [{ listen: 'test', script: { type: 'text/javascript', exec: [`if (pm.response.code === ${status}) pm.environment.set('${extra.capture}', pm.response.json().accountId);`] } }];
  return result;
}
const folders = [];
const authentication = { name: '00 - Workshop mock tokens', item: [] };
function tokenRequest(name, scope, variable, client = 'workshop-client', scenario = '', expected = 200, secret = '{{clientSecret}}', grant = 'client_credentials') {
  const form = [{ key: 'grant_type', value: grant }, { key: 'client_id', value: client }, { key: 'client_secret', value: secret }, { key: 'scope', value: scope }];
  if (scenario) form.push({ key: 'scenario', value: scenario });
  const request = { method: 'POST', auth: { type: 'noauth' }, header: [], url: '{{origin}}/oauth/token', body: { mode: 'urlencoded', urlencoded: form }, description: `Expected HTTP ${expected}. Local signed mock JWT only; not a Mambu token.` };
  const result = { name, request, response: [{ name: `HTTP ${expected}`, originalRequest: request, status: STATUS_CODES[expected], code: expected, header: [{ key: 'Content-Type', value: 'application/json' }],
    body: JSON.stringify(expected === 200 ? { access_token: '{{generatedToken}}', token_type: 'Bearer', expires_in: scenario ? 0 : 3600, scope } : { error: grant !== 'client_credentials' ? 'unsupported_grant_type' : expected === 401 ? 'invalid_client' : 'invalid_scope' }, null, 2) }] };
  if (variable) result.event = [{ listen: 'test', script: { type: 'text/javascript', exec: [`if (pm.response.code === 200) pm.environment.set('${variable}', pm.response.json().access_token);`] } }];
  authentication.item.push(result);
}
scopes.forEach((scope, index) => tokenRequest(`Token - ${scope}`, scope, tokens[index]));
tokenRequest('Token - all scopes', scopes.join(' '), 'tokenAll');
tokenRequest('Token - no scopes', '', 'tokenNoScopes');
scopes.forEach((scope, index) => tokenRequest(`Foreign consumer - ${scope}`, scope, `tokenOther${index}`, 'workshop-other'));
tokenRequest('Token - expired', scopes.join(' '), 'tokenExpired', 'workshop-client', 'expired');
tokenRequest('Invalid client credentials - 401', scopes[0], null, 'workshop-client', '', 401, 'incorrect');
tokenRequest('Unknown scope - 400', 'accounts.delete', null, 'workshop-client', '', 400);
tokenRequest('Unsupported grant - 400', scopes[0], null, 'workshop-client', '', 400, '{{clientSecret}}', 'password');
for (let mask = 0; mask < 32; mask++) {
  const selected = scopes.filter((scope, index) => mask & (1 << index));
  tokenRequest(`Combination ${mask} - ${selected.join(' ') || 'no scopes'}`, selected.join(' '), `tokenCombination${mask}`);
}
folders.push(authentication);

const operations = [
  { name: 'List accounts', method: 'GET', route: `${prefix}/customers/cus-100045/accounts`, scope: 0, response: [reference] },
  { name: 'Get account', method: 'GET', route: `${prefix}/accounts/acc-50001`, scope: 0, response: reference },
  { name: 'Get balances', method: 'GET', route: `${prefix}/accounts/acc-50001/balances`, scope: 1, response: balance },
  { name: 'Create account', method: 'POST', route: `${prefix}/customers/cus-100045/accounts`, scope: 2, body: createBody },
  { name: 'Close account', method: 'POST', route: `${prefix}/accounts/acc-50003/closure`, scope: 3 },
  { name: 'Adjust balance', method: 'POST', route: `${prefix}/accounts/acc-50009/balance-adjustments`, scope: 4, body: adjustmentBody }
];
const matrix = { name: '01 - Scope matrix - 30 combinations', item: [] };
scopes.forEach((scope, index) => {
  const folder = { name: scope, auth: auth(tokens[index]), item: [] };
  operations.forEach(operation => {
    const allowed = operation.scope === index;
    const write = operation.method === 'POST';
    folder.item.push(item(`${operation.name} - ${allowed ? 'permitted' : '403 forbidden'}`, operation.method, operation.route, tokens[index], operation.body,
      allowed ? write ? 409 : 200 : 403, allowed && !write ? operation.response : undefined,
      { scenario: allowed && write ? 'upstream-conflict' : undefined, description: 'Valid resource/body. Permitted writes simulate upstream 409 to avoid changing fixtures; unrelated scopes must return 403 first.' }));
  });
  matrix.item.push(folder);
});
folders.push(matrix);
const combinations = { name: '01b - All 32 scope combinations - 192 flows', item: [] };
for (let mask = 0; mask < 32; mask++) {
  const selected = scopes.filter((scope, index) => mask & (1 << index));
  const folder = { name: `${mask} - ${selected.join(' ') || 'no scopes'}`, item: [] };
  for (const operation of operations) {
    const allowed = Boolean(mask & (1 << operation.scope));
    const write = operation.method === 'POST';
    folder.item.push(item(`${operation.name} - combination ${mask}`, operation.method, operation.route,
      `tokenCombination${mask}`, operation.body, allowed ? write ? 409 : 200 : 403,
      allowed && !write ? operation.response : undefined,
      { scenario: allowed && write ? 'upstream-conflict' : undefined, description: 'All possible scope subsets. Authorized writes simulate conflict before mutation; missing required scope returns 403.' }));
  }
  combinations.item.push(folder);
}
folders.push(combinations);
const security = { name: '02 - Authentication and resource visibility', item: [] };
operations.forEach(operation => {
  for (const [label, token, status] of [['Missing token', null, 401], ['Invalid token', 'tokenInvalid', 401], ['Expired token', 'tokenExpired', 401], ['No scopes', 'tokenNoScopes', 403], ['Foreign consumer', `tokenOther${operation.scope}`, 404]])
    security.item.push(item(`${operation.name} - ${label}`, operation.method, operation.route, token, operation.body, status));
  security.item.push(item(`${operation.name} - not found`, operation.method, operation.route.replace('cus-100045', 'cus-unknown').replace(/acc-\d+/, 'acc-unknown'), tokens[operation.scope], operation.body, 404));
  security.item.push(item(`${operation.name} - invalid identifier`, operation.method, operation.route.replace('cus-100045', 'cus-invalid!').replace(/acc-\d+/, 'acc-invalid!'), tokens[operation.scope], operation.body, 400));
});
folders.push(security);
const reads = { name: '03 - accounts.read', auth: auth(tokens[0]), item: [
  item('List authorized accounts', 'GET', operations[0].route, tokens[0], undefined, 200, [reference]),
  item('Empty collection - mock scenario', 'GET', operations[0].route, tokens[0], undefined, 200, [], { scenario: 'empty-list' }),
  item('Get account', 'GET', operations[1].route, tokens[0], undefined, 200, reference),
  item('Incomplete account list - 502 no partial list', 'GET', operations[0].route, tokens[0], undefined, 502, undefined, { scenario: 'incomplete-reference' }),
  item('Incomplete account detail - 502', 'GET', operations[1].route, tokens[0], undefined, 502, undefined, { scenario: 'incomplete-reference' })
] };
folders.push(reads);
const balances = { name: '04 - balances.read - includes business errors HTTP 200', auth: auth(tokens[1]), item: [] };
balances.item.push(item('Available and book balances with holds', 'GET', operations[2].route, tokens[1], undefined, 200, balance));
for (const [id, available, book, label] of [['acc-50002', 0, 0, 'Closed account recorded zero'], ['acc-50003', 0, 0, 'Active zero'], ['acc-50005', -100, -100, 'Negative balances with overdraft'], ['acc-50009', 1000, 1000, 'Positive equal balances'], ['acc-50010', 50, 100, 'Pending holds']])
  balances.item.push(item(label, 'GET', `${prefix}/accounts/${id}/balances`, tokens[1], undefined, 200, { ...balance, accountId: id, availableBalance: available, bookBalance: book }));
const businessErrors = [
  ['acc-50677', 677, 'Balance is not posible'],
  ['acc-50678', 678, 'Balance inquiry is not allowed for a locked account'],
  ['acc-50679', 679, 'Balance is unavailable while the account is pending approval'],
  ['acc-50680', 680, 'Required balance information is missing'],
  ['acc-50006', 680, 'Required balance information is missing'],
  ['acc-50007', 680, 'Required balance information is missing'],
  ['acc-50008', 680, 'Required balance information is missing']
];
businessErrors.forEach(([id, code, message]) => balances.item.push(item(`HTTP 200 - business error ${code} - ${id}`, 'GET', `${prefix}/accounts/${id}/balances`, tokens[1], undefined, 200, { errorCode: code, message })));
folders.push(balances);

const faults = { name: '05 - Upstream and infrastructure scenarios per scope', item: [] };
operations.forEach(operation => {
  for (const [scenario, status] of [['internal-error', 500], ['upstream-invalid', 502], ['upstream-unavailable', 503], ['upstream-timeout', 504], ['corrupt-json', 502]])
    faults.item.push(item(`${operation.name} - ${scenario}`, operation.method, operation.route, tokens[operation.scope], operation.body, status, undefined, { scenario }));
});
faults.item.push(item('Unknown mock scenario - 400', 'GET', operations[2].route, tokens[1], undefined, 400, undefined, { scenario: 'not-defined' }));
faults.item.push(item('Rate limit - repeat until HTTP 429', 'GET', operations[2].route, tokens[1], undefined, 429, undefined,
  { description: 'Manual repeated request after configured per-minute permit limit (default 500). On a fresh window this returns 200. Do not run as part of the ordinary sequence.' }));
folders.push(faults);

const creations = { name: '06 - accounts.write', auth: auth(tokens[2]), item: [] };
types.forEach((type, index) => creations.item.push(item(`Create ${type} - 201`, 'POST', operations[3].route, tokens[2], { ...createBody, accountType: type }, 201,
  { accountId: '{{newAccountId}}', accountType: type, currency: 'EUR', accountState: 'PENDING_APPROVAL' }, { capture: index === 0 ? 'newAccountId' : `newAccountId${index}` })));
for (const [name, body] of [['Missing fields', {}], ['Empty name', { ...createBody, name: '' }], ['Invalid type', { ...createBody, accountType: 'NOT_A_PRODUCT' }], ['Unsupported currency', { ...createBody, currency: 'USD' }]])
  creations.item.push(item(`${name} - 400`, 'POST', operations[3].route, tokens[2], body, 400));
creations.item.push(item('Missing request body - 400', 'POST', operations[3].route, tokens[2], undefined, 400));
creations.item.push(item('Unavailable product mapping - 503', 'POST', operations[3].route, tokens[2], createBody, 503, undefined, { scenario: 'mapping-unavailable' }));
creations.item.push(item('Upstream conflict - 409', 'POST', operations[3].route, tokens[2], createBody, 409, undefined, { scenario: 'upstream-conflict' }));
folders.push(creations);
const adjustments = { name: '07 - balances.adjust', auth: auth(tokens[4]), item: [] };
const adjustmentResponse = amount => ({ adjustmentId: '{{generatedAdjustmentId}}', accountId: 'acc-50009', amount, currency: 'EUR', reason: adjustmentBody.reason, recordedAt: '{{recordedAt}}' });
for (const [name, amount] of [['Credit', 100], ['Debit', -25], ['Compensating adjustment with NEW key', 25]])
  adjustments.item.push(item(`${name} - 201`, 'POST', operations[5].route, tokens[4], { ...adjustmentBody, amount }, 201, adjustmentResponse(amount)));
adjustments.item.push(item('First idempotent posting - 201', 'POST', operations[5].route, tokens[4], adjustmentBody, 201, adjustmentResponse(1), { key: '{{replayKey}}' }));
adjustments.item.push(item('Identical replay - original 201', 'POST', operations[5].route, tokens[4], adjustmentBody, 201, adjustmentResponse(1), { key: '{{replayKey}}' }));
adjustments.item.push(item('Same key different amount - 409', 'POST', operations[5].route, tokens[4], { ...adjustmentBody, amount: 2 }, 409, undefined, { key: '{{replayKey}}' }));
adjustments.item.push(item('Same key different account - 409', 'POST', operations[5].route.replace('acc-50009', 'acc-50004'), tokens[4], adjustmentBody, 409, undefined, { key: '{{replayKey}}' }));
adjustments.item.push(item('Seeded processing duplicate - 202', 'POST', operations[5].route, tokens[4], { amount: 1, currency: 'EUR', reason: 'Pending workshop adjustment' }, 202, undefined, { key: '11111111-1111-4111-8111-111111111111' }));
for (const [name, body] of [['Zero amount', { ...adjustmentBody, amount: 0 }], ['Excess decimal precision', { ...adjustmentBody, amount: 0.001 }], ['Currency mismatch', { ...adjustmentBody, currency: 'USD' }], ['Empty reason', { ...adjustmentBody, reason: '' }], ['Missing fields', {}]])
  adjustments.item.push(item(`${name} - 400`, 'POST', operations[5].route, tokens[4], body, 400));
adjustments.item.push(item('Missing idempotency header - 400', 'POST', operations[5].route, tokens[4], adjustmentBody, 400, undefined, { key: '' }));
adjustments.item.push(item('Invalid UUID - 400', 'POST', operations[5].route, tokens[4], adjustmentBody, 400, undefined, { key: 'invalid' }));
adjustments.item.push(item('UUID v1 instead of v4 - 400', 'POST', operations[5].route, tokens[4], adjustmentBody, 400, undefined, { key: '11111111-1111-1111-8111-111111111111' }));
adjustments.item.push(item('Insufficient funds - 409 atomic rejection', 'POST', operations[5].route.replace('acc-50009', 'acc-50004'), tokens[4], { ...adjustmentBody, amount: -11 }, 409));
adjustments.item.push(item('Configured overdraft respected - 201', 'POST', operations[5].route.replace('acc-50009', 'acc-50005'), tokens[4], { ...adjustmentBody, amount: -10 }, 201,
  { ...adjustmentResponse(-10), accountId: 'acc-50005' }));
adjustments.item.push(item('Configured overdraft exceeded - 409', 'POST', operations[5].route.replace('acc-50009', 'acc-50005'), tokens[4], { ...adjustmentBody, amount: -1000 }, 409));
adjustments.item.push(item('Closed account - 409', 'POST', operations[5].route.replace('acc-50009', 'acc-50002'), tokens[4], adjustmentBody, 409));
adjustments.item.push(item('Incomplete account balances - 502', 'POST', operations[5].route.replace('acc-50009', 'acc-50007'), tokens[4], adjustmentBody, 502));
folders.push(adjustments);

const closures = { name: '08 - accounts.close - run last', auth: auth(tokens[3]), item: [] };
const representatives = { PENDING_APPROVAL: 'acc-50012', APPROVED: 'acc-50011', ACTIVE: 'acc-50003', ACTIVE_IN_ARREARS: 'acc-51096', MATURED: 'acc-51460', LOCKED: 'acc-50678', DORMANT: 'acc-52187', CLOSED: 'acc-50002', CLOSED_WRITTEN_OFF: 'acc-52912', WITHDRAWN: 'acc-53275', CLOSED_REJECTED: 'acc-53638' };
Object.entries(representatives).forEach(([state, id]) => {
  const allowed = ['ACTIVE', 'APPROVED', 'CLOSED'].includes(state);
  const number = Number(id.slice(4)) - 50001;
  closures.item.push(item(`Close from ${state} - ${allowed ? 200 : 409}`, 'POST', `${prefix}/accounts/${id}/closure`, tokens[3], undefined, allowed ? 200 : 409,
    allowed ? { accountId: id, accountType: types[number % 5], currency: 'EUR', accountState: 'CLOSED' } : undefined));
});
closures.item.push(item('Repeat closure - 200', 'POST', `${prefix}/accounts/acc-50003/closure`, tokens[3], undefined, 200, { accountId: 'acc-50003', accountType: 'FIXED_DEPOSIT', currency: 'EUR', accountState: 'CLOSED' }));
closures.item.push(item('Nonzero balance and holds - 409', 'POST', `${prefix}/accounts/acc-50001/closure`, tokens[3], undefined, 409));
closures.item.push(item('Created pending account cannot close - 409', 'POST', `${prefix}/accounts/{{newAccountId}}/closure`, tokens[3], undefined, 409));
folders.push(closures);

const collection = { info: { name: 'Core Banking Workshop - all scope flows', schema: 'https://schema.getpostman.com/json/collection/v2.1.0/collection.json', description: 'Run folders in order against a freshly migrated database. No tests/assertions. Post-response scripts only capture tokens and IDs. Special balance errors 677-680 return HTTP 200. Matrix writes simulate conflict to avoid mutation. Rate-limit request is manual only.' }, item: folders };
const environment = { name: 'Core Banking - Local workshop', values: Object.entries({ origin: 'http://localhost:5000', clientSecret: 'workshop-only', correlationId: 'postman-workshop-correlation', replayKey: '22222222-2222-4222-8222-222222222222', tokenInvalid: 'not-a-jwt', newAccountId: '', ...Object.fromEntries([...tokens, 'tokenAll', 'tokenNoScopes', 'tokenExpired', ...scopes.map((scope, index) => `tokenOther${index}`)].map(token => [token, ''])) }).map(([key, value]) => ({ key, value, type: 'default', enabled: true })), _postman_variable_scope: 'environment' };
fs.writeFileSync(path.join(__dirname, 'CoreBanking.postman_collection.json'), JSON.stringify(collection, null, 2) + '\n');
fs.writeFileSync(path.join(__dirname, 'Local.postman_environment.json'), JSON.stringify(environment, null, 2) + '\n');
console.log(`Generated ${folders.reduce((count, folder) => count + folder.item.reduce((total, entry) => total + (entry.item ? entry.item.length : 1), 0), 0)} requests; matrix contains 30 combinations; no assertions.`);