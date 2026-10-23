---
name: ascended-design
description: Design rules for the Ascended Tweaks WPF app (colors, type scale, spacing, components, accessibility, honesty). Use before creating or changing any XAML, style, view model text or screenshot of the app, and to review a page against the design.
---

# Ascended Tweaks design rules

A friendly, lively Windows 11 tool that explains instead of pushing. Factual, high quality, tidy, trustworthy.
There is only one design: dark. No light mode, no "follow Windows" option; Windows high contrast still uses system colors.
Palette: gray is the main color (like a chat app), black for edges and soft shading, white text and blue accents:
strong blue buttons, light blue links and toggles, navy selection. No other hues except a muted red for "Expert" and errors.
Never the generic "AI app" look: no navy plus electric blue, no blue-violet gradients, no neon outlines, no glass effects.
If the UI and these rules disagree, fix the UI.

## 1. Colors: tokens only

Never write a hex color in a page, template or control. Use the brushes from `Themes/Tokens.Dark.xaml` through
`{DynamicResource Brush.X}`, so high contrast mode can swap them.

| Brush | Value | Role |
| --- | --- | --- |
| `Brush.Sidebar` | #202124 | Frame: title bar and navigation (darker gray) |
| `Brush.Background` | #2B2D31 | Content panel behind the pages (main gray), rounded top-left corner, black edge to the frame |
| `Brush.Surface` | #313338 | Cards and tiles (lighter gray) |
| `Brush.Elevated` | #383A40 | Dialogs, side panel |
| `Brush.Border` | #1E1F22 | Edges and dividers (black shading) |
| `Brush.AccentFill` | #3D6BE0 | Primary buttons (strong blue) |
| `Brush.OnAccentFill` | #FFFFFF | Text on primary buttons |
| `Brush.Accent` | #8AB0FF | Links, focus ring, toggles on, selection bars (light blue) |
| `Brush.OnAccent` | #1E1F22 | Toggle knob and text on light blue |
| `Brush.AccentTint` | #2A3A5E | Selected navigation entry and segment, symbol tiles (navy) |
| `Brush.Success` | #BEBEBE | Risk "Safe", success (light gray) |
| `Brush.Warning` | #F2F2F2 | Risk "Caution", notices (white) |
| `Brush.Danger` | #F29A9A | Risk "Expert", errors (muted red, the only extra hue) |
| `Brush.TextPrimary` | #F2F2F2 | Body text, headings |
| `Brush.TextSecondary` | #BEBEBE | Descriptions, metadata |

Risk levels differ by tone and always by symbol and text, never by color alone.
Shading: `Shadow.Card` (soft black shadow) only under the start hero and preset tiles, drawn on an underlay Border behind
the card, never as an effect on the card itself (effects blur the text).

- One accent fill per screen: the main action. Everything else is outline, text or neutral.
- Color is never the only signal: risk badges always have text and a symbol.
- Text needs 4.5:1, icons 3:1. New color pairs go into `tools/contrast-check`.
- Tints: selection 14 % accent, hover 10 % TextSecondary, filled badges 15 % tone.

## 2. Type scale

Font only through `Font.Text` (Segoe UI Variable Text) and `Font.Display` (Segoe UI Variable Display). Use the text styles,
never a bare `FontSize` on text:

| Style | Size | Weight | Use |
| --- | --- | --- | --- |
| `Text.Caption` | 12 | Regular | Badges, metadata, chips |
| `Text.Simple.Caption` | 13 | Regular | Hints in simple mode |
| `Text.Body` / `Text.Secondary` | 14 | Regular | Body text, card description |
| `Text.BodyStrong` | 14 | SemiBold | Card titles, table headers |
| `Text.Simple.Body` | 15 | Regular | Body text in simple mode |
| `Text.Simple.Title` | 17 | SemiBold | Card titles in simple mode, large choice buttons |
| `Text.Subtitle` | 20 | SemiBold | Section headings |
| `Text.Title` | 28 | SemiBold | Page titles |

Icons (`ui:SymbolIcon`) use 12, 16, 20 or 24. The gap between an icon and its text is 8.
Card descriptions stop after two lines with an ellipsis; the full text is in the side panel.

## 3. Spacing, radius, size

- Spacing on the 8-point grid: 4, 8, 12, 16, 24, 32, 48 (20 only for simple-mode card padding 20 x 16).
  Padding of 6 or 10 is allowed only inside 1-point bordered controls, where it puts the content on the grid.
- Radius 8 (`Radius.Small`) for buttons, inputs, badges; 12 (`Radius.Large`) for cards and dialogs; a toggle track is a pill.
- Click targets at least 32 x 32, in simple mode at least 40 high; toggles 40 x 20.
- Window 1280 x 800 by default, minimum 800 x 480; content scrolls vertically. Navigation 240 wide, collapses to 56 below 1000.
- Card gap 16. Simple-mode pages have 24 bottom spacing.

## 4. Components

- **Tweak card**: min height 96, padding 16, radius 12, Surface with Border. Text on the left (name, short description,
  risk and impact badges, chips "Restart required" / "Admin required", then "More details" as a text button),
  toggle on the right with 24 gap, like Windows 11 Settings. Simple-mode cards follow the same layout.
- **Start page**: a hero card (padding 32 x 24) with the 28 pt welcome title, one sentence, three fact tiles
  (bordered, radius 8, caption plus 17 pt value) and the only accent button of the page. The shell hides the page
  title there (`ShowTitle` false). Preset tiles: two columns, padding 24, min height 200, symbol 24 in a 48 x 48 navy tile with
  radius 12 (`PresetTile`, `PresetSymbol` styles), secondary "Review and apply" button.
- **Navigation**: sits on the darker `Brush.Sidebar` frame together with the title bar; entries at least 40 high; the selected entry
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
