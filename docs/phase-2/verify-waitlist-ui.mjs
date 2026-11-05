// Optional rendered check of the Phase 2 waitlist UI (Playwright + local Chrome/Chromium).
// Run against a DISPOSABLE fixture database: it sells out the first hayride departure with held
// (unpaid) orders, joins the waitlist twice, raises capacity and records one manual notification.
// It never clicks "Buy these tickets" (that would create a Stripe test session).
//   PLAYWRIGHT_MODULE=file:///.../playwright/index.mjs node docs/phase-2/verify-waitlist-ui.mjs
import assert from 'node:assert/strict';
import { mkdir, writeFile } from 'node:fs/promises';
import { fileURLToPath } from 'node:url';

const { chromium } = await import(process.env.PLAYWRIGHT_MODULE ?? 'playwright');
const origin = process.env.UI_ORIGIN ?? 'http://127.0.0.1:5173';
const outputDir = new URL('../../ai-sessions/PHASE-2/evidence/ui/', import.meta.url);
await mkdir(outputDir, { recursive: true });
const shot = (name) => fileURLToPath(new URL(name, outputDir));

async function api(path, init) {
  const response = await fetch(`${origin}${path}`, { ...init, headers: { 'Content-Type': 'application/json', ...init?.headers } });
  if (!response.ok) throw new Error(`${path} -> ${response.status} ${await response.text()}`);
  return response.status === 204 ? null : response.json();
}

// 1. Sell out the first bookable hayride departure (held orders only; no checkout).
const events = await api('/api/events');
const hayride = events.find((e) => e.name === 'Pumpkin Patch Hayride');
const slot = hayride.timeSlots.find((s) => s.isBookable);
const availability = await api(`/api/events/${hayride.id}/availability?timeSlotId=${slot.id}`);
const typeId = (name) => availability.ticketTypes.find((t) => t.name === name).ticketTypeId;
for (const [key, tickets] of [
  ['ui-fill-1', [{ ticketTypeId: typeId('Family Wagon'), quantity: 3 }, { ticketTypeId: typeId('Adult'), quantity: 12 }]],
  ['ui-fill-2', [{ ticketTypeId: typeId('Child'), quantity: 10 }]],
]) {
  await api('/api/orders', { method: 'POST', body: JSON.stringify({ timeSlotId: slot.id, tickets }), headers: { 'Idempotency-Key': `${key}-${slot.id}` } });
}
const venueTime = new Intl.DateTimeFormat('en-US', { timeZone: 'America/New_York', hour: 'numeric', minute: '2-digit' }).format(new Date(slot.startUtc));

const browser = await chromium.launch({ channel: 'chrome', headless: true });
const results = [];
const errors = [];
const links = [];

try {
  // 2. Customer: sold-out panel and join, at desktop and phone widths, from two non-venue time zones.
  for (const [width, timezoneId] of [[1440, 'America/Los_Angeles'], [390, 'Asia/Tokyo']]) {
    const context = await browser.newContext({ viewport: { width, height: 900 }, timezoneId });
    const page = await context.newPage();
    page.on('pageerror', (error) => errors.push(`${width}: ${error.message}`));
    await page.goto(origin);
    await page.getByRole('button', { name: /Pumpkin Patch Hayride/ }).first().click();
    await page.getByText('This departure is full (25 people maximum).').waitFor();
    assert.ok(await page.getByRole('button', { name: 'More Adult', exact: true }).isDisabled(), 'sold-out steppers disabled');
    await page.getByRole('button', { name: 'More Child on waitlist' }).click();
    await page.getByRole('button', { name: 'More Child on waitlist' }).click();
    await page.getByLabel('Email for the organizer to contact you').fill(`ui-check-${width}@example.com`);
    await page.locator('#booking').screenshot({ path: shot(`waitlist-join-${width}.png`) });
    await page.getByRole('button', { name: 'Join waitlist (no charge)' }).click();
    await page.getByRole('heading', { name: 'You’re on the waitlist' }).waitFor();
    const link = await page.locator('.waitlist-panel a.mono').getAttribute('href');
    links.push(link);

    await page.goto(link);
    await page.getByRole('heading', { name: 'You’re on the waitlist' }).waitFor();
    const when = await page.locator('dd').nth(1).innerText();
    assert.ok(when.includes(venueTime) && /E[SD]T/.test(when), `venue time shown regardless of ${timezoneId}: ${when}`);
    const overflow = await page.evaluate(() => document.documentElement.scrollWidth - innerWidth);
    assert.ok(overflow <= 1, `no horizontal overflow at ${width}`);
    await page.screenshot({ path: shot(`waitlist-status-${width}.png`), fullPage: true });
    results.push({ step: 'join+status', width, timezoneId, when, overflow });
    await context.close();
  }

  // 3. Operator: raise the shared limit so both groups (2 + 2) fit, then record one manual notification.
  for (const width of [1440, 390]) {
    const context = await browser.newContext({ viewport: { width, height: 900 } });
    const page = await context.newPage();
    page.on('pageerror', (error) => errors.push(`operator ${width}: ${error.message}`));
    await page.goto(`${origin}/?operator`);
    await page.getByRole('heading', { name: 'Waitlist outbox' }).waitFor();
    if (width === 1440) {
      const form = page.locator('fieldset.capacity-form').filter({ hasText: 'Pumpkin Patch Hayride' }).first();
      await form.getByLabel('Shared limit').fill('29');
      await form.getByRole('button', { name: 'Save capacity' }).click();
      await page.getByText('Capacity updated for Pumpkin Patch Hayride.').waitFor();
      assert.equal(await page.getByText('Places reserved — contact this customer, then mark notified').count(), 2);
      await page.getByRole('button', { name: 'Mark notified (I contacted them)' }).first().click();
      await page.getByText('Manually notified — customer may buy').waitFor();
    }
    const overflow = await page.evaluate(() => document.documentElement.scrollWidth - innerWidth);
    assert.ok(overflow <= 1, `operator no horizontal overflow at ${width}`);
    await page.screenshot({ path: shot(`operator-${width}.png`), fullPage: true });
    results.push({ step: 'operator', width, overflow });
    await context.close();
  }

  // 4. Customer views after notification: first can buy, second still waits for contact.
  const context = await browser.newContext({ viewport: { width: 390, height: 900 }, timezoneId: 'Pacific/Pago_Pago' });
  const page = await context.newPage();
  page.on('pageerror', (error) => errors.push(`offer: ${error.message}`));
  await page.goto(links[0]);
  await page.getByRole('heading', { name: 'Your tickets are ready to buy' }).waitFor();
  assert.ok(await page.getByRole('button', { name: 'Buy these tickets' }).isVisible());
  await page.screenshot({ path: shot('offer-available-390.png'), fullPage: true });
  await page.goto(links[1]);
  await page.getByRole('heading', { name: 'Places are being set aside for you' }).waitFor();
  assert.equal(await page.getByRole('button', { name: 'Buy these tickets' }).count(), 0);
  results.push({ step: 'offer-gate', firstCanBuy: true, secondCanBuy: false });
  await context.close();

  assert.deepEqual(errors, []);
  results.push({ pageErrors: errors.length });
} finally {
  await browser.close();
  await writeFile(new URL('results.json', outputDir), JSON.stringify({ checkedAt: new Date().toISOString(), origin, results, errors }, null, 2));
}
console.log(JSON.stringify(results));
