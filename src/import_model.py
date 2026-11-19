"""Convert the downloaded COLLADA rig without requiring Blender at runtime."""
from pathlib import Path
import xml.etree.ElementTree as ET
import numpy as np
import json

root=Path(__file__).resolve().parents[1]
r=ET.parse(root/'assets/model/Luonnotar/Luonnotar.dae').getroot()
ns={'c':r.tag.split('}')[0][1:]}
def floats(e): return np.array(e.text.split(),float)
bones=[]; globals_=[]
def walk(node,parent=-1):
    local=floats(node.find('c:matrix',ns)).reshape(4,4)
    if node.get('type')=='JOINT':
        idx=len(bones)
        g=globals_[parent]@local if parent>=0 else local
        bones.append(dict(name=node.get('sid'),parent=parent,local=local.ravel().tolist(),inverse=np.linalg.inv(g).ravel().tolist()))
        globals_.append(g);parent=idx
    for child in node.findall('c:node',ns): walk(child,parent)
for node in r.findall('c:library_visual_scenes/c:visual_scene/c:node',ns):walk(node)
ids={b['name']:i for i,b in enumerate(bones)}
meshes=[]
for geo in r.findall('.//c:geometry',ns):
    if geo.get('name')=='Moon.002': continue
    mesh=geo.find('c:mesh',ns);sources={s.get('id'):s for s in mesh.findall('c:source',ns)}
    positions=floats(mesh.find('c:source/c:float_array',ns)).reshape(-1,3)
    tris=mesh.find('c:triangles',ns);ins={i.get('semantic'):i for i in tris.findall('c:input',ns)}
    stride=max(int(i.get('offset')) for i in ins.values())+1
    tuples=np.array(tris.find('c:p',ns).text.split(),int).reshape(-1,stride)
    uvs=floats(sources[ins['TEXCOORD'].get('source')[1:]].find('c:float_array',ns)).reshape(-1,2)
    skin=next(s for s in r.findall('.//c:skin',ns) if s.get('source')=='#'+geo.get('id'))
    ss={s.get('id'):s for s in skin.findall('c:source',ns)}
    vw=skin.find('c:vertex_weights',ns)
    wi={i.get('semantic'):i for i in vw.findall('c:input',ns)}
    names=ss[wi['JOINT'].get('source')[1:]].find('c:Name_array',ns).text.split()
    weights=floats(ss[wi['WEIGHT'].get('source')[1:]].find('c:float_array',ns))
    vv=np.array(vw.find('c:v',ns).text.split(),int).reshape(-1,2)
    influence=[];offset=0
    for cnt in map(int,vw.find('c:vcount',ns).text.split()):
        pairs=vv[offset:offset+cnt];offset+=cnt
        influence.append([[ids[names[j]],float(weights[w])] for j,w in pairs])
    verts=[];tri=[];seen={}
    for tup in tuples:
        p=int(tup[int(ins['VERTEX'].get('offset'))]);uv=int(tup[int(ins['TEXCOORD'].get('offset'))])
        key=(p,round(float(uvs[uv,0]),6),round(float(uvs[uv,1]),6))
        if key not in seen:
            seen[key]=len(verts)
            verts.append(dict(p=positions[p].tolist(),uv=[float(uvs[uv,0]),1-float(uvs[uv,1])],weights=influence[p]))
        tri.append(seen[key])
    meshes.append(dict(name=geo.get('name'),vertices=verts,triangles=tri))
out=dict(bones=bones,meshes=meshes)
(root/'assets/model/rig.json').write_text(json.dumps(out,separators=(',',':')),encoding='utf-8')
print('Imported',len(bones),'bones;',[(m['name'],len(m['vertices']),len(m['triangles'])//3) for m in meshes])
