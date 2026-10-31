// Small page helpers, in a file so the Content-Security-Policy needs no inline script.

// Minutes ahead of UTC (Istanbul: 180); Stats uses it for "today" and the 7-day chart
window.pomodoroTime = { utcOffsetMinutes: () => -new Date().getTimezoneOffset() };

// The timer writes the remaining time into the tab title while it runs
window.pomodoroPage = { setTitle: t => { document.title = t; } };

// <form data-confirm="..."> asks before submitting (inline onsubmit handlers are blocked by the CSP)
document.addEventListener('submit', e => {
    const message = e.target.dataset && e.target.dataset.confirm;
    if (message && !window.confirm(message)) e.preventDefault();
});
