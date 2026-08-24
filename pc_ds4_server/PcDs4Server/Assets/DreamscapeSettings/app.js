(() => {
  'use strict';

  const REFERENCE_WIDTH = 1672;
  const REFERENCE_HEIGHT = 941;
  const propertyByField = Object.freeze({
    overallSizePercent: 'overallSizePercent',
    doubleTapWindowMs: 'doubleTapWindowMs',
    selectionDeadZone: 'selectionDeadZone',
    selectedIntensity: 'selectedIntensity',
    petalOpacity: 'petalOpacity',
    borderOpacity: 'borderOpacity',
    textOpacity: 'textOpacity'
  });
  const outputByField = Object.freeze({
    overallSizePercent: 'overall-size-value',
    doubleTapWindowMs: 'double-tap-value',
    selectionDeadZone: 'dead-zone-value',
    selectedIntensity: 'highlight-value',
    petalOpacity: 'petal-opacity-value',
    borderOpacity: 'border-opacity-value',
    textOpacity: 'text-opacity-value'
  });
  let lastState = null;
  let bridgeMessagesSent = 0;
  const root = document.getElementById('design-root');
  const form = document.getElementById('settings-form');
  const advancedForm = document.getElementById('advanced-form');

  function calculateScale(width, height) {
    return Math.min(width / REFERENCE_WIDTH, height / REFERENCE_HEIGHT);
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

  function postMessage(message) {
    bridgeMessagesSent += 1;
    window.chrome?.webview?.postMessage(message);
  }

  function postCommand(command) {
    postMessage({ command });
  }

  function postChange(field, value) {
    postMessage({ command: 'settingsBasicChange', field, value });
  }

  function postAdvancedChange(field, value) {
    postMessage({ command: 'settingsAdvancedChange', field, value });
  }

  function populateSelect(select, options, value, optionValue, optionText) {
    const signature = JSON.stringify(options);
    if (select.dataset.options !== signature) {
      select.replaceChildren(...options.map(option => {
        const element = document.createElement('option');
        element.value = String(optionValue(option));
        element.textContent = optionText(option);
        return element;
      }));
      select.dataset.options = signature;
    }
    select.value = String(value);
  }

  function updateRange(input, state, field) {
    const range = state.ranges[field];
    input.min = String(range.min);
    input.max = String(range.max);
    input.step = String(range.step);
    input.value = String(state[propertyByField[field]]);
    updateRangePresentation(input);
  }

  function updateRangePresentation(input) {
    const min = Number(input.min);
    const max = Number(input.max);
    const value = Number(input.value);
    const fill = max === min ? 0 : ((value - min) / (max - min)) * 100;
    input.style.setProperty('--fill', `${fill}%`);
    document.getElementById(outputByField[input.dataset.field]).textContent = String(value);
  }

  function updateAdvancedRange(input, definition) {
    input.min = String(definition.min);
    input.max = String(definition.max);
    input.step = String(definition.step);
    input.value = String(definition.value);
    input.dataset.unit = definition.unit;
    updateAdvancedRangePresentation(input);
  }

  function updateAdvancedRangePresentation(input) {
    const min = Number(input.min);
    const max = Number(input.max);
    const value = Number(input.value);
    const fill = max === min ? 0 : ((value - min) / (max - min)) * 100;
    input.style.setProperty('--fill', `${fill}%`);
    const output = document.getElementById(`${input.id}-value`);
    output.textContent = `${value}${input.dataset.unit === '°' ? '' : ' '}${input.dataset.unit}`;
  }

  function applySection(section) {
    const activeSection = section === 'advanced' ? 'advanced' : 'basic';
    document.documentElement.dataset.section = activeSection;
    document.querySelectorAll('.settings-tab').forEach(tab => {
      const sectionForTab = tab.dataset.command === 'showSettingsAdvanced'
        ? 'advanced'
        : tab.dataset.command === 'showSettingsBasic' ? 'basic' : 'mappings';
      tab.classList.toggle('active', sectionForTab === activeSection);
      tab.setAttribute('aria-current', sectionForTab === activeSection ? 'page' : 'false');
    });
  }

  function applyState(state) {
    if (!state || state.type !== 'receiverSettingsBasicState') return;
    lastState = state;
    populateSelect(
      document.getElementById('visual-pack'), state.visualPackOptions, state.visualPackId,
      option => option.id, option => option.name);
    populateSelect(
      document.getElementById('receiver-scale'), state.receiverUiScaleOptions,
      state.receiverUiScalePercent, option => option, option => `${option}%`);
    form.querySelectorAll('input[type="range"]').forEach(input =>
      updateRange(input, state, input.dataset.field));
    advancedForm.querySelectorAll('input[type="range"]').forEach(input =>
      updateAdvancedRange(input, state.advancedFields[input.dataset.advancedField]));
    applySection(state.activeSection);

    document.getElementById('connection-status').textContent = state.connectionStatus;
    document.getElementById('connection-dot').classList.toggle(
      'connected', state.connectionStatus === '已连接');
    form.querySelectorAll('input,select').forEach(control => { control.disabled = !state.enabled; });
    advancedForm.querySelectorAll('input').forEach(control => { control.disabled = !state.enabled; });
    document.getElementById('preview-button').disabled = !state.enabled;
    document.getElementById('hide-preview-button').disabled = !state.previewActive;
    document.getElementById('apply-button').disabled = !state.enabled || !state.dirty;
    document.getElementById('restore-button').disabled = !state.enabled;
    document.documentElement.dataset.stateReceived = 'true';
  }

  document.querySelectorAll('[data-command]').forEach(element => {
    element.addEventListener('click', () => postCommand(element.dataset.command));
  });
  document.querySelectorAll('select[data-field]').forEach(select => {
    select.addEventListener('change', () => {
      const value = select.dataset.field === 'receiverUiScalePercent'
        ? Number(select.value) : select.value;
      postChange(select.dataset.field, value);
    });
  });
  document.querySelectorAll('input[type="range"][data-field]').forEach(input => {
    input.addEventListener('input', () => {
      updateRangePresentation(input);
      postChange(input.dataset.field, Number(input.value));
    });
  });
  document.querySelectorAll('input[type="range"][data-advanced-field]').forEach(input => {
    input.addEventListener('input', () => {
      updateAdvancedRangePresentation(input);
      postAdvancedChange(input.dataset.advancedField, Number(input.value));
    });
  });
  document.getElementById('window-drag-zone').addEventListener('pointerdown', event => {
    if (event.button === 0) postCommand('beginDrag');
  });
  window.addEventListener('resize', fitCanvas);
  window.chrome?.webview?.addEventListener('message', event => applyState(event.data));

  fitCanvas();
  document.documentElement.dataset.frontendReady = 'true';
  postCommand('settingsRequestState');
  window.setInterval(() => postCommand('settingsRequestState'), 1000);

  window.leftpadSettings = Object.freeze({
    calculateScale,
    postCommand,
    postChange,
    postAdvancedChange,
    diagnostics: () => ({
      frontendReady: document.documentElement.dataset.frontendReady === 'true',
      stateReceived: document.documentElement.dataset.stateReceived === 'true',
      bridgeMessagesSent,
      visibleFields: document.documentElement.dataset.section === 'advanced'
        ? advancedForm.querySelectorAll('[data-advanced-field]').length
        : form.querySelectorAll('[data-field]').length,
      basicFields: form.querySelectorAll('[data-field]').length,
      advancedFields: advancedForm.querySelectorAll('[data-advanced-field]').length,
      activeSection: document.documentElement.dataset.section,
      scale: Number(root.dataset.scale),
      originX: Number(root.dataset.originX),
      originY: Number(root.dataset.originY),
      viewport: { width: window.innerWidth, height: window.innerHeight },
      reference: { width: REFERENCE_WIDTH, height: REFERENCE_HEIGHT },
      lastState
    })
  });
})();
