'use strict';
// MagicCampus stage 4-4 FirstConnection fixture generator. Node stdlib only; writes nothing outside --out.
// Reads the untouched design examples and schemas, derives real JSON files under ./generated.
// Usage: node build-fixtures.cjs [--check] [--root dir] [--design dir] [--out dir]
//        [--functions file] [--runner-schema file] [--catalog-schema file] [--save-schema file]
const fs = require('fs');
const path = require('path');
const crypto = require('crypto');

function parseArgs(list) {
  const r = {};
  for (let i = 0; i < list.length; i++) {
    const a = list[i];
    if (a.indexOf('--') !== 0) throw new Error('unexpected argument ' + a);
    const k = a.slice(2);
    if (k === 'check') { r.check = true; continue; }
    if (i + 1 >= list.length) throw new Error('missing value for ' + a);
    r[k] = list[++i];
  }
  return r;
}

const args = parseArgs(process.argv.slice(2));
const HERE = __dirname;
const ROOT = path.resolve(args.root || path.join(HERE, '..', '..', '..'));
const DESIGN = path.resolve(args.design || path.join(ROOT, 'design', 'systems', 'full-design'));
const EXAMPLES = path.join(DESIGN, 'examples');
const SCHEMAS = path.join(DESIGN, 'schemas');
const OUT = path.resolve(args.out || path.join(HERE, 'generated'));
if (OUT !== path.join(HERE, 'generated') && !OUT.startsWith(HERE + path.sep)) throw new Error('output must stay inside this fixture directory');

// Fixture policy. These names live only in this generator and in generated data, never in production code.
const PLAYABLE_MAPS = ['map.courtyard', 'map.lounge'];
const ACTIVE_NPC = 'npc.serin';
const SECOND_NPC = 'npc.mira';
const EV_SNACK = 'event.snack';
const EV_LATER = 'event.snack_later';
const EV_MIRA = 'event.mira_snack';
const ACCEPT_MINUTE = 420;
const PRIORITY_BY_CATEGORY = { resolution: 400, relationship: 300, festival: 200, general: 100, schedule: 10 };
const q = (s) => '"' + s + '"';

const sha256 = (buf) => crypto.createHash('sha256').update(buf).digest('hex');
const clone = (v) => JSON.parse(JSON.stringify(v));
const deepEq = (a, b) => JSON.stringify(a) === JSON.stringify(b);
const readJson = (buf) => JSON.parse(buf.toString('utf8'));

function byId(list, id, what) {
  const f = list.find((x) => x.id === id);
  if (!f) throw new Error('missing ' + what + ' ' + id);
  return f;
}

// ---------------------------------------------------------------- schema access
function loadSchemaFile(file) {
  const p = path.resolve(file);
  const buf = fs.readFileSync(p);
  return { name: path.basename(p), buf, doc: readJson(buf) };
}

function findSchema(predicate) {
  for (const name of fs.readdirSync(SCHEMAS).sort()) {
    if (!name.endsWith('.json')) continue;
    const buf = fs.readFileSync(path.join(SCHEMAS, name));
    let doc;
    try { doc = readJson(buf); } catch (e) { continue; }
    if (predicate(doc, name)) return { name, buf, doc };
  }
  return null;
}

const idOf = (d) => (d && typeof d.$id === 'string') ? d.$id : '';

function resolveSchema(argName, predicate, required) {
  if (args[argName]) return loadSchemaFile(args[argName]);
  const found = findSchema(predicate);
  if (!found && required) throw new Error('cannot locate the ' + argName + ' input under ' + SCHEMAS + '; pass --' + argName);
  return found;
}

const functionsSchema = resolveSchema('functions', (d, n) => n === 'functions.json', true);
const runnerSchema = resolveSchema('runner-schema', (d) => idOf(d) === 'urn:magic-campus:runner:2', true);
const catalogSchema = resolveSchema('catalog-schema', (d, n) => idOf(d).indexOf('urn:magic-campus:catalog') === 0 || n.indexOf('catalog') >= 0, true);
const saveSchema = resolveSchema('save-schema', (d, n) => idOf(d).indexOf('save') >= 0 || n.indexOf('save') >= 0, true);

// Minimal draft 2020-12 subset: only the keywords the runner schema uses (plus local $ref).
function typeOk(type, v) {
  if (Array.isArray(type)) return type.some((t) => typeOk(t, v));
  switch (type) {
    case 'integer': return typeof v === 'number' && Number.isInteger(v);
    case 'number': return typeof v === 'number';
    case 'string': return typeof v === 'string';
    case 'boolean': return typeof v === 'boolean';
    case 'null': return v === null;
    case 'array': return Array.isArray(v);
    case 'object': return v !== null && typeof v === 'object' && !Array.isArray(v);
    default: return true;
  }
}

function deref(doc, node) {
  let cur = node;
  while (cur && typeof cur.$ref === 'string') {
    if (cur.$ref.indexOf('#/') !== 0) throw new Error('unsupported $ref ' + cur.$ref);
    let t = doc;
    for (const seg of cur.$ref.slice(2).split('/')) t = t[seg.split('~1').join('/').split('~0').join('~')];
    cur = t;
  }
  return cur;
}

function validate(doc, schema, value, at, errs) {
  schema = deref(doc, schema);
  if (schema === true || schema === undefined || schema === null) return;
  if (Object.prototype.hasOwnProperty.call(schema, 'const') && !deepEq(schema.const, value)) errs.push(at + ' const');
  if (schema.enum && !schema.enum.some((e) => deepEq(e, value))) errs.push(at + ' enum');
  if (schema.oneOf) {
    let n = 0;
    for (const s of schema.oneOf) { const e = []; validate(doc, s, value, at, e); if (e.length === 0) n++; }
    if (n !== 1) errs.push(at + ' oneOf matched ' + n);
  }
  if (schema.type !== undefined && !typeOk(schema.type, value)) { errs.push(at + ' type'); return; }
  if (typeof value === 'string') {
    if (schema.minLength !== undefined && value.length < schema.minLength) errs.push(at + ' minLength');
    if (schema.pattern && !new RegExp(schema.pattern, 'u').test(value)) errs.push(at + ' pattern');
  } else if (typeof value === 'number') {
    if (schema.minimum !== undefined && value < schema.minimum) errs.push(at + ' minimum');
    if (schema.exclusiveMinimum !== undefined && value <= schema.exclusiveMinimum) errs.push(at + ' exclusiveMinimum');
    if (schema.maximum !== undefined && value > schema.maximum) errs.push(at + ' maximum');
  } else if (Array.isArray(value)) {
    if (schema.minItems !== undefined && value.length < schema.minItems) errs.push(at + ' minItems');
    if (schema.maxItems !== undefined && value.length > schema.maxItems) errs.push(at + ' maxItems');
    if (schema.uniqueItems && new Set(value.map((x) => JSON.stringify(x))).size !== value.length) errs.push(at + ' uniqueItems');
    if (schema.items) value.forEach((x, i) => validate(doc, schema.items, x, at + '/' + i, errs));
  } else if (value !== null && typeof value === 'object') {
    for (const k of schema.required || []) if (!Object.prototype.hasOwnProperty.call(value, k)) errs.push(at + ' missing ' + k);
    const props = schema.properties || {};
    for (const k of Object.keys(value)) {
      if (Object.prototype.hasOwnProperty.call(props, k)) validate(doc, props[k], value[k], at + '/' + k, errs);
      else if (schema.additionalProperties === false) errs.push(at + ' unexpected ' + k);
      else if (schema.additionalProperties && typeof schema.additionalProperties === 'object') validate(doc, schema.additionalProperties, value[k], at + '/' + k, errs);
    }
  }
}

