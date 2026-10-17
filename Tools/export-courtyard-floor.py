"""源地板集合禁止渲染但视口可见；补导出这两块几何以匹配用户截图。"""
import bpy
import json
import sys
from pathlib import Path

folder = Path(sys.argv[sys.argv.index('--') + 1])
selected = list(bpy.context.selected_objects)
active = bpy.context.view_layer.objects.active
names = ['立方体.009', '立方体.001']
try:
    for obj in selected:
        obj.select_set(False)
    for name in names:
        obj = bpy.data.objects[name]
        assert obj.visible_get() and len(obj.data.polygons) > 0
        obj.select_set(True)
    path = folder / 'Courtyard_44.glb'
    result = bpy.ops.export_scene.gltf(filepath=str(path), export_format='GLB',
                                     use_selection=True, use_visible=True, use_renderable=False,
                                     export_apply=True, export_animations=False,
                                     export_skins=False, export_morph=False)
    assert result == {'FINISHED'} and path.stat().st_size > 1000
    manifest = json.loads((folder / 'parts.json').read_text(encoding='utf-8'))
    for part in manifest:
        part['objects'] = [name for name in part['objects'] if name not in names]
    manifest.append(dict(file=path.name, objects=names, bytes=path.stat().st_size))
    (folder / 'parts.json').write_text(json.dumps(manifest, ensure_ascii=False, indent=2), encoding='utf-8')
finally:
    for obj in bpy.context.selected_objects:
        obj.select_set(False)
    for obj in selected:
        obj.select_set(True)
    bpy.context.view_layer.objects.active = active
print('COURTYARD_FLOOR_EXPORT_OK')
