import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { validateWorld, projectWorld, navigate, goBack } from '../src/Visions.Viewer/Assets/lens.mjs';
const source = JSON.parse(readFileSync('examples/process-lens/world.json', 'utf8'));
const copy = () => structuredClone(source);
let count = 0;
const check = (name, test) => { test(); ++count; console.log('PASS process-lens:', name); };
const rejected = (change, reason) => { const w = copy(); change(w); assert.throws(() => validateWorld(w), reason); };
check('valid source contract', () => assert.equal(validateWorld(source), source));
const left = projectWorld(source), right = projectWorld(source, { who: 'manager', whom: 'staff' });
check('same model on either side', () => assert.deepEqual(left.right, right.left));
check('explicit views only', () => assert.throws(() => projectWorld(source, { who: 'director', whom: 'staff' }), /lens_not_supplied/));
check('only supplied correspondence', () => { assert.equal(left.between.length, 0); assert.equal(right.between[0].state, 'unknown'); });
check('deterministic replay', () => assert.deepEqual(projectWorld(source), left));
check('duplicate entity refused', () => rejected(w => w.entities.push(w.entities[0]), /duplicate_entity/));
check('dangling reference refused', () => rejected(w => w.relations[0].to = 'absent', /relation_reference/));
check('unsupported match refused', () => rejected(w => w.relations.at(-1).state = 'match', /unsupported_verdict/));
check('unsupported mismatch refused', () => rejected(w => w.relations.at(-1).state = 'mismatch', /unsupported_verdict/));
check('unknown evidence kept as unknown', () => { const w = copy(); w.relations.at(-1).evidence = ['source:one']; assert.equal(projectWorld(w, { who: 'manager', whom: 'staff' }).between[0].state, 'unknown'); });
check('containment cycle refused before layout', () => rejected(w => w.relations.push({ id: 'cycle', from: 'director.goal', to: 'director', kind: 'contains' }), /containment_cycle/));
check('multiple containment parents explicit unsupported', () => rejected(w => w.relations.push({ id: 'parent2', from: 'staff', to: 'manager.goal', kind: 'contains' }), /multiple_containment_parents/));
check('duplicate containment does not duplicate source nodes', () => { const w = copy(); w.relations.push({ ...w.relations[0], id: 'same-parent' }); assert.equal(projectWorld(w).left.children.length, 2); });
check('relationship direction preserved', () => { const w = copy(); Object.assign(w.relations.at(-1), { from: 'staff.judge', to: 'manager.judge' }); assert.equal(projectWorld(w, { who: 'manager', whom: 'staff' }).between[0].from, 'staff.judge'); });
check('role labels have no behavioral meaning', () => { const w = copy(); w.entities.forEach(e => e.label = '同じ表示名'); assert.deepEqual(projectWorld(w).left, left.left); });
check('back restores camera and selection, without a World copy', () => {
  const before = { lens: source.lens, selected: { id: 'manager' }, camera: { scale: 2, translateX: 4, translateY: 6 }, history: [] };
  assert.deepEqual(goBack(navigate(source, before, { who: 'manager', whom: 'staff' })), before);
});
check('deep recursive process is not clipped at an arbitrary level', () => {
  const w = copy(); let parent = 'staff';
  for (let i = 0; i < 20; i++) { const id = 'depth' + i; w.entities.push({ id, kind: 'process', label: id }); w.relations.push({ id: 'contains' + i, from: parent, to: id, kind: 'contains' }); parent = id; }
  let node = projectWorld(w, { who: 'manager', whom: 'staff' }).right.children.at(-1), n = 0;
  while (node) { n++; node = node.children[0]; } assert.equal(n, 20);
});
check('input World is unchanged by browsing', () => { const bytes = JSON.stringify(source); projectWorld(source); assert.equal(JSON.stringify(source), bytes); });
check('one shared graph surface, no bottom detail', () => {
  const html = readFileSync('src/Visions.Viewer/Assets/process-lens.html', 'utf8');
  assert.equal((html.match(/id="process-graph"/g) || []).length, 1); assert.ok(!/<aside\b|<table\b/.test(html));
});
check('renderer and touch handling belong to ui', () => {
  const js = readFileSync('src/Visions.Viewer/Assets/lens-view.mjs', 'utf8');
  assert.equal((js.match(/createMaxGraphAdapter\(container\)/g) || []).length, 1);
  assert.ok(js.includes("from './ui/packages/semantic-map/renderer-maxgraph/adapter.js'"));
  assert.ok(js.includes('installTouchNavigation') && !js.includes('innerHTML'));
  assert.ok(!js.includes("createElementNS(ns, 'rect')") && !js.includes('pointermove'));
});
console.log(`RESULT process-lens ${count}/${count} PASS`);
