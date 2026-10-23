"""Sound design for the motion graphics promo: real clicks, typing ticks, soft slides, chime and a low impact (Kenney, CC0)."""
import glob, json, subprocess, wave
import numpy as np
SR = 48000
T = {'end': 14.0}
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

def low(x, hz):
    raw = subprocess.run(['ffmpeg','-loglevel','error','-f','f32le','-ac','2','-ar',str(SR),'-i','-','-af',f'lowpass=f={hz},lowpass=f={hz}','-f','f32le','-'],
                         input=x.T.astype('<f4').tobytes(), capture_output=True, check=True).stdout
    y = np.frombuffer(raw,'<f4').reshape(-1,2).T; return y / np.max(np.abs(y))

click, release, ok = sfx('mouseclick1'), sfx('mouserelease1'), sfx('confirmation_002')
keys = [sfx('click4'), sfx('click5'), sfx('click2')]
thump = low(sfx('bookPlace1'), 380)
rng = np.random.default_rng(3)
def type_ticks(start, n, step, db):
    for i in range(n):
        put(keys[i % 3], start + i * step + rng.uniform(-.008, .008), db + rng.uniform(-2, 1))

# Only a soft swell into the end card and the spoken line.
put(sfx('cardSlide6'), 9.85, -14)
put(thump, 10.0, -8)
put(load('../voice2/outro-line.wav'), 10.44, 0)
mix = np.tanh(mix * 1.05); mix = mix / np.max(np.abs(mix)) * .89
with wave.open('mix.wav','wb') as w:
    w.setnchannels(2); w.setsampwidth(2); w.setframerate(SR); w.writeframes((mix.T*32767).astype('<i2').tobytes())
