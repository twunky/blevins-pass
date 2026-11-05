// Independent post-correction UI verification. Built client, controlled API fixtures, no Stripe.
import assert from 'node:assert/strict';
import { createServer } from 'node:http';
import { readFile,mkdir,writeFile } from 'node:fs/promises';
import { fileURLToPath,pathToFileURL } from 'node:url';
import path from 'node:path';
const {chromium}=await import(pathToFileURL(process.argv[2]).href);
const root=fileURLToPath(new URL('../../../client/dist/',import.meta.url));
const output=new URL('../../../ai-sessions/PHASE-2/evidence/codex-review/',import.meta.url);
await mkdir(output,{recursive:true});
const server=createServer(async(req,res)=>{
 try{const pathname=new URL(req.url,'http://localhost').pathname;
 const target=path.resolve(root,pathname==='/'?'index.html':`.${pathname}`);
 if(!target.startsWith(root)){res.writeHead(403).end();return;}
 res.setHeader('Content-Type',target.endsWith('.js')?'text/javascript':target.endsWith('.css')?'text/css':'text/html');
 res.end(await readFile(target));}catch{res.writeHead(404).end();}
});
await new Promise(r=>server.listen(0,'127.0.0.1',r));
const origin=`http://127.0.0.1:${server.address().port}`;
const reference='21111111-1111-4111-8111-111111111111',orderId='32222222-2222-4222-8222-222222222222';
let accepted=false,acceptCalls=0;const checkoutOrders=[],errors=[];
const browser=await chromium.launch({channel:'chrome',headless:true});
try{
 const page=await browser.newPage({viewport:{width:390,height:900},timezoneId:'Asia/Tokyo'});
 page.on('pageerror',e=>errors.push(e.message));
 await page.route('**/api/**',async route=>{
  const pathname=new URL(route.request().url()).pathname;let body;
  if(pathname==='/api/events')body=[];
  else if(pathname==='/api/settings/theme')body={activeTheme:'Fall'};
  else if(pathname.endsWith('/accept')){accepted=true;acceptCalls++;body={id:orderId,status:'PendingCheckout'};}
  else if(pathname.endsWith('/checkout')){
   checkoutOrders.push(pathname.split('/')[3]);
   if(checkoutOrders.length===1){await route.fulfill({status:503,json:{error:{code:'payment_provider_unavailable',message:'Temporary checkout failure'}}});return;}
   body={orderId,checkoutUrl:`${origin}/?resumed=1`,expiresUtc:'2026-11-15T15:00:00Z'};
  }
  else if(pathname===`/api/waitlist/${reference}`)body={reference,eventId:2,eventName:'Pumpkin Patch Hayride',timeSlotId:3,
   slotStartUtc:'2026-11-14T15:00:00Z',status:accepted?'CheckoutStarted':'OfferAvailable',positionInLine:null,
   offerExpiresUtc:null,orderId:accepted?orderId:null,canAccept:!accepted,canCancel:!accepted,
   canResumeCheckout:accepted,items:[{description:'Adult',quantity:2}]};
  else{await route.fulfill({status:404,json:{error:{code:'not_found',message:'No fixture'}}});return;}
  await route.fulfill({json:body});
 });
 await page.goto(`${origin}/?waitlist=${reference}`);
 await page.getByRole('button',{name:'Buy these tickets',exact:true}).click();
 await page.getByRole('button',{name:'Resume checkout',exact:true}).waitFor();
 await page.reload();
 await page.getByRole('button',{name:'Resume checkout',exact:true}).waitFor();
 await page.screenshot({path:fileURLToPath(new URL('checkout-resume-after-reload.png',output)),fullPage:true});
 await page.getByRole('button',{name:'Resume checkout',exact:true}).click();
 await page.waitForURL(`${origin}/?resumed=1`);
 assert.equal(acceptCalls,1);assert.deepEqual(checkoutOrders,[orderId,orderId]);assert.deepEqual(errors,[]);
 const result={checkedAtUtc:new Date().toISOString(),passed:true,evidence:'Built client with controlled API fixtures; first checkout503, second mocked local redirect; no real API/provider/database',
  viewport:390,timezone:'Asia/Tokyo',resumeVisibleAfterReload:true,acceptCalls,checkoutRequests:checkoutOrders.length,
  sameOrder:true,successfulLocalRedirect:true,pageErrors:errors.length};
 await writeFile(new URL('checkout-resume.json',output),JSON.stringify(result,null,2));console.log(JSON.stringify(result,null,2));
}finally{await browser.close();await new Promise(r=>server.close(r));}
