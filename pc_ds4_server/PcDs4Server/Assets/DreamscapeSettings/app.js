(() => {
  'use strict';

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
  let stateRequestsSent = 0;
  let pageActive = false;
  let pollingTimer = null;
  let pollingTimerStarts = 0;
  let pollingTimerStops = 0;
  let statusTimer = null;
  const form = document.getElementById('settings-form');
  const advancedForm = document.getElementById('advanced-form');
  const mappingSection = document.getElementById('mapping-section');
  const mappingGrid = document.getElementById('mapping-grid');
  const mappingDetail = document.getElementById('mapping-detail');

  function postMessage(message) {
    bridgeMessagesSent += 1;
    window.chrome?.webview?.postMessage(message);
  }

  function postCommand(command) {
    postMessage({ command });
  }

  function requestState() {
    if (!pageActive) return;
    stateRequestsSent += 1;
    postCommand('settingsRequestState');
  }

  function startPolling() {
    if (!pageActive || pollingTimer !== null) return;
    requestState();
    pollingTimer = window.setInterval(requestState, 1000);
    pollingTimerStarts += 1;
  }

  function stopPolling() {
    if (pollingTimer === null) return;
    window.clearInterval(pollingTimer);
    pollingTimer = null;
    pollingTimerStops += 1;
  }

  function applyPageActivation(message) {
    if (!message || message.type !== 'pageActivation' ||
        typeof message.active !== 'boolean') return false;
    if (pageActive === message.active) return true;
    pageActive = message.active;
    document.documentElement.dataset.pageActive = String(pageActive);
    if (pageActive) startPolling();
    else stopPolling();
    return true;
  }

  function postChange(field, value) {
    postMessage({ command: 'settingsBasicChange', field, value });
  }

  function postAdvancedChange(field, value) {
    postMessage({ command: 'settingsAdvancedChange', field, value });
  }

  function postMappingSelection(profileId, slotId) {
    postMessage({ command: 'settingsMappingSelectSlot', profileId, slotId });
  }

  function postMappingChange(profileId, slotId, mapping) {
    postMessage({
      command: 'settingsMappingChange', profileId, slotId,
      actionKind: mapping.actionKind,
      key: mapping.key ?? null,
      ctrl: Boolean(mapping.ctrl), alt: Boolean(mapping.alt),
      shift: Boolean(mapping.shift), win: Boolean(mapping.win),
      ds4Action: mapping.ds4Action ?? null
    });
  }

  function applyStatus(status) {
    if (!status || status.type !== 'receiverSettingsStatus') return;
    const output = document.getElementById('status-message');
    output.textContent = status.message;
    output.dataset.tone = status.tone;
    output.classList.add('visible');
    window.clearTimeout(statusTimer);
    statusTimer = window.setTimeout(() => output.classList.remove('visible'), 3200);
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
    const activeSection = section === 'advanced'
      ? 'advanced'
      : section === 'mappings' ? 'mappings' : 'basic';
    document.documentElement.dataset.section = activeSection;
    document.querySelectorAll('.settings-tab').forEach(tab => {
      const sectionForTab = tab.dataset.command === 'showSettingsAdvanced'
        ? 'advanced'
        : tab.dataset.command === 'showSettingsBasic' ? 'basic' : 'mappings';
      tab.classList.toggle('active', sectionForTab === activeSection);
      tab.setAttribute('aria-current', sectionForTab === activeSection ? 'page' : 'false');
    });
  }

  function optionElements(options) {
    return options.map(option => {
      const element = document.createElement('option');
      element.value = option.id;
      element.textContent = option.name;
      return element;
    });
  }

  function createMappingSelect(className, options, value, disabled) {
    const select = document.createElement('select');
    select.className = `mapping-select ${className}`;
    select.replaceChildren(...optionElements(options));
    select.value = value ?? '';
    select.disabled = disabled;
    return select;
  }

  function defaultMapping(kind, state) {
    return {
      actionKind: kind,
      key: kind === 'keyboardKey' || kind === 'keyboardShortcut'
        ? state.keyboardKeyOptions[0]?.id ?? null : null,
      ctrl: kind === 'keyboardShortcut', alt: false, shift: false, win: false,
      ds4Action: kind === 'ds4Button' ? state.ds4ActionOptions[0]?.id ?? null : null
    };
  }

  function renderMappingGrid(state) {
    document.documentElement.dataset.mappingSlots = String(state.mappingSlotCount);
    document.getElementById('mapping-profile-badge').textContent = state.mappingProfileId.toUpperCase();
    document.getElementById('mapping-caption').textContent =
      `${state.mappingSlotCount} 个槽位 · ${state.mappingSplitIndex} + ${state.mappingSlotCount - state.mappingSplitIndex} · 全部一次显示`;
    const signature = JSON.stringify({
      profile: state.mappingProfileId,
      split: state.mappingSplitIndex,
      selected: state.selectedMappingSlot,
      enabled: state.enabled,
      mappings: state.mappings,
      kinds: state.actionKindOptions
    });
    if (mappingGrid.dataset.signature === signature) return;
    mappingGrid.dataset.signature = signature;
    mappingGrid.replaceChildren();

    state.mappings.forEach(mapping => {
      const left = mapping.slotId <= state.mappingSplitIndex;
      const rowIndex = left ? mapping.slotId - 1 : mapping.slotId - state.mappingSplitIndex - 1;
      const row = document.createElement('article');
      row.className = `mapping-row column-${left ? 0 : 1}`;
      row.classList.toggle('selected', mapping.slotId === state.selectedMappingSlot);
      row.dataset.slot = String(mapping.slotId);
      row.style.setProperty('--row', String(rowIndex));

      const selector = document.createElement('button');
      selector.type = 'button';
      selector.className = 'slot-selector';
      selector.disabled = !state.enabled;
      selector.setAttribute('aria-label', `编辑 Slot ${mapping.slotId}`);
      selector.innerHTML = `<span class="slot-orb">${mapping.slotId}</span><strong>Slot ${mapping.slotId}</strong><small>${mapping.summary || 'ACTION TYPE'}</small>`;
      selector.addEventListener('click', () =>
        postMappingSelection(state.mappingProfileId, mapping.slotId));

      const kind = createMappingSelect(
        'mapping-kind', state.actionKindOptions, mapping.actionKind, !state.enabled);
      kind.setAttribute('aria-label', `Slot ${mapping.slotId} 动作类型`);
      kind.addEventListener('change', () =>
        postMappingChange(state.mappingProfileId, mapping.slotId, defaultMapping(kind.value, state)));
      row.append(selector, kind);
      mappingGrid.appendChild(row);
    });
  }

  function appendDetailField(className, labelText, select) {
    const label = document.createElement('label');
    label.className = `detail-field ${className}`;
    label.append(document.createTextNode(labelText), select);
    mappingDetail.appendChild(label);
  }

  function renderMappingDetail(state) {
    const signature = JSON.stringify({
      profile: state.mappingProfileId,
      selected: state.selectedMappingSlot,
      enabled: state.enabled,
      mapping: state.mappings.find(item => item.slotId === state.selectedMappingSlot),
      kinds: state.actionKindOptions,
      keys: state.keyboardKeyOptions,
      ds4: state.ds4ActionOptions
    });
    if (mappingDetail.dataset.signature === signature) return;
    mappingDetail.dataset.signature = signature;
    mappingDetail.replaceChildren();
    const mapping = state.mappings.find(item => item.slotId === state.selectedMappingSlot);
    if (!mapping) {
      mappingDetail.innerHTML = `<div class="mapping-detail-empty"><span class="detail-gem">✦</span><div><strong>选择一个 Slot 编辑详细动作</strong><small>None 状态保持列表紧凑，页面高度稳定</small></div></div>`;
      return;
    }

    const kindOption = state.actionKindOptions.find(option => option.id === mapping.actionKind);
    const header = document.createElement('div');
    header.className = 'detail-header';
    header.innerHTML = `<span class="slot-orb">${mapping.slotId}</span><div><strong>Slot ${mapping.slotId}</strong><small>${(kindOption?.name ?? mapping.actionKind).toUpperCase()}</small></div>`;
    mappingDetail.appendChild(header);

    const kind = createMappingSelect('', state.actionKindOptions, mapping.actionKind, !state.enabled);
    kind.addEventListener('change', () =>
      postMappingChange(state.mappingProfileId, mapping.slotId, defaultMapping(kind.value, state)));
    appendDetailField('action-kind', 'Action Type', kind);

    if (mapping.actionKind === 'keyboardKey' || mapping.actionKind === 'keyboardShortcut') {
      const key = createMappingSelect('', state.keyboardKeyOptions, mapping.key, !state.enabled);
      key.addEventListener('change', () =>
        postMappingChange(state.mappingProfileId, mapping.slotId, { ...mapping, key: key.value }));
      appendDetailField('main-key', 'Main Key', key);
    }

    if (mapping.actionKind === 'keyboardShortcut') {
      const modifiers = document.createElement('div');
      modifiers.className = 'modifier-list';
      const modifierNames = ['ctrl', 'alt', 'shift', 'win'];
      const selectedCount = modifierNames.filter(name => mapping[name]).length;
      [['ctrl', 'Ctrl'], ['alt', 'Alt'], ['shift', 'Shift'], ['win', 'Win']].forEach(([name, text]) => {
        const chip = document.createElement('button');
        chip.type = 'button';
        chip.className = 'modifier-chip';
        chip.classList.toggle('selected', mapping[name]);
        chip.textContent = text;
        chip.disabled = !state.enabled || (mapping[name] && selectedCount === 1);
        chip.setAttribute('aria-pressed', String(Boolean(mapping[name])));
        chip.addEventListener('click', () =>
          postMappingChange(state.mappingProfileId, mapping.slotId, {
            ...mapping, [name]: !mapping[name]
          }));
        modifiers.appendChild(chip);
      });
      mappingDetail.appendChild(modifiers);
    }

    if (mapping.actionKind === 'ds4Button') {
      const action = createMappingSelect('', state.ds4ActionOptions, mapping.ds4Action, !state.enabled);
      action.addEventListener('change', () =>
        postMappingChange(state.mappingProfileId, mapping.slotId, {
          ...mapping, ds4Action: action.value
        }));
      appendDetailField('ds4-action', 'DS4 Action', action);
    }
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
    renderMappingGrid(state);
    renderMappingDetail(state);
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
  window.chrome?.webview?.addEventListener('message', event => {
    if (applyPageActivation(event.data)) return;
    if (event.data?.type === 'receiverSettingsStatus') applyStatus(event.data);
    else applyState(event.data);
  });

  document.documentElement.dataset.frontendReady = 'true';
  document.documentElement.dataset.pageActive = 'false';

  window.leftpadSettings = Object.freeze({
    calculateScale: window.leftpadShell.calculateScale,
    postCommand,
    postChange,
    postAdvancedChange,
    postMappingSelection,
    postMappingChange,
    diagnostics: () => {
      const shell = window.leftpadShell.diagnostics();
      return {
      frontendReady: document.documentElement.dataset.frontendReady === 'true',
      stateReceived: document.documentElement.dataset.stateReceived === 'true',
      bridgeMessagesSent,
      stateRequestsSent,
      pageActive,
      polling: pollingTimer !== null,
      pollingTimerStarts,
      pollingTimerStops,
      visibleFields: document.documentElement.dataset.section === 'advanced'
        ? advancedForm.querySelectorAll('[data-advanced-field]').length
        : document.documentElement.dataset.section === 'mappings'
          ? mappingGrid.querySelectorAll('.mapping-row').length
          : form.querySelectorAll('[data-field]').length,
      basicFields: form.querySelectorAll('[data-field]').length,
      advancedFields: advancedForm.querySelectorAll('[data-advanced-field]').length,
      mappingSlots: mappingGrid.querySelectorAll('.mapping-row').length,
      mappingColumns: {
        left: mappingGrid.querySelectorAll('.column-0').length,
        right: mappingGrid.querySelectorAll('.column-1').length
      },
      selectedMappingSlot: lastState?.selectedMappingSlot ?? null,
      mappingProfileId: lastState?.mappingProfileId ?? null,
      mappingOverflow: mappingSection.scrollHeight > mappingSection.clientHeight ||
        mappingSection.scrollWidth > mappingSection.clientWidth,
      activeSection: document.documentElement.dataset.section,
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
