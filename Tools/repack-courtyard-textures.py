"""替换 GLB 内嵌贴图以控制 Unity 导入内存；几何二进制逐字节复制并校验。"""
import hashlib
import importlib.util
import json
import shutil
import sys
import tempfile
from pathlib import Path

spec = importlib.util.spec_from_file_location('courtyard_glb', Path(__file__).with_name('merge-courtyard-gltf.py'))
glb = importlib.util.module_from_spec(spec)
spec.loader.exec_module(glb)
source_path, image_folder, destination = map(Path, sys.argv[1:])
with source_path.open('rb') as source, tempfile.TemporaryFile() as binary:
    document, start, size = glb.read_header(source)
    original_nodes = json.dumps(document['nodes'], sort_keys=True)
    replacements = {image['bufferView']: image_folder / f'image_{index:03d}.png'
                    for index, image in enumerate(document['images'])}
    geometry_before = hashlib.sha256()
    for index, view in enumerate(document['bufferViews']):
        offset, length = view.get('byteOffset', 0), view['byteLength']
        assert 0 <= offset <= offset + length <= size
        destination_offset = binary.tell()
        if index in replacements:
            path = replacements[index]
            assert path.stat().st_size > 16
            with path.open('rb') as image:
                shutil.copyfileobj(image, binary)
            length = path.stat().st_size
        else:
            source.seek(start + offset)
            remaining = length
            while remaining:
                block = source.read(min(remaining, 1024 * 1024))
                assert block
                geometry_before.update(block)
                binary.write(block)
                remaining -= len(block)
        view.update(buffer=0, byteOffset=destination_offset, byteLength=length)
        binary.write(b'\0' * (-length % 4))
    for image in document['images']:
        image['mimeType'] = 'image/png'
    assert json.dumps(document['nodes'], sort_keys=True) == original_nodes
    glb.write_glb(destination, document, binary)
with destination.open('rb') as saved:
    verified, binary_start, _ = glb.read_header(saved)
    geometry_after = hashlib.sha256()
    for index, view in enumerate(verified['bufferViews']):
        if index in replacements:
            continue
        saved.seek(binary_start + view.get('byteOffset', 0))
        remaining = view['byteLength']
        while remaining:
            block = saved.read(min(remaining, 1024 * 1024))
            assert block
            geometry_after.update(block)
            remaining -= len(block)
    assert geometry_before.digest() == geometry_after.digest()
print('COURTYARD_REPACK_OK', destination.stat().st_size, 'geometry_sha256', geometry_before.hexdigest())
