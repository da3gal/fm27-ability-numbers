# Scan FM27 UI bundles for VisualTreeAssets; list asset names + which reference given type names.
import sys, os, UnityPy, json
d = sys.argv[1]; out = sys.argv[2]
res = {}
for f in sorted(os.listdir(d)):
    if not f.startswith('ui-visualtreeassets'): continue
    try:
        env = UnityPy.load(os.path.join(d, f))
    except Exception as e:
        print('ERR', f, e); continue
    for obj in env.objects:
        if obj.type.name != 'MonoBehaviour': continue
        try:
            tree = obj.read_typetree()
        except Exception:
            continue
        name = tree.get('m_Name', '')
        if 'm_VisualTree' in tree:
            res.setdefault(f, []).append(name)
            os.makedirs(out, exist_ok=True)
            with open(os.path.join(out, f'{name or obj.path_id}.json'), 'w') as fh:
                json.dump(tree, fh, default=str)
print(sum(len(v) for v in res.values()), 'VTAs')
for f, v in res.items(): print(f, len(v))
