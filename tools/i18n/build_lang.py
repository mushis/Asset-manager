import json, re, os, sys, glob

here = os.path.dirname(os.path.abspath(__file__))
keys = json.load(open(os.path.join(here, 'keys.json'), encoding='utf-8'))
out_dir = os.path.abspath(os.path.join(here, '..', '..', 'src', 'QuakeAssetManager', 'lang'))
os.makedirs(out_dir, exist_ok=True)
ok = True
for path in sorted(glob.glob(os.path.join(here, 'tr', '*.txt'))):
    code = os.path.splitext(os.path.basename(path))[0]
    result = {}
    problems = []
    for n, line in enumerate(open(path, encoding='utf-8').read().splitlines(), 1):
        if not line.strip():
            continue
        idx, _, text = line.partition('|')
        text = text.replace('\\n', '\n')   # a line break inside a text is written as \n
        i = int(idx)
        key = keys[i]
        if set(re.findall(r'\{\d\}', key)) != set(re.findall(r'\{\d\}', text)):
            problems.append(f'{i}: placeholders differ: {text}')
        if key.count('**') != text.count('**') or key.count('`') != text.count('`'):
            problems.append(f'{i}: markup differs: {text}')
        if key.startswith('# ') != text.startswith('# ') or key.startswith('• ') != text.startswith('• '):
            problems.append(f'{i}: heading/bullet differs: {text}')
        result[key] = text
    skip = {'ezquake.exe'} | ({'{0} files#few', '{0} files#one'} if code != 'ru' else set())
    missing = [i for i, k in enumerate(keys) if k not in result and k not in skip]
    if missing:
        problems.append(f'missing indexes: {missing}')
    if problems:
        ok = False
        print(code, 'PROBLEMS:')
        for p in problems:
            print('  ', p)
    json.dump(result, open(os.path.join(out_dir, code + '.json'), 'w', encoding='utf-8'), ensure_ascii=False, indent=1)
    print(code, len(result), 'entries')
sys.exit(0 if ok else 1)
