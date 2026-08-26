(() => {
  'use strict';

  const staticArt = document.getElementById('static-art');
  let lastState = null;
  let bridgeMessagesSent = 0;
  let changedTextNodes = 0;
  let changedButtonStates = 0;
  let staticArtLoadCount = staticArt.complete ? 1 : 0;

  if (!staticArt.complete) {
    staticArt.addEventListener('load', () => { staticArtLoadCount += 1; }, { once: true });
  }

  function postCommand(command) {
    bridgeMessagesSent += 1;
    window.chrome?.webview?.postMessage({ command });
  }

  function setText(id, value) {
    const element = document.getElementById(id);
    const text = String(value ?? '');
    if (element.textContent === text) return;
    element.textContent = text;
    changedTextNodes += 1;
  }

  function setButtonState(name, pressed) {
    const element = document.querySelector(`[data-button="${name}"]`);
    const next = pressed ? 'true' : 'false';
    if (element.dataset.pressed === next) return;
    element.dataset.pressed = next;
    element.querySelector('.button-state').textContent = pressed ? '已按下' : '未按下';
    element.setAttribute('aria-label', `${name} ${pressed ? '已按下' : '未按下'}`);
    changedButtonStates += 1;
  }

  function applyState(state) {
    if (!state || state.type !== 'receiverControllerState') return;
    lastState = state;
    setText('move-state', state.moveState);
    setText('mode', state.mode);
    setText('cursor-sampling', state.cursorSampling);
    setText('direction-captured', state.directionCaptured);
    setText('move-locked', state.moveLocked);
    setText('current-direction', state.currentDirection);
    setText('locked-direction', state.lockedDirection);
    setText('joystick', state.joystick);
    setText('ds4', state.ds4);
    setText('center', state.center);
    setText('cursor', state.cursor);
    setText('connection-state', state.connectionState);
    document.getElementById('connection-pill').dataset.connected =
      state.connectionState === '已连接' ? 'true' : 'false';
    setButtonState('triangle', state.trianglePressed);
    setButtonState('square', state.squarePressed);
    setButtonState('cross', state.crossPressed);
    setButtonState('circle', state.circlePressed);
    document.documentElement.dataset.stateReceived = 'true';
  }

  window.chrome?.webview?.addEventListener('message', event => applyState(event.data));

  document.documentElement.dataset.frontendReady = 'true';
  postCommand('controllerRequestState');

  window.leftpadController = Object.freeze({
    calculateScale: window.leftpadShell.calculateScale,
    postCommand,
    applyState,
    diagnostics: () => {
      const shell = window.leftpadShell.diagnostics();
      return {
      frontendReady: document.documentElement.dataset.frontendReady === 'true',
      stateReceived: document.documentElement.dataset.stateReceived === 'true',
      bridgeMessagesSent,
      changedTextNodes,
      changedButtonStates,
      staticArtLoadCount,
      staticArtElements: document.querySelectorAll('#static-art').length,
      diagnosticFields: document.querySelectorAll('.field').length,
      pressedButtons: [...document.querySelectorAll('[data-button][data-pressed="true"]')]
        .map(element => element.dataset.button),
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
