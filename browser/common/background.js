const api = globalThis.browser ?? globalThis.chrome;
let port, sequence = 0, current = null, generation = 0, syncing = Promise.resolve(), allowing = Promise.resolve();
let known = new Set(), listVersion = -1;
const pending = new Map(), targets = new Map();
const requests = (operation, extra = {}) => new Promise((resolve, reject) => {
  if (!port) return reject(new Error('OFFLINE'));
  const id = String(++sequence), timer = setTimeout(() => { pending.delete(id); reject(new Error('TIMEOUT')); }, operation === 'exception' ? 120000 : 15000);
  pending.set(id, { resolve, reject, timer }); port.postMessage({ operation, id, ...extra });
});
const safeUrl = value => { try { const u = new URL(value); return ['http:', 'https:'].includes(u.protocol) && !u.username && !u.password && value.length <= 4096 ? u.href : null; } catch { return null; } };
const listed = host => { for (let h = host; h; h = h.includes('.') ? h.slice(h.indexOf('.') + 1) : '') if (known.has(h)) return true; return false; };
async function removeAllows() {
  const old = await api.declarativeNetRequest.getSessionRules();
  if (old.length) await api.declarativeNetRequest.updateSessionRules({ removeRuleIds: old.map(r => r.id) });
}
async function clearRules() {
  await removeAllows();
  const old = await api.declarativeNetRequest.getDynamicRules();
  if (old.length) await api.declarativeNetRequest.updateDynamicRules({ removeRuleIds: old.map(r => r.id) });
  known.clear(); listVersion = -1; targets.clear(); await api.storage.session.clear();
}
async function permissions(state) {
  // Revoke first, without depending on a list download. Session permissions never survive browser restart.
  await removeAllows(); await api.alarms.clear('alterdpi-expiry');
  const entries = state?.protection ? (state.exceptions ?? []).filter(e => Date.parse(e.expires) > Date.now()) : [];
  if (!entries.length) return;
  await api.alarms.create('alterdpi-expiry', { when: Math.min(...entries.map(e => Date.parse(e.expires))) });
  const addRules = entries.slice(0, 128).map((entry, index) => ({
    id: 100001 + index, priority: 100, action: { type: 'allow' },
    condition: { regexFilter: '^https?://' + entry.host.replace(/[.*+?^${}()|[\]\\]/g, '\\$&') + '(?::(?:80|443))?(?:/|$)', resourceTypes: ['main_frame'] }
  }));
  await api.declarativeNetRequest.updateSessionRules({ addRules });
}
async function sync(state, stamp) {
  if (!state?.protection) { await clearRules(); return; }
  if (listVersion === state.listVersion) return;
  const list = [];
  for (let offset = 0; ; offset += 400) {
    if (stamp !== generation) return;
    const reply = await requests('snapshot', { offset });
    if (!reply.ok || !reply.state?.protection || reply.data.version !== state.listVersion) throw new Error('CHANGED');
    list.push(...reply.data.domains);
    if (list.length >= reply.data.total || !reply.data.domains.length) break;
    if (list.length > 1000000) throw new Error('LIMIT');
  }
  if (stamp !== generation || !current?.protection) return;
  const addRules = [];
  for (let i = 0; i < list.length; i += 500) addRules.push({ id: addRules.length + 1, priority: 1,
    action: { type: 'redirect', redirect: { extensionPath: '/blocked.html' } },
    condition: { requestDomains: list.slice(i, i + 500), resourceTypes: ['main_frame'] } });
  if (addRules.length > 4900) throw new Error('LIMIT');
  const old = await api.declarativeNetRequest.getDynamicRules();
  await api.declarativeNetRequest.updateDynamicRules({ removeRuleIds: old.map(r => r.id), addRules });
  known = new Set(list); listVersion = state.listVersion;
  await api.storage.local.set({ coverage: 'ready' }); await api.alarms.clear('alterdpi-retry');
}
function schedule() {
  const stamp = ++generation;
  allowing=allowing.catch(()=>{}).then(()=>permissions(current)).catch(async()=>{
    await removeAllows().catch(()=>{});await api.alarms.create('alterdpi-retry',{when:Date.now()+30000});
  });
  syncing = syncing.catch(() => {}).then(() => sync(current, stamp)).catch(async () => {
    // No stale permission survives even when the block-list fetch fails.
    await removeAllows().catch(() => {}); await api.storage.local.set({ coverage: 'limited' });
    await api.alarms.create('alterdpi-retry', { when: Date.now() + 30000 });
  });
}
function connect() {
  if (port) return;
  try { port = api.runtime.connectNative('com.alterdpi.bridge'); } catch { setTimeout(connect, 10000); return; }
  port.onMessage.addListener(message => {
    if (message.id) { const p = pending.get(message.id); if (p) { clearTimeout(p.timer); pending.delete(message.id); p.resolve(message); } return; }
    if (message.eventType === 'offline') { current = null; schedule(); return; }
    if (message.eventType === 'state') {
      const before = current; current = message.state;
      if (!before || before.protection !== current.protection || before.listVersion !== current.listVersion || JSON.stringify(before.exceptions) !== JSON.stringify(current.exceptions)) schedule();
    }
  });
  port.onDisconnect.addListener(() => { port = null; current = null;
    for (const p of pending.values()) { clearTimeout(p.timer); p.reject(new Error('OFFLINE')); }
    pending.clear(); schedule(); setTimeout(connect, 10000);
  });
}
async function remember(id, url) { targets.set(id, url); await api.storage.session.set({ ['tab-' + id]: url }); }
api.webNavigation.onBeforeNavigate.addListener(async event => {
  if (event.frameId !== 0 || !current?.protection) return;
  const url = safeUrl(event.url); if (url && listed(new URL(url).hostname)) await remember(event.tabId, url);
});
api.webNavigation.onErrorOccurred.addListener(async event => {
  if (event.frameId !== 0 || !current?.protection) return;
  const url = safeUrl(event.url); if (!url) return;
  try { const reply = await requests('classify', { host: new URL(url).hostname });
    if (reply.data?.blocked) { await remember(event.tabId, url); await api.tabs.update(event.tabId, { url: api.runtime.getURL('blocked.html') }); }
  } catch { /* Ordinary network failures are not labelled security blocks. */ }
});
api.tabs.onRemoved.addListener(async id => { targets.delete(id); await api.storage.session.remove('tab-' + id); });
api.runtime.onMessage.addListener((message, sender, respond) => {
  if (sender.id !== api.runtime.id || !sender.url?.startsWith(api.runtime.getURL(''))) return false;
  (async () => {
    const id = sender.tab?.id, stored = id == null ? {} : await api.storage.session.get('tab-' + id);
    const url = safeUrl(targets.get(id) ?? stored['tab-' + id]);
    if (message.action === 'context') return { url, enabled: !!current?.protection };
    if (message.action !== 'exception' || id == null || !url || !current?.protection) return { ok: false };
    const reply = await requests('exception', { host: new URL(url).hostname });
    if (reply.ok && reply.data?.granted) {
      const refreshed = await requests('state'); if (!refreshed.ok || !refreshed.state?.protection) return { ok: false };
      current = refreshed.state; schedule(); await syncing;await allowing;
      if (!current?.exceptions?.some(e => e.host === new URL(url).hostname && Date.parse(e.expires) > Date.now())) return { ok: false };
      await api.tabs.update(id, { url });
    }
    return reply;
  })().then(respond, () => respond({ ok: false })); return true;
});
api.alarms.onAlarm.addListener(async alarm => {
  if (alarm.name === 'alterdpi-expiry') { await removeAllows().catch(() => {}); schedule(); }
  if (alarm.name === 'alterdpi-retry') schedule();
});
api.runtime.onStartup.addListener(connect); api.runtime.onInstalled.addListener(connect); connect();
