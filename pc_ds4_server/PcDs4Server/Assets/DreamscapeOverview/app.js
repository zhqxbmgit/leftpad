(() => {
  'use strict';

  const mappingLayout = [
    ['CR', 932, 564, 1005, 'cross'],
    ['SQ', 932, 619, 1005, 'square'],
    ['L1', 932, 674, 1005, 'l1'],
    ['L2', 932, 729, 1005, 'l2'],
    ['L3', 932, 784, 1005, 'l3'],
    ['CIR', 1281, 564, 1357, 'circle'],
    ['TRI', 1281, 619, 1357, 'triangle'],
    ['R1', 1281, 674, 1357, 'r1'],
    ['R2', 1281, 729, 1357, 'r2'],
    ['R3', 1281, 784, 1357, 'r3']
  ];
  let lastState = null;
  let bridgeMessagesSent = 0;

  const mappingRoot = document.getElementById('mapping-values');

  function postCommand(command, payload = {}) {
    bridgeMessagesSent += 1;
    if (window.chrome?.webview) {
      window.chrome.webview.postMessage({ command, ...payload });
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

  function catalogSignature(options) {
    return (options ?? [])
      .map(option => `${option.value}\u0000${option.label}`)
      .join('\u0001');
  }

  function syncSelectOptions(select, options, selectedValue, editable) {
    const normalizedOptions = Array.isArray(options) ? options : [];
    const signature = catalogSignature(normalizedOptions);
    if (select.dataset.catalogSignature !== signature) {
      const fragment = document.createDocumentFragment();
      normalizedOptions.forEach(item => {
        const option = document.createElement('option');
        option.value = item.value;
        option.textContent = item.label;
        fragment.append(option);
      });
      select.replaceChildren(fragment);
      select.dataset.catalogSignature = signature;
    }

    if (selectedValue != null && select.value !== selectedValue) {
      select.value = selectedValue;
    }
    const disabled = !editable;
    if (select.disabled !== disabled) select.disabled = disabled;
  }

  function initializeMappings() {
    mappingLayout.forEach(([name, labelX, y, valueX, action]) => {
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
      select.dataset.action = action;
      select.disabled = true;
      select.addEventListener('change', () => postCommand('setKeyboardMapping', {
        action: select.dataset.action,
        key: select.value
      }));
      mappingRoot.append(label, select);
    });
  }

  function renderMappings(mappings, options, editable) {
    mappingRoot.querySelectorAll('[data-mapping]').forEach(select => {
      syncSelectOptions(
        select,
        options,
        mappings?.[select.dataset.mapping],
        editable);
    });
  }

  function applyState(state) {
    if (!state || state.type !== 'receiverOverviewState') return;
    lastState = state;
    document.getElementById('vigem-status').textContent = state.vigemStatus;
    document.getElementById('virtual-ds4-status').textContent = state.virtualDs4Status;
    document.getElementById('phone-status').textContent = state.phoneStatus;
    document.getElementById('port-status').textContent = String(state.port);
    syncSelectOptions(
      document.getElementById('output-mode'),
      state.outputModeOptions,
      state.outputModeValue,
      state.outputModeEditable);
    document.getElementById('start-stop-label').textContent = state.startStopLabel;
    document.getElementById('start-stop').dataset.mode =
      state.serviceRunning ? 'running' : 'stopped';
    updateStatus('vigem-card', 'vigem-dot', state.vigemStatus);
    updateStatus('virtual-ds4-card', 'virtual-ds4-dot', state.virtualDs4Status);
    updateStatus('phone-card', 'phone-dot', state.phoneStatus);
    updateStatus('port-card', 'port-dot', String(state.port));
    renderMappings(
      state.mappings,
      state.keyboardKeyOptions,
      state.keyboardMappingsEditable);
    document.documentElement.dataset.stateReceived = 'true';
  }

  document.querySelectorAll('[data-command]').forEach(element => {
    element.addEventListener('click', () => postCommand(element.dataset.command));
  });
  document.getElementById('output-mode').addEventListener('change', event => {
    postCommand('setOutputMode', { value: event.currentTarget.value });
  });
  window.chrome?.webview?.addEventListener('message', event => applyState(event.data));

  initializeMappings();
  document.documentElement.dataset.frontendReady = 'true';
  postCommand('requestState');
  window.setInterval(() => postCommand('requestState'), 1000);

  window.leftpadSpike = Object.freeze({
    calculateScale: window.leftpadShell.calculateScale,
    postCommand,
    diagnostics: () => {
      const shell = window.leftpadShell.diagnostics();
      return {
      frontendReady: document.documentElement.dataset.frontendReady === 'true',
      stateReceived: document.documentElement.dataset.stateReceived === 'true',
      bridgeMessagesSent,
      scale: shell.scale,
      originX: shell.origin.x,
      originY: shell.origin.y,
      viewport: shell.viewport,
      reference: shell.design,
      shell,
      lastState
      };
    }
  });
})();
