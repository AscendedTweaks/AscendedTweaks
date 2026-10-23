"""Download guide in the cinematic look, timed to the voice (words-guide.json)."""
import json
words = json.load(open('words-guide.json'))
N = [w[0] for w in words]; W = [w[1] for w in words]
assert len(N) == 60, N
T = {
    'step1': W[6], 'tap': W[8], 'bio': W[12], 'download': W[15],
    'step2': W[16], 'right': W[18], 'zip': W[21], 'extract': W[24], 'all': W[25],
    'step3': W[26], 'open': W[28], 'warning': W[36], 'more': W[38], 'run': W[41], 'anyway': W[42],
    'thats': W[43], 'pick': W[45], 'check': W[48], 'apply': W[52],
    'logo': W[53], 'anything': W[55], 'link': W[57], 'end': W[59] + 1.6,
}
# (first, last, blue words, mode)
GROUPS = [(0, 5, set(), 'big'), (6, 15, {6, 7, 15}, 'cap'), (16, 25, {16, 17, 24, 25}, 'cap'), (26, 30, {26, 27}, 'cap'),
          (31, 42, {38, 39, 41, 42}, 'cap'), (43, 52, {52}, 'cap'), (53, 59, set(), 'none')]
TEXT = {4: 'AscendedTweaks', 5: None, 29: 'AscendedTweaks', 30: None, 53: 'AscendedTweaks', 54: None, 18: 'Right-click', 19: None,
        15: 'Download', 24: 'Extract', 38: 'More', 41: 'Run'}
PUNCT = {5: '.', 7: '.', 12: ',', 15: '.', 17: '.', 25: '.', 27: '.', 30: '.', 36: ',', 39: ',', 42: '.', 44: '.', 47: ',', 52: '.'}
CAPS = {0, 6, 8, 16, 26, 28, 31, 32, 43, 45}
groups = []
for i, (a, b, blue, mode) in enumerate(GROUPS):
    end = W[GROUPS[i + 1][0]] - .12 if i + 1 < len(GROUPS) else T['end']
    items = []
    for k in range(a, b + 1):
        if k in TEXT and TEXT[k] is None:
            items[-1]['p'] = PUNCT.get(k, ''); continue
        t = TEXT.get(k, N[k]); t = t[:1].upper() + t[1:] if k in CAPS else t
        if N[k] == "here's" : t = "Here's"
        if N[k] == "that's": t = "That's"
        items.append({'t': t, 's': W[k], 'k': k in blue, 'p': PUNCT.get(k, '')})
    groups.append({'start': W[a], 'end': end, 'mode': mode, 'words': items})
html = open('scene.template.html').read().replace('/*T*/', json.dumps(T)).replace('/*GROUPS*/', json.dumps(groups))
open('scene.html', 'w').write(html)
json.dump(T, open('timeline.json', 'w'))
print(json.dumps(T))
for g in groups: print(' '.join(w['t'] + w['p'] for w in g['words']))
