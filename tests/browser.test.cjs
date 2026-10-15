const assert=require('node:assert/strict'),fs=require('node:fs'),vm=require('node:vm');
let passed=0,rules=[],sessionRules=[],listener,requests=[],state={protection:true,listVersion:1,exceptions:[]},navigation=[],granted=false,failSnapshot=false,now=Date.now();
class Clock extends Date{static now(){return now;}}
const event=()=>({addListener(fn){this.listener=fn;}}),alarms=new Map();
const port={onMessage:event(),onDisconnect:event(),postMessage(req){requests.push(req);setImmediate(()=>port.onMessage.listener({id:req.id,ok:!(req.operation==='snapshot'&&failSnapshot),state,data:req.operation==='snapshot'?{domains:['bad.example','evil.example'],total:2,version:state.listVersion}:{granted}}));}};
const runtime={id:'pilot',getURL:p=>'chrome-extension://pilot/'+p,connectNative:()=>port,onStartup:event(),onInstalled:event(),onMessage:{addListener:fn=>listener=fn}};
const storage=new Map();const api={runtime,alarms:{create:async(n,v)=>alarms.set(n,v),clear:async n=>alarms.delete(n),onAlarm:event()},storage:{session:{set:async v=>Object.entries(v).forEach(([k,x])=>storage.set(k,x)),get:async k=>({[k]:storage.get(k)}),remove:async k=>storage.delete(k),clear:async()=>storage.clear()},local:{set:async()=>{}}},declarativeNetRequest:{getDynamicRules:async()=>rules,updateDynamicRules:async o=>{rules=rules.filter(r=>!o.removeRuleIds?.includes(r.id));rules.push(...(o.addRules??[]));},getSessionRules:async()=>sessionRules,updateSessionRules:async o=>{sessionRules=sessionRules.filter(r=>!o.removeRuleIds?.includes(r.id));sessionRules.push(...(o.addRules??[]));}},webNavigation:{onBeforeNavigate:event(),onErrorOccurred:event()},tabs:{onRemoved:event(),update:async(id,data)=>navigation.push([id,data])}};
const context={chrome:api,console,URL,Map,Set,Promise,Date:Clock,setTimeout,clearTimeout};vm.createContext(context);vm.runInContext(fs.readFileSync('browser/common/background.js','utf8'),context);
const wait=()=>new Promise(r=>setTimeout(r,40));
(async()=>{
 port.onMessage.listener({eventType:'state',state});await wait();assert.equal(rules.length,1);assert.equal(rules[0].action.type,'redirect');assert.equal(rules[0].condition.resourceTypes[0],'main_frame');passed+=3;
 await api.webNavigation.onBeforeNavigate.listener({frameId:0,tabId:3,url:'https://bad.example/path?x=1'});
 const ask=(message,sender)=>new Promise(resolve=>{const result=listener(message,sender,resolve);if(result===false)resolve(false);});
 const sender={id:'pilot',url:'chrome-extension://pilot/blocked.html',tab:{id:3}};
 assert.equal(await ask({action:'context'},{id:'other',url:'https://evil.example'}),false);passed++;
 const good=await ask({action:'context'},sender);assert.equal(good.url,'https://bad.example/path?x=1');passed++;
 await ask({action:'exception'},sender);assert.equal(navigation.length,0);passed++;
 state={...state,exceptions:[{host:'bad.example',expires:new Date(now+600000).toISOString()}]};port.onMessage.listener({eventType:'state',state});await wait();const allow=sessionRules.find(r=>r.action.type==='allow');assert(allow);assert(new RegExp(allow.condition.regexFilter).test('https://bad.example/path'));assert(!new RegExp(allow.condition.regexFilter).test('https://sub.bad.example/path'));passed+=3;
 granted=true;await ask({action:'exception'},sender);assert.equal(navigation.length,1);assert.equal(navigation[0][1].url,'https://bad.example/path?x=1');passed+=2;
 assert(alarms.has('alterdpi-expiry'));passed++;
 now+=600001;failSnapshot=true;state={...state,listVersion:2,exceptions:[]};port.onMessage.listener({eventType:'state',state});await wait();assert.equal(sessionRules.length,0);assert.equal(rules.length,1);assert(alarms.has('alterdpi-retry'));passed+=3;
 await api.alarms.onAlarm.listener({name:'alterdpi-expiry'});await wait();assert.equal(sessionRules.length,0);passed++;
 failSnapshot=false;await api.alarms.onAlarm.listener({name:'alterdpi-retry'});await wait();assert.equal(rules.length,1);passed++;
 state={...state,protection:false};port.onMessage.listener({eventType:'state',state});await wait();assert.equal(rules.length,0);assert.equal(sessionRules.length,0);assert.equal(storage.size,0);passed+=3;
 await api.webNavigation.onBeforeNavigate.listener({frameId:0,tabId:9,url:'https://normal.example/private'});assert.equal(storage.size,0);passed++;
 console.log(JSON.stringify({passed,failed:0}));process.exit(0);
})().catch(e=>{console.error(e.stack);process.exit(1);});
