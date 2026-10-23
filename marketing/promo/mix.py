"""Sound design for the motion graphics promo: real clicks, typing ticks, soft slides, chime and a low impact (Kenney, CC0)."""
import glob, json, subprocess, wave
import numpy as np
SR = 48000
D = json.load(open('timeline.json')); T = D['T']; LINES = D['lines']
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

put(sfx('cardSlide1'), .05, -12)
for i, l in enumerate(LINES):
    # Each word lands with a low hit; the italic follow-ups get a click instead.
    if '<i>' in l['html'] and not l['html'].startswith('Your'):
        put(click, l['t'], -7); put(release, l['t'] + .08, -12)
    else:
        put(thump, l['t'] - .02, -9 if i else -12)
for i, s in enumerate(['cardSlide2', 'cardSlide3', 'cardSlide5']):
    put(sfx(s), LINES[i * 4]['t'] - .1, -16)
put(sfx('cardSlide6'), T['collapse'] - .2, -11)
put(thump, T['outro'] - .02, -3)
type_ticks(T['outro'] + .25, 14, .04, -30)
put(load('../voice2/outro-line.wav'), T['voice'], 0)
put(release, T['bio'], -14)
mix = np.tanh(mix * 1.05); mix = mix / np.max(np.abs(mix)) * .89
with wave.open('mix.wav','wb') as w:
    w.setnchannels(2); w.setsampwidth(2); w.setframerate(SR); w.writeframes((mix.T*32767).astype('<i2').tobytes())
