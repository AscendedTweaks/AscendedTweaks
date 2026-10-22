"""Fast motion graphics promo: liquid light, typing search, morphing cards. 15 s, no voice."""
import json
T = {'ignite': .65, 'word': .8, 'header': 1.5, 'chips': 1.6, 'cursor1': 1.9, 'clickGaming': 2.6, 'push': 3.0,
     'type1': 3.5, 'step': .08, 'cursor2': 5.3, 'clickRow': 6.1, 'card': 6.15, 'mouseOff': 6.9, 'saved': 7.3,
     'del': 8.0, 'type2': 8.45, 'quiet': 8.95, 'domino': 9.3, 'cursor3': 9.9, 'clickUndo': 10.5, 'rewind': 10.6,
     'back': 11.3, 'dissolve': 12.0, 'outro': 12.6, 'tag': 13.2, 'bio': 13.7, 'end': 15.0}
open('scene.html', 'w').write(open('scene.template.html').read().replace('/*T*/', json.dumps(T)))
json.dump(T, open('timeline.json', 'w'))
