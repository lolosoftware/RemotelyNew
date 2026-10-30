import test from 'node:test';
import assert from 'node:assert/strict';
import { RemoteKeyboard } from '../../Server/wwwroot/src/RemoteKeyboard.js';
const event = (key, code, extra = {}) => ({key, code, ctrlKey:false, altKey:false, metaKey:false, repeat:false, isComposing:false, getModifierState:()=>false, ...extra});
function setup() {
    const calls=[];
    const keyboard=new RemoteKeyboard({down:async(...v)=>calls.push(['down',...v]),up:async(...v)=>calls.push(['up',...v]),text:async v=>calls.push(['text',v]),reset:async()=>calls.push(['reset'])});
    return {keyboard,calls};
}
test('local text preserves accented characters',async()=>{const {keyboard:k,calls}=setup();k.keyDown(event('é','Digit2'));k.keyUp(event('é','Digit2'));await k.flush();assert.deepEqual(calls,[['text','é']]);});
test('physical key releases use original key when Shift changes',async()=>{const {keyboard:k,calls}=setup();k.mode='physical';k.keyDown(event('Q','KeyA'));k.keyUp(event('q','KeyA'));await k.flush();assert.deepEqual(calls,[['down','Q','KeyA'],['up','Q','KeyA']]);});
test('focus loss releases all pressed keys in order',async()=>{const {keyboard:k,calls}=setup();k.keyDown(event('Control','ControlLeft',{ctrlKey:true}));k.keyDown(event('c','KeyC',{ctrlKey:true}));k.releaseAll();await k.flush();assert.deepEqual(calls.map(v=>v[0]),['down','down','up','up','reset']);});
test('composition is sent only when committed',async()=>{const {keyboard:k,calls}=setup();assert.equal(k.keyDown(event('Process','KeyA',{isComposing:true})),false);k.text('日本語');await k.flush();assert.deepEqual(calls,[['text','日本語']]);});
test('AltGr text clears shortcut modifiers before typing',async()=>{const {keyboard:k,calls}=setup();k.keyDown(event('@','Digit0',{ctrlKey:true,altKey:true,getModifierState:n=>n==='AltGraph'}));await k.flush();assert.deepEqual(calls,[['reset'],['text','@']]);});
test('key-up with no matching press sends nothing',async()=>{const {keyboard:k,calls}=setup();assert.equal(k.keyUp(event('Shift','ShiftLeft')),false);await k.flush();assert.deepEqual(calls,[]);});
