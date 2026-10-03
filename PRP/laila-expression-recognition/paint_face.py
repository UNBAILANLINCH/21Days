"""用 Blender 执行，为现有 UV 添加眉毛和唇色并另存模型。

历史制作工具：生成早期程序化贴图，不能重建当前手绘 PNG；勿在最终交付模型上直接运行。

形态制作脚本只负责顶点位移，不能承担材质制作；项目没有现成的脸部上色脚本。
颜色按 Basis 定位后写入 UV，避免表情变化时颜色留在原位置。
"""
from pathlib import Path
import bpy
import numpy as np

obj = bpy.data.objects['Eve']
if bpy.context.object and bpy.context.object.mode != 'OBJECT':
    bpy.ops.object.mode_set(mode='OBJECT')
mesh = obj.data
source = Path(bpy.data.filepath)
output = source.with_name(source.stem + '-painted.blend')
assert not output.exists(), '拒绝覆盖已有上色文件'
coords = np.array([tuple(p.co) for p in mesh.shape_keys.key_blocks[0].data])
uv = mesh.uv_layers.active
assert uv and len(uv.data) == len(mesh.loops)
size = 2048
pixels = np.ones((size,size,4), dtype=np.float32)
coverage = np.zeros((size,size), dtype=bool)


def tint(points):
    x,y,z = np.abs(points[...,0]),points[...,1],points[...,2]
    front = np.clip((-y-.075)/.025,0,1)
    t = np.clip((x-.011)/.065,0,1)
    arch = .064 + .008*np.sin(np.pi*t)
    width = .004*(1-.65*t) + .0008
    ends = np.clip((x-.010)/.004,0,1)*np.clip((.078-x)/.008,0,1)
    brow = np.clip((width-np.abs(z-arch))/.0012,0,1)*ends*front
    hairs = .86 + .14*np.sin(x*6500+z*2400)**2
    brow *= hairs
    rgb = 1-brow[...,None]*(1-np.array((.36,.31,.27)))
    upper = -.043 + .004*np.exp(-((x-.008)/.006)**2)-.010*(x/.040)**2
    lower = -.068 + .014*(x/.040)**2
    ends = np.clip((.042-x)/.006,0,1)
    lip = np.clip((upper-z)/.0018,0,1)*np.clip((z-lower)/.0022,0,1)*ends*front
    rgb *= 1-lip[...,None]*(1-np.array((.94,.71,.74)))
    seam = -.055 + .003*(x/.040)**2
    line = np.exp(-((z-seam)/.0007)**2)*ends*front
    rgb *= 1-line[...,None]*(1-np.array((.64,.45,.43)))
    return rgb


mesh.calc_loop_triangles()
for triangle in mesh.loop_triangles:
    vertices = coords[list(triangle.vertices)]
    tex = np.array([tuple(uv.data[i].uv) for i in triangle.loops])*size-.5
    low = np.maximum(np.floor(tex.min(axis=0)).astype(int),0)
    high = np.minimum(np.ceil(tex.max(axis=0)).astype(int),size-1)
    if np.any(high < low):
        continue
    a,b,c = tex
    denominator = (b[1]-c[1])*(a[0]-c[0])+(c[0]-b[0])*(a[1]-c[1])
    if abs(denominator) < 1e-8:
        continue
    xx,yy = np.meshgrid(np.arange(low[0],high[0]+1),np.arange(low[1],high[1]+1))
    u = ((b[1]-c[1])*(xx-c[0])+(c[0]-b[0])*(yy-c[1]))/denominator
    v = ((c[1]-a[1])*(xx-c[0])+(a[0]-c[0])*(yy-c[1]))/denominator
    w = 1-u-v
    inside = (u>=-1e-5)&(v>=-1e-5)&(w>=-1e-5)
    colors = tint(u[...,None]*vertices[0]+v[...,None]*vertices[1]+w[...,None]*vertices[2])
    # 镜像 UV 可重叠；取较深色，避免背景三角形覆盖已上色区域。
    region = pixels[low[1]:high[1]+1,low[0]:high[0]+1,:3]
    region[inside] = np.minimum(region[inside],colors[inside])
    coverage[low[1]:high[1]+1,low[0]:high[0]+1] |= inside
# 扩展 UV 岛外六像素，避免过滤和 mipmap 在接缝处混入白底。
for _ in range(6):
    grown = coverage.copy()
    for dy,dx in ((1,0),(-1,0),(0,1),(0,-1)):
        valid = np.roll(coverage,(dy,dx),(0,1)) & ~coverage
        pixels[valid] = np.minimum(pixels[valid],np.roll(pixels,(dy,dx),(0,1))[valid])
        grown |= valid
    coverage = grown
assert np.count_nonzero(pixels[...,:3].min(axis=2)<.5)>100, '没有生成可见眉毛'
texture = source.parent.parent/'Assets/_Project/Art/Textures/Laila/T_LailaFace_BrowsLips.png'
image = bpy.data.images.new('Laila_BrowsLips',width=size,height=size,alpha=True)
image.pixels.foreach_set(pixels.ravel())
image.filepath_raw = str(texture)
image.file_format = 'PNG'
image.save()
image.pack()
material = bpy.data.materials.new('Laila_PaintedFace')
material.use_nodes = True
nodes = material.node_tree.nodes
shader = nodes.get('Principled BSDF')
shader.inputs['Roughness'].default_value = .8
texture_node = nodes.new('ShaderNodeTexImage')
texture_node.image = image
multiply = nodes.new('ShaderNodeMixRGB')
multiply.blend_type = 'MULTIPLY'
multiply.inputs[0].default_value = 1
multiply.inputs[1].default_value = (.65,.40,.22,1)
material.node_tree.links.new(texture_node.outputs['Color'],multiply.inputs[2])
material.node_tree.links.new(multiply.outputs['Color'],shader.inputs['Base Color'])
material.diffuse_color = (.65,.40,.22,1)
for i in range(len(mesh.materials)):
    mesh.materials[i] = material
for key in mesh.shape_keys.key_blocks:
    key.value = 0
for screen in bpy.data.screens:
    for area in screen.areas:
        if area.type == 'VIEW_3D':
            area.spaces.active.shading.type = 'MATERIAL'
bpy.ops.wm.save_as_mainfile(filepath=str(output))
print('PAINTED_FACE_SAVED',output.name,texture.name)
