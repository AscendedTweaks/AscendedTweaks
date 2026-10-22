const { chromium } = require(process.env.PW);
const { spawn } = require('child_process');
(async () => {
  const fps = 30, duration = 23.0, frames = Math.round(fps * duration);
  const ff = spawn('ffmpeg', ['-y', '-loglevel', 'error', '-f', 'image2pipe', '-framerate', String(fps), '-c:v', 'mjpeg', '-i', '-',
    '-c:v', 'libx264', '-preset', 'medium', '-crf', '18', '-pix_fmt', 'yuv420p', 'video-only.mp4'], { stdio: ['pipe', 'inherit', 'inherit'] });
  const b = await chromium.launch(); const p = await b.newPage({ viewport: { width: 1080, height: 1920 } });
  await p.goto('file://' + process.cwd() + '/scene.html'); await p.waitForTimeout(300);
  for (let i = 0; i < frames; i++) {
    await p.evaluate(t => setTime(t), i / fps);
    const buf = await p.screenshot({ type: 'jpeg', quality: 95 });
    if (!ff.stdin.write(buf)) await new Promise(r => ff.stdin.once('drain', r));
  }
  ff.stdin.end(); await new Promise(r => ff.on('close', r)); await b.close(); console.log('frames', frames);
})();
