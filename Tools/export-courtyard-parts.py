"""完整 glTF 导出已实测耗尽内存；按网格批次导出，保留源场景和选择状态。"""
import bpy
import gc
import inspect
import json
import sys
from pathlib import Path


def release_export_cache():
    # Blender 5.2 导出器把几何和导出设置留在函数缓存中，失败后也须释放。
    sys.last_traceback = sys.last_value = sys.last_exc = None
    for name, module in list(sys.modules.items()):
        if not name.startswith('io_scene_gltf2') or module is None:
            continue
        for function in list(vars(module).values()):
            if not inspect.isfunction(function):
                continue
            while function is not None:
                for key in ('__cache', '__export_settings'):
                    if key in vars(function):
                        delattr(function, key)
                function = getattr(function, '__wrapped__', None)
    gc.collect()


def export_parts(folder):
    folder = Path(folder)
    folder.mkdir(parents=True, exist_ok=True)
    meshes = [obj for obj in bpy.context.scene.objects
              if obj.type == 'MESH' and obj.visible_get() and not obj.hide_render]
    assert meshes, '没有可导出的网格'
    groups = []
    group = []
    count = 0
    for obj in sorted(meshes, key=lambda item: item.name):
        size = len(obj.data.vertices)
        if group and count + size > 500000:
            groups.append(group)
            group, count = [], 0
        group.append(obj)
        count += size
    if group:
        groups.append(group)
    assert sum(map(len, groups)) == len(meshes)
    selected = list(bpy.context.selected_objects)
    active = bpy.context.view_layer.objects.active
    records = []
    try:
        for index, objects in enumerate(groups):
            release_export_cache()
            for obj in bpy.context.selected_objects:
                obj.select_set(False)
            for obj in objects:
                obj.select_set(True)
            destination = folder / f'Courtyard_{index:02d}.glb'
            result = bpy.ops.export_scene.gltf(
                filepath=str(destination), export_format='GLB', use_selection=True,
                use_visible=True, use_renderable=False, export_apply=True,
                export_cameras=False, export_lights=False, export_animations=False,
                export_skins=False, export_morph=False, export_yup=True)
            assert result == {'FINISHED'} and destination.stat().st_size > 1000
            records.append(dict(file=destination.name, objects=[o.name for o in objects],
                                bytes=destination.stat().st_size))
            (folder / 'parts.json').write_text(json.dumps(records, ensure_ascii=False, indent=2), encoding='utf-8')
            print('COURTYARD_PART_OK', index + 1, '/', len(groups), flush=True)
    finally:
        release_export_cache()
        for obj in bpy.context.selected_objects:
            obj.select_set(False)
        for obj in selected:
            obj.select_set(True)
        bpy.context.view_layer.objects.active = active
    assert len(records) == len(groups)
    print('COURTYARD_EXPORT_COMPLETE', len(meshes), len(groups), flush=True)


export_parts(sys.argv[sys.argv.index('--') + 1])
