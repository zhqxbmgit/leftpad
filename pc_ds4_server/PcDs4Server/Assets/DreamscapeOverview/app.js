(() => {
  'use strict';

  const REFERENCE_WIDTH = 1672;
  const REFERENCE_HEIGHT = 941;
  const mappingLayout = [
    ['CR', 932, 564, 1005],
    ['SQ', 932, 619, 1005],
    ['L1', 932, 674, 1005],
    ['L2', 932, 729, 1005],
    ['L3', 932, 784, 1005],
    ['CIR', 1281, 564, 1357],
    ['TRI', 1281, 619, 1357],
    ['R1', 1281, 674, 1357],
    ['R2', 1281, 729, 1357],
    ['R3', 1281, 784, 1357]
  ];
  let lastState = null;
  let bridgeMessagesSent = 0;

  const root = document.getElementById('design-root');
  const mappingRoot = document.getElementById('mapping-values');

  function calculateScale(viewportWidth, viewportHeight) {
    return Math.min(viewportWidth / REFERENCE_WIDTH, viewportHeight / REFERENCE_HEIGHT);
  }

  function fitCanvas() {
    const scale = calculateScale(window.innerWidth, window.innerHeight);
    const left = (window.innerWidth - REFERENCE_WIDTH * scale) / 2;
    const top = (window.innerHeight - REFERENCE_HEIGHT * scale) / 2;
    root.style.transform = `translate(${left}px, ${top}px) scale(${scale})`;
    root.dataset.scale = scale.toFixed(6);
    root.dataset.originX = left.toFixed(3);
    root.dataset.originY = top.toFixed(3);
  }

  function postCommand(command) {
    bridgeMessagesSent += 1;
    if (window.chrome?.webview) {
      window.chrome.webview.postMessage({ command });
    }
  }

  function statusTone(value) {
    if (value === '就绪' || value === '已连接' || /^\d+$/.test(String(value))) {
      return 'positive';
    }
    if (value === '等待连接' || value === '检查中...') return 'waiting';
    return 'inactive';
  }

  function updateStatus(cardId, dotId, value) {
    const tone = statusTone(value);
    const card = document.getElementById(cardId);
    const dot = document.getElementById(dotId);
    if (card) card.dataset.tone = tone;
    if (dot) dot.dataset.tone = tone;
  }

  function setSelectValue(select, value) {
    const normalized = value ?? 'None';
    let option = select.options[0];
    if (!option) {
      option = document.createElement('option');
      select.append(option);
    }
    option.value = normalized;
    option.textContent = normalized;
    select.value = normalized;
    select.disabled = value == null;
  }

  function initializeMappings() {
    mappingLayout.forEach(([name, labelX, y, valueX]) => {
      const label = document.createElement('span');
      label.className = 'mapping-label';
      label.style.left = `${labelX}px`;
      label.style.top = `${y}px`;
      label.textContent = name;

      const select = document.createElement('select');
      select.className = 'mapping-value premium-select';
      select.dataset.mapping = name;
      select.style.left = `${valueX - 15}px`;
      select.style.top = `${y}px`;
      select.setAttribute('aria-label', `${name} 映射`);
      setSelectValue(select, null);
      mappingRoot.append(label, select);
    });
  }

  function renderMappings(mappings) {
    mappingRoot.querySelectorAll('[data-mapping]').forEach(select => {
      setSelectValue(select, mappings?.[select.dataset.mapping]);
    });
  }

  function applyState(state) {
    if (!state || state.type !== 'receiverOverviewState') return;
    lastState = state;
    document.getElementById('vigem-status').textContent = state.vigemStatus;
    document.getElementById('virtual-ds4-status').textContent = state.virtualDs4Status;
    document.getElementById('phone-status').textContent = state.phoneStatus;
    document.getElementById('port-status').textContent = String(state.port);
    setSelectValue(document.getElementById('output-mode'), state.outputMode);
    document.getElementById('start-stop-label').textContent = state.startStopLabel;
    document.getElementById('start-stop').dataset.mode =
      state.startStopLabel === '停止' ? 'running' : 'stopped';
    updateStatus('vigem-card', 'vigem-dot', state.vigemStatus);
    updateStatus('virtual-ds4-card', 'virtual-ds4-dot', state.virtualDs4Status);
    updateStatus('phone-card', 'phone-dot', state.phoneStatus);
    updateStatus('port-card', 'port-dot', String(state.port));
    renderMappings(state.mappings);
    document.documentElement.dataset.stateReceived = 'true';
  }

  document.querySelectorAll('[data-command]').forEach(element => {
    element.addEventListener('click', () => postCommand(element.dataset.command));
  });
  document.getElementById('window-drag-zone').addEventListener('pointerdown', event => {
    if (event.button === 0) postCommand('beginDrag');
  });
  window.addEventListener('resize', fitCanvas);
  window.chrome?.webview?.addEventListener('message', event => applyState(event.data));

  initializeMappings();
  fitCanvas();
  document.documentElement.dataset.frontendReady = 'true';
  postCommand('requestState');
  window.setInterval(() => postCommand('requestState'), 1000);

  window.leftpadSpike = Object.freeze({
    calculateScale,
    postCommand,
    diagnostics: () => ({
      frontendReady: document.documentElement.dataset.frontendReady === 'true',
      stateReceived: document.documentElement.dataset.stateReceived === 'true',
      bridgeMessagesSent,
      scale: Number(root.dataset.scale),
      originX: Number(root.dataset.originX),
      originY: Number(root.dataset.originY),
      viewport: { width: window.innerWidth, height: window.innerHeight },
      reference: { width: REFERENCE_WIDTH, height: REFERENCE_HEIGHT },
      lastState
    })
  });
})();