function schemaCheck(label, runner) {
  const errs = [];
  validate(runnerSchema.doc, runnerSchema.doc, runner, '#', errs);
  if (errs.length) throw new Error(label + ' violates the runner schema: ' + errs.slice(0, 5).join('; '));
}

// ---------------------------------------------------------------- call-text helpers (generator side only)
function splitCalls(text) {
  const parts = [];
  let cur = '';
  let inQuote = false;
  for (let i = 0; i < text.length; i++) {
    const c = text[i];
    if (inQuote) {
      cur += c;
      if (c === '\\' && i + 1 < text.length) cur += text[++i];
      else if (c === '"') inQuote = false;
    } else if (c === '"') { inQuote = true; cur += c; }
    else if (c === '/') { parts.push(cur.trim()); cur = ''; }
    else cur += c;
  }
  parts.push(cur.trim());
  return parts.filter((p) => p.length > 0);
}

function tokens(call) {
  const out = [];
  let cur = '';
  let inQuote = false;
  for (let i = 0; i < call.length; i++) {
    const c = call[i];
    if (inQuote) {
      cur += c;
      if (c === '\\' && i + 1 < call.length) cur += call[++i];
      else if (c === '"') inQuote = false;
    } else if (c === '"') { inQuote = true; cur += c; }
    else if (c === ' ') { if (cur) out.push(cur); cur = ''; }
    else cur += c;
  }
  if (cur) out.push(cur);
  return out;
}

const tokensList = (text) => splitCalls(text).map(tokens);

function replaceCall(script, index, text) {
  const parts = splitCalls(script);
  parts[index - 1] = text;
  return parts.join(' / ');
}

function firstMove(script) {
  for (const t of tokensList(script)) if (t[0] === 'move' && t.length >= 3) return { actor: t[1], destination: t[2] };
  return null;
}

// ---------------------------------------------------------------- sources
const catalogBytes = fs.readFileSync(path.join(EXAMPLES, 'full-catalog.json'));
const srcRunner = readJson(fs.readFileSync(path.join(EXAMPLES, 'runner-bundle.json')));
const srcSaveBytes = fs.readFileSync(path.join(EXAMPLES, 'safe-save.json'));
const srcSave = readJson(srcSaveBytes);
const catalogDoc = readJson(catalogBytes);
const catalogHash = sha256(catalogBytes);

if (srcRunner.contentVersion !== srcSave.contentVersion) throw new Error('runner and save contentVersion differ');
if (typeof catalogDoc.contentVersion === 'string' && catalogDoc.contentVersion !== srcRunner.contentVersion) throw new Error('catalog contentVersion differs from the runner');
if (Object.keys(srcSave).length !== 45) throw new Error('the source save must keep its 45 fields');

function savedPosition(save, actorId) {
  if (actorId === 'player') return { mapId: save.player.mapId, x: save.player.x, y: save.player.y };
  const p = save.npcPositions.find((n) => n.npcId === actorId);
  if (!p) throw new Error('no saved position for ' + actorId);
  return { mapId: p.mapId, x: p.x, y: p.y };
}

function pointerOfActor(save, id) {
  if (id === 'player') return '/player';
  const i = save.npcPositions.findIndex((n) => n.npcId === id);
  if (i < 0) throw new Error('no saved position for ' + id);
  return '/npcPositions/' + i;
}

// ---------------------------------------------------------------- derived runner bundles
function buildBaseRunner() {
  const r = clone(srcRunner);
  const serin = clone(byId(srcRunner.npcs, ACTIVE_NPC, 'npc'));
  serin.position = savedPosition(srcSave, ACTIVE_NPC);
  serin.home = 'dest.serin_home';
  byId(r.destinations, serin.home, 'destination');
  r.npcs = [serin];
  const sch = clone(byId(srcRunner.schedules, 'schedule.serin', 'schedule'));
  sch.script = '420 home / 720 dest.notice / 1080 home';
  r.schedules = [sch];
  r.variables.push({ id: 'memory.snack_later_done', type: 'bool', initial: false });

  const snack = clone(byId(srcRunner.events, EV_SNACK, 'event'));
  if (snack.staging !== 'reserved' || snack.repeatPolicy !== 'once' || snack.executionMode !== 'scene') throw new Error('source event.snack no longer has the expected shape');
  delete snack.completionKey;
  snack.when = 'eq story.intro_done true';
  snack.costMinutes = 10;
  snack.resources = [ACTIVE_NPC, 'space.map.courtyard', 'space.map.lounge', 'input.player', 'camera.main'];
  snack.effects = 'set memory.snack_done true / currency.add 20';

  const later = clone(snack);
  later.id = EV_LATER;
  later.category = 'general';
  later.trigger = 'interact npc.serin ' + q('나중에 간식 모임') + ' 1.5';
  later.script = 'move npc.serin dest.snack / say npc.serin ' + q('이번에는 차 한 잔만 가볍게 할까?') + ' / choice ' + q('얼마나 할까?') + ' quick ' + q('금방 끝내자') + ' long ' + q('천천히 하자');
  later.forks = { quick: 'say npc.serin ' + q('좋아, 금방 끝내자.'), long: 'say npc.serin ' + q('좋아, 천천히 이야기하자.') };
  later.effects = 'set memory.snack_later_done true / currency.add 5';
  r.events = [snack, later];
  return r;
}

function buildDestMoved(base) {
  const r = clone(base);
  const d = byId(r.destinations, 'dest.snack', 'destination');
  d.position = { mapId: d.position.mapId, x: 4.5, y: 0.5 };
  return r;
}

