# Photoshop 导出脚本没有像素断言；在应用前检查透明画布、层序和重组结果。
import json
from pathlib import Path
from PIL import Image, ImageChops

root = Path(__file__).resolve().parents[1] / 'Assets/_Project/Art/Sprites/Characters'
for character, count in [('customers', 17), ('drunk_customer', 14)]:
    folder = root / character
    manifest = json.loads((folder / 'parts.json').read_text(encoding='utf-8-sig'))
    assert len(manifest['layers']) == count
    assert len(list((folder / 'parts').glob('*.png'))) == count
    combined = Image.new('RGBA', (512, 512))
    for layer in sorted(manifest['layers'], key=lambda row: row['sortingOrder']):
        with Image.open(folder / layer['file']) as part:
            assert part.size == (512, 512) and part.mode == 'RGBA'
            assert part.getchannel('A').getextrema() == (0, 255)
            combined = Image.alpha_composite(combined, part)
    with Image.open(folder / 'composite.png') as reference:
        white = Image.new('RGBA', (512, 512), 'white')
        diff = ImageChops.difference(Image.alpha_composite(white, combined).convert('RGB'),
                                    Image.alpha_composite(white, reference).convert('RGB'))
        maximum = max(channel[1] for channel in diff.getextrema())
        assert maximum <= 2, (character, maximum)
    print(f'{character}: {count} parts, transparent 512x512, composite maximum error {maximum}/255')
