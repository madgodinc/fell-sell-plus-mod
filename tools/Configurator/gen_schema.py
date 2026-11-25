"""Builds the configurator's fallback schema from the mod sources (every cfg.Bind call and the
Challenge parameter table), so the app knows every setting before the game has ever run.
Usage: python gen_schema.py <src/FellSellPlus> <out schema.json>"""
import json, os, re, sys

src, out = sys.argv[1], sys.argv[2]
entries = []


def args_of(text, start):
    """Split the argument list of a call whose '(' is at text[start]."""
    depth, i, cur, parts, in_str = 0, start, '', [], False
    while i < len(text):
        c = text[i]
        if in_str:
            cur += c
            if c == '\\': cur += text[i + 1]; i += 1
            elif c == '"': in_str = False
        elif c == '"':
            in_str = True; cur += c
        elif c == '(':
            depth += 1
            if depth > 1: cur += c
        elif c == ')':
            depth -= 1
            if depth == 0: parts.append(cur.strip()); return parts
            cur += c
        elif c == ',' and depth == 1:
            parts.append(cur.strip()); cur = ''
        else:
            cur += c
        i += 1
    return parts


def strings(s):
    return ''.join(m.group(1) for m in re.finditer(r'"((?:[^"\\]|\\.)*)"', s))


def typed(v):
    v = v.strip()
    if v in ('true', 'false'): return 'Boolean', v == 'true'
    if v.startswith('"'): return 'String', strings(v)
    if v.endswith('f') or '.' in v: return 'Single', float(v.rstrip('f'))
    return 'Int32', int(v)


for root, _, files in os.walk(src):
    if os.sep + 'obj' in root or os.sep + 'bin' in root: continue
    for fn in files:
        if not fn.endswith('.cs'): continue
        text = open(os.path.join(root, fn), encoding='utf8').read()
        consts = dict(re.findall(r'const string (\w+) = "(\w+)";', text))
        for m in re.finditer(r'\.Bind\(', text):
            a = args_of(text, m.end() - 1)
            if len(a) < 3: continue
            sec = strings(a[0]) if a[0].startswith('"') else consts.get(a[0])
            if not sec or not a[1].startswith('"'): continue
            if a[2].startswith('-1f') and 'Challenge_' in a[0] + sec: continue
            try: t, d = typed(a[2])
            except ValueError: continue
            rest = ','.join(a[3:])
            rng = re.search(r'AcceptableValueRange<\w+>\(\s*([-\d.f]+)\s*,\s*([-\d.f]+)\s*\)', rest)
            desc = strings(rest.split('new AcceptableValueRange')[0])
            e = dict(section='FellSellPlus_' + sec, key=strings(a[1]), type=t, default=d, description=desc)
            if rng:
                conv = float if t == 'Single' else int
                e['min'], e['max'] = conv(float(rng.group(1).rstrip('f'))), conv(float(rng.group(2).rstrip('f')))
            entries.append(e)
        # Challenge parameters: new("Section", "Key", "Text", vanilla, min, max, H/L, veteran, nightmare, blasphemous, insane)
        for m in re.finditer(r'new\("(\w+)", "(\w+)", "((?:[^"\\]|\\.)*)", ([-\d.f]+), ([-\d.f]+), ([-\d.f]+), [HL], ([-\d.f]+), ([-\d.f]+), ([-\d.f]+), ([-\d.f]+)\)', text):
            f = lambda x: float(x.rstrip('f'))
            entries.append(dict(section='FellSellPlus_Challenge_' + m.group(1), key=m.group(2), type='Single', default=-1.0,
                                min=-1.0, max=f(m.group(6)), description=m.group(3), vanilla=f(m.group(4)),
                                presets=[f(m.group(i)) for i in range(7, 11)]))

seen, uniq = set(), []
for e in entries:
    k = (e['section'], e['key'])
    if k not in seen: seen.add(k); uniq.append(e)
json.dump({'entries': uniq}, open(out, 'w', encoding='utf8'), ensure_ascii=False, indent=1)
print(f'{len(uniq)} settings')
