// One ui renderer. This file owns application input/browse/popup, not geometry or business judgments.
import { createMaxGraphAdapter } from './ui/packages/semantic-map/renderer-maxgraph/adapter.js';
import { installTouchNavigation } from './ui/packages/semantic-map/renderer-maxgraph/touch-navigation.js';
import { fitCamera } from './ui/packages/semantic-map/camera-fit.js';
import { validateWorld, navigate, goBack } from './lens.mjs';
import { prepareScene, sceneAt } from './lens-scene.mjs';

const $ = id => document.getElementById(id), container = $('process-graph');
const adapter = createMaxGraphAdapter(container);
const graph = adapter.graph;
graph.setCellsMovable(false); graph.setCellsResizable(false); graph.setCellsEditable(false);
graph.setConnectable(false); graph.setCellsDeletable(false);
graph.getPlugin('PanningHandler').useLeftButtonForPanning = true;
const empty = () => ({ pattern: 'graph/1', scale: 1, representations: [], relations: [], selectionProxies: {}, resourceComposition: null });
let world = null, prepared = null, browse = { lens: null, camera: null, selected: null, history: [] }, revision = 0, drawing = false;
const viewport = () => ({ x: 0, y: 0, width: Math.max(1, container.clientWidth), height: Math.max(1, container.clientHeight) });
const element = (tag, text) => { const e = document.createElement(tag); if (text !== undefined) e.textContent = text; return e; };
const popup = document.createElementNS('http://www.w3.org/2000/svg', 'foreignObject');
popup.classList.add('lens-popup'); popup.setAttribute('width', '320'); popup.setAttribute('height', '240');
popup.setAttribute('role', 'dialog'); popup.setAttribute('aria-label', '選択した記録の詳細');
popup.hidden = true; popup.style.display = 'none';
adapter.overlaySvg.removeAttribute('aria-hidden'); adapter.overlaySvg.append(popup);

function detail() {
  const selected = browse.selected;
  popup.replaceChildren(); popup.style.display = 'none';
  if (!selected || !world) return;
  const record = [...world.entities, ...world.relations].find(r => r.id === selected.id);
  const cell = selected.occurrence ? adapter.cellsByRegionId.get(selected.occurrence) :
    [...adapter.edgesByProjectionKey.values()].find(e => e.semantic.from === selected.from && e.semantic.to === selected.to && e.semantic.relationIds.includes(selected.id));
  const position = cell && graph.getView().getState(cell);
  // A folded record remains selected, but its hidden node never gets a false visible anchor.
  if (!record || !position) return;
  const box = element('section'); box.className = 'lens-popup-content';
  const close = element('button', '閉じる'); close.onclick = () => { browse.selected = null; detail(); container.focus(); };
  box.append(close, element('strong', record.label || record.kind));
  for (const [name, value] of [['ref', record.id], ['種類', record.kind], ['提供状態', record.state ?? '未提供'],
    ['方向', record.from ? `${record.from} → ${record.to}` : '—'], ['根拠参照', record.evidence?.join('\n') || '未提供']]) {
    box.append(element('div', name), element('pre', value));
  }
  popup.append(box);
  const view = viewport(), width = Math.min(320, view.width), height = Math.min(240, view.height);
  popup.setAttribute('width', width); popup.setAttribute('height', height);
  popup.setAttribute('x', Math.max(0, Math.min(view.width - width, position.x + position.width / 2)));
  popup.setAttribute('y', Math.max(0, Math.min(view.height - height, position.y + 24)));
  popup.style.display = ''; popup.hidden = false;
}
function draw() {
  if (drawing || !prepared) return;
  drawing = true;
  try {
    browse.camera = adapter.camera();
    const scene = sceneAt(prepared, browse.camera, viewport());
    adapter.render(scene); detail();
    const omitted = prepared.records.size - scene.representations.filter(r => r.recursiveUnit.readable).length;
    $('process-status').textContent = `供給: ${world.source} · ${world.authority ? '権威は供給側の主張（未検証）' : '権威なしの入力'} · 折畳み等 ${omitted}件（非存在とは限りません）`;
  } finally { drawing = false; }
}
function show() {
  prepared = prepareScene(world, browse.lens);
  const camera = browse.camera ?? fitCamera(prepared.layout.world, viewport(), 0.9);
  adapter.setCamera(camera.scale, camera.translateX, camera.translateY); draw();
  const choices = $('process-lenses'); choices.replaceChildren();
  for (const r of world.relations.filter(r => r.kind === 'views')) {
    const label = id => world.entities.find(e => e.id === id).label;
    const button = element('button', `${label(r.from)} → ${label(r.to)}`);
    button.classList.toggle('active', browse.lens.who === r.from && browse.lens.whom === r.to);
    button.onclick = () => { browse = navigate(world, browse, { who: r.from, whom: r.to }); show(); };
    choices.append(button);
  }
  $('process-back').disabled = !browse.history.length;
}
function accept(input) {
  validateWorld(input);
  world = structuredClone(input); browse = { lens: { ...world.lens }, camera: null, selected: null, history: [] }; show();
}
function reject() {
  world = prepared = null; browse = { lens: null, camera: null, selected: null, history: [] };
  adapter.render(empty()); detail(); $('process-lenses').replaceChildren(); $('process-back').disabled = true;
  $('process-status').textContent = '入力または依存を検証できません。旧表示は破棄しました。';
}
adapter.setActivationHandler(activation => { browse.selected = activation; detail(); });
adapter.setErrorHandler(reject);
adapter.onCameraChange(draw);
const touch = installTouchNavigation({ container, adapter, minimumScale: () => 0.05, maximumScale: () => 1e9,
  deferPan: true, applyCamera: (scale, x, y) => adapter.setCamera(scale, x, y) });
const resize = new ResizeObserver(draw); resize.observe(container);
const wheel = event => { if (!prepared) return; event.preventDefault();
  graph.zoomTo(Math.max(0.05, Math.min(1e9, adapter.camera().scale * (event.deltaY < 0 ? 1.15 : 1 / 1.15)))); };
container.addEventListener('wheel', wheel, { passive: false });
const key = event => { if (event.key === 'Escape') { browse.selected = null; detail(); } };
document.addEventListener('keydown', key);
$('process-back').onclick = () => { if (browse.history.length) { browse = goBack(browse); show(); } };
$('process-fit').onclick = () => { if (prepared) { const c = fitCamera(prepared.layout.world, viewport(), 0.9); adapter.setCamera(c.scale, c.translateX, c.translateY); } };
$('process-demo').onclick = async () => {
  const request = ++revision;
  try { const response = await fetch('process-world.json', { credentials: 'omit', cache: 'no-store' });
    if (!response.ok) throw Error('demo_unavailable'); const data = await response.json(); if (request === revision) accept(data);
  } catch { if (request === revision) reject(); }
};
$('process-file').onchange = async event => {
  const file = event.target.files?.[0]; if (!file) return; const request = ++revision;
  try { if (file.size > 512 * 1024) throw Error('file_limit'); const data = JSON.parse(await file.text()); if (request === revision) accept(data); }
  catch { if (request === revision) reject(); } event.target.value = '';
};
window.addEventListener('pagehide', () => {
  ++revision; resize.disconnect(); touch.destroy(); document.removeEventListener('keydown', key);
  container.removeEventListener('wheel', wheel); graph.getView().removeListener(draw); graph.destroy(); container.replaceChildren();
}, { once: true });
adapter.render(empty());
