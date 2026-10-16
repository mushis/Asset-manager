"""
Keep the tr/<code>.txt files in step with keys.json after texts were added or removed.

Usage:
  python reindex.py before   # remember the current key for every index (run BEFORE extract_keys.py)
  python extract_keys.py     # rebuild keys.json from the code
  python reindex.py after    # rewrite tr/*.txt with the new indexes; prints the texts still to translate
"""
import json, os, sys, glob

here = os.path.dirname(os.path.abspath(__file__))
snapshot = os.path.join(here, 'tr', '.by-key.json')

def load_tr(path, keys):
    out = {}
    for line in open(path, encoding='utf-8').read().splitlines():
        if not line.strip():
            continue
        idx, _, text = line.partition('|')
        out[keys[int(idx)]] = text
    return out

if sys.argv[1:] == ['before']:
    keys = json.load(open(os.path.join(here, 'keys.json'), encoding='utf-8'))
    data = {os.path.basename(p)[:-4]: load_tr(p, keys) for p in glob.glob(os.path.join(here, 'tr', '*.txt'))}
    json.dump(data, open(snapshot, 'w', encoding='utf-8'), ensure_ascii=False)
    print('saved', {k: len(v) for k, v in data.items()})
elif sys.argv[1:] == ['after']:
    keys = json.load(open(os.path.join(here, 'keys.json'), encoding='utf-8'))
    data = json.load(open(snapshot, encoding='utf-8'))
    missing = set()
    for code, by_key in data.items():
        lines = [f'{i}|{by_key[k]}' for i, k in enumerate(keys) if k in by_key]
        open(os.path.join(here, 'tr', code + '.txt'), 'w', encoding='utf-8').write('\n'.join(lines) + '\n')
        missing |= {i for i, k in enumerate(keys) if k not in by_key and k not in ('ezquake.exe',) and not k.endswith(('#few', '#one'))}
    os.remove(snapshot)
    for i in sorted(missing):
        print(f'{i}|{keys[i]}')
else:
    print(__doc__)
