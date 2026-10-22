"""Rebuild the voice with "Your settings are backed up first." before "Step three. Hit apply." and longer pauses."""
import json, subprocess, wave
import numpy as np
SR = 48000
raw = subprocess.run(['ffmpeg','-loglevel','error','-i','original.mp3','-ac','1','-ar',str(SR),'-f','s16le','-'],capture_output=True,check=True).stdout
x = np.frombuffer(raw,'<i2')/32768
end = x.size / SR
# (start, end, pause after) in the original recording; cuts lie inside measured silences.
SEGS = [(0, 2.18, .45), (2.18, 4.27, .5), (4.27, 6.96, .5), (6.96, 10.78, .5),
        (12.34, 13.82, .5), (10.78, 12.34, .5),
        (13.82, 16.43, .55), (16.43, 18.76, .65), (18.76, 19.37, .8), (19.37, 20.17, .15), (20.17, end, 0)]
words = [w for w in json.load(open('words.json')) if w[0] != '<sil>']
parts, pos, out = [], 0.0, []
for a, b, pause in SEGS:
    seg = x[int(a*SR):int(b*SR)].copy()
    f = int(.006*SR); seg[:f] *= np.linspace(0,1,f); seg[-f:] *= np.linspace(1,0,f)
    for n, s, e in words:
        if a - .08 <= s < b - .08:
            out.append((n.split('(')[0], round(s - a + pos, 3), round(e - a + pos, 3)))
    parts += [seg, np.zeros(int(pause*SR))]
    pos += (b - a) + pause
y = np.concatenate(parts)
with wave.open('voice-reordered.wav','wb') as w:
    w.setnchannels(1); w.setsampwidth(2); w.setframerate(SR); w.writeframes((y*32767).astype('<i2').tobytes())
json.dump(out, open('words-stretched.json','w'))
print(round(y.size/SR,2), len(out)); print(' '.join(f'{i}:{n}@{s}' for i,(n,s,e) in enumerate(out) if 36 <= i <= 50))
