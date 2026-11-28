# Writes the bow mesh without its built-in string (a separate 32-vertex strip) as a small binary
# for the mod: int nv, int ni, float pos[3nv], float normal[3nv], float uv[2nv], int index[ni].
import UnityPy, sys, os, struct, collections
from UnityPy.helpers.MeshHelper import MeshHandler
D=r"C:\Program Files (x86)\Steam\steamapps\common\Fell & Sell\Fell & Sell_Data"
env=UnityPy.load(os.path.join(D,"resources.assets"))
m=[o.read() for o in env.objects if o.type.name=="Mesh" and o.read().m_Name=="SM_Prop_Bow_01"][0]
h=MeshHandler(m); h.process()
V=h.m_Vertices; N=h.m_Normals; U=h.m_UV0
tris=h.get_triangles()
print("verts",len(V),"normals",len(N) if N else None,"uv",len(U) if U else None,"submeshes",len(tris),"colors",bool(h.m_Colors), "uv1", bool(h.m_UV1))
T=[t for sub in tris for t in sub]
key={}; ids=[key.setdefault(tuple(round(c,4) for c in v[:3]),len(key)) for v in V]
par=list(range(len(key)))
def find(x):
    while par[x]!=x: par[x]=par[par[x]]; x=par[x]
    return x
for t in T:
    for i in t[1:]: par[find(ids[t[0]])]=find(ids[i])
comp=collections.defaultdict(list)
for i in range(len(V)): comp[find(ids[i])].append(i)
drop=set()
for c,idx in comp.items():
    xs=[abs(V[i][0]) for i in idx]
    print(len(idx), max(xs), min(V[i][2] for i in idx), max(V[i][2] for i in idx))
    if len(idx)<100 and max(xs)<0.01: drop|=set(idx)
keep=[i for i in range(len(V)) if i not in drop]; remap={o:n for n,o in enumerate(keep)}
T2=[t for t in T if not any(i in drop for i in t)]
out=struct.pack("<ii",len(keep),len(T2)*3)
out+=b"".join(struct.pack("<3f",*V[i][:3]) for i in keep)
out+=b"".join(struct.pack("<3f",*N[i][:3]) for i in keep)
out+=b"".join(struct.pack("<2f",*U[i][:2]) for i in keep)
out+=b"".join(struct.pack("<3i",*[remap[i] for i in t]) for t in T2)
open(sys.argv[1],"wb").write(out); print("kept",len(keep),"tris",len(T2),"bytes",len(out))
