import test from 'node:test';
import assert from 'node:assert/strict';
import {apiJson,pageTitle,PageInsteadOfData,reloadButton} from '../src/lib/api-response.mjs';
const imunify='<!DOCTYPE html>\n<html><head><meta charset="utf-8"><title>One moment, please...</title></head><body>Please wait while your request is being verified...</body></html>';
const answer=(body,status,type)=>new Response(body,{status,headers:type?{'Content-Type':type}:{}});
test('JSON answers parse as before, also with another content type and with an error status',async()=>{assert.deepEqual(await apiJson(answer('{"ok":true}',200,'application/json; charset=utf-8')),{ok:true});assert.deepEqual(await apiJson(answer('{"error":"Inicia sesión en Mi cuenta."}',401,'application/json')),{error:'Inicia sesión en Mi cuenta.'});assert.deepEqual(await apiJson(answer(' [1]',200,'text/plain')),[1]);});
test('a web page instead of JSON names the hosting or the network, with status and title',async()=>{
 await assert.rejects(apiJson(answer(imunify,200,'text/html; charset=UTF-8')),e=>e instanceof PageInsteadOfData&&e.httpStatus===200&&e.pageTitle==='One moment, please...'&&e.message==='El servidor de seguridad del hosting o tu red devolvió una página en lugar de datos (HTTP 200, «One moment, please...»)');
 await assert.rejects(apiJson(answer(imunify,403,'application/json')),e=>e instanceof PageInsteadOfData&&e.httpStatus===403);
 await assert.rejects(apiJson(answer('',503,'text/html')),e=>e instanceof PageInsteadOfData&&e.message.endsWith('(HTTP 503)'));
});
test('other answers that are not JSON fail with a plain error that keeps the status',async()=>{await assert.rejects(apiJson(answer('Service Unavailable',503,'text/plain')),e=>!(e instanceof PageInsteadOfData)&&e.httpStatus===503&&/HTTP 503/.test(e.message));await assert.rejects(apiJson(answer('',200,'application/json')),e=>!(e instanceof PageInsteadOfData)&&e.httpStatus===200);});
test('page titles are decoded, put on one line and cut',()=>{assert.equal(pageTitle('<TITLE lang="es">\n Un momento,&nbsp;por&#32;favor&hellip; &#x2014; &laquo;x&raquo;&amp;y\n</TITLE>'),'Un momento, por favor… — «x»&y');assert.equal(pageTitle('<html><body>503</body></html>'),'');assert.equal(pageTitle('<title> </title>'),'');const long=pageTitle('<title>'+'x'.repeat(200)+'</title>');assert.equal(long.length,80);assert.ok(long.endsWith('…'));assert.equal(pageTitle(null),'');});
test('only a page error offers «Recargar página»',()=>{assert.match(reloadButton(new PageInsteadOfData(200,'t')),/data-reload-page>Recargar página</);assert.equal(reloadButton(new Error('x')),'');assert.equal(reloadButton(null),'');});
