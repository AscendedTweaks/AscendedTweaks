// Site configuration: the only place to change owner, repository, version and copyright.
const CONFIG = {
  owner: 'AscendedTweaks',
  repo: 'AscendedTweaks',
  version: '1.1',
  copyright: 'Ascended Tweaks',
  // Set to true once img/app.webp, img/before.webp and img/after.webp exist (real screenshots, English UI).
  screenshots: false,
};

document.documentElement.classList.add('js');

const base = `https://github.com/${CONFIG.owner}/${CONFIG.repo}`;
const links = {
  repo: base,
  latest: `${base}/releases/latest`,
  zip: `${base}/releases/latest/download/AscendedTweaks.zip`,
  license: `${base}/blob/master/LICENSE`,
};

document.querySelectorAll('[data-link]').forEach(a => {
  const href = links[a.dataset.link];
  if (href) a.href = href;
});

const texts = { version: CONFIG.version, copyright: CONFIG.copyright, year: String(new Date().getFullYear()) };
document.querySelectorAll('[data-text]').forEach(el => {
  const value = texts[el.dataset.text];
  if (value) el.textContent = value;
});

// Screenshots: requested only when configured, so a missing file never causes a request.
if (CONFIG.screenshots) {
  document.querySelectorAll('img[data-screenshot]').forEach(img => { img.src = img.dataset.screenshot; });
  const shot = document.querySelector('img.shot');
  shot.addEventListener('load', () => { shot.hidden = false; }, { once: true });

  // Compare slider: shown only when both images load. Keyboard works through the native range input.
  const compare = document.getElementById('compare');
  const box = compare.querySelector('.compare');
  Promise.all([...box.querySelectorAll('img')].map(img => img.decode())).then(() => { compare.hidden = false; }).catch(() => {});
  const range = box.querySelector('input');
  range.addEventListener('input', () => box.style.setProperty('--pos', `${range.value}%`));
}

// Scroll reveal (CSS turns it off for reduced motion).
const reduce = window.matchMedia('(prefers-reduced-motion: reduce)').matches;
const revealItems = document.querySelectorAll('.reveal');
if (reduce || !('IntersectionObserver' in window)) {
  revealItems.forEach(el => el.classList.add('in'));
} else {
  const observer = new IntersectionObserver(entries => {
    entries.forEach(entry => {
      if (entry.isIntersecting) {
        entry.target.classList.add('in');
        observer.unobserve(entry.target);
      }
    });
  }, { rootMargin: '0px 0px -8% 0px' });
  revealItems.forEach(el => observer.observe(el));
}

// Active navigation link.
const navLinks = [...document.querySelectorAll('.nav-links a')];
const sections = navLinks.map(a => document.querySelector(a.getAttribute('href'))).filter(Boolean);
if ('IntersectionObserver' in window) {
  const spy = new IntersectionObserver(entries => {
    entries.forEach(entry => {
      if (!entry.isIntersecting) return;
      navLinks.forEach(a => a.setAttribute('aria-current', String(a.getAttribute('href') === `#${entry.target.id}`)));
    });
  }, { rootMargin: '-45% 0px -50% 0px' });
  sections.forEach(s => spy.observe(s));
}
