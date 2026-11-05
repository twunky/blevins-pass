// Independent browser probe of the built client. API replies are controlled fixtures, never Stripe.
import { createServer } from 'node:http';
import { readFile, mkdir, writeFile } from 'node:fs/promises';
import { fileURLToPath, pathToFileURL } from 'node:url';
import path from 'node:path';
const { chromium } = await import(pathToFileURL(process.argv[2]).href);
const root = fileURLToPath(new URL('../../../client/dist/',import.meta.url));
const output = new URL('../../../ai-sessions/PHASE-2/evidence/codex-review/',import.meta.url);
await mkdir(output,{recursive:true});
const server = createServer(async(req,res)=>{
  try {
    const requestPath = new URL(req.url,'http://localhost').pathname;
    const target = path.resolve(root, requestPath === '/' ? 'index.html' : `.${requestPath}`);
    if (!target.startsWith(root)) {res.writeHead(403).end();return;}
    const type = target.endsWith('.js')?'text/javascript':target.endsWith('.css')?'text/css':target.endsWith('.svg')?'image/svg+xml':'text/html';
    res.setHeader('Content-Type',type);res.end(await readFile(target));
  } catch {res.writeHead(404).end();}
});
await new Promise(r=>server.listen(0,'127.0.0.1',r));
const origin=`http://127.0.0.1:${server.address().port}`;
let accepted=false,checkoutCalls=0;
const reference='21111111-1111-4111-8111-111111111111';
const orderId='32222222-2222-4222-8222-222222222222';
const browser=await chromium.launch({channel:'chrome',headless:true});
try {
 const page=await browser.newPage({viewport:{width:390,height:900}});
 await page.route('**/api/**',async route=>{
  const pathname=new URL(route.request().url()).pathname;
  let body;
  if(pathname==='/api/events')body=[];
  else if(pathname==='/api/settings/theme')body={activeTheme:'Fall'};
  else if(pathname.endsWith('/accept')){accepted=true;body={id:orderId,status:'PendingCheckout'};}
  else if(pathname.endsWith('/checkout')){checkoutCalls++;await route.fulfill({status:503,json:{error:{code:'payment_provider_unavailable',message:'Checkout temporarily unavailable; please retry.'}}});return;}
  else if(pathname===`/api/waitlist/${reference}`) body={reference,eventId:2,eventName:'Pumpkin Patch Hayride',timeSlotId:3,slotStartUtc:'2026-11-14T15:00:00Z',status:accepted?'CheckoutStarted':'OfferAvailable',positionInLine:null,offerExpiresUtc:null,orderId:accepted?orderId:null,canAccept:!accepted,canCancel:!accepted,items:[{description:'Adult',quantity:2}]};
  else {await route.fulfill({status:404,json:{error:{code:'not_found',message:'Fixture has no such endpoint'}}});return;}
  await route.fulfill({json:body});
 });
 await page.goto(`${origin}/?waitlist=${reference}`);
 await page.getByRole('button',{name:'Buy these tickets',exact:true}).click();
 await page.getByRole('heading',{name:'Checkout started',exact:true}).waitFor();
 const result={checkedAt:new Date().toISOString(),evidence:'Built production client with controlled API fixtures; no real provider or application database',checkoutCalls,remainingPurchaseButtons:await page.getByRole('button',{name:/buy|retry|resume|finish|pay/i}).count(),statusLink:await page.getByRole('link',{name:'Check payment status'}).count(),observation:'After accept succeeds and checkout returns 503, purchase/retry button is absent. Only payment-status link remains.'};
 await page.screenshot({path:fileURLToPath(new URL('checkout-recovery.png',output)),fullPage:true});
 await writeFile(new URL('checkout-recovery.json',output),JSON.stringify(result,null,2));
 console.log(JSON.stringify(result,null,2));
} finally {await browser.close();await new Promise(r=>server.close(r));}
