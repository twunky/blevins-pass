// Optional UI verification: provide PLAYWRIGHT_MODULE or install Playwright in your test environment.
// Uses the real local API; never submits an order or payment. Restores the persisted theme afterward.
import assert from 'node:assert/strict';
import { writeFile, mkdir } from 'node:fs/promises';
const { chromium } = await import(process.env.PLAYWRIGHT_MODULE ?? 'playwright');

const output = new URL('./', import.meta.url);
await mkdir(output, { recursive: true });
const browser = await chromium.launch({ channel: 'chrome', headless: true });
const context = await browser.newContext();
const page = await context.newPage();
const errors = [];
const results = [];
page.on('pageerror', error => errors.push(error.message));
const origin = process.env.UI_ORIGIN ?? 'http://127.0.0.1:5173';
const initialTheme = await (await context.request.get(`${origin}/api/settings/theme`)).json();

async function chooseTheme(theme) {
  const saved = page.waitForResponse(response => response.url().includes('/api/settings/theme') && response.request().method() === 'PUT');
  await page.getByRole('button', { name: theme, exact: true }).click();
  await page.waitForFunction(value => document.documentElement.dataset.theme === value, theme.toLowerCase());
  await saved;
  await page.locator('.hero-art').evaluate(image => image.decode());
}

try {
  for (const width of [1440, 1024, 768, 390, 360]) {
    await page.setViewportSize({ width, height: 900 });
    await page.goto(origin);
    await page.locator('.experience-link').first().waitFor();
    assert.equal(await page.locator('.experience-link').count(), 6);
    for (const theme of ['Fall', 'Winter']) {
      await chooseTheme(theme);
      await page.screenshot({ path: new URL(`${theme.toLowerCase()}-${width}.png`, output).pathname.replace(/^\/(\w:)/, '$1') });
      const widths = await page.evaluate(() => ({ viewport: innerWidth, document: document.documentElement.scrollWidth }));
      assert.ok(widths.document <= widths.viewport + 1, `${theme} ${width}: horizontal overflow ${JSON.stringify(widths)}`);
      results.push({ width, theme, ...widths });
    }
    await page.locator('.experience-link').first().click();
    await page.getByRole('button', { name: 'More Adult', exact: true }).click();
    assert.match(await page.locator('.summary-total').innerText(), /24\.00/);
    await chooseTheme('Fall');
    assert.match(await page.locator('.summary-total').innerText(), /24\.00/);
    await page.getByRole('button', { name: 'Review order', exact: true }).click();
    await page.getByRole('heading', { name: 'Review your order' }).waitFor();
    await page.waitForFunction(() => window.scrollY === 0);
    assert.match(await page.locator('.order-table').innerText(), /24\.00/);
    assert.ok(await page.getByRole('button', { name: 'Pay with Stripe', exact: true }).isEnabled());
    assert.ok(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth + 1), `Review ${width}: horizontal overflow`);
    await page.screenshot({ path: new URL(`review-${width}.png`, output).pathname.replace(/^\/(\w:)/, '$1') });
    await page.getByRole('button', { name: 'Change selection', exact: true }).click();
    await page.locator('.experience-link').filter({ hasText: 'Winter Lights Concert' }).click();
    const seat = page.locator('.seat[aria-disabled="false"]').first();
    await seat.waitFor();
    const target = await seat.boundingBox();
    assert.ok(target.width >= 43.9 && target.height >= 43.9, `Seating ${width}: touch target too small`);
    await seat.focus();
    await page.keyboard.press('Enter');
    assert.equal(await page.locator('.seat.seat-selected').count(), 1);
    assert.ok(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth + 1), `Seating ${width}: horizontal overflow`);
    assert.ok(await page.getByRole('button', { name: 'Review order', exact: true }).isEnabled());
    await page.locator('.seat-map').scrollIntoViewIfNeeded();
    await page.screenshot({ path: new URL(`seating-${width}.png`, output).pathname.replace(/^\/(\w:)/, '$1') });
    results.push({ width, booking: 'quantity, theme preservation, review and keyboard seat selection passed' });
  }
  assert.deepEqual(errors, []);
  await writeFile(new URL('verification-results.json', output), JSON.stringify({ results, pageErrors: errors, paymentSubmitted: false }, null, 2));
  console.log(JSON.stringify({ passed: true, viewportChecks: results.length, pageErrors: errors.length }));
} finally {
  await context.request.put(`${origin}/api/settings/theme`, { data: initialTheme });
  await browser.close();
}