function buildMira(base) {
  const r = clone(base);
  const mira = clone(byId(srcRunner.npcs, SECOND_NPC, 'npc'));
  mira.position = savedPosition(srcSave, SECOND_NPC);
  mira.home = 'dest.mira_home';
  byId(r.destinations, mira.home, 'destination');
  r.npcs.push(mira);
  const sch = clone(byId(srcRunner.schedules, 'schedule.mira', 'schedule'));
  sch.script = '420 home / 1080 dest.stage';
  r.schedules.push(sch);
  r.variables.push({ id: 'memory.mira_done', type: 'bool', initial: false });
  const ev = clone(byId(base.events, EV_SNACK, 'event'));
  ev.id = EV_MIRA;
  ev.trigger = 'interact npc.mira ' + q('미라와 무대 준비') + ' 1.5';
  ev.resources = [SECOND_NPC, 'space.map.courtyard', 'space.map.lounge', 'input.player', 'camera.main'];
  ev.script = 'move npc.mira dest.stage / say npc.mira ' + q('무대 소품을 같이 확인해 줄래?') + ' / choice ' + q('무엇부터 볼까?') + ' stage ' + q('무대부터 보자') + ' props ' + q('소품부터 보자');
  ev.forks = { stage: 'say npc.mira ' + q('좋아, 무대 위치부터 맞춰 보자.'), props: 'say npc.mira ' + q('좋아, 소품 상자부터 열어 보자.') };
  ev.effects = 'set memory.mira_done true / currency.add 30';
  r.events.push(ev);
  return r;
}

// ---------------------------------------------------------------- saves
function appointmentItemSchema() {
  if (!saveSchema || !saveSchema.doc.properties || !saveSchema.doc.properties.appointments) return null;
  const arr = deref(saveSchema.doc, saveSchema.doc.properties.appointments);
  return arr && arr.items ? deref(saveSchema.doc, arr.items) : null;
}

function fitsSchema(ps, v) {
  const errs = [];
  validate(saveSchema.doc, ps, v, '#', errs);
  return errs.length === 0;
}

function appointmentRow(spec) {
  const label = spec.status;
  const cand = { id: spec.eventId, eventId: spec.eventId, reservationId: spec.eventId, status: label, state: label, acceptedAt: spec.acceptedAt, reservedAt: spec.reservedAt, sequence: spec.sequence, priority: spec.priority };
  const item = appointmentItemSchema();
  if (!item || !item.properties) throw new Error('original closed appointment schema is required');
  const row = {};
  for (const key of Object.keys(item.properties)) {
    const ps = deref(saveSchema.doc, item.properties[key]);
    let v = cand[key];
    if ((key === 'status' || key === 'state') && ps && Array.isArray(ps.enum)) {
      const hit = ps.enum.find((e) => typeof e === 'string' && e.toLowerCase() === label.toLowerCase());
      if (hit !== undefined) v = hit;
    }
    if (v !== undefined && fitsSchema(ps, v)) row[key] = v;
    else if ((item.required || []).indexOf(key) >= 0) throw new Error('cannot fill required appointment field ' + key + ' from the save schema');
  }
  return row;
}

function appointmentsFor(runner, eventIds, acceptedIds) {
  return eventIds.map((id, i) => {
    const ev = byId(runner.events, id, 'event');
    const prio = PRIORITY_BY_CATEGORY[ev.category];
    if (prio === undefined) throw new Error(id + ': no candidate priority for category ' + ev.category);
    const accepted = acceptedIds.indexOf(id) >= 0;
    return appointmentRow({ eventId: id, status: accepted ? 'Accepted' : 'Offered', acceptedAt: accepted ? ACCEPT_MINUTE : 0, reservedAt: ACCEPT_MINUTE, sequence: i, priority: prio });
  });
}

function buildSave(runner, appointments) {
  const s = clone(srcSave);
  s.catalogHash = catalogHash;
  s.appointments = appointments;
  for (const v of runner.variables) {
    if (v.id.indexOf('clock.') === 0) continue;
    const f = s.flags.find((x) => x.id === v.id);
    if (f) f.value = v.initial; else s.flags.push({ id: v.id, value: v.initial });
  }
  if (!deepEq(Object.keys(s).sort(), Object.keys(srcSave).sort())) throw new Error('derived save changed its field set');
  return s;
}

// ---------------------------------------------------------------- consistency checks
function cellReachable(map, from, to) {
  const blocked = new Set(map.blocked.map((b) => b[0] + ',' + b[1]));
  const sx = Math.floor(from.x), sy = Math.floor(from.y), tx = Math.floor(to.x), ty = Math.floor(to.y);
  if (blocked.has(tx + ',' + ty)) return false;
  const seen = new Set([sx + ',' + sy]);
  const queue = [[sx, sy]];
  while (queue.length) {
    const cell = queue.shift();
    if (cell[0] === tx && cell[1] === ty) return true;
    for (const d of [[1, 0], [-1, 0], [0, 1], [0, -1]]) {
      const nx = cell[0] + d[0], ny = cell[1] + d[1], k = nx + ',' + ny;
      if (nx < 0 || ny < 0 || nx >= map.width || ny >= map.height || blocked.has(k) || seen.has(k)) continue;
      seen.add(k);
      queue.push([nx, ny]);
    }
  }
  return false;
}

