"""嘴部形态修复：由 Blender 后台显式执行，不注册自动运行。

旧 refine_shape_keys 只拆眉毛并镜像原位移，无法修复唇缘突变；本脚本保留
Basis、拓扑、UV、所有非嘴部 Key，仅重建现有嘴部位移并另存候选版本。
用法：blender --factory-startup --background <备份.blend> --python <本脚本>
      -- <输出.blend> <诊断.json>
"""
from pathlib import Path
import bpy
import numpy as np
import sys
import json
import itertools
from mathutils.kdtree import KDTree
from mathutils import Vector

args=sys.argv[sys.argv.index('--')+1:]
output=Path(args[0])
assert not output.exists(), '候选文件已存在，不覆盖'
obj=bpy.data.objects['Eve']
if bpy.context.object and bpy.context.object.mode!='OBJECT':
    bpy.ops.object.mode_set(mode='OBJECT')
keys=obj.data.shape_keys.key_blocks
basis=keys[0]
p=np.array([v.co[:] for v in basis.data],dtype=float)
original={k.name:np.array([v.co[:] for v in k.data],dtype=float) for k in keys}
assert all(k.relative_key==basis for k in keys[1:])
obj.data.calc_loop_triangles()
t=np.array([tri.vertices[:] for tri in obj.data.loop_triangles])
tree=KDTree(len(p))
for i,co in enumerate(p):tree.insert(Vector(co),i)
tree.balance()
mirror=[]
for co in p:
    _,index,error=tree.find(Vector((-co[0],co[1],co[2])))
    assert error<1e-6
    mirror.append(index)
mirror=np.array(mirror)
assert len(set(mirror))==len(p)

# 反距离边权让挤在一起的唇缘顶点一起运动，避免极小三角面撕裂。
edges=np.array([e.vertices[:] for e in obj.data.edges])
a=np.concatenate((edges[:,0],edges[:,1]));b=np.concatenate((edges[:,1],edges[:,0]))
w=1/np.maximum(np.linalg.norm(p[a]-p[b],axis=1),.00025)**2
tot=np.bincount(a,weights=w,minlength=len(p))
def smooth(values,steps=18):
    values=values.copy()
    for _ in range(steps):
        neighbour=np.bincount(a,weights=w*values[b],minlength=len(p))/np.maximum(tot,1)
        values=.55*values+.45*neighbour
    return values
def smoothstep(x):
    x=np.clip(x,0,1)
    return x*x*(3-2*x)
def compact(x):
    x=np.clip(x,0,1)
    return (1-x)**4*(1+4*x)

upper=np.maximum(original['Mouth_UpperLipL_Up'][:,2]-p[:,2], original['Mouth_UpperLipR_Up'][:,2]-p[:,2])
upper=(upper+upper[mirror])/2
lower=np.maximum(p[:,2]-original['Mouth_LowerLip_Down'][:,2],0)
lower=(lower+lower[mirror])/2
upper=smooth(upper,24);lower=smooth(lower)
upper=upper/upper.max()*.0090
lower=lower/lower.max()*.0080
side=smoothstep((p[:,0]+.022)/.044)
delta={}
for name,share in [('Mouth_UpperLipL_Up',1-side),('Mouth_UpperLipR_Up',side)]:
    d=np.zeros_like(p);d[:,2]=upper*share
    # 唇体整体沿竖直方向运动，保留前后层厚度，不单独拉扯尖锐轮廓。
    delta[name]=d
d=np.zeros_like(p);d[:,2]=-lower;delta['Mouth_LowerLip_Down']=d
for side_name,sign in [('L',-1),('R',1)]:
    center=np.array([sign*.0335,-.104,-.0575])
    radius=np.array([.065,.075,.055])
    influence=compact(np.linalg.norm((p-center)/radius,axis=1))
    influence=smooth(influence,8)
    influence/=influence.max()
    for direction,amount in [('Up',.0050),('Down',-.0050)]:
        d=np.zeros_like(p);d[:,2]=influence*amount
        delta[f'Mouth_{side_name}_{direction}']=d
    for direction,amount in [('Out',.0040),('In',-.0040)]:
        d=np.zeros_like(p);d[:,0]=influence*sign*amount
        delta[f'Mouth_{side_name}_{direction}']=d

