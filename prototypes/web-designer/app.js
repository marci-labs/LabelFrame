(() => {
'use strict';

// ---------- 常量与状态 ----------
const PX = 4;                    // 1mm = 4px（设计逻辑像素）
const PAD_MM = 10;               // 画布四周留白（mm）
const RULER = 20;                // 标尺区（逻辑 px，画进 Konva 与内容同坐标系）
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

// 画布总尺寸 = 标尺区 + 内容画布（padding + 标签），逻辑 px
const canvasW = () => RULER + (paperW + PAD_MM * 2) * PX;
const canvasH = () => RULER + (paperH + PAD_MM * 2) * PX;
// 内容画布（padding 左上角）偏移，逻辑 px
const contentOX = () => RULER + PAD_MM * PX;
const contentOY = () => RULER + PAD_MM * PX;

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
      const r = g.getClientRect({ relativeTo: layer });
      e.x = mm(r.x - RULER) - PAD_MM;
      e.y = mm(r.y - RULER) - PAD_MM;
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
  // 「适应窗口」= 画布略小于视口，便于预览全局
  return Math.max(0.05, Math.min((vw - 32) / cw, (vh - 32) / ch));
}
function totalScale() {
  return (viewMode === 'actual' ? REAL_FACTOR : fitScale()) * contentZoom;
}

function applyView() {
  const cw = canvasW(), ch = canvasH();
  const total = totalScale();
  // 比例尺（点/逻辑px 的缩放）：stage 尺寸 = 逻辑尺寸 * total，canvas 真实放大，内容完整可见
  stage.width(cw * total);
  stage.height(ch * total);
  stage.scale({ x: total, y: total });
  clampStage();
  const box = $('stageBox');
  box.style.width = (cw * total) + 'px';
  box.style.height = (ch * total) + 'px';
  const vw = $('viewport').clientWidth, vh = $('viewport').clientHeight;
  box.style.left = Math.max(0, (vw - cw * total) / 2) + 'px';
  box.style.top = Math.max(0, (vh - ch * total - 20) / 2) + 'px';
  $('zoomLabel').textContent = Math.round(contentZoom * 100) + '%';
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
    case 'Text':   return { id, type, x: 5, y: 5, w: 40, h: 5, fontH: 5, fontW: 5, mode: 'field', key: '', text: '', align: 'Left', padding: 1, border: 0, fitMode: 'shrink' };
    case 'Barcode':return { id, type, x: 5, y: 20, w: 50, h: 20, mode: 'field', key: '', text: '', border: 0, padding: 1, barcodeFormat: 'CODE128', displayValue: true, moduleWidth: 1 };
    case 'QrCode': return { id, type, x: 5, y: 20, w: 20, h: 20, mode: 'field', key: '', text: '', border: 0, padding: 1, qrEcc: 'M', qrMargin: 2 };
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
  drawRulersKonva();
  layer.draw();
  refreshFields();
  $('paperInfo').textContent = '纸张 ' + paperW + ' x ' + paperH + ' mm（四周留白 ' + PAD_MM + ' mm）';
}

function drawGrid() {
  if (!$('gridCheck').checked) return;
  const step = 5 * PX;
  const gw = (paperW + PAD_MM * 2) * PX, gh = (paperH + PAD_MM * 2) * PX;
  for (let x = 0; x <= gw; x += step) {
    layer.add(new Konva.Line({ points: [RULER + x, RULER, RULER + x, RULER + gh], stroke: (x / step) % 2 === 0 ? '#e3e9f0' : '#eef1f5', strokeWidth: 1, listening: false, strokeScaleEnabled: false }));
  }
  for (let y = 0; y <= gh; y += step) {
    layer.add(new Konva.Line({ points: [RULER, RULER + y, RULER + gw, RULER + y], stroke: (y / step) % 2 === 0 ? '#e3e9f0' : '#eef1f5', strokeWidth: 1, listening: false, strokeScaleEnabled: false }));
  }
}

// 标尺画进 Konva（与内容同一坐标系）：随画布平移 / 缩放天然对齐
function drawRulersKonva() {
  // 左上角
  layer.add(new Konva.Rect({ x: 0, y: 0, width: RULER, height: RULER, fill: '#f7f8fa', stroke: '#d8dee6', strokeWidth: 1, listening: false, strokeScaleEnabled: false }));
  const wMm = paperW + PAD_MM * 2, hMm = paperH + PAD_MM * 2;
  // 顶部标尺（0mm 对应内容画布左缘）
  for (let m = 0; m <= wMm; m++) {
    const x = RULER + m * PX;
    const isEdge = m === PAD_MM || m === PAD_MM + paperW;
    const len = m % 10 === 0 || isEdge ? 14 : m % 5 === 0 ? 9 : 4;
    const color = isEdge ? '#1668dc' : '#9aa6b4';
    const width = isEdge ? 2 : 1;
    layer.add(new Konva.Line({ points: [x, RULER - len, x, RULER], stroke: color, strokeWidth: width, listening: false, strokeScaleEnabled: false }));
    if (m % 10 === 0 || isEdge) {
      layer.add(new Konva.Text({ x: x + 2, y: RULER - 12, text: String(m), fontSize: 9, fontFamily: 'Consolas, Microsoft YaHei', fill: isEdge ? '#1668dc' : '#667', listening: false }));
    }
  }
  // 左侧标尺
  for (let m = 0; m <= hMm; m++) {
    const y = RULER + m * PX;
    const isEdge = m === PAD_MM || m === PAD_MM + paperH;
    const len = m % 10 === 0 || isEdge ? 14 : m % 5 === 0 ? 9 : 4;
    const color = isEdge ? '#1668dc' : '#9aa6b4';
    const width = isEdge ? 2 : 1;
    layer.add(new Konva.Line({ points: [RULER - len, y, RULER, y], stroke: color, strokeWidth: width, listening: false, strokeScaleEnabled: false }));
    if (m % 10 === 0 || isEdge) {
      layer.add(new Konva.Text({ x: RULER - 13, y: y + 2, text: String(m), fontSize: 9, fontFamily: 'Consolas, Microsoft YaHei', fill: isEdge ? '#1668dc' : '#667', listening: false }));
    }
  }
}

function elementContent(e) {
  if (e.mode === 'literal') return e.text || '（固定值）';
  if (!e.key) return '（未绑定字段）';
  return e.key;
}

// ---------- 文本适应（自动换行 / 截断 / 缩小字体 / 不限制高度） ----------
// 文本绘制：文本框 = 遮罩区域；内容按设置 缩小适应(shrink) / 原样溢出(overflow，超出被遮罩裁剪)
function applyTextFit(text, e, content) {
  const wPx = Math.max(2, pxv(e.w) - 2 * pxv(e.padding || 0));
  const hPx = Math.max(2, pxv(e.h));
  text.width(wPx);
  text.height(hPx);
  text.wrap('none');
  text.ellipsis(false);
  text.verticalAlign('middle');
  if (e.fitMode === 'overflow') {
    text.fontSize(Math.max(1, pxv(e.fontH)));
    text.text(content);
    return { wPx, hPx, clip: true };
  }
  // shrink：缩小字体以适应绘制区域（有最小字高）
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
  return { wPx, hPx, clip: true };
}

// ---------- 条码 / 二维码实时渲染 ----------
function makeBarcodeCanvas(e) {
  const content = elementContent(e);
  const isUnbound = content === '（未绑定字段）' || content === '（固定值）';
  const c = document.createElement('canvas');
  if (isUnbound) {
    // 未绑定：绘制可见占位（边框 + 文字），便于用户看到元素存在
    c.width = Math.max(100, pxv(e.w || 40));
    c.height = Math.max(40, pxv(e.h || 20));
    const ctx = c.getContext('2d');
    ctx.strokeStyle = '#9ab3d6';
    ctx.setLineDash([6, 4]);
    ctx.strokeRect(1, 1, c.width - 2, c.height - 2);
    ctx.fillStyle = '#7a8490';
    ctx.font = '14px "Microsoft YaHei"';
    ctx.textAlign = 'center';
    ctx.textBaseline = 'middle';
    ctx.fillText('条码 · 未绑定字段', c.width / 2, c.height / 2);
    return c;
  }
  try {
    JsBarcode(c, content, {
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

// 二维码同步渲染：直接遍历模块绘制到 canvas（无异步加载，稳定可见）
function makeQrCanvas(e, wPx, hPx) {
  const c = document.createElement('canvas');
  const content = elementContent(e);
  const isUnbound = content === '（未绑定字段）' || content === '（固定值）';
  if (isUnbound) {
    c.width = Math.max(60, wPx);
    c.height = Math.max(60, hPx);
    const ctx = c.getContext('2d');
    ctx.strokeStyle = '#9ab3d6';
    ctx.setLineDash([6, 4]);
    ctx.strokeRect(1, 1, c.width - 2, c.height - 2);
    ctx.fillStyle = '#7a8490';
    ctx.font = '14px "Microsoft YaHei"';
    ctx.textAlign = 'center';
    ctx.textBaseline = 'middle';
    ctx.fillText('二维码 · 未绑定字段', c.width / 2, c.height / 2);
    return c;
  }
  try {
    const qr = qrcode(0, e.qrEcc || 'M');
    qr.addData(content);
    qr.make();
    const count = qr.getModuleCount();
    const cell = 4;
    const margin = (e.qrMargin == null ? 2 : e.qrMargin) * cell;
    c.width = count * cell + margin * 2;
    c.height = count * cell + margin * 2;
    const ctx = c.getContext('2d');
    ctx.fillStyle = '#ffffff';
    ctx.fillRect(0, 0, c.width, c.height);
    ctx.fillStyle = '#000000';
    for (let r = 0; r < count; r++) {
      for (let col = 0; col < count; col++) {
        if (qr.isDark(r, col)) ctx.fillRect(margin + col * cell, margin + r * cell, cell, cell);
      }
    }
    return c;
  } catch (ex) {
    c.width = Math.max(60, wPx);
    c.height = Math.max(60, hPx);
    const ctx = c.getContext('2d');
    ctx.fillStyle = '#f0f1f3';
    ctx.fillRect(0, 0, c.width, c.height);
    return c;
  }
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
      const fit = applyTextFit(text, e, elementContent(e));
      g.add(rect);
      if (fit.clip) {
        // Konva 9 Text 无 clipFunc，用 Group 的 clip 属性裁剪
        const clip = new Konva.Group({ x: 0, y: 0, clipX: 0, clipY: 0, clipWidth: fit.wPx, clipHeight: fit.hPx });
        clip.add(text);
        g.add(clip);
      } else {
        g.add(text);
      }
      return g;
    }
    case 'Barcode': {
      const rect = new Konva.Rect({ x: 0, y: 0, width: w, height: h, stroke: e.border > 0 ? '#000' : null, strokeWidth: borderW, strokeScaleEnabled: false });
      const pad = pxv(e.padding || 0);
      const innerW = Math.max(2, w - pad * 2), innerH = Math.max(2, h - pad * 2);
      const canvas = makeBarcodeCanvas(e);
      const img = new Konva.Image({ image: canvas, listening: false });
      fitImageNode(img, innerW, innerH);
      img.x(img.x() + pad);
      img.y(img.y() + pad);
      g.add(rect); g.add(img);
      return g;
    }
    case 'QrCode': {
      const rect = new Konva.Rect({ x: 0, y: 0, width: w, height: h, stroke: e.border > 0 ? '#000' : null, strokeWidth: borderW, strokeScaleEnabled: false });
      const pad = pxv(e.padding || 0);
      const innerW = Math.max(2, w - pad * 2), innerH = Math.max(2, h - pad * 2);
      const canvas = makeQrCanvas(e, innerW, innerH);
      const img = new Konva.Image({ image: canvas, listening: false });
      fitImageNode(img, innerW, innerH);
      img.x(img.x() + pad);
      img.y(img.y() + pad);
      g.add(rect); g.add(img);
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
  return { x: mm(l.x - RULER) - PAD_MM, y: mm(l.y - RULER) - PAD_MM };
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
  const r = g.getClientRect({ relativeTo: layer });
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
    const or = n.getClientRect({ relativeTo: layer });
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
  const r = el.getClientRect({ relativeTo: layer });
  e.x = mm(r.x - RULER) - PAD_MM;
  e.y = mm(r.y - RULER) - PAD_MM;
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

// 中键平移（原生 DOM + document 级监听，保证松开必复位、不粘滞）
let panning = false, panStart = { x: 0, y: 0 }, stageStart = { x: 0, y: 0 };
const stageDom = document.getElementById('stage');
function panMove(ev) {
  if (!panning) return;
  stage.x(stageStart.x + (ev.clientX - panStart.x));
  stage.y(stageStart.y + (ev.clientY - panStart.y));
  clampStage();
  layer.draw();
}
function panEnd() {
  if (!panning) return;
  panning = false;
  document.removeEventListener('mousemove', panMove);
  document.removeEventListener('mouseup', panEnd);
}
stageDom.addEventListener('mousedown', (ev) => {
  if (ev.button === 1) {
    ev.preventDefault();
    panning = true;
    panStart = { x: ev.clientX, y: ev.clientY };
    stageStart = { x: stage.x(), y: stage.y() };
    document.addEventListener('mousemove', panMove);
    document.addEventListener('mouseup', panEnd);
  }
});
// 兜底：窗口失焦也复位
window.addEventListener('blur', panEnd);

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
    const gCommon = document.createElement('div');
    gCommon.className = 'group';
    gCommon.innerHTML = '<h4>边框 / 内边距（通用）</h4>';
    addNum(gCommon, '内边距', e.padding || 0, (v) => { e.padding = Math.max(0, v); });
    addNum(gCommon, '边框', e.border || 0, (v) => { e.border = Math.max(0, v); });
    box.appendChild(gCommon);
  }

  if (e.type === 'Text') {
    const gText = document.createElement('div');
    gText.className = 'group';
    gText.innerHTML = '<h4>文本 / 字体</h4>';
    addNum(gText, '字高', e.fontH, (v) => { e.fontH = Math.max(1, v); e.h = e.fontH; });
    addSelect(gText, '文字对齐', [['左对齐', 'Left'], ['居中', 'Center'], ['右对齐', 'Right']], e.align, (v) => { e.align = v; });
    addSelect(gText, '溢出处理', [['缩小适应', 'shrink'], ['溢出显示', 'overflow']], e.fitMode || 'shrink', (v) => { e.fitMode = v; });
    box.appendChild(gText);
  } else if (e.type === 'Barcode') {
    const gBar = document.createElement('div');
    gBar.className = 'group';
    gBar.innerHTML = '<h4>条码参数</h4>';
    addSelect(gBar, '码制', [['Code128', 'CODE128'], ['EAN13', 'EAN13'], ['CODE39', 'CODE39'], ['UPC', 'UPC']], e.barcodeFormat || 'CODE128', (v) => { e.barcodeFormat = v; });
    addCheck(gBar, '底部显示文字', e.displayValue !== false, (v) => { e.displayValue = v; });
    addNum(gBar, '模块宽', e.moduleWidth || 1, (v) => { e.moduleWidth = Math.max(0.5, v); });
    box.appendChild(gBar);
  } else if (e.type === 'QrCode') {
    const gQr = document.createElement('div');
    gQr.className = 'group';
    gQr.innerHTML = '<h4>二维码参数</h4>';
    addSelect(gQr, '纠错级别', [['L(约7%)', 'L'], ['M(约15%)', 'M'], ['Q(约25%)', 'Q'], ['H(约30%)', 'H']], e.qrEcc || 'M', (v) => { e.qrEcc = v; });
    addNum(gQr, '边距', e.qrMargin == null ? 2 : e.qrMargin, (v) => { e.qrMargin = Math.max(0, v); });
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
  sel.addEventListener('change', () => { onSet(sel.value); commit(); });
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
  cb.addEventListener('change', () => { onSet(cb.checked); commit(); });
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
      return { ...base, type: 'Text', w: j.widthMm || 0, h: j.fontHeightMm, fontH: j.fontHeightMm, fontW: j.fontWidthMm || 5, mode: j.literal != null ? 'literal' : 'field', key: j.sourceKey || '', text: j.literal || '', align: j.textAlign || 'Left', padding: j.paddingMm || 0, fitMode: 'shrink', regionId: j.regionId || null };
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
