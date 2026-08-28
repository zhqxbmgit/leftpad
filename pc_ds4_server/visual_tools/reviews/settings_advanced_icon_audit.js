const fs = require('fs');
const path = require('path');
const { chromium } = require('playwright');

const endpoint = process.argv[2] || 'http://127.0.0.1:9225';
const reportPath = process.argv[3];
const captureDirectory = process.argv[4];
if (!reportPath || !captureDirectory) {
  throw new Error('Usage: node settings_advanced_icon_audit.js <endpoint> <report> <captures>');
}

async function pointerClick(page, selector) {
  const point = await page.evaluate(selector => {
    const element = document.querySelector(selector);
    if (!element) throw new Error(`Missing ${selector}`);
    const rect = element.getBoundingClientRect();
    return { x: rect.left + rect.width / 2, y: rect.top + rect.height / 2 };
  }, selector);
  const stack = await page.evaluate(({ x, y }) => document.elementsFromPoint(x, y).map(element => ({
    tag: element.tagName.toLowerCase(),
    id: element.id || null,
    classes: typeof element.className === 'string' ? element.className : null
  })), point);
  await page.mouse.click(point.x, point.y);
  await page.waitForTimeout(300);
  return { point, stack };
}

function rectJson(rect) {
  return { x: rect.x, y: rect.y, width: rect.width, height: rect.height };
}

