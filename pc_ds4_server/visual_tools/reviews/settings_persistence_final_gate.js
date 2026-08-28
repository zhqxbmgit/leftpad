const fs = require('fs');
const path = require('path');
const { chromium } = require('playwright');

const endpoint = process.argv[2];
const mode = process.argv[3];
const reportPath = process.argv[4];
const screenshotDirectory = process.argv[5];
const settingsPath = process.argv[6];
if (!endpoint || !mode || !reportPath || !screenshotDirectory || !settingsPath) {
  throw new Error('Usage: node gate.js <endpoint> <save|restart|restored> <report> <screenshots> <settings>');
}

async function point(page, selector) {
  return page.evaluate(selector => {
    const element = document.querySelector(selector);
    if (!element) throw new Error(`Missing ${selector}`);
    const rect = element.getBoundingClientRect();
    return { x: rect.left + rect.width / 2, y: rect.top + rect.height / 2 };
  }, selector);
}

async function pointerClick(page, selector) {
  const location = await point(page, selector);
  const hit = await page.evaluate(({ x, y }) => {
    const element = document.elementFromPoint(x, y);
    return {
      tag: element?.tagName.toLowerCase(),
      id: element?.id || null,
      classes: typeof element?.className === 'string' ? element.className : null,
      button: element?.closest('button')?.id || element?.closest('button')?.dataset.command || null
    };
  }, location);
  await page.mouse.click(location.x, location.y);
  await page.waitForTimeout(300);
  return { location, hit };
}

const state = page => page.evaluate(() => window.leftpadSettings.diagnostics().lastState);
const row3 = page => page.evaluate(() => {
  const row = document.querySelector('.mapping-row[data-slot="3"]');
  return {
    selected: row.classList.contains('selected'),
    summary: row.querySelector('.slot-selector small').textContent.trim(),
    kind: row.querySelector('.mapping-kind').value,
    text: row.textContent.trim().replace(/\s+/g, ' ')
  };
});

