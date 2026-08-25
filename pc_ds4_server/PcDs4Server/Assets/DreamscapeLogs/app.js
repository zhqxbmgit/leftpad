(() => {
  'use strict';

  const REFERENCE_WIDTH = 1672;
  const REFERENCE_HEIGHT = 941;
  const MAX_ENTRIES = 300;
  const NEAR_BOTTOM_PX = 24;
  const root = document.getElementById('design-root');
  const view = document.getElementById('log-view');
  const latestMarker = document.getElementById('latest-marker');
  const staticArt = document.getElementById('static-art');
  let bridgeMessagesSent = 0;
  let snapshotCount = 0;
  let appendCount = 0;
  let evictedCount = 0;
  let staticArtLoadCount = staticArt.complete ? 1 : 0;
  let userAwayFromBottom = false;

  if (!staticArt.complete) {
    staticArt.addEventListener('load', () => { staticArtLoadCount += 1; }, { once: true });
  }

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
    window.chrome?.webview?.postMessage({ command });
  }

  function isNearBottom() {
    return view.scrollHeight - view.clientHeight - view.scrollTop <= NEAR_BOTTOM_PX;
  }

  function updateLatestIndicator() {
    latestMarker.dataset.pending = userAwayFromBottom ? 'true' : 'false';
  }

  function returnToBottom() {
    view.scrollTop = view.scrollHeight;
    userAwayFromBottom = false;
    updateLatestIndicator();
  }

  function createLogRow(entry) {
    const row = document.createElement('p');
    row.className = `log-line ${entry.category || 'default'}`;
    row.dataset.sequenceId = String(entry.sequenceId);
    row.textContent = entry.rawText;
    return row;
  }

  function appendEntry(entry) {
    const followLatest = isNearBottom() && !userAwayFromBottom;
    view.append(createLogRow(entry));
    appendCount += 1;
    while (view.childElementCount > MAX_ENTRIES) {
      view.firstElementChild.remove();
      evictedCount += 1;
    }
    if (followLatest) returnToBottom();
    else {
      userAwayFromBottom = true;
      updateLatestIndicator();
    }
  }

  function updateConnection(connectionState) {
    const value = connectionState || '等待连接';
    const text = document.getElementById('connection-state');
    if (text.textContent !== value) text.textContent = value;
    document.getElementById('connection-pill').dataset.connected =
      value === '已连接' ? 'true' : 'false';
  }

  function applyMessage(message) {
    if (!message) return;
    if (message.type === 'receiverLogSnapshot') {
      const rows = (message.entries || []).slice(-MAX_ENTRIES).map(createLogRow);
      view.replaceChildren(...rows);
      snapshotCount += 1;
      updateConnection(message.connectionState);
      returnToBottom();
      document.documentElement.dataset.snapshotReceived = 'true';
      return;
    }
    if (message.type === 'receiverLogAppend' && message.entry) {
      appendEntry(message.entry);
      return;
    }
    if (message.type === 'receiverLogConnectionState') {
      updateConnection(message.connectionState);
    }
  }

  view.addEventListener('scroll', () => {
    userAwayFromBottom = !isNearBottom();
    updateLatestIndicator();
  });
  latestMarker.addEventListener('click', returnToBottom);
  document.querySelectorAll('[data-command]').forEach(element => {
    element.addEventListener('click', () => postCommand(element.dataset.command));
  });
  document.getElementById('window-drag-zone').addEventListener('pointerdown', event => {
    if (event.button === 0) postCommand('beginDrag');
  });
  window.addEventListener('resize', fitCanvas);
  window.chrome?.webview?.addEventListener('message', event => applyMessage(event.data));

  fitCanvas();
  updateLatestIndicator();
  document.documentElement.dataset.frontendReady = 'true';
  postCommand('logsRequestSnapshot');

  window.leftpadLogs = Object.freeze({
    calculateScale,
    postCommand,
    applyMessage,
    returnToBottom,
    scrollUpForSmoke: () => {
      view.scrollTop = 0;
      userAwayFromBottom = true;
      updateLatestIndicator();
    },
    diagnostics: () => ({
      frontendReady: document.documentElement.dataset.frontendReady === 'true',
      snapshotReceived: document.documentElement.dataset.snapshotReceived === 'true',
      bridgeMessagesSent,
      snapshotCount,
      appendCount,
      evictedCount,
      lineCount: view.childElementCount,
      maximumEntries: MAX_ENTRIES,
      scrollable: view.scrollHeight > view.clientHeight,
      atBottom: isNearBottom(),
      userAwayFromBottom,
      latestPending: latestMarker.dataset.pending === 'true',
      scrollTop: view.scrollTop,
      scrollHeight: view.scrollHeight,
      clientHeight: view.clientHeight,
      staticArtLoadCount,
      staticArtElements: document.querySelectorAll('#static-art').length,
      categories: [...view.children].map(row => row.className),
      lastRawText: view.lastElementChild?.textContent ?? null,
      scale: Number(root.dataset.scale),
      originX: Number(root.dataset.originX),
      originY: Number(root.dataset.originY),
      viewport: { width: window.innerWidth, height: window.innerHeight },
      reference: { width: REFERENCE_WIDTH, height: REFERENCE_HEIGHT }
    })
  });
})();
