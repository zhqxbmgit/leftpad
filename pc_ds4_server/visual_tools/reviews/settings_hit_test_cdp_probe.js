const fs = require('fs');
const { chromium } = require('playwright');

const endpoint = process.argv[2] || 'http://127.0.0.1:9222';
const reportPath = process.argv[3];
const mode = process.argv[4] || 'baseline';
const screenshotDirectory = process.argv[5] || require('path').dirname(reportPath);

if (!reportPath) {
  throw new Error('Usage: node settings_hit_test_cdp_probe.js <endpoint> <report-path> [baseline|final]');
}

function describe(element) {
  if (!element) return null;
  const classes = typeof element.className === 'string'
    ? element.className.trim().split(/\s+/).filter(Boolean).join('.')
    : '';
  return {
    tag: element.tagName.toLowerCase(),
    id: element.id || null,
    classes: classes || null,
    command: element.dataset?.command || null,
    text: (element.textContent || '').trim().replace(/\s+/g, ' ').slice(0, 80),
    disabled: 'disabled' in element ? element.disabled : null
  };
}

async function hitAtCenter(page, selector) {
  return page.evaluate((selector) => {
    const target = document.querySelector(selector);
    if (!target) throw new Error(`Missing target ${selector}`);
    const rect = target.getBoundingClientRect();
    const x = rect.left + rect.width / 2;
    const y = rect.top + rect.height / 2;
    const describe = (element) => {
      if (!element) return null;
      const classes = typeof element.className === 'string'
        ? element.className.trim().split(/\s+/).filter(Boolean).join('.')
        : '';
      return {
        tag: element.tagName.toLowerCase(), id: element.id || null,
        classes: classes || null, command: element.dataset?.command || null,
        text: (element.textContent || '').trim().replace(/\s+/g, ' ').slice(0, 80),
        disabled: 'disabled' in element ? element.disabled : null
      };
    };
    return {
      selector,
      point: { x, y },
      target: describe(target),
      topmost: describe(document.elementFromPoint(x, y)),
      stack: document.elementsFromPoint(x, y).slice(0, 8).map(describe)
    };
  }, selector);
}

async function clickCenter(page, selector) {
  const hit = await hitAtCenter(page, selector);
  await page.mouse.click(hit.point.x, hit.point.y);
  await page.waitForTimeout(350);
  return {
    hit,
    sectionAfter: await page.evaluate(() => document.documentElement.dataset.section),
    previewActiveAfter: await page.evaluate(() => document.documentElement.dataset.previewActive || null)
  };
}

async function waitForState(page, predicate, description) {
  await page.waitForFunction(predicate, null, { timeout: 5000 });
  return page.evaluate(() => window.leftpadSettings.diagnostics().lastState);
}

