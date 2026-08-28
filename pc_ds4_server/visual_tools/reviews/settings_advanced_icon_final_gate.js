const fs = require('fs');
const path = require('path');
const { chromium } = require('playwright');

const endpoint = process.argv[2];
const reportPath = process.argv[3];
const captureDirectory = process.argv[4];
if (!endpoint || !reportPath || !captureDirectory) {
  throw new Error('Usage: node settings_advanced_icon_final_gate.js <endpoint> <report> <captures>');
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
  await page.waitForTimeout(320);
  return { point, stack };
}

async function sectionAudit(page) {
  return page.evaluate(() => {
    const styleOf = selector => {
      const element = document.querySelector(selector);
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
    return {
      section: document.documentElement.dataset.section,
      basic: styleOf('#settings-form'),
      advanced: styleOf('#advanced-form'),
      mapping: styleOf('#mapping-section')
    };
  });
}

(async () => {
  const browser = await chromium.connectOverCDP(endpoint);
  const pages = browser.contexts().flatMap(context => context.pages());
  const settings = pages.find(page => page.url().includes('leftpad-settings.local'));
  const overview = pages.find(page => page.url().includes('leftpad-overview.local'));
  if (!settings || !overview) throw new Error(`Missing WebView targets: ${pages.map(page => page.url())}`);

  fs.mkdirSync(captureDirectory, { recursive: true });
  await overview.evaluate(() => window.leftpadSpike.postCommand('showSettings'));
  await settings.waitForTimeout(450);
  await settings.evaluate(() => window.leftpadSettings.postChange('receiverUiScalePercent', 150));
  await settings.waitForFunction(() =>
    window.leftpadSettings.diagnostics().lastState.receiverUiScalePercent === 150);

  const scene = await settings.evaluate(() => {
    const rect = document.querySelector('.scene-art').getBoundingClientRect();
    return { x: rect.x, y: rect.y, scaleX: rect.width / 1672, scaleY: rect.height / 941 };
  });
  const designPoint = (x, y) => ({ x: scene.x + x * scene.scaleX, y: scene.y + y * scene.scaleY });
  const designClip = (x, y, width, height) => ({
    x: scene.x + x * scene.scaleX,
    y: scene.y + y * scene.scaleY,
    width: width * scene.scaleX,
    height: height * scene.scaleY
  });

  const report = {
    viewport: await settings.evaluate(() => ({ width: innerWidth, height: innerHeight })),
    scene,
    initialState: await settings.evaluate(() => window.leftpadSettings.diagnostics().lastState)
  };

  report.basicPointer = await pointerClick(settings, '[data-command="showSettingsBasic"]');
  await settings.waitForFunction(() => document.documentElement.dataset.section === 'basic');
  report.basic = await sectionAudit(settings);
  report.basicCounts = await settings.evaluate(() => ({
    rows: document.querySelectorAll('#settings-form .setting-row').length,
    deadZoneLabel: document.querySelector('label[for="dead-zone"]').textContent.trim(),
    deadZoneRect: (() => {
      const rect = document.querySelector('label[for="dead-zone"]').closest('.setting-row').getBoundingClientRect();
      return { x: rect.x, y: rect.y, width: rect.width, height: rect.height };
    })()
  }));
  await settings.screenshot({ path: path.join(captureDirectory, 'settings-basic-icon-regression.png') });

  report.advancedPointer = await pointerClick(settings, '[data-command="showSettingsAdvanced"]');
  await settings.waitForFunction(() => document.documentElement.dataset.section === 'advanced');
  report.advanced = await sectionAudit(settings);
  report.advancedAudit = await settings.evaluate(() => {
    const visible = element => {
      const style = getComputedStyle(element);
      const rect = element.getBoundingClientRect();
      return style.display !== 'none' && style.visibility !== 'hidden' &&
        Number(style.opacity) > 0 && rect.width > 0 && rect.height > 0;
    };
    const rows = [...document.querySelectorAll('#advanced-form .advanced-row')];
    const pseudo = side => {
      const style = getComputedStyle(document.getElementById('advanced-form'), side);
      return {
        content: style.content,
        left: style.left,
        top: style.top,
        width: style.width,
        height: style.height,
        backgroundImage: style.backgroundImage,
        pointerEvents: style.pointerEvents,
        zIndex: style.zIndex
      };
    };
    return {
      rows: rows.length,
      icons: document.querySelectorAll('#advanced-form .advanced-icon').length,
      visibleIcons: [...document.querySelectorAll('#advanced-form .advanced-icon')].filter(visible).length,
      leftRows: document.querySelectorAll('#advanced-form .advanced-left-row').length,
      rightRows: document.querySelectorAll('#advanced-form .advanced-right-row').length,
      visibleLeftIcons: [...document.querySelectorAll('#advanced-form .advanced-left-row .advanced-icon')].filter(visible).length,
      visibleRightIcons: [...document.querySelectorAll('#advanced-form .advanced-right-row .advanced-icon')].filter(visible).length,
      basicRows: document.querySelectorAll('#settings-form .setting-row').length,
      visibleBasicRows: [...document.querySelectorAll('#settings-form .setting-row')].filter(visible).length,
      beforeMask: pseudo('::before'),
      afterMask: pseudo('::after'),
      fields: rows.map(row => ({
        field: row.querySelector('label').textContent.trim(),
        side: row.classList.contains('advanced-left-row') ? 'left' : 'right',
        iconCount: row.querySelectorAll(':scope > .advanced-icon').length,
        visibleIconCount: [...row.querySelectorAll(':scope > .advanced-icon')].filter(visible).length
      }))
    };
  });
  const skullPoint = designPoint(377, 696);
  report.skullPointAudit = await settings.evaluate(({ x, y }) => ({
    topmost: (() => {
      const element = document.elementFromPoint(x, y);
      return element ? { tag: element.tagName.toLowerCase(), id: element.id || null,
        classes: typeof element.className === 'string' ? element.className : null } : null;
    })(),
    stack: document.elementsFromPoint(x, y).map(element => ({
      tag: element.tagName.toLowerCase(), id: element.id || null,
      classes: typeof element.className === 'string' ? element.className : null
    })),
    basicRow: Boolean(document.elementFromPoint(x, y)?.closest('#settings-form .setting-row')),
    basicIcon: Boolean(document.elementFromPoint(x, y)?.closest('#settings-form .advanced-icon, #settings-form .field-icon')),
    advancedIcon: Boolean(document.elementFromPoint(x, y)?.closest('#advanced-form .advanced-icon')),
    beforeContent: getComputedStyle(document.getElementById('advanced-form'), '::before').content,
    beforeBoundsInDesignPixels: { left: 342, top: 288, width: 70, height: 454 },
    coveredByAdvancedBackgroundMask: x >= (342 / 1672) * document.querySelector('.scene-art').getBoundingClientRect().width &&
      x <= (412 / 1672) * document.querySelector('.scene-art').getBoundingClientRect().width &&
      y >= document.querySelector('.scene-art').getBoundingClientRect().y +
        (288 / 941) * document.querySelector('.scene-art').getBoundingClientRect().height &&
      y <= document.querySelector('.scene-art').getBoundingClientRect().y +
        (742 / 941) * document.querySelector('.scene-art').getBoundingClientRect().height
  }), skullPoint);
  await settings.screenshot({ path: path.join(captureDirectory, 'settings-advanced-icon-fixed.png') });
  await settings.screenshot({
    path: path.join(captureDirectory, 'advanced-icon-area-after.png'),
    clip: designClip(330, 570, 150, 190)
  });

  report.mappingPointer = await pointerClick(settings, '[data-command="showSettingsMappings"]');
  await settings.waitForFunction(() => document.documentElement.dataset.section === 'mappings');
  report.mapping = await sectionAudit(settings);
  report.mappingAudit = await settings.evaluate(() => ({
    selectedMappingSlot: window.leftpadSettings.diagnostics().lastState.selectedMappingSlot,
    row3Summary: document.querySelector('.mapping-row[data-slot="3"] .slot-selector small').textContent.trim(),
    basicVisibleRows: [...document.querySelectorAll('#settings-form .setting-row')].filter(row => {
      const rect = row.getBoundingClientRect();
      return getComputedStyle(row).display !== 'none' && rect.width > 0 && rect.height > 0;
    }).length,
    advancedVisibleIcons: [...document.querySelectorAll('#advanced-form .advanced-icon')].filter(icon => {
      const rect = icon.getBoundingClientRect();
      return getComputedStyle(icon).display !== 'none' && rect.width > 0 && rect.height > 0;
    }).length
  }));
  report.previewPointer = await pointerClick(settings, '#preview-button');
  await settings.waitForFunction(() => window.leftpadSettings.diagnostics().lastState.previewActive === true);
  report.hidePreviewPointer = await pointerClick(settings, '#hide-preview-button');
  await settings.waitForFunction(() => window.leftpadSettings.diagnostics().lastState.previewActive === false);
  report.finalState = await settings.evaluate(() => window.leftpadSettings.diagnostics().lastState);
  await settings.screenshot({ path: path.join(captureDirectory, 'settings-mapping-icon-regression.png') });

  fs.mkdirSync(path.dirname(reportPath), { recursive: true });
  fs.writeFileSync(reportPath, JSON.stringify(report, null, 2));
  console.log(JSON.stringify({
    scale: report.initialState.receiverUiScalePercent,
    basicRows: report.basicCounts.rows,
    advanced: report.advancedAudit,
    skullPointAudit: report.skullPointAudit,
    mapping: report.mappingAudit,
    previewActiveAtEnd: report.finalState.previewActive,
    dirtyAtEnd: report.finalState.dirty
  }, null, 2));
  await browser.close();
})().catch(error => {
  console.error(error);
  process.exitCode = 1;
});
