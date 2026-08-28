const fs = require('fs');
const path = require('path');
const childProcess = require('child_process');
const { chromium } = require('playwright');

const endpoint = process.argv[2];
const reportPath = process.argv[3];
const rootPid = Number(process.argv[4]);
const samplerPath = process.argv[5];

if (!endpoint || !reportPath || !Number.isInteger(rootPid) || !samplerPath) {
  throw new Error('Usage: node runtime-navigation-audit.js <endpoint> <report> <rootPid> <sampler.ps1>');
}

const hosts = Object.freeze({
  overview: 'leftpad-overview.local',
  controller: 'leftpad-controller.local',
  settings: 'leftpad-settings.local',
  logs: 'leftpad-logs.local'
});

function sampleProcess(round) {
  const output = childProcess.execFileSync(
    'powershell.exe',
    ['-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', samplerPath,
      '-RootPid', String(rootPid), '-Round', String(round)],
    { encoding: 'utf8' });
  return JSON.parse(output.trim());
}

async function shellState(page) {
  return page.evaluate(() => {
    const root = document.querySelector('#design-root').getBoundingClientRect();
    const selected = [...document.querySelectorAll('[data-shell-page]')]
      .filter(element => element.getAttribute('aria-current') === 'page');
    const rect = selector => {
      const value = document.querySelector(selector).getBoundingClientRect();
      return { x: value.x, y: value.y, width: value.width, height: value.height };
    };
    return {
      visibilityState: document.visibilityState,
      selectedCount: selected.length,
      selectedPage: selected[0]?.dataset.shellPage ?? null,
      designRoot: { x: root.x, y: root.y, width: root.width, height: root.height },
      minimize: rect('.dreamscape-window-button.minimize'),
      close: rect('.dreamscape-window-button.close'),
      focusedShellPage: document.activeElement?.dataset?.shellPage ?? null
    };
  });
}

async function diagnostics(page, globalName) {
  return page.evaluate(name => window[name].diagnostics(), globalName);
}

async function clickNav(page, target) {
  await page.evaluate(targetPage => {
    document.querySelector(`[data-shell-page="${targetPage}"]`).click();
  }, target);
  await page.waitForTimeout(45);
}

(async () => {
  const browser = await chromium.connectOverCDP(endpoint);
  const allPages = browser.contexts().flatMap(context => context.pages());
  const pages = Object.fromEntries(Object.entries(hosts).map(([name, host]) => [
    name, allPages.find(page => page.url().includes(host))
  ]));
  for (const [name, page] of Object.entries(pages)) {
    if (!page) throw new Error(`Missing ${name} target. Found: ${allPages.map(item => item.url())}`);
  }

  const report = {
    endpoint,
    rootPid,
    targetUrls: Object.fromEntries(Object.entries(pages).map(([name, page]) => [name, page.url()])),
    checkpoints: [],
    hiddenPolling: null,
    logs1000: null,
    finalShell: null
  };

  // Keep Logs visible while observing whether hidden Overview and Settings continue polling.
  await clickNav(pages.overview, 'logs');
  const pollBefore = {
    overview: await diagnostics(pages.overview, 'leftpadSpike'),
    settings: await diagnostics(pages.settings, 'leftpadSettings')
  };
  await pages.logs.waitForTimeout(2250);
  const pollAfter = {
    overview: await diagnostics(pages.overview, 'leftpadSpike'),
    settings: await diagnostics(pages.settings, 'leftpadSettings')
  };
  report.hiddenPolling = {
    waitMs: 2250,
    overviewMessagesBefore: pollBefore.overview.bridgeMessagesSent,
    overviewMessagesAfter: pollAfter.overview.bridgeMessagesSent,
    settingsMessagesBefore: pollBefore.settings.bridgeMessagesSent,
    settingsMessagesAfter: pollAfter.settings.bridgeMessagesSent,
    overviewDocumentVisibility: (await shellState(pages.overview)).visibilityState,
    settingsDocumentVisibility: (await shellState(pages.settings)).visibilityState
  };

  report.logs1000 = await pages.logs.evaluate(() => {
    const started = performance.now();
    for (let sequenceId = 1; sequenceId <= 1000; sequenceId += 1) {
      window.leftpadLogs.applyMessage({
        type: 'receiverLogAppend',
        entry: {
          sequenceId,
          category: sequenceId % 2 ? 'info' : 'warning',
          rawText: sequenceId === 1000
            ? "审计 Ω C:\\Users\\audit\\x.txt <script>alert('x')</script>"
            : `audit line ${sequenceId}`
        }
      });
    }
    const elapsedMs = performance.now() - started;
    const view = document.getElementById('log-view');
    return {
      elapsedMs,
      lineCount: view.childElementCount,
      firstSequenceId: Number(view.firstElementChild?.dataset.sequenceId),
      lastSequenceId: Number(view.lastElementChild?.dataset.sequenceId),
      lastText: view.lastElementChild?.textContent,
      scriptElementCount: view.querySelectorAll('script').length,
      diagnostics: window.leftpadLogs.diagnostics()
    };
  });

  await clickNav(pages.logs, 'overview');
  report.checkpoints.push({ round: 0, process: sampleProcess(0), shell: await shellState(pages.overview) });

  const checkpointRounds = new Set([10, 25, 50]);
  let current = 'overview';
  for (let round = 1; round <= 50; round += 1) {
    for (const target of ['controller', 'settings', 'logs', 'overview']) {
      await clickNav(pages[current], target);
      current = target;
    }
    if (checkpointRounds.has(round)) {
      report.checkpoints.push({
        round,
        process: sampleProcess(round),
        shell: await shellState(pages.overview),
        pageTargets: browser.contexts().flatMap(context => context.pages()).length
      });
    }
  }

  report.finalShell = {};
  for (const [name, page] of Object.entries(pages)) {
    report.finalShell[name] = await shellState(page);
  }

  fs.mkdirSync(path.dirname(reportPath), { recursive: true });
  fs.writeFileSync(reportPath, JSON.stringify(report, null, 2));
  process.stdout.write(JSON.stringify(report, null, 2));
  await browser.close();
})().catch(error => {
  console.error(error);
  process.exitCode = 1;
});
