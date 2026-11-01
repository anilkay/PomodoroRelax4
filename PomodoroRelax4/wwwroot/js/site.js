// Small page helpers, in a file so the Content-Security-Policy needs no inline script.

// Small per-browser values in cookies, so the server can use them when it prerenders a page
// (Services/ClientPreferences.cs reads them)
window.pomodoroPrefs = {
    set: (name, value) => {
        const secure = location.protocol === 'https:' ? '; secure' : '';
        document.cookie = `${name}=${encodeURIComponent(value)}; path=/; max-age=31536000; samesite=lax${secure}`;
    }
};

// Minutes ahead of UTC (Istanbul: 180); Stats and the task due dates use it for "today"
window.pomodoroTime = { utcOffsetMinutes: () => -new Date().getTimezoneOffset() };
pomodoroPrefs.set('tz', String(pomodoroTime.utcOffsetMinutes()));

// The timer writes the remaining time into the tab title while it runs
window.pomodoroPage = { setTitle: t => { document.title = t; } };

// [data-theme-toggle] buttons switch light/dark and remember it (theme.js applies it on load)
document.addEventListener('click', e => {
    if (!(e.target instanceof Element) || !e.target.closest('[data-theme-toggle]')) return;
    const next = document.documentElement.dataset.theme === 'light' ? 'dark' : 'light';
    document.documentElement.dataset.theme = next;
    try { localStorage.setItem('theme', next); } catch { /* storage blocked: lasts this page */ }
});

// Without a stored choice, follow the system setting when it changes
window.matchMedia('(prefers-color-scheme: light)').addEventListener('change', e => {
    let stored = null;
    try { stored = localStorage.getItem('theme'); } catch { }
    if (!stored) document.documentElement.dataset.theme = e.matches ? 'light' : 'dark';
});

// <input type="file" data-max-bytes="..."> refuses a too-large file before upload; past the
// server's request limit the post would fail with a bare 400 instead of a readable message
document.addEventListener('change', e => {
    const input = e.target;
    if (!(input instanceof HTMLInputElement) || input.type !== 'file' || !input.dataset.maxBytes) return;
    const tooBig = [...input.files].some(f => f.size > Number(input.dataset.maxBytes));
    input.setCustomValidity(tooBig ? input.dataset.maxBytesMessage || 'The file is too large.' : '');
    input.reportValidity();
});

// <form data-confirm="..."> asks before submitting (inline onsubmit handlers are blocked by the CSP)
document.addEventListener('submit', e => {
    const message = e.target.dataset && e.target.dataset.confirm;
    if (message && !window.confirm(message)) e.preventDefault();
});
