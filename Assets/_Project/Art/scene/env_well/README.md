# env_well — 水井小景

`Art/scene/` 下的一次美术交付。目录怎么组织、资产怎么命名，见上一级 [`../README.md`](../README.md)。

## 交付内容

美术交付的一整块场景 diorama：水井 `puit`、木桶、铺石地面、泥地、蕨类与高草、芦苇、落叶。
103 个网格 / 34 个材质 / 33 张贴图，无骨骼、无动画。

| 文件 | 是什么 |
| --- | --- |
| `env_well.glb` | **Unity 用的那一份**。Blender 走 PBR 流程导出，材质已经烘成 贴图（BaseColor / Normal / Roughness 等），拖进场景即带材质 |
| `env_well.fbx` | 美术给的对照件，只有几何 + Blender 专属 shader，**工程里导入是灰模**，不用于正式表现 |
| `env_well_colors.txt` | 美术的配色清单（树叶、芦苇、高草丛等各部位的取色），供替换/调色时对照 |

**贴图已从 4096 降到 2048**：原始 `env_well.glb` 是 163 MB，其中水井石材 `pierre` 与木桶 `seau` 的 6 张
4096×4096 PNG 就占 129 MB。两个硬约束都拦它——`docs/artist-guide.md` 第 10 章「不要提交超大贴图，
单张超过 2048×2048 就会被体检报出来」，以及 `.gitattributes` 写明的 GitHub 单文件 100 MB 上限。
降采样只动了那 6 张，网格、材质、其余贴图逐字节未改动，结构校验与原件一致；降完 60.8 MB，最大贴图 2048。
**原始 4K 素材仍保留在美术交付的 rar 里**，将来要更高精度就从那份重出。

**注意**：`.glb` 由 GLTFUtility（`com.siccity.gltfutility`）导入，贴图是**资产内的子资产**，
**不走 Unity 的 Texture Importer** —— 也就是说 Max Size、压缩格式、平台覆盖这些设置在它身上**不生效**，
贴图按源分辨率以未压缩格式进内存。实测：这一份导入后 `Library/Artifacts/20/20128f4910c7aa04b21a3b723ba9ad7d`
是 **403.9 MB**（对得上 33 张贴图按 ARGB32 + mipmap 展开的约 428 MB），而同样内容的 `.fbx` 导入产物只有 16 MB。
真要压，得把贴图拆成独立 PNG 文件、材质改成引用工程内材质，找程序一起做。

**要重出这个 `.glb`（改贴图、换精度）先读一条坑**：GLTFUtility 的 `Importer.GetGLBJson` 是按**字符数**读
JSON 块的，JSON 里只要有一个非 ASCII 字符（中文材质名就算）解析就会失败，报
`JsonReaderException: Additional text encountered after finished reading JSON content`。
重打包时 JSON 必须序列化成纯 ASCII（非 ASCII 走 `\uXXXX` 转义），详见
[`ai-docs/pitfalls.md`](../../../../ai-docs/pitfalls.md) 的「GLTFUtility 按「字符数」读 GLB 的 JSON 块」一条。

**不要放**：`.glb` / `.fbx` 以外的中间产物（`.blend` 源文件、导出临时件）；压缩包（`.rar` / `.zip` / `.7z`）——
Unity 不导入压缩包，改一处就要整包重存一份。

## 在 demo 场景（`Assets/Scenes/SampleScene.unity`）里的实例

2026-10-08 放入，走 Unity MCP，场景由 Unity 自己序列化：

| 项 | 值 |
| --- | --- |
| 物体名 / 父节点 | `Env_Well` → `Environment_Graybox`（与 `Ground`、`Tower`、`MultiLevel` 平级） |
| 类型 | `env_well.glb` 的预制体实例，**保持直链**（美术重导 glb 会直接流到场景里）。碰撞体是实例上的附加组件，没有另做包装预制体 |
| Layer | `Ground`（slot 8） |
| Scale | **`0.09`** —— 缩放后整体 **6.32 × 4.33 × 5.60** 世界单位。水井本体 `puit.021` **1.267 × 0.791 × 1.261**（角色纸片 1.5，井到腰胸之间，符合村口井的实感）；木桶 `puit.096` 0.63 |
| Position | `(31.672, 2.339, 1.539)` —— 让 diorama 自带地面的顶面落在 **y = 4.8882**，与场景 `Ground` 顶面 4.8884 齐平 |
| 占地 | x 24.47～30.79、z −7.93～−2.33，落在 `Ground_East`（x 19～33）上，**不再与东侧 `Ramp_South`（z −1.22 起）重叠** |

**为什么是 0.09 而不是最初的 0.19**：0.19 时井是 2.67 × 1.67，比角色还高一大截，画面里像 14 米的巨物
（源文件按「diorama 陈列尺度」建模：整块 70.2 × 62.2 × 48.1，井本体 14.07 × 14.01 × 8.79）。
按「村口井外径约 1.3 单位、高约 0.8 单位」反推得 0.092，取 **0.09**。

**碰撞体（2026-10-08 补）**——按工程的分层约定：挡人的放 `Obstacle`(10)，可站的放 `Ground`(8)。

