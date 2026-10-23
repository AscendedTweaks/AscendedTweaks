"""Checks the Ascended Tweaks XAML against the ascended-design rules. Prints every finding; exit code 1 if there are any."""
import glob
import os
import re
import sys

APP = os.path.join(os.path.dirname(os.path.abspath(__file__)), '..', '..', '..', '..', 'src', 'AscendedTweaks.App')
TEXT_SIZES = {'12', '13', '14', '15', '17', '20', '28'}
ICON_SIZES = {'12', '16', '20', '24'}
SPACING = {0, 1, 2, 4, 8, 12, 16, 20, 24, 32, 40, 48, 56}
BORDERED_PADDING = {6, 10}
RADII = {'0', '8', '12'}


def main():
    os.chdir(APP)
    files = [f for f in glob.glob('**/*.xaml', recursive=True)
             if 'Tokens.' not in f and os.sep + 'obj' + os.sep not in f and os.sep + 'bin' + os.sep not in f]
    findings = []
    for path in files:
        element = ''
        for number, line in enumerate(open(path, encoding='utf-8'), 1):
            stripped = line.strip()
            if stripped.startswith('<!--'):
                continue
            if stripped.startswith('<') and not stripped.startswith('</'):
                element = stripped.split()[0]

            def add(kind, value):
                findings.append(f'{path}:{number}  {kind}: {value}')

            for m in re.finditer(r'"#[0-9A-Fa-f]{3,8}"', stripped):
                add('hex color (use a Brush token)', m.group(0))
            for m in re.finditer(r'FontSize="([0-9.]+)"', stripped):
                is_icon = 'SymbolIcon' in element or 'SymbolIcon' in stripped
                allowed = ICON_SIZES if is_icon else TEXT_SIZES
                if m.group(1) not in allowed:
                    add('icon size' if is_icon else 'text size (use a Text.* style)', m.group(1))
            for m in re.finditer(r'(?<![\w.])(Margin|Padding)="([-0-9., ]+)"', stripped):
                values = [abs(float(v)) for v in re.split('[ ,]+', m.group(2).strip()) if v]
                allowed = SPACING | (BORDERED_PADDING if m.group(1) == 'Padding' and 'Button' in element else set())
                if any(v not in allowed for v in values):
                    add(m.group(1).lower() + ' off the 8-point grid', m.group(2))
            for m in re.finditer(r'(?<![\w.])CornerRadius="([0-9., ]+)"', stripped):
                corners = [v.strip() for v in m.group(1).split(',')]
                if any(c not in RADII for c in corners) and 'Track' not in stripped and 'PresetEdge' not in stripped:
                    add('radius (use Radius.Small or Radius.Large)', m.group(1))
            for m in re.finditer(r'(?<![\w.])(MinHeight|Height)="([0-9.]+)"', stripped):
                if 'Button' in element and float(m.group(2)) < 32:
                    add('click target below 32', m.group(0))
            for m in re.finditer(r'(?<![\w.:])(Text|Content|Header|ToolTip|Title)="([^"{][^"]*)"', stripped):
                if re.search('[A-Za-z]{3,}', m.group(2)):
                    add('hard-coded text (use a Loc key)', m.group(2)[:60])
            if 'FontFamily="' in stripped and 'Font.' not in stripped:
                add('font family (use Font.Text or Font.Display)', stripped[:80])
    for finding in findings:
        print(finding)
    print(f'{len(findings)} findings')
    return 1 if findings else 0


if __name__ == '__main__':
    sys.exit(main())
