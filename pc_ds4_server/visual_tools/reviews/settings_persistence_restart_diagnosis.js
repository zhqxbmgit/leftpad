const fs = require('fs');
const { chromium } = require('playwright');

const endpoint = process.argv[2] || 'http://127.0.0.1:9224';
const reportPath = process.argv[3];
if (!reportPath) throw new Error('Missing report path.');

async function clickCenter(page, selector) {
  const point = await page.evaluate(selector => {
    const element = document.querySelector(selector);
    if (!element) throw new Error(`Missing ${selector}`);
    const rect = element.getBoundingClientRect();
    return { x: rect.left + rect.width / 2, y: rect.top + rect.height / 2 };
  }, selector);
  await page.mouse.click(point.x, point.y);
  await page.waitForTimeout(300);
  return point;
}

(async () => {
  const browser = await chromium.connectOverCDP(endpoint);
  const pages = browser.contexts().flatMap(context => context.pages());
  const settings = pages.find(page => page.url().includes('leftpad-settings.local'));
  const overview = pages.find(page => page.url().includes('leftpad-overview.local'));
  if (!settings || !overview) throw new Error('Missing Settings/Overview WebView targets.');

  const webStateBeforeSettingsEntry = await settings.evaluate(
    () => window.leftpadSettings.diagnostics().lastState);
  await overview.evaluate(() => window.leftpadSpike.postCommand('showSettings'));
  await settings.waitForTimeout(400);
  await settings.evaluate(() => window.leftpadSettings.postCommand('showSettingsMappings'));
  await settings.waitForTimeout(400);

  const stateAfterSettingsEntry = await settings.evaluate(
    () => window.leftpadSettings.diagnostics().lastState);
  const rowBeforeSelection = await settings.evaluate(() => {
    const row = document.querySelector('.mapping-row[data-slot="3"]');
    return {
      text: row.textContent.trim().replace(/\s+/g, ' '),
      small: row.querySelector('.slot-selector small').textContent.trim(),
      selected: row.classList.contains('selected')
    };
  });

  const slotPoint = await clickCenter(settings, '.mapping-row[data-slot="3"] .slot-selector');
  await settings.waitForFunction(() =>
    window.leftpadSettings.diagnostics().lastState.selectedMappingSlot === 3);
  const stateAfterSelection = await settings.evaluate(
    () => window.leftpadSettings.diagnostics().lastState);
  const detail = await settings.evaluate(() => ({
    actionType: document.querySelector('#mapping-detail .detail-field.action-kind select').value,
    mainKey: document.querySelector('#mapping-detail .detail-field.main-key select').value
  }));

  const report = {
    webStateBeforeSettingsEntry,
    stateAfterSettingsEntry,
    rowBeforeSelection,
    slotPoint,
    stateAfterSelection,
    detail
  };
  fs.mkdirSync(require('path').dirname(reportPath), { recursive: true });
  fs.writeFileSync(reportPath, JSON.stringify(report, null, 2));
  console.log(JSON.stringify({
    beforeSettingsEntry: webStateBeforeSettingsEntry && {
      selected: webStateBeforeSettingsEntry.selectedMappingSlot,
      slot3: webStateBeforeSettingsEntry.mappings[2]
    },
    afterSettingsEntry: {
      selected: stateAfterSettingsEntry.selectedMappingSlot,
      slot3: stateAfterSettingsEntry.mappings[2]
    },
    rowBeforeSelection,
    afterSelection: {
      selected: stateAfterSelection.selectedMappingSlot,
      slot3: stateAfterSelection.mappings[2],
      detail
    }
  }, null, 2));
  await browser.close();
})().catch(error => {
  console.error(error.stack || String(error));
  process.exitCode = 1;
});
