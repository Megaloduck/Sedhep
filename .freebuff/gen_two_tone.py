"""Turn Material Icons two-tone SVGs into XAML StreamGeometry pairs.

Each two-tone icon is two drawings: the main outline (full ink) and a lighter fill (30% ink)
that sits underneath. Splitting them lets the app tint both layers with its own theme colours.
"""
import xml.etree.ElementTree as ET
from pathlib import Path

SRC = Path('.freebuff/icons')
OUT = Path('src/Sedhep/Styles/TwoToneIcons.axaml')
NS = '{http://www.w3.org/2000/svg}'


def num(v):
    return f'{float(v):g}'


def points_to_path(points):
    values = [float(v) for v in points.replace(',', ' ').split()]
    coords = list(zip(values[0::2], values[1::2]))
    d = f'M{num(coords[0][0])}, {num(coords[0][1])}'
    for x, y in coords[1:]:
        d += f' L{num(x)}, {num(y)}'
    return d + ' Z'


def circle_to_path(cx, cy, r):
    cx, cy, r = num(cx), num(cy), num(r)
    return (f'M{cx}, {cy} m-{r}, 0 a {r},{r} 0 1,0 {num(float(r) * 2)},0 '
            f'a {r},{r} 0 1,0 -{num(float(r) * 2)},0 Z')


def shapes(node, opacity):
    out = []
    for el in node.iter():
        tag = el.tag.replace(NS, '')
        fill = (el.get('fill') or '').lower()
        if fill == 'none' or el.get('opacity') == '0':
            continue
        local = opacity * float(el.get('opacity', 1))
        if tag == 'path' and el.get('d'):
            out.append((el.get('d').strip(), local))
        elif tag == 'polygon' and el.get('points'):
            out.append((points_to_path(el.get('points')), local))
        elif tag == 'polyline' and el.get('points'):
            out.append((points_to_path(el.get('points')), local))
        elif tag == 'circle':
            out.append((circle_to_path(el.get('cx', 0), el.get('cy', 0), el.get('r', 0)), local))
        elif tag == 'rect' and el.get('width') and el.get('height'):
            x, y = float(el.get('x', 0)), float(el.get('y', 0))
            w, h = float(el.get('width')), float(el.get('height'))
            out.append((f'M{num(x)}, {num(y)} h{num(w)} v{num(h)} h-{num(w)} Z', local))
    return out


def build(path):
    root = ET.parse(path).getroot()
    parts = shapes(root, 1.0)
    main = [d for d, o in parts if o > 0.5]
    soft = [d for d, o in parts if o <= 0.5]
    return ' '.join(main), ' '.join(soft)


def pascal(name):
    return ''.join(word.capitalize() for word in name.split('_'))


resources = []
for svg in sorted(SRC.glob('*.svg')):
    main, soft = build(svg)
    key = pascal(svg.stem)
    resources.append((f'Tone{key}', main))
    if soft:
        resources.append((f'Tone{key}Soft', soft))

lines = [
    '<ResourceDictionary xmlns="https://github.com/avaloniaui"',
    '                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">',
    '  <!--',
    '    Material Icons, two-tone. Each icon is two geometries: the outline and the lighter fill',
    '    that sits under it. The control that draws them (Sedhep.Controls.TwoToneIcon) tints both',
    '    with the surrounding foreground, so the icons follow the theme instead of the font palette.',
    '  -->',
]
for key, data in resources:
    lines.append(f'  <StreamGeometry x:Key="{key}">{data}</StreamGeometry>')
lines.append('</ResourceDictionary>')
OUT.write_text('\n'.join(lines) + '\n', encoding='utf-8')
print(f'wrote {OUT} with {len(resources)} geometries from {len(list(SRC.glob("*.svg")))} icons')
