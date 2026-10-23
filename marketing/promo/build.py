"""Pure promo: kinetic typography on liquid light, no app UI. About 11 s, no voice."""
import json
B = .55  # one beat
LINES = []
def line(html, t, out, y=0, size=150):
    LINES.append({'html': html, 't': round(t, 3), 'out': round(out, 3), 'y': y, 'size': size})
line('Your PC.', .45, .45 + 2 * B, 60, 170)
line('Your <i>rules.</i>', .45 + B, .45 + 2 * B, 260, 170)
s = .45 + 2.2 * B
line('Every tweak', s, s + 4 * B, 0, 130)
line('<i>Previewed.</i>', s + B, s + 2 * B, 190, 150)
line('<i>Backed up.</i>', s + 2 * B, s + 3 * B, 190, 150)
line('<i>Undoable.</i>', s + 3 * B, s + 4 * B, 190, 150)
s += 4.2 * B
line('No ads.', s, s + 2 * B, 60, 160)
line('No tracking.', s + B, s + 2 * B, 260, 160)
s += 2.2 * B
line('Built for', s, s + 2.2 * B, 40, 150)
line('<i>gamers.</i>', s + .6 * B, s + 2.2 * B, 230, 210)
collapse = s + 2.4 * B
T = {'collapse': round(collapse, 3), 'outro': round(collapse + .15, 3), 'tag': round(collapse + 1.62, 3), 'bio': round(collapse + 2.55, 3), 'voice': round(collapse + .14, 3), 'end': round(collapse + 4.0, 3)}
html = open('scene.template.html').read().replace('/*T*/', json.dumps(T)).replace('/*LINES*/', json.dumps(LINES))
open('scene.html', 'w').write(html)
json.dump({'T': T, 'lines': LINES}, open('timeline.json', 'w'))
print(T)