| 物体 | 层 | 类型 | 说明 |
| --- | --- | --- | --- |
| `puit.021` 井 | 10 Obstacle | MeshCollider（12,796 面，非凸） | **环形**：射线打到环壁 y≈5.77，从井口穿过去；玩家高度处只在环壁命中 |
| `puit.096` 木桶 | 10 Obstacle | MeshCollider（3,020 面） | |
| `Col_TreeTrunk` | 10 Obstacle | CapsuleCollider（r 0.30 / h 1.0） | 对准实测树干 0.575 × 0.521 @ (25.922, −5.665)。**整棵树不能套碰撞体**：`FP_Tree_A_001` 有 158,382 面，且 20% 高度处就散成 1.27 × 2.69 的枝展 |
| `立方体.260` 土台 | 8 Ground | **未加** | 网格法线朝向混着（73 朝上 / 71 朝下），背面剔除一开查询就落空，命中点也只在 y≈4.91（与场景地面 4.890 齐平）。加了会让贴地时高时低地跳，所以不加，站场景地面即可 |

整块 diorama 合计 **238,028 三角面**（树占 158k），对手机 2.5D 偏重，验收时留意。

## 材质修复：21 个材质在 URP 下渲染不出来（2026-10-08 已修，待美术复核）

放进 `SampleScene` 后实拍：草丛一片洋红兼死黑。诊断与处理如下。

**根因**：`.glb` 的 34 个材质里 **21 个根本没有 `pbrMetallicRoughness` 块** ——
`Eevee Grass B Gold.003` 与 `Eevee Tall Grass C White` 及其 `.001`～`.020` 副本。
`GLTFUtility/Scripts/Spec/GLTFMaterial.cs:52` 对这类材质的兜底是
`new Material(Shader.Find("Standard"))`，**Built-in 的 Standard shader 在 URP 下渲染不出来**。
这条路径**不读**导入设置里的 shaderOverrides，把 overrides 指到 `Universal Render Pipeline/Lit` 也修不到。
**已确认这不是重打包造成的**：原始 4K 文件里同样缺这 21 个（Blender 用 Eevee 专属节点，导出器映射不出来）。

**怎么修的**（改的是 `.glb` 的 JSON 块，BIN 与 33 张贴图逐字节未动）：

| 对象 | 依据 | 处理 |
| --- | --- | --- |
| `Eevee Grass B Gold.003` | 同族 `…Gold.001` 幸存，UV 窗口 `(0.251,0.651,0.446,0.585)` **逐字节相同**，其贴图 `cao2` 在该窗口平均色 (106,98,75) 是米色芦苇 | 照抄同族的 `cao2` 贴图 + `BLEND` |
| `Eevee Tall Grass C White` ×20 | 同族 `….002` 用的是 `yezi2`，但该贴图 RGB **恒为 0**（纯黑遮罩，只有 alpha 有形状），参照不可用 | 退回 `env_well_colors.txt` 的「后面雷霆高草丛」`#2C1E09`/`#66360C`，取中值 **`#492A0A`** 纯色；这 90 个网格是**真实叶片几何**（280 顶点/320 三角，1.46×2.57×0.48 有厚度）、原 `alphaMode` 就是 `OPAQUE`，不需要贴图 |
| `Plant_0.001`（`FP_Tropical_Plants_012`） | `baseColorFactor` 是纯黑且无贴图，与上面同一类毛病 | 按清单「最前面神秘一坨（桶隔壁那个）」`#574F33`/`#998C6C` 取中值 **`#786D4F`** |

修复后 Unity 侧实测：**Built-in `Standard` 材质 0 个**（28 个 `GLTFUtility/URP/Standard (Metallic)` +
6 个 `…Transparent (Metallic)`），洋红与死黑消失。
（上表就是这次改动的完整记录；改 `.glb` 的一次性脚本没有进仓库，要留档说一声。）

### 补丁 2：给那 21 个材质贴草地贴图（2026-10-08 同日）

纯色只是止血。美术侧提供了 `ai-shared/attachments/blender/env-well-grass/texture-grass-basecolor.jpg`
（2048²，无缝草地）：先在 Blender 里把 21 个 `Eevee Tall Grass C White.*` 的 Base Color
接成 `Image Texture × Color Attribute`（顶点色在 glb 里全是白的，乘不乘一样），
**接法和另外 5 个能正常工作的 `Eevee Grass *` 材质完全一致**；
再把同一张 JPEG 嵌进 `.glb` 的 21 个材质（新增第 34 张 image / 第 37 张 texture，`baseColorFactor` 归白）。

同一目录的 `texture-leaves-basecolor.jpg` 与树冠在用的 `Shiny_Stylized_Leaves_001_Base_Color`
**逐像素同款**（平均色 (0.041, 0.418, 0.367) 一致），所以叶子没动。

实测：21 个材质都拿到 `texture33 2048×2048 RGB24`，草从纯色变成绿色带纹理。
文件 62.1 MB（+1.3 MB，就是那张 JPEG）。

