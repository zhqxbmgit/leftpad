(() => {
  'use strict';

  const metrics = Object.freeze({
    designWidth: 1672,
    designHeight: 941,
    sidebar: Object.freeze({ x: 0, y: 0, width: 307, height: 941 }),
    brand: Object.freeze({ x: 63, y: 169, width: 214, height: 82 }),
    nav: Object.freeze({
      overview: Object.freeze({ x: 15, y: 300, width: 276, height: 72 }),
      controller: Object.freeze({ x: 15, y: 382, width: 276, height: 72 }),
      settings: Object.freeze({ x: 15, y: 464, width: 276, height: 72 }),
      logs: Object.freeze({ x: 15, y: 546, width: 276, height: 72 })
    }),
    windowButtons: Object.freeze({
      minimize: Object.freeze({ x: 1532, y: 16, width: 52, height: 52 }),
      close: Object.freeze({ x: 1592, y: 16, width: 52, height: 52 })
    })
  });

  const commandMaps = Object.freeze({
    overview: Object.freeze({ overview: 'requestState', controller: 'showGamepad', settings: 'showSettings', logs: 'showLogs' }),
    controller: Object.freeze({ overview: 'showOverview', controller: 'controllerRequestState', settings: 'showSettings', logs: 'showLogs' }),
    settings: Object.freeze({ overview: 'showOverview', controller: 'showGamepad', settings: 'settingsRequestState', logs: 'showLogs' }),
    logs: Object.freeze({ overview: 'showOverview', controller: 'showController', settings: 'showSettings', logs: 'logsRequestSnapshot' })
  });

  const iconMarkup = Object.freeze({
    overview: '<path d="M4.5 17.2 18 5.7l13.5 11.5-2.8 3.2-2.1-1.8v11.7H9.4V18.6l-2.1 1.8Z"/><path class="icon-cut" d="M15.2 21.2h5.6v9.1h-5.6z"/><path class="icon-detail" d="M11.8 15.6 18 10.3l6.2 5.3-.9 1.1-5.3-4.5-5.3 4.5Z"/>',
    controller: '<path d="M10.1 12.4h15.8c2.8 0 4.6 1.8 5.3 4.5l1.3 5.4c.8 3.2-.6 6.6-3.6 7.4-1.8.5-3.1-.3-4.4-1.8l-2.6-3H14l-2.6 3c-1.3 1.5-2.6 2.3-4.4 1.8-3-.8-4.4-4.2-3.6-7.4l1.3-5.4c.7-2.7 2.5-4.5 5.4-4.5Z"/><path class="icon-cut" d="M10 17h2.3v3h3v2.3h-3v3H10v-3H7v-2.3h3Zm13.7 1.2a1.55 1.55 0 1 1 0 3.1 1.55 1.55 0 0 1 0-3.1Zm3.8 3.6a1.55 1.55 0 1 1 0 3.1 1.55 1.55 0 0 1 0-3.1Z"/>',
    settings: '<path fill-rule="evenodd" d="m20.7 4.7.7 3.2c.8.3 1.5.6 2.2 1.1l3-1.3 2.5 2.5-1.3 3c.5.7.8 1.4 1.1 2.2l3.2.7v3.6l-3.2.7c-.3.8-.6 1.5-1.1 2.2l1.3 3-2.5 2.5-3-1.3c-.7.5-1.4.8-2.2 1.1l-.7 3.2h-3.6l-.7-3.2c-.8-.3-1.5-.6-2.2-1.1l-3 1.3-2.5-2.5 1.3-3c-.5-.7-.8-1.4-1.1-2.2l-3.2-.7v-3.6l3.2-.7c.3-.8.6-1.5 1.1-2.2l-1.3-3 2.5-2.5 3 1.3c.7-.5 1.4-.8 2.2-1.1l.7-3.2Zm-1.8 8.1a5.1 5.1 0 1 0 0 10.2 5.1 5.1 0 0 0 0-10.2Z"/><circle class="icon-detail" cx="18.9" cy="17.9" r="2.5"/>',
    logs: '<path d="M8 5.5h16.8l4.2 4.3v20.7H8Z"/><path class="icon-detail" d="M24.8 5.5v4.3H29Z"/><path class="icon-cut" d="M12.2 14h12.7v2H12.2zm0 5.1h12.7v2H12.2zm0 5.1h9v2h-9z"/>'
  });

  const labels = Object.freeze({ overview: '总览', controller: '手柄状态', settings: '设置', logs: '日志' });
  const root = document.getElementById('design-root');
  const shell = document.getElementById('dreamscape-shell');
  const activePage = shell?.dataset.activePage;
  let hostMetrics = Object.freeze({ zoomFactor: null });

  function calculateScale(viewportWidth, viewportHeight) {
    return Math.min(viewportWidth / metrics.designWidth, viewportHeight / metrics.designHeight);
  }

  function fitCanvas() {
    const scale = calculateScale(window.innerWidth, window.innerHeight);
    const originX = (window.innerWidth - metrics.designWidth * scale) / 2;
    const originY = (window.innerHeight - metrics.designHeight * scale) / 2;
    root.style.transform = `translate(${originX}px, ${originY}px) scale(${scale})`;
    root.dataset.scale = scale.toFixed(6);
    root.dataset.originX = originX.toFixed(3);
    root.dataset.originY = originY.toFixed(3);
  }

  function postCommand(command) {
    if (command) window.chrome?.webview?.postMessage({ command });
  }

  function navButton(page) {
    const selected = page === activePage;
    const rect = metrics.nav[page];
    return `<button class="dreamscape-nav-command" data-shell-page="${page}" style="--shell-nav-y:${rect.y}px"${selected ? ' aria-current="page"' : ''} aria-label="${labels[page]}">` +
      `<svg class="dreamscape-nav-icon" viewBox="0 0 36 36" aria-hidden="true">${iconMarkup[page]}</svg><span>${labels[page]}</span></button>`;
  }

  function renderShell() {
    if (!root || !shell || !commandMaps[activePage]) return;
    shell.innerHTML = `<aside class="dreamscape-sidebar" aria-label="LeftPad 主导航">` +
      '<img class="dreamscape-logo" src="https://leftpad-shared.local/official-prism.png" alt="" aria-hidden="true">' +
      '<section class="dreamscape-brand" aria-label="品牌"><div class="dreamscape-brand-name">LeftPad</div><div class="dreamscape-brand-subtitle">DS4 接收器</div></section>' +
      `<nav class="dreamscape-nav" aria-label="主导航">${Object.keys(labels).map(navButton).join('')}</nav></aside>` +
      '<div class="dreamscape-window-surface" aria-hidden="true"></div>' +
      '<div class="dreamscape-window-drag-zone" aria-hidden="true"></div>' +
      '<button class="dreamscape-window-button minimize" data-shell-command="minimize" aria-label="最小化">—</button>' +
      '<button class="dreamscape-window-button close" data-shell-command="closeWindow" aria-label="关闭">×</button>';

    shell.querySelectorAll('[data-shell-page]').forEach(element => {
      element.addEventListener('click', () => postCommand(commandMaps[activePage][element.dataset.shellPage]));
    });
    shell.querySelectorAll('[data-shell-command]').forEach(element => {
      element.addEventListener('click', () => postCommand(element.dataset.shellCommand));
    });
    shell.querySelector('.dreamscape-window-drag-zone').addEventListener('pointerdown', event => {
      if (event.button === 0) postCommand('beginDrag');
    });
  }

  function viewportRect(element) {
    if (!element) return null;
    const rect = element.getBoundingClientRect();
    return { x: rect.x, y: rect.y, width: rect.width, height: rect.height };
  }

  function diagnostics() {
    const scale = Number(root.dataset.scale);
    const nav = {};
    shell.querySelectorAll('[data-shell-page]').forEach(element => {
      nav[element.dataset.shellPage] = {
        design: metrics.nav[element.dataset.shellPage],
        viewport: viewportRect(element),
        selected: element.getAttribute('aria-current') === 'page'
      };
    });
    return {
      activePage,
      viewport: { width: window.innerWidth, height: window.innerHeight },
      devicePixelRatio: window.devicePixelRatio,
      visualViewportScale: window.visualViewport?.scale ?? 1,
      zoomFactor: hostMetrics.zoomFactor,
      design: { width: metrics.designWidth, height: metrics.designHeight },
      scale,
      origin: { x: Number(root.dataset.originX), y: Number(root.dataset.originY) },
      rootRect: viewportRect(root),
      sidebar: { design: metrics.sidebar, viewport: viewportRect(shell.querySelector('.dreamscape-sidebar')) },
      brand: { design: metrics.brand, viewport: viewportRect(shell.querySelector('.dreamscape-brand')) },
      nav,
      windowButtons: {
        minimize: { design: metrics.windowButtons.minimize, viewport: viewportRect(shell.querySelector('.dreamscape-window-button.minimize')) },
        close: { design: metrics.windowButtons.close, viewport: viewportRect(shell.querySelector('.dreamscape-window-button.close')) }
      }
    };
  }

  renderShell();
  fitCanvas();
  window.addEventListener('resize', fitCanvas);
  window.leftpadShell = Object.freeze({
    metrics,
    calculateScale,
    fitCanvas,
    diagnostics,
    setHostMetrics: value => { hostMetrics = Object.freeze({ zoomFactor: value?.zoomFactor ?? null }); }
  });
})();
