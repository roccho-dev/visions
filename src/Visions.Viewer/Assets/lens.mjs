// A browser-only, read-only projection. It never joins Windows UI facts to business claims.
const fail = reason => { throw new Error(reason); };
const ok = (value, reason) => { if (!value) fail(reason); };
const text = value => typeof value === 'string' && value.length > 0 && value.length <= 160;
const obj = value => value !== null && typeof value === 'object' && !Array.isArray(value);
const same = (objValue, keys) => Object.keys(objValue).every(key => keys.includes(key));
export function validateWorld(world) {
  ok(obj(world) && same(world, ['schema', 'source', 'authority', 'entities', 'relations', 'lens']), 'world_fields');
  ok(world.schema === 'visions.processWorld/1' && text(world.source) && typeof world.authority === 'boolean', 'world_schema');
  ok(Array.isArray(world.entities) && world.entities.length <= 160 && Array.isArray(world.relations) && world.relations.length <= 320, 'world_size');
  const ids = new Set();
  for (const entity of world.entities) {
    ok(obj(entity) && same(entity, ['id', 'label', 'kind']) && text(entity.id) && text(entity.label) && text(entity.kind), 'entity_fields');
    ok(!ids.has(entity.id), 'duplicate_entity'); ids.add(entity.id);
  }
  const refs = new Set();
  for (const relation of world.relations) {
    ok(obj(relation) && same(relation, ['id', 'from', 'to', 'kind', 'state', 'evidence']), 'relation_fields');
    ok(text(relation.id) && text(relation.from) && text(relation.to) && text(relation.kind), 'relation_identity');
    ok(!refs.has(relation.id) && !ids.has(relation.id) && ids.has(relation.from) && ids.has(relation.to), 'relation_reference');
    refs.add(relation.id);
    ok(relation.state === undefined || ['match', 'mismatch', 'unknown', 'excluded'].includes(relation.state), 'relation_state');
    ok(relation.evidence === undefined || (Array.isArray(relation.evidence) && relation.evidence.length <= 16 && relation.evidence.every(text)), 'evidence_refs');
    ok(!['match', 'mismatch'].includes(relation.state) || relation.evidence?.length > 0, 'unsupported_verdict');
  }
  ok(obj(world.lens) && same(world.lens, ['who', 'whom']) && text(world.lens.who) && text(world.lens.whom), 'lens_fields');
  ok(world.relations.some(r => r.kind === 'views' && r.from === world.lens.who && r.to === world.lens.whom), 'lens_not_supplied');
  return world;
}
export function projectWorld(world, lens = world.lens) {
  validateWorld(world);
  ok(obj(lens) && text(lens.who) && text(lens.whom), 'lens_fields');
  ok(world.relations.some(r => r.kind === 'views' && r.from === lens.who && r.to === lens.whom), 'lens_not_supplied');
  const entities = new Map(world.entities.map(e => [e.id, e]));
  const children = new Map();
  for (const relation of world.relations) {
    if (relation.kind !== 'contains') continue;
    if (!children.has(relation.from)) children.set(relation.from, []);
    children.get(relation.from).push(relation.to);
  }
  const issues = [];
  const tree = root => {
    let count = 0;
    const walk = (id, visiting, depth) => {
      if (visiting.has(id)) { issues.push('containment_cycle:' + id); return { id, children: [], cycle: true }; }
      if (count++ >= 96 || depth >= 10) { issues.push('display_limit:' + id); return { id, children: [], limited: true }; }
      const next = new Set(visiting); next.add(id);
      return { id, children: (children.get(id) || []).map(child => walk(child, next, depth + 1)) };
    };
    return walk(root, new Set(), 0);
  };
  const left = tree(lens.who), right = tree(lens.whom);
  const includes = (branch, set = new Set()) => {
    set.add(branch.id);
    branch.children.forEach(child => includes(child, set));
    return set;
  };
  const lrefs = includes(left), rrefs = includes(right);
  const between = world.relations.filter(r => r.kind !== 'contains' && r.kind !== 'views' &&
    ((lrefs.has(r.from) && rrefs.has(r.to)) || (rrefs.has(r.from) && lrefs.has(r.to))));
  const next = world.relations.filter(r => r.kind === 'views' && r.from === lens.whom);
  return { entities, left, right, between, next, issues, lens };
}

