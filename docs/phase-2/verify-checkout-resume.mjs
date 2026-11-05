// Rendered check for review-001 correction 1: an accepted waitlist offer whose checkout fails can be
// resumed (after a reload) with the SAME order. Run against a disposable fixture database, with the API
// started using a deliberately non-test Stripe key so the real server refuses checkout with 503 before
// any network call. The second (resumed) checkout response is supplied by Playwright, so no Stripe API
// is ever called; same-session reuse at the provider is covered by the server tests instead.
//   PLAYWRIGHT_MODULE=file:///.../playwright/index.mjs node docs/phase-2/verify-checkout-resume.mjs
import assert from 'node:assert/strict';
import { mkdir, writeFile } from 'node:fs/promises';
import { fileURLToPath } from 'node:url';

const { chromium } = await import(process.env.PLAYWRIGHT_MODULE ?? 'playwright');
const origin = process.env.UI_ORIGIN ?? 'http://127.0.0.1:5173';
const outputDir = new URL('../../ai-sessions/PHASE-2/evidence/ui-retry/', import.meta.url);
await mkdir(outputDir, { recursive: true });
const shot = (name) => fileURLToPath(new URL(name, outputDir));

async function api(path, init) {
  const response = await fetch(`${origin}${path}`, { ...init, headers: { 'Content-Type': 'application/json', ...init?.headers } });
  if (!response.ok) throw new Error(`${path} -> ${response.status} ${await response.text()}`);
  return response.status === 204 ? null : response.json();
}

// Setup: sell out the first hayride departure, queue one group, then free the places so it is offered.
const events = await api('/api/events');
const hayride = events.find((e) => e.name === 'Pumpkin Patch Hayride');
const slot = hayride.timeSlots.find((s) => s.isBookable);
const availability = await api(`/api/events/${hayride.id}/availability?timeSlotId=${slot.id}`);
const typeId = (name) => availability.ticketTypes.find((t) => t.name === name).ticketTypeId;
const filler = await api('/api/orders', {
  method: 'POST',
  headers: { 'Idempotency-Key': `retry-fill-${slot.id}` },
  body: JSON.stringify({ timeSlotId: slot.id, tickets: [{ ticketTypeId: typeId('Adult'), quantity: 20 }, { ticketTypeId: typeId('Child'), quantity: 5 }] }),
});
const joined = await api('/api/waitlist', {
  method: 'POST',
  headers: { 'Idempotency-Key': `retry-join-${slot.id}` },
  body: JSON.stringify({ timeSlotId: slot.id, email: 'retry-check@example.com', tickets: [{ ticketTypeId: typeId('Child'), quantity: 3 }] }),
});
await api(`/api/orders/${filler.id}/confirm`, { method: 'POST', body: JSON.stringify({ cancel: true }) }); // no session existed
const link = `${origin}/?waitlist=${joined.reference}`;

const browser = await chromium.launch({ channel: 'chrome', headless: true });
const results = [];
const errors = [];
try {
  const context = await browser.newContext({ viewport: { width: 390, height: 900 }, timezoneId: 'Asia/Tokyo' });
  const page = await context.newPage();
  page.on('pageerror', (error) => errors.push(error.message));

  // Owner records the manual notification in the operator page.
  await page.goto(`${origin}/?operator`);
  await page.getByRole('button', { name: 'Mark notified (I contacted them)' }).click();
  await page.getByText('Manually notified — customer may buy').waitFor();

  // Attempt 1: real server refuses checkout (503 payment_not_configured); the offer was already accepted.
  await page.goto(link);
  await page.getByRole('heading', { name: 'Your tickets are ready to buy' }).waitFor();
  const firstCheckout = page.waitForResponse((r) => /\/api\/orders\/[^/]+\/checkout$/.test(r.url()));
  await page.getByRole('button', { name: 'Buy these tickets' }).click();
  const failed = await firstCheckout;
  assert.equal(failed.status(), 503);
  const failedOrderId = failed.url().split('/').at(-2);
  await page.getByRole('alert').waitFor();
  results.push({ step: 'first-checkout', status: failed.status(), orderId: failedOrderId });

  // Reload: the server still authorizes resuming the same order; Buy (new acceptance) is gone.
  await page.reload();
  await page.getByRole('heading', { name: 'Checkout started' }).waitFor();
  assert.equal(await page.getByRole('button', { name: 'Buy these tickets' }).count(), 0);
  assert.ok(await page.getByRole('button', { name: 'Resume checkout' }).isVisible());
  const status = await api(`/api/waitlist/${joined.reference}`);
  assert.equal(status.orderId, failedOrderId);
  assert.equal(status.canResumeCheckout, true);
  await page.screenshot({ path: shot('resume-available-390.png'), fullPage: true });

  // Attempt 2 (controlled success): intercept only this response, record which order was requested.
  let resumedOrderId = null;
  await page.route('**/api/orders/*/checkout', async (route) => {
    resumedOrderId = new URL(route.request().url()).pathname.split('/').at(-2);
    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify({ orderId: resumedOrderId, checkoutUrl: `${origin}/?simulated-stripe-checkout=1`, expiresUtc: new Date(Date.now() + 1800000).toISOString() }),
    });
  });
  await Promise.all([page.waitForURL(/simulated-stripe-checkout=1/), page.getByRole('button', { name: 'Resume checkout' }).click()]);
  assert.equal(resumedOrderId, failedOrderId);
  results.push({ step: 'resume', sameOrder: resumedOrderId === failedOrderId });

  // Server state afterwards: still one accepted order holding exactly the 3 offered places.
  const after = await api(`/api/waitlist/${joined.reference}`);
  const seats = await api(`/api/events/${hayride.id}/availability?timeSlotId=${slot.id}`);
  assert.equal(after.orderId, failedOrderId);
  assert.equal(seats.sharedRemaining, 22);
  results.push({ step: 'server-state', status: after.status, sameOrder: after.orderId === failedOrderId, sharedRemaining: seats.sharedRemaining });
  assert.deepEqual(errors, []);
  results.push({ pageErrors: errors.length });
  await context.close();
} finally {
  await browser.close();
  await writeFile(new URL('results.json', outputDir), JSON.stringify({ checkedAt: new Date().toISOString(), origin, results, errors }, null, 2));
}
console.log(JSON.stringify(results));
