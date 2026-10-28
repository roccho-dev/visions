// Source contract and relative browsing only. No renderer or business evaluator.
const object = x => x !== null && typeof x === 'object' && !Array.isArray(x);
const text = x => typeof x === 'string' && x.length > 0 && x.length <= 160;
const require = (yes, reason) => { if (!yes) throw new Error(reason); };
const fields = (x, allowed) => object(x) && Object.keys(x).every(k => allowed.includes(k));
export function validateWorld(w) {
  require(fields(w, ['schema', 'source', 'authority', 'entities', 'relations', 'lens']), 'world_fields');
  require(w.schema === 'visions.processWorld/1' && text(w.source) && typeof w.authority === 'boolean', 'world_schema');
  require(Array.isArray(w.entities) && w.entities.length <= 160 && Array.isArray(w.relations) && w.relations.length <= 320, 'world_size');
  const ids = new Set(), relationIds = new Set(), parents = new Map();
  for (const e of w.entities) {
    require(fields(e, ['id', 'label', 'kind']) && [e.id, e.label, e.kind].every(text), 'entity_fields');
    require(!ids.has(e.id), 'duplicate_entity'); ids.add(e.id);
  }
  for (const r of w.relations) {
    require(fields(r, ['id', 'from', 'to', 'kind', 'state', 'evidence']) && [r.id, r.from, r.to, r.kind].every(text), 'relation_fields');
    require(!relationIds.has(r.id) && !ids.has(r.id) && ids.has(r.from) && ids.has(r.to), 'relation_reference'); relationIds.add(r.id);
    require(r.state === undefined || ['match', 'mismatch', 'unknown', 'excluded'].includes(r.state), 'relation_state');
    require(r.evidence === undefined || (Array.isArray(r.evidence) && r.evidence.length <= 16 && r.evidence.every(text)), 'evidence_refs');
    require(!['match', 'mismatch'].includes(r.state) || r.evidence?.length > 0, 'unsupported_verdict');
    if (r.kind === 'contains') {
      require(!parents.has(r.to) || parents.get(r.to) === r.from, 'multiple_containment_parents');
      parents.set(r.to, r.from);
    }
  }
  for (const id of ids) {
    const visited = new Set();
    for (let p = id; p !== undefined; p = parents.get(p)) {
      require(!visited.has(p), 'containment_cycle'); visited.add(p);
    }
  }
  validateLens(w, w.lens);
  return w;
}
export function validateLens(w, lens) {
  require(fields(lens, ['who', 'whom']) && text(lens.who) && text(lens.whom), 'lens_fields');
  require(w.relations.some(r => r.kind === 'views' && r.from === lens.who && r.to === lens.whom), 'lens_not_supplied');
}
export function projectWorld(world, lens = world.lens) {
  validateWorld(world); validateLens(world, lens);
  const entities = new Map(world.entities.map(e => [e.id, e])), children = new Map();
  for (const r of world.relations.filter(r => r.kind === 'contains')) {
    if (!children.has(r.from)) children.set(r.from, new Set());
    children.get(r.from).add(r.to);
  }
  const tree = id => ({ id, children: [...(children.get(id) || [])].map(tree) });
  const left = tree(lens.who), right = tree(lens.whom);
  const refs = node => new Set([node.id, ...node.children.flatMap(child => [...refs(child)])]);
  const a = refs(left), b = refs(right);
  const relations = world.relations.filter(r => !['contains', 'views'].includes(r.kind));
  return { entities, left, right, relations,
    between: relations.filter(r => (a.has(r.from) && b.has(r.to)) || (b.has(r.from) && a.has(r.to))),
    next: world.relations.filter(r => r.kind === 'views' && r.from === lens.whom),
    lens: { ...lens }, issues: [] };
}
// A browse transition stores no second World. Capture the camera and selection too.
export function navigate(world, browse, lens) {
  validateLens(world, lens);
  return { ...browse, lens: { ...lens }, selected: null, camera: null,
    history: [...browse.history, { lens: browse.lens, selected: browse.selected, camera: browse.camera }] };
}
export function goBack(browse) {
  return browse.history.length ? { ...browse, ...browse.history.at(-1), history: browse.history.slice(0, -1) } : browse;
}