// Presentation only. No business classification or Plan/Fact correlation occurs here.
if (typeof document !== 'undefined') {
  const svg = document.getElementById('process-graph');
  const status = document.getElementById('process-status');
  const lenses = document.getElementById('process-lenses');
  const back = document.getElementById('process-back');
  const ns = 'http://www.w3.org/2000/svg';
  const state = { world: null, lens: null, history: [], selected: null, camera: null };
  let width = 1000, height = 650;
  const make = (tag, attrs = {}, value) => {
    const n = document.createElementNS(ns, tag);
    for (const [key, item] of Object.entries(attrs)) n.setAttribute(key, String(item));
    if (value !== undefined) n.textContent = String(value);
    return n;
  };
  const label = id => state.world.entities.find(n => n.id === id)?.label || id;
  const camera = () => {
    const c = state.camera || { x: 0, y: 0, zoom: 1 };
    svg.setAttribute('viewBox', [c.x, c.y, width / c.zoom, height / c.zoom].join(' '));
  };
  const boxHeight = node => 54 + (node.children.length ? 8 + node.children.reduce((n, x) => n + boxHeight(x) + 8, 0) : 0);
  function render() {
    svg.replaceChildren();
    if (!state.world) { lenses.replaceChildren(); back.disabled = true; return; }
    const view = projectWorld(state.world, state.lens);
    height = Math.max(650, boxHeight(view.left) + 85, boxHeight(view.right) + 85);
    const edges = make('g'), nodes = make('g'), positions = new Map();
    const drawNode = (branch, side, x, y, w) => {
      const h = boxHeight(branch), entity = view.entities.get(branch.id);
      positions.set(side + ':' + branch.id, { x: x + w / 2, y: y + 27 });
      const group = make('g', { 'class': 'process-node', tabindex: '0', role: 'button', 'aria-label': entity.label });
      group.append(make('rect', { x, y, width: w, height: h, rx: 12 }));
      group.append(make('text', { x: x + 14, y: y + 27, 'class': 'process-title' }, entity.label));
      group.append(make('text', { x: x + w - 15, y: y + 27, 'text-anchor': 'end', 'class': 'process-kind' }, entity.kind));
      if (branch.cycle || branch.limited) group.append(make('text', { x: x + 14, y: y + 44, 'class': 'process-unknown' }, '内側の表示は未確定'));
      group.addEventListener('click', () => { state.selected = { item: entity, at: positions.get(side + ':' + branch.id) }; render(); });
      group.addEventListener('keydown', e => { if (e.key === 'Enter' || e.key === ' ') { e.preventDefault(); group.dispatchEvent(new Event('click')); } });
      nodes.append(group);
      let cursor = y + 48;
      for (const child of branch.children) {
        drawNode(child, side, x + 14, cursor, Math.max(130, w - 28));
        cursor += boxHeight(child) + 8;
      }
    };
    svg.append(make('rect', { x: 12, y: 48, width: 477, height: height - 60, rx: 14, 'class': 'process-pane' }));
    svg.append(make('rect', { x: 511, y: 48, width: 477, height: height - 60, rx: 14, 'class': 'process-pane' }));
    svg.append(make('text', { x: 28, y: 32, 'class': 'process-heading' }, '👁 誰が · ' + label(state.lens.who)));
    svg.append(make('text', { x: 528, y: 32, 'class': 'process-heading' }, '🎯 誰を · ' + label(state.lens.whom)));
    svg.append(edges, nodes);
    drawNode(view.left, 'left', 24, 62, 452);
    drawNode(view.right, 'right', 523, 62, 452);
    for (const relation of view.between) {
      const a = positions.get('left:' + relation.from) || positions.get('left:' + relation.to);
      const b = positions.get('right:' + relation.to) || positions.get('right:' + relation.from);
      if (!a || !b) continue;
      const line = make('line', { x1: a.x, y1: a.y, x2: b.x, y2: b.y,
        'class': 'process-relation ' + (relation.state || 'unknown'), tabindex: '0', role: 'button',
        'aria-label': '対応関係 ' + (relation.state || '未分類') });
      const open = () => { state.selected = { item: relation, at: { x: (a.x + b.x) / 2, y: (a.y + b.y) / 2 } }; render(); };
      line.addEventListener('click', open);
      line.addEventListener('keydown', e => { if (e.key === 'Enter' || e.key === ' ') { e.preventDefault(); open(); } });
      edges.append(line);
    }
    const list = state.selected ? [state.selected.item.label || state.selected.item.kind,
      'ref: ' + state.selected.item.id,
      '種類: ' + state.selected.item.kind,
      '提供状態: ' + (state.selected.item.state || '提供なし'),
      '根拠: ' + (state.selected.item.evidence?.join(', ') || '未提供')] : [];
    if (list.length) {
      const { x, y } = state.selected.at;
      const px = Math.max(5, Math.min(674, x + 22)), py = Math.max(55, Math.min(height - 190, y + 12));
      const pop = make('g', { 'class': 'process-popup', role: 'dialog', 'aria-label': '詳細と根拠' });
      pop.append(make('rect', { x: px, y: py, width: 305, height: 156, rx: 11 }));
      list.forEach((t, i) => pop.append(make('text', { x: px + 14, y: py + 25 + i * 27 }, String(t).slice(0, 42))));
      const close = make('text', { x: px + 285, y: py + 22, 'class': 'process-close', tabindex: '0', role: 'button' }, '×');
      const closePopup = () => { state.selected = null; render(); };
      close.addEventListener('click', closePopup);
      close.addEventListener('keydown', e => { if (e.key === 'Enter' || e.key === ' ') { e.preventDefault(); closePopup(); } });
      pop.append(close);
      svg.append(pop);
    }
    lenses.replaceChildren();
    const nextViews = state.world.relations.filter(r => r.kind === 'views');
    for (const relation of nextViews) {
      const button = document.createElement('button');
      button.textContent = label(relation.from) + ' → ' + label(relation.to);
      button.classList.toggle('active', state.lens.who === relation.from && state.lens.whom === relation.to);
      button.addEventListener('click', () => navigate({ who: relation.from, whom: relation.to }));
      lenses.append(button);
    }
    back.disabled = !state.history.length;
    status.textContent = '供給: ' + state.world.source + (state.world.authority ? '（権限の真偽は未検証）' : '（非権威の入力）') +
      ' · 対応関係 ' + view.between.length + ' · 表示上の不明 ' + view.issues.length;
    camera();
  }
  function navigate(lens) {
    if (!state.world || (state.lens.who === lens.who && state.lens.whom === lens.whom)) return;
    projectWorld(state.world, lens);
    state.history.push({ ...state.lens }); state.lens = lens; state.selected = null; state.camera = null; render();
  }
  function accept(raw) {
    const world = validateWorld(raw);
    state.world = world; state.lens = { ...world.lens }; state.history = [];
    state.selected = null; state.camera = null; render();
  }
  document.getElementById('process-demo').addEventListener('click', async () => {
    try {
      const response = await fetch('process-world.json', { cache: 'no-store', credentials: 'omit' });
      if (!response.ok) throw Error('demo_unavailable');
      accept(await response.json());
    } catch { status.textContent = '合成入力を開けませんでした。'; }
  });
  document.getElementById('process-file').addEventListener('change', async event => {
    const file = event.target.files?.[0]; if (!file) return;
    try { ok(file.size <= 512 * 1024, 'file_limit'); accept(JSON.parse(await file.text())); }
    catch (error) { state.world = null; state.lens = null; state.history = []; state.selected = null;
      render(); status.textContent = 'World契約を拒否: ' + error.message; }
    event.target.value = '';
  });
  back.addEventListener('click', () => {
    if (!state.history.length) return;
    state.lens = state.history.pop(); state.selected = null; state.camera = null; render();
  });
  document.getElementById('process-fit').addEventListener('click', () => { state.camera = null; camera(); });
  document.addEventListener('keydown', event => {
    if (event.key === 'Escape' && state.selected) { state.selected = null; render(); }
  });
  svg.addEventListener('wheel', e => {
    if (!state.world) return; e.preventDefault();
    const rect = svg.getBoundingClientRect(); const x = (e.clientX - rect.left) / rect.width;
    const y = (e.clientY - rect.top) / rect.height;
    const old = state.camera || { x: 0, y: 0, zoom: 1 };
    const zoom = Math.max(0.6, Math.min(3.5, old.zoom * (e.deltaY < 0 ? 1.12 : 1 / 1.12)));
    state.camera = { zoom,
      x: old.x + x * width / old.zoom - x * width / zoom,
      y: old.y + y * height / old.zoom - y * height / zoom };
    camera();
  }, { passive: false });
  let dragging = null;
  svg.addEventListener('pointerdown', e => {
    if (!state.world || e.target.closest('.process-node,.process-relation,.process-popup')) return;
    dragging = { x: e.clientX, y: e.clientY };
    svg.setPointerCapture(e.pointerId);
  });
  svg.addEventListener('pointermove', e => {
    if (!dragging || !state.world) return;
    const rect = svg.getBoundingClientRect();
    const old = state.camera || { x: 0, y: 0, zoom: 1 };
    state.camera = { zoom: old.zoom, x: old.x - (e.clientX - dragging.x) / rect.width * width / old.zoom,
      y: old.y - (e.clientY - dragging.y) / rect.height * height / old.zoom };
    dragging = { x: e.clientX, y: e.clientY }; camera();
  });
  svg.addEventListener('pointerup', () => { dragging = null; });
  svg.addEventListener('pointercancel', () => { dragging = null; });
  render();
}
