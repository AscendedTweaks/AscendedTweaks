"""Builds scene.html from the word timings of the voice (words-stretched.json)."""
import json

words = json.load(open('../voice2/words-ascended.json'))
W = [w[1] for w in words]  # start time per word index
names = [w[0] for w in words]


def at(word, n=1):
    """Start time of the n-th occurrence of a word."""
    seen = 0
    for name, start, _ in words:
        if name == word:
            seen += 1
            if seen == n:
                return start
    raise KeyError(word)


T = {
    'hook': 0.0, 'take': at("here's"), 'three': at('three'), 'steps': at('steps'),
    'step1': at('step', 1), 'gaming': at('gaming'), 'privacy': at('privacy'),
    'step2': at('step', 2), 'check': at('check'), 'every': at('every'), 'change': at('change'), 'listed': at('listed'), 'before': at('before'),
    'step3': at('step', 3), 'apply': at('apply'), 'backup': at('your', 1), 'first': at('first'),
    'mind': at('changed'), 'click': at('click'), 'everything': at('everything'), 'back': at('back'),
    'ads': at('ads'), 'tracking': at('tracking'), 'free': at('free'),
    'logo': at('ascended'), 'anything': at('anything'), 'link': at('link'), 'end': at('bio') + 2.0,
}

# Caption groups: (first word index, last word index, words to colour blue).
GROUPS = [
    (0, 8, {'never'}), (9, 16, {'control'}), (17, 25, {'step', 'one'}), (26, 30, {'step', 'two'}),
    (31, 37, {'before'}), (38, 43, {'backed', 'up'}), (44, 47, {'step', 'three'}), (48, 50, set()),
    (51, 56, {'one', 'click'}), (57, 60, set()), (61, 64, {'free'}), (65, 68, {'ascended', 'tweaks'}), (69, 71, set()),
]
PROPER = {'windows', 'gaming', 'privacy', 'tweak', 'ascended', 'tweaks'}
PUNCT = {8: '.', 13: ',', 16: '.', 18: '.', 21: ',', 25: '.', 27: '.', 30: '.', 37: '.', 43: '.', 45: '.', 47: '.',
         50: '?', 52: ',', 56: '.', 58: '.', 60: '.', 64: '.', 66: '.', 68: '.', 71: '.'}

captions = []
for i, (a, b, keys) in enumerate(GROUPS):
    end = W[GROUPS[i + 1][0]] - 0.12 if i + 1 < len(GROUPS) else T['end']
    items = []
    for k in range(a, b + 1):
        n = names[k]
        cap = k == a or PUNCT.get(k - 1) in ('.', '?') or n in PROPER
        text = n[:1].upper() + n[1:] if cap else n
        if n == 'tweaks' and k - 1 >= a and names[k - 1] == 'ascended':
            items[-1]['t'] = 'AscendedTweaks'
            items[-1]['p'] = PUNCT.get(k, '')
            continue
        items.append({'t': text, 's': W[k], 'k': n in keys and k != 67, 'p': PUNCT.get(k, '')})
    captions.append({'start': W[a], 'end': end, 'words': items})

html = open('scene.template.html').read()
html = html.replace('/*T*/', json.dumps(T)).replace('/*CAPTIONS*/', json.dumps(captions))
open('scene.html', 'w').write(html)
json.dump(T, open('timeline.json', 'w'))
print(json.dumps(T))
