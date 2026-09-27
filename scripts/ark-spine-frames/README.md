# ark-spine-frames：Spine 模型 → 透明 PNG 序列帧

把明日方舟「基建小人」这类 **Spine 3.8.x** 模型（`.skel` + `.atlas` + `.png`）离线渲染成透明 PNG 序列帧，
给 Unity 当**占位角色**用。工程里**不装 spine-unity 运行时**；spine-ts 只在本机当渲染工具，不进包体、不提交。

## 文件

| 文件 | 作用 |
| --- | --- |
| `render.html` | 渲染页：加载 spine-ts 3.8 WebGL 运行时与模型，在透明 canvas 上逐帧绘制；暴露 `loadModel` / `computeBounds` / `setupView` / `renderFrames` |
| `render_frames.py` | 驱动：Playwright 启动本机 Edge（无头），用 `page.route` 把本目录与模型目录喂给页面，逐帧取 PNG 落盘并写 `meta.json` |
| `check_frames.py` | 核对输出（尺寸、alpha、首末帧非空、边缘有无裁切），生成每角色一张 contact sheet |
| `vendor/spine-webgl.js` | spine-ts 运行时，**自行下载，不提交**（`.gitignore` 已忽略） |

## 依赖

- Python 3.10+，`pip install playwright pillow`
- 本机 Microsoft Edge（默认 `--channel msedge`）或 Chrome（`--channel chrome`）；都没有时脚本退回 Playwright 自带 chromium（需先 `python -m playwright install chromium`）
- spine-ts 3.8：
  `curl -L -o scripts/ark-spine-frames/vendor/spine-webgl.js https://raw.githubusercontent.com/EsotericSoftware/spine-runtimes/3.8/spine-ts/build/spine-webgl.js`

## 用法

```bash
# 1. 看模型里有哪些动画、时长
python scripts/ark-spine-frames/render_frames.py --list --model amiya=<模型目录>

# 2. 渲染（--model 可重复；每个目录里放一套 .skel/.atlas/.png）
python scripts/ark-spine-frames/render_frames.py --out <输出根目录> --model amiya=<目录> --model texas=<目录>

# 2b. 游戏里只要待机 + 走路时的精简版（工程里 Characters/Ark 用的就是这一套）
python scripts/ark-spine-frames/render_frames.py --out <输出根目录> --anims Relax,Move --max-height 384 --model amiya=<目录>

# 3. 核对 + contact sheet
python scripts/ark-spine-frames/check_frames.py <输出根目录> --sheets <输出根目录>/_sheets
```

输出目录请放在**仓库外**（例如系统临时目录），挑好的帧再按美术规范导入 `Assets/_Project/Art/Sprites/`。

### 参数

| 参数 | 默认 | 说明 |
| --- | --- | --- |
| `--model 名字=目录` | 必填 | 角色名（用于文件名）与模型目录，可重复 |
| `--out` | — | 输出根目录，每个角色一个子目录 |
| `--fps` | 24 | 采样帧率；走 / 跑建议 24（12 fps 时步态发顿），待机 24 或 12 皆可 |
| `--margin` | 4 | 包围盒四周外扩像素 |
| `--max-height` | 512 | 画布高超过此值时整体缩小，缩放写进 meta |
| `--anims` | 全部 | 只渲这些 Spine **源动画名**（逗号分隔、区分大小写，如 `Relax,Move`）；模型里没有的直接报错。画布与锚点只按选中动画的包围盒并集算，所以比全动画紧凑 |
| `--tex-pma` | `true` | 贴图是否**已预乘 alpha**（见下文 PMA 判定） |
| `--channel` | `msedge` | `msedge` / `chrome` / 空串（自带 chromium） |
| `--headful` | 否 | 显示浏览器窗口，调试用 |
| `--list` | 否 | 只列动画名与时长 |
| `--pma-check 目录` | — | 只把第一个模型 `Relax` 第 1 帧按 `--tex-pma true/false` 各渲一张，用于判定贴图是否预乘 |

## 输出格式

```
<out>/<名字>/chr_<名字>_<状态>_<NN>.png   NN 两位，从 01 起
<out>/<名字>/meta.json
<out>/_summary.json
```

