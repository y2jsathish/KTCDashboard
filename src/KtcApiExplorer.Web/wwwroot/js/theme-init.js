// Runs before first paint to avoid a light/dark flash. Kept external because the CSP forbids inline scripts.
(function () {
  try {
    var t = localStorage.getItem('theme');
    if (!t) t = window.matchMedia && window.matchMedia('(prefers-color-scheme: dark)').matches ? 'dark' : 'light';
    document.documentElement.setAttribute('data-bs-theme', t);
  } catch (e) { /* storage blocked: stay on light */ }
})();
