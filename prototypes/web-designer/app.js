(() => {
'use strict';

// ---------- 常量与状态 ----------
const PX = 4;                    // 1mm = 4px（设计逻辑像素）
const PAD_MM = 10;               // 画布四周留白（mm）
const REAL_FACTOR = 2;           // 「实际大小」= 1mm 8 点 = 4px*2（203dpi 打印比例）
let contentZoom = 1;             // 内容缩放（Ctrl+滚轮，设计时看整体 / 局部）
let viewMode = 'fit';            // 'fit' 画布铺满视口 | 'actual' 真实比例（1mm=8点）
let paperW = 100, paperH = 60;
let elements = [];               // 版式元素状态（坐标 = 标签内容区，0..paperW）
let selected = [];               // 选中的元素 id
let pendingType = null;
let serverUrl = 'http://127.0.0.1:53960';
let connected = false;

const $ = (id) => document.getElementById(id);
const uid = () => 'e' + Math.random().toString(36).slice(2, 10);
const mm = (px) => px / PX;
const pxv = (v) => v * PX;
const r2 = (v) => Math.round((Number(v) || 0) * 100) / 100;

// 画布（含 padding）总尺寸，逻辑 px
const canvasW = () => (paperW + PAD_MM * 2) * PX;
const canvasH = () => (paperH + PAD_MM * 2) * PX;
// 内容区偏移（padding 左上角），逻辑 px
const contentOX = () => PAD_MM * PX;
const contentOY = () => PAD_MM * PX;

// ---------- Konva ----------
const stage = new Konva.Stage({ container: 'stage', width: 100, height: 60 });
const layer = new Konva.Layer();
stage.add(layer);
let tr = createTransformer();

function createTransformer() {
  const t = new Konva.Transformer({
    rotateEnabled: false,
    keepRatio: false,
    anchorSize: 8,
    anchorStroke: '#1668dc',
    borderStroke: '#1668dc',
    borderDash: [4, 2],
  });
  t.on('transformend', () => {
    t.nodes().forEach((g) => {
      const e = elementById(g.id());
      if (!e) return;
      const r = g.getClientRect();
      e.x = mm(r.x) - PAD_MM;
      e.y = mm(r.y) - PAD_MM;
      e.w = mm(r.width);
      e.h = mm(r.height);
      if (e.type === 'QrCode') { e.w = e.h = Math.max(e.w, e.h); }
      if (e.type === 'Text') { e.fontH = e.h; }
      if (e.type === 'Barcode') { e.heightMm = e.h; }
    });
    render();
    renderProps();
  });
  return t;
}

// ---------- 视口模型（画布铺满视口 + 内容缩放 + 真实比例） ----------
function fitScale() {
  const vw = $('viewport').clientWidth, vh = $('viewport').clientHeight;
  const cw = canvasW(), ch = canvasH();
  return Math.max(0.05, Math.min((vw - 16) / cw, (vh - 16) / ch));
}
function totalScale() {
  return (viewMode === 'actual' ? REAL_FACTOR : fitScale()) * contentZoom;
}

function applyView() {
  const cw = canvasW(), ch = canvasH();
  const total = totalScale();
  stage.width(cw); stage.height(ch);
  stage.scale({ x: total, y: total });
  clampStage();
  const box = $('stageBox');
  box.style.width = (cw * total) + 'px';
  box.style.height = (ch * total + 20) + 'px';
  const vw = $('viewport').clientWidth, vh = $('viewport').clientHeight;
  box.style.left = Math.max(0, (vw - cw * total) / 2) + 'px';
  box.style.top = Math.max(0, (vh - ch * total - 20) / 2) + 'px';
  $('zoomLabel').textContent = Math.round(contentZoom * 100) + '%';
  drawRulers();
}

// 画布平移不越界：保证画布至少有一块区域在视口内，且不超出可视范围过多
function clampStage() {
  const total = totalScale();
  const cw = canvasW() * total, ch = canvasH() * total;
  const vw = $('viewport').clientWidth, vh = $('viewport').clientHeight;
  let x = stage.x(), y = stage.y();
  if (cw > vw) x = Math.min(0, Math.max(vw - cw, x));
  else x = 0;
  if (ch > vh) y = Math.min(0, Math.max(vh - ch, y));
  else y = 0;
  if (x !== stage.x()) stage.x(x);
  if (y !== stage.y()) stage.y(y);
}

function fitWindow() { viewMode = 'fit'; contentZoom = 1; stage.x(0); stage.y(0); applyView(); render(); }
function actualSize() { viewMode = 'actual'; contentZoom = 1; stage.x(0); stage.y(0); applyView(); render(); }

// ---------- 日志 ----------
function log(msg) {
  const box = $('logBox');
  const div = document.createElement('div');
  div.textContent = new Date().toLocaleTimeString('zh-CN', { hour12: false }) + '  ' + msg;
  box.appendChild(div);
  box.scrollTop = box.scrollHeight;
}
function status(msg) { $('statusText').textContent = msg; log(msg); }

// ---------- 元素创建 ----------
function defaultElement(type) {
  const id = uid();
  switch (type) {
    case 'Text':   return { id, type, x: 5, y: 5, w: 40, h: 5, fontH: 5, fontW: 5, mode: 'field', key: '', text: '', align: 'Left', padding: 1, border: 0, fitMode: 'wrap' };
    case 'Barcode':return { id, type, x: 5, y: 20, w: 50, h: 20, mode: 'field', key: '', text: '', border: 0, barcodeFormat: 'CODE128', displayValue: true, moduleWidth: 1 };
    case 'QrCode': return { id, type, x: 5, y: 20, w: 20, h: 20, mode: 'field', key: '', text: '', border: 0, qrEcc: 'M', qrMargin: 2 };
    case 'Image':  return { id, type, x: 5, y: 20, w: 20, h: 20, key: '', border: 0 };
    case 'Line':   return { id, type, x: 5, y: 5, w: 60, h: 0, thickness: 0.5 };
    case 'Region': return { id, type, x: 5, y: 5, w: 60, h: 30, border: 0.3, containerId: 'c' + Math.random().toString(36).slice(2, 8) };
  }
}

// ---------- 渲染 ----------
function render() {
  layer.destroyChildren();
  tr = createTransformer();
  layer.add(tr);
  drawGrid();
  // 标签内容区边界（比网格深一些，提示真实纸张范围）
  layer.add(new Konva.Rect({
    x: contentOX(), y: contentOY(),
    width: paperW * PX, height: paperH * PX,
    stroke: '#b0b8c4', strokeWidth: 1, dash: [8, 4], listening: false, strokeScaleEnabled: false,
  }));
  elements.forEach((e) => {
    const g = nodeFor(e);
    if (g) layer.add(g);
  });
  const selNodes = selected.map((id) => layer.findOne('#' + id)).filter(Boolean);
  tr.nodes(selNodes);
  layer.draw();
  drawRulers();
  refreshFields();
  $('paperInfo').textContent = '纸张 ' + paperW + ' x ' + paperH + ' mm（四周留白 ' + PAD_MM + ' mm）';
}

function drawGrid() {
  if (!$('gridCheck').checked) return;
  const step = 5 * PX;
  const ox = contentOX(), oy = contentOY();
  const w = paperW * PX, h = paperH * PX;
  for (let x = 0; x <= w; x += step) {
    layer.add(new Konva.Line({ points: [ox + x, oy, ox + x, oy + h], stroke: (x / step) % 2 === 0 ? '#dde4ec' : '#eef1f5', strokeWidth: 1, listening: false, strokeScaleEnabled: false }));
  }
  for (let y = 0; y <= h; y += step) {
    layer.add(new Konva.Line({ points: [ox, oy + y, ox + w, oy + y], stroke: (y / step) % 2 === 0 ? '#dde4ec' : '#eef1f5', strokeWidth: 1, listening: false, strokeScaleEnabled: false }));
  }
}

// 标尺覆盖整个画布（含 padding）：0 到 paper+2*PAD，单位 mm，随画布移动
function drawRulers() {
  const total = totalScale();
  const hR = $('hRuler'), vR = $('vRuler');
  hR.innerHTML = ''; vR.innerHTML = '';
  hR.style.width = (canvasW() * total) + 'px';
  vR.style.height = (canvasH() * total) + 'px';
  const wMm = paperW + PAD_MM * 2, hMm = paperH + PAD_MM * 2;
  for (let m = 0; m <= wMm; m++) {
    const x = m * PX * total;
    const isPaperEdge = m === PAD_MM || m === PAD_MM + paperW;
    if (m % 10 === 0 || isPaperEdge) {
      const line = document.createElement('div');
      line.className = 'ruler-line';
      line.style.cssText = 'left:' + x + 'px;top:0;width:' + (isPaperEdge ? 2 : 1) + 'px;height:14px;' + (isPaperEdge ? 'background:#1668dc;' : '');
      hR.appendChild(line);
      const t = document.createElement('div');
      t.className = 'ruler-text';
      t.style.cssText = 'left:' + (x + 2) + 'px;top:1px;' + (isPaperEdge ? 'color:#1668dc;font-weight:bold;' : '');
      t.textContent = m;
      hR.appendChild(t);
    } else if (m % 5 === 0) {
      const line = document.createElement('div');
      line.className = 'ruler-line';
      line.style.cssText = 'left:' + x + 'px;top:0;width:1px;height:8px';
      hR.appendChild(line);
    }
  }
  for (let m = 0; m <= hMm; m++) {
    const y = m * PX * total;
    const isPaperEdge = m === PAD_MM || m === PAD_MM + paperH;
    if (m % 10 === 0 || isPaperEdge) {
      const line = document.createElement('div');
      line.className = 'ruler-line';
      line.style.cssText = 'left:0;top:' + y + 'px;height:1px;width:14px;' + (isPaperEdge ? 'background:#1668dc;' : '');
      vR.appendChild(line);
      const t = document.createElement('div');
      t.className = 'ruler-text';
      t.style.cssText = 'left:2px;top:' + (y + 1) + 'px;' + (isPaperEdge ? 'color:#1668dc;font-weight:bold;' : '');
      t.textContent = m;
      vR.appendChild(t);
    } else if (m % 5 === 0) {
      const line = document.createElement('div');
      line.className = 'ruler-line';
      line.style.cssText = 'left:0;top:' + y + 'px;height:1px;width:8px';
      vR.appendChild(line);
    }
  }
}

function elementContent(e) {
  if (e.mode === 'literal') return e.text || '（固定值）';
  if (!e.key) return '（未绑定字段）';
  return e.key;
}

// ---------- 文本适应（自动换行 / 截断 / 缩小字体 / 不限制高度） ----------
function applyTextFit(text, e, content) {
  const wPx = Math.max(2, pxv(e.w) - 2 * pxv(e.padding || 0));
  const hPx = Math.max(2, pxv(e.h));
  text.width(wPx);
  text.wrap(e.fitMode === 'wrap' || e.fitMode === 'shrink' ? 'word' : 'none');
  text.ellipsis(false);
  if (e.fitMode === 'auto') {
    // 不限制高度：显示全部内容（框高度固定，但内容允许超出，便于观察真实占用）
    text.height(null);
    text.verticalAlign('top');
    text.text(content);
    return;
  }
  text.height(hPx);
  text.verticalAlign('middle');
  if (e.fitMode === 'truncate') {
    let t = content;
    if (text.measureSize(t).width > wPx) {
      let lo = 0, hi = t.length;
      while (lo < hi) {
        const mid = Math.ceil((lo + hi) / 2);
        const cand = t.slice(0, mid) + '…';
        if (text.measureSize(cand).width <= wPx) lo = mid; else hi = mid - 1;
      }
      t = t.slice(0, lo) + (lo < content.length ? '…' : '');
    }
    text.text(t);
  } else if (e.fitMode === 'shrink') {
    let fs = Math.max(1, pxv(e.fontH));
    const minFs = Math.max(1, pxv(1.5));
    text.fontSize(fs);
    let m = text.measureSize(content);
    while ((m.width > wPx || m.height > hPx) && fs > minFs) {
      fs = Math.max(minFs, fs - 0.5);
      text.fontSize(fs);
      m = text.measureSize(content);
    }
    text.text(content);
  } else {
    text.text(content);
  }
  text.clipFunc((ctx) => { ctx.beginPath(); ctx.rect(0, 0, wPx, hPx); });
}

// ---------- 条码 / 二维码实时渲染 ----------
function makeBarcodeCanvas(e) {
  const content = elementContent(e);
  const c = document.createElement('canvas');
  try {
    JsBarcode(c, content === '（未绑定字段）' ? ' ' : content, {
      format: e.barcodeFormat || 'CODE128',
      displayValue: e.displayValue !== false,
      width: Math.max(1, e.moduleWidth || 1),
      height: Math.max(10, Math.min(80, pxv(e.h || 20) * 0.85)),
      margin: 0,
      background: 'transparent',
    });
  } catch (ex) {
    const ctx = c.getContext('2d');
    ctx.fillStyle = '#f0f1f3';
    ctx.fillRect(0, 0, 10, 10);
  }
  return c;
}

function fitImageNode(node, wPx, hPx) {
  const iw = node.image().width, ih = node.image().height;
  if (!iw || !ih) { node.width(wPx); node.height(hPx); return; }
  const fit = Math.min(wPx / iw, hPx / ih);
  node.width(iw * fit); node.height(ih * fit);
  node.x((wPx - iw * fit) / 2); node.y((hPx - ih * fit) / 2);
}

function queueQrRender(holder, e, wPx, hPx) {
  const content = elementContent(e);
  const qr = qrcode(0, e.qrEcc || 'M');
  qr.addData(content === '（未绑定字段）' ? ' ' : content);
  qr.make();
  const dataUrl = qr.createDataURL(4, e.qrMargin == null ? 2 : e.qrMargin);
  const im = new Image();
  im.onload = () => {
    if (holder.isDestroyed()) return;
    const node = new Konva.Image({ image: im, listening: false });
    fitImageNode(node, wPx, hPx);
    holder.add(node);
    layer.draw();
  };
  im.src = dataUrl;
}

// ---------- 元素节点（Group：边框 + 内容；坐标 = padding 偏移 + 内容坐标） ----------
function nodeFor(e) {
  const x = contentOX() + pxv(e.x);
  const y = contentOY() + pxv(e.y);
  const w = Math.max(2, pxv(e.w)), h = Math.max(2, pxv(e.h));
  const g = new Konva.Group({ id: e.id, name: 'element', x, y, draggable: true });
  const borderW = Math.max(1, pxv(e.border || 0));

  switch (e.type) {
    case 'Text': {
      const rect = new Konva.Rect({ x: 0, y: 0, width: w, height: h, stroke: e.border > 0 ? '#000' : null, strokeWidth: borderW, strokeScaleEnabled: false });
      const text = new Konva.Text({
        x: pxv(e.padding || 0), y: 0, width: Math.max(2, w - 2 * pxv(e.padding || 0)),
        fontSize: Math.max(1, pxv(e.fontH)), fontFamily: 'Microsoft YaHei',
        fill: e.mode === 'field' && !e.key ? '#999' : '#000',
        listening: false, strokeScaleEnabled: false,
      });
      text.align(e.align === 'Center' ? 'center' : e.align === 'Right' ? 'right' : 'left');
      applyTextFit(text, e, elementContent(e));
      g.add(rect); g.add(text);
      return g;
    }
    case 'Barcode': {
      const rect = new Konva.Rect({ x: 0, y: 0, width: w, height: h, stroke: e.border > 0 ? '#000' : null, strokeWidth: borderW, strokeScaleEnabled: false });
      const canvas = makeBarcodeCanvas(e);
      const img = new Konva.Image({ image: canvas, listening: false });
      fitImageNode(img, w, h);
      g.add(rect); g.add(img);
      return g;
    }
    case 'QrCode': {
      const rect = new Konva.Rect({ x: 0, y: 0, width: w, height: h, stroke: e.border > 0 ? '#000' : null, strokeWidth: borderW, strokeScaleEnabled: false });
      g.add(rect);
      queueQrRender(g, e, w, h);
      return g;
    }
    case 'Image': {
      const rect = new Konva.Rect({ x: 0, y: 0, width: w, height: h, fill: '#f5f6f8', stroke: e.border > 0 ? '#000' : '#aab4c0', strokeWidth: borderW, dash: [4, 3], strokeScaleEnabled: false });
      const t = new Konva.Text({ x: 4, y: 4, text: '图片: ' + (e.key || ''), fontSize: 11, fontFamily: 'Microsoft YaHei', fill: '#6b7684', listening: false });
      g.add(rect); g.add(t);
      return g;
    }
    case 'Line': {
      const line = new Konva.Line({ x, y, points: [0, 0, pxv(e.w), pxv(e.h)], stroke: '#000', strokeWidth: Math.max(1, pxv(e.thickness || 0.5)), strokeScaleEnabled: false });
      line.id(e.id); line.name('element');
      line.draggable(true);
      return line;
    }
    case 'Region': {
      const rect = new Konva.Rect({ x: 0, y: 0, width: w, height: h, fill: 'rgba(0,128,255,0.06)', stroke: e.border > 0 ? '#000' : '#8a94a0', strokeWidth: borderW, dash: [6, 4], strokeScaleEnabled: false });
      const t = new Konva.Text({ x: 4, y: 2, text: '容器 ' + (e.containerId || ''), fontSize: 10, fontFamily: 'Microsoft YaHei', fill: '#7a8490', listening: false });
      g.add(rect); g.add(t);
      return g;
    }
    default:
      return null;
  }
}

function isElementTarget(target) {
  if (!target) return false;
  if (target.hasName && target.hasName('element')) return true;
  const p = target.getParent && target.getParent();
  return !!(p && p.hasName && p.hasName('element'));
}
function elementFromTarget(target) {
  if (target.hasName && target.hasName('element')) return target;
  const p = target.getParent && target.getParent();
  return p && p.hasName && p.hasName('element') ? p : null;
}

// ---------- 选择 ----------
function selectOnly(id) {
  selected = [id];
  render(); renderProps();
}
function toggleSelect(id) {
  if (selected.includes(id)) selected = selected.filter((x) => x !== id);
  else selected.push(id);
  render(); renderProps();
}
function clearSelection() {
  selected = [];
  render(); renderProps();
}

// ---------- 坐标换算（不依赖 Konva 指针状态，HTML5 拖拽期间也可靠） ----------
// viewX/Y：相对 stage 内容盒左上角的视觉像素
function viewToLogic(viewX, viewY) {
  const rect = $('stage').getBoundingClientRect();
  const sx = stage.scaleX(), sy = stage.scaleY();
  return {
    x: (viewX - stage.x()) / sx,
    y: (viewY - stage.y()) / sy,
  };
}
// 视觉像素 → 标签内容坐标（mm）
function viewToContentMm(viewX, viewY) {
  const l = viewToLogic(viewX, viewY);
  return { x: mm(l.x) - PAD_MM, y: mm(l.y) - PAD_MM };
}

// ---------- 画布交互 ----------
stage.on('click', (ev) => {
  const el = elementFromTarget(ev.target);
  if (!el) {
    if (pendingType) {
      const ptr = stage.getPointerPosition();
      if (ptr) addElementAt(pendingType, ptr.x, ptr.y);
      pendingType = null;
      return;
    }
    clearSelection();
    return;
  }
  if (ev.evt.shiftKey || ev.evt.ctrlKey) toggleSelect(el.id());
  else selectOnly(el.id());
});

// ---------- 智能参考线（拖动 / 缩放时辅助对齐） ----------
let guideLines = [];
function clearGuides() {
  guideLines.forEach((n) => n.destroy());
  guideLines = [];
}
function drawGuideV(x) {
  const n = new Konva.Line({ points: [x, 0, x, canvasH()], stroke: '#ff4d6d', strokeWidth: 1, dash: [5, 3], listening: false, strokeScaleEnabled: false });
  guideLines.push(n);
  layer.add(n);
}
function drawGuideH(y) {
  const n = new Konva.Line({ points: [0, y, canvasW(), y], stroke: '#ff4d6d', strokeWidth: 1, dash: [5, 3], listening: false, strokeScaleEnabled: false });
  guideLines.push(n);
  layer.add(n);
}
function snapNode(g) {
  const e = elementById(g.id());
  if (!e) return;
  const r = g.getClientRect();
  const TH = 6; // 吸附阈值（逻辑 px）
  const xs = [r.x, r.x + r.width / 2, r.x + r.width];
  const ys = [r.y, r.y + r.height / 2, r.y + r.height];
  // 候选线：画布（含 padding）边缘 / 中心 + 内容区边缘 / 中心
  const cx = [0, canvasW() / 2, canvasW(), contentOX(), contentOX() + paperW * PX / 2, contentOX() + paperW * PX];
  const cy = [0, canvasH() / 2, canvasH(), contentOY(), contentOY() + paperH * PX / 2, contentOY() + paperH * PX];
  elements.forEach((o) => {
    if (o.id === e.id) return;
    const n = layer.findOne('#' + o.id);
    if (!n) return;
    const or = n.getClientRect();
    cx.push(or.x, or.x + or.width / 2, or.x + or.width);
    cy.push(or.y, or.y + or.height / 2, or.y + or.height);
  });
  let bestDx = null, bestDy = null;
  xs.forEach((x) => { cx.forEach((c) => { const d = c - x; if (Math.abs(d) <= TH && (bestDx === null || Math.abs(d) < Math.abs(bestDx.d))) bestDx = { d, c }; }); });
  ys.forEach((y) => { cy.forEach((c) => { const d = c - y; if (Math.abs(d) <= TH && (bestDy === null || Math.abs(d) < Math.abs(bestDy.d))) bestDy = { d, c }; }); });
  clearGuides();
  if (bestDx) { g.x(g.x() + bestDx.d); drawGuideV(bestDx.c); }
  if (bestDy) { g.y(g.y() + bestDy.d); drawGuideH(bestDy.c); }
}

// 多选拖动：主节点拖动，其余节点跟随
let multiDrag = null;
stage.on('dragstart', (ev) => {
  const el = elementFromTarget(ev.target);
  if (el && selected.length > 1 && selected.includes(el.id())) {
    multiDrag = { targetId: el.id(), lastX: el.x(), lastY: el.y() };
  } else {
    multiDrag = null;
  }
});
stage.on('dragmove', (ev) => {
  const el = elementFromTarget(ev.target);
  if (!el) return;
  if (multiDrag) {
    const dx = el.x() - multiDrag.lastX;
    const dy = el.y() - multiDrag.lastY;
    multiDrag.lastX = el.x(); multiDrag.lastY = el.y();
    selected.forEach((id) => {
      if (id === multiDrag.targetId) return;
      const n = layer.findOne('#' + id);
      if (n) { n.x(n.x() + dx); n.y(n.y() + dy); }
    });
  }
  snapNode(el);
  layer.draw();
});
stage.on('dragend', (ev) => {
  multiDrag = null;
  clearGuides();
  const el = elementFromTarget(ev.target);
  if (!el) return;
  const e = elementById(el.id());
  if (!e) return;
  const r = el.getClientRect();
  e.x = mm(r.x) - PAD_MM;
  e.y = mm(r.y) - PAD_MM;
  renderProps();
  const container = containerHit(e);
  if (container) e.regionId = container.containerId; else delete e.regionId;
});

function commit() { render(); renderProps(); }

// Ctrl+滚轮：内容缩放（以鼠标为中心；画布仍铺满视口的基准不变）
stage.on('wheel', (ev) => {
  ev.evt.preventDefault();
  if (!ev.evt.ctrlKey) return;
  const oldZoom = contentZoom;
  contentZoom = Math.max(0.1, Math.min(8, contentZoom * (ev.evt.deltaY < 0 ? 1.1 : 1 / 1.1)));
  const base = viewMode === 'actual' ? REAL_FACTOR : fitScale();
  const oldTotal = base * oldZoom;
  const newTotal = base * contentZoom;
  const ptr = stage.getPointerPosition();
  if (ptr) {
    const sx = ptr.x, sy = ptr.y;
    stage.x(sx - ((sx - stage.x()) * newTotal / oldTotal));
    stage.y(sy - ((sy - stage.y()) * newTotal / oldTotal));
  }
  applyView();
  render();
});

// 中键平移（带边界限制）
let panning = false, panStart = { x: 0, y: 0 }, stageStart = { x: 0, y: 0 };
stage.on('mousedown', (ev) => {
  if (ev.evt.button === 1) {
    panning = true;
    panStart = { x: ev.evt.clientX, y: ev.evt.clientY };
    stageStart = { x: stage.x(), y: stage.y() };
    ev.evt.preventDefault();
  }
});
stage.on('mousemove', (ev) => {
  if (panning) {
    stage.x(stageStart.x + (ev.evt.clientX - panStart.x));
    stage.y(stageStart.y + (ev.evt.clientY - panStart.y));
    clampStage();
    layer.draw();
  }
});
stage.on('mouseup', (ev) => { if (ev.evt.button === 1) panning = false; });

// 键盘删除
document.addEventListener('keydown', (ev) => {
  const tag = ev.target && ev.target.tagName;
  if (tag === 'INPUT' || tag === 'SELECT' || tag === 'TEXTAREA') return;
  if (ev.key === 'Delete' || ev.key === 'Backspace') {
    if (selected.length) {
      const count = selected.length;
      elements = elements.filter((e) => !selected.includes(e.id));
      selected = [];
      status('已删除 ' + count + ' 个元素。');
      commit();
    }
  }
});

// 控件栏：点击放置 / 拖入
document.querySelectorAll('.palette button').forEach((btn) => {
  btn.draggable = true;
  btn.addEventListener('click', () => {
    pendingType = btn.dataset.type;
    status('点击画布放置「' + btn.textContent + '」，或直接拖到画布。');
  });
  btn.addEventListener('dragstart', (ev) => {
    ev.dataTransfer.setData('text/plain', btn.dataset.type);
    ev.dataTransfer.effectAllowed = 'copy';
  });
});
$('stageBox').addEventListener('dragover', (ev) => { ev.preventDefault(); ev.dataTransfer.dropEffect = 'copy'; });
$('stageBox').addEventListener('drop', (ev) => {
  ev.preventDefault();
  const type = ev.dataTransfer.getData('text/plain');
  if (!type) return;
  const stageRect = $('stage').getBoundingClientRect();
  const pos = viewToContentMm(ev.clientX - stageRect.left, ev.clientY - stageRect.top);
  addElementAt(type, pos.x, pos.y);
});

function addElementAt(type, contentX, contentY) {
  const e = defaultElement(type);
  e.x = Math.max(0, Math.min(paperW - 2, r2(contentX)));
  e.y = Math.max(0, Math.min(paperH - 2, r2(contentY)));
  elements.push(e);
  selected = [e.id];
  status('已添加「' + (e.type === 'Barcode' ? '条码' : e.type === 'QrCode' ? '二维码' : '文本') + '」。');
  commit();
}

function containerHit(e) {
  return elements.find((c) => c.type === 'Region' && c.containerId !== e.id &&
    e.x + e.w / 2 >= c.x && e.x + e.w / 2 <= c.x + c.w &&
    e.y + e.h / 2 >= c.y && e.y + e.h / 2 <= c.y + c.h);
}

function elementById(id) { return elements.find((e) => e.id === id); }

// ---------- 对齐（多选） ----------
function alignSelected(align) {
  const sel = elements.filter((e) => selected.includes(e.id) && e.type !== 'Region');
  if (sel.length < 2) { status('对齐需要至少 2 个元素（容器除外）。'); return; }
  const left = Math.min(...sel.map((e) => e.x));
  const right = Math.max(...sel.map((e) => e.x + e.w));
  const top = Math.min(...sel.map((e) => e.y));
  const bottom = Math.max(...sel.map((e) => e.y + e.h));
  sel.forEach((e) => {
    delete e.regionId;
    switch (align) {
      case 'left': e.x = left; break;
      case 'centerH': e.x = left + (right - left - e.w) / 2; break;
      case 'right': e.x = right - e.w; break;
      case 'top': e.y = top; break;
      case 'centerV': e.y = top + (bottom - top - e.h) / 2; break;
      case 'bottom': e.y = bottom - e.h; break;
    }
  });
  status('已对齐 ' + sel.length + ' 个元素。');
  commit();
}

// ---------- 字段自动推导 ----------
function refreshFields() {
  const keys = [];
  elements.forEach((e) => {
    if (!['Image', 'Line', 'Region'].includes(e.type) && e.mode === 'field' && e.key && !keys.includes(e.key)) keys.push(e.key);
  });
  const ul = $('fieldList');
  ul.innerHTML = '';
  if (!keys.length) {
    const li = document.createElement('li');
    li.textContent = '暂无字段';
    li.style.color = '#999';
    ul.appendChild(li);
    return;
  }
  keys.forEach((k) => {
    const li = document.createElement('li');
    li.textContent = k;
    ul.appendChild(li);
  });
}

// ---------- 属性面板 ----------
function renderProps() {
  const box = $('props');
  if (!selected.length) {
    box.innerHTML = '<div id="emptyProps">在画布上选中元素后显示属性。</div>';
    return;
  }
  const sel = elements.filter((e) => selected.includes(e.id));
  if (sel.length > 1) {
    box.innerHTML = '';
    const info = document.createElement('p');
    info.textContent = '已选 ' + sel.length + ' 个元素';
    info.style.fontWeight = 'bold';
    box.appendChild(info);
    const g = document.createElement('div');
    g.className = 'group';
    const h = document.createElement('h4');
    h.textContent = '对齐（以包围框为基准）';
    g.appendChild(h);
    [['左对齐', 'left'], ['水平居中', 'centerH'], ['右对齐', 'right'], ['上对齐', 'top'], ['垂直居中', 'centerV'], ['下对齐', 'bottom']].forEach(([label, key]) => {
      const b = document.createElement('button');
      b.textContent = label;
      b.style.margin = '2px';
      b.addEventListener('click', () => alignSelected(key));
      g.appendChild(b);
    });
    box.appendChild(g);
    const del = document.createElement('button');
    del.textContent = '删除选中';
    del.style.marginTop = '8px';
    del.addEventListener('click', () => {
      elements = elements.filter((e) => !selected.includes(e.id));
      selected = [];
      status('已删除选中元素。');
      commit();
    });
    box.appendChild(del);
    return;
  }

  const e = sel[0];
  box.innerHTML = '';
  const title = document.createElement('p');
  title.textContent = typeLabel(e) + (e.key ? ' (' + e.key + ')' : '');
  title.style.fontWeight = 'bold';
  box.appendChild(title);

  const gPos = document.createElement('div');
  gPos.className = 'group';
  gPos.innerHTML = '<h4>位置 / 尺寸（mm，相对标签内容区）</h4>';
  addNum(gPos, 'X', e.x, (v) => { e.x = v; });
  addNum(gPos, 'Y', e.y, (v) => { e.y = v; });
  if (e.type !== 'Line') {
    addNum(gPos, '宽', e.w, (v) => {
      e.w = Math.max(1, v);
      if (e.type === 'QrCode') e.h = e.w;
    });
    if (e.type !== 'Text') {
      addNum(gPos, '高', e.h, (v) => {
        e.h = Math.max(1, v);
        if (e.type === 'QrCode') e.w = e.h;
      });
    }
  }
  box.appendChild(gPos);

  if (e.type === 'Text' || e.type === 'Barcode' || e.type === 'QrCode') {
    box.appendChild(contentGroup(e));
  }

  if (e.type === 'Text') {
    const gText = document.createElement('div');
    gText.className = 'group';
    gText.innerHTML = '<h4>文本 / 字体</h4>';
    addNum(gText, '字高', e.fontH, (v) => { e.fontH = Math.max(1, v); e.h = e.fontH; });
    addSelect(gText, '文字对齐', [['左对齐', 'Left'], ['居中', 'Center'], ['右对齐', 'Right']], e.align, (v) => { e.align = v; });
    addNum(gText, '内边距', e.padding || 0, (v) => { e.padding = Math.max(0, v); });
    addNum(gText, '边框', e.border || 0, (v) => { e.border = Math.max(0, v); });
    addSelect(gText, '文本溢出', [['自动换行', 'wrap'], ['超长截断', 'truncate'], ['缩小字体', 'shrink'], ['不限制高度', 'auto']], e.fitMode || 'wrap', (v) => { e.fitMode = v; });
    box.appendChild(gText);
  } else if (e.type === 'Barcode') {
    const gBar = document.createElement('div');
    gBar.className = 'group';
    gBar.innerHTML = '<h4>条码参数</h4>';
    addSelect(gBar, '码制', [['Code128', 'CODE128'], ['EAN13', 'EAN13'], ['CODE39', 'CODE39'], ['UPC', 'UPC']], e.barcodeFormat || 'CODE128', (v) => { e.barcodeFormat = v; });
    addCheck(gBar, '底部显示文字', e.displayValue !== false, (v) => { e.displayValue = v; });
    addNum(gBar, '模块宽', e.moduleWidth || 1, (v) => { e.moduleWidth = Math.max(0.5, v); });
    addNum(gBar, '边框', e.border || 0, (v) => { e.border = Math.max(0, v); });
    box.appendChild(gBar);
  } else if (e.type === 'QrCode') {
    const gQr = document.createElement('div');
    gQr.className = 'group';
    gQr.innerHTML = '<h4>二维码参数</h4>';
    addSelect(gQr, '纠错级别', [['L(约7%)', 'L'], ['M(约15%)', 'M'], ['Q(约25%)', 'Q'], ['H(约30%)', 'H']], e.qrEcc || 'M', (v) => { e.qrEcc = v; });
    addNum(gQr, '边距', e.qrMargin == null ? 2 : e.qrMargin, (v) => { e.qrMargin = Math.max(0, v); });
    addNum(gQr, '边框', e.border || 0, (v) => { e.border = Math.max(0, v); });
    box.appendChild(gQr);
  } else if (e.type === 'Line') {
    const gLine = document.createElement('div');
    gLine.className = 'group';
    gLine.innerHTML = '<h4>线（兼容显示）</h4>';
    addNum(gLine, '长度 X', e.w, (v) => { e.w = v; });
    addNum(gLine, '长度 Y', e.h, (v) => { e.h = v; });
    addNum(gLine, '线宽', e.thickness || 0.5, (v) => { e.thickness = Math.max(0.1, v); });
    box.appendChild(gLine);
  } else if (e.type === 'Region') {
    const gR = document.createElement('div');
    gR.className = 'group';
    gR.innerHTML = '<h4>容器（兼容显示）</h4><p style="color:#777;font-size:11px">Id：' + e.containerId + '（只读）</p>';
    addNum(gR, '边框', e.border || 0, (v) => { e.border = Math.max(0, v); });
    box.appendChild(gR);
  } else if (e.type === 'Image') {
    const gR = document.createElement('div');
    gR.className = 'group';
    gR.innerHTML = '<h4>图片（兼容显示）</h4>';
    addNum(gR, '边框', e.border || 0, (v) => { e.border = Math.max(0, v); });
    box.appendChild(gR);
  }

  const del = document.createElement('button');
  del.textContent = '删除元素';
  del.style.marginTop = '8px';
  del.addEventListener('click', () => {
    elements = elements.filter((x) => x.id !== e.id);
    selected = [];
    status('已删除元素。');
    commit();
  });
  box.appendChild(del);
}

function typeLabel(e) {
  switch (e.type) {
    case 'Text': return '文本';
    case 'Barcode': return '条码';
    case 'QrCode': return '二维码';
    case 'Image': return '图片';
    case 'Line': return '线';
    case 'Region': return '容器';
  }
  return e.type;
}

function contentGroup(e) {
  const gContent = document.createElement('div');
  gContent.className = 'group';
  gContent.innerHTML = '<h4>填充（内容来源，值变化立即渲染）</h4>';
  const modeSel = document.createElement('select');
  [['字段填充', 'field'], ['固定值', 'literal']].forEach(([label, val]) => {
    const o = document.createElement('option');
    o.value = val; o.textContent = label;
    if (e.mode === val) o.selected = true;
    modeSel.appendChild(o);
  });
  modeSel.addEventListener('change', () => { e.mode = modeSel.value; commit(); });
  gContent.appendChild(modeSel);
  const keyInput = document.createElement('input');
  keyInput.placeholder = '字段 Key（输入后自动建立字段）';
  keyInput.value = e.key || '';
  keyInput.style.marginTop = '4px';
  keyInput.addEventListener('change', () => { e.key = keyInput.value.trim(); refreshFields(); commit(); });
  gContent.appendChild(keyInput);
  const litInput = document.createElement('input');
  litInput.placeholder = '固定值';
  litInput.value = e.text || '';
  litInput.style.marginTop = '4px';
  litInput.addEventListener('change', () => { e.text = litInput.value; commit(); });
  gContent.appendChild(litInput);
  const updateMode = () => {
    keyInput.style.display = modeSel.value === 'field' ? '' : 'none';
    litInput.style.display = modeSel.value === 'literal' ? '' : 'none';
  };
  updateMode();
  modeSel.addEventListener('change', updateMode);
  return gContent;
}

function addNum(parent, label, value, onSet) {
  const wrap = document.createElement('label');
  const span = document.createElement('span');
  span.textContent = label;
  const input = document.createElement('input');
  input.type = 'number';
  input.step = '0.5';
  input.value = Number(value || 0).toFixed(1);
  input.addEventListener('change', () => {
    const v = parseFloat(input.value);
    if (!isNaN(v)) onSet(v);
    commit();
  });
  wrap.appendChild(span);
  wrap.appendChild(input);
  parent.appendChild(wrap);
}

function addSelect(parent, label, options, value, onSet) {
  const wrap = document.createElement('label');
  const span = document.createElement('span');
  span.textContent = label;
  const sel = document.createElement('select');
  options.forEach(([text, val]) => {
    const o = document.createElement('option');
    o.value = val; o.textContent = text;
    if (value === val) o.selected = true;
    sel.appendChild(o);
  });
  sel.addEventListener('change', () => onSet(sel.value));
  wrap.appendChild(span);
  wrap.appendChild(sel);
  parent.appendChild(wrap);
}

function addCheck(parent, label, value, onSet) {
  const wrap = document.createElement('label');
  const span = document.createElement('span');
  span.textContent = label;
  const cb = document.createElement('input');
  cb.type = 'checkbox';
  cb.checked = !!value;
  cb.style.width = 'auto';
  cb.addEventListener('change', () => onSet(cb.checked));
  wrap.appendChild(span);
  wrap.appendChild(cb);
  parent.appendChild(wrap);
}

// ---------- WinHost API ----------
async function api(path, options) {
  const res = await fetch(serverUrl + path, options);
  if (!res.ok) {
    const body = await res.text();
    throw new Error(res.status + ' ' + body.slice(0, 200));
  }
  return res;
}

async function connect() {
  serverUrl = $('serverUrl').value.trim().replace(/\/+$/, '');
  try {
    const res = await fetch(serverUrl + '/healthz');
    if (!res.ok) throw new Error('HTTP ' + res.status);
    const health = await res.json();
    connected = true;
    $('connStatus').textContent = '已连接（' + (health.transport || '未知') + '）';
    $('connStatus').classList.add('on');
    status('已连接 WinHost：' + serverUrl);
    await refreshTemplateList();
  } catch (ex) {
    connected = false;
    $('connStatus').textContent = '未连接';
    $('connStatus').classList.remove('on');
    status('连接失败：' + ex.message);
  }
}

async function refreshTemplateList() {
  const list = $('templateList');
  list.innerHTML = '';
  const templates = await (await api('/api/templates')).json();
  templates.forEach((t) => {
    const o = document.createElement('option');
    o.value = t.name;
    o.textContent = t.name + (t.group ? '（' + t.group + '）' : '');
    list.appendChild(o);
  });
  if (templates.length) list.selectedIndex = 0;
}

async function loadTemplate() {
  const name = $('templateList').value;
  if (!name) return;
  try {
    const detail = await (await api('/api/templates/' + encodeURIComponent(name))).json();
    paperW = detail.layout.widthMm;
    paperH = detail.layout.heightMm;
    $('widthInput').value = paperW;
    $('heightInput').value = paperH;
    $('nameInput').value = detail.name;
    elements = (detail.layout.elements || []).map(parseElement).filter(Boolean);
    selected = [];
    applyView();
    render();
    renderProps();
    status('已加载模板：' + name + '（' + elements.length + ' 个元素）。');
  } catch (ex) {
    status('加载失败：' + ex.message);
  }
}

function parseElement(j) {
  const base = { id: uid(), x: j.xMm || 0, y: j.yMm || 0, border: j.borderMm || 0 };
  switch (j.type) {
    case 'text':
      return { ...base, type: 'Text', w: j.widthMm || 0, h: j.fontHeightMm, fontH: j.fontHeightMm, fontW: j.fontWidthMm || 5, mode: j.literal != null ? 'literal' : 'field', key: j.sourceKey || '', text: j.literal || '', align: j.textAlign || 'Left', padding: j.paddingMm || 0, fitMode: 'wrap', regionId: j.regionId || null };
    case 'barcode':
      return { ...base, type: 'Barcode', w: (j.heightMm || 20) * 2.5, h: j.heightMm || 20, heightMm: j.heightMm || 20, mode: j.literal != null ? 'literal' : 'field', key: j.sourceKey || '', text: j.literal || '', barcodeFormat: 'CODE128', displayValue: true, moduleWidth: 1, regionId: j.regionId || null };
    case 'qrcode':
      return { ...base, type: 'QrCode', w: j.sizeMm || 20, h: j.sizeMm || 20, mode: j.literal != null ? 'literal' : 'field', key: j.sourceKey || '', text: j.literal || '', qrEcc: 'M', qrMargin: 2, regionId: j.regionId || null };
    case 'image':
      return { ...base, type: 'Image', w: j.widthMm || 20, h: j.heightMm || 20, key: j.sourceKey || '', regionId: j.regionId || null };
    case 'line':
      return { ...base, type: 'Line', x: j.xMm, y: j.yMm, w: (j.x2Mm || 0) - j.xMm, h: (j.y2Mm || 0) - j.yMm, thickness: j.thicknessMm || 0.5 };
    case 'region':
      return { ...base, type: 'Region', w: j.widthMm || 60, h: j.heightMm || 30, containerId: j.id || 'c1' };
    default:
      return null;
  }
}

function toElementJson(e) {
  const base = { xMm: r2(e.x), yMm: r2(e.y), borderMm: r2(e.border || 0) };
  const field = () => ({ sourceKey: e.key || '', literal: null });
  const literal = () => ({ sourceKey: '', literal: e.text || '' });
  switch (e.type) {
    case 'Text':
      return { type: 'text', ...base, ...(e.mode === 'literal' ? literal() : field()), fontHeightMm: r2(e.fontH), fontWidthMm: r2(e.fontW), widthMm: r2(e.w), textAlign: e.align || 'Left', paddingMm: r2(e.padding || 0), borderMm: r2(e.border || 0) };
    case 'Barcode':
      return { type: 'barcode', ...base, ...(e.mode === 'literal' ? literal() : field()), heightMm: r2(e.heightMm || e.h), moduleWidth: 2, borderMm: r2(e.border || 0) };
    case 'QrCode':
      return { type: 'qrcode', ...base, ...(e.mode === 'literal' ? literal() : field()), sizeMm: r2(e.w), borderMm: r2(e.border || 0) };
    case 'Image':
      return { type: 'image', ...base, sourceKey: e.key || '', widthMm: r2(e.w), heightMm: r2(e.h), borderMm: r2(e.border || 0) };
    case 'Line':
      return { type: 'line', xMm: r2(e.x), yMm: r2(e.y), x2Mm: r2(e.x + e.w), y2Mm: r2(e.y + e.h), thicknessMm: r2(e.thickness || 0.5) };
    case 'Region':
      return { type: 'region', xMm: r2(e.x), yMm: r2(e.y), id: e.containerId, widthMm: r2(e.w), heightMm: r2(e.h), borderMm: r2(e.border || 0.3) };
  }
}

async function saveTemplate() {
  const name = $('nameInput').value.trim() || 'web-demo';
  const fieldKeys = [];
  elements.forEach((e) => {
    if (!['Image', 'Line', 'Region'].includes(e.type) && e.mode === 'field' && e.key && !fieldKeys.includes(e.key)) fieldKeys.push(e.key);
  });
  const payload = {
    name,
    group: '原型',
    contract: {
      name,
      version: '1.0',
      fields: fieldKeys.map((k) => ({ key: k, displayName: k, isRequired: false, type: 'Text' })),
    },
    layout: {
      name: name + '-layout',
      contractName: name,
      contractVersion: '1.0',
      widthMm: paperW,
      heightMm: paperH,
      elements: elements.map(toElementJson),
    },
  };
  try {
    await api('/api/templates', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(payload),
    });
    status('已保存模板：' + name);
    await refreshTemplateList();
  } catch (ex) {
    status('保存失败：' + ex.message);
  }
}

async function previewTemplate() {
  const name = $('nameInput').value.trim() || 'web-demo';
  try {
    const res = await api('/api/templates/' + encodeURIComponent(name) + '/preview', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ data: {} }),
    });
    const blob = await res.blob();
    const url = URL.createObjectURL(blob);
    $('previewImg').src = url;
    $('previewPanel').style.display = 'block';
    status('预览已生成（WinHost 渲染，真实打印效果）。');
  } catch (ex) {
    status('预览失败：' + ex.message);
  }
}

