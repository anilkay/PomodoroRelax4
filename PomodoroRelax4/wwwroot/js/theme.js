// Picks the colour theme before the page paints, so there is no flash of the wrong one.
// Loaded synchronously in <head> of both layouts. The choice made with the ☀/☾ button
// (site.js) is kept in localStorage; until then the system setting decides.
(function () {
    var stored = null;
    try { stored = localStorage.getItem('theme'); } catch (e) { /* storage blocked */ }
    var theme = stored === 'light' || stored === 'dark'
        ? stored
        : (window.matchMedia('(prefers-color-scheme: light)').matches ? 'light' : 'dark');
    document.documentElement.dataset.theme = theme;
})();
