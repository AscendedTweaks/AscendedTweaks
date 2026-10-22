"""Voice with the new ending 'AscendedTweaks. Tweak anything. Link in bio.' from a second recording."""
import json, subprocess, wave
import numpy as np
SR = 48000
def load(p):
    raw = subprocess.run(['ffmpeg','-loglevel','error','-i',p,'-ac','1','-ar',str(SR),'-f','s16le','-'],capture_output=True,check=True).stdout
    return np.frombuffer(raw,'<i2')/32768
main, ending = load('original.mp3'), load('ending.mp3')
# (source, start, end, pause after); cuts lie inside measured silences.
SEGS = [('m', 0, 2.18, .45), ('m', 2.18, 4.27, .5), ('m', 4.27, 6.96, .5), ('m', 6.96, 10.78, .5),
        ('m', 12.34, 13.82, .5), ('m', 10.78, 12.34, .5), ('m', 13.82, 16.43, .55), ('m', 16.43, 18.76, .65),
        ('e', 0, .84, .75), ('e', .84, 1.575, .2), ('e', 1.575, ending.size / SR, 0)]
words_m = [w for w in json.load(open('words.json')) if w[0] != '<sil>']
words_e = [('ascended', 0, .42), ('tweaks', .42, .82), ('tweak', .82, 1.08), ('anything', 1.08, 1.55), ('link', 1.55, 1.78), ('in', 1.78, 1.88), ('bio', 1.88, 2.27)]
parts, pos, out = [], 0.0, []
for src, a, b, pause in SEGS:
    x = main if src == 'm' else ending
    seg = x[int(a*SR):int(b*SR)].copy()
    if src == 'e':
        seg *= np.sqrt((main[int(16.43*SR):int(18.76*SR)]**2).mean()) / np.sqrt((ending**2).mean())  # match loudness
    f = int(.006*SR); seg[:f] *= np.linspace(0,1,f); seg[-f:] *= np.linspace(1,0,f)
    for n, s, e in (words_m if src == 'm' else words_e):
        if a - .08 <= s < b - .08:
            out.append((n.split('(')[0], round(s - a + pos, 3), round(e - a + pos, 3)))
    parts += [seg, np.zeros(int(pause*SR))]
    pos += (b - a) + pause
y = np.concatenate(parts)
y = y / np.max(np.abs(y)) * .95
with wave.open('voice-ascended.wav','wb') as w:
    w.setnchannels(1); w.setsampwidth(2); w.setframerate(SR); w.writeframes((y*32767).astype('<i2').tobytes())
json.dump(out, open('words-ascended.json','w'))
print(round(y.size/SR,2), len(out), ' '.join(f'{i}:{n}@{s}' for i,(n,s,e) in enumerate(out) if i >= 62))
