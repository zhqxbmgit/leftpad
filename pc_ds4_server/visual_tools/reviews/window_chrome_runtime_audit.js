const fs = require('fs');
const path = require('path');
const { chromium } = require('playwright');

const endpoint = process.argv[2] || 'http://127.0.0.1:9222';
const reportPath = process.argv[3];
const captureDirectory = process.argv[4];
const phase = process.argv[5] || 'before';

if (!reportPath || !captureDirectory) {
  throw new Error('Usage: node window_chrome_runtime_audit.js <endpoint> <report> <captures> <phase>');
}

const pageHosts = Object.freeze({
  overview: 'leftpad-overview.local',
  controller: 'leftpad-controller.local',
  settings: 'leftpad-settings.local',
  logs: 'leftpad-logs.local'
});

async function chromeClip(page) {
  return page.evaluate(() => {
    const root = document.querySelector('#design-root').getBoundingClientRect();
    const scale = root.width / 1672;
    return {
      x: root.x + 1508 * scale,
      y: root.y,
      width: 164 * scale,
      height: 86 * scale
    };
  });
}

async function audit(page) {
  return page.evaluate(() => {
    const describe = (element, pseudo = null) => {
      const style = getComputedStyle(element, pseudo);
      return {
        display: style.display,
        opacity: style.opacity,
        background: style.background,
        backgroundColor: style.backgroundColor,
        backgroundImage: style.backgroundImage,
        boxShadow: style.boxShadow,
        color: style.color,
        font: style.font,
        lineHeight: style.lineHeight,
        textShadow: style.textShadow,
        content: style.content,
        position: style.position,
        inset: style.inset,
        zIndex: style.zIndex
      };
    };
    const rect = element => {
      const value = element.getBoundingClientRect();
      return { x: value.x, y: value.y, width: value.width, height: value.height,
        right: value.right, bottom: value.bottom };
    };
    const button = selector => {
      const element = document.querySelector(selector);
      return {
        rect: rect(element),
        textContent: element.textContent,
        childElementCount: element.childElementCount,
        style: describe(element),
        before: describe(element, '::before'),
        after: describe(element, '::after')
      };
    };
    return {
      activePage: document.querySelector('#dreamscape-shell')?.dataset.activePage,
      designRoot: rect(document.querySelector('#design-root')),
      controls: {
        minimize: button('.dreamscape-window-button.minimize'),
        close: button('.dreamscape-window-button.close')
      },
      domCounts: {
        shell: document.querySelectorAll('#dreamscape-shell').length,
        allWindowButtons: document.querySelectorAll('.dreamscape-window-button').length,
        minimize: document.querySelectorAll('.dreamscape-window-button.minimize').length,
        close: document.querySelectorAll('.dreamscape-window-button.close').length,
        commandMinimize: document.querySelectorAll('[data-shell-command="minimize"]').length,
        commandClose: document.querySelectorAll('[data-shell-command="closeWindow"]').length
      },
      surface: (() => {
        const element = document.querySelector('.dreamscape-window-surface');
        return { rect: rect(element), style: describe(element), before: describe(element, '::before'),
          after: describe(element, '::after') };
      })()
    };
  });
}

async function controlState(page, selector) {
  return page.evaluate(selector => {
    const element = document.querySelector(selector);
    const rect = element.getBoundingClientRect();
    const style = getComputedStyle(element);
    const before = getComputedStyle(element, '::before');
    const after = getComputedStyle(element, '::after');
    return {
      rect: { x: rect.x, y: rect.y, width: rect.width, height: rect.height,
        right: rect.right, bottom: rect.bottom },
      matches: {
        hover: element.matches(':hover'),
        active: element.matches(':active'),
        focus: element.matches(':focus'),
        focusVisible: element.matches(':focus-visible')
      },
      style: { background: style.background, outline: style.outline, transform: style.transform },
      before: { content: before.content, background: before.background, opacity: before.opacity },
      after: { content: after.content, background: after.background, opacity: after.opacity }
    };
  }, selector);
}

