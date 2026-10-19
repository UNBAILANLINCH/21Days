"""Unity 无法直接检查源 blend；后台 Blender 只读输出网格范围，不保存源文件。"""
import bpy
import json
import sys
from pathlib import Path
from mathutils import Vector

output = Path(sys.argv[sys.argv.index('--') + 1])
objects = []
for obj in bpy.context.scene.objects:
    if obj.type != 'MESH' or obj.hide_render or not obj.visible_get():
        continue
    corners = [obj.matrix_world @ Vector(corner) for corner in obj.bound_box]
    lo = [min(p[i] for p in corners) for i in range(3)]
    hi = [max(p[i] for p in corners) for i in range(3)]
    objects.append(dict(name=obj.name, min=lo, max=hi,
                        vertices=len(obj.data.vertices),
                        materials=[m.name if m else None for m in obj.data.materials]))
assert objects, '源场景没有可渲染网格'
assert all(all(a <= b for a, b in zip(o['min'], o['max'])) for o in objects)
output.parent.mkdir(parents=True, exist_ok=True)
output.write_text(json.dumps(dict(version=bpy.app.version_string,
                                 objects=objects), ensure_ascii=False, indent=2), encoding='utf-8')
print('WATER_INSPECT_OK', len(objects), flush=True)
arguments = sys.argv[sys.argv.index('--') + 1:]
if len(arguments) > 1:
    destination = Path(arguments[1]).resolve()
    properties = bpy.ops.export_scene.gltf.get_rna_type().properties.keys()
    settings = dict(filepath=str(destination), export_format='GLB',
                    export_yup=True, export_cameras=False, export_lights=False,
                    use_visible=True, use_renderable=True, export_apply=True,
                    export_animations=False, export_skins=False, export_morph=False)
    settings = {key: value for key, value in settings.items() if key in properties}
    result = bpy.ops.export_scene.gltf(**settings)
    assert result == {'FINISHED'} and destination.stat().st_size > 1000
    print('WATER_EXPORT_OK', destination.stat().st_size, flush=True)