function checkRunner(r, label) {
  const fail = (m) => { throw new Error(label + ': ' + m); };
  const dests = new Map(r.destinations.map((d) => [d.id, d]));
  const npcs = new Set(r.npcs.map((n) => n.id));
  const maps = new Map(r.maps.map((m) => [m.id, m]));
  const vars = new Map(r.variables.map((v) => [v.id, v]));
  if (dests.size !== r.destinations.length) fail('duplicate destination id');
  if (vars.size !== r.variables.length) fail('duplicate variable id');
  if (new Set(r.events.map((e) => e.id)).size !== r.events.length) fail('duplicate event id');
  for (const d of r.destinations) {
    const m = maps.get(d.position.mapId);
    if (!m) fail(d.id + ' is on an unknown map');
    if (d.position.x >= m.width || d.position.y >= m.height) fail(d.id + ' is outside its map');
    if (m.blocked.some((b) => b[0] === Math.floor(d.position.x) && b[1] === Math.floor(d.position.y))) fail(d.id + ' is on a blocked cell');
  }
  for (const n of r.npcs) {
    if (!dests.has(n.home)) fail(n.id + ' home is unknown');
    if (!maps.has(n.position.mapId)) fail(n.id + ' starts on an unknown map');
  }
  for (const s of r.schedules) {
    if (!npcs.has(s.npcId)) fail(s.id + ' targets an inactive npc');
    let last = -1;
    for (const t of tokensList(s.script)) {
      const minute = Number(t[0]);
      if (!Number.isInteger(minute) || minute <= last || minute > 1439) fail(s.id + ' minutes must ascend inside 0..1439');
      last = minute;
      if (t[1] !== 'home' && !dests.has(t[1])) fail(s.id + ' unknown destination ' + t[1]);
    }
  }
  const checkCalls = (text, where, forks, used) => {
    for (const t of tokensList(text)) {
      if (t[0] === 'move') {
        if (!npcs.has(t[1])) fail(where + ' move actor ' + t[1]);
        if (!dests.has(t[2])) fail(where + ' move destination ' + t[2]);
      } else if (t[0] === 'say') {
        if (!npcs.has(t[1])) fail(where + ' say speaker ' + t[1]);
      } else if (t[0] === 'choice') {
        for (let i = 2; i + 1 < t.length; i += 2) {
          if (!forks[t[i]]) fail(where + ' choice fork ' + t[i]);
          used.add(t[i]);
        }
      } else fail(where + ' unexpected command ' + t[0]);
    }
  };
  for (const e of r.events) {
    const forks = e.forks || {};
    const used = new Set();
    const trig = tokens(e.trigger);
    if (trig[0] === 'interact' && !npcs.has(trig[1])) fail(e.id + ' trigger target ' + trig[1]);
    for (const res of e.resources) {
      if (res.indexOf('npc.') === 0 && !npcs.has(res)) fail(e.id + ' resource ' + res);
      if (res.indexOf('space.map.') === 0 && !maps.has('map.' + res.slice('space.map.'.length))) fail(e.id + ' resource ' + res);
    }
    for (const t of tokensList(e.when)) {
      if (t[0] !== 'eq' || !vars.has(t[1])) fail(e.id + ' when must use eq on a declared variable');
    }
    checkCalls(e.script, e.id, forks, used);
    for (const name of Object.keys(forks)) checkCalls(forks[name], e.id + ':' + name, {}, new Set());
    for (const name of Object.keys(forks)) if (!used.has(name)) fail(e.id + ' fork ' + name + ' is never used');
    for (const t of tokensList(e.effects)) {
      if (t[0] === 'set') {
        const v = vars.get(t[1]);
        if (!v || v.id.indexOf('clock.') === 0) fail(e.id + ' set target ' + t[1]);
        const okValue = v.type === 'bool' ? (t[2] === 'true' || t[2] === 'false') : Number.isInteger(Number(t[2]));
        if (!okValue) fail(e.id + ' set value ' + t[2]);
      } else if (t[0] === 'currency.add') {
        const n = Number(t[1]);
        if (!Number.isInteger(n) || n < 1 || n > 999999) fail(e.id + ' currency.add amount');
      } else fail(e.id + ' effect ' + t[0] + ' is outside the minimal registered set');
    }
  }
}

function incomingPortal(r, targetMapId) {
  const found = [];
  for (const m of r.maps) {
    if (m.id === targetMapId) continue;
    for (const p of m.portals) if (p.to.mapId === targetMapId) found.push({ portal: p, fromMapId: m.id });
  }
  found.sort((a, b) => (a.portal.open === b.portal.open ? 0 : a.portal.open ? -1 : 1) || (a.portal.id < b.portal.id ? -1 : a.portal.id > b.portal.id ? 1 : 0));
  if (!found.length) throw new Error('no portal leads to ' + targetMapId);
  return found[0];
}

function stagingRecipes(r, save) {
  const out = [];
  for (const e of r.events) {
    if (e.staging !== 'reserved') continue;
    const mv = firstMove(e.script);
    if (!mv) throw new Error(e.id + ': reserved staging needs a first move to derive the target map');
    const dest = byId(r.destinations, mv.destination, 'destination');
    const via = incomingPortal(r, dest.position.mapId);
    const arrival = { mapId: via.portal.to.mapId, x: via.portal.to.x, y: via.portal.to.y };
    const join = e.resources.filter((x) => x.indexOf('npc.') === 0).map((x) => ({ actorId: x, entry: savedPosition(save, x), joinAt: arrival }));
    if (e.resources.indexOf('input.player') >= 0) join.push({ actorId: 'player', entry: savedPosition(save, 'player'), joinAt: arrival });
    out.push({
      eventId: e.id,
      policy: 'hidden-join-at-incoming-portal-arrival',
      firstMove: { actorId: mv.actor, destinationId: mv.destination, targetMapId: dest.position.mapId },
      incomingPortalId: via.portal.id,
      incomingFromMapId: via.fromMapId,
      arrival,
      join,
      walk: 'leased actors are joined offscreen at arrival during the hidden transition, then the move walks from arrival to the actual destination; the destination is never a warp target'
    });
  }
  return out;
}

function assertReachable(r, save, label) {
  for (const rec of stagingRecipes(r, save)) {
    const dest = byId(r.destinations, rec.firstMove.destinationId, 'destination');
    const map = byId(r.maps, dest.position.mapId, 'map');
    if (!cellReachable(map, rec.arrival, dest.position)) throw new Error(label + ': ' + rec.eventId + ' destination is not reachable from the staging arrival');
  }
}

function validateValid(label, runner, save) {
  schemaCheck(label, runner);
  const saveErrors = []; validate(saveSchema.doc, saveSchema.doc, save, '#', saveErrors);
  if (saveErrors.length) throw new Error(label + ' save violates original schema: ' + saveErrors.slice(0, 5).join('; '));
  checkRunner(runner, label);
  assertReachable(runner, save, label);
}

// ---------------------------------------------------------------- build data
const baseRunner = buildBaseRunner();
const destMovedRunner = buildDestMoved(baseRunner);
const miraRunner = buildMira(baseRunner);

const baseIds = [EV_SNACK, EV_LATER];
const miraIds = [EV_SNACK, EV_LATER, EV_MIRA];
const saveOffered = buildSave(baseRunner, appointmentsFor(baseRunner, baseIds, []));
const saveAccepted = buildSave(baseRunner, appointmentsFor(baseRunner, baseIds, [EV_SNACK]));
const saveAcceptedAll = buildSave(baseRunner, appointmentsFor(baseRunner, baseIds, baseIds));
const miraOffered = buildSave(miraRunner, appointmentsFor(miraRunner, miraIds, []));
const miraAccepted = buildSave(miraRunner, appointmentsFor(miraRunner, miraIds, [EV_MIRA]));

validateValid('base', baseRunner, saveOffered);
validateValid('dest-moved', destMovedRunner, saveOffered);
validateValid('a10-mira', miraRunner, miraOffered);

const unregisteredRunner = clone(baseRunner);
byId(unregisteredRunner.events, EV_SNACK, 'event').effects += ' / spell.learn spell.water';
schemaCheck('unregistered-effect', unregisteredRunner);

