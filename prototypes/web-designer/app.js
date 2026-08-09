(() => {
'use strict';

// ---------- 常量与状态 ----------
const PX = 4;                 // 1mm = 4px（100%）
let zoom = 1;                 // 画布缩放
let paperW = 100, paperH = 60;
let elements = [];            // 版式元素状态
let selected = [];            // 选中的元素 id
let pendingType = null;       // 控件栏待放置类型
let serverUrl = 'http://127.0.0.1:53960';
let connected = false;

const $ = (id) => document.getElementById(id);
const uid = () => 'e' + Math.random().toString(36).slice(2, 10);
const mm = (px) => px / PX / zoom;
const pxv = (v) => v * PX * zoom;
const r2 = (v) => Math.round((Number(v) || 0) * 100) / 100;

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
    t.nodes().forEach((node) => {
      const e = elementById(node.id());
      if (!e) return;
      if (e.type === 'Line') {
        const sX = node.scaleX(), sY = node.scaleY();
        const pts = node.points().slice();
        node.points(pts.map((p, i) => i % 2 === 0 ? p * sX : p * sY));
        node.scaleX(1); node.scaleY(1);
        const r = node.getClientRect();
        e.w = mm(r.width); e.h = mm(r.height);
        e.x = mm(node.x()); e.y = mm(node.y());
        return;
      }
      const w = Math.max(2, node.width() * node.scaleX());
      const h = Math.max(2, node.height() * node.scaleY());
      node.width(w); node.height(h); node.scaleX(1); node.scaleY(1);
      const wMm = mm(w), hMm = mm(h);
      if (e.type === 'Text') {
        e.w = wMm; e.h = hMm; e.fontH = hMm;
      } else if (e.type === 'QrCode') {
        const size = Math.max(wMm, hMm);
        e.w = size; e.h = size;
        node.width(pxv(size)); node.height(pxv(size));
      } else if (e.type === 'Barcode') {
        e.w = wMm; e.h = hMm; e.heightMm = hMm;
      } else {
        e.w = wMm; e.h = hMm;
      }
      e.x = mm(node.x());
      e.y = mm(node.y());
    });
    render();
    renderProps();
  });
  return t;
}

// ---------- 日志 ----------
function log(msg) {
  const box = $('logBox');
  const div = document.createElement('div');
  div.textContent = new Date().toLocaleTimeString('zh-CN', { hour12: false }) + '  ' + msg;
  box.appendChild(div);
  box.scrollTop = box.scrollHeight;
}
function status(msg) {
  $('statusText').textContent = msg;
  log(msg);
}

// ---------- 元素创建 ----------
function defaultElement(type) {
  const id = uid();
  switch (type) {
    case 'Text':   return { id, type, x: 5, y: 5, w: 40, h: 5, fontH: 5, fontW: 5, mode: 'field', key: '', text: '', align: 'Left', padding: 0, border: 0 };
    case 'Barcode':return { id, type, x: 5, y: 20, w: 50, h: 20, heightMm: 20, mode: 'field', key: '', text: '', border: 0 };
    case 'QrCode': return { id, type, x: 5, y: 20, w: 20, h: 20, mode: 'field', key: '', text: '', border: 0 };
    case 'Image':  return { id, type, x: 5, y: 20, w: 20, h: 20, key: '', border: 0 };
    case 'Line':   return { id, type, x: 5, y: 5, w: 60, h: 0, thickness: 0.5 };
    case 'Region': return { id, type, x: 5, y: 5, w: 60, h: 30, border: 0.3, containerId: 'c' + Math.random().toString(36).slice(2, 8) };
  }
}

// ---------- 渲染 ----------
function render() {
  stage.width(paperW * PX * zoom);
  stage.height(paperH * PX * zoom);
  layer.destroyChildren();
  tr = createTransformer();
  layer.add(tr);
  drawGrid();
  elements.forEach((e) => {
    const nodes = nodeFor(e);
    const list = Array.isArray(nodes) ? nodes : [nodes];
    list.forEach((n) => layer.add(n));
  });
  const selNodes = selected.map((id) => layer.findOne('#' + id)).filter(Boolean);
  tr.nodes(selNodes);
  layer.draw();
  drawRulers();
  refreshFields();
  $('paperInfo').textContent = '纸张 ' + paperW + ' x ' + paperH + ' mm';
}

