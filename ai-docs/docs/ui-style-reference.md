# UI 风格参考：从已落地资产创作新界面

本页是 `$ui-style-create` 的事实参考，首次核查 2026-10-20。只覆盖已查看的这几批 UI，不宣称覆盖所有主题。数值在 1920×1080 设计坐标下；“实测”指当前 PNG／预制体／代码或明确的交付设置，“推导”是设计选择，“未确认”需继续取证。美术满意度由开发者验收。

## 证据与适用范围

| 基准 | 已查看成品、资产与记录 | 可复用语言／专属部分 |
| --- | --- | --- |
| 任务查阅 | [成品](ui-quest-20261019/quest-1920x1080.png)、[记录](ui-quest-20261019.md)；`Prefabs/UI/QuestPanelView.prefab`，`Art/Sprites/UI/Quest/Delivery20261019/Slices/` | 浅纸、青色内渐变、折角、低对比纹样；倾斜叠页和主支线字标属于任务 |
| 背包 | [成品](ui-inventory-20261019/inventory-1920x1080.png)、[记录](ui-inventory-20261019.md)；`Prefabs/UI/InventoryPanelView.prefab`，`Art/Sprites/UI/Inventory/Delivery20261019/` | 格子、云形动作钮、选中边框、左右信息分区；八角详情底纹、鸟与浪花属于该底板 |
| 金银行 | [成品](ui-battle-forging-20261020/forging-state0-1920x1080.png)、[记录](ui-battle-forging-20261020/README.md)；`Prefabs/UI/ForgingView.prefab`，`Art/Sprites/UI/Forging/Delivery20261020/` | 标题菱结、青蓝折角与薄荷高光；卦象／阵盘／金属份额和店名字标不是通用元素 |
| Boss | [成品](ui-battle-forging-20261020/battle-state0-1920x1080.png)、同批记录；`Prefabs/UI/BattleView.prefab`，`Art/Sprites/UI/Battle/Delivery20261020/` | 纸边、结纹、焦点轮廓；红生命／红姓名、金醉意、怒气与胜败书法是战斗语义 |
| 气泡／提示 | [阴阳成品](ui-beautify-20261019/ui-1920x1080.png)、[置入记录](ui-beautify-20261019.md)；`DialogueView`／`NotificationView`／`InteractPromptHudView` 预制体，当前小部件参数另记录于下表 | 手撕纸边、结纹、少量云线；阳间浅底深青字，阴间黑底白字红姓名，物资通知深青底，成功通知浅底，不能把全项目硬改浅底 |

上表资产简写前缀均为 `Assets/_Project/`。成品包含隔离夹具，不是正式剧情完成证据；原图与预制体才是接线来源。

## 颜色、透明度与字体（实测）

| 用途 | 值与来源 | 使用边界 |
| --- | --- | --- |
| 主信息 | `#216A79`，任务标题／目标、背包名称；金银行和招式近邻 `#206978` | 主标题、可读操作信息；两值接近但不伪称同一个精确 token |
| 次信息 | `#5C95A1`，任务正文、小任务目标、打造台／怒气消耗 | 说明和次级状态；小字号落在浅纸实色区域，避免直接叠世界 |
| 动作钮 | 普通字 `#195768`，深底聚焦字 `#8CCADB`；`UITextButton.cs` 与背包预制体 | 深底和浅字同时切；disabled 字在该组件默认约 RGBA(.25,.4,.43,.55)，不是只靠 alpha=0 隐藏 |
| 数字 | `#1E3633`；背包数量、战斗角标与工件数 | 字体要分开读：背包数量当前是宋体 Bold 近似 Regular，战斗／工件角标使用黑体 Regular |
| 语义色 | Boss `#D10404`；醉态 `#E2B664`；任务完成附注预制体 RGB(1,.85,.4) | 仅已有语义，不能由“唐风”推导全局金色或用红色指示普通选择 |
| PNG alpha | 背包普通按钮 0–201、聚焦按钮 0–244、普通格 0–247、选中格 0–255；Confirm 空白板 0–255 | 2026-10-20 Pillow 逐像素 extrema；Image 白色 tint 保留源 alpha，不按查看器白底重画、不二次叠正常／选中图 |

可用授权源：`Assets/_Project/Art/Fonts/SourceHanSerifCN/SourceHanSerifCN-Bold.otf` 及同名 SDF，宋体 CN **Bold**；`SourceHanSansCN/SourceHanSansCN-Regular.otf` 及同名 SDF，黑体 CN **Regular**；各目录相邻 OFL 必须保留。现有 NotoSansSC fallback 另是一族，不当宋体同款。见 [Fonts README](../../Assets/_Project/Art/Fonts/README.md)。不下载字重、不改全局材质。

| 层级 | 实际范围与来源 | 新界面选用（推导） |
| --- | --- | --- |
| 大标题 | 任务 50.65／背包 50，交付要求 Heavy 但实用宋体 Bold；Boss名 Bold 43，招式46 | 主标题约46–51，宋体 Bold；不能写成已拥有 Heavy |
| 操作／分区 | 背包动作37／37.18、任务追踪34.63；小任务标题27.36；打造动作50为特例 | 主动作约32–37，区标题约28–32；同屏只建立清楚的主次 |
| 正文 | 任务30（SemiBold 以 Bold 近似）；目标27.79；背包22.08 | 按阅读量取24–30，不让所有文字50 |
| 附注／数量 | 任务20、交互26.58、数量26；按键提示黑体Regular22.53 | 次状态22–26；数字密集或键位用已有黑体Regular |

