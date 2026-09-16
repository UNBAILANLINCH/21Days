# Fonts —— 字体资产

放 TextMesh Pro 的字体资产（`.asset`）与它依赖的字体源文件（`.ttf` / `.otf`），以及许可证。

## 现在有什么

| 文件 | 是什么 |
| --- | --- |
| `Font_NotoSansSC_Regular.otf` | Noto Sans SC Regular 源字体，8.0 MB，简体中文全字库 |
| `Font_NotoSansSC_Regular SDF.asset` | 上面那个字体的 TMP 资产，**Dynamic 图集**，1024×1024 |
| `OFL.txt` | SIL Open Font License 1.1，上面那份字体的许可证，**不要删** |

**中文已经能正常显示了，拉下来就是好的，不需要各自再生成一次。**
接法是 fallback：`TMP Settings` 的 **Fallback Font Assets** 里挂着 `Font_NotoSansSC_Regular SDF`，
默认字体仍是 `LiberationSans SDF`。所以英文数字走 Liberation（字形更规整），
遇到 Liberation 没有的字（中文）自动回落到 Noto Sans SC。界面上的 TMP 组件**不用改 Font Asset**。

## 自动规则

无。字体资产要用 Font Asset Creator（或脚本）生成，不是拖进来就能用。

## 命名

`Font_<字族><字重>`，全英文。例：`Font_NotoSansSC_Regular`、`Font_NotoSansSC_Bold`。
生成出来的 TMP 资产和它的源 `.otf` 放在一起，名字对得上（TMP 会加 ` SDF` 后缀）。

## 要加别的字重 / 别的字体时

1. 下同一家族的源文件放这里，按上面的命名。**许可证一并放进来**（换字族就再放一份对应的）。
2. `Window → TextMeshPro → Font Asset Creator`，**Atlas Population Mode 选 Dynamic**，
   图集 1024×1024，Sampling Point Size 90，Padding 9，Render Mode `SDFAA`。
   中文字库两万多字，**选 Static 会烘出巨大图集并拖慢导入**，这是最容易做错的一步。
3. 生成后：只是想让缺字能兜住 → 加进 `TMP Settings` 的 Fallback 列表；
   想让某个面板整体换字重 → 在那个 TMP 组件上直接指定 Font Asset。

## 注意

- **Dynamic 字体资产会在编辑器里被玩脏**：进一次 Play，用到的字就被烘进 `.asset`，
  文件从 6 KB 涨到 2 MB 上下，`git status` 里冒出来。提交前在字体资产的 Inspector 里
  点 **Clear Dynamic Data**（或右键 `Reset`）再提交，保持基线干净。
  运行时（出包后）只在内存里加字，不写回资产，不影响成品。
- 字体有授权问题。Noto Sans SC 是 SIL OFL 1.1，**可商用、可再分发**，条件是保留 `OFL.txt`。
  **不要用系统自带的微软雅黑 / 黑体** —— 那些不可再分发，商用会出问题。

详见 `docs/artist-guide.md` 第 3 章、`docs/developer-guide.md` 第 11.5 节。
