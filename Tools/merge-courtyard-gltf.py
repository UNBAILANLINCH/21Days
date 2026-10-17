"""完整导出耗尽内存后，将 Blender 分批 GLB 流式去重合并，不改几何与变换。"""
import copy
import hashlib
import json
import shutil
import struct
import sys
import tempfile
from pathlib import Path


def read_header(stream):
    magic, version, length = struct.unpack('<III', stream.read(12))
    assert magic == 0x46546C67 and version == 2
    size, kind = struct.unpack('<II', stream.read(8))
    assert kind == 0x4E4F534A
    document = json.loads(stream.read(size))
    binary_size, kind = struct.unpack('<II', stream.read(8))
    assert kind == 0x004E4942 and stream.tell() + binary_size == length
    return document, stream.tell(), binary_size


def write_glb(destination, document, binary):
    binary.seek(0, 2)
    binary_size = binary.tell()
    document['buffers'] = [{'byteLength': binary_size}]
    # 当前 GLTFUtility 按字符数读取 JSON chunk；转义中文使字节数与字符数一致。
    encoded = json.dumps(document, ensure_ascii=True, separators=(',', ':')).encode('utf-8')
    encoded += b' ' * (-len(encoded) % 4)
    assert binary_size % 4 == 0
    total = 12 + 8 + len(encoded) + 8 + binary_size
    assert total < 2 ** 32
    with open(destination, 'wb') as output:
        output.write(struct.pack('<IIIII', 0x46546C67, 2, total, len(encoded), 0x4E4F534A))
        output.write(encoded)
        output.write(struct.pack('<II', binary_size, 0x004E4942))
        binary.seek(0)
        shutil.copyfileobj(binary, output, 1024 * 1024)


def merge(paths, destination):
    tables = ('bufferViews', 'accessors', 'images', 'samplers', 'textures', 'materials', 'meshes', 'nodes')
    result = {'asset': {'version': '2.0', 'generator': 'Courtyard segmented export'},
              'scene': 0, 'scenes': [{'nodes': []}], **{key: [] for key in tables}}
    indexes = {key: {} for key in tables}
    used_names = {key: set() for key in ('images', 'materials', 'meshes')}
    extensions = set()

    def intern(table, value):
        key = json.dumps(value, sort_keys=True, separators=(',', ':'))
        if key in indexes[table]:
            return indexes[table][key]
        item = copy.deepcopy(value)
        if table in used_names and 'name' in item:
            base = item['name']
            suffix = 1
            while item['name'] in used_names[table]:
                item['name'] = f'{base}_part{suffix}'
                suffix += 1
            used_names[table].add(item['name'])
        index = len(result[table])
        indexes[table][key] = index
        result[table].append(item)
        return index

    with tempfile.TemporaryFile() as binary:
        for path in paths:
            with open(path, 'rb') as source:
                document, binary_start, binary_length = read_header(source)
                assert not any(document.get(key) for key in ('animations', 'skins', 'cameras'))
                extensions.update(document.get('extensionsUsed', []))
                mapping = {}
                mapping['bufferViews'] = []
                for view in document.get('bufferViews', []):
                    offset, size = view.get('byteOffset', 0), view['byteLength']
                    assert view.get('buffer', 0) == 0 and 0 <= offset <= offset + size <= binary_length
                    source.seek(binary_start + offset)
                    digest = hashlib.sha256()
                    remaining = size
                    while remaining:
                        block = source.read(min(remaining, 1024 * 1024))
                        assert block
                        digest.update(block)
                        remaining -= len(block)
                    key = (digest.digest(), size, view.get('byteStride'), view.get('target'))
                    if key not in indexes['bufferViews']:
                        source.seek(binary_start + offset)
                        start = binary.tell()
                        remaining = size
                        while remaining:
                            block = source.read(min(remaining, 1024 * 1024))
                            binary.write(block)
                            remaining -= len(block)
                        binary.write(b'\0' * (-size % 4))
                        item = dict(view, buffer=0, byteOffset=start)
                        indexes['bufferViews'][key] = len(result['bufferViews'])
                        result['bufferViews'].append(item)
                    mapping['bufferViews'].append(indexes['bufferViews'][key])

                for table in ('accessors', 'images', 'samplers', 'textures', 'materials', 'meshes'):
                    mapping[table] = []
                    for original in document.get(table, []):
                        item = copy.deepcopy(original)
                        if table in ('accessors', 'images') and 'bufferView' in item:
                            item['bufferView'] = mapping['bufferViews'][item['bufferView']]
                        assert table != 'accessors' or 'sparse' not in item
                        if table == 'textures':
                            if 'source' in item:
                                item['source'] = mapping['images'][item['source']]
                            if 'sampler' in item:
                                item['sampler'] = mapping['samplers'][item['sampler']]
                        if table == 'materials':
                            def textures(value):
                                if isinstance(value, dict):
                                    for key, child in value.items():
                                        if key.endswith('Texture') and isinstance(child, dict) and 'index' in child:
                                            child['index'] = mapping['textures'][child['index']]
                                        textures(child)
                                elif isinstance(value, list):
                                    for child in value:
                                        textures(child)
                            textures(item)
                        if table == 'meshes':
                            for primitive in item['primitives']:
                                primitive['attributes'] = {k: mapping['accessors'][v] for k, v in primitive['attributes'].items()}
                                if 'indices' in primitive:
                                    primitive['indices'] = mapping['accessors'][primitive['indices']]
                                if 'material' in primitive:
                                    primitive['material'] = mapping['materials'][primitive['material']]
                                assert 'targets' not in primitive and 'extensions' not in primitive
                        mapping[table].append(intern(table, item))

                node_offset = len(result['nodes'])
                for original in document.get('nodes', []):
                    node = copy.deepcopy(original)
                    if 'mesh' in node:
                        node['mesh'] = mapping['meshes'][node['mesh']]
                    if 'children' in node:
                        node['children'] = [index + node_offset for index in node['children']]
                    result['nodes'].append(node)
                scene = document['scenes'][document.get('scene', 0)]
                result['scenes'][0]['nodes'].extend(index + node_offset for index in scene['nodes'])
        if extensions:
            result['extensionsUsed'] = sorted(extensions)
        write_glb(destination, result, binary)
    assert all(0 <= child < len(result['nodes']) for node in result['nodes'] for child in node.get('children', []))
    print(json.dumps(dict(bytes=Path(destination).stat().st_size,
                         mesh_nodes=sum('mesh' in node for node in result['nodes']),
                         meshes=len(result['meshes']), materials=len(result['materials']), images=len(result['images']))))
    return result


