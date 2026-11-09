# Print a dumped VTA JSON as a compact element tree: type #name .classes {props} [serialized non-empty fields]
import json, sys, os
SKIP = {'uxmlAssetId','tooltip','viewDataKey','pickingMode','usageHints','languageDirection','tabIndex','focusable','enabledSelf','dataSourceUnityObject','dataSourcePathString','dataSourceTypeString'}
def show(path):
    t = json.load(open(path))
    refs = {r['rid']: r for r in t['references']['RefIds']}
    tmpl = {e.get('alias') or e.get('id'): e for e in t.get('m_AssetEntries', [])} if False else None
    def ser(rid):
        r = refs.get(rid)
        if not r or 'data' not in r: return ''
        d = r['data']; out = []
        for k, v in d.items():
            if k in SKIP or k.endswith('_UxmlAttributeFlags') or v in ('', 0, [], None, {}) : continue
            if isinstance(v, dict) and v.get('rid') == -2: continue
            if isinstance(v, dict) and set(v) == {'m_FileID','m_PathID'} and v['m_PathID'] == 0: continue
            out.append(f'{k}={json.dumps(v)[:200]}')
        return ' [' + ', '.join(out) + ']' if out else ''
    def walk(rid, depth):
        r = refs[rid]; d = r['data']
        typ = d.get('m_FullTypeName') or ('Template:' + d.get('m_TemplateAlias', '?'))
        props = {p['name']: p['value'] for p in d.get('m_Properties', [])}
        cls = '.'.join(d.get('m_Classes', []))
        sd = d.get('m_SerializedData', {}).get('rid', -2)
        print('  ' * depth + typ.split('.')[-1] if '.' in typ and not typ.startswith('Template') else '  ' * depth + typ,
              ('.' + cls) if cls else '', props if props else '', ser(sd))
        for c in d.get('m_Children', []): walk(c['rid'], depth + 1)
    walk(t['m_VisualTree']['rid'], 0)
for p in sys.argv[1:]:
    print('=====', os.path.basename(p)); show(p)