### 补丁 3：6 个 BLEND 卡片材质改成 MASK（同日，修「黑方片」）

草贴上之后近看还有**黑底方片**：草卡/蕨卡的空区渲染成实心黑。

**实验定位**：把这 6 个透明材质的渲染器 `enabled = false` 后黑方片全部消失，同时露出正常的绿色草 ——
所以黑方片就是这 6 个材质。它们的贴图 alpha 是好的（图集左上 8×8 平均 alpha = 0.000，
`ARGB32`），但 **GLTFUtility 的 `Standard Transparent (Metallic)` 是 ShaderGraph 资产，
实测在 URP 下没有把贴图 alpha 抠出来**，于是黑 RGB 被实心画了出来。

**处理**：把这 6 个材质的 `alphaMode` 由 `BLEND` 改成 **`MASK`（`alphaCutoff: 0.5`）**。
依据 `GLTFMaterial.cs:98-99,138`：`MASK` 走**不透明 shader + `_AlphaCutoff`**，`BLEND` 才走透明 shader；
草卡/蕨卡本来也适合抠图（无排序问题、无黑边）。改完黑方片消失。

### 补丁 4：最后一个无贴图材质 `Plant_0.001`（同日）

`Plant_0.001`（`FP_Tropical_Plants_012`，第 4 大件）是 32 个材质里**唯一没有贴图**的 ——
原 glb 里它的 `baseColorFactor` 是纯黑且无贴图，和那 21 个草一样是「Blender 专属节点丢失」。
上一步先用配色清单的 `#786D4F` 纯色止血。

**选贴图时被数据否掉了一次**：它自己的 UV 窗口是 `(0.48, 0.638, 0.712, 0.907)`，实测该窗口在
`yezi` / `yezi2` / `cao1` / `cao2` **四张图集里不透明占比都是 0.0%** —— 贴上去这株植物会整株消失。
窗口里有内容的只有三张：`Shiny_Stylized_Leaves`（#0A6B5E）、`Grass_Base_Color`（#628439）、
`wood_base color`（#3D2E21）。选了 **`Shiny_Stylized_Leaves`** —— 它本身就是大叶形贴图，和「热带植物」
语义最对，色调也和旁边那棵树一致。

**这是重建不是还原**：UV 窗口与源图集对不上，叶片形状不会精确吻合，只是色调和质感合理。
真正修好要美术重导。

### 补丁 5：井与桶的真 UV 换到 TEXCOORD_0（同日，修「光滑棕色」）

Blender 里井壁有石砖、桶有木箍，进 Unity 后这两件各是一坨光滑棕色。

**根因**：这两个网格有**两套 UV** —— `UVMap` 全是 `(0,0)`（退化），`UVMap.001` 才是真 UV。
Blender 材质用 `UV Map` 节点显式指定了 `UVMap.001`，所以正常；glTF 靠 `texCoord` 序号选 UV 集，
Blender 也**确实写对了** `"texCoord": 1`，但 **GLTFUtility 解析了 `texCoord` 却从不使用**
（`GLTFMaterial.cs:259` 定义后无引用，`KHR_texture_transform.cs:37` 是 `// TODO texCoord`），
导入时永远按 UV0 采样；而 Unity 侧 `mesh.uv = TEXCOORD_0`、`mesh.uv2 = TEXCOORD_1`
（`GLTFMesh.cs:287-288`），于是采到的是那套退化 UV，整个网格只命中贴图上的**一个像素**。

**处理**：把 2 个 primitive 的 `TEXCOORD_0` 指向原来 `TEXCOORD_1` 的 accessor、删掉 `TEXCOORD_1`，
再把这两个材质所有贴图引用的 `texCoord` 归 0（让规范读取器一致）。只改 JSON 索引，不动 BIN 与几何。

实测：`puit.021` 的 `mesh.uv` 变成 `u[0, 0.994] v[0, 1]`、`puit.096` 变成 `u[0, 1] v[0, 0.92]`，
石砖与木箍正常显示。

**影响范围只有这两个材质** —— 全库 34 个材质里只有 `pierre`、`seau` 带 `texCoord: 1`，其余 32 个本来就是 0。

### 仍待美术处理（我没动，也动不了）：

- **`yezi2` 是纯黑遮罩**，不是 base color，`Eevee Tall Grass C White.002` 原本就指着它。
- **UV 环绕拉丝**：`Wood_material` 与 `闪亮风格叶子 001` 的 UV 范围是
  `(-0.963, 2.964, -599.839, 1.966)`（V 到 −600），采样时环绕，树冠附近出现拉丝黑块。
- **树冠本来就是青色**：`Shiny_Stylized_Leaves_001_Base_Color` 实测平均 `#0A6A5D`，
  是贴图本身的颜色，不是色域问题。

**重导 `.glb` 时的硬要求**：JSON 块必须保持纯 ASCII（非 ASCII 走 `\uXXXX` 转义），
否则 GLTFUtility 按字符数读 JSON 会失败，见 [`ai-docs/pitfalls.md`](../../../../ai-docs/pitfalls.md)
的「GLTFUtility 按「字符数」读 GLB 的 JSON 块」一条。
