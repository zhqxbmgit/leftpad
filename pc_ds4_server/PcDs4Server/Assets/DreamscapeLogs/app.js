(() => {
  'use strict';

  const MAX_ENTRIES = 300;
  const NEAR_BOTTOM_PX = 24;
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
  window.chrome?.webview?.addEventListener('message', event => applyMessage(event.data));

  updateLatestIndicator();
  document.documentElement.dataset.frontendReady = 'true';
  postCommand('logsRequestSnapshot');

  window.leftpadLogs = Object.freeze({
    calculateScale: window.leftpadShell.calculateScale,
    postCommand,
    applyMessage,
    returnToBottom,
    scrollUpForSmoke: () => {
      view.scrollTop = 0;
      userAwayFromBottom = true;
      updateLatestIndicator();
    },
    diagnostics: () => {
      const shell = window.leftpadShell.diagnostics();
      return {
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
      scale: shell.scale,
      originX: shell.origin.x,
      originY: shell.origin.y,
      viewport: shell.viewport,
      reference: shell.design,
      shell
      };
    }
  });
})();