async function captureInteractionStates(page, name, captureDirectory, phase, clip) {
  const selectors = Object.freeze({
    minimize: '.dreamscape-window-button.minimize',
    close: '.dreamscape-window-button.close'
  });
  const states = {};
  for (const [control, selector] of Object.entries(selectors)) {
    states[control] = {};
    await page.hover(selector);
    await page.waitForTimeout(120);
    states[control].hover = await controlState(page, selector);
    await page.screenshot({
      path: path.join(captureDirectory, `${phase}-${name}-${control}-hover.png`), clip
    });

    await page.keyboard.press('Tab');
    await page.evaluate(selector => document.querySelector(selector).focus(), selector);
    await page.waitForTimeout(120);
    states[control].focusVisible = await controlState(page, selector);
    await page.screenshot({
      path: path.join(captureDirectory, `${phase}-${name}-${control}-focus-visible.png`), clip
    });

    const center = await page.evaluate(selector => {
      const rect = document.querySelector(selector).getBoundingClientRect();
      return { x: rect.left + rect.width / 2, y: rect.top + rect.height / 2 };
    }, selector);
    await page.mouse.move(center.x, center.y);
    await page.mouse.down();
    await page.waitForTimeout(120);
    states[control].pressed = await controlState(page, selector);
    await page.screenshot({
      path: path.join(captureDirectory, `${phase}-${name}-${control}-pressed.png`), clip
    });
    await page.mouse.move(900, 500);
    await page.mouse.up();
    await page.evaluate(() => document.activeElement?.blur());
  }
  return states;
}

(async () => {
  const browser = await chromium.connectOverCDP(endpoint);
  const allPages = browser.contexts().flatMap(context => context.pages());
  const pages = Object.fromEntries(Object.entries(pageHosts).map(([name, host]) => [
    name, allPages.find(page => page.url().includes(host))
  ]));
  for (const [name, page] of Object.entries(pages)) {
    if (!page) throw new Error(`Missing ${name} WebView. Found: ${allPages.map(item => item.url())}`);
  }

  fs.mkdirSync(captureDirectory, { recursive: true });
  const report = { phase, urls: {}, pages: {} };
  let visiblePage = pages.overview;
  for (const [name, page] of Object.entries(pages)) {
    if (page !== visiblePage) {
      await visiblePage.evaluate(target => {
        document.querySelector(`[data-shell-page="${target}"]`).click();
      }, name);
      await page.waitForTimeout(400);
      visiblePage = page;
    }
    report.urls[name] = page.url();
    await page.evaluate(() => document.activeElement?.blur());
    await page.mouse.move(900, 500);
    await page.waitForTimeout(100);
    report.pages[name] = await audit(page);
    const clip = await chromeClip(page);
    await page.screenshot({
      path: path.join(captureDirectory, `${phase}-${name}-chrome-normal.png`),
      clip
    });
    if (phase === 'acceptance') {
      report.pages[name].states = await captureInteractionStates(
        page, name, captureDirectory, phase, clip);
      await page.mouse.move(900, 500);
      await page.evaluate(() => document.activeElement?.blur());
      await page.waitForTimeout(300);
      await page.screenshot({
        path: path.join(captureDirectory, `${name}-window-chrome-fixed.png`),
        timeout: 60000
      });
    }
    await page.evaluate(() => {
      document.querySelectorAll('.dreamscape-window-button').forEach(element => {
        element.dataset.auditDisplay = element.style.display;
        element.style.display = 'none';
      });
    });
    await page.screenshot({
      path: path.join(captureDirectory, `${phase}-${name}-chrome-buttons-hidden.png`),
      clip
    });
    await page.evaluate(() => {
      document.querySelectorAll('.dreamscape-window-button').forEach(element => {
        element.style.display = element.dataset.auditDisplay || '';
        delete element.dataset.auditDisplay;
      });
    });
  }

  fs.mkdirSync(path.dirname(reportPath), { recursive: true });
  fs.writeFileSync(reportPath, JSON.stringify(report, null, 2));
  console.log(JSON.stringify({
    phase,
    pages: Object.fromEntries(Object.entries(report.pages).map(([name, value]) => [name, {
      domCounts: value.domCounts,
      minimizeRect: value.controls.minimize.rect,
      closeRect: value.controls.close.rect,
      minimizeBackground: value.controls.minimize.style.background,
      closeBackground: value.controls.close.style.background,
      minimizeBefore: value.controls.minimize.before.content,
      minimizeAfter: value.controls.minimize.after.content,
      closeBefore: value.controls.close.before.content,
      closeAfter: value.controls.close.after.content
    }]))
  }, null, 2));
  await browser.close();
})().catch(error => {
  console.error(error);
  process.exitCode = 1;
});
