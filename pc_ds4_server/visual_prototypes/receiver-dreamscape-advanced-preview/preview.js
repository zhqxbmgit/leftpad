(() => {
  const root = document.getElementById('design-root');
  function fit() {
    const scale = Math.min(innerWidth / 1672, innerHeight / 941);
    const x = (innerWidth - 1672 * scale) / 2;
    const y = (innerHeight - 941 * scale) / 2;
    root.style.transform = `translate(${x}px,${y}px) scale(${scale})`;
    root.dataset.scale = scale.toFixed(6);
  }
  addEventListener('resize', fit);
  fit();
})();
