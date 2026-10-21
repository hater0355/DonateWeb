const { chromium } = require('C:/Users/Admin/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright');

(async () => {
  const browser = await chromium.launch({ headless: true, executablePath: 'C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe' });
  const page = await browser.newPage({ viewport: { width: 1200, height: 1600 }, deviceScaleFactor: 1 });
  page.on('pageerror', error => process.stderr.write(`PAGEERROR ${error.message}\n`));
  await page.goto('http://127.0.0.1:8765/.tmp-contract-renderer/index.html', { waitUntil: 'networkidle' });
  await page.waitForSelector('#render-status', { timeout: 30000 });
  const pages = page.locator('.docx');
  const count = await pages.count();
  process.stdout.write(`Rendered document pages: ${count}\n`);
  const structure = await page.locator('.docx').evaluate(root => ({ root: { class: root.className, height: Math.round(root.getBoundingClientRect().height), style: root.getAttribute('style'), computed: getComputedStyle(root).cssText }, children: Array.from(root.children).map(el => ({ tag: el.tagName, cls: el.className, height: Math.round(el.getBoundingClientRect().height), width: Math.round(el.getBoundingClientRect().width), style: el.getAttribute('style') })) }));
  process.stdout.write(`Root structure: ${JSON.stringify(structure)}\n`);
  await page.emulateMedia({ media: 'print' });
  await page.pdf({ path: '.tmp-contract-renderer/contract-preview.pdf', format: 'A4', printBackground: true, preferCSSPageSize: true });
  for (let i = 0; i < count; i++) {
    const item = pages.nth(i);
    const box = await item.boundingBox();
    const text = (await item.innerText()).replace(/\s+/g, ' ').slice(0, 180);
    process.stdout.write(`Page ${i + 1}: ${Math.round(box.width)}x${Math.round(box.height)} ${text}\n`);
    await item.screenshot({ path: `.tmp-contract-renderer/page-${String(i + 1).padStart(2, '0')}.png` });
  }
  await browser.close();
})().catch(error => { console.error(error); process.exit(1); });
