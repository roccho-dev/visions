// Real ui recursive layout/frontier, not a browser rendering test.
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { prepareScene, sceneAt } from '../src/Visions.Viewer/Assets/lens-scene.mjs';
const source = JSON.parse(readFileSync('examples/process-lens/world.json', 'utf8'));
const lens = { who: 'manager', whom: 'staff' };
const copy = () => structuredClone(source);
let count = 0;
const check = (name, test) => { test(); ++count; console.log('PASS ui-scene:', name); };
const view = { x: 0, y: 0, width: 20000, height: 20000 }, camera = { scale: 10, translateX: 0, translateY: 0 };
check('actual ui creates finite nested geometry', () => {
  const p = prepareScene(source, lens);
  for (const unit of p.layout.units.values()) {
    assert.ok(Object.values(unit.bounds).every(Number.isFinite));
    if (unit.parent) { const b = unit.bounds, a = p.layout.units.get(unit.parent).bounds;
      assert.ok(b.x >= a.x && b.y >= a.y && b.x + b.width <= a.x + a.width + 1e-9 && b.y + b.height <= a.y + a.height + 1e-9); }
  }
});
check('source references survive the two appearances', () => {
  const p = prepareScene(source, lens), s = sceneAt(p, camera, view);
  assert.ok(s.representations.every(r => r.activation.id === p.records.get(r.regionId).id));
});
check('internal process edges are not lost', () => {
  const w = copy(); w.relations.push({ id: 'next', from: 'staff.goal', to: 'staff.judge', kind: 'next' });
  const p = prepareScene(w, lens), edge = p.relations.find(r => r.relationIds[0] === 'next');
  assert.equal(p.records.get(edge.from).id, 'staff.goal'); assert.equal(p.records.get(edge.to).id, 'staff.judge');
});
check('right-to-left direction remains right-to-left', () => {
  const w = copy(); Object.assign(w.relations.at(-1), { from: 'staff.judge', to: 'manager.judge' });
  const p = prepareScene(w, lens), edge = p.relations[0];
  assert.equal(p.records.get(edge.from).id, 'staff.judge'); assert.equal(p.records.get(edge.to).id, 'manager.judge');
});
check('only readable original endpoints admit edges', () => {
  const p = prepareScene(source, lens), s = sceneAt(p, { ...camera, scale: 0.2 }, view);
  const painted = new Set(s.representations.filter(r => r.recursiveUnit.readable).map(r => r.regionId));
  assert.ok(s.relations.every(r => painted.has(r.from) && painted.has(r.to)));
  assert.ok(s.representations.filter(r => !r.recursiveUnit.readable).every(r => r.activation === null));
});
check('unknown remains a supplied state, never an inferred mismatch', () => {
  const s = sceneAt(prepareScene(source, lens), camera, view);
  assert.equal(s.relations.length, 1); assert.equal(s.relations[0].label, 'corresponds · unknown');
});
check('all native editing caps are absent', () => {
  const s = sceneAt(prepareScene(source, lens), camera, view);
  assert.ok(s.representations.every(r => r.readOnly && !r.geometryEditable && !r.labelEditable));
  assert.ok(s.relations.every(r => r.readOnly));
});
check('same input produces same ui scene', () => {
  assert.deepEqual(sceneAt(prepareScene(source, lens), camera, view), sceneAt(prepareScene(source, lens), camera, view));
});
console.log(`RESULT ui-scene ${count}/${count} PASS`);
