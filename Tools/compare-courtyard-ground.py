"""复用已有 GLB 读写器，比较局部地表并提取几何；已有工具只支持整包合并。"""
import copy
import hashlib
import importlib.util
import io
import json
import struct
import sys
from pathlib import Path

spec = importlib.util.spec_from_file_location('courtyard_glb', Path(__file__).with_name('merge-courtyard-gltf.py'))
glb = importlib.util.module_from_spec(spec)
spec.loader.exec_module(glb)


def accessor_bytes(stream, document, start, index):
    accessor = document['accessors'][index]
    assert 'sparse' not in accessor
    view = document['bufferViews'][accessor['bufferView']]
    widths = {'SCALAR': 1, 'VEC2': 2, 'VEC3': 3, 'VEC4': 4}
    sizes = {5121: 1, 5123: 2, 5125: 4, 5126: 4}
    width = widths[accessor['type']] * sizes[accessor['componentType']]
    stride = view.get('byteStride', width)
    offset = accessor.get('byteOffset', 0)
    length = (accessor['count'] - 1) * stride + width
    assert stride >= width and offset + length <= view['byteLength']
    stream.seek(start + view.get('byteOffset', 0) + offset)
    data = stream.read(length)
    assert len(data) == length
    return data if stride == width else b''.join(data[i:i + width] for i in range(0, length, stride))


def inspect(path):
    result = {}
    with Path(path).open('rb') as stream:
        document, start, _ = glb.read_header(stream)
        parents = {child: i for i, n in enumerate(document['nodes']) for child in n.get('children', [])}
        for index, node in enumerate(document['nodes']):
            if 'mesh' not in node:
                continue
            primitives = []
            for primitive in document['meshes'][node['mesh']]['primitives']:
                attributes = dict(primitive['attributes'])
                if 'indices' in primitive:
                    attributes['indices'] = primitive['indices']
                primitives.append(dict(material=document['materials'][primitive['material']],
                                       attributes={key: dict(count=document['accessors'][value]['count'],
                                                             sha256=hashlib.sha256(accessor_bytes(stream, document, start, value)).hexdigest(),
                                                             bounds=[document['accessors'][value].get('min'), document['accessors'][value].get('max')])
                                                   for key, value in attributes.items()}))
            name = node['name']
            if name in result:
                name = f'{name}#node{index}'
            ancestors = []
            parent = parents.get(index)
            while parent is not None:
                ancestors.append({k: v for k, v in document['nodes'][parent].items() if k not in ('mesh', 'children')})
                parent = parents.get(parent)
            result[name] = dict(node={k: v for k, v in node.items() if k not in ('mesh', 'children')},
                                ancestors=ancestors, mesh=document['meshes'][node['mesh']].get('name'), primitives=primitives)
    return result


def extract(source, destination, names, bake_transform=False):
    with Path(source).open('rb') as stream:
        document, start, _ = glb.read_header(stream)
        selected = [n for n in document['nodes'] if n.get('name') in names]
        assert len(selected) == len(names) and len(set(names)) == len(names)
        result = dict(asset={'version': '2.0', 'generator': 'Courtyard selected ground geometry'},
                      scene=0, scenes=[{'nodes': list(range(len(selected)))}],
                      nodes=[], meshes=[], materials=[], accessors=[], bufferViews=[], textures=[], images=[], samplers=[])
        if document.get('extensionsUsed'):
            result['extensionsUsed'] = document['extensionsUsed']
        binary = io.BytesIO()
        mappings = {key: {} for key in ('materials', 'textures', 'images', 'samplers')}

        def dependency(table, index):
            if index in mappings[table]:
                return mappings[table][index]
            item = copy.deepcopy(document[table][index])
            if table == 'images':
                view = document['bufferViews'][item['bufferView']]
                stream.seek(start + view.get('byteOffset', 0))
                data = stream.read(view['byteLength'])
                assert len(data) == view['byteLength']
                item['bufferView'] = len(result['bufferViews'])
                result['bufferViews'].append(dict(buffer=0, byteOffset=binary.tell(), byteLength=len(data)))
                binary.write(data)
                binary.write(b'\0' * (-len(data) % 4))
            elif table == 'textures':
                item['source'] = dependency('images', item['source'])
                if 'sampler' in item:
                    item['sampler'] = dependency('samplers', item['sampler'])
            elif table == 'materials':
                def remap_textures(value):
                    if isinstance(value, dict):
                        for key, child in value.items():
                            if key.endswith('Texture') and isinstance(child, dict) and 'index' in child:
                                child['index'] = dependency('textures', child['index'])
                            remap_textures(child)
                remap_textures(item)
            new = len(result[table])
            mappings[table][index] = new
            result[table].append(item)
            return new

        for original in selected:
            assert not original.get('children') and 'skin' not in original
            node = copy.deepcopy(original)
            if bake_transform:
                import numpy as np
                x, y, z, w = node.get('rotation', [0, 0, 0, 1])
                rotation = np.array([[1 - 2 * (y*y + z*z), 2 * (x*y - z*w), 2 * (x*z + y*w)],
                                     [2 * (x*y + z*w), 1 - 2 * (x*x + z*z), 2 * (y*z - x*w)],
                                     [2 * (x*z - y*w), 2 * (y*z + x*w), 1 - 2 * (x*x + y*y)]])
                scale = np.array(node.get('scale', [1, 1, 1]))
                translation = np.array(node.get('translation', [0, 0, 0]))
                assert np.all(scale > 0) and 'matrix' not in node
                for field in ('rotation', 'scale', 'translation'):
                    node.pop(field, None)
            mesh = copy.deepcopy(document['meshes'][node['mesh']])
            node['mesh'] = len(result['meshes'])
            result['nodes'].append(node)
            for primitive in mesh['primitives']:
                assert not primitive.get('targets') and not primitive.get('extensions')
                for key, old in list(primitive['attributes'].items()) + [('indices', primitive['indices'])]:
                    data = accessor_bytes(stream, document, start, old)
                    accessor = copy.deepcopy(document['accessors'][old])
                    if bake_transform and key in ('POSITION', 'NORMAL'):
                        assert accessor['componentType'] == 5126 and accessor['type'] == 'VEC3'
                        values = np.frombuffer(data, '<f4').reshape(-1, 3)
                        transformed = (values * scale) @ rotation.T + translation if key == 'POSITION' else (values / scale) @ rotation.T
                        if key == 'NORMAL':
                            transformed /= np.maximum(np.linalg.norm(transformed, axis=1, keepdims=True), 1e-30)
                        data = transformed.astype('<f4').tobytes()
                        accessor['min'] = transformed.min(axis=0).tolist()
                        accessor['max'] = transformed.max(axis=0).tolist()
                    assert not bake_transform or key != 'TANGENT'
                    accessor.pop('byteOffset', None)
                    accessor['bufferView'] = len(result['bufferViews'])
                    result['bufferViews'].append(dict(buffer=0, byteOffset=binary.tell(), byteLength=len(data)))
                    binary.write(data)
                    binary.write(b'\0' * (-len(data) % 4))
                    new = len(result['accessors'])
                    result['accessors'].append(accessor)
                    if key == 'indices':
                        primitive[key] = new
                    else:
                        primitive['attributes'][key] = new
                primitive['material'] = dependency('materials', primitive['material'])
            result['meshes'].append(mesh)
        glb.write_glb(destination, result, binary)
    verified = inspect(destination)
    expected = inspect(source)
    for name in names:
        for before, after in zip(expected[name]['primitives'], verified[name]['primitives']):
            for key in before['attributes']:
                if not bake_transform or key not in ('POSITION', 'NORMAL'):
                    assert before['attributes'][key] == after['attributes'][key]
    print(json.dumps(dict(extracted=names, bytes=Path(destination).stat().st_size), ensure_ascii=True))


