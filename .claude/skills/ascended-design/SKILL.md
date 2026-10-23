---
name: ascended-design
description: Design rules for the Ascended Tweaks WPF app (colors, type scale, spacing, components, accessibility, honesty). Use before creating or changing any XAML, style, view model text or screenshot of the app, and to review a page against the design.
---

# Ascended Tweaks design rules

A friendly, lively tuning tool for PC gamers that explains instead of pushing. Factual, high quality, trustworthy.
There is only one design: dark. No light mode, no "follow Windows" option; Windows high contrast still uses system colors.
Direction: plum night with victory gold. The loud element is type (Bahnschrift Condensed), not decoration.
Never the generic "AI app" look: no navy plus electric blue, no near-black with one vermilion or acid accent, no coral or
terracotta, no blue-violet gradients, no neon outlines, no glass effects, no stat-tile rows, no card per list item.
If the UI and these rules disagree, fix the UI.

## 1. Colors: tokens only

Never write a hex color in a page, template or control. Use the brushes from `Themes/Tokens.Dark.xaml` through
`{DynamicResource Brush.X}`, so high contrast mode can swap them.

| Brush | Value | Role |
| --- | --- | --- |
| `Brush.Background` | #18131F | Window background, navigation (plum night) |
| `Brush.Surface` | #221B2B | Panels and tiles |
| `Brush.Elevated` | #2C2437 | Dialogs, side panel |
| `Brush.Border` | #40364F | Borders, row dividers |
| `Brush.Accent` | #FFD23F | Primary action, toggles on, focus, links (victory gold) |
| `Brush.AccentTint` | 14 % accent | Selected segment and navigation entry |
| `Brush.OnAccent` | #18131F | Text and icons on accent fills |
| `Brush.Success` | #5EE0A0 | Risk "Safe", success |
| `Brush.Warning` | #FF9F43 | Risk "Caution", notices (orange, so it never reads as the gold accent) |
| `Brush.Danger` | #FF6B8A | Risk "Expert", errors |
| `Brush.TextPrimary` | #F7F2FC | Body text, headings |
| `Brush.TextSecondary` | #BBB0CC | Descriptions, metadata |

Preset colors (`Brush.Hue.X`, tile `Brush.Hue.X.Tint` at 16 %): Mint #5EE0A0 (safe), Sky #63C2FF (gaming),
Coral #FF8A65 (competitive), Lime #B5E655 (laptop), Lilac #C3A6FF (privacy), Rose #FF8CC6 (cleanup), Teal #3FD9CF (office).
They mark which preset a tile belongs to (4-point top edge `PresetEdge`, symbol tile `PresetTile`, symbol `PresetSymbol`)
and never carry risk or state.

Glow (`Glow.Accent`, soft gold drop shadow without offset) only on primary buttons (not when disabled) and the selected
navigation bar. Nowhere else: not on text, tiles, symbols or borders.

- One accent fill per screen: the main action. Everything else is outline, text or neutral.
- Color is never the only signal: risk badges always have text and a symbol.
- Text needs 4.5:1, icons 3:1. New color pairs go into `tools/contrast-check`.
- Tints: selection 14 % accent, hover 10 % TextSecondary, filled badges 15 % tone.

## 2. Type scale

Two families with clear roles: `Font.Display` is Bahnschrift (ships with Windows 10 and 11; DIN-like, used in condensed
widths like a gauge or HUD label) for titles; `Font.Text` is Segoe UI Variable Text for everything people read.
Use the text styles, never a bare `FontSize` on text. Sentence case everywhere, no all-caps labels, no eyebrow labels.

| Style | Family | Size | Weight / width | Use |
| --- | --- | --- | --- | --- |
| `Text.Caption` | Text | 12 | Regular | Badges, metadata, chips |
| `Text.Simple.Caption` | Text | 13 | Regular | Hints in simple mode |
| `Text.Body` / `Text.Secondary` | Text | 14 | Regular | Body text, card description |
| `Text.BodyStrong` | Text | 14 | SemiBold | Row titles in expert mode, table headers |
| `Text.Simple.Body` | Text | 15 | Regular | Body text in simple mode |
| `Text.Simple.Title` | Text | 17 | SemiBold | Row titles in simple mode, large choice buttons |
| `Text.Subtitle` | Display | 22 | SemiBold, SemiCondensed | Section headings, preset tile names |
| `Text.Title` | Display | 32 | SemiBold, Condensed | Page titles |
| `Text.Hero` | Display | 44 | Bold, Condensed | Only the recommended preset's name on the start page |

Icons (`ui:SymbolIcon`) use 12, 16, 20 or 24, and 48 only in the hero symbol tile. The gap between an icon and its text is 8.
Card descriptions stop after two lines with an ellipsis; the full text is in the side panel.

## 3. Spacing, radius, size

- Spacing on the 8-point grid: 4, 8, 12, 16, 24, 32, 48 (20 only for simple-mode card padding 20 x 16).
  Padding of 6 or 10 is allowed only inside 1-point bordered controls, where it puts the content on the grid.