字号不是字体可见字高：小任务27.36实际字高39.32，原36框曾截字，现44框。标题与行框要用 TMP preferredHeight／成品渲染核对。未建立统一字距／行距规范，不能凭截图补一个“全项目字距”；新界面逐件记录。

## 几何、组件与素材选择

- **实测比例**：背包主底板1674×1019；任务详情1471×1080、页签458×917；它们有固定构图，不能随意九宫格伸缩。普通／聚焦动作底图208×91；格子182×183／183×183；金银行空白标题455×94；Confirm空白板778×477。实际已查看对应原 PNG 与当前预制体。
- **实测关系**：背包格140×140，图标100×70位于顶部，数量在下方；信息按钮固定、长正文独立滚动。图标 RectTransform pivot=(.5,.5) 配 preserveAspect 才居中，不能只改 Sprite pivot；见[居中修正](ui-inventory-20261019.md)。任务详情正文、目标间有分隔，列表和详情各自裁剪与导航；不把装饰做成射线阻挡。
- **推导留白**：新界面先留纸板内缘约60–90的阅读安全带，相关文字／动作间约20–40，分区间约45–70，再按真实字高验收。这些是从已有构图出发的起点，不是已批准的固定网格。
- **边框与九宫格实测**：Confirm板当前 Sprite border 全0，Image Simple。当前任务／背包大板含纹样与角饰，整张保比例；没有证据支持“所有青框都可Sliced”。需要伸缩时先检查边界像素和纹样位置，单独设该新资产边界，不改共享原图导入配置影响其它面板。
- **装饰分配（推导）**：主界面一处大纹样，标题一处细菱结，动作钮少量高光即可。纹样低对比且避开密集文字；卦象只能服务原打造主题。不要重复叠多套鸟、花纹和云条来证明同风格。

可直接引用的无字资源（不复制一套）：

| 用途 | 项目路径（前缀 `Assets/_Project/Art/Sprites/UI/`） | 注意 |
| --- | --- | --- |
| 对称折角浅板 | `Confirm/ui_confirm_panel.png` | 中央淡结纹，无其它店名；保持778:477比例，四角仍在阅读区外 |
| 标题菱结云底 | `Forging/Delivery20261020/主标题底.png` | 455:94，无“金银行”文字；与单独的金银行标题图区分 |
| 动作钮两态 | `Inventory/Delivery20261019/Slices/button-idle.png`、`button-selected.png` | 深底聚焦需浅字；交互通常用UITextButton＋UIButtonFeedback |
| 选项格两态 | 同目录 `cell-idle.png`、`cell-selected.png` | 浅底选中靠薄荷边，不应套深底浅字；持久选择与当前导航焦点是两件事 |
| 分隔 | `Quest/Delivery20261019/Slices/04-divider.png` | 横向细线；不放大成粗边框 |

## 状态、时间与 Canvas

**实测**：任务／背包／打造普通状态都沿 UGUI SpriteSwap／颜色和导航；UIButtonFeedback 80ms缩到0.94，恢复／隐藏／销毁取消。UIConfig 150ms根Fade；任务／背包换项详情160ms OutQuad、unscaled；通知已有200ms alpha与24px上移。没有常驻呼吸。以上见[各批记录](ui-quest-20261019.md)及 `Core/UI/UIButtonFeedback.cs`、`UIView.cs`、`UIConfig.cs`。

**推导**：高频操作首选即时选中与局部160ms反馈，结果可淡入但不能延迟结算；低频仪式展开可以作为备选方案。尚无可比 Profiler 数据，不把“子Canvas／事件更新”描述为已实测降耗。

**实测适配契约**：[architecture §5.6](../../docs/architecture.md#56-ui)，UIService创建四层Canvas、ScaleWithScreenSize、1920×1080、matchHeight=1、SafeAreaFitter。最低1280×720，2560×1080宽屏横向扩展。新View挂现有Panel/Hud层，不另起根Canvas；仅按更新频率分小型子Canvas，不能每个按钮一个。实际状态改变时更新文字／Sprite；动画结束停止更新，长文和空态用真实需求决定。

## 不应沉淀成风格的部分

- 任务／背包鸟边的黄绿色描线、部分原图透明区彩线属于交付现状，非推荐描边规范；不擅自修原图，也不在新图复刻瑕疵。
- 黑灰截图背景常是离屏夹具或调试世界，不能采成全屏最终背景；早期金棕白盒划拳和未配图道具名称是占位。
- Heavy／SemiBold缺失、数字描边材质近似已明确，不能将近似升级为“精确复刻”。
- 空任务已有浅色衬底；背包空态隐藏详情；禁用动作保持拒绝／许可语义。没有证据支持所有空态必须加插画、加载条或统一红色禁用。
- 当前只确认上述主题。酒肆不因设定自动追加酒壶插图、酒量条、倒计时、QTE、打字或新奖励；需求来自 [Tavern guide](modules/tavern/tavern-module-guide.md)。

实作对照与本轮结果：[行酒令](ui-tavern-20261020.md)。
