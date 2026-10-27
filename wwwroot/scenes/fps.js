// Shared frame-rate cap for scenes. The app opens a scene as scene.html?fps=30 to halve the work;
// scenes keep using requestAnimationFrame and time-based animation, so nothing else changes.
(() => {
  const fps = Number(new URLSearchParams(location.search).get('fps')) || 0;
  if (!fps || fps >= 60) return;

  const raw = window.requestAnimationFrame.bind(window);
  const interval = 1000 / fps;
  let lastShown = 0;
  window.requestAnimationFrame = callback => raw(function tick(time) {
    if (time - lastShown >= interval - 2) { // small tolerance so 60 Hz displays hit an even 30
      lastShown = time;
      callback(time);
    } else {
      raw(tick);
    }
  });
})();