const INVALID_EDITS = [
  { id: 'say-arg-count', call: 2, text: 'say npc.serin', codes: ['ARG_COUNT'], anyOf: false },
  { id: 'wait-arg-type', call: 1, text: 'wait notanumber', codes: ['ARG_TYPE'], anyOf: false },
  { id: 'unknown-function', call: 1, text: 'teleport npc.serin dest.snack', codes: ['UNKNOWN_FUNCTION'], anyOf: false },
  { id: 'move-ref-not-found', call: 1, text: 'move npc.serin dest.nowhere', codes: ['REF_NOT_FOUND'], anyOf: false },
  { id: 'move-ref-kind', call: 1, text: 'move dest.snack dest.snack', codes: ['REF_KIND', 'REF_NOT_FOUND'], anyOf: true }
];
const invalidRunners = INVALID_EDITS.map((edit) => {
  const r = clone(baseRunner);
  const ev = byId(r.events, EV_SNACK, 'event');
  ev.script = replaceCall(ev.script, edit.call, edit.text);
  schemaCheck('invalid/' + edit.id, r);
  return { edit, runner: r };
});

// ---------------------------------------------------------------- expectations
const flagValue = (text) => (text === 'true' ? true : text === 'false' ? false : parseInt(text, 10));

function successExpectation(runner, save, eventIds) {
  const recipes = stagingRecipes(runner, save);
  const near = new Map();
  const flags = new Map();
  let wallet = save.wallet;
  let minute = save.clock.absoluteMinute;
  let lastArrival = null;
  for (const id of eventIds) {
    const ev = byId(runner.events, id, 'event');
    const rec = recipes.find((x) => x.eventId === id);
    if (!rec) throw new Error(id + ': expectations need reserved staging');
    minute += ev.costMinutes;
    for (const t of tokensList(ev.effects)) {
      if (t[0] === 'currency.add') wallet += parseInt(t[1], 10);
      else if (t[0] === 'set') flags.set(t[1], flagValue(t[2]));
      else throw new Error(id + ': effect ' + t[0] + ' is outside the minimal set');
    }
    const dest = byId(runner.destinations, rec.firstMove.destinationId, 'destination');
    near.set(rec.firstMove.actorId, { pointer: pointerOfActor(save, rec.firstMove.actorId), mapId: dest.position.mapId, x: dest.position.x, y: dest.position.y, tolerance: dest.radius });
    lastArrival = rec.arrival;
  }
  const set = { '/wallet': wallet, '/clock/absoluteMinute': minute };
  for (const entry of flags) {
    const i = save.flags.findIndex((f) => f.id === entry[0]);
    if (i < 0) throw new Error('no saved flag ' + entry[0]);
    set['/flags/' + i + '/value'] = entry[1];
  }
  const nearList = Array.from(near.values());
  nearList.push({ pointer: '/player', mapId: lastArrival.mapId, x: lastArrival.x, y: lastArrival.y, tolerance: 0.001 });
  return {
    status: 'Succeeded',
    set,
    near: nearList,
    onceLedger: { completedAdds: eventIds.map((id) => byId(runner.events, id, 'event').completionKey || id), rewardLedgerAddsCount: eventIds.length },
    appointmentsRemoved: eventIds,
    timeCostMinutes: minute - save.clock.absoluteMinute,
    rewardCrowns: wallet - save.wallet,
    appliedOnce: true,
    unchangedElsewhere: true
  };
}

const restoredEntry = (file) => ({ stateEqualsFile: file, ignorePointers: ['/generation', '/safePoint'], rewardCrowns: 0, timeCostMinutes: 0, reservationStatus: 'Accepted' });

function candidateOrder(runner, ids) {
  const rows = ids.map((id, i) => ({ id, prio: PRIORITY_BY_CATEGORY[byId(runner.events, id, 'event').category], seq: i }));
  rows.sort((a, b) => b.prio - a.prio || a.seq - b.seq || (a.id < b.id ? -1 : a.id > b.id ? 1 : 0));
  return rows.map((x) => x.id);
}

// ---------------------------------------------------------------- injections (runtime hooks; logical definitions stay valid)
const snackDest = byId(baseRunner.destinations, 'dest.snack', 'destination');
const toLounge = incomingPortal(baseRunner, snackDest.position.mapId);
const wrongMap = PLAYABLE_MAPS.find((m) => m !== snackDest.position.mapId);
const injection = (id, hooks) => ({
  formatVersion: 1,
  id,
  baseRunner: 'base/runner-bundle.json',
  note: 'Runtime hook only. Logical maps, portals and destinations stay untouched so the catalog still compiles.',
  hooks
});
const injections = [
  injection('closed-portal', [{ kind: 'portalOpen', portalId: toLounge.portal.id, open: false }]),
  injection('blocked-destination', [{ kind: 'blockedCell', mapId: snackDest.position.mapId, x: Math.floor(snackDest.position.x), y: Math.floor(snackDest.position.y) }]),
  injection('map-load-failure', [{ kind: 'mapLoadFailure', mapId: snackDest.position.mapId, code: 'MAP_LOAD_FAILED' }]),
  injection('wrong-arrival-map', [{ kind: 'actualPositionOverride', actorId: ACTIVE_NPC, when: 'move-final-position', position: { mapId: wrongMap, x: snackDest.position.x, y: snackDest.position.y } }])
];

// ---------------------------------------------------------------- reproduction manifest
const BASE_FILES = {
  catalog: 'base/full-catalog.json',
  functions: 'base/schemas/' + functionsSchema.name,
  catalogSchema: 'base/schemas/' + catalogSchema.name,
  runnerSchema: 'base/schemas/' + runnerSchema.name,
  runner: 'base/runner-bundle.json'
};
const files = (over) => Object.assign({}, BASE_FILES, over);

const accept = (eventId, seq) => ({ op: 'accept', eventId, acceptedAt: ACCEPT_MINUTE, inputSequence: seq });
const start = (eventId, seq, tx, extra) => Object.assign({ op: 'start', eventId, inputSequence: seq, transactionId: tx, ownerToken: 'owner.player' }, extra || {});
const confirm = (seq) => ({ op: 'confirm', target: 'say', inputSequence: seq });
const choose = (key, seq) => ({ op: 'choice', key, inputSequence: seq });
const saveOp = (seq) => ({ op: 'save', inputSequence: seq });
const reload = () => ({ op: 'reload' });
const kill = (phase) => ({ op: 'killphase', phase });

const SCENE_ORDER = ['accept-persisted', 'entry-position-projection', 'recovery-capture-entry', 'scene-entry-notified', 'entry-save-succeeded', 'staging', 'script', 'condition-recheck', 'single-commit', 'scene-complete-notified'];
const scenarios = [];
const scenario = (id, title, initialFiles, inputs, expect) => scenarios.push({ id, title, initialFiles, inputs, expect });

scenario('A01', 'Accepted reservation runs to success with choice notes', files({ save: 'base/save-offered.json' }),
  [accept(EV_SNACK, 1), start(EV_SNACK, 2, 'tx.a01.start'), confirm(3), choose('notes', 4), saveOp(5)],
  Object.assign({ sceneOrder: SCENE_ORDER, afterSave: 'SaveLatestSafe succeeds' }, successExpectation(baseRunner, saveOffered, [EV_SNACK])));

