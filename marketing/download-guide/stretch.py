"""Adds short breathing pauses between the steps and shifts the word timings."""
import json, subprocess, wave
import numpy as np
SR = 48000
raw = subprocess.run(['ffmpeg','-loglevel','error','-i','voice.mp3','-ac','2','-ar',str(SR),'-f','s16le','-'],capture_output=True,check=True).stdout
x = np.frombuffer(raw,'<i2').reshape(-1,2).T/32768
CUTS = [(1.55, .25), (4.47, .3), (7.28, .3), (12.55, .3), (15.55, .35)]  # (cut point inside a pause, extra seconds)
parts, last = [], 0
for at, extra in CUTS:
    parts += [x[:, int(last*SR):int(at*SR)], np.zeros((2, int(extra*SR)))]; last = at
parts.append(x[:, int(last*SR):])
y = np.concatenate(parts, 1)
with wave.open('voice-guide.wav','wb') as w:
    w.setnchannels(2); w.setsampwidth(2); w.setframerate(SR); w.writeframes((np.clip(y,-1,1).T*32767).astype('<i2').tobytes())
shift = lambda t: t + sum(e for a, e in CUTS if t > a)
words = [(n.split('(')[0], round(shift(s), 3), round(shift(e), 3)) for n, s, e in json.load(open('words.json')) if n != '<sil>']
json.dump(words, open('words-guide.json', 'w'))
print(len(words), y.shape[1] / SR)
