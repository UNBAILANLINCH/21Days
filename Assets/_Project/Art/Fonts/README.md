# Fonts —— 字体资产

放 TextMesh Pro 的字体资产（`.asset`）与它依赖的字体源文件（`.ttf` / `.otf`）。

## 自动规则

无。字体资产要用 Font Asset Creator 生成，不是拖进来就能用。

## 命名

`Font_<字族><字重>`，全英文。例：`Font_NotoSansSC_Regular`、`Font_NotoSansSC_Bold`。
生成出来的 TMP 资产和它的源 `.ttf` 放在一起，名字对得上。

## 注意

- 界面上的中文现在显示成 `□`，因为默认字体 `LiberationSans SDF` 没有中日韩字形。
  上中文界面前要先做一次中文 TMP 字体资产，流程见 `docs/developer-guide.md` 第 11.5 节。
- 中文字库很大，**不要一次把全部字符打进图集**。按常用字表生成，再配一个动态 fallback 兜生僻字。
- 字体有授权问题，商用前确认许可证。开源可商用的有思源黑体 / Noto Sans SC。

详见 `docs/artist-guide.md` 第 3 章。