scenario('A02', 'Same flow with choice tea', files({ save: 'base/save-offered.json' }),
  [accept(EV_SNACK, 1), start(EV_SNACK, 2, 'tx.a02.start'), confirm(3), choose('tea', 4), saveOp(5)],
  Object.assign({ sceneOrder: SCENE_ORDER }, successExpectation(baseRunner, saveOffered, [EV_SNACK])));

scenario('A03', 'Same-NPC second invitation loses on priority, stays Accepted, then succeeds later', files({ save: 'base/save-accepted-all.json' }),
  [
    start(EV_SNACK, 1, 'tx.a03.first'),
    start(EV_LATER, 2, 'tx.a03.second', { expectAfter: { status: 'Failed', code: 'RESOURCE_BUSY', diagnosticPerBusyResource: true, reservationStatus: 'Accepted', timeCostMinutes: 0, rewardCrowns: 0 } }),
    confirm(3), choose('notes', 4),
    start(EV_LATER, 5, 'tx.a03.retry'), confirm(6), choose('quick', 7)
  ],
  Object.assign({ candidateOrderBefore: candidateOrder(baseRunner, baseIds) }, successExpectation(baseRunner, saveAcceptedAll, baseIds)));

const killExpect = restoredEntry('base/save-accepted.json');
scenario('A04', 'Hard kill during move restores the entry with the reservation Accepted', files({ save: 'base/save-offered.json' }),
  [accept(EV_SNACK, 1), start(EV_SNACK, 2, 'tx.a04.start'), kill('move'), reload()], killExpect);
scenario('A05', 'Hard kill during say restores the entry with the reservation Accepted', files({ save: 'base/save-offered.json' }),
  [accept(EV_SNACK, 1), start(EV_SNACK, 2, 'tx.a05.start'), kill('say'), reload()], killExpect);
scenario('A06', 'Hard kill during choice restores the entry with the reservation Accepted', files({ save: 'base/save-offered.json' }),
  [accept(EV_SNACK, 1), start(EV_SNACK, 2, 'tx.a06.start'), confirm(3), kill('choice'), reload()], killExpect);

scenario('A07', 'Duplicate start applies cost and reward once; a once event refuses a new start', files({ save: 'base/save-offered.json' }),
  [
    accept(EV_SNACK, 1), start(EV_SNACK, 2, 'tx.a07.start'), confirm(3), choose('notes', 4),
    start(EV_SNACK, 2, 'tx.a07.start', { expectAfter: { resultEqualsFirst: true, appliedAgain: false } }),
    start(EV_SNACK, 5, 'tx.a07.again', { expectAfter: { status: 'Failed', code: 'CONDITION_FALSE', note: 'assumed refusal code for a completed once event' } }),
    saveOp(6), reload()
  ],
  Object.assign({ afterReload: 'state equals the success state' }, successExpectation(baseRunner, saveOffered, [EV_SNACK])));

scenario('A08', 'Disk failure after the success commit keeps memory success and reports a separate save result', files({ save: 'base/save-offered.json' }),
  [accept(EV_SNACK, 1), start(EV_SNACK, 2, 'tx.a08.start'), confirm(3), { op: 'inject', fault: 'file-write-fail', at: 'after-success-commit' }, choose('notes', 4), reload()],
  {
    memoryAfterSuccess: successExpectation(baseRunner, saveOffered, [EV_SNACK]),
    saveResult: { status: 'Failed', reportedSeparatelyFromEvent: true, unsavedIndicator: true },
    afterReload: restoredEntry('base/save-accepted.json')
  });

const entrySerin = savedPosition(saveAccepted, ACTIVE_NPC);
const entryPlayer = savedPosition(saveAccepted, 'player');
const baseRecipe = stagingRecipes(baseRunner, saveAccepted).find((x) => x.eventId === EV_SNACK);
scenarios.push({
  id: 'A09',
  title: 'Cancel, duplicate cancel, and failure: MoveService last valid position versus Runner whole-scene restore',
  initialFiles: files({ save: 'base/save-accepted.json' }),
  cases: [
    {
      id: 'A09a',
      title: 'Cancel during choice, then a duplicate cancel',
      inputs: [start(EV_SNACK, 1, 'tx.a09a.start'), confirm(2), { op: 'cancel', reason: 'player-cancel', inputSequence: 3 }, { op: 'cancel', reason: 'player-cancel', inputSequence: 4 }],
      expect: Object.assign({ status: 'Cancelled', code: 'CANCELLED', terminalCount: 1, childrenTerminatedBeforeTerminal: true, leasesReleased: true }, restoredEntry('base/save-accepted.json'))
    },
    {
      id: 'A09c',
      title: 'Blocked destination injection: MoveService keeps the last valid position, the Runner restores the whole scene',
      injection: 'variants/injections/blocked-destination.json',
      inputs: [start(EV_SNACK, 1, 'tx.a09c.start')],
      expect: {
        moveService: { status: 'Failed', code: 'TARGET_BLOCKED', actorId: ACTIVE_NPC, positionAfter: baseRecipe.arrival, meaning: 'last valid position, no teleport and no restore' },
        runner: Object.assign({ status: 'Failed', code: 'TARGET_BLOCKED', actorPositions: { [ACTIVE_NPC]: entrySerin, player: entryPlayer } }, restoredEntry('base/save-accepted.json')),
        distinction: 'MoveService alone leaves the actor at the joined arrival point; only the Runner failure path restores the entry snapshot'
      }
    }
  ]
});

scenario('A10', 'Second active NPC variant: cloned event, own variable, flag and reward; catalog bytes unchanged', {
  catalog: BASE_FILES.catalog, functions: BASE_FILES.functions, catalogSchema: BASE_FILES.catalogSchema, runnerSchema: BASE_FILES.runnerSchema,
  runner: 'variants/a10-mira/runner-bundle.json', save: 'variants/a10-mira/save-offered.json'
},
  [accept(EV_MIRA, 1), start(EV_MIRA, 2, 'tx.a10.start'), confirm(3), choose('stage', 4)],
  Object.assign({ catalogSha256: catalogHash, originalNpcMetadataUnchanged: true, otherAppointmentsUntouched: [EV_SNACK, EV_LATER] }, successExpectation(miraRunner, miraOffered, [EV_MIRA])));

