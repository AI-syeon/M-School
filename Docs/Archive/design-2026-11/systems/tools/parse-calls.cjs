// Shared parser for function-call columns (see full-design/11-function-syntax.md).
// Turns "name arg key=value / label: !name \"text\"" into typed call objects. No eval.
'use strict';

class CallError extends Error {
  constructor(code, offset, message, extra = {}) {
    super(message);
    Object.assign(this, { code, offset }, extra);
  }
}

const ID = /^[a-z][a-z0-9_]*(\.[a-z0-9_]+)+$/;
const WORD = /^[A-Za-z_][A-Za-z0-9_\-]*$/;
const NUM = /^-?(\d+(\.\d+)?|\.\d+)$/;

function classify(raw, quoted) {
  if (quoted) return { type: 'text', value: raw };
  if (raw === 'true' || raw === 'false') return { type: 'bool', value: raw === 'true' };
  if (NUM.test(raw)) return { type: Number.isInteger(Number(raw)) ? 'int' : 'number', value: Number(raw) };
  if (ID.test(raw)) return { type: 'id', value: raw };
  if (/^[A-Za-z0-9_.\-]+:[a-z0-9_]+$/.test(raw)) return { type: 'labelref', value: raw };
  if (WORD.test(raw)) return { type: 'word', value: raw };
  return { type: 'raw', value: raw };
}

// Split into calls on "/" outside quotes, then into tokens on spaces outside quotes.
function tokenize(text) {
  const calls = [];
  let tokens = [], buf = '', quoted = false, inQuote = false, tokenStart = -1, callStart = 0;
  const pushToken = () => {
    if (tokenStart >= 0) tokens.push({ raw: buf, quoted, offset: tokenStart });
    buf = ''; quoted = false; tokenStart = -1;
  };
  for (let i = 0; i < text.length; i++) {
    const ch = text[i];
    if (inQuote) {
      if (ch === '\\') {
        const next = text[i + 1];
        if (next !== '"' && next !== '\\') throw new CallError('PARSE_ERROR', i, 'only \\" and \\\\ escapes are allowed');
        buf += next; i++;
      } else if (ch === '"') inQuote = false;
      else buf += ch;
      continue;
    }
    if (ch === '"') {
      if (tokenStart >= 0) throw new CallError('PARSE_ERROR', i, 'quote must start a token');
      tokenStart = i; quoted = true; inQuote = true; continue;
    }
    if (ch === '/') { pushToken(); calls.push({ tokens, offset: callStart }); tokens = []; callStart = i + 1; continue; }
    if (/\s/.test(ch)) { pushToken(); continue; }
    if (tokenStart < 0) tokenStart = i;
    buf += ch;
  }
  if (inQuote) throw new CallError('PARSE_ERROR', text.length, 'unclosed quote');
  pushToken();
  calls.push({ tokens, offset: callStart });
  return calls;
}

// Returns [{index,label,negate,name,positional:[{type,value,raw,offset}],named:{key:{...}},offset}]
function parseCalls(text) {
  if (typeof text !== 'string') throw new CallError('PARSE_ERROR', 0, 'call list must be a string');
  if (text.trim() === '') return [];
  return tokenize(text).map((call, idx) => {
    const t = call.tokens.slice();
    if (!t.length) throw new CallError('PARSE_ERROR', call.offset, 'empty call between "/"', { call: idx + 1 });
    let label = null;
    if (!t[0].quoted && /^[a-z][a-z0-9_]*:$/.test(t[0].raw)) label = t.shift().raw.slice(0, -1);
    else if (!t[0].quoted && /^[a-z][a-z0-9_]*:[!a-z]/.test(t[0].raw) && !ID.test(t[0].raw)) {
      const [l, rest] = [t[0].raw.slice(0, t[0].raw.indexOf(':')), t[0].raw.slice(t[0].raw.indexOf(':') + 1)];
      label = l; t[0] = { ...t[0], raw: rest, offset: t[0].offset + l.length + 1 };
    }
    if (!t.length) throw new CallError('PARSE_ERROR', call.offset, 'label without call', { call: idx + 1 });
    let head = t.shift();
    if (head.quoted) throw new CallError('PARSE_ERROR', head.offset, 'function name cannot be quoted', { call: idx + 1 });
    let negate = false, name = head.raw;
    if (name.startsWith('!')) { negate = true; name = name.slice(1); }
    if (!/^[a-zA-Z][a-zA-Z0-9_.\-]*$/.test(name) && !/^\d+$/.test(name))
      throw new CallError('PARSE_ERROR', head.offset, `bad function name "${name}"`, { call: idx + 1 });
    const positional = [], named = {};
    for (const tok of t) {
      const eq = tok.quoted ? -1 : tok.raw.indexOf('=');
      if (eq > 0) {
        const key = tok.raw.slice(0, eq), raw = tok.raw.slice(eq + 1);
        if (key in named) throw new CallError('PARSE_ERROR', tok.offset, `duplicate named argument "${key}"`, { call: idx + 1 });
        named[key] = { ...classify(raw, false), raw, offset: tok.offset + eq + 1 };
      } else {
        if (Object.keys(named).length) throw new CallError('PARSE_ERROR', tok.offset, 'positional argument after named argument', { call: idx + 1 });
        positional.push({ ...classify(tok.raw, tok.quoted), raw: tok.raw, offset: tok.offset });
      }
    }
    return { index: idx + 1, label, negate, name, positional, named, offset: head.offset };
  });
}

