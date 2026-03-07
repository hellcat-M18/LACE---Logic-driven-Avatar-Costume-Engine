document.addEventListener('DOMContentLoaded', () => {
  const urlText = document.getElementById('urlText');
  if (!urlText) return;

  const repoUrl = urlText.textContent.trim();
  const vccUrl = `vcc://vpm/addRepo?url=${encodeURIComponent(repoUrl)}`;

  // Set proper encoded href on all VCC links
  document.querySelectorAll('#addToVcc, .add-vcc').forEach(el => {
    el.href = vccUrl;
  });

  // Copy URL button
  const copyBtn = document.getElementById('copyUrl');
  const toast = document.getElementById('toast');

  if (copyBtn) {
    copyBtn.addEventListener('click', async () => {
      try {
        await navigator.clipboard.writeText(repoUrl);
        showToast('Copied!');
      } catch {
        showToast('Copy failed');
      }
    });
  }

  function showToast(msg) {
    if (!toast) return;
    toast.textContent = msg;
    toast.classList.add('show');
    setTimeout(() => toast.classList.remove('show'), 1500);
  }
});