const moveFrom = (actorId, destinationId, token) => ({ op: 'moveService', actorId, destinationId, usePortals: true, timeoutSeconds: 30, ownerToken: token });
const runtimeVariants = [
  {
    id: 'RV01', name: 'closed-portal', injection: 'variants/injections/closed-portal.json', initialFiles: files({ save: 'base/save-offered.json' }),
    inputs: [moveFrom(ACTIVE_NPC, 'dest.snack', 'owner.rv01')],
    expect: { status: 'Failed', code: 'NO_ROUTE', positionUnchanged: savedPosition(saveOffered, ACTIVE_NPC) }
  },
  {
    id: 'RV02', name: 'blocked-destination', injection: 'variants/injections/blocked-destination.json', initialFiles: files({ save: 'base/save-offered.json' }),
    inputs: [{ op: 'place', actorId: ACTIVE_NPC, position: baseRecipe.arrival }, moveFrom(ACTIVE_NPC, 'dest.snack', 'owner.rv02')],
    expect: { status: 'Failed', code: 'TARGET_BLOCKED', positionUnchanged: baseRecipe.arrival }
  },
  {
    id: 'RV03', name: 'map-load-failure', injection: 'variants/injections/map-load-failure.json', initialFiles: files({ save: 'base/save-offered.json' }),
    inputs: [moveFrom(ACTIVE_NPC, 'dest.snack', 'owner.rv03')],
    expect: { status: 'Failed', code: 'MAP_LOAD_FAILED', activeMapIdAfter: saveOffered.player.mapId, temporarySceneCleanedUp: true, catalogStillCompiles: true }
  },
  {
    id: 'RV04', name: 'wrong-arrival-map', injection: 'variants/injections/wrong-arrival-map.json', initialFiles: files({ save: 'base/save-offered.json' }),
    inputs: [{ op: 'place', actorId: ACTIVE_NPC, position: baseRecipe.arrival }, moveFrom(ACTIVE_NPC, 'dest.snack', 'owner.rv04')],
    expect: { statusNot: 'Succeeded', status: 'Failed', codeOneOf: ['NO_ROUTE', 'TARGET_BLOCKED'], arrived: false, distance: 'Infinity', note: 'arrival is judged from the actual final position, never from the request' }
  },
  {
    id: 'RV05', name: 'dest-moved', initialFiles: files({ runner: 'variants/dest-moved/runner-bundle.json', save: 'base/save-offered.json' }),
    inputs: [accept(EV_SNACK, 1), start(EV_SNACK, 2, 'tx.rv05.start'), confirm(3), choose('notes', 4)],
    expect: successExpectation(destMovedRunner, saveOffered, [EV_SNACK])
  }
];

const compileNegatives = invalidRunners.map((x) => ({
  id: x.edit.id,
  runner: 'variants/invalid/' + x.edit.id + '/runner-bundle.json',
  editedCall: { event: EV_SNACK, index: x.edit.call, text: x.edit.text },
  expect: { outcome: 'Failed', resultCode: 'CONTENT_INVALID', diagnosticCodes: x.edit.codes, diagnosticCodesMatch: x.edit.anyOf ? 'any' : 'all', wholeBundleRejected: true, providerKeepsPreviousCatalog: true }
}));

const unregisteredEffect = {
  id: 'unregistered-effect',
  initialFiles: files({ runner: 'variants/unregistered-effect/runner-bundle.json', save: 'base/save-accepted.json' }),
  inputs: [start(EV_SNACK, 1, 'tx.unreg.start'), confirm(2), choose('notes', 3)],
  expect: Object.assign({ status: 'Failed', code: 'UNREGISTERED', wholeEffectListRefused: true }, restoredEntry('base/save-accepted.json'))
};

emitStart();

function emitStart() {}

// ---------------------------------------------------------------- emit
// Codex reconciliation against the original acceptance IDs; delegated supplemental cases are retained separately.
const introFalseSave = clone(saveOffered);
introFalseSave.flags.find(f => f.id === 'story.intro_done').value = false;
const canonicalScenarios = [
  {id:'A01',title:'등록 함수 호출 칸 로드와 오류 위치',initialFiles:files({save:'base/save-offered.json'}),
    inputs:[{op:'compile'},...compileNegatives.map(c=>({op:'compile-negative',fixture:c}))],
    expect:{validBundleAccepted:true,eventSpecificCode:false,invalidBundleRejectedWhole:true,diagnostics:['file','pointer','call','offset']}},
  {id:'A02',title:'도입 조건 false에서 무변경 거부',initialFiles:files({save:'base/save-intro-false.json'}),
    inputs:[start(EV_SNACK,1,'tx.a02.false')],expect:{status:'Failed',code:'CONDITION_FALSE',stateReplacements:0,moves:0,saveWrites:0,leases:0,expectedActualReason:true}},
  {id:'A03',title:'초대 수락 후 전체 임대·안전 진입 저장이 이동보다 먼저',initialFiles:files({save:'base/save-offered.json'}),
    inputs:[accept(EV_SNACK,1),start(EV_SNACK,2,'tx.a03.start')],expect:{sceneOrder:SCENE_ORDER,reservation:'Accepted',entrySavedBeforeStaging:true,resourcesAllOrNone:true}},
  {id:'A04',title:'목적지 JSON 좌표만 변경',initialFiles:files({save:'base/save-offered.json',runner:'variants/dest-moved/runner-bundle.json'}),
    inputs:[accept(EV_SNACK,1),start(EV_SNACK,2,'tx.a04.moved'),confirm(3),choose('notes',4),confirm(5)],
    expect:successExpectation(destMovedRunner,saveOffered,[EV_SNACK])},
  {id:'A05',title:'이동 중 확인 입력으로 대사가 앞서 열리지 않음',initialFiles:files({save:'base/save-offered.json'}),
    inputs:[accept(EV_SNACK,1),start(EV_SNACK,2,'tx.a05.arrival'),{op:'confirm-during-move',repeat:3},{op:'wait-for-actual-arrival'}],
    expect:{sayCountBeforeArrival:0,confirmConsumedDuringMove:false,sayAfterSuccessfulArrival:true,actualDistanceWithinTolerance:true}},
  {id:'A06',title:'선택지별 독립 실행과 선택한 응답만 표시',initialFiles:files({save:'base/save-offered.json'}),
    cases:['notes','tea'].map((key,index)=>({freshProcess:true,inputs:[accept(EV_SNACK,1),start(EV_SNACK,2,'tx.a06.'+key),confirm(3),choose(key,4),confirm(5)],
      expect:{...successExpectation(baseRunner,saveOffered,[EV_SNACK]),selectedFork:key,otherForkDisplayed:false,commitCount:1}}))},
  {id:'A07',title:'성공 저장 후 프로세스 종료·재실행',initialFiles:files({save:'base/save-offered.json'}),
    inputs:[accept(EV_SNACK,1),start(EV_SNACK,2,'tx.a07.success'),confirm(3),choose('notes',4),confirm(5),saveOp(6),{op:'exit-process'},{op:'relaunch-process'},start(EV_SNACK,7,'tx.a07.again')],
    expect:{...successExpectation(baseRunner,saveOffered,[EV_SNACK]),reloadMatchesCompletionClockPositionsWallet:true,retryCode:'CONDITION_FALSE',rewardGrantedAgain:0}},
  {id:'A08',title:'이동·대사·선택 중 강제 종료 후 안전 진입 복원',initialFiles:files({save:'base/save-offered.json'}),
    cases:['move','say','choice'].map(phase=>({freshProcess:true,inputs:[accept(EV_SNACK,1),start(EV_SNACK,2,'tx.a08.'+phase),{op:'drive-until-phase',phase},kill(phase),{op:'relaunch-process'}],
      expect:{...killExpect,liveHandles:0,pauseTokens:0,leases:0,inputLocked:false}}))},
  {id:'A09',title:'문 폐쇄·막힌 목표·맵 실패·중복 취소',initialFiles:files({save:'base/save-accepted.json'}),
    cases:[...runtimeVariants,...scenarios.find(s=>s.id==='A09').cases],expect:{terminalCount:1,timeoutBounded:true,failureEffects:0,rewardDelta:0,timeCostDelta:0,liveHandles:0,leases:0}},
  {...scenarios.find(s=>s.id==='A10'),title:'NPC·이벤트·대사 JSON 추가 후 A03–A07 재실행',repeatAcceptance:['A03','A04','A05','A06','A07'],noDedicatedScript:true}
];
const outputs = new Map();
function emitBytes(rel, buf) {
  if (rel.split('/').indexOf('..') >= 0 || outputs.has(rel)) throw new Error('bad or duplicate output path ' + rel);
  outputs.set(rel, buf);
}
function emitJson(rel, value) { emitBytes(rel, Buffer.from(JSON.stringify(value, null, 2) + '\n', 'utf8')); }

