"""Voice, clicks, check chimes and soft slides (Kenney, CC0)."""
import glob, json, subprocess, wave
import numpy as np
SR = 48000
T = json.load(open('timeline.json'))
DL = '../dl'

def load(path):
    raw = subprocess.run(['ffmpeg','-loglevel','error','-i',path,'-ac','2','-ar',str(SR),'-f','s16le','-'],capture_output=True,check=True).stdout
    return np.frombuffer(raw,'<i2').reshape(-1,2).T/32768

def sfx(name):
    hits = (glob.glob(f'{DL}/kenney-interface-sounds/**/{name}.wav', recursive=True) + glob.glob(f'{DL}/kenney-ui-audio/**/{name}.wav', recursive=True)
            + glob.glob(f'{DL}/kenney-audio-assets/**/{name}.ogg', recursive=True))
    x = load(hits[0]); return x / np.max(np.abs(x))

mix = np.zeros((2, int(SR * T['end'])))
def put(x, at, db):
    a = int(at * SR); e = min(mix.shape[1], a + x.shape[1]); mix[:, a:e] += x[:, :e - a] * 10 ** (db / 20)

voice = load('voice-guide.wav'); put(voice / np.max(np.abs(voice)), 0, 0)
click, release, ok = sfx('mouseclick1'), sfx('mouserelease1'), sfx('confirmation_002')
for c in [T['download'], T['right'], T['all'], T['open'], T['open'] + .14, T['more'], T['run'], T['pick'], T['apply']]:
    put(click, c, -6); put(release, c + .08, -12)
for k, d in [('download', 1.1), ('all', .25), ('run', .3), ('apply', .3)]:
    put(ok, T[k] + d, -11)
for s, k in [('cardSlide2', 'step1'), ('cardSlide3', 'step2'), ('cardSlide5', 'step3'), ('cardSlide2', 'thats'), ('cardSlide6', 'logo')]:
    put(sfx(s), T[k] - .35, -17)
mix = np.tanh(mix * 1.05); mix = mix / np.max(np.abs(mix)) * .89
with wave.open('mix.wav','wb') as w:
    w.setnchannels(2); w.setsampwidth(2); w.setframerate(SR); w.writeframes((mix.T*32767).astype('<i2').tobytes())
