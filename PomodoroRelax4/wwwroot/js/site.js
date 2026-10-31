// Small page helpers, in a file so the Content-Security-Policy needs no inline script.

// Minutes ahead of UTC (Istanbul: 180); Stats uses it for "today" and the 7-day chart
window.pomodoroTime = { utcOffsetMinutes: () => -new Date().getTimezoneOffset() };

// The timer writes the remaining time into the tab title while it runs
window.pomodoroPage = { setTitle: t => { document.title = t; } };

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
