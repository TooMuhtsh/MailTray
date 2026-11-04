namespace MailTray;

/// <summary>Script injecté dans les pages Gmail et Chat. Il remonte à l'app les signaux utiles.</summary>
internal static class Scripts
{
    public static string Hook(string kind) => Template.Replace("%KIND%", kind);

    private const string Template = """
(() => {
  if (window.top !== window) return;
  const KIND = '%KIND%';
  const post = (m) => { try { m.kind = KIND; window.chrome.webview.postMessage(m); } catch (e) {} };

  // 1. Badge : la page appelle navigator.setAppBadge(n) comme une app installée.
  try {
    const setB = (n) => { post({ type: 'badge', count: n === undefined ? -1 : (Number(n) || 0) }); return Promise.resolve(); };
    const clrB = () => { post({ type: 'badge', count: 0 }); return Promise.resolve(); };
    Object.defineProperty(Navigator.prototype, 'setAppBadge', { value: setB, configurable: true, writable: true });
    Object.defineProperty(Navigator.prototype, 'clearAppBadge', { value: clrB, configurable: true, writable: true });
  } catch (e) {}

  // 2. Titre de l'onglet, par exemple « Boîte de réception (3) ».
  let lastTitle = null;
  const readTitle = () => {
    if (document.title !== lastTitle) { lastTitle = document.title; post({ type: 'title', title: lastTitle }); }
  };
  setInterval(readTitle, 2000);
  document.addEventListener('DOMContentLoaded', readTitle);

  // 3. Pastille « Chat » du rail de gauche de Gmail (page mail uniquement).
  if (KIND === 'mail') {
    const leafText = (e) => (e.childElementCount === 0 ? (e.textContent || '').trim() : '');
    // Pastille d'un bouton du rail (Mail ou Chat). names : libellés possibles du bouton.
    const railCount = (names) => {
      const re = new RegExp('^(' + names.join('|') + ')\\b', 'i');
      let label = null, count = null;
      // a. Libellé d'accessibilité, par exemple « Chat, 1 message non lu »
      for (const el of document.querySelectorAll('[aria-label]')) {
        const a = el.getAttribute('aria-label') || '';
        if (a.length < 80 && re.test(a)) {
          label = a;
          const m = a.match(/(\d+)/);
          if (m) { count = parseInt(m[1], 10); break; }
        }
      }
      // b. Libellé visible et pastille numérique dans le même bloc
      if (count === null) {
        const labels = [...document.querySelectorAll('div,span')].filter((e) => names.includes(leafText(e)));
        for (const l of labels) {
          const own = leafText(l);
          let p = l.parentElement;
          for (let i = 0; i < 5 && p; i++, p = p.parentElement) {
            const leaves = [...p.querySelectorAll('*')].map(leafText).filter((t) => t !== '');
            if (leaves.some((t) => t !== own && !/^\d+\+?$/.test(t))) break; // on sort du bloc
            const nums = leaves.filter((t) => /^\d+\+?$/.test(t));
            if (nums.length === 1) { count = parseInt(nums[0], 10); label = label || 'pastille visible'; break; }
          }
          if (count !== null) break;
        }
        if (count === null && (label || labels.length)) { count = 0; label = label || 'libellé sans pastille'; }
      }
      return { count, label };
    };
    const last = {};
    const report = (type, names) => {
      const r = railCount(names);
      const key = JSON.stringify(r);
      if (key !== last[type]) { last[type] = key; post({ type, count: r.count === null ? -1 : r.count, label: r.label || '' }); }
    };
    setInterval(() => {
      try { report('railchat', ['Chat']); } catch (e) {}
      try { report('railmail', ['Mail', 'Messagerie']); } catch (e) {}
    }, 1500);
  }

  // 4. Relève du flux des non-lus Gmail (appelée par l'app).
  window.__gtPollMail = async (url) => {
    // Relève seulement depuis une boîte Gmail ouverte (pas pendant la connexion).
    if (location.hostname !== 'mail.google.com' || !location.pathname.startsWith('/mail/')) {
      post({ type: 'atom', status: 0, error: 'pas encore connecté' });
      return;
    }
    try {
      const r = await fetch(url, { credentials: 'include', cache: 'no-store' });
      post({ type: 'atom', status: r.status, xml: await r.text() });
    } catch (e) {
      post({ type: 'atom', status: 0, error: String(e) });
    }
  };
})();
""";
}