- Radius 8 (`Radius.Small`) for buttons, inputs, badges; 12 (`Radius.Large`) for panels, tiles and dialogs; a toggle track is a
  pill; a preset edge inside a panel uses 11,11,0,0 so it meets the 1-point border.
- Click targets at least 32 x 32, in simple mode at least 40 high; toggles 40 x 20.
- Window 1280 x 800 by default, minimum 800 x 480; content scrolls vertically. Navigation 240 wide, collapses to 56 below 1000.
- Card gap 16. Simple-mode pages have 24 bottom spacing.

## 4. Components

- **Tweak card**: min height 96, padding 16, radius 12, Surface with Border. Text on the left (name, short description,
  risk and impact badges, chips "Restart required" / "Admin required", then "More details" as a text button),
  toggle on the right with 24 gap, like Windows 11 Settings. Simple-mode cards follow the same layout.
- **Lists of tweaks** (simple mode): one Surface panel, one row per tweak with a 1-point divider above every row but the first;
  text left, toggle right. No card per item.
- **Start page**: the hero is the recommended preset itself, not a welcome text: 4-point edge in its color, its name in
  `Text.Hero`, its sentence, its level segments, "Changes: ...", the only primary button "Apply recommended settings" and
  "Show changes"; a divider, then one quiet line with the PC facts and the help link. A 96 x 96 symbol tile sits on the right.
  The other presets follow in two columns (the recommended one is not repeated): edge in the preset color, 48 x 48 symbol
  tile, name in `Text.Subtitle`, sentence, summary, "Changes: ...", levels, secondary "Apply" and "Show changes".
- **Navigation**: sits on `Brush.Background` without its own surface; entries at least 40 high; the selected entry
  has the accent tint, the accent bar, a filled accent icon and SemiBold text; a 1-point divider above the footer.
- **Mode switch** (Simple / Expert): one bordered segmented control on Surface; the active segment is `Brush.AccentTint`
  with SemiBold text, never an accent fill.
- **Buttons** (`Button.Primary`, `Button.Secondary`, `Button.Text`, `Button.Danger`): states normal, hover, pressed, focus,
  disabled, running (spinner, text stays visible).
- **Inputs**: radius 8, Border, accent frame on focus, error text below in Danger with a symbol.
- **Side panel**: 420 wide, Elevated, overlays content below 1000 wide.
- **Change bar**: bottom, 56 high, only with pending changes: Discard (text), Review (secondary), Apply (primary).
- **Dialogs**: radius 12, Elevated, soft short shadow; the preview is a table (Tweak, Location, Current, New).
- **Focus ring**: 2 points accent, 2 points offset, always visible with the keyboard (`FocusRing`).
- **Selection in lists and navigation**: 14 % accent tint plus accent bar on the left.
- Depth comes from surface color and border, not heavy shadows. Animations 200 ms ease-out, opacity and offset up to 8,
  none when Windows animations are off.

## 5. Text and honesty

- Every visible string comes from `Strings/en-US.json` and `Strings/de-DE.json` through `{s:Loc key}` or the localizer.
  Both files get the key in the same commit with the same placeholders. English is the default language.
- English: plain, natural ("Restart", not "Reboot"), no umlauts. German: "du", short sentences.
- No fear wording ("Your PC is at risk"), no percentage promises, no superlatives without proof, no overall health score.
- No emojis, no fake progress bars, no artificial waiting.
- Side effects are never empty; impact badges only as strong as the evidence.
- One click: everything that can be applied safely applies with one click (toggle, preset tile, "Apply recommended
  settings", "Fix now" in gaming checks). Before the click the card or tile says what changes ("Changes: ..."), and
  a "Show changes" link keeps the full preview available. Every change is backed up first and can be undone.
- Never one click for deleting: cleanup, large files and app removal keep their preview and confirmation, and
  "Show in File Explorer" stays so people see which file goes. Expert tweaks keep their three-step warning.

## 6. Accessibility

- Every interactive element has an `AutomationProperties.Name`; icon-only buttons also get a `ToolTip`.
- Never let a screen reader read a type name: bind names to text, not to objects.
- Works at 200 % scaling (1920 x 1080) without clipped content, and in high contrast mode (system colors, borders stay visible).
- Reasons for a disabled control are also shown as text, because tooltips of disabled elements are not reachable by keyboard.

## Checking a change

1. Run `python .claude/skills/ascended-design/scripts/design_audit.py`. It lists hex colors, off-scale text and icon sizes,
   off-grid spacing, odd radii, small click targets, hard-coded text and font overrides. Expected result: 0 findings.
2. Build and render: `dotnet build AscendedTweaks.sln`, then
   `src/AscendedTweaks.App/bin/Debug/net8.0-windows10.0.19041.0/AscendedTweaks.App.exe --snapshot <folder>`.
   It renders every page at 100 % and 200 %, narrow and in high contrast, and writes `a11y-report.txt` (expected: 0 findings).
   Look at the PNGs of every page you touched. The snapshots use the language saved in the user's settings.
3. Run the quality gate: `powershell -ExecutionPolicy Bypass -File build.ps1` (includes the contrast check).
