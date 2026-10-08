const fs = require('node:fs');
const vm = require('node:vm');
const assert = require('node:assert/strict');
const html = fs.readFileSync('src/SkyEye.Companion.Hybrid/Resources/Raw/vision-diagnostics-dashboard.html', 'utf8');
const script = html.match(/<script>([\s\S]*?)<\/script>/)[1];
const elements = new Map(), images = [], requestedFrames = [], revoked = [];
let refresh, current, objectId = 0;
const snapshot = (sessionId, sequence) => ({ sessionId, active: true, frame: { sequence, frameId: 100 + sequence, mediaGeneration: 7 },
  detection: { balls: [], candidates: [], circleCount: 0 }, samples: [] });
const queue = [snapshot('a', 1), snapshot('a', 2), { sessionId: 'a', active: false, frame: null, detection: null, samples: [] }, snapshot('b', 1), snapshot('c', 1)];
const element = id => {
  if (!elements.has(id)) elements.set(id, { getBoundingClientRect: () => ({ width: 0, height: 0 }), getContext: () => ({}) });
  return elements.get(id);
};
class ImageStub { constructor() { images.push(this); } }
const context = vm.createContext({ document: { getElementById: element }, window: { addEventListener() {} }, Image: ImageStub,
  URL: { createObjectURL: () => `blob:${++objectId}`, revokeObjectURL: value => revoked.push(value) },
  setInterval: fn => { refresh = fn; }, console,
  fetch: async url => {
    if (url === '/api/snapshot') { current = queue.shift(); return { ok: true, json: async () => current }; }
    requestedFrames.push(url);
    const sequence = current.frame.sequence;
    return { ok: true, status: 200, headers: { get: () => String(sequence) }, blob: async () => ({}) };
  } });
const settle = () => new Promise(resolve => setImmediate(resolve));
(async () => {
  vm.runInContext(script, context); await settle();
  images[0].onload(); assert.equal(element('empty').hidden, true);
  await refresh(); assert.equal(images.length, 2);
  await refresh(); assert.equal(element('empty').hidden, false, 'Paused sampling must clear the displayed source image');
  images[1].onload(); assert.equal(element('empty').hidden, false, 'A late image callback cannot restore stale pixels after pause');
  await refresh(); images[2].onload(); assert.equal(element('empty').hidden, true);
  await refresh(); assert.equal(images.length, 4, 'A different session with the same sequence must fetch a new image');
  images[3].onload();
  assert(requestedFrames.some(url => url.includes('sessionId=b')));
  assert(requestedFrames.some(url => url.includes('sessionId=c')));
  assert(revoked.includes('blob:2'), 'Late, mismatched frame object URL must be released');
  console.log('PASS dashboard pause, late-image fencing, session reset and object URL cleanup');
})().catch(error => { console.error(error); process.exitCode = 1; });