(async () => {
  const browser = await chromium.connectOverCDP(endpoint);
  const pages = browser.contexts().flatMap(context => context.pages());
  const settings = pages.find(page => page.url().includes('leftpad-settings.local'));
  const overview = pages.find(page => page.url().includes('leftpad-overview.local'));
  if (!settings || !overview) throw new Error(`Missing WebView targets: ${pages.map(page => page.url())}`);

  await overview.evaluate(() => window.leftpadSpike.postCommand('showSettings'));
  await settings.waitForTimeout(450);
  await settings.evaluate(() => window.leftpadSettings.postChange('receiverUiScalePercent', 150));
  await settings.waitForFunction(() =>
    window.leftpadSettings.diagnostics().lastState.receiverUiScalePercent === 150);
  const advancedPointer = await pointerClick(settings, '[data-command="showSettingsAdvanced"]');
  await settings.waitForFunction(() => document.documentElement.dataset.section === 'advanced');

  const audit = await settings.evaluate(() => {
    const visible = element => {
      const style = getComputedStyle(element);
      const rect = element.getBoundingClientRect();
      return style.display !== 'none' && style.visibility !== 'hidden' &&
        Number(style.opacity) > 0 && rect.width > 0 && rect.height > 0;
    };
    const styleOf = element => {
      const style = getComputedStyle(element);
      const rect = element.getBoundingClientRect();
      return {
        display: style.display,
        visibility: style.visibility,
        opacity: style.opacity,
        pointerEvents: style.pointerEvents,
        rect: { x: rect.x, y: rect.y, width: rect.width, height: rect.height }
      };
    };
    const rows = [...document.querySelectorAll('#advanced-form .advanced-row')];
    return {
      section: document.documentElement.dataset.section,
      stateScale: window.leftpadSettings.diagnostics().lastState.receiverUiScalePercent,
      basicRows: document.querySelectorAll('#settings-form .setting-row').length,
      basicIcons: document.querySelectorAll('#settings-form .advanced-icon, #settings-form .field-icon').length,
      advancedRows: rows.length,
      advancedIcons: document.querySelectorAll('#advanced-form .advanced-icon').length,
      advancedLeftRows: document.querySelectorAll('#advanced-form .advanced-left-row').length,
      advancedRightRows: document.querySelectorAll('#advanced-form .advanced-right-row').length,
      visibleAdvancedIcons: [...document.querySelectorAll('#advanced-form .advanced-icon')].filter(visible).length,
      visibleLeftIcons: [...document.querySelectorAll('#advanced-form .advanced-left-row .advanced-icon')].filter(visible).length,
      visibleRightIcons: [...document.querySelectorAll('#advanced-form .advanced-right-row .advanced-icon')].filter(visible).length,
      basicForm: styleOf(document.getElementById('settings-form')),
      advancedForm: styleOf(document.getElementById('advanced-form')),
      mappingForm: styleOf(document.getElementById('mapping-section')),
      advancedBefore: getComputedStyle(document.getElementById('advanced-form'), '::before').content,
      fields: rows.map(row => ({
        field: row.querySelector('label').textContent.trim(),
        inputId: row.querySelector('input').id,
        side: row.classList.contains('advanced-left-row') ? 'left' : 'right',
        iconCount: row.querySelectorAll(':scope > .advanced-icon').length,
        visibleIconCount: [...row.querySelectorAll(':scope > .advanced-icon')].filter(visible).length
      }))
    };
  });

  const sceneGeometry = await settings.evaluate(() => {
    const rect = document.querySelector('.scene-art').getBoundingClientRect();
    return { x: rect.x, y: rect.y, scaleX: rect.width / 1672, scaleY: rect.height / 941 };
  });
  const designPoint = (x, y) => ({
    x: sceneGeometry.x + x * sceneGeometry.scaleX,
    y: sceneGeometry.y + y * sceneGeometry.scaleY
  });
  const designClip = (x, y, width, height) => ({
    x: sceneGeometry.x + x * sceneGeometry.scaleX,
    y: sceneGeometry.y + y * sceneGeometry.scaleY,
    width: width * sceneGeometry.scaleX,
    height: height * sceneGeometry.scaleY
  });
  const skullPoint = designPoint(377, 696);
  const skullStack = await settings.evaluate(({ x, y }) => ({
    topmost: (() => {
      const element = document.elementFromPoint(x, y);
      return element ? { tag: element.tagName.toLowerCase(), id: element.id || null,
        classes: typeof element.className === 'string' ? element.className : null } : null;
    })(),
    stack: document.elementsFromPoint(x, y).map(element => ({
      tag: element.tagName.toLowerCase(), id: element.id || null,
      classes: typeof element.className === 'string' ? element.className : null
    })),
    basicAtPoint: document.querySelector('#settings-form')?.contains(document.elementFromPoint(x, y)) || false,
    advancedIconAtPoint: Boolean(document.elementFromPoint(x, y)?.closest('.advanced-icon')),
    pseudoBefore: getComputedStyle(document.getElementById('advanced-form'), '::before').content,
    sceneRect: (() => {
      const rect = document.querySelector('.scene-art').getBoundingClientRect();
      return { x: rect.x, y: rect.y, width: rect.width, height: rect.height };
    })()
  }), skullPoint);

  fs.mkdirSync(captureDirectory, { recursive: true });
  await settings.screenshot({ path: path.join(captureDirectory, 'advanced-before.png') });
  await settings.screenshot({
    path: path.join(captureDirectory, 'advanced-icon-area-before.png'),
    clip: designClip(330, 570, 150, 190)
  });
  await settings.evaluate(() => {
    for (const icon of document.querySelectorAll('#advanced-form .advanced-icon')) {
      icon.dataset.auditDisplay = icon.style.display;
      icon.style.display = 'none';
    }
  });
  const visibleAfterHidingDynamicIcons = await settings.evaluate(() =>
    [...document.querySelectorAll('#advanced-form .advanced-icon')]
      .filter(icon => getComputedStyle(icon).display !== 'none').length);
  await settings.screenshot({
    path: path.join(captureDirectory, 'advanced-all-dynamic-icons-hidden.png'),
    clip: designClip(330, 570, 150, 190)
  });
  await settings.evaluate(() => {
    for (const icon of document.querySelectorAll('#advanced-form .advanced-icon')) {
      icon.style.display = icon.dataset.auditDisplay;
      delete icon.dataset.auditDisplay;
    }
  });

  const report = {
    advancedPointer,
    audit,
    sceneGeometry,
    skullPoint,
    skullStack,
    visibleAfterHidingDynamicIcons
  };
  fs.mkdirSync(path.dirname(reportPath), { recursive: true });
  fs.writeFileSync(reportPath, JSON.stringify(report, null, 2));
  console.log(JSON.stringify(report, null, 2));
  await browser.close();
})().catch(error => {
  console.error(error);
  process.exitCode = 1;
});
