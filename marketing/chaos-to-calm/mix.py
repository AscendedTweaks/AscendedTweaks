"""Sound effects only: the liked click and check sounds plus soft slides for the camera moves (Kenney, CC0)."""
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

click, release, ok = sfx('mouseclick1'), sfx('mouserelease1'), sfx('confirmation_002')
for i in range(9):
    put(click, T['pop'] + i * T['popGap'], -14 + i * .8)
put(sfx('cardSlide1'), T['orbIn'] - .1, -8)
for i in range(9):
    put(release, T['sweep'] + i * T['gap'], -9)
put(sfx('cardSlide2'), T['clean'] - .1, -12)
for i in range(5):
    put(click, T['flip'] + i * T['flipGap'], -6); put(release, T['flip'] + i * T['flipGap'] + .07, -12)
put(sfx('cardSlide5'), T['shield'] - .1, -13)
put(ok, T['saved'], -8)
put(sfx('cardSlide6'), T['flash'] - .3, -8)
put(ok, T['tag'], -12)
mix = np.tanh(mix * 1.05); mix = mix / np.max(np.abs(mix)) * .89
with wave.open('mix.wav','wb') as w:
    w.setnchannels(2); w.setsampwidth(2); w.setframerate(SR); w.writeframes((mix.T*32767).astype('<i2').tobytes())