(async () => {
  const browser = await chromium.connectOverCDP(endpoint);
  const pages = browser.contexts().flatMap(context => context.pages());
  const settings = pages.find(page => page.url().includes('leftpad-settings.local'));
  const overview = pages.find(page => page.url().includes('leftpad-overview.local'));
  if (!settings || !overview) {
    throw new Error(`Expected Overview and Settings targets. URLs: ${pages.map(page => page.url()).join(', ')}`);
  }

  await overview.evaluate(() => window.leftpadSpike.postCommand('showSettings'));
  await settings.waitForTimeout(500);
  await settings.evaluate(() => window.leftpadSettings.postCommand('showSettingsMappings'));
  await settings.waitForTimeout(500);

  const selectors = {
    basic: '[data-command="showSettingsBasic"]',
    advanced: '[data-command="showSettingsAdvanced"]',
    mappings: '[data-command="showSettingsMappings"]',
    preview: '#preview-button',
    hidePreview: '#hide-preview-button',
    apply: '#apply-button',
    restore: '#restore-button',
    mappingBlank: '#mapping-grid',
    slot: '.mapping-row .slot-selector',
    kind: '.mapping-row .mapping-kind',
    detail: '#mapping-detail'
  };

  const report = {
    mode,
    urls: pages.map(page => page.url()),
    viewport: await settings.evaluate(() => ({ width: innerWidth, height: innerHeight })),
    shell: await settings.evaluate(() => window.leftpadSettings.diagnostics().shell),
    sectionBefore: await settings.evaluate(() => document.documentElement.dataset.section),
    hits: {}
  };

  for (const [name, selector] of Object.entries(selectors)) {
    report.hits[name] = await hitAtCenter(settings, selector);
  }

  report.basicPointerClick = await clickCenter(settings, selectors.basic);
  if (mode === 'baseline') {
    await settings.evaluate(() => window.leftpadSettings.postCommand('showSettingsMappings'));
    await settings.waitForTimeout(250);
    report.advancedPointerClick = await clickCenter(settings, selectors.advanced);
  } else {
    fs.mkdirSync(screenshotDirectory, { recursive: true });
    const screenshot = async name => settings.screenshot({
      path: require('path').join(screenshotDirectory, name)
    });

    await settings.evaluate(() => window.leftpadSettings.postCommand('showSettingsMappings'));
    await settings.waitForTimeout(300);
    await screenshot('settings-mapping-hit-test-fixed.png');

    report.tabNavigation = {};
    report.tabNavigation.mappingToBasic = await clickCenter(settings, selectors.basic);
    await screenshot('settings-basic-after-mapping-click.png');
    await clickCenter(settings, selectors.mappings);
    report.tabNavigation.mappingToAdvanced = await clickCenter(settings, selectors.advanced);
    await screenshot('settings-advanced-after-mapping-click.png');
    report.tabNavigation.advancedToMapping = await clickCenter(settings, selectors.mappings);

    report.mappingInteraction = {};
    report.mappingInteraction.slot = await clickCenter(settings, selectors.slot);
    report.mappingInteraction.selectedSlot = await settings.evaluate(
      () => window.leftpadSettings.diagnostics().lastState.selectedMappingSlot);
    report.mappingInteraction.kindHit = await hitAtCenter(settings, selectors.kind);
    const kindPoint = report.mappingInteraction.kindHit.point;
    await settings.mouse.click(kindPoint.x, kindPoint.y);
    await settings.waitForTimeout(180);
    report.mappingInteraction.kindFocused = await settings.evaluate(
      () => document.activeElement?.classList.contains('mapping-kind'));
    await settings.keyboard.press('Escape');

    await settings.locator('.mapping-row .mapping-kind').first().selectOption('keyboardShortcut');
    await waitForState(settings,
      () => window.leftpadSettings.diagnostics().lastState.mappings[0].actionKind === 'keyboardShortcut',
      'keyboard shortcut detail');
    report.mappingInteraction.shortcutMainKey = await hitAtCenter(
      settings, '#mapping-detail .detail-field.main-key select');
    report.mappingInteraction.modifierAlt = await clickCenter(
      settings, '#mapping-detail .modifier-chip:nth-child(2)');
    report.mappingInteraction.modifierAltPressed = await settings.evaluate(() =>
      document.querySelector('#mapping-detail .modifier-chip:nth-child(2)').getAttribute('aria-pressed'));

    await settings.locator('.mapping-row .mapping-kind').first().selectOption('ds4Button');
    await waitForState(settings,
      () => window.leftpadSettings.diagnostics().lastState.mappings[0].actionKind === 'ds4Button',
      'DS4 detail');
    report.mappingInteraction.ds4Action = await hitAtCenter(
      settings, '#mapping-detail .detail-field.ds4-action select');
    report.mappingInteraction.detail = await hitAtCenter(settings, selectors.detail);

    report.geometry = {};
    report.geometry.radial6 = await settings.evaluate(() => {
      const diagnostics = window.leftpadSettings.diagnostics();
      return {
        slots: diagnostics.mappingSlots,
        columns: diagnostics.mappingColumns,
        overflow: diagnostics.mappingOverflow,
        detailTop: document.getElementById('mapping-detail').getBoundingClientRect().top
      };
    });
    await settings.evaluate(() =>
      window.leftpadSettings.postChange('visualPackId', 'radial-8-minimal-v1'));
    await waitForState(settings,
      () => window.leftpadSettings.diagnostics().lastState.mappingSlotCount === 8,
      'radial 8');
    report.geometry.radial8 = await settings.evaluate(() => {
      const diagnostics = window.leftpadSettings.diagnostics();
      return {
        slots: diagnostics.mappingSlots,
        columns: diagnostics.mappingColumns,
        overflow: diagnostics.mappingOverflow,
        detailTop: document.getElementById('mapping-detail').getBoundingClientRect().top
      };
    });
    await settings.evaluate(() => window.leftpadSettings.postChange('visualPackId', 'radial-v5'));
    await waitForState(settings,
      () => window.leftpadSettings.diagnostics().lastState.mappingSlotCount === 6,
      'radial 6 restore');

    report.bottomActions = {};
    report.bottomActions.preview = await clickCenter(settings, selectors.preview);
    report.bottomActions.previewState = await waitForState(settings,
      () => window.leftpadSettings.diagnostics().lastState.previewActive === true,
      'preview active');
    await screenshot('settings-mapping-preview-active.png');
    report.bottomActions.hidePreviewHitEnabled = await hitAtCenter(settings, selectors.hidePreview);
    report.bottomActions.hidePreview = await clickCenter(settings, selectors.hidePreview);
    report.bottomActions.hidePreviewState = await waitForState(settings,
      () => window.leftpadSettings.diagnostics().lastState.previewActive === false,
      'preview hidden');

    const originalSize = await settings.evaluate(
      () => window.leftpadSettings.diagnostics().lastState.overallSizePercent);
    const temporarySize = originalSize < 150 ? originalSize + 1 : originalSize - 1;
    await settings.evaluate(value =>
      window.leftpadSettings.postChange('overallSizePercent', value), temporarySize);
    report.bottomActions.applyEnabledState = await waitForState(settings,
      () => window.leftpadSettings.diagnostics().lastState.dirty === true &&
        document.getElementById('apply-button').disabled === false,
      'dirty apply enabled');
    report.bottomActions.applyHitEnabled = await hitAtCenter(settings, selectors.apply);
    report.bottomActions.apply = await clickCenter(settings, selectors.apply);
    report.bottomActions.applyState = await waitForState(settings,
      () => window.leftpadSettings.diagnostics().lastState.dirty === false,
      'apply persisted');

    report.bottomActions.restoreHit = await hitAtCenter(settings, selectors.restore);
    report.bottomActions.restore = await clickCenter(settings, selectors.restore);
    report.bottomActions.restoreState = await waitForState(settings,
      () => window.leftpadSettings.diagnostics().lastState.dirty === true,
      'restore default draft');
    report.previewClosedAtEnd = report.bottomActions.restoreState.previewActive === false;
  }

  fs.mkdirSync(require('path').dirname(reportPath), { recursive: true });
  fs.writeFileSync(reportPath, JSON.stringify(report, null, 2));
  console.log(JSON.stringify(report, null, 2));
  await browser.close();
})().catch(error => {
  console.error(error.stack || String(error));
  process.exitCode = 1;
});
