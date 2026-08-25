(() => {
  'use strict';

  const REFERENCE_WIDTH = 1672;
  const REFERENCE_HEIGHT = 941;
  const root = document.getElementById('design-root');
  const staticArt = document.getElementById('static-art');
  let lastState = null;
  let bridgeMessagesSent = 0;
  let changedTextNodes = 0;
  let changedButtonStates = 0;
  let staticArtLoadCount = staticArt.complete ? 1 : 0;

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

  document.querySelectorAll('[data-command]').forEach(element => {
    element.addEventListener('click', () => postCommand(element.dataset.command));
  });
  document.getElementById('window-drag-zone').addEventListener('pointerdown', event => {
    if (event.button === 0) postCommand('beginDrag');
  });
  window.addEventListener('resize', fitCanvas);
  window.chrome?.webview?.addEventListener('message', event => applyState(event.data));

  fitCanvas();
  document.documentElement.dataset.frontendReady = 'true';
  postCommand('controllerRequestState');

  window.leftpadController = Object.freeze({
    calculateScale,
    postCommand,
    applyState,
    diagnostics: () => ({
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
      scale: Number(root.dataset.scale),
      originX: Number(root.dataset.originX),
      originY: Number(root.dataset.originY),
      viewport: { width: window.innerWidth, height: window.innerHeight },
      reference: { width: REFERENCE_WIDTH, height: REFERENCE_HEIGHT },
      lastState
    })
  });
})();