def self_test():
    import tempfile
    with tempfile.TemporaryDirectory() as folder:
        path = Path(folder) / 'source.glb'
        output = Path(folder) / 'selected.glb'
        document = dict(asset={'version': '2.0'}, scene=0, scenes=[{'nodes': [0]}],
                        nodes=[dict(name='test', mesh=0)], materials=[dict(name='ground')],
                        meshes=[dict(primitives=[dict(attributes={'POSITION': 0}, indices=1, material=0)])],
                        accessors=[dict(bufferView=0, componentType=5126, count=3, type='VEC3'),
                                   dict(bufferView=1, componentType=5123, count=3, type='SCALAR')],
                        bufferViews=[dict(buffer=0, byteOffset=0, byteLength=36),
                                     dict(buffer=0, byteOffset=36, byteLength=6)])
        glb.write_glb(path, document, io.BytesIO(struct.pack('<9f3H2x', 0, 0, 0, 1, 0, 0, 0, 1, 0, 0, 1, 2)))
        extract(path, output, ['test'])
        assert inspect(path)['test']['primitives'][0]['attributes'] == inspect(output)['test']['primitives'][0]['attributes']
    print('COURTYARD_GROUND_SELF_TEST_OK')


if __name__ == '__main__':
    if sys.argv[1:] == ['--self-test']:
        self_test()
    elif sys.argv[1] in ('extract', 'extract-baked'):
        extract(sys.argv[2], sys.argv[3], sys.argv[4:], bake_transform=sys.argv[1] == 'extract-baked')
    else:
        old, new, destination = sys.argv[1:]
        old_data, new_data = inspect(old), inspect(new)
        report = {name: dict(old=value, new=new_data.get(name)) for name, value in old_data.items()
                  if any(p['material'].get('name', '').startswith(('草', '石砖')) for p in value['primitives'])}
        Path(destination).parent.mkdir(parents=True, exist_ok=True)
        Path(destination).write_text(json.dumps(report, ensure_ascii=True, indent=2), encoding='utf-8')
        for name, pair in report.items():
            before, after = pair['old'], pair['new']
            changed = []
            if after is not None:
                for i, (a, b) in enumerate(zip(before['primitives'], after['primitives'])):
                    changed.extend(f'{i}:{key}' for key in set(a['attributes']) | set(b['attributes'])
                                   if a['attributes'].get(key) != b['attributes'].get(key))
                    if a['material'] != b['material']:
                        changed.append(f'{i}:material')
            print(json.dumps(dict(name=name, oldMesh=before['mesh'], newMesh=after and after['mesh'],
                                  transformSame=after and before['node'] == after['node'] and before['ancestors'] == after['ancestors'],
                                  slots=[len(before['primitives']), after and len(after['primitives'])], changed=changed), ensure_ascii=True))
