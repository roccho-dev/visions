// Thin adapter to ui's real layout/frontier. Source IDs never become display IDs.
import { recursiveLayout, recursiveFrontier } from './ui/packages/semantic-map/projection/recursive-view.js';
import { wrapLabel, linePx, LABEL_LINE_PX } from './ui/packages/control/src/atlas-text.mjs';
import { projectWorld } from './lens.mjs';

export function prepareScene(world, lens) {
  const view = projectWorld(world, lens), children = new Map(), records = new Map(), captions = new Map();
  const key = (side, id) => JSON.stringify([side, id]);
  function visit(node, side, root = false) {
    const id = key(side, node.id), record = view.entities.get(node.id);
    records.set(id, record);
    captions.set(id, wrapLabel((root ? (side === 'who' ? '誰が · ' : '誰を · ') : '') + record.label));
    children.set(id, node.children.map(child => visit(child, side)));
    return id;
  }
  const roots = [visit(view.left, 'who', true), visit(view.right, 'whom', true)];
  const layout = recursiveLayout({ roots, children, caption: id => {
    const lines = captions.get(id).split('\n');
    return { width: Math.max(...lines.map(linePx)), height: lines.length * LABEL_LINE_PX };
  } });
  const relations = [];
  for (const r of view.relations) {
    const internal = ['who', 'whom'].map(side => [key(side, r.from), key(side, r.to)])
      .filter(pair => pair.every(id => records.has(id)));
    const pairs = internal.length ? internal : [['who', 'whom'], ['whom', 'who']]
      .map(([from, to]) => [key(from, r.from), key(to, r.to)]).filter(pair => pair.every(id => records.has(id)));
    for (const [from, to] of pairs) relations.push({
      relationIds: [r.id], from, to, kind: r.kind, label: `${r.kind} · ${r.state ?? '未提供'}`,
      directed: true, readOnly: true, foreground: true,
      activation: { type: 'lens.select', id: r.id, from, to },
    });
  }
  return { layout, children, records, captions, relations, world, lens };
}

export function sceneAt(prepared, camera, viewport) {
  const frontier = recursiveFrontier(prepared.layout, { camera, viewport });
  const opening = new Map();
  for (const [id, amount] of [...[...frontier.painted].map(id => [id, 1]), ...frontier.previews]) {
    const parent = prepared.layout.units.get(id).parent;
    if (parent) opening.set(parent, Math.max(opening.get(parent) ?? 0, amount));
  }
  const representations = [...prepared.layout.units].filter(([id]) => frontier.painted.has(id) || frontier.previews.has(id))
    .map(([id, unit]) => {
      const readable = frontier.painted.has(id), progress = opening.get(id) ?? 0;
      return { regionId: id, sourceRegionId: id, label: prepared.captions.get(id), bounds: unit.bounds,
        kind: prepared.records.get(id).kind, depth: unit.depth, zIndex: unit.depth * 10,
        shape: progress > 0 ? 'boundary' : 'graph-node',
        ...(progress > 0 || !readable ? { mode: 'boundary' } : {}),
        readOnly: true, geometryEditable: false, labelEditable: false,
        recursiveUnit: { ...unit, hasInterior: prepared.children.get(id).length > 0,
          readable, opened: progress > 0, opening: progress, previewOpacity: readable ? 1 : frontier.previews.get(id), cameraScale: camera.scale },
        activation: readable ? { type: 'lens.select', id: prepared.records.get(id).id, occurrence: id } : null };
    });
  return { pattern: 'graph/1', scale: camera.scale, selectionProxies: {}, resourceComposition: null,
    representations, relations: prepared.relations.filter(r => frontier.painted.has(r.from) && frontier.painted.has(r.to)) };
}
