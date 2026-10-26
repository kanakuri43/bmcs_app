// スクリーンショットに赤枠・番号・矢印を重ねる。座標は画像ピクセル（ウィンドウ左上が原点）。
// <figure data-ann='[{"r":[x,y,w,h],"n":1,"b":[bx,by]}]'> の形で指定する。
//   r: 囲む矩形  n: 番号バッジ  b: バッジ中心（矩形の外に置くと矢印が付く。省略時は矩形の左上）
const NS = 'http://www.w3.org/2000/svg';
const RED = '#d9261c';

function el(name, attrs) {
  const e = document.createElementNS(NS, name);
  for (const k in attrs) e.setAttribute(k, attrs[k]);
  return e;
}

// 点(px,py)から矩形 r に一番近い点
function nearest([x, y, w, h], px, py) {
  return [Math.min(Math.max(px, x), x + w), Math.min(Math.max(py, y), y + h)];
}

function draw(fig, idx) {
  const img = fig.querySelector('img');
  const frame = fig.querySelector('.frame');
  frame.querySelector('svg')?.remove();
  const W = img.naturalWidth, H = img.naturalHeight;
  const s = W / img.clientWidth; // 表示px→画像px
  const sw = 3 * s, rad = 13 * s;
  const svg = el('svg', { viewBox: `0 0 ${W} ${H}` });
  const mid = `arrow${idx}`;
  const defs = el('defs', {});
  const m = el('marker', { id: mid, viewBox: '0 0 10 10', refX: 8, refY: 5, markerWidth: 4, markerHeight: 4, orient: 'auto-start-reverse' });
  m.appendChild(el('path', { d: 'M0,0 L10,5 L0,10 z', fill: RED }));
  defs.appendChild(m);
  svg.appendChild(defs);

  for (const a of JSON.parse(fig.dataset.ann)) {
    const [x, y, w, h] = a.r;
    const pad = 3 * s;
    svg.appendChild(el('rect', { x: x - pad, y: y - pad, width: w + 2 * pad, height: h + 2 * pad, rx: 4 * s, fill: 'rgba(217,38,28,.08)', stroke: RED, 'stroke-width': sw }));
    const [bx, by] = a.b ?? [x - pad, y - pad];
    if (a.b) {
      const [tx, ty] = nearest([x - pad, y - pad, w + 2 * pad, h + 2 * pad], bx, by);
      const d = Math.hypot(tx - bx, ty - by);
      if (d > rad) {
        const sx = bx + (tx - bx) / d * rad, sy = by + (ty - by) / d * rad;
        svg.appendChild(el('line', { x1: sx, y1: sy, x2: tx, y2: ty, stroke: RED, 'stroke-width': sw, 'marker-end': `url(#${mid})` }));
      }
    }
    if (a.n != null) {
      svg.appendChild(el('circle', { cx: bx, cy: by, r: rad, fill: RED, stroke: '#fff', 'stroke-width': 1.5 * s }));
      const t = el('text', { x: bx, y: by + 5.5 * s, 'text-anchor': 'middle', fill: '#fff', 'font-size': 16 * s, 'font-weight': 'bold', 'font-family': 'sans-serif' });
      t.textContent = a.n;
      svg.appendChild(t);
    }
  }
  frame.appendChild(svg);
}

document.querySelectorAll('figure[data-ann]').forEach((fig, i) => {
  const img = fig.querySelector('img');
  const go = () => draw(fig, i);
  img.complete ? go() : img.addEventListener('load', go);
  addEventListener('resize', go);
});
