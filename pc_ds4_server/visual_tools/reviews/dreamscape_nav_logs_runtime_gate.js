const fs = require('fs');
const path = require('path');
const { chromium } = require('playwright');

const endpoint = process.argv[2] || 'http://127.0.0.1:9222';
const reportPath = process.argv[3];
const captureDirectory = process.argv[4];
const phase = process.argv[5] || 'before';

if (!reportPath || !captureDirectory) {
  throw new Error('Usage: node dreamscape_nav_logs_runtime_gate.js <endpoint> <report> <captures> <before|after>');
}

const pageNames = ['overview', 'controller', 'settings', 'logs'];
const pageHosts = Object.freeze({
  overview: 'leftpad-overview.local',
  controller: 'leftpad-controller.local',
  settings: 'leftpad-settings.local',
  logs: 'leftpad-logs.local'
});

function plainRect(rect) {
  return { x: rect.x, y: rect.y, width: rect.width, height: rect.height,
    right: rect.right, bottom: rect.bottom };
}

async function navAudit(page) {
  return page.evaluate(() => {
    const describe = (element, pseudo = null) => {
      const style = getComputedStyle(element, pseudo);
      return {
        opacity: style.opacity,
        outline: style.outline,
        outlineColor: style.outlineColor,
        outlineStyle: style.outlineStyle,
        outlineWidth: style.outlineWidth,
        boxShadow: style.boxShadow,
        background: style.background,
        backgroundImage: style.backgroundImage,
        border: style.border,
        filter: style.filter,
        content: style.content
      };
    };
    const rectOf = element => {
      const rect = element.getBoundingClientRect();
      return { x: rect.x, y: rect.y, width: rect.width, height: rect.height,
        right: rect.right, bottom: rect.bottom };
    };
    const nav = [...document.querySelectorAll('[data-shell-page]')].map(element => ({
      page: element.dataset.shellPage,
      ariaCurrent: element.getAttribute('aria-current'),
      className: element.className,
      dataActive: element.dataset.active ?? null,
      activeAttribute: element.getAttribute('active'),
      rect: rectOf(element),
      matchesFocus: element.matches(':focus'),
      matchesFocusVisible: element.matches(':focus-visible'),
      matchesHover: element.matches(':hover'),
      style: describe(element),
      before: describe(element, '::before'),
      after: describe(element, '::after')
    }));
    const focused = document.activeElement;
    return {
      activePage: document.getElementById('dreamscape-shell')?.dataset.activePage ?? null,
      currentCount: nav.filter(item => item.ariaCurrent === 'page').length,
      nav,
      activeElement: focused ? {
        tag: focused.tagName.toLowerCase(),
        id: focused.id || null,
        className: typeof focused.className === 'string' ? focused.className : null,
        shellPage: focused.dataset?.shellPage ?? null,
        outerHTML: focused.outerHTML
      } : null
    };
  });
}

async function overviewEdgeAudit(page) {
  return page.evaluate(() => {
    const overview = document.querySelector('[data-shell-page="overview"]');
    const rect = overview.getBoundingClientRect();
    const points = [
      { name: 'left-edge', x: rect.left + 5, y: rect.top + rect.height / 2 },
      { name: 'top-edge', x: rect.left + rect.width / 2, y: rect.top + 5 },
      { name: 'right-edge', x: rect.right - 5, y: rect.top + rect.height / 2 }
    ];
    const pseudo = side => {
      const style = getComputedStyle(overview, side);
      return { opacity: style.opacity, content: style.content, background: style.background,
        border: style.border, boxShadow: style.boxShadow, clipPath: style.clipPath };
    };
    return {
      points: points.map(point => ({
        ...point,
        stack: document.elementsFromPoint(point.x, point.y).map(element => ({
          tag: element.tagName.toLowerCase(), id: element.id || null,
          className: typeof element.className === 'string' ? element.className : null,
          shellPage: element.dataset?.shellPage ?? null
        }))
      })),
      before: pseudo('::before'),
      after: pseudo('::after'),
      activeElementOuterHTML: document.activeElement?.outerHTML ?? null
    };
  });
}

async function pillAudit(page) {
  return page.evaluate(() => {
    const rectOf = selector => {
      const rect = document.querySelector(selector).getBoundingClientRect();
      return { x: rect.x, y: rect.y, width: rect.width, height: rect.height,
        right: rect.right, bottom: rect.bottom };
    };
    const pill = document.querySelector('#connection-pill');
    const style = getComputedStyle(pill);
    const root = document.querySelector('#design-root').getBoundingClientRect();
    const scale = root.width / 1672;
    const rect = pill.getBoundingClientRect();
    return {
      viewportRect: rectOf('#connection-pill'),
      designRect: {
        x: (rect.x - root.x) / scale,
        y: (rect.y - root.y) / scale,
        width: rect.width / scale,
        height: rect.height / scale
      },
      css: { position: style.position, left: style.left, top: style.top,
        right: style.right, transform: style.transform, zIndex: style.zIndex,
        boxShadow: style.boxShadow, background: style.background },
      designRoot: rectOf('#design-root'),
      staticArt: rectOf('#static-art')
    };
  });
}

