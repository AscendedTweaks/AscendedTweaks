# Marketing videos

- `zilem-style/`: TikTok video with voice, step badges and green checks.
- `cinematic/`: TikTok video with voice, glowing horizon, light orb and serif type.
- `cinematic-warm-sfx/`: no voice, warm colours, sound effects only, text only on the end card.
- `chaos-to-calm/`: 14.5 s, no voice, pop-ups swept away by a light orb, warm colours.
- `TweakPilot-TikTok-Profile.png`: TikTok profile picture, 1080 x 1080.
- `voice/`: ElevenLabs voice (`original.mp3`), word alignment (`align.py`, PocketSphinx) and the reordered voice with longer pauses (`reorder.py`).

Each folder renders with `build.py` (writes `scene.html`), `render.js` (Playwright frames to `video-only.mp4`) and `mix.py` (voice plus Kenney CC0 sounds).
Paths inside the scripts point to the original working folders and need adjusting before a re-render.
