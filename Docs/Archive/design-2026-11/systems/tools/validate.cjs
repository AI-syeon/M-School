// Documentation/data validation only. No Unity code or gameplay execution.
// Function-call columns (trigger/when/script/forks/effects/schedule script) are parsed by the shared
// parse-calls.cjs and checked against the small v1 function registry below.
// Grammar and decisions: design/systems/full-design/11-function-syntax.md
const fs = require('node:fs');
const path = require('node:path');
const crypto = require('node:crypto');
const assert = require('node:assert/strict');
const { parseCalls, checkCall, CallError } = require('./parse-calls.cjs');
const root = path.resolve(__dirname, '..');
const read = name => JSON.parse(fs.readFileSync(path.join(root, name), 'utf8'));
const bundleFile = 'examples/campus-demo.json';
const saveFile = 'examples/save-v1.json';
const bundle = read(bundleFile);
const contentSchema = read('schemas/content.schema.json');
const saveSchema = read('schemas/save.schema.json');
const hash = crypto.createHash('sha256').update(fs.readFileSync(path.join(root, bundleFile))).digest('hex');
const ptr = s => String(s).replace(/~/g, '~0').replace(/\//g, '~1');
function schemaErrors(schema, value, base = schema, at = '') {
  if (schema === true) return [];
  if (schema === false) return [{code: 'SCHEMA', pointer: at, message: 'Forbidden value'}];
  if (schema.$ref) {
    assert(schema.$ref.startsWith('#/'), 'Only local references supported');
    const target = schema.$ref.slice(2).split('/').reduce((x, k) => x[k], base);
    assert(target, 'Unresolved schema ref ' + schema.$ref);
    return schemaErrors(target, value, base, at);
  }
  const errors = [];
  const fail = message => errors.push({code: 'SCHEMA', pointer: at, message});
  if (schema.oneOf) {
    const alternatives = schema.oneOf.map(s => schemaErrors(s, value, base, at));
    const count = alternatives.filter(x => !x.length).length;
    if (count !== 1) {
      fail('oneOf matched ' + count + ' alternatives');
      if (!count) errors.push(...alternatives.sort((a,b) => a.length-b.length)[0]);
    }
  }
  if ('const' in schema && JSON.stringify(value) !== JSON.stringify(schema.const)) fail('const mismatch');
  if (schema.enum && !schema.enum.some(x => JSON.stringify(x) === JSON.stringify(value))) fail('enum mismatch');
  const types = {object: x => x !== null && typeof x === 'object' && !Array.isArray(x), array: Array.isArray,
    string: x => typeof x === 'string', number: x => typeof x === 'number' && Number.isFinite(x),
    integer: x => Number.isInteger(x), boolean: x => typeof x === 'boolean'};
  if (schema.type && !types[schema.type](value)) { fail('Expected ' + schema.type); return errors; }
  if (typeof value === 'number') {
    for (const [k, invalid] of [['minimum', value < schema.minimum], ['maximum', value > schema.maximum],
      ['exclusiveMinimum', value <= schema.exclusiveMinimum], ['exclusiveMaximum', value >= schema.exclusiveMaximum]])
      if (k in schema && invalid) fail(k + ' violated');
  }
  if (typeof value === 'string') {
    if (schema.minLength !== undefined && [...value].length < schema.minLength) fail('minLength violated');
    if (schema.pattern && !new RegExp(schema.pattern).test(value)) fail('pattern mismatch');
  }
  if (Array.isArray(value)) {
    if (schema.minItems !== undefined && value.length < schema.minItems) fail('minItems violated');
    if (schema.maxItems !== undefined && value.length > schema.maxItems) fail('maxItems violated');
    if (schema.uniqueItems && new Set(value.map(x => JSON.stringify(x))).size !== value.length) fail('Duplicate array value');
    if (schema.items) value.forEach((x,i) => errors.push(...schemaErrors(schema.items,x,base,at+'/'+i)));
  } else if (value !== null && typeof value === 'object') {
    for (const k of schema.required || []) if (!(k in value)) errors.push({code:'SCHEMA',pointer:at+'/'+ptr(k),message:'Required property missing'});
    for (const [k,v] of Object.entries(value)) {
      if (schema.properties?.[k]) errors.push(...schemaErrors(schema.properties[k],v,base,at+'/'+ptr(k)));
      else if (schema.additionalProperties === false) errors.push({code:'SCHEMA',pointer:at+'/'+ptr(k),message:'Unknown property'});
      else if (schema.additionalProperties && typeof schema.additionalProperties === 'object') errors.push(...schemaErrors(schema.additionalProperties,v,base,at+'/'+ptr(k)));
    }
  }
  return errors;
}

// ---- v1 function registry (a small subset of full-design/11-function-syntax.md) -------------------------
// params: positional [{name,type,ref?,gt?,min?}], named: {key:{type,default?,gt?,min?}}, negatable: "!" allowed.
// type names are those of parse-calls.cjs checkCall; ref = definition kind the id must point to.
const FUNCTIONS = {
  trigger: {
    interact: {params: [{name: 'target', type: 'id', ref: 'npc'}, {name: 'label', type: 'text'}, {name: 'range', type: 'number', gt: 0}]},
    clock: {params: []},
    manual: {params: []}
  },
  condition: {
    eq: {negatable: true, params: [{name: 'variable', type: 'id', ref: 'var'}, {name: 'value', type: 'value'}]},
    gte: {negatable: true, params: [{name: 'variable', type: 'id', ref: 'var'}, {name: 'value', type: 'int'}]},
    any: {negatable: true, params: [{name: 'conditions', type: 'text', rest: true}], minRest: 1}
  },
  effect: {
    set: {params: [{name: 'variable', type: 'id', ref: 'var'}, {name: 'value', type: 'value'}]}
  },
  command: {
    move: {params: [{name: 'actor', type: 'id', ref: 'npc'}, {name: 'dest', type: 'id', ref: 'dest'}], named: {timeout: {type: 'number', default: 30, gt: 0}, portals: {type: 'bool', default: true}, async: {type: 'word'}}},
    await: {params: [{name: 'handles', type: 'word', rest: true}], minRest: 1},
    say: {params: [{name: 'speaker', type: 'id', ref: 'npc'}, {name: 'text', type: 'text'}]},
    choice: {params: [{name: 'prompt', type: 'text'}], pairs: true},
    branch: {params: [{name: 'condition', type: 'text'}, {name: 'then', type: 'word'}, {name: 'else', type: 'word', optional: true}]},
    wait: {params: [{name: 'seconds', type: 'number', min: 0}]},
    camera: {params: [{name: 'dest', type: 'id', ref: 'dest'}, {name: 'seconds', type: 'number', gt: 0}], named: {async: {type: 'word'}}},
    animate: {params: [{name: 'actor', type: 'id', ref: 'npc'}, {name: 'clip', type: 'enum:wave|idle'}, {name: 'seconds', type: 'number', gt: 0}], named: {async: {type: 'word'}}}
  },
  schedule: {entry: {params: [{name: 'dest', type: 'word'}]}}
};
const lookup = (kind, name) => Object.hasOwn(FUNCTIONS[kind], name) ? FUNCTIONS[kind][name] : undefined;
const FORK_NAME = /^[A-Za-z_][A-Za-z0-9_\-]*$/;

function semantic(b) {
  const errors = [], routes = [], positions = new Set(), calls = {trigger: 0, when: 0, script: 0, forks: 0, effects: 0, schedule: 0};
  // Diagnostic: {severity,code,file,pointer,entityId,call,offset,message,expected,actual}
  let entity = null;
  const fail = (code, pointer, message, x = {}) => errors.push({severity: 'error', code, file: bundleFile, pointer, entityId: entity, call: x.call, offset: x.offset, message, expected: x.expected, actual: x.actual});
  const ids = new Set(), kinds = new Map();
  const define = (x, p, prefix, kind = prefix) => {
    entity = x.id;
    if (ids.has(x.id)) fail('DUPLICATE_ID', p + '/id', x.id);
    ids.add(x.id); kinds.set(x.id, kind);
    if (prefix && !x.id.startsWith(prefix + '.')) fail('ID_KIND', p + '/id', 'Expected ' + prefix + '.*');
  };
  const collections = [['variables', null, 'var'], ['maps', 'map', 'map'], ['destinations', 'dest', 'dest'], ['npcs', 'npc', 'npc'], ['schedules', 'schedule', 'schedule'], ['events', 'event', 'event']];
  for (const [name, prefix, kind] of collections) b[name].forEach((x, i) => define(x, `/${name}/${i}`, prefix, kind));
  const maps = new Map(b.maps.map(x => [x.id, x]));
  const dests = new Map(b.destinations.map(x => [x.id, x]));
  const npcs = new Map(b.npcs.map(x => [x.id, x]));
  const vars = new Map(b.variables.map(x => [x.id, x]));
  const tables = {map: maps, dest: dests, npc: npcs, var: vars, event: new Map(b.events.map(x => [x.id, x]))};
  // Reference by kind: missing id -> REF_NOT_FOUND, existing id of another kind -> REF_KIND.
  const ref = (kind, id, p, x = {}) => {
    const hit = tables[kind].get(id);
    if (hit) return hit;
    if (kinds.has(id)) fail('REF_KIND', p, `${id} is a ${kinds.get(id)}, expected ${kind}`, {...x, expected: kind, actual: kinds.get(id)});
    else fail('REF_NOT_FOUND', p, id, {...x, expected: kind});
    return undefined;
  };
  const typedValue = (v, value, p, x, numeric = false) => {
    if (!v) return;
    if ((v.type === 'bool' ? typeof value !== 'boolean' : !Number.isInteger(value)) || (numeric && v.type !== 'int'))
      fail('TYPE_MISMATCH', p, `${v.id} is ${v.type}`, {...x, expected: numeric ? 'int variable and int value' : v.type, actual: String(value)});
  };
  b.variables.forEach((v, i) => { entity = v.id; typedValue(v, v.initial, `/variables/${i}/initial`, {}); });
  const walkable = pos => {
    const m = maps.get(pos.mapId); if (!m) return false;
    return pos.x >= 0 && pos.y >= 0 && pos.x < m.width && pos.y < m.height && !m.blocked.some(([x, y]) => x === Math.floor(pos.x) && y === Math.floor(pos.y));
  };
  const position = (pos, p) => { if (!ref('map', pos.mapId, p + '/mapId')) return; if (!walkable(pos)) fail('INVALID_POSITION', p, JSON.stringify(pos)); };
  b.maps.forEach((m, i) => {
    entity = m.id;
    m.blocked.forEach(([x, y], j) => { if (x >= m.width || y >= m.height) fail('OUT_OF_BOUNDS', `/maps/${i}/blocked/${j}`, 'Outside map'); });
    m.portals.forEach((po, j) => { const p = `/maps/${i}/portals/${j}`; define(po, p, 'portal'); entity = po.id; position({...po, mapId: m.id}, p); position(po.to, p + '/to'); });
  });
  b.destinations.forEach((d, i) => { entity = d.id; position(d.position, `/destinations/${i}/position`); });
  b.npcs.forEach((n, i) => { entity = n.id; position(n.position, `/npcs/${i}/position`); ref('dest', n.home, `/npcs/${i}/home`); });
  // Static graph reachability oracle, deliberately not a runtime A* or movement executor.
  function route(from, to, allowPortals = true) {
    if (!walkable(from) || !walkable(to)) return null;
    const key = p => `${p.mapId}:${Math.floor(p.x)},${Math.floor(p.y)}`;
    const queue = [{...from, via: []}], seen = new Set([key(from)]);
    for (let i = 0; i < queue.length; i++) {
      const p = queue[i], m = maps.get(p.mapId);
      if (key(p) === key(to)) return p.via;
      const candidates = [];
      for (let dy = -1; dy <= 1; dy++) for (let dx = -1; dx <= 1; dx++) if (dx || dy) {
        const n = {mapId: p.mapId, x: Math.floor(p.x) + dx + 0.5, y: Math.floor(p.y) + dy + 0.5, via: p.via};
        if (dx && dy && (!walkable({...n, x: p.x}) || !walkable({...n, y: p.y}))) continue;
        candidates.push(n);
      }
      if (allowPortals) for (const po of m.portals) if (po.open && Math.floor(po.x) === Math.floor(p.x) && Math.floor(po.y) === Math.floor(p.y)) candidates.push({...po.to, via: [...p.via, po.id]});
      for (const n of candidates) if (walkable(n) && !seen.has(key(n))) { seen.add(key(n)); queue.push(n); }
    }
    return null;
  }

  // ---- call-column plumbing ----------------------------------------------------------------------------
  // parseList: string -> calls (with .pos) or null after a PARSE_ERROR. base shifts offsets of nested text.
  // 1-based index of the call that contains offset (counts "/" outside quotes).
  const callAt = (text, offset) => { let n = 1, q = false; for (let i = 0; i < Math.min(offset, text.length); i++) { if (text[i] === '\\' && q) i++; else if (text[i] === '"') q = !q; else if (text[i] === '/' && !q) n++; } return n; };
  function parseList(text, pointer, pos, base = 0) {
    try { return parseCalls(text).map(c => ({...c, pos: pos(c.index), offset: base + c.offset, positional: c.positional.map(a => ({...a, offset: base + a.offset})), named: Object.fromEntries(Object.entries(c.named).map(([k, a]) => [k, {...a, offset: base + a.offset}]))})); }
    catch (e) {
      if (!(e instanceof CallError)) throw e;
      fail('PARSE_ERROR', pointer, e.message, {call: pos(e.call || callAt(text, e.offset)), offset: base + e.offset});
      return null;
    }
  }
  const limit = (spec, arg, pointer, c, label) => {
    if (!arg || typeof arg.value !== 'number') return;
    if ((spec.gt !== undefined && !(arg.value > spec.gt)) || (spec.min !== undefined && arg.value < spec.min))
      fail('ARG_TYPE', pointer, `${c.name}.${label} out of range`, {call: c.pos, offset: arg.offset, expected: spec.gt !== undefined ? '>' + spec.gt : '>=' + spec.min, actual: arg.raw});
  };
  // checkSig: parse-calls checkCall + range limits + definition references. Returns resolved values.
  function checkSig(c, sig, pointer) {
    const view = sig.pairs ? {...c, positional: c.positional.slice(0, sig.params.length)} : c;
    const r = checkCall(view, sig);
    for (const e of r.errors) fail(e.code, pointer, e.message, {call: c.pos, offset: e.offset, expected: e.expected, actual: e.actual});
    sig.params.forEach((p, i) => {
      const arg = c.positional[i]; if (!arg) return;
      if (p.rest) { c.positional.slice(i).forEach(a => limit(p, a, pointer, c, p.name)); return; }
      limit(p, arg, pointer, c, p.name);
      if (p.ref && arg.type === 'id') ref(p.ref, arg.value, pointer, {call: c.pos, offset: arg.offset});
    });
    for (const [k, spec] of Object.entries(sig.named || {})) limit(spec, c.named[k], pointer, c, k);
    if (sig.minRest) {
      const rest = sig.params[sig.params.length - 1], fixed = sig.params.length - 1;
      if (c.positional.length - fixed < sig.minRest) fail('ARG_COUNT', pointer, `${c.name}: needs at least ${sig.minRest} ${rest.name}`, {call: c.pos, offset: c.offset, expected: '>=' + (fixed + sig.minRest), actual: c.positional.length});
    }
    return r.values;
  }
  const unknown = (c, kind, pointer) => fail('UNKNOWN_FUNCTION', pointer, `unknown ${kind} function "${c.name}"`, {call: c.pos, offset: c.offset, expected: Object.keys(FUNCTIONS[kind]).join('|'), actual: c.name});

  // Condition list (AND of calls). pos(i) names a call; base = offset of the text inside its parent cell.
  function conditionList(text, pointer, pos, base = 0) {
    const list = parseList(text, pointer, pos, base); if (!list) return;
    for (const c of list) {
      calls.when++; positions.add(c.pos);
      const sig = lookup('condition', c.name);
      if (!sig) { unknown(c, 'condition', pointer); continue; }
      const v = checkSig(c, sig, pointer);
      const x = {call: c.pos, offset: c.positional[1]?.offset ?? c.offset};
      if (c.name === 'eq' || c.name === 'gte') {
        const variable = vars.get(c.positional[0]?.value), arg = c.positional[1];
        if (variable && arg && arg.type !== 'text') typedValue(variable, arg.value, pointer, x, c.name === 'gte');
      }
      if (c.name === 'any') c.positional.forEach(a => { if (a.type === 'text') conditionList(a.value, pointer, () => c.pos, a.offset + 1); });
    }
  }
  b.schedules.forEach((s, i) => { entity = s.id; ref('npc', s.npcId, `/schedules/${i}/npcId`); });

  // ---- schedules ---------------------------------------------------------------------------------------
  const scheduleNpcs = new Set(), scheduleTargets = new Map();
  b.schedules.forEach((s, i) => {
    entity = s.id;
    const p = `/schedules/${i}/script`;
    if (scheduleNpcs.has(s.npcId)) fail('SCHEDULE_DUPLICATE', `/schedules/${i}`, s.npcId); scheduleNpcs.add(s.npcId);
    const list = parseList(s.script, p, k => `${s.id}#${k}`); if (!list) return;
    let previous = -1;
    for (const c of list) {
      calls.schedule++; positions.add(c.pos);
      if (!/^\d+$/.test(c.name)) { fail('UNKNOWN_FUNCTION', p, `schedule entry must start with a minute, got "${c.name}"`, {call: c.pos, offset: c.offset, expected: 'minute', actual: c.name}); continue; }
      const minute = Number(c.name);
      checkSig(c, FUNCTIONS.schedule.entry, p);
      if (minute > 1439) fail('SCHEDULE_RANGE', p, 'start minute must be 0..1439', {call: c.pos, offset: c.offset, expected: '0..1439', actual: c.name});
      else if (minute <= previous) fail('SCHEDULE_ORDER', p, 'start minutes must strictly increase', {call: c.pos, offset: c.offset, expected: '>' + previous, actual: c.name});
      previous = Math.max(previous, minute);
      const target = c.positional[0];
      if (target && target.value === 'home') { const n = npcs.get(s.npcId); if (n) { (scheduleTargets.get(s.npcId) || scheduleTargets.set(s.npcId, []).get(s.npcId)).push(n.home); } }
      else if (target && target.type === 'id' && ref('dest', target.value, p, {call: c.pos, offset: target.offset})) (scheduleTargets.get(s.npcId) || scheduleTargets.set(s.npcId, []).get(s.npcId)).push(target.value);
    }
  });

  // ---- events ------------------------------------------------------------------------------------------
  b.events.forEach((e, ei) => {
    entity = e.id;
    const ep = '/events/' + ei, declared = new Set(e.resources), written = new Set();
    const validResources = new Set([...npcs.keys(), ...[...maps.keys()].map(x => 'space.' + x), 'input.player', 'camera.main']);
    e.resources.forEach((r, i) => { if (!validResources.has(r)) fail('REF_NOT_FOUND', `${ep}/resources/${i}`, r); });

    // trigger: exactly one call
    {
      const p = ep + '/trigger', list = parseList(e.trigger, p, () => `${e.id}@trigger`);
      if (list) {
        if (list.length !== 1) fail('ARG_COUNT', p, 'trigger must be exactly one call', {expected: 1, actual: list.length});
        for (const c of list.slice(0, 1)) { calls.trigger++; const sig = lookup('trigger', c.name); if (!sig) unknown(c, 'trigger', p); else checkSig(c, sig, p); }
      }
    }
    conditionList(e.when, ep + '/when', i => `${e.id}@when#${i}`);

    // effects: applied once, atomically, after the script succeeds
    {
      const p = ep + '/effects', list = parseList(e.effects, p, i => `${e.id}@effects#${i}`) || [];
      for (const c of list) {
        calls.effects++; positions.add(c.pos);
        const sig = lookup('effect', c.name);
        if (!sig) { unknown(c, 'effect', p); continue; }
        checkSig(c, sig, p);
        const key = c.positional[0]?.value, arg = c.positional[1], x = {call: c.pos, offset: arg?.offset ?? c.offset};
        if (arg && vars.has(key) && arg.type !== 'text') typedValue(vars.get(key), arg.value, p, x);
        if (typeof key === 'string' && key.startsWith('clock.')) fail('READ_ONLY', p, key, {call: c.pos, offset: c.positional[0].offset, expected: 'writable variable', actual: key});
        if (written.has(key)) fail('EFFECT_DUPLICATE', p, key, {call: c.pos, offset: c.positional[0]?.offset});
        written.add(key);
      }
    }

    // script and forks
    const forks = e.forks || {}, usedForks = new Set(), edges = [];
    function commands(listName, text, pointer) {
      const pos = i => listName === null ? `${e.id}#${i}` : `${e.id}:${listName}#${i}`;
      const list = parseList(text, pointer, pos); if (!list) return;
      const active = new Map(), seenHandles = new Set(), labels = new Set();
      const use = (r, c) => { if (!declared.has(r)) fail('LEASE_UNDECLARED', pointer, r, {call: c.pos, offset: c.offset, expected: 'declared in resources', actual: r}); };
      const forkRef = (arg, c) => {
        if (!arg || arg.type === 'text') return;
        if (!Object.hasOwn(forks, arg.value)) { fail('FORK_NOT_FOUND', pointer, `fork "${arg.value}" not defined in this event`, {call: c.pos, offset: arg.offset, expected: Object.keys(forks).join('|') || '(no forks)', actual: arg.value}); return; }
        usedForks.add(arg.value); edges.push([listName, arg.value]);
      };
      const mustBeClosed = c => { for (const [h, a] of active) fail('HANDLE_UNAWAITED', pointer, `async handle "${h}" (${a.pos}) is not awaited before ${c.name}`, {call: c.pos, offset: c.offset, expected: 'await ' + h, actual: c.name}); };
      list.forEach(c => {
        calls[listName === null ? 'script' : 'forks']++; positions.add(c.pos);
        const sig = lookup('command', c.name);
        if (!sig) { unknown(c, 'command', pointer); return; }
        if (c.label) { if (labels.has(c.label)) fail('LABEL_DUP', pointer, c.label, {call: c.pos, offset: c.offset}); labels.add(c.label); }
        const v = checkSig(c, sig, pointer);
        // resources written by this call, for async conflict detection
        const touches = c.name === 'move' || c.name === 'animate' ? [c.positional[0]?.value] : c.name === 'camera' ? ['camera.main'] : [];
        for (const r of touches.filter(Boolean)) for (const [h, a] of active) if (a.touches.includes(r))
          fail('HANDLE_CONFLICT', pointer, `${r} is already used by async handle "${h}" (${a.pos})`, {call: c.pos, offset: c.offset, expected: 'await ' + h + ' first', actual: r});
        if (c.name === 'move') {
          const n = npcs.get(c.positional[0]?.value), d = dests.get(c.positional[1]?.value);
          use(c.positional[0]?.value, c);
          if (n && d) {
            const via = route(n.position, d.position, v.portals ?? true);
            if (via === null) fail('NO_ROUTE', pointer, 'No static route from NPC definition position', {call: c.pos, offset: c.offset, expected: 'route to ' + d.id, actual: 'none'});
            else {
              routes.push({eventId: e.id, call: c.pos, actorId: n.id, destinationId: d.id, portals: via});
              use('space.' + n.position.mapId, c); use('space.' + d.position.mapId, c);
              for (const id of via) for (const m of b.maps) if (m.portals.some(x => x.id === id)) use('space.' + m.id, c);
            }
          }
        } else if (c.name === 'say') use('input.player', c);
        else if (c.name === 'camera') { const d = dests.get(c.positional[0]?.value); use('camera.main', c); if (d) use('space.' + d.position.mapId, c); }
        else if (c.name === 'animate') use(c.positional[0]?.value, c);
        if (c.name === 'await') {
          for (const arg of c.positional) {
            if (active.has(arg.value)) active.delete(arg.value);
            else fail('HANDLE_UNDEFINED', pointer, `handle "${arg.value}" is not an open async handle in this list (handles never cross into forks)`, {call: c.pos, offset: arg.offset, expected: [...active.keys()].join('|') || '(none open)', actual: arg.value});
          }
        }
        if (['say', 'choice', 'branch'].includes(c.name)) mustBeClosed(c);
        if (c.name === 'choice') {
          use('input.player', c);
          const rest = c.positional.slice(1);
          if (!rest.length || rest.length % 2) fail('ARG_COUNT', pointer, 'choice: needs prompt followed by (fork "text") pairs', {call: c.pos, offset: c.offset, expected: 'prompt + 2n args, n>=1', actual: c.positional.length});
          const seen = new Set();
          for (let k = 0; k < rest.length; k += 2) {
            const [label, text] = [rest[k], rest[k + 1]];
            if (label && !['word', 'id'].includes(label.type)) fail('ARG_TYPE', pointer, 'choice fork name', {call: c.pos, offset: label.offset, expected: 'word', actual: label.type});
            if (text && text.type !== 'text') fail('ARG_TYPE', pointer, 'choice text', {call: c.pos, offset: text.offset, expected: 'text', actual: text.type});
            if (label) { if (seen.has(label.value)) fail('LABEL_DUP', pointer, `duplicate choice fork "${label.value}"`, {call: c.pos, offset: label.offset}); seen.add(label.value); forkRef(label, c); }
          }
        }
        if (c.name === 'branch') {
          const cond = c.positional[0];
          if (cond?.type === 'text') conditionList(cond.value, pointer, () => c.pos, cond.offset + 1);
          forkRef(c.positional[1], c); forkRef(c.positional[2], c);
        }
        if (v.async !== undefined) {
          if (seenHandles.has(v.async)) fail('HANDLE_DUPLICATE', pointer, `handle "${v.async}" reused in this list`, {call: c.pos, offset: c.named.async.offset, expected: 'unique handle', actual: v.async});
          seenHandles.add(v.async); active.set(v.async, {pos: c.pos, touches});
        }
      });
      for (const [h, a] of active) fail('HANDLE_UNAWAITED', pointer, `async handle "${h}" is not awaited before the end of the list`, {call: a.pos, offset: list.find(c => c.pos === a.pos).offset, expected: 'await ' + h, actual: 'end of list'});
    }
    commands(null, e.script, ep + '/script');
    for (const [name, text] of Object.entries(forks)) {
      if (!FORK_NAME.test(name)) fail('PARSE_ERROR', `${ep}/forks/${ptr(name)}`, `bad fork name "${name}"`, {expected: 'word', actual: name});
      commands(name, text, `${ep}/forks/${ptr(name)}`);
    }
    for (const name of Object.keys(forks)) if (!usedForks.has(name)) fail('FORK_UNUSED', `${ep}/forks/${ptr(name)}`, `fork "${name}" is never called by choice or branch`);
    // fork graph must be acyclic
    const graph = new Map(); for (const [from, to] of edges) (graph.get(from) || graph.set(from, new Set()).get(from)).add(to);
    const state = new Map();
    const visit = n => { if (state.get(n) === 1) { fail('FORK_CYCLE', `${ep}/forks/${ptr(n)}`, `fork "${n}" calls itself`); return; } if (state.get(n) === 2) return; state.set(n, 1); for (const m of graph.get(n) || []) visit(m); state.set(n, 2); };
    visit(null);
  });
  // Verify all declared NPC start positions can reach every schedule target and home.
  b.npcs.forEach((n, i) => {
    entity = n.id;
    for (const id of [n.home, ...(scheduleTargets.get(n.id) || [])]) { const d = dests.get(id); if (d && route(n.position, d.position) === null) fail('SCHEDULE_NO_ROUTE', `/npcs/${i}`, id); }
  });
  return {errors, routes, ids, positions, calls};
}

function inspect(b) {
  const structural = schemaErrors(contentSchema, b).map(e => ({severity: 'error', file: bundleFile, ...e}));
  if (structural.length) return {errors: structural, routes: [], ids: new Set(), positions: new Set(), calls: {}};
  return semantic(b);
}
const result = inspect(bundle);
assert.equal(result.errors.length, 0, JSON.stringify(result.errors, null, 2));
const saved = read(saveFile);
const saveErrors = schemaErrors(saveSchema, saved);
assert.equal(saveErrors.length, 0, JSON.stringify(saveErrors, null, 2));
assert.equal(saved.catalogHash, hash, 'Stale save catalog hash');
assert.equal(saved.contentVersion, bundle.contentVersion);
assert.deepEqual(Object.keys(saved.variables).sort(), bundle.variables.map(x => x.id).sort());
for (const v of bundle.variables) assert(v.type === 'bool' ? typeof saved.variables[v.id] === 'boolean' : Number.isInteger(saved.variables[v.id]));
assert.equal(saved.variables['clock.minute_of_day'], saved.clock.absoluteMinute % 1440);
assert.equal(new Set(saved.positions.map(p => p.actorId)).size, saved.positions.length);
assert.deepEqual(saved.positions.map(p => p.actorId).sort(), ['actor.player', ...bundle.npcs.map(x => x.id)].sort());
for (const p of saved.positions) {
  const m = bundle.maps.find(x => x.id === p.mapId); assert(m, 'Unknown save map');
  assert(p.x < m.width && p.y < m.height && !m.blocked.some(([x, y]) => x === Math.floor(p.x) && y === Math.floor(p.y)), 'Invalid save position');
}
for (const id of saved.completed) assert(bundle.events.some(x => x.id === id), 'Unknown completion key');
for (const a of saved.appointments) { assert(bundle.events.some(x => x.id === a.eventId)); assert(!saved.completed.includes(a.eventId)); assert(a.acceptedAt <= saved.clock.absoluteMinute); }
// This authored snapshot is specifically the expected snack result, not a general save oracle.
assert.deepEqual(saved.completed, ['event.snack']);
assert.equal(saved.variables['memory.snack_done'], true);
assert.equal(saved.clock.absoluteMinute, 430);
const trace = read('examples/expected-trace.json');
assert.equal(trace.kind, 'specification-example-not-runtime-log');
trace.records.forEach((r, i) => {
  assert.equal(r.sequence, i + 1);
  assert(result.ids.has(r.eventId));
  if (r.call) { assert(result.positions.has(r.call), 'Trace call has no matching position: ' + r.call); assert(r.call.startsWith(r.eventId), 'Trace call belongs to another event'); }
  if (r.fork) assert(Object.hasOwn(bundle.events.find(x => x.id === r.eventId).forks, r.fork), 'Trace fork not defined: ' + r.fork);
  assert(!('commandId' in r) && !('handleId' in r) && !('optionId' in r), 'Trace still uses removed command/handle/option ids');
});
assert.equal(trace.expected.absoluteMinute, saved.clock.absoluteMinute);
assert.deepEqual(trace.expected.completed, saved.completed);
assert.equal(trace.expected['memory.snack_done'], saved.variables['memory.snack_done']);

// ---- negative cases: each mutation must be rejected with the named code --------------------------------
const negative = [];
const callCodes = new Set(['PARSE_ERROR', 'UNKNOWN_FUNCTION', 'ARG_COUNT', 'ARG_TYPE', 'REF_NOT_FOUND', 'REF_KIND', 'FORK_NOT_FOUND', 'HANDLE_UNAWAITED', 'HANDLE_CONFLICT', 'HANDLE_UNDEFINED', 'HANDLE_DUPLICATE']);
function reject(name, mutate, code) {
  const b = structuredClone(bundle); mutate(b); const r = inspect(b);
  const hit = r.errors.find(e => e.code === code);
  assert(hit, `${name}: expected ${code}, got ${JSON.stringify(r.errors)}`);
  const located = hit.pointer && hit.call !== undefined && Number.isInteger(hit.offset);
  // Diagnostics for call codes raised inside a call column must carry pointer + call position + offset.
  if (callCodes.has(code) && hit.pointer && /\/(script|forks|when|effects|trigger)/.test(hit.pointer)) assert(located, `${name}: ${code} lacks call position/offset: ${JSON.stringify(hit)}`);
  negative.push({name, expectedCode: code, status: 'pass', diagnostic: {pointer: hit.pointer, entityId: hit.entityId, call: hit.call, offset: hit.offset, message: hit.message}});
}
const [snack, stage, lunch] = [0, 1, 2];
const sub = (b, i, col, from, to) => { assert(b.events[i][col].includes(from), 'mutation target missing: ' + from); b.events[i][col] = b.events[i][col].replace(from, to); };
// parse / registry / arguments
reject('unclosed quote in script', b => b.events[snack].script += ' / say npc.serin "open', 'PARSE_ERROR');
reject('empty call between slashes', b => sub(b, snack, 'script', ' / say npc.serin', ' / / say npc.serin'), 'PARSE_ERROR');
reject('"!" on a command', b => sub(b, snack, 'script', 'move npc.serin', '!move npc.serin'), 'PARSE_ERROR');
reject('unknown command (teleport)', b => sub(b, snack, 'script', 'move npc.serin dest.snack', 'teleport npc.serin dest.snack'), 'UNKNOWN_FUNCTION');
reject('removed command parallel', b => b.events[snack].script += ' / parallel a b', 'UNKNOWN_FUNCTION');
reject('removed command commit', b => b.events[snack].script += ' / commit', 'UNKNOWN_FUNCTION');
reject('effect function used as command', b => b.events[snack].script += ' / set memory.snack_done true', 'UNKNOWN_FUNCTION');
reject('unknown condition function', b => b.events[snack].when = 'completed node.a2', 'UNKNOWN_FUNCTION');
reject('unknown effect function', b => b.events[snack].effects = 'complete event.snack', 'UNKNOWN_FUNCTION');
reject('unknown trigger function', b => b.events[lunch].trigger = 'daily', 'UNKNOWN_FUNCTION');
reject('unknown trigger call', b => sub(b, snack, 'trigger', 'interact', 'touch'), 'UNKNOWN_FUNCTION');
reject('unknown property typo', b => b.events[snack].scirpt = 'wait 1', 'SCHEMA');
reject('missing argument (move)', b => sub(b, snack, 'script', 'move npc.serin dest.snack', 'move npc.serin'), 'ARG_COUNT');
reject('extra argument (say)', b => sub(b, snack, 'script', '"실습 노트를 같이 정리해 볼래?"', '"실습 노트를 같이 정리해 볼래?" extra'), 'ARG_COUNT');
reject('unknown named argument', b => sub(b, stage, 'script', 'timeout=30', 'speed=30'), 'ARG_COUNT');
reject('named timeout not a number', b => sub(b, stage, 'script', 'timeout=30', 'timeout=abc'), 'ARG_TYPE');
reject('named timeout not positive', b => sub(b, stage, 'script', 'timeout=30', 'timeout=0'), 'ARG_TYPE');
reject('clip outside registry', b => sub(b, stage, 'script', 'wave', 'dance'), 'ARG_TYPE');
reject('quoted text where id expected', b => sub(b, snack, 'script', 'move npc.serin', 'move "npc.serin"'), 'ARG_TYPE');
reject('choice prompt count (pairs)', b => sub(b, snack, 'script', 'tea "차부터 준비하자"', 'tea'), 'ARG_COUNT');
reject('gte needs int value', b => b.events[lunch].when = 'gte clock.minute_of_day true', 'ARG_TYPE');
// references
reject('missing destination', b => sub(b, snack, 'script', 'dest.snack', 'dest.missing'), 'REF_NOT_FOUND');
reject('missing NPC speaker', b => sub(b, snack, 'script', 'say npc.serin', 'say npc.ghost'), 'REF_NOT_FOUND');
reject('missing variable in when', b => b.events[snack].when = 'eq story.nope true', 'REF_NOT_FOUND');
reject('missing trigger target', b => sub(b, snack, 'trigger', 'npc.serin', 'npc.ghost'), 'REF_NOT_FOUND');
reject('destination used as actor', b => sub(b, snack, 'script', 'move npc.serin dest.snack', 'move dest.snack dest.snack'), 'REF_KIND');
reject('duplicate ID', b => b.npcs[1].id = b.npcs[0].id, 'DUPLICATE_ID');
// types and effects
reject('wrong bool initial', b => b.variables[0].initial = 1, 'TYPE_MISMATCH');
reject('wrong bool effect value', b => b.events[snack].effects = 'set memory.snack_done 7', 'TYPE_MISMATCH');
reject('gte on bool', b => b.events[snack].when = 'gte story.intro_done 1', 'TYPE_MISMATCH');
reject('readonly clock write', b => b.events[snack].effects = 'set clock.minute_of_day 800', 'READ_ONLY');
reject('duplicate effect write', b => b.events[snack].effects += ' / set memory.snack_done false', 'EFFECT_DUPLICATE');
// map / route
reject('blocked destination', b => b.destinations[2].position = {mapId: 'map.lounge', x: 2.5, y: 2.5}, 'INVALID_POSITION');
reject('outside map', b => b.npcs[0].position.x = 100, 'INVALID_POSITION');
reject('closed cross map portal', b => b.maps[0].portals[0].open = false, 'NO_ROUTE');
reject('forbid required portal', b => sub(b, stage, 'script', 'portals=true', 'portals=false'), 'NO_ROUTE');
reject('diagonal corner cut', b => { b.npcs[1].position = {mapId: 'map.courtyard', x: 0.5, y: 0.5}; b.maps[0].blocked.push([1, 0], [0, 1]); }, 'NO_ROUTE');
// forks
reject('choice calls undefined fork', b => sub(b, snack, 'script', 'notes "노트부터', 'ghost "노트부터'), 'FORK_NOT_FOUND');
reject('branch calls undefined fork', b => sub(b, lunch, 'script', 'known new', 'known ghost'), 'FORK_NOT_FOUND');
reject('fork never called', b => b.events[snack].forks.spare = 'wait 1', 'FORK_UNUSED');
reject('fork calls itself', b => b.events[snack].forks.notes = 'choice "again?" notes "yes"', 'FORK_CYCLE');
// handles
reject('await of unknown handle', b => sub(b, stage, 'script', 'await cam wave', 'await cam ghost'), 'HANDLE_UNDEFINED');
reject('unawaited handle before say', b => sub(b, stage, 'script', ' / await cam wave', ''), 'HANDLE_UNAWAITED');
reject('unawaited handle at end of list', b => b.events[lunch].script += ' / camera dest.stage 1 async=late', 'HANDLE_UNAWAITED');
reject('async move not awaited before choice', b => sub(b, snack, 'script', 'move npc.serin dest.snack', 'move npc.serin dest.snack async=m'), 'HANDLE_UNAWAITED');
reject('handle crossing into a fork', b => { sub(b, lunch, 'script', 'move npc.serin dest.notice', 'move npc.serin dest.notice / camera dest.stage 1 async=cam'); b.events[lunch].forks.known = 'await cam / ' + b.events[lunch].forks.known; }, 'HANDLE_UNDEFINED');
reject('handle name reused', b => sub(b, stage, 'script', 'async=wave', 'async=cam'), 'HANDLE_DUPLICATE');
reject('same camera used by two async calls', b => sub(b, stage, 'script', 'animate npc.mira wave 0.5 async=wave', 'camera dest.stage 1 async=cam2'), 'HANDLE_CONFLICT');
reject('same actor used by two async calls', b => sub(b, stage, 'script', 'camera dest.stage 0.5 async=cam', 'animate npc.mira idle 0.5 async=cam'), 'HANDLE_CONFLICT');
reject('sync move of an actor busy in async', b => sub(b, stage, 'script', ' / await cam wave', ' / move npc.mira dest.stage / await cam wave'), 'HANDLE_CONFLICT');
// leases
reject('undeclared NPC lease', b => b.events[snack].resources = b.events[snack].resources.filter(r => r !== 'npc.serin'), 'LEASE_UNDECLARED');
reject('undeclared camera lease', b => b.events[stage].resources = b.events[stage].resources.filter(r => r !== 'camera.main'), 'LEASE_UNDECLARED');
// schedules
reject('schedule start order', b => b.schedules[0].script = b.schedules[0].script.replace('420 dest.snack', '800 dest.snack'), 'SCHEDULE_ORDER');
reject('schedule minute out of range', b => b.schedules[0].script = b.schedules[0].script.replace('1080', '1500'), 'SCHEDULE_RANGE');
reject('schedule entry without minute', b => b.schedules[0].script = 'dest.snack', 'UNKNOWN_FUNCTION');
reject('schedule missing destination', b => b.schedules[0].script = b.schedules[0].script.replace('dest.notice', 'dest.nowhere'), 'REF_NOT_FOUND');
reject('duplicate schedule per NPC', b => b.schedules[1].npcId = 'npc.serin', 'SCHEDULE_DUPLICATE');
// old object format must not load
reject('schema version 1 object bundle', b => { b.schemaVersion = 1; }, 'SCHEMA');
reject('old event object format (commands/condition/once)', b => { b.events[snack].commands = []; b.events[snack].condition = {op: 'all', args: []}; b.events[snack].once = true; }, 'SCHEMA');
reject('old interactions table', b => { b.interactions = []; }, 'SCHEMA');
reject('old schedule rows', b => { b.schedules[0].rows = []; }, 'SCHEMA');
// ---- positive variants: legal forms the demo bundle does not use must be accepted ---------------------
const variants = [];
function accept(name, mutate) {
  const b = structuredClone(bundle); mutate(b); const r = inspect(b);
  assert.equal(r.errors.length, 0, name + ': ' + JSON.stringify(r.errors));
  variants.push({name, status: 'pass'});
}
accept('! and any in when', b => { b.events[1].when = '!eq memory.snack_done true / any "eq story.intro_done true" "gte clock.minute_of_day 720"'; });
accept('negated any', b => { b.events[1].when = '!any "eq memory.snack_done true" "eq world.stage_ready true"'; });
accept('async camera beside a sync move of another resource', b => { b.events[1].script = 'camera dest.stage 0.5 async=c / move npc.mira dest.stage / await c / say npc.mira "ok"'; });
accept('fork calling another fork', b => { b.events[0].forks.notes = 'choice "one more?" tea "yes"'; b.events[0].forks.tea = 'say npc.serin "ok"'; b.events[0].script = 'move npc.serin dest.snack / say npc.serin "hi" / choice "start" notes "notes"'; });
accept('empty when, effects and schedule', b => { b.events[0].when = ''; b.events[0].effects = ''; b.schedules[0].script = ''; });
accept('label on a call and escaped quote', b => { b.events[0].script = 'first: move npc.serin dest.snack / say npc.serin "a \\"quoted\\" word / and slash"'; b.events[0].forks = {}; });
assert(schemaErrors(saveSchema, {...saved, saveVersion: 2}).length, 'Future save version must fail');
negative.push({name: 'future save version', expectedCode: 'SCHEMA', status: 'pass'});
assert(schemaErrors(saveSchema, {...saved, activeCall: 'event.snack#2'}).length, 'Unsafe PC field must fail');
negative.push({name: 'unsafe call-PC save', expectedCode: 'SCHEMA', status: 'pass'});

// Check local Markdown links. This is structural verification, not rendered UI verification.
const docs = fs.readdirSync(root).filter(x => x.endsWith('.md'));
let links = 0;
for (const file of docs) {
  const body = fs.readFileSync(path.join(root, file), 'utf8');
  for (const match of body.matchAll(/\]\(([^)]+)\)/g)) {
    const target = match[1]; if (/^(https?:|#)/.test(target)) continue;
    // The report is generated at the end of this run; all other targets must exist now.
    if (target !== 'validation/report.json') assert(fs.existsSync(path.resolve(root, target.split('#')[0])), `${file}: broken link ${target}`);
    links++;
  }
}
const source = path.resolve(root, '../마법학교_최초기획안.md');
const sourceHash = crypto.createHash('sha256').update(fs.readFileSync(source)).digest('hex');
assert.equal(sourceHash.toUpperCase(), 'CAAACF9F4398FE7BBA5AFF54A777BDACCD29205C16C211DB57D44EB06CC8EDBA');
const parserSelfTest = require('node:child_process').spawnSync(process.execPath, [path.join(__dirname, 'parse-calls.cjs')], {encoding: 'utf8'});
assert.equal(parserSelfTest.status, 0, 'parse-calls self-test failed: ' + parserSelfTest.stderr);
const report = {
  scope: 'Documentation validation only; no Unity/runtime gameplay execution',
  schemaDialect: '2020-12; bundled checker evaluates only keywords used in supplied schemas',
  format: 'function-call columns (full-design/11-function-syntax.md); one event = one row',
  contentVersion: bundle.contentVersion, catalogHash: hash, sourceMarkdownHash: sourceHash,
  counts: {maps: bundle.maps.length, npcs: bundle.npcs.length, events: bundle.events.length, schedules: bundle.schedules.length, forks: bundle.events.reduce((n, e) => n + Object.keys(e.forks || {}).length, 0),
    parsedCalls: result.calls, staticMoveRoutes: result.routes.length, markdownDocuments: docs.length, localLinks: links, negativeCases: negative.length},
  positiveVariants: variants,
  positive: {schema: 'pass', callParsing: 'pass', functionRegistry: 'pass', references: 'pass', staticReachability: 'pass', handleContracts: 'pass', saveExample: 'pass', expectedTraceReferences: 'pass', documentLinks: 'pass', sourceUnchanged: 'pass', parserSelfTest: 'pass'},
  routes: result.routes, negative,
  notExecuted: ['Unity scene or C# execution', 'Dynamic collision and replanning', 'Actual disk crash recovery', 'NPC leases at runtime', '720 quest order model', 'Gameplay balance or visual UI inspection', 'Full-design function registry (perform, domain conditions/effects): only the v1 subset is registered here']
};
fs.mkdirSync(path.join(root, 'validation'), {recursive: true});
fs.writeFileSync(path.join(root, 'validation/report.json'), JSON.stringify(report, null, 2) + '\n');
console.log(JSON.stringify(report, null, 2));