// Signature: {params:[{name,type,optional,default,rest}], named:{key:{type,default}}}
// type: id | int | number | bool | text | word | value | labelref | idOrLabel | enum:<a|b>
function typeOk(arg, type) {
  if (type === 'value') return ['bool', 'int', 'number'].includes(arg.type);
  if (type === 'number') return arg.type === 'int' || arg.type === 'number';
  if (type === 'idOrLabel') return arg.type === 'id' || arg.type === 'labelref';
  if (type === 'word') return arg.type === 'word' || arg.type === 'id';
  if (type && type.startsWith('enum:')) return type.slice(5).split('|').includes(String(arg.value));
  return arg.type === type;
}

function checkCall(call, sig) {
  const errors = [];
  const at = (code, offset, message, expected, actual) => errors.push({ code, call: call.index, offset, message, expected, actual });
  const params = sig.params || [];
  const rest = params.length && params[params.length - 1].rest ? params[params.length - 1] : null;
  const fixed = rest ? params.slice(0, -1) : params;
  const required = fixed.filter(p => !p.optional).length;
  if (call.positional.length < required || (!rest && call.positional.length > fixed.length))
    at('ARG_COUNT', call.offset, `${call.name}: wrong argument count`, rest ? `>=${required}` : `${required}-${fixed.length}`, call.positional.length);
  const values = {};
  call.positional.forEach((arg, i) => {
    const p = i < fixed.length ? fixed[i] : rest;
    if (!p) return;
    if (!typeOk(arg, p.type)) at('ARG_TYPE', arg.offset, `${call.name}.${p.name} type`, p.type, arg.type);
    if (p === rest) (values[p.name] = values[p.name] || []).push(arg.value); else values[p.name] = arg.value;
  });
  fixed.forEach((p, i) => { if (i >= call.positional.length && 'default' in p) values[p.name] = p.default; });
  const named = sig.named || {};
  for (const [key, arg] of Object.entries(call.named)) {
    if (!(key in named)) { at('ARG_COUNT', arg.offset, `${call.name}: unknown named argument "${key}"`, Object.keys(named).join('|'), key); continue; }
    if (!typeOk(arg, named[key].type)) at('ARG_TYPE', arg.offset, `${call.name}.${key} type`, named[key].type, arg.type);
    values[key] = arg.value;
  }
  for (const [key, spec] of Object.entries(named)) if (!(key in values) && 'default' in spec) values[key] = spec.default;
  if (call.negate && !sig.negatable) at('PARSE_ERROR', call.offset, `"!" is only allowed on conditions`, 'no !', '!');
  return { values, errors };
}

module.exports = { parseCalls, checkCall, CallError };

if (require.main === module) {
  const assert = require('node:assert/strict');
  const a = parseCalls('move npc.serin dest.snack timeout=30 / say npc.serin "a / b \\"q\\"" / device: setState object.x supply_stopped count=2 / !completed node.a2');
  assert.equal(a.length, 4);
  assert.equal(a[0].named.timeout.value, 30);
  assert.equal(a[1].positional[1].value, 'a / b "q"');
  assert.equal(a[2].label, 'device');
  assert.equal(a[3].negate, true);
  assert.equal(parseCalls('').length, 0);
  assert.equal(parseCalls('420 dest.snack / 1080 home')[0].name, '420');
  assert.equal(parseCalls('perform puzzle room.case_a3:device')[0].positional[1].type, 'labelref');
  assert.throws(() => parseCalls('say npc.a "open'), e => e.code === 'PARSE_ERROR');
  assert.throws(() => parseCalls('say a / / b'), e => e.code === 'PARSE_ERROR');
  const { errors } = checkCall(parseCalls('move npc.serin')[0], { params: [{ name: 'actor', type: 'id' }, { name: 'dest', type: 'id' }] });
  assert.equal(errors[0].code, 'ARG_COUNT');
  console.log('parse-calls self-test: pass');
}
