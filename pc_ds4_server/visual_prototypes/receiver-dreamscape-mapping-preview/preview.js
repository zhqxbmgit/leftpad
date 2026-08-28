(() => {
  'use strict';

  const REFERENCE_WIDTH = 1672;
  const REFERENCE_HEIGHT = 941;
  const params = new URLSearchParams(location.search);
  const option = params.get('option') === 'b' ? 'b' : 'a';
  const state = params.get('state') === 'expanded' ? 'expanded' : 'none';
  const slotCount = params.get('layout') === '6' ? 6 : 8;
  const root = document.getElementById('design-root');
  const form = document.getElementById('mapping-form');
  const sharedDetail = document.getElementById('shared-detail');

  document.documentElement.dataset.option = option;
  document.documentElement.dataset.state = state;
  document.documentElement.dataset.layout = String(slotCount);

  const expandedTypes = {
    1: 'KeyboardKey',
    2: 'KeyboardShortcut',
    3: 'Ds4Button'
  };

  function chevronSelect(value, extraClass = '') {
    return `<span class="mock-select ${extraClass}"><span>${value}</span><i></i></span>`;
  }

  function inlineDetail(slot, type) {
    if (type === 'KeyboardKey') {
      return `<div class="inline-detail"><em>MAIN KEY</em>${chevronSelect('K', 'mini-select')}</div>`;
    }
    if (type === 'KeyboardShortcut') {
      return `<div class="inline-detail shortcut-detail"><em>MAIN KEY</em>${chevronSelect('P', 'mini-select')}
        <span class="modifier on">CTRL</span><span class="modifier">ALT</span><span class="modifier on">SHIFT</span><span class="modifier">WIN</span></div>`;
    }
    if (type === 'Ds4Button') {
      return `<div class="inline-detail"><em>DS4 ACTION</em>${chevronSelect('CROSS', 'detail-select')}</div>`;
    }
    return '';
  }

  function createRow(slot, columnIndex, rowIndex) {
    const type = state === 'expanded' && expandedTypes[slot] ? expandedTypes[slot] : 'None';
    const displayType = type === 'None' ? '无' : type;
    const row = document.createElement('article');
    row.className = `mapping-row column-${columnIndex} ${type !== 'None' ? 'has-detail' : ''}`;
    row.dataset.slot = String(slot);
    row.dataset.type = type;
    row.style.setProperty('--row', String(rowIndex));
    row.innerHTML = `<span class="slot-orb">${slot}</span><strong>Slot ${slot}</strong>
      <small>ACTION TYPE</small>${chevronSelect(displayType, 'action-select')}
      ${option === 'a' ? inlineDetail(slot, type) : ''}`;
    return row;
  }

  function renderRows() {
    const perColumn = slotCount / 2;
    for (let slot = 1; slot <= slotCount; slot += 1) {
      const columnIndex = slot <= perColumn ? 0 : 1;
      const rowIndex = columnIndex === 0 ? slot - 1 : slot - perColumn - 1;
      form.appendChild(createRow(slot, columnIndex, rowIndex));
    }
  }

  function renderSharedDetail() {
    if (option !== 'b') return;
    if (state === 'none') {
      sharedDetail.innerHTML = `<div class="detail-empty"><span class="detail-gem">✦</span>
        <div><strong>选择一个 Slot 编辑详细动作</strong><small>None 状态保持列表紧凑，页面高度稳定</small></div></div>`;
      return;
    }
    sharedDetail.innerHTML = `<div class="detail-header"><span class="slot-orb">2</span>
      <div><strong>Slot 2</strong><small>KEYBOARD SHORTCUT</small></div></div>
      <label>Action Type${chevronSelect('KeyboardShortcut')}</label>
      <label>Main Key${chevronSelect('P', 'key-select')}</label>
      <div class="shared-modifiers"><span class="modifier on">CTRL</span><span class="modifier">ALT</span>
        <span class="modifier on">SHIFT</span><span class="modifier">WIN</span></div>`;
  }

  function fit() {
    const scale = Math.min(innerWidth / REFERENCE_WIDTH, innerHeight / REFERENCE_HEIGHT);
    const x = (innerWidth - REFERENCE_WIDTH * scale) / 2;
    const y = (innerHeight - REFERENCE_HEIGHT * scale) / 2;
    root.style.transform = `translate(${x}px,${y}px) scale(${scale})`;
    root.dataset.scale = scale.toFixed(6);
  }

  document.getElementById('layout-badge').textContent = `RADIAL-${slotCount}`;
  document.getElementById('mapping-caption').textContent = slotCount === 8
    ? '8 个槽位 · 4 + 4 · 全部一次显示'
    : '6 个槽位 · 3 + 3 · 动态布局预览';
  renderRows();
  renderSharedDetail();
  addEventListener('resize', fit);
  fit();

  window.mappingPreview = Object.freeze({ option, state, slotCount });
})();
