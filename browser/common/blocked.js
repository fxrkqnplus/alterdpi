const api = globalThis.browser ?? globalThis.chrome;
const address = document.getElementById('address'), feedback = document.getElementById('feedback'), proceed = document.getElementById('continue');
api.runtime.sendMessage({ action: 'context' }).then(context => {
  address.textContent = context?.url ?? 'Adres bilgisi alınamadı.';
  proceed.disabled = !context?.enabled || !context?.url;
});
document.getElementById('back').addEventListener('click', () => { if (history.length > 1) history.back(); else location.replace('about:blank'); });
proceed.addEventListener('click', async () => {
  proceed.disabled = true; feedback.textContent = 'AlterDPI onay pencerelerini bekleyin…';
  try { const reply = await api.runtime.sendMessage({ action: 'exception' });
    feedback.textContent = reply?.data?.granted ? 'Geçici izin verildi. Bağlantı yeniden açılıyor…' : 'İzin verilmedi. Koruma sürüyor.';
  } catch { feedback.textContent = 'Uygulamaya ulaşılamadı. Koruma ayarını AlterDPI’dan kontrol edin.'; }
  proceed.disabled = false;
});
