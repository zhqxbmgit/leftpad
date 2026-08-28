const fs = require('fs');
const { chromium } = require('playwright');

const endpoint = process.argv[2];
const reportPath = process.argv[3];

(async () => {
  const browser = await chromium.connectOverCDP(endpoint);
  const pages = browser.contexts().flatMap(context => context.pages());
  const overview = pages.find(page => page.url().includes('leftpad-overview.local'));
  if (!overview) throw new Error('Overview WebView target not found.');
  await overview.evaluate(() => {
    document.querySelector('[data-shell-command="closeWindow"]').click();
  });
  await overview.waitForTimeout(500);
  const result = { closeCommandSent: true, targetStillAlive: !overview.isClosed(), url: overview.url() };
  fs.writeFileSync(reportPath, JSON.stringify(result, null, 2));
  process.stdout.write(JSON.stringify(result));
  await browser.close();
})().catch(error => {
  console.error(error);
  process.exitCode = 1;
});
