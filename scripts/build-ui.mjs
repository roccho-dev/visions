// Build-time dependency closure, never a runtime download or a second source copy.
import fs from 'node:fs/promises';
import path from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';
import { execFileSync } from 'node:child_process';
import { createHash } from 'node:crypto';
const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const ui = path.join(root, 'deps/ui');
const git = (cwd, ...args) => execFileSync('git', ['-C', cwd, ...args], { encoding: 'utf8', maxBuffer: 16 * 1024 * 1024 });
const entry = git(root, 'ls-tree', 'HEAD', 'deps/ui').trim().split(/\s+/);
if (entry[0] !== '160000' || entry[1] !== 'commit' || !/^[0-9a-f]{40}$/.test(entry[2])) throw Error('ui_gitlink_missing');
const commit = entry[2];
if (git(ui, 'rev-parse', 'HEAD').trim() !== commit || git(ui, 'status', '--porcelain').trim()) throw Error('ui_dependency_not_exact');
const { packBrowserModules } = await import(pathToFileURL(path.join(ui, 'packages/semantic-map/scripts/browser-module-closure.mjs')));
const roots = ['packages/semantic-map/', 'packages/control/src/', 'packages/data-pin/', 'packages/a2ui-browser/src/'];
const entries = ['packages/semantic-map/renderer-maxgraph/adapter.js',
  'packages/semantic-map/renderer-maxgraph/touch-navigation.js', 'packages/semantic-map/projection/recursive-view.js',
  'packages/semantic-map/camera-fit.js', 'packages/control/src/atlas-text.mjs'];
const modules = new Set();
for (const entry of entries) for (const name of (await packBrowserModules({ repoRoot: ui, entry, roots })).modules) modules.add(name);
const out = path.join(root, 'src/Visions.Viewer/Assets/ui');
// This directory contains generated files only. No broader cleanup is performed.
await fs.rm(out, { recursive: true, force: true }); await fs.mkdir(out, { recursive: true });
const files = {};
async function emit(name, bytes) {
  const dest = path.join(out, name); await fs.mkdir(path.dirname(dest), { recursive: true });
  await fs.writeFile(dest, bytes); files[name] = createHash('sha256').update(bytes).digest('hex');
}
for (const name of [...modules].sort()) {
  if (!roots.some(root => name.startsWith(root)) || name.includes('..')) throw Error('ui_closure_escape');
  // git show uses the pinned canonical bytes, including on CRLF Windows checkouts.
  await emit(name, git(ui, 'show', `${commit}:${name}`));
}
for (const name of ['THIRD_PARTY_NOTICES.md', 'LICENSE.maxGraph'])
  await emit(name, git(ui, 'show', `${commit}:packages/semantic-map/${name}`));
const rootFiles = git(ui, 'ls-tree', '--name-only', commit).split('\n');
for (const name of rootFiles.filter(name => /^(LICENSE|COPYING|NOTICE)(\.[\w-]+)?$/i.test(name)))
  await emit('upstream-' + name, git(ui, 'show', `${commit}:${name}`));
await fs.writeFile(path.join(out, 'package.json'), '{"type":"module"}\n');
await fs.writeFile(path.join(out, 'provenance.json'), JSON.stringify({ repository: 'roccho-dev/ui', commit,
  entries, files, generated: true }, null, 2) + '\n');
console.log(`ui dependency ${commit}: ${modules.size} exact modules, same-origin assets only`);
