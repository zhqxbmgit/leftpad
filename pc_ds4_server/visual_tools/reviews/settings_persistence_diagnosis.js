const fs = require('fs');
const { chromium } = require('playwright');

const endpoint = process.argv[2] || 'http://127.0.0.1:9223';
const reportPath = process.argv[3];
if (!reportPath) throw new Error('Missing report path.');

async function center(page, selector) {
  return page.evaluate(selector => {
    const element = document.querySelector(selector);
    if (!element) throw new Error(`Missing ${selector}`);
    const rect = element.getBoundingClientRect();
    return { x: rect.left + rect.width / 2, y: rect.top + rect.height / 2 };
  }, selector);
}

async function pointerClick(page, selector) {
  const point = await center(page, selector);
  const topmost = await page.evaluate(({ x, y }) => {
    const element = document.elementFromPoint(x, y);
    return {
      tag: element?.tagName.toLowerCase(),
      id: element?.id || null,
      classes: typeof element?.className === 'string' ? element.className : null,
      closestButton: element?.closest('button')?.id ||
        element?.closest('button')?.dataset.command || null
    };
  }, point);
  await page.mouse.click(point.x, point.y);
  await page.waitForTimeout(300);
  return { point, topmost };
}

async function state(page) {
  return page.evaluate(() => window.leftpadSettings.diagnostics().lastState);
}

(async () => {
  const browser = await chromium.connectOverCDP(endpoint);
  const pages = browser.contexts().flatMap(context => context.pages());
  const settings = pages.find(page => page.url().includes('leftpad-settings.local'));
  const overview = pages.find(page => page.url().includes('leftpad-overview.local'));
  if (!settings || !overview) throw new Error(`Missing WebView targets: ${pages.map(p => p.url())}`);

  await overview.evaluate(() => window.leftpadSpike.postCommand('showSettings'));
  await settings.waitForTimeout(400);
  await settings.evaluate(() => window.leftpadSettings.postCommand('showSettingsMappings'));
  await settings.waitForTimeout(400);

  const report = {
    receiverUiScalePercent: (await state(settings)).receiverUiScalePercent,
    selectedBefore: (await state(settings)).selectedMappingSlot,
    slot3Before: (await state(settings)).mappings[2]
  };

  report.slotPointer = await pointerClick(settings, '.mapping-row[data-slot="3"] .slot-selector');
  await settings.waitForFunction(() =>
    window.leftpadSettings.diagnostics().lastState.selectedMappingSlot === 3);

  const kindSelector = '.mapping-row[data-slot="3"] .mapping-kind';
  report.kindPointer = await pointerClick(settings, kindSelector);
  await settings.keyboard.press('Home');
  await settings.keyboard.press('ArrowDown');
  await settings.keyboard.press('Enter');
  await settings.waitForFunction(() =>
    window.leftpadSettings.diagnostics().lastState.mappings[2].actionKind === 'keyboardKey');

  const keySelector = '#mapping-detail .detail-field.main-key select';
  report.keyPointer = await pointerClick(settings, keySelector);
  await settings.keyboard.press('Home');
  await settings.keyboard.press('Enter');
  await settings.waitForFunction(() => {
    const diagnostics = window.leftpadSettings.diagnostics();
    return diagnostics.lastState.mappings[2].key === 'Tab' && diagnostics.lastState.dirty;
  });

  report.beforeApply = await state(settings);
  report.applyDisabledBefore = await settings.evaluate(
    () => document.getElementById('apply-button').disabled);
  report.applyPointer = await pointerClick(settings, '#apply-button');
  await settings.waitForFunction(() =>
    window.leftpadSettings.diagnostics().lastState.dirty === false);
  report.afterApply = await state(settings);
  report.applyDisabledAfter = await settings.evaluate(
    () => document.getElementById('apply-button').disabled);

  fs.mkdirSync(require('path').dirname(reportPath), { recursive: true });
  fs.writeFileSync(reportPath, JSON.stringify(report, null, 2));
  console.log(JSON.stringify({
    receiverUiScalePercent: report.receiverUiScalePercent,
    selectedBefore: report.selectedBefore,
    slot3Before: report.slot3Before,
    beforeApply: {
      dirty: report.beforeApply.dirty,
      selected: report.beforeApply.selectedMappingSlot,
      slot3: report.beforeApply.mappings[2],
      applyDisabled: report.applyDisabledBefore
    },
    afterApply: {
      dirty: report.afterApply.dirty,
      selected: report.afterApply.selectedMappingSlot,
      slot3: report.afterApply.mappings[2],
      applyDisabled: report.applyDisabledAfter
    },
    pointerTargets: {
      slot: report.slotPointer.topmost,
      kind: report.kindPointer.topmost,
      key: report.keyPointer.topmost,
      apply: report.applyPointer.topmost
    }
  }, null, 2));
  await browser.close();
})().catch(error => {
  console.error(error.stack || String(error));
  process.exitCode = 1;
});
