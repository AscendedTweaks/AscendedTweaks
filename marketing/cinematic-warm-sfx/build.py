"""Sound-effects-only cinematic version in warm colours; text only on the end card."""
import json
T = {
    'take': 2.4, 'step1': 4.4, 'gaming': 6.2, 'privacy': 6.8,
    'step2': 7.6, 'check': 7.9, 'every': 8.6, 'change': 8.9, 'listed': 9.2, 'before': 9.5,
    'backup': 10.4, 'first': 11.3, 'step3': 12.4, 'apply': 13.3,
    'mind': 14.4, 'click': 14.9, 'everything': 15.4, 'back': 16.1,
    'ads': 17.4, 'tracking': 17.6, 'free': 17.6, 'logo': 18.6, 'anything': 19.8, 'link': 20.8, 'end': 23.0,
}
html = open('scene.template.html').read()
html = html.replace('/*T*/', json.dumps(T)).replace('/*GROUPS*/', '[]')
open('scene.html', 'w').write(html)
json.dump(T, open('timeline.json', 'w'))
