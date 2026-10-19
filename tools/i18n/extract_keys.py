import re, glob, json, os, sys

here = os.path.dirname(os.path.abspath(__file__))
repo = os.path.abspath(os.path.join(here, '..', '..'))
ui = os.path.join(repo, 'src', 'QuakeAssetManager')
core = os.path.join(repo, 'src', 'QuakeAssetManager.Core')
keys = []

def add(k):
    if k and k not in keys:
        keys.append(k)

def unescape(lit):
    return lit.encode('utf-8').decode('unicode_escape').encode('latin1').decode('utf-8')

cs_lit = r'"((?:[^"\\]|\\.)*)"'
for path in glob.glob(os.path.join(ui, '*.cs')):
    if 'Snapshots' in path:
        continue
    s = open(path, encoding='utf-8').read()
    for m in re.finditer(r'Loc\.[TF]\(\s*' + cs_lit, s):
        add(unescape(m.group(1)))
    if path.endswith('AboutFacts.cs'):
        body = s[s.index('Lines ='):]
        for m in re.finditer(cs_lit, body):
            v = unescape(m.group(1))
            if v.strip():
                add(v)
for path in glob.glob(os.path.join(ui, '*.axaml')):
    s = open(path, encoding='utf-8').read()
    for m in re.finditer(r"\{local:T '([^']*)'\}", s):
        add(m.group(1).replace('&lt;', '<').replace('&gt;', '>').replace('&quot;', '"').replace('&#10;', '\n').replace('&amp;', '&'))

# Messages from the core library that the UI translates.
for name in ['SettingsValidator.cs']:
    s = open(os.path.join(core, name), encoding='utf-8').read()
    for m in re.finditer(r'(?:Error|error)\s*=\s*' + cs_lit, s):
        add(unescape(m.group(1)))
    for m in re.finditer(r'\?\s*' + cs_lit + r'\s*:', s):
        add(unescape(m.group(1)))
    for m in re.finditer(r'\bquakeError = ' + cs_lit + r'|\bassetsError = ' + cs_lit + r'|\bclientError = ' + cs_lit, s):
        add(unescape(next(g for g in m.groups() if g)))
s = open(os.path.join(core, 'AssetManager.cs'), encoding='utf-8').read()
ne = s[s.index('public static string? NameError'):]
ne = ne[:ne.index('\n    }\n')]
for m in re.finditer(r'return ' + cs_lit, ne):
    add(unescape(m.group(1)))
for k in ['{0} files#few', '{0} files#one', 'hidden', 'name fits several asset types', 'the game doesn\'t load this name',
          'unsupported file type', 'not found', 'Rust', 'Slate', 'Parchment']:
    add(k)

out = os.path.join(here, 'keys.json')
json.dump(keys, open(out, 'w', encoding='utf-8'), ensure_ascii=False, indent=1)
print(len(keys))