def self_test():
    import io
    with tempfile.TemporaryDirectory() as folder:
        a, b, output = [Path(folder) / name for name in ('a.glb', 'b.glb', 'merged.glb')]
        document = dict(asset={'version': '2.0'}, scene=0, scenes=[{'nodes': [0]}],
                        nodes=[{'name': 'triangle', 'mesh': 0}],
                        meshes=[{'name': 'triangle', 'primitives': [{'attributes': {'POSITION': 0}}]}],
                        bufferViews=[{'buffer': 0, 'byteLength': 36}],
                        accessors=[{'bufferView': 0, 'componentType': 5126, 'count': 3, 'type': 'VEC3'}])
        for path in (a, b):
            write_glb(path, document, io.BytesIO(struct.pack('<9f', 0, 0, 0, 1, 0, 0, 0, 1, 0)))
        merged = merge([a, b], output)
        assert len(merged['meshes']) == len(merged['bufferViews']) == 1 and len(merged['nodes']) == 2
        with output.open('rb') as stream:
            _, _, size = read_header(stream)
            assert size == 36
    print('COURTYARD_MERGE_SELF_TEST_OK')


if __name__ == '__main__':
    if sys.argv[1:] == ['--self-test']:
        self_test()
    else:
        folder, destination = map(Path, sys.argv[1:])
        manifest = json.loads((folder / 'parts.json').read_text(encoding='utf-8'))
        expected = json.loads((folder.parent / 'model-inspection.json').read_text(encoding='utf-8'))['objects']
        exported = [name for part in manifest for name in part['objects']]
        assert len(exported) == len(set(exported)) == len(expected)
        assert set(exported) == {obj['name'] for obj in expected}
        merged = merge([folder / part['file'] for part in manifest], destination)
        assert sum('mesh' in node for node in merged['nodes']) == len(expected)