emitBytes('base/full-catalog.json', catalogBytes);
for (const s of [functionsSchema, runnerSchema, catalogSchema, saveSchema]) emitBytes('base/schemas/' + s.name, s.buf);
emitJson('base/runner-bundle.json', baseRunner);
emitJson('base/save-offered.json', saveOffered);
emitJson('base/save-accepted.json', saveAccepted);
emitJson('base/save-accepted-all.json', saveAcceptedAll);
emitJson('base/save-intro-false.json', introFalseSave);
emitJson('base/staging-recipes.json', stagingRecipes(baseRunner, saveOffered));

emitJson('variants/dest-moved/runner-bundle.json', destMovedRunner);
emitJson('variants/dest-moved/staging-recipes.json', stagingRecipes(destMovedRunner, saveOffered));

emitJson('variants/a10-mira/runner-bundle.json', miraRunner);
emitJson('variants/a10-mira/save-offered.json', miraOffered);
emitJson('variants/a10-mira/save-accepted.json', miraAccepted);
emitJson('variants/a10-mira/staging-recipes.json', stagingRecipes(miraRunner, miraOffered));

for (const inj of injections) emitJson('variants/injections/' + inj.id + '.json', inj);
emitJson('variants/unregistered-effect/runner-bundle.json', unregisteredRunner);
for (const x of invalidRunners) emitJson('variants/invalid/' + x.edit.id + '/runner-bundle.json', x.runner);

emitJson('repro-manifest.json', {
  formatVersion: 1,
  status: 'expectations only; nothing here claims a runtime pass',
  catalogSha256: catalogHash,
  inputOps: {
    accept: 'ReservationParticipant accept {eventId, acceptedAt}',
    start: 'IEventRunner.Start with the given inputSequence and transactionId',
    confirm: 'confirm input for the open say window',
    choice: 'select a fork key in the open choice window',
    save: 'ISaveService.SaveLatestSafe',
    reload: 'ISaveService.LoadLatest followed by state replacement',
    killphase: 'abort the process once the named phase (move, say, choice) is reached; no cleanup runs',
    cancel: 'cancel the running event handle',
    inject: 'arm a test-double fault',
    moveService: 'direct IMoveService.Move',
    place: 'IActorWorld.Restore of one actor at a safe boundary'
  },
  stagingPolicy: 'see base/staging-recipes.json; reserved staging joins leased actors at the incoming portal arrival while hidden, current staging never warps',
  acceptanceSource: 'design/implementation-tasks/인수기준.md',
  scenarios: canonicalScenarios,
  supplementalScenarios: scenarios.map(s=>({...s,id:'S'+s.id.slice(1)})),
  runtimeVariants,
  compileNegatives,
  unregisteredEffect
});

const inventory = Array.from(outputs.keys()).sort().map((rel) => ({ path: rel, sha256: sha256(outputs.get(rel)), bytes: outputs.get(rel).length }));
emitJson('manifest.json', {
  formatVersion: 1,
  generator: 'build-fixtures.cjs',
  source: {
    catalogSha256: catalogHash,
    catalogBytes: catalogBytes.length,
    originalSaveCatalogHash: srcSave.catalogHash,
    originalSaveCatalogHashMatches: srcSave.catalogHash === catalogHash,
    contentVersion: srcRunner.contentVersion,
    functionsFile: functionsSchema.name,
    runnerSchemaFile: runnerSchema.name,
    catalogSchemaFile: catalogSchema.name,
    saveSchemaFile: saveSchema ? saveSchema.name : null,
    appointmentRowSource: appointmentItemSchema() ? 'save schema ' + saveSchema.name : 'fallback fields from ADR 0003'
  },
  playableMaps: PLAYABLE_MAPS,
  inactive: {
    maps: srcRunner.maps.map((m) => m.id).filter((id) => PLAYABLE_MAPS.indexOf(id) < 0),
    npcs: srcRunner.npcs.map((n) => n.id).filter((id) => id !== ACTIVE_NPC),
    reason: 'kept only because the strict full catalog references them; no runner NPC, schedule or event uses them in the base bundle'
  },
  files: inventory
});

let diffs = 0;
for (const rel of Array.from(outputs.keys()).sort()) {
  const full = path.join(OUT, rel);
  if (full.indexOf(OUT + path.sep) !== 0) throw new Error('refusing to touch ' + full);
  const buf = outputs.get(rel);
  if (args.check) {
    if (!fs.existsSync(full) || !fs.readFileSync(full).equals(buf)) { console.error('DIFF ' + rel); diffs++; }
  } else {
    fs.mkdirSync(path.dirname(full), { recursive: true });
    fs.writeFileSync(full, buf);
  }
}
if (args.check) {
  if (diffs) { console.error(diffs + ' file(s) differ from the generator output'); process.exitCode = 1; }
  else console.log('check ok: ' + outputs.size + ' files match');
} else {
  console.log('wrote ' + outputs.size + ' files under ' + OUT);
}