base_n=np.cross(p[t[:,1]]-p[t[:,0]],p[t[:,2]]-p[t[:,0]])
base_area=np.linalg.norm(base_n,axis=1)
def check(d):
    q=p+d
    n=np.cross(q[t[:,1]]-q[t[:,0]],q[t[:,2]]-q[t[:,0]])
    areas=np.linalg.norm(n,axis=1)/np.maximum(base_area,1e-20)
    bad=np.where((np.sum(n*base_n,axis=1)<=0)&(base_area>1e-12))[0]
    return {'flips':bad.tolist(),'minimum_area_ratio':float(areas[base_area>1e-12].min())}

report={'source':Path(bpy.data.filepath).name,'output':output.name,'keys':len(keys)-1,'vertices':len(p),'single':{},'combinations':[]}
for name,d in delta.items():
    report['single'][name]={'max_displacement':float(np.linalg.norm(d,axis=1).max()),'affected_vertices':int(np.sum(np.linalg.norm(d,axis=1)>1e-7)),**check(d)}
for states in itertools.product((-1,0,1),repeat=4):
    chosen={}
    for (side_name,axis),state in zip(itertools.product(('L','R'),('vertical','horizontal')),states):
        if state:
            direction=('Up' if state>0 else 'Down') if axis=='vertical' else ('Out' if state>0 else 'In')
            chosen[f'Mouth_{side_name}_{direction}']=1
    for lip_states in itertools.product((0,1),repeat=3):
        names=('Mouth_UpperLipL_Up','Mouth_UpperLipR_Up','Mouth_LowerLip_Down')
        combination=dict(chosen)
        combination.update({name:1 for name,state in zip(names,lip_states) if state})
        d=sum((delta[name]*v for name,v in combination.items()),np.zeros_like(p))
        report['combinations'].append({'weights':combination,**check(d)})
random=np.random.default_rng(20261002)
report['random_samples']=4096
report['random_failures']=[]
report['random_minimum_area_ratio']=1.0
for _ in range(report['random_samples']):
    combination={}
    for side_name in ('L','R'):
        for positive,negative in [('Up','Down'),('Out','In')]:
            value=float(random.uniform(-1,1))
            combination[f'Mouth_{side_name}_{positive if value>=0 else negative}']=abs(value)
    for name in ('Mouth_UpperLipL_Up','Mouth_UpperLipR_Up','Mouth_LowerLip_Down'):
        combination[name]=float(random.uniform(0,1))
    measured=check(sum((delta[name]*v for name,v in combination.items()),np.zeros_like(p)))
    report['random_minimum_area_ratio']=min(report['random_minimum_area_ratio'],measured['minimum_area_ratio'])
    if measured['flips'] or measured['minimum_area_ratio']<.15:
        report['random_failures'].append({'weights':combination,**measured})
print('RANDOM_GEOMETRY',report['random_samples'],'failures',len(report['random_failures']),'minimum_area_ratio',report['random_minimum_area_ratio'])
print('SINGLE',json.dumps(report['single']))
bad=[c for c in report['combinations'] if c['flips'] or c['minimum_area_ratio']<.15]
print('COMBINATIONS',len(report['combinations']),'BAD',len(bad),'WORST',sorted(report['combinations'],key=lambda c:c['minimum_area_ratio'])[:3])
assert not bad and not report['random_failures'], '几何检查失败，不保存最终版'
Path(args[1]).write_text(json.dumps(report,indent=2),encoding='utf-8')
for name,d in delta.items():
    for v,co in zip(keys[name].data,p+d):v.co=co
for key in keys:
    key.value=0
    if not key.name.startswith('Mouth'):
        assert np.array_equal(original[key.name],np.array([v.co[:] for v in key.data]))
obj.data.update()
bpy.ops.wm.save_as_mainfile(filepath=str(output))
print('SAVED_CANDIDATE',output.name)
