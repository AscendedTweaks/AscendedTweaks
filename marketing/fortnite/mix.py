"""Sound design for the motion graphics promo: real clicks, typing ticks, soft slides, chime and a low impact (Kenney, CC0)."""
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

put(sfx('cardSlide1'), .05, -10)
put(thump, T['ignite'] - .05, -15)
type_ticks(T['word'], 14, .045, -24)
put(sfx('cardSlide2'), T['header'] - .05, -15)
for i in range(4):
    put(release, T['chips'] + i * .14, -17)
for k in ['clickGaming', 'clickRow', 'clickUndo']:
    put(click, T[k], -4); put(release, T[k] + .08, -10)
put(sfx('cardSlide3'), T['push'] - .05, -11)
type_ticks(T['type1'], 8, T['step'], -18)
put(sfx('cardSlide5'), T['card'] - .05, -13)
put(click, T['mouseOff'], -13)
put(ok, T['saved'], -9)
type_ticks(T['del'], 8, (T['type2'] - .1 - T['del']) / 9, -20)
type_ticks(T['type2'], 5, T['step'], -18)
put(sfx('cardSlide2'), T['quiet'] - .05, -14)
for i in range(3):
    put(click, T['domino'] + i * .2, -12)
put(sfx('cardSlide6')[:, ::-1].copy(), T['rewind'] - .05, -11)
for i in range(3):
    put(release, T['rewind'] + .1 + i * .12, -13)
put(ok, T['back'], -9)
put(sfx('cardSlide6'), T['dissolve'] - .05, -10)
put(thump, T['outro'] - .02, -4)
type_ticks(T['outro'] + .25, 14, .04, -26)
put(release, T['bio'], -14)
put(load('../voice2/outro-name.wav'), T['outro'] + .2, 0)
mix = np.tanh(mix * 1.05); mix = mix / np.max(np.abs(mix)) * .89
with wave.open('mix.wav','wb') as w:
    w.setnchannels(2); w.setsampwidth(2); w.setframerate(SR); w.writeframes((mix.T*32767).astype('<i2').tobytes())
