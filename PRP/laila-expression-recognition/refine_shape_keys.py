"""Blender 中运行：镜像上唇并拆分眉毛，另存新版本。

历史制作工具：输入为对应阶段源模型，部分路径需要 .blend1；对最终模型重复运行会再次放大眉毛。

现有训练脚本不编辑网格；旧 Blender 内嵌验证面向历史 Key，不能复用。
用法：blender --background <源.blend> --python <本脚本>
"""
from pathlib import Path
import math
import itertools
import bpy
from mathutils import Vector
from mathutils.kdtree import KDTree


obj = bpy.data.objects['Eve']
assert obj.type == 'MESH' and obj.data.shape_keys
if bpy.context.object and bpy.context.object.mode != 'OBJECT':
    bpy.ops.object.mode_set(mode='OBJECT')
keys = obj.data.shape_keys.key_blocks
basis = keys[0]
coords = [p.co.copy() for p in basis.data]
original = {k.name: [p.co.copy() for p in k.data] for k in keys}
source = Path(bpy.data.filepath)
assert source.is_file()
is_split = 'Brow_L_Mid_Up' in keys
output = source.with_name(source.stem + ('-refined.blend' if is_split else '-brow-regions.blend'))
assert not output.exists(), '输出已存在，停止以防覆盖手工修改'
assert all(k.relative_key == basis for k in keys[1:])
assert not obj.data.shape_keys.animation_data, '存在动画或驱动，需先核对引用'

tree = KDTree(len(coords))
for i, co in enumerate(coords):
    tree.insert(co, i)
tree.balance()
mirror = []
for co in coords:
    _, index, distance = tree.find(Vector((-co.x, co.y, co.z)))
    assert distance < 1e-6, '基础网格不对称，不能直接镜像'
    mirror.append(index)
assert len(set(mirror)) == len(coords), '镜像匹配不是一一对应'
assert all(mirror[mirror[i]] == i for i in range(len(coords)))

left = keys['Mouth_UpperLipL_Up']
right = keys['Mouth_UpperLipR_Up']
restored = False
if not any((p.co-co).length > 1e-7 for p,co in zip(left.data,coords)):
    backup = source.with_name(source.stem.split('-brow-regions')[0] + '.blend1')
    assert backup.is_file(), 'L 上唇为空且没有源备份'
    with bpy.data.libraries.load(str(backup), link=False) as (_, loaded):
        loaded.objects = ['Eve']
    backup_obj = loaded.objects[0]
    backup_keys = backup_obj.data.shape_keys.key_blocks
    assert len(backup_keys[0].data) == len(coords)
    assert all((p.co-co).length < 1e-7 for p,co in zip(backup_keys[0].data,coords)), '备份 Basis 不匹配'
    for p,q in zip(left.data,backup_keys[left.name].data):
        p.co = q.co
    bpy.data.objects.remove(backup_obj, do_unlink=True)
    restored = True
assert any((p.co-co).length > 1e-7 for p,co in zip(left.data,coords)), 'L 上唇为空，禁止覆盖 R'
for i, p in enumerate(left.data):
    delta = p.co - coords[i]
    right.data[mirror[i]].co = coords[mirror[i]] + Vector((-delta.x, delta.y, delta.z))

removed = []
created = []
for side in ('L', 'R'):
    if is_split:
        continue
    names = [f'Brow_{side}_{direction}' for direction in ('Up', 'Down')]
    affected = [i for i in range(len(coords)) if any((original[n][i]-coords[i]).length > 1e-7 for n in names)]
    lo = min(abs(coords[i].x) for i in affected if abs(coords[i].x) > 1e-6)
    hi = max(abs(coords[i].x) for i in affected)
    assert hi > lo
    for direction, name in zip(('Up', 'Down'), names):
        parts = [obj.shape_key_add(name=f'Brow_{side}_{region}_{direction}', from_mix=False) for region in ('Inner', 'Mid', 'Outer')]
        for i, co in enumerate(coords):
            t = max(0.0, min(1.0, (abs(co.x)-lo)/(hi-lo))) * 2
            u = t if t <= 1 else t-1
            u = u*u*(3-2*u)
            weights = (1-u, u, 0) if t <= 1 else (0, 1-u, u)
            delta = original[name][i]-co
            for part, weight in zip(parts, weights):
                part.data[i].co = co + delta*weight
            reconstructed = sum((part.data[i].co-co for part in parts), Vector())
            assert (reconstructed-delta).length < 1e-7
        for part in parts:
            assert any((p.co-co).length > 1e-7 for p,co in zip(part.data,coords)), part.name
            created.append(part.name)
        obj.shape_key_remove(keys[name])
        removed.append(name)

gains = {}
if is_split:
    # 保留原方向与影响范围，只增强已有分段；Up/Down 同区域应互斥。
    for side in ('L', 'R'):
        for region in ('Inner', 'Mid', 'Outer'):
            for direction in ('Up', 'Down'):
                gain = {'Inner':1.0,'Mid':1.25,'Outer':1.4}[region] if direction == 'Up' else {'Inner':1.2,'Mid':1.25,'Outer':1.35}[region]
                gains[f'Brow_{side}_{region}_{direction}'] = gain
    obj.data.calc_loop_triangles()
    triangles = [tuple(t.vertices) for t in obj.data.loop_triangles]
    normals = [(coords[b]-coords[a]).cross(coords[c]-coords[a]) for a,b,c in triangles]

    def bad_faces(values):
        bad = set()
        for index, ((a,b,c),normal) in enumerate(zip(triangles,normals)):
            new = (values[b]-values[a]).cross(values[c]-values[a])
            if normal.length > 1e-12 and (new.length < normal.length*.15 or new.dot(normal) <= 0):
                bad.add(index)
        return bad

    # 729 个六区域互斥极值组合，比较增强前后新增翻面/退化面。
    checks = 0
    for states in itertools.product((-1,0,1), repeat=6):
        base = [co.copy() for co in coords]
        candidate = [co.copy() for co in coords]
        for (side,region),state in zip(itertools.product(('L','R'),('Inner','Mid','Outer')),states):
            if not state:
                continue
            name = f'Brow_{side}_{region}_{"Up" if state>0 else "Down"}'
            for i,co in enumerate(coords):
                delta = original[name][i]-co
                base[i] += delta
                candidate[i] += delta*gains[name]
        assert not (bad_faces(candidate)-bad_faces(base)), ('增强导致新增翻面/退化',states)
        checks += 1
    for name,gain in gains.items():
        for i,p in enumerate(keys[name].data):
            p.co = coords[i] + (original[name][i]-coords[i])*gain
    print('BROW_GEOMETRY_CHECK',checks,'extreme combinations',gains)

# 保存前检查：镜像精确、其余 Key/Basis 不变、没有无效坐标。
for i, p in enumerate(left.data):
    delta = p.co-coords[i]
    assert (right.data[mirror[i]].co-coords[mirror[i]]-Vector((-delta.x,delta.y,delta.z))).length < 1e-7
for name, values in original.items():
    if name not in removed and name != right.name and name not in gains and not (restored and name == left.name):
        assert all((p.co-v).length == 0 for p,v in zip(keys[name].data,values)), name
for k in keys:
    k.value = 0
    assert all(math.isfinite(v) for p in k.data for v in p.co)
obj.active_shape_key_index = keys.find('Brow_L_Inner_Up')
obj.data.update()
bpy.ops.wm.save_as_mainfile(filepath=str(output))
print('SHAPE_KEY_CHECK_PASSED', {'file':output.name,'created':created,'removed':removed,'keys':len(keys),'vertices':len(coords)})