// ---------- 初始化 ----------
function init() {
  $('connectBtn').addEventListener('click', connect);
  $('loadBtn').addEventListener('click', loadTemplate);
  $('saveBtn').addEventListener('click', saveTemplate);
  $('previewBtn').addEventListener('click', previewTemplate);
  $('fitBtn').addEventListener('click', () => {
    $('fitBtn').classList.add('active');
    $('actualBtn').classList.remove('active');
    fitWindow();
  });
  $('actualBtn').addEventListener('click', () => {
    $('actualBtn').classList.add('active');
    $('fitBtn').classList.remove('active');
    actualSize();
  });
  $('newBtn').addEventListener('click', () => {
    paperW = parseFloat($('widthInput').value) || 100;
    paperH = parseFloat($('heightInput').value) || 60;
    elements = [];
    selected = [];
    pendingType = null;
    status('已新建空模板。');
    applyView();
    render();
    renderProps();
  });
  $('widthInput').addEventListener('change', () => { paperW = parseFloat($('widthInput').value) || 100; applyView(); render(); });
  $('heightInput').addEventListener('change', () => { paperH = parseFloat($('heightInput').value) || 60; applyView(); render(); });
  $('gridCheck').addEventListener('change', render);
  $('clearLogBtn').addEventListener('click', () => { $('logBox').innerHTML = ''; });
  $('closePreviewBtn').addEventListener('click', () => { $('previewPanel').style.display = 'none'; });
  window.addEventListener('resize', () => { applyView(); render(); });
  applyView();
  render();
  status('原型就绪：画布四周留白 10mm，标尺覆盖全画布；拖入控件到画布定位；1mm=8 点查看真实比例。');
}

init();
})();