function drawGrid() {
  if (!$('gridCheck').checked) return;
  const step = pxv(5);
  const w = paperW * PX * zoom, h = paperH * PX * zoom;
  for (let x = 0; x <= w; x += step) {
    layer.add(new Konva.Line({ points: [x, 0, x, h], stroke: (x / step) % 2 === 0 ? '#dde4ec' : '#eef1f5', strokeWidth: 1, listening: false }));
  }
  for (let y = 0; y <= h; y += step) {
    layer.add(new Konva.Line({ points: [0, y, w, y], stroke: (y / step) % 2 === 0 ? '#dde4ec' : '#eef1f5', strokeWidth: 1, listening: false }));
  }
}

function drawRulers() {
  const hR = $('hRuler'), vR = $('vRuler');
  hR.innerHTML = ''; vR.innerHTML = '';
  hR.style.width = (paperW * PX * zoom) + 'px';
  vR.style.height = (paperH * PX * zoom) + 'px';
  for (let m = 0; m <= paperW; m++) {
    const x = pxv(m);
    if (m % 10 === 0) {
      const line = document.createElement('div');
      line.className = 'ruler-line';
      line.style.cssText = 'left:' + x + 'px;top:0;width:1px;height:14px';
      hR.appendChild(line);
      const t = document.createElement('div');
      t.className = 'ruler-text';
      t.style.cssText = 'left:' + (x + 2) + 'px;top:1px';
      t.textContent = m;
      hR.appendChild(t);
    } else if (m % 5 === 0) {
      const line = document.createElement('div');
      line.className = 'ruler-line';
      line.style.cssText = 'left:' + x + 'px;top:0;width:1px;height:8px';
      hR.appendChild(line);
    }
  }
  for (let m = 0; m <= paperH; m++) {
    const y = pxv(m);
    if (m % 10 === 0) {
      const line = document.createElement('div');
      line.className = 'ruler-line';
      line.style.cssText = 'left:0;top:' + y + 'px;height:1px;width:14px';
      vR.appendChild(line);
      const t = document.createElement('div');
      t.className = 'ruler-text';
      t.style.cssText = 'left:2px;top:' + (y + 1) + 'px';
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

function nodeFor(e) {
  const x = pxv(e.x), y = pxv(e.y);
  switch (e.type) {
    case 'Text': {
      const t = new Konva.Text({
        id: e.id, name: 'element', x, y,
        width: Math.max(8, pxv(e.w)), height: Math.max(8, pxv(e.h)),
        text: elementContent(e), fontSize: Math.max(8, pxv(e.fontH)),
        fontFamily: 'Microsoft YaHei',
        fill: e.mode === 'field' && !e.key ? '#999' : '#000',
        draggable: true,
        padding: pxv(e.padding || 0),
        stroke: e.border > 0 ? '#000' : null,
        strokeWidth: Math.max(1, pxv(e.border || 0)),
      });
      t.align(e.align === 'Center' ? 'center' : e.align === 'Right' ? 'right' : 'left');
      return t;
    }
    case 'Barcode':
    case 'QrCode':
    case 'Image': {
      const label = e.type === 'Barcode' ? '条码' : e.type === 'QrCode' ? '二维码' : '图片';
      const rect = new Konva.Rect({
        id: e.id, name: 'element', x, y,
        width: Math.max(8, pxv(e.w)), height: Math.max(8, pxv(e.h)),
        fill: '#f5f6f8', stroke: e.border > 0 ? '#000' : '#aab4c0',
        strokeWidth: Math.max(1, pxv(e.border || 0)), dash: [4, 3], draggable: true,
      });
      const t = new Konva.Text({
        x: x + 2, y: y + 2, text: label + ': ' + elementContent(e),
        fontSize: Math.max(9, 11 * zoom), fontFamily: 'Microsoft YaHei', fill: '#6b7684', listening: false,
      });
      return [rect, t];
    }
    case 'Line': {
      return new Konva.Line({
        id: e.id, name: 'element', x, y,
        points: [0, 0, pxv(e.w), pxv(e.h)],
        stroke: '#000', strokeWidth: Math.max(1, pxv(e.thickness || 0.5)),
        draggable: true,
      });
    }
    case 'Region': {
      const rect = new Konva.Rect({
        id: e.id, name: 'element', x, y,
        width: Math.max(8, pxv(e.w)), height: Math.max(8, pxv(e.h)),
        fill: 'rgba(0,128,255,0.06)', stroke: e.border > 0 ? '#000' : '#8a94a0',
        strokeWidth: Math.max(1, pxv(e.border || 0)), dash: [6, 4], draggable: true,
      });
      const t = new Konva.Text({
        x: x + 4, y: y + 2, text: '容器 ' + (e.containerId || ''),
        fontSize: Math.max(9, 10 * zoom), fontFamily: 'Microsoft YaHei', fill: '#7a8490', listening: false,
      });
      return [rect, t];
    }
  }
}

// ---------- 选择 ----------
function selectOnly(id) {
  selected = [id];
  render();
  renderProps();
}
function toggleSelect(id) {
  if (selected.includes(id)) selected = selected.filter((x) => x !== id);
  else selected.push(id);
  render();
  renderProps();
}
function clearSelection() {
  selected = [];
  render();
  renderProps();
}

// ---------- 画布交互 ----------
stage.on('click', (ev) => {
  const target = ev.target;
  if (!target || !target.hasName('element')) {
    if (pendingType) {
      const ptr = stage.getPointerPosition();
      addElementAt(pendingType, ptr.x, ptr.y);
      pendingType = null;
      return;
    }
    clearSelection();
    return;
  }
  if (ev.evt.shiftKey || ev.evt.ctrlKey) toggleSelect(target.id());
  else selectOnly(target.id());
});

stage.on('dragend', (ev) => {
  const node = ev.target;
  if (!node || !node.hasName('element')) return;
  const e = elementById(node.id());
  if (!e) return;
  e.x = mm(node.x());
  e.y = mm(node.y());
  const container = containerHit(e);
  if (container) {
    e.regionId = container.containerId;
    e.x = container.x + (container.w - e.w) / 2;
    e.y = container.y + (container.h - e.h) / 2;
    status('元素已放入容器 ' + container.containerId + '（居中）。');
  } else {
    delete e.regionId;
  }
  render();
  renderProps();
});

// Ctrl+滚轮缩放（以鼠标为中心）
stage.on('wheel', (ev) => {
  ev.evt.preventDefault();
  if (!ev.evt.ctrlKey) return;
  const oldZoom = zoom;
  zoom = Math.max(0.25, Math.min(4, zoom * (ev.evt.deltaY < 0 ? 1.1 : 1 / 1.1)));
  const pointer = stage.getPointerPosition();
  if (pointer) {
    const rx = (pointer.x - stage.x()) / oldZoom;
    const ry = (pointer.y - stage.y()) / oldZoom;
    stage.x(pointer.x - rx * zoom);
    stage.y(pointer.y - ry * zoom);
  }
  $('zoomLabel').textContent = Math.round(zoom * 100) + '%';
  render();
});

// 中键平移
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
      render();
      renderProps();
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
  const rect = $('stage').getBoundingClientRect();
  addElementAt(type, ev.clientX - rect.left, ev.clientY - rect.top);
});

function addElementAt(type, viewX, viewY) {
  const e = defaultElement(type);
  const world = { x: viewX - stage.x(), y: viewY - stage.y() };
  e.x = Math.max(0, Math.min(paperW - 2, world.x / PX / zoom));
  e.y = Math.max(0, Math.min(paperH - 2, world.y / PX / zoom));
  elements.push(e);
  selected = [e.id];
  status('已添加「' + type + '」。');
  render();
  renderProps();
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
  render();
}

// ---------- 字段自动推导 ----------
function refreshFields() {
  const keys = [];
  elements.forEach((e) => {
    if (e.type !== 'Region' && e.mode === 'field' && e.key && !keys.includes(e.key)) keys.push(e.key);
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
      render();
      renderProps();
    });
    box.appendChild(del);
    return;
  }

  const e = sel[0];
  box.innerHTML = '';
  const title = document.createElement('p');
  title.textContent = (e.type === 'Region' ? '容器 (' + e.containerId + ')' : e.type + (e.key ? ' (' + e.key + ')' : ''));
  title.style.fontWeight = 'bold';
  box.appendChild(title);

  const gPos = document.createElement('div');
  gPos.className = 'group';
  gPos.innerHTML = '<h4>位置 / 尺寸（mm）</h4>';
  addNum(gPos, 'X', e.x, (v) => { e.x = v; });
  addNum(gPos, 'Y', e.y, (v) => { e.y = v; });
  if (e.type !== 'Line') {
    addNum(gPos, '宽', e.w, (v) => {
      e.w = Math.max(0, v);
      if (e.type === 'QrCode') { e.h = e.w; }
      if (e.type === 'Text') { e.h = e.fontH; }
    });
    if (e.type !== 'Text') {
      addNum(gPos, '高', e.h, (v) => {
        e.h = Math.max(0, v);
        if (e.type === 'QrCode') { e.w = e.h; }
      });
    }
  }
  box.appendChild(gPos);

  if (e.type === 'Text' || e.type === 'Barcode' || e.type === 'QrCode') {
    const gContent = document.createElement('div');
    gContent.className = 'group';
    gContent.innerHTML = '<h4>填充（内容来源）</h4>';
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
    keyInput.placeholder = '字段 Key';
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
    box.appendChild(gContent);
  }

  if (e.type === 'Text') {
    const gText = document.createElement('div');
    gText.className = 'group';
    gText.innerHTML = '<h4>文本 / 字体</h4>';
    addNum(gText, '字高', e.fontH, (v) => { e.fontH = Math.max(1, v); e.h = e.fontH; });
    const alignSel = document.createElement('select');
    [['左对齐', 'Left'], ['居中', 'Center'], ['右对齐', 'Right']].forEach(([label, val]) => {
      const o = document.createElement('option');
      o.value = val; o.textContent = label;
      if (e.align === val) o.selected = true;
      alignSel.appendChild(o);
    });
    const row = document.createElement('label');
    row.innerHTML = '<span>文字对齐</span>';
    alignSel.addEventListener('change', () => { e.align = alignSel.value; commit(); });
    row.appendChild(alignSel);
    gText.appendChild(row);
    addNum(gText, '内边距', e.padding || 0, (v) => { e.padding = Math.max(0, v); });
    addNum(gText, '边框', e.border || 0, (v) => { e.border = Math.max(0, v); });
    box.appendChild(gText);
  } else if (e.type === 'Line') {
    const gLine = document.createElement('div');
    gLine.className = 'group';
    gLine.innerHTML = '<h4>线</h4>';
    addNum(gLine, '长度 X', e.w, (v) => { e.w = v; });
    addNum(gLine, '长度 Y', e.h, (v) => { e.h = v; });
    addNum(gLine, '线宽', e.thickness || 0.5, (v) => { e.thickness = Math.max(0.1, v); });
    box.appendChild(gLine);
  } else if (e.type === 'Region') {
    const gR = document.createElement('div');
    gR.className = 'group';
    gR.innerHTML = '<h4>容器</h4><p style="color:#777;font-size:11px">Id：' + e.containerId + '（只读）</p>';
    addNum(gR, '边框', e.border || 0, (v) => { e.border = Math.max(0, v); });
    box.appendChild(gR);
  } else {
    const gR = document.createElement('div');
    gR.className = 'group';
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
    render();
    renderProps();
  });
  box.appendChild(del);

  function commit() { render(); renderProps(); }
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
    render();
    renderProps();
  });
  wrap.appendChild(span);
  wrap.appendChild(input);
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
      return { ...base, type: 'Text', w: j.widthMm || 0, h: j.fontHeightMm, fontH: j.fontHeightMm, fontW: j.fontWidthMm || 5, mode: j.literal != null ? 'literal' : 'field', key: j.sourceKey || '', text: j.literal || '', align: j.textAlign || 'Left', padding: j.paddingMm || 0, regionId: j.regionId || null };
    case 'barcode':
      return { ...base, type: 'Barcode', w: (j.heightMm || 20) * 2.5, h: j.heightMm || 20, heightMm: j.heightMm || 20, mode: j.literal != null ? 'literal' : 'field', key: j.sourceKey || '', text: j.literal || '', regionId: j.regionId || null };
    case 'qrcode':
      return { ...base, type: 'QrCode', w: j.sizeMm || 20, h: j.sizeMm || 20, mode: j.literal != null ? 'literal' : 'field', key: j.sourceKey || '', text: j.literal || '', regionId: j.regionId || null };
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
    if (e.type !== 'Region' && e.mode === 'field' && e.key && !fieldKeys.includes(e.key)) fieldKeys.push(e.key);
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
    status('预览已生成（WinHost 渲染）。');
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
  $('newBtn').addEventListener('click', () => {
    paperW = parseFloat($('widthInput').value) || 100;
    paperH = parseFloat($('heightInput').value) || 60;
    elements = [];
    selected = [];
    pendingType = null;
    status('已新建空模板。');
    render();
    renderProps();
  });
  $('widthInput').addEventListener('change', () => { paperW = parseFloat($('widthInput').value) || 100; render(); });
  $('heightInput').addEventListener('change', () => { paperH = parseFloat($('heightInput').value) || 60; render(); });
  $('gridCheck').addEventListener('change', render);
  $('clearLogBtn').addEventListener('click', () => { $('logBox').innerHTML = ''; });
  $('closePreviewBtn').addEventListener('click', () => { $('previewPanel').style.display = 'none'; });
  render();
  status('原型就绪：点击控件栏后在画布放置，或直接拖入；已连接时可加载 / 保存 / 预览。');
}

init();
})();
