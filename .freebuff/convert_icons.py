"""Replace the hand-drawn Path icons with Material two-tone TwoToneIcon controls.

The old markup says which icon it wants and how it is tinted; this keeps the tint, drops the
stroke-only attributes, and points at the generated geometry pair instead.
"""
import re
from pathlib import Path

MAPPING = {
    'IconTrash': 'ToneDelete',
    'IconClose': 'ToneClose',
    'IconInfo': 'ToneInfo',
    'IconFolder': 'ToneFolder',
    'IconShare': 'ToneIosShare',
    'IconNearby': 'ToneWifiTethering',
    'IconChevronLeft': 'ToneChevronLeft',
    'IconStar': 'ToneStar',
    'IconPot': 'ToneSoupKitchen',
    'IconPlus': 'ToneAdd',
    'IconLeaf': 'ToneEco',
    'IconChevronRight': 'ToneChevronRight',
    'IconArrowUp': 'ToneArrowUpward',
    'IconArrowDown': 'ToneArrowDownward',
    'IconTimer': 'ToneTimer',
    'IconTag': 'ToneLabel',
    'IconSliders': 'ToneTune',
    'IconPlay': 'TonePlayArrow',
    'IconPerson': 'TonePerson',
    'IconPencil': 'ToneEdit',
    'IconClock': 'ToneSchedule',
    'IconDownload': 'ToneDownload',
    'IconCards': 'ToneStyle',
    'IconCamera': 'TonePhotoCamera',
    'IconBook': 'ToneMenuBook',
}

GEOMETRY = set(re.findall(r'x:Key="(Tone\w+)"', Path('src/Sedhep/Styles/TwoToneIcons.axaml').read_text()))

DROP = {'Data', 'Stroke', 'StrokeThickness', 'StrokeLineCap', 'StrokeJoin', 'Fill', 'Classes'}
SIZE = {'icon': 20, 'sm': 16, 'lg': 26}


def convert(tag):
    attrs = re.findall(r'([\w:.]+)="([^"]*)"', tag)
    values = dict(attrs)
    # Data value looks like {StaticResource IconTrash}
    icon = re.search(r'StaticResource (Icon\w+)', values['Data']).group(1)
    resource = MAPPING[icon]

    classes = values.get('Classes', '').split()
    bound_favorite = 'Classes.favorite' in values
    size = values.get('Width')
    height = values.get('Height')
    if size is None:
        for token, px in (('lg', 26), ('sm', 16)):
            if token in classes:
                size = height = str(px)
                break
        else:
            size = height = '20'

    if 'Stroke' in values:
        foreground = values['Stroke']
    elif 'Fill' in values:
        foreground = values['Fill']
    elif 'onAccent' in classes:
        foreground = '{DynamicResource OnAccent}'
    elif 'accent' in classes:
        foreground = '{DynamicResource AccentInk}'
    elif 'favorite' in classes:
        foreground = '{DynamicResource FavoriteInk}'
    elif bound_favorite:
        foreground = None
    else:
        foreground = '{DynamicResource TextSecondary}'

    out = [('Data', f'{{StaticResource {resource}}}')]
    if f'{resource}Soft' in GEOMETRY:
        out.append(('SoftData', f'{{StaticResource {resource}Soft}}'))
    if foreground:
        out.append(('Foreground', foreground))

    for name, value in attrs:
        if name in DROP or name in ('Width', 'Height'):
            continue
        if name == 'Classes.favorite':
            out.append((name, value))
            continue
        out.append((name, value))

    out.append(('Width', size))
    out.append(('Height', height))

    body = ' '.join(f'{name}="{value}"' for name, value in out)
    return f'<controls:TwoToneIcon {body} />'


pattern = re.compile(r'<Path\b[^>]*?Data="\{StaticResource Icon\w+\}"[^>]*?/>', re.S)
changed = 0
for view in sorted(Path('src/Sedhep/Views').glob('*.axaml')):
    text = view.read_text(encoding='utf-8')

    def replace(match):
        global changed
        changed += 1
        return convert(match.group(0))

    updated = pattern.sub(replace, text)
    if updated != text:
        view.write_text(updated, encoding='utf-8')
        print('updated', view)
print('converted', changed, 'icons')