- 状态名：Spine 动画 `Relax` → `idle`，`Move` → `walk`，其余转小写原名（`Sit` → `sit`，`Relax_Idle` → `relax_idle`）。
- 帧数 = `max(1, round(duration × fps))`；第 i 帧取时间 `i / fps`，循环动画最后一帧不重复第一帧。
- **同一角色所有状态共用一个画布与锚点**：对该角色选中的（默认全部）动画全部帧的 `skeleton.getBounds` 取并集，外扩 `margin`；
  画布宽 = 2 × max(|minX|, |maxX|)，骨骼原点 x=0 在水平正中；画布高覆盖 [minY, maxY]。
  原生高度加边距超过 `--max-height` 时整体等比缩小，保证画布高 ≤ 上限。
  最后宽高各向上补到 **4 的倍数**（宽两侧对称补、高只补顶部，原点像素不变），满足 DXT/ETC2/ASTC 块压缩；`--max-height` 取 4 的倍数时仍不超上限。
- `meta.json`：

```json
{
  "name": "amiya", "fps": 24, "scale": 0.915949,
  "frameWidth": 514, "frameHeight": 512,
  "pivot": {"x": 0.5, "y": 0.183594},      // 骨骼原点在画布中的归一化位置，y 以底边为 0
  "pivotPx": {"x": 257, "y": 94},           // 同上，像素，y 自底边向上
  "texturePremultiplied": true,
  "nativeBounds": {"minX": ..., "minY": ..., "maxX": ..., "maxY": ...},  // 骨骼空间、未缩放
  "animations": {
    "idle": {"source": "Relax", "frames": 12, "duration": 1.0, "loop": true},
    "sit":  {"source": "Sit", "frames": 96, "duration": 8.0, "loop": true, "loopNote": "skel 不带循环标记，默认按循环处理"}
  }
}
```

导入 Unity 时把 Sprite 的 Pivot 设为 Custom = `pivot`，所有状态脚底即对齐同一点。

脚本不写 `animations.<状态>.groundSpeed`（Spine 动画本身不带地速）。需要时手工补进 `meta.json`：
`"walk": {..., "groundSpeed": 3}`、`"run": {..., "groundSpeed": 5}`，表示该剪辑按每秒多少单位的移动速度制作；
缺省时生成工具按走 3 / 跑 5 处理（字段说明见 `docs/artist-guide.md` 3.4 节）。

## 透明与 PMA

- canvas 以 `alpha: true, premultipliedAlpha: true, preserveDrawingBuffer: true` 建 WebGL 上下文，每帧 `clearColor(0,0,0,0)`。
- 帧缓冲里一律存**预乘颜色**（混合 `ONE, ONE_MINUS_SRC_ALPHA`），这样输出 PNG 的 alpha 才正确；`toDataURL` 会反预乘成普通 PNG。
- `--tex-pma` 只决定贴图上传时要不要再乘一次 alpha：`true` 原样上传（贴图已预乘），`false` 上传时由 WebGL 预乘（贴图是直通 alpha）。
- 方舟基建小人贴图实测**已预乘**：5 个角色的 atlas PNG 里所有半透明像素都满足 `max(r,g,b) ≤ a`（数万个半透明像素零例外），
  低 alpha 像素的颜色随 alpha 成比例趋近 0。选错成 `false` 时半透明叠加部位（发丝阴影等）被二次预乘，会发暗、出黑线。
  换别的模型来源时先跑一次 `--pma-check` 或同样的统计再定。

## 版权与许可

- **美术素材**：明日方舟角色模型版权归上海鹰角网络（Hypergryph）所有。本工具渲出的帧**仅作内部开发占位**，
  不得进入对外发布的包体或宣传物料，正式美术到位后必须替换。模型文件与渲染输出都不提交进仓库。
- **spine-ts**：受 [Spine Runtimes License](https://esotericsoftware.com/spine-runtimes-license) 约束，使用需持有 Spine 许可。
  本项目只把它当本机离线渲染工具：**不进包体、不提交**（`vendor/` 已被 `.gitignore` 忽略），工程里也不装 spine-unity。

## 已知坑

- 本地 `file://` 打开 `render.html` 会撞 CORS，必须经 `render_frames.py`（`page.route` 喂文件）或本地 HTTP 服务。
- 无头浏览器的 WebGL 走 SwiftShader（启动参数 `--use-angle=swiftshader --enable-unsafe-swiftshader`），不依赖显卡。
- Windows 控制台默认 GBK，脚本已把 stdout 改成 UTF-8；若仍乱码，设 `PYTHONIOENCODING=utf-8`。
- `Sit` 动画角色坐在椅沿、腿垂到原点以下（包围盒 minY 明显为负），`Sleep` 横躺很宽，所以共用画布比 `idle` 单独算要大一圈。
- `Default` 动画时长为 0，只出 1 帧（setup pose）。
