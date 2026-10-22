"""Cinematic version: same voice and word timings, new look."""
import json

words = json.load(open('../voice2/words-stretched.json'))
T = json.load(open('../video3/timeline.json'))
names = [w[0] for w in words]
W = [w[1] for w in words]

# (first, last, blue words, italic words, mode: big kinetic text, bottom caption, or none when the scene shows the words itself)
GROUPS = [
    (0, 8, set(), {'never', 'asked', 'for'}, 'big'), (9, 16, {'control'}, set(), 'cap'), (17, 25, {'step', 'one'}, set(), 'cap'),
    (26, 30, {'step', 'two'}, set(), 'cap'), (31, 37, {'before'}, set(), 'cap'), (38, 43, {'backed', 'up'}, set(), 'cap'),
    (44, 47, {'step', 'three'}, set(), 'cap'), (48, 50, set(), {'mind'}, 'big'), (51, 56, {'one', 'click'}, set(), 'cap'),
    (57, 60, set(), set(), 'none'), (61, 64, set(), set(), 'none'), (65, 68, set(), set(), 'none'), (69, 71, set(), set(), 'none'),
]
PROPER = {'windows', 'gaming', 'privacy', 'tweak', 'pilot'}
PUNCT = {8: '.', 13: ',', 16: '.', 18: '.', 21: ',', 25: '.', 27: '.', 30: '.', 37: '.', 43: '.', 45: '.', 47: '.',
         50: '?', 52: ',', 56: '.', 58: '.', 60: '.', 64: '.', 66: '.', 68: '.', 71: '.'}

groups = []
for i, (a, b, blue, italic, mode) in enumerate(GROUPS):
    end = W[GROUPS[i + 1][0]] - 0.12 if i + 1 < len(GROUPS) else T['end']
    items = []
    for k in range(a, b + 1):
        n = names[k]
        cap = k == a or PUNCT.get(k - 1) in ('.', '?') or n in PROPER
        text = n[:1].upper() + n[1:] if cap else n
        if n == 'pilot' and names[k - 1] == 'tweak':
            items[-1]['t'] = 'TweakPilot'
            items[-1]['p'] = PUNCT.get(k, '')
            continue
        items.append({'t': text, 's': W[k], 'k': n in blue, 'i': n in italic, 'p': PUNCT.get(k, '')})
    groups.append({'start': W[a], 'end': end, 'mode': mode, 'words': items})

html = open('scene.template.html').read()
html = html.replace('/*T*/', json.dumps(T)).replace('/*GROUPS*/', json.dumps(groups))
open('scene.html', 'w').write(html)