async function clickNav(page, target) {
  const point = await page.evaluate(target => {
    const rect = document.querySelector(`[data-shell-page="${target}"]`).getBoundingClientRect();
    return { x: rect.left + rect.width / 2, y: rect.top + rect.height / 2 };
  }, target);
  await page.mouse.click(point.x, point.y);
  await page.waitForTimeout(500);
  return point;
}

(async () => {
  const browser = await chromium.connectOverCDP(endpoint);
  const allPages = browser.contexts().flatMap(context => context.pages());
  const pages = Object.fromEntries(pageNames.map(name => [
    name, allPages.find(page => page.url().includes(pageHosts[name]))
  ]));
  for (const name of pageNames) {
    if (!pages[name]) throw new Error(`Missing ${name} WebView. Found: ${allPages.map(page => page.url())}`);
  }

  fs.mkdirSync(captureDirectory, { recursive: true });
  const report = { phase, urls: Object.fromEntries(pageNames.map(name => [name, pages[name].url()])), initial: {} };
  for (const name of pageNames) {
    report.initial[name] = await navAudit(pages[name]);
  }

  report.overviewToControllerPoint = await clickNav(pages.overview, 'controller');
  report.controllerAfterMouseNavigation = await navAudit(pages.controller);
  report.controllerOverviewEdge = await overviewEdgeAudit(pages.controller);
  await pages.controller.screenshot({ path: path.join(captureDirectory, `${phase}-controller-pointer-stationary.png`) });

  await pages.controller.mouse.move(900, 500);
  await pages.controller.waitForTimeout(180);
  report.controllerAfterPointerMovedAway = await navAudit(pages.controller);

  await pages.controller.evaluate(() => {
    document.body.tabIndex = -1;
    document.body.focus();
  });
  await pages.controller.keyboard.press('Tab');
  await pages.controller.waitForTimeout(120);
  report.controllerKeyboardFocus = await navAudit(pages.controller);

  await pages.controller.keyboard.press('Enter');
  await pages.overview.waitForTimeout(400);
  report.controllerAfterKeyboardNavigationAway = await navAudit(pages.controller);
  report.overviewToControllerReentryPoint = await clickNav(pages.overview, 'controller');
  report.controllerReentryAfterStaleFocusScenario = await navAudit(pages.controller);
  await pages.controller.screenshot({ path: path.join(captureDirectory, `${phase}-controller-nav-highlight-fixed.png`) });

  report.controllerToSettingsPoint = await clickNav(pages.controller, 'settings');
  report.settingsAfterMouseNavigation = await navAudit(pages.settings);
  report.settingsToLogsPoint = await clickNav(pages.settings, 'logs');
  report.logsAfterMouseNavigation = await navAudit(pages.logs);
  report.logsPill = await pillAudit(pages.logs);
  report.pagePills = {};
  for (const name of pageNames) {
    if (await pages[name].locator('#connection-pill').count()) report.pagePills[name] = await pillAudit(pages[name]);
  }
  await pages.logs.screenshot({ path: path.join(captureDirectory, `${phase}-logs-pill-visible.png`) });
  await pages.logs.evaluate(() => { document.querySelector('#connection-pill').style.display = 'none'; });
  report.logsPillHidden = await pages.logs.evaluate(() => ({
    display: getComputedStyle(document.querySelector('#connection-pill')).display,
    stackAtBakedPillCenter: document.elementsFromPoint(1372, 191).map(element => ({
      tag: element.tagName.toLowerCase(), id: element.id || null,
      className: typeof element.className === 'string' ? element.className : null
    }))
  }));
  await pages.logs.screenshot({ path: path.join(captureDirectory, `${phase}-logs-pill-hidden.png`) });
  await pages.logs.evaluate(() => { document.querySelector('#connection-pill').style.display = ''; });

  report.logsToOverviewPoint = await clickNav(pages.logs, 'overview');
  report.overviewFinal = await navAudit(pages.overview);
  report.final = {};
  for (const name of pageNames) {
    await pages[name].evaluate(() => document.activeElement?.blur());
    await pages[name].mouse.move(900, 500);
    await pages[name].waitForTimeout(120);
    report.final[name] = await navAudit(pages[name]);
    await pages[name].screenshot({
      path: path.join(captureDirectory, `${phase}-${name}-state.png`),
      timeout: 60000
    });
  }

  fs.mkdirSync(path.dirname(reportPath), { recursive: true });
  fs.writeFileSync(reportPath, JSON.stringify(report, null, 2));
  console.log(JSON.stringify({
    phase,
    activeCounts: Object.fromEntries(pageNames.map(name => [name, report.initial[name].currentCount])),
    controllerMouse: report.controllerAfterMouseNavigation,
    controllerEdge: report.controllerOverviewEdge,
    controllerKeyboard: report.controllerKeyboardFocus,
    logsPill: report.logsPill,
    pagePills: report.pagePills,
    logsPillHidden: report.logsPillHidden
  }, null, 2));
  await browser.close();
})().catch(error => {
  console.error(error);
  process.exitCode = 1;
});