(async () => {
  const browser = await chromium.connectOverCDP(endpoint);
  const pages = browser.contexts().flatMap(context => context.pages());
  const settings = pages.find(page => page.url().includes('leftpad-settings.local'));
  const overview = pages.find(page => page.url().includes('leftpad-overview.local'));
  if (!settings || !overview) throw new Error('Missing Settings/Overview WebView targets.');
  await overview.evaluate(() => window.leftpadSpike.postCommand('showSettings'));
  await settings.waitForTimeout(400);
  await settings.evaluate(() => window.leftpadSettings.postCommand('showSettingsMappings'));
  await settings.waitForTimeout(400);

  fs.mkdirSync(path.dirname(reportPath), { recursive: true });
  fs.mkdirSync(screenshotDirectory, { recursive: true });
  const report = {
    mode,
    viewport: await settings.evaluate(() => ({ width: innerWidth, height: innerHeight })),
    shell: await settings.evaluate(() => window.leftpadSettings.diagnostics().shell),
    entryState: await state(settings),
    rowBeforeSelection: await row3(settings)
  };

  if (mode === 'save') {
    await settings.evaluate(() =>
      window.leftpadSettings.postChange('receiverUiScalePercent', 150));
    await settings.waitForFunction(() =>
      window.leftpadSettings.diagnostics().lastState.receiverUiScalePercent === 150);
    report.slotPointer = await pointerClick(
      settings, '.mapping-row[data-slot="3"] .slot-selector');
    await settings.waitForFunction(() =>
      window.leftpadSettings.diagnostics().lastState.selectedMappingSlot === 3);
    const keySelector = '#mapping-detail .detail-field.main-key select';
    await settings.locator(keySelector).selectOption('E');
    await settings.waitForFunction(() =>
      window.leftpadSettings.diagnostics().lastState.mappings[2].key === 'E');
    report.keyPointer = await pointerClick(settings, keySelector);
    await settings.keyboard.press('Home');
    await settings.keyboard.press('Enter');
    await settings.waitForFunction(() => {
      const current = window.leftpadSettings.diagnostics().lastState;
      return current.mappings[2].key === 'Tab' && current.dirty &&
        document.getElementById('apply-button').disabled === false;
    });
    report.beforeApplyState = await state(settings);
    report.beforeApplyRow = await row3(settings);
    report.applyDisabledBefore = await settings.evaluate(
      () => document.getElementById('apply-button').disabled);
    await settings.screenshot({
      path: path.join(screenshotDirectory, 'mapping-tab-before-save.png')
    });

    report.applyPointer = await pointerClick(settings, '#apply-button');
    await settings.waitForFunction(() => {
      const status = document.getElementById('status-message');
      return window.leftpadSettings.diagnostics().lastState.dirty === false &&
        status.classList.contains('visible') && status.textContent === '设置已保存';
    });
    report.afterApplyState = await state(settings);
    report.afterApplyRow = await row3(settings);
    report.applyFeedback = await settings.evaluate(() => {
      const status = document.getElementById('status-message');
      return { text: status.textContent, tone: status.dataset.tone, visible: status.classList.contains('visible') };
    });
    await settings.screenshot({
      path: path.join(screenshotDirectory, 'mapping-tab-after-save.png')
    });
  } else if (mode === 'restart') {
    await settings.screenshot({
      path: path.join(screenshotDirectory, 'mapping-tab-after-restart.png')
    });
    report.basicPointer = await pointerClick(settings, '[data-command="showSettingsBasic"]');
    report.sectionAfterBasic = await settings.evaluate(() => document.documentElement.dataset.section);
    await pointerClick(settings, '[data-command="showSettingsMappings"]');
    report.advancedPointer = await pointerClick(settings, '[data-command="showSettingsAdvanced"]');
    report.sectionAfterAdvanced = await settings.evaluate(() => document.documentElement.dataset.section);
    await pointerClick(settings, '[data-command="showSettingsMappings"]');
    report.previewPointer = await pointerClick(settings, '#preview-button');
    await settings.waitForFunction(() =>
      window.leftpadSettings.diagnostics().lastState.previewActive === true);
    report.hidePointer = await pointerClick(settings, '#hide-preview-button');
    await settings.waitForFunction(() =>
      window.leftpadSettings.diagnostics().lastState.previewActive === false);

    report.slotPointer = await pointerClick(
      settings, '.mapping-row[data-slot="3"] .slot-selector');
    await settings.waitForFunction(() =>
      window.leftpadSettings.diagnostics().lastState.selectedMappingSlot === 3);
    report.afterSelectionState = await state(settings);
    report.detail = await settings.evaluate(() => ({
      actionType: document.querySelector('#mapping-detail .detail-field.action-kind select').value,
      mainKey: document.querySelector('#mapping-detail .detail-field.main-key select').value
    }));
    await settings.screenshot({
      path: path.join(screenshotDirectory, 'mapping-tab-detail-after-restart.png')
    });
  }

  const disk = JSON.parse(fs.readFileSync(settingsPath, 'utf8'));
  report.diskSlot3 = disk.mappingsByProfile?.['radial-6']?.[2] || null;
  report.diskReceiverUiScalePercent = disk.receiverUiScalePercent;
  fs.writeFileSync(reportPath, JSON.stringify(report, null, 2));
  console.log(JSON.stringify({
    mode,
    entry: {
      receiverUiScalePercent: report.entryState.receiverUiScalePercent,
      selected: report.entryState.selectedMappingSlot,
      slot3: report.entryState.mappings[2],
      row: report.rowBeforeSelection
    },
    beforeApply: report.beforeApplyState && {
      dirty: report.beforeApplyState.dirty,
      slot3: report.beforeApplyState.mappings[2],
      row: report.beforeApplyRow,
      applyDisabled: report.applyDisabledBefore
    },
    afterApply: report.afterApplyState && {
      dirty: report.afterApplyState.dirty,
      slot3: report.afterApplyState.mappings[2],
      row: report.afterApplyRow,
      feedback: report.applyFeedback
    },
    restart: report.afterSelectionState && {
      sectionAfterBasic: report.sectionAfterBasic,
      sectionAfterAdvanced: report.sectionAfterAdvanced,
      slot3: report.afterSelectionState.mappings[2],
      detail: report.detail
    },
    disk: { receiverUiScalePercent: report.diskReceiverUiScalePercent, slot3: report.diskSlot3 }
  }, null, 2));
  await browser.close();
})().catch(error => {
  console.error(error.stack || String(error));
  process.exitCode = 1;
});
