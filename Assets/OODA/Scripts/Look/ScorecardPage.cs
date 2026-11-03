namespace Ooda.Look
{
    /// <summary>
    /// The rating page a scorecard run writes (rate.html in the run's folder): the automatic checks, the two
    /// Clean checks judged by eye, and the Pretty half: each golden shot from this run and the run before,
    /// shown as A and B in a random order, rated 1 to 5 on five questions. Reveal shows which was which and the
    /// total; Copy puts a one-line summary on the clipboard. Ratings are kept in the browser while you work.
    /// __DATA__ is replaced with the run's score.json.
    /// </summary>
    static class ScorecardPage
    {
        public const string Html = @"<!doctype html>
<html lang='en'><head><meta charset='utf-8'><meta name='viewport' content='width=device-width,initial-scale=1'>
<title>OODA scorecard</title>
<style>
:root{--bg:#0b130b;--panel:#122012;--line:#2f5a2f;--text:#cfeccf;--dim:#7fae7f;--hi:#9dff9d;--amber:#ffd84a;--red:#ff6a5a}
*{box-sizing:border-box}body{margin:0;background:var(--bg);color:var(--text);font:15px/1.45 'Share Tech Mono',ui-monospace,Consolas,monospace}
main{max-width:1500px;margin:0 auto;padding:16px}
h1{font-size:28px;margin:4px 0 2px;color:var(--hi);letter-spacing:1px}h2{font-size:19px;margin:28px 0 8px;color:var(--hi)}
.sub{color:var(--dim)}table{border-collapse:collapse;width:100%}td,th{border-bottom:1px solid var(--line);padding:6px 8px;text-align:left;vertical-align:top}
th{color:var(--dim);font-weight:normal}.pass{color:var(--hi)}.near{color:var(--amber)}.fail{color:var(--red)}
.card{background:var(--panel);border:1px solid var(--line);border-radius:6px;padding:12px;margin:12px 0}
.pair{display:grid;grid-template-columns:1fr 1fr;gap:12px}@media(max-width:900px){.pair{grid-template-columns:1fr}}
.pair img{width:100%;display:block;border:1px solid var(--line);border-radius:4px;cursor:zoom-in}
.tag{font-size:22px;color:var(--amber);margin:4px 0}.q{display:flex;justify-content:space-between;gap:8px;margin:3px 0;flex-wrap:wrap}
.q span{color:var(--dim)}.q label{margin-left:4px;cursor:pointer}.q input{accent-color:#7fdc7f}
button,select{background:#183018;color:var(--text);border:1px solid var(--line);border-radius:4px;padding:6px 12px;font:inherit;cursor:pointer}
button:hover{border-color:var(--hi)}.total{font-size:22px;color:var(--hi)}#zoom{position:fixed;inset:0;background:#000d;display:none;align-items:center;justify-content:center;z-index:9}
#zoom img{max-width:98vw;max-height:98vh}
</style></head><body><main>
<h1>OODA Clean &amp; Pretty scorecard</h1><div class='sub' id='meta'></div>
<h2>Clean: automatic checks</h2><table id='auto'></table>
<h2>Clean: by eye (this run)</h2>
<div class='card'><div class='q'><span>Text legibility: every line on a panel or shadowed, contrast good, nothing under 14 px (8 points)</span>
<select id='legib'><option value=''>choose</option><option value='8'>pass</option><option value='4'>near</option><option value='0'>fail</option></select></div>
<div class='q'><span>No overlapping or cut-off text in any shot (5 points)</span>
<select id='overlap'><option value=''>choose</option><option value='5'>pass</option><option value='2.5'>near</option><option value='0'>fail</option></select></div></div>
<h2>Pretty: rate each picture 1 to 5</h2><div class='sub' id='how'></div><div id='shots'></div>
<div class='card'><button id='reveal'>Reveal and total</button> <button id='copy'>Copy summary</button> <button id='clear'>Clear my ratings</button>
<div id='result' style='margin-top:10px'></div></div>
</main><div id='zoom'><img alt=''></div>
<script>
const D = __DATA__;
const Q = [['style','One style: ground, sky, planes and UI read as one world'],['focus','Eye goes to the fight: planes and plan seen first'],
 ['depth','Depth and light: haze, sun and shadow give distance'],['chrome','Clean chrome: only what I need, tidy and aligned'],['wow','Wow: I would screenshot this and share it']];
const key = 'ooda-scorecard-' + D.run;
let S = {}; try { S = JSON.parse(localStorage.getItem(key) || '{}'); } catch (e) {}
const save = () => { try { localStorage.setItem(key, JSON.stringify(S)); } catch (e) {} };
if (!S.order) { S.order = {}; D.shots.forEach(s => S.order[s.id] = Math.random() < 0.5); save(); }
S.r = S.r || {};
const el = (t, a, h) => { const e = document.createElement(t); if (a) Object.assign(e, a); if (h != null) e.innerHTML = h; return e; };
document.getElementById('meta').textContent = 'Run ' + D.run + ' · ' + D.resolution + ' · mood ' + D.mood + (D.prev ? ' · compared with ' + D.prev : ' · first run (baseline)');
const at = document.getElementById('auto');
at.appendChild(el('tr', null, '<th>Check</th><th>Measured</th><th>Target</th><th>Points</th>'));
D.checks.forEach(c => at.appendChild(el('tr', null, '<td>' + c.name + '</td><td>' + c.value + '</td><td>' + c.target + '</td><td class=' + c.grade + '>' + c.points + ' / ' + c.max + ' ' + c.grade + '</td>')));
at.appendChild(el('tr', null, '<td><b>Automatic total</b></td><td></td><td></td><td class=total>' + D.autoPoints + ' / 37</td>'));
document.getElementById('how').textContent = D.prev ? 'Each shot shows this run and the last one as A and B, in a random order. Rate both, then Reveal.' : 'First run: rate each shot once. The next run will show before and after.';
['legib', 'overlap'].forEach(id => { const s = document.getElementById(id); if (S[id] != null) s.value = S[id]; s.onchange = () => { S[id] = s.value; save(); }; });
const box = document.getElementById('shots');
D.shots.forEach(s => {
  const card = el('div', { className: 'card' }); card.appendChild(el('div', null, '<b>' + s.title + '</b> <span class=sub>(UI ' + s.ui + ', saturated ' + s.sat + ', pop ' + s.pop + ', depth ' + s.depth + ')</span>'));
  const pair = el('div', { className: 'pair' });
  const now = { who: 'now', src: 'shots/' + s.id + '.jpg' }, before = D.prev ? { who: 'prev', src: '../' + D.prev + '/shots/' + s.id + '.jpg' } : null;
  const list = before ? (S.order[s.id] ? [now, before] : [before, now]) : [now];
  list.forEach((v, i) => {
    const col = el('div'); col.appendChild(el('div', { className: 'tag' }, list.length > 1 ? 'ABCD'[i] : ''));
    const img = el('img', { src: v.src, alt: s.title, loading: 'lazy' }); img.onerror = () => { img.replaceWith(el('div', { className: 'sub' }, 'no picture')); };
    img.onclick = () => { const z = document.getElementById('zoom'); z.firstChild.src = v.src; z.style.display = 'flex'; };
    col.appendChild(img);
    Q.forEach(([q, text]) => {
      const row = el('div', { className: 'q' }); row.appendChild(el('span', null, text)); const opts = el('div');
      const name = s.id + '|' + v.who + '|' + q;
      for (let n = 1; n <= 5; n++) {
        const lab = el('label'); const r = el('input', { type: 'radio', name: name, value: n, checked: S.r[name] == n });
        r.onchange = () => { S.r[name] = n; save(); }; lab.appendChild(r); lab.appendChild(document.createTextNode(n)); opts.appendChild(lab);
      }
      row.appendChild(opts); col.appendChild(row);
    });
    pair.appendChild(col);
  });
  card.appendChild(pair); box.appendChild(card);
});
document.getElementById('zoom').onclick = e => e.currentTarget.style.display = 'none';
function pretty(who) {
  let sum = 0, n = 0, want = 0;
  D.shots.forEach(s => Q.forEach(([q]) => { want++; const v = S.r[s.id + '|' + who + '|' + q]; if (v) { sum += +v; n++; } }));
  return { score: n ? sum / n / 5 * 50 : NaN, done: n, of: want };
}
function summary() {
  const p = pretty('now'), b = D.prev ? pretty('prev') : null;
  const man = (S.legib !== undefined && S.legib !== '' ? +S.legib : NaN) + (S.overlap !== undefined && S.overlap !== '' ? +S.overlap : NaN);
  const clean = D.autoPoints + (isNaN(man) ? 0 : man);
  const f = v => isNaN(v) ? '?' : v.toFixed(1);
  let t = 'Scorecard ' + D.run + ': Clean ' + f(clean) + '/50 (auto ' + D.autoPoints + '/37, by eye ' + f(man) + '/13), Pretty ' + f(p.score) + '/50 (' + p.done + ' of ' + p.of + ' rated), Total ' + f(clean + p.score) + '/100';
  if (b) t += '; last run rated Pretty ' + f(b.score) + '/50 in this comparison';
  return t;
}
document.getElementById('reveal').onclick = () => {
  document.querySelectorAll('.tag').forEach(e => e.dataset.done = 1);
  D.shots.forEach((s, k) => { if (!D.prev) return; const tags = box.children[k].querySelectorAll('.tag'); const nowFirst = S.order[s.id];
    tags[0].textContent = (nowFirst ? 'A: this run' : 'A: last run'); tags[1].textContent = (nowFirst ? 'B: last run' : 'B: this run'); });
  document.getElementById('result').innerHTML = '<div class=total>' + summary() + '</div>';
};
document.getElementById('copy').onclick = () => { const t = summary(); (navigator.clipboard ? navigator.clipboard.writeText(t) : Promise.reject()).then(() => alert('Copied:\n' + t), () => prompt('Copy this:', t)); };
document.getElementById('clear').onclick = () => { if (confirm('Clear every rating on this page?')) { localStorage.removeItem(key); location.reload(); } };
</script></body></html>";
    }
}
