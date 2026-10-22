"""'From chaos to calm': short sound-effects-only video, warm colours, text only on the end card."""
import json
T = {'pop': .15, 'popGap': .22, 'orbIn': 2.3, 'sweep': 3.0, 'gap': .32, 'clean': 6.0, 'flip': 6.7, 'flipGap': .22,
     'shield': 8.4, 'saved': 9.6, 'flash': 10.4, 'word': 10.8, 'tag': 11.5, 'link': 12.2, 'end': 14.5}
TOASTS = [
    ['Recommended for you', 'New apps you might like', '#d9481c', 60, 300, -3],
    ['Get tips and suggestions', 'Learn what is new', '#c2372a', 240, 520, 2.5],
    ['Restart required', 'Updates are waiting', '#a33b1f', 40, 760, -1.5],
    ['Background apps running', '12 apps use resources', '#d9661c', 260, 980, 3],
    ['Your advertising ID', 'Personalised ads are on', '#b5301c', 80, 1200, -2.5],
    ['Finish setting up', 'Remind me in 3 days', '#d9481c', 220, 410, 1.5],
    ['Start menu suggestions', 'Sponsored content', '#c2372a', 50, 650, -3.5],
    ['Activity history', 'Sending your activity', '#a33b1f', 250, 870, 2],
    ['Storage almost full', 'Clean up now', '#d9661c', 70, 1090, -1],
]
html = open('scene.template.html').read().replace('/*T*/', json.dumps(T)).replace('/*TOASTS*/', json.dumps(TOASTS))
open('scene.html', 'w').write(html)
json.dump(T, open('timeline.json', 'w'))
