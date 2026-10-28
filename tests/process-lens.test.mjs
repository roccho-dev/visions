import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { validateWorld, projectWorld } from '../src/Visions.Viewer/Assets/lens.mjs';

const source = JSON.parse(readFileSync('examples/process-lens/world.json', 'utf8'));
const clone = () => structuredClone(source);
const check = (name, test) => { test(); process.stdout.write('PASS process-lens: ' + name + '\n'); };
check('valid source contract', () => assert.equal(validateWorld(source).schema, 'visions.processWorld/1'));
const first = projectWorld(source), second = projectWorld(source, { who: 'manager', whom: 'staff' });
check('recursion reuses same model for actor on either side', () => assert.deepEqual(first.right, second.left));
check('explicit views relation required to navigate', () => {
  assert.equal(first.next.length, 1);
  assert.equal(first.next[0].to, 'staff');
  assert.throws(() => projectWorld(source, { who: 'director', whom: 'staff' }), /lens_not_supplied/);
});
check('only supplied correspondence is shown', () => {
  assert.equal(first.between.length, 0);
  assert.equal(second.between.length, 1);
  assert.equal(second.between[0].state, 'unknown');
});
check('unknown is not mislabeled as mismatch', () => assert.ok(!second.between.some(r => r.state === 'mismatch')));
check('replay is deterministic', () => assert.deepEqual(projectWorld(source), projectWorld(source)));
check('duplicate entity rejected', () => {
  const w=clone(); w.entities.push({ ...w.entities[0] });
  assert.throws(() => validateWorld(w), /duplicate_entity/);
});
check('dangling edge rejected', () => {
  const w=clone(); w.relations[0].to = 'absent';
  assert.throws(() => validateWorld(w), /relation_reference/);
});
check('unsupported success verdict rejected', () => {
  const w=clone(); w.relations.find(r=>r.id==='rel0').state='match';
  assert.throws(() => validateWorld(w), /unsupported_verdict/);
});
check('cycle is visible as an issue without a fabricated mismatch', () => {
  const w=clone(); w.relations.push({id:'cycle',kind:'contains',from:'director.goal',to:'director'});
  const result=projectWorld(w);
  assert.ok(result.issues.some(x=>x.startsWith('containment_cycle:')));
  assert.equal(result.between.length,0);
});
check('rendering uses a single SVG without detail pane', () => {
  const html=readFileSync('src/Visions.Viewer/Assets/process-lens.html','utf8');
  assert.equal((html.match(/<svg\b/g)||[]).length,1);
  assert.ok(!/<aside\b|<table\b/.test(html));
});
check('browser code never injects source text as HTML', () => {
  const js=readFileSync('src/Visions.Viewer/Assets/lens.mjs','utf8');
  assert.ok(!js.includes('innerHTML') && !js.includes('eval('));
});
