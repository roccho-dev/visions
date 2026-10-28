'use strict';
const $ = id => document.getElementById(id);
const roles = {button:'ボタン', input:'入力欄', list:'一覧', grid:'表', window:'画面', other:'未分類'};
const el = (tag, text) => {const x=document.createElement(tag); if(text!==undefined)x.textContent=text; return x;};
function render(model){
  const facts = new Map(model.facts.map(f=>[f.id,f]));
  const edges = new Map(model.transitions.map(e=>[e.to_id,e]));
  const label = s => `${roles[s.role]||'未分類'} · ${s.target.slice(0,6)}`;
  const select = id => {$('evidence').textContent=JSON.stringify(facts.get(id),null,2);};
  $('count').textContent=model.steps.length;
  $('edge-count').textContent=model.transitions.length;
  const artifacts=model.facts.filter(f=>f.kind==='artifact');
  $('artifact-count').textContent=artifacts.length;
  const synthetic=model.facts.some(f=>f.kind==='capture'&&f.data.scope==='synthetic');
  $('coverage').textContent=synthetic?'合成データのデモです。実測値ではありません。':'観測範囲は限定されています。UIイベントが出ない操作は検出できません。';
  $('version').textContent=`投影規則: ${model.rule_version} · 外部送信なし · 明示的な再読込だけ`;
  const warnings=$('warnings'); warnings.replaceChildren();
  if(model.issues.length){warnings.append(el('strong',`不明・欠損・未完了: ${model.issues.length}件`));const d=el('details');d.append(el('summary','理由コードを見る'));for(const issue of model.issues)d.append(el('div',issue));warnings.append(d);}
  const flow=$('flow'), timeline=$('timeline');flow.replaceChildren();timeline.replaceChildren();
  const shown=model.steps.slice(0,200);
  for(let i=0;i<shown.length;i++){
    const step=shown[i],edge=edges.get(step.id);
    if(i>0){const separator=el('span',edge&&edge.from_id===shown[i-1].id?'→':'');separator.className=separator.textContent?'arrow':'break';flow.append(separator);}
    const button=el('button',label(step));button.onclick=()=>select(step.id);flow.append(button);
    const row=el('tr');row.append(el('td',String(step.seq)),el('td',new Date(step.at).toISOString().slice(11,23)));
    const cell=el('td'), pick=el('button',label(step));pick.onclick=()=>select(step.id);cell.append(pick);row.append(cell,el('td',edge?`${(edge.elapsed_ms/1000).toFixed(2)} 秒`:'不明／区間境界'));timeline.append(row);
  }
  if(model.steps.length>shown.length)flow.append(el('p',`先頭200件を表示中。記録総数は${model.steps.length}件です。`));
  if(!shown.length)flow.append(el('p','観測ステップはありません。Windowsアプリで記録、またはデモを選んでください。'));
  const list=$('artifacts');list.replaceChildren();for(const a of artifacts){const b=el('button',`${a.data.type} · ${a.data.bytes} bytes · ${a.data.ref.slice(0,8)}`);b.onclick=()=>select(a.id);list.append(b);}
  // A previous selection must never be mistaken for evidence from the new capture.
  $('evidence').textContent='ステップまたは出力参照を選んでください。';
}
async function refresh(){const button=$('refresh');button.disabled=true;try{const r=await fetch('model',{cache:'no-store',credentials:'omit'});if(!r.ok)throw Error('unavailable');render(await r.json());}catch{$('warnings').textContent='読込できません。Windowsアプリが動いているか確認してください。';}finally{button.disabled=false;}}
$('refresh').addEventListener('click',refresh);refresh();
