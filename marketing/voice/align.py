import json, sys
from pocketsphinx import Decoder
text = ("windows is full of settings you never asked for here's how to take control in three steps "
        "step one pick a goal like gaming or privacy step two check the preview every change is listed before it happens "
        "step three hit apply your settings are backed up first changed your mind one click and everything is back "
        "no ads no tracking free and open source tweak pilot tweak anything link in bio")
d = Decoder(samprate=16000, bestpath=False)
d.set_align_text(text)
d.start_utt()
d.process_raw(open('voice16.raw', 'rb').read(), full_utt=True)
d.end_utt()
d.set_alignment()
d.start_utt()
d.process_raw(open('voice16.raw', 'rb').read(), full_utt=True)
d.end_utt()
words = [(w.name, w.start / 100, (w.start + w.duration) / 100) for w in d.get_alignment()]
json.dump(words, open('words.json', 'w'))
print(' '.join(f"{n}@{s:.2f}" for n, s, e in words))
