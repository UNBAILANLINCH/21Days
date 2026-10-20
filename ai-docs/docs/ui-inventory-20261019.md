# 背包与关键道具 UI 置入（2026-10-19）

## 范围、入口与保留行为

接续[任务界面](ui-quest-20261019.md)，不覆盖前批结果。实际输入资产 `GameInput.inputactions` 的 `Gameplay/Inventory` 绑定 B／手柄 RB；入口脚本注释写 I，不能据注释改键。链路仍是 `InventoryHotkeyPresenter` → `InventoryPanelController.OpenAsync` → `IUIService` 的同名 Addressables 地址 `InventoryPanelView`。面板开时暂停世界并关 Gameplay，返回／Esc 释放会话；背包键只负责打开。

保留全部／物品／线索、真实数量、排序、选中、使用与合成的服务事件。使用与合成已有生产实现，不新增规则；缺料合成仍允许点击以显示拒绝原因，没有配方／执行者按原逻辑禁用。名簿是文书，归线索筛选，不因素材统称“关键道具”而改分类。

## 归档与绑定

只枚举与复制 `.png`，保留外部原件；`.psd`／`.psb` 按扩展名不区分大小写排除，没有导入或引用外部源工程。22 张 PNG 原样归档到 `Assets/_Project/Art/Sprites/UI/Inventory/Delivery20261019/`，复制时逐个比较 SHA-256 相等。[全部交付图预览](ui-inventory-20261019/source-contact.png)。不把参考截图绑定到游戏。

| 源 PNG | 归档相对路径 | 实际用途 |
| --- | --- | --- |
| 按键底未选中 | Slices/button-idle | 使用／合成普通底图 |
| 选中按键底 | Slices/button-selected | 动作按钮悬停／导航选中／按压，SpriteSwap |
| 背包分类未选中、背包分类选中 | Slices/filter-idle、filter-selected | 三个分类状态；字保持 TMP |
| 底框 | Slices/panel | 1674×1019 整张半透明底板，原比例 |
| 返回 | Slices/return | 固定书法与原关闭按钮 |
| 物品格子、物品格子选中 | Slices/cell-idle、cell-selected | 网格普通／当前选中底图，避免叠加两层 alpha |
| 滑轨短、滑轨长 | Slices/scroll-handle、scroll-track | 列表滚动条；详情滚动条复用短柄 |
| 道具名称信息、道具信息文字信息 | References/name-type、description-type | 字号、字重、颜色参考 |
| 格子内数量文字信息 | References/count-type | 数量与描边参考 |
| 使用按键文字、合成按键文字 | References/use-type、craft-type | 按钮 TMP 样式参考 |
| 位置参考 | References/layout | 1920×1080 构图参考 |
| 名簿 | Icons/name-register | **确定：1601 名簿**，网格与详情使用同一图 |
| 福肉、归业簿、卷轴、龙牙、赈粮册 | Icons/blessed-meat、return-register、scroll、dragon-tooth、relief-register | **仅归档预览，不绑定**，用户明确选择 |

正式表只有名簿可确定；龙牙候选为 1301“牙”，设计文档仍把是否同物列为待定；其余四项没有对应表项。本批不新建道具、不把归业簿或赈粮册误接到名簿。未配图项显示真实名称，品质颜色仍可辨别，完整名称在详情显示。

6 张参考图不透明；10 张切片与 6 张图标都有透明区域。底板有 1,408,449 个半透明像素，普通按钮最大 alpha 201，选中按钮最大 244；白色 Image tint 保留纹理 alpha。切片／图标用 Sprite Single、Bilinear、Clamp、无 mipmap、未压缩；参考图用普通纹理。

## 样式与结构

基准沿用项目 1920×1080、高度缩放、安全区，最低 1280×720、宽屏 2560×1080。左侧五列网格采用独立 ScrollRect／RectMask2D，右侧详情有独立阅读滚动区，名称、类别、描述和配方说明一起参与长文排版；按钮固定在底部。列表与详情各一个子 Canvas，没有逐格增加 Canvas。

实查现有字体与许可证，使用项目思源宋体 CN Bold；名称 Heavy 50 和数量 Regular 26 用 Bold 近似，明确不是同字重。描述 Bold 22.08、按钮 Bold 37／37.18、主文字 #216A79，普通动作字 #195768、聚焦字 #8CCADB。数量 #1E3633、青色细描边近似参考 1px；极大数量缩至最低 12，不截断真实值。未知图标格中名称省略显示，详情保留完整名称。两个动作普通态都采用深色字保证浅底可读，聚焦深底使用浅青字。

`UIScrollFocus` 从任务专用组件提升到 Core/UI，两块界面共用同一导航滚入逻辑，不新增模块相互依赖。`UITextButton` 只扩展标准 Button 的 TMP 状态字色；SpriteSwap、点击、导航仍走 UGUI，按压复用 `UIButtonFeedback`。所有文字与装饰关闭 Raycast，选中格／分类底图需要接收指针并交给父 Button，阅读与列表透明命中区保持实际射线可用。列表池与布局只随数据变化刷新。

## 恰好三版动效方案

只有 A 落地，B／C 仅文字；时长为设计值。

| 方案 | 触发、阅读动线与节奏 | 反馈、退场及生命周期 | 成本 |
| --- | --- | --- | --- |
| **A 格中递读（推荐）** | 打开沿用既有整页 UITransition；格子／分类立即切状态，从左侧选择移向右侧图与名称。仅物品编号变化时详情 160ms OutQuad 淡入，同项数量与可用性变化不重播。 | 悬停／导航立即换深色底与浅青字；按压沿用 80ms 缩至 0.94，释放归位。由玩家返回／Esc 退场，无自动停留；重触发取消旧淡入，禁用／关闭恢复 alpha=1，unscaled。 | 一个详情 alpha；结束即停，无持续呼吸或逐帧排版。适合高频背包查阅。 |
| B 轻取回弹 | 选中图标 180ms 从 0.96 回稳，名称延后 40ms、描述延后 70ms 淡入，总约 280ms；分类即时更新。 | 悬停 100ms 增亮，按压 70ms 收缩／140ms OutBack 恢复；根退场沿用项目过渡。中断还原基准，暂停使用 unscaled，无持续停留。 | 多一个图标缩放与两段文字 alpha，不修改布局。 |
| C 册页展开 | 首次打开详情 320ms 自左向右揭示；图标 120–280ms、名称 220–380ms、正文 300–550ms；连续选择直接切内容。 | 悬停 120ms、按压 80ms，关闭只走根淡出。中断取消序列与遮罩，禁用恢复终值，unscaled；不自动关面板。 | 新增展开遮罩与分段网格变化，成本最高，本批不选。 |

## 验证记录

### 2026-10-20 图标居中与重置入口补充修正

范围是布局和激活状态，不增加动效、图标身份绑定或美术框架。旧动效方案继续沿用 A。

根因核查：`InventoryPanelView` 的池项 `Icon` 与 `DetailIcon` 均为左上 pivot `(0,1)`。UGUI `Image.preserveAspect` 缩短一轴时依照 RectTransform pivot 分配余量，导致扁长图贴上沿；Sprite 自身 pivot 已为中心。九张交付图在 alpha≥32 时的外形包围盒与原图四边仅相差 0–7 像素，透明留白不是主要原因；环形以外轮廓包围盒判断，不按像素面积重心偏移。无需运行时读像素、裁图或改全局导入设置。

八角底纹属于 `Slices/panel.png`，原图约 x1099–1526、y168–594，中心约 `(1312.5,381)`；结合 Paper `(145,-75)` 与 Detail `(1140,-215)`，换算为 Detail 内约 `(317.5,-241)`。名称／类别／说明继续留在原阅读区域。两份预制体经 Unity MCP 保存：格子图标100×70、中心 `(70,-55)`，留在 x20–120、y20–90，和顶部向下94开始的数量区分开；详情图250×210、中心 `(317,-241)`，最下沿346，阅读区始于360，至少留14个布局单位。两处 pivot 均改 `(0.5,0.5)`，原 anchor 及 `preserveAspect=true` 保留。

重置入口唯一资产来源为 `ExplorationHudView.prefab/ControlsRoot/ResetButton`，运行时 Addressables 创建这份 HUD。`OnOpenAsync` 只绑定监听，`SetControlsVisible` 只改父 CanvasGroup，没有重新激活按钮的生成器；暂时停用此节点即可保留恢复能力与底层重置流程，不触碰进度、物资或存档。

全物品核查清单（40 项，绑定状态与名称兜底均在真实入口回放逐项断言）：

| ID | 名称 | 展示 |
| --- | --- | --- |
| 1001 | 铁剑 | 名称兜底 |
| 1002 | 精钢长剑 | 名称兜底 |
| 1003 | 龙鳞护甲 | 名称兜底 |
| 1004 | 治疗药水 | 名称兜底 |
| 1005 | 破旧信笺 | 名称兜底 |
| 1006 | 铜镜碎片 | 名称兜底 |
| 1101 | 普通皮 | 名称兜底 |
| 1102 | 湿皮 | 名称兜底 |
| 1103 | 空白皮 | 名称兜底 |
| 1201 | 查勘使面具 | 名称兜底 |
| 1202 | 泾龙面具 | 名称兜底 |
| 1301 | 牙 | 名称兜底，不擅自绑定龙牙图 |
| 1401 | 水痕 | 名称兜底 |
| 1501 | 泾龙丹 | 名称兜底 |
| 1601 | 名簿 | name-register，格子／详情共用 |
| 1701 | 进入废弃官署的钥匙 | 名称兜底 |
| 1801 | 行头三样 | 名称兜底 |
| 1802 | 黑色长衫 | 名称兜底 |
| 1803 | 金襟扣 | gold-lapel-clasp，格子／详情共用 |
| 1804 | 金带钩 | gold-belt-hook，格子／详情共用 |
| 1805 | 金环扣 | gold-ring-clasp，格子／详情共用 |
| 1901 | 学徒皮 | 名称兜底 |
| 1902 | 都知皮 | 名称兜底 |
| 1903 | 乐正皮 | 名称兜底 |
| 1904 | 顾客甲皮 | 名称兜底 |
| 1905 | 顾客乙皮 | 名称兜底 |
| 1906 | 顾客丙皮 | 名称兜底 |
| 1907 | 官证皮 | 名称兜底 |
| 1908 | 金银匠皮 | 名称兜底 |
| 1909 | 酒客皮 | 名称兜底 |
| 2001 | 活力药剂 | 名称兜底 |
| 2002 | 狂怒结晶 | 名称兜底 |
| 2003 | 铁壁护符 | 名称兜底 |
| 2004 | 锐锋油膏 | 名称兜底 |
| 2005 | 浴血药剂 | 名称兜底 |
| 2006 | 大瓶生命药剂 | 名称兜底 |
| 2007 | 瞬怒晶石 | 名称兜底 |
| 2008 | 坚韧斗篷 | 名称兜底 |
| 2009 | 迅击粉末 | 名称兜底 |
| 2010 | 固本甘露 | 名称兜底 |

额外五张交付图（福肉、归业簿、赈粮册、卷轴、龙牙）仅在隔离预制体实例中预览，不更改正式映射。物品显示入口查到 `InventoryPanelController` → `SetList`／`SetDetail`；战斗物品槽目前显示文字，未引用这四张背包图。未新增运行时代码、每帧扫描或像素读回，不修改 Sprite 原图／导入／meta。

本次验证（不借用旧批次结果）：

- 手动自检脚本 [verify-inventory-centering.txt](../../Tools/verify-inventory-centering.txt) 复用实际预制体，在不保存的临时场景生成九图×三种分辨率（1920×1080、1280×720、2560×1080）27张截图；全部实际 UGUI 网格中心、等比显示、1／999／2147483647数量不溢出检查通过，空列表隐藏详情。已查看[九图详情总览](ui-inventory-20261019/centering-contact.png)及多分辨率代表图。纵向卷轴、圆环、扁长双瓣、长钩、不对称牙形与册页均完整显示；环形以外边界为中心。
- EditMode **91/91通过**，失败／跳过0，作业 `3e755884214049ea9783dba6de2e220d`。
- PlayMode **2/2通过**，作业 `8d36cc78c5a34129a86a0d2ce99aa207`，报告 `Logs/verify/inventory/20261020-015913/report.md`。覆盖原有使用／合成、空图兜底、分类／滚动／池项复用、切换选择、空背包、最大数量、长文本、返回／Esc、暂停与动效收尾；新增用例逐项核对40件正式物品与4个真实绑定图标，首次启动及不调用重置的探索状态重进后按钮仍停用、物品数量保留。
- 随后补充按钮原位置的 `EventSystem.RaycastAll` 不命中断言，再跑该用例 **1/1通过**，作业 `6756feb1780546908173ff8825a7fd25`，报告 `Logs/verify/inventory/20261020-020142/report.md`。两个成功批次均为0失败检查点、0条运行时异常；这不等于所有Warning都清零。
- 初次运行请求在测试代码笔误修复前未能初始化，0项执行、超时结束，不算通过。取消该请求曾被自动审批以“可能中断另一聊天回放”拒绝；没有重试取消或绕过审批，等待其超时后才编译重跑。之后编译完成、控制台Error0、项目lint与源文件空白检查通过。
- 收尾时恢复SampleScene，实际读回 `Dirty=false`、非Play、无测试、节奏x1。临时测试场景已由测试框架自动清除。三份动态TMP仅通过Unity API清缓存及孤立图集，源字体、材质、faceInfo、atlas宽高／Padding／渲染与填充模式逐项比较不变，资产6404／6329／6337 bytes；不更改字体方案。保留其他会话修改，不提交或推送。

| 对照 | 截图 |
| --- | --- |
| 金环扣修正前／后（同一隔离夹具） | [前](ui-inventory-20261019/centering-before-gold-ring-clasp-1920x1080.png)／[后](ui-inventory-20261019/centering-after-gold-ring-clasp-1920x1080.png) |
| 三件金饰的格子与中文详情（真实入口） | [金襟扣](ui-inventory-20261019/centering-runtime-1803.png)／[金带钩](ui-inventory-20261019/centering-runtime-1804.png)／[金环扣](ui-inventory-20261019/centering-runtime-1805.png) |
| 名簿（真实入口） | [1601](ui-inventory-20261019/centering-runtime-1601.png) |
| 1280×720扁长图／2560×1080纵向图 | [双瓣](ui-inventory-20261019/centering-after-gold-lapel-clasp-1280x720.png)／[卷轴](ui-inventory-20261019/centering-after-scroll-2560x1080.png) |
| 首次启动／重进场景，按钮隐藏且射线不命中 | [首次](ui-inventory-20261019/centering-runtime-reset-hidden.png)／[重进](ui-inventory-20261019/centering-runtime-reentry.png) |

三种分辨率为隔离预制体布局检查，真实Boot入口及探索重进回放使用1920×1080。性能未实测，不报告GC、批次或耗时改善；未来换成带大量透明留白的新图时需重新核对可见边界。视觉表现仍由用户在Game视图／截图中确认。

文档／配置健康检查已实际运行。交接后的第一次完整 `gc_scan` 曾因接续PlayMode作业产生的字体缓存和临时场景返回1，本批未中断对方测试。金银行／Boss聊天完成回放并释放编辑器后，本聊天独立复跑完整 `gc_scan`，exit 0：Markdown链接、模块登记、hooks自测与工程静态不变量全部通过；同时独立检查无 `InitTestScene` 残留，三份字体资产6404／6329／6337 bytes。源文件空白检查通过；此前全局检查边界已补齐。

### 名簿越框修正

用户截图指出名簿上方两角越过八边形底纹，原详情图 340×300、位置 (140,-35) 偏大且偏高。通过 Unity MCP 将 `DetailIcon` 改为 280×250、位置 (170,-85)，保持图片比例与文字位置。重新渲染并查看三种分辨率，名簿已完整落在八边形内，右上角鸟形装饰仍按原参考图越外框。本次只调整显示矩形，没有改代码或重跑整套业务测试；下方真实入口截图、五态按钮和 GIF 保留此前验证批次，当前图标布局以三种分辨率预览为准。

22 张查看、尺寸／alpha 核对、原样归档、SHA-256 与 PNG／meta 配对均通过。预制体通过 Unity MCP 修改，实读图标映射仅一项 1601；本批交付区无 PSD／PSB。递归依赖里只有 TMP 包原有的两个编辑器 Gizmo PSD 图标，没有本次外部源工程；未为此改第三方包。旧 InventoryShowcase 只调整列表层级路径，使用／合成断言保留，本轮没有声称旧两例通过。

- EditMode：Inventory、Loot、Quest 共 **284/284 通过**，失败／跳过均为 0；作业 `cca1bfff873149d0816769a9d224faa6`。第一次测试桥初始化超时，没有采纳该次为通过，重跑取得完整结果。
- PlayMode：背包和任务美术回放串行 **2/2 通过**，作业 `dd5fd567cc414b7688813232fa3b5412`；共享焦点组件后任务入口回归通过，报告 `Logs/verify/quest/20261019-231310/report.md`。
- 背包最终回放 **1/1 通过，16 个检查点、0 失败、0 条运行时异常**，作业 `a17b70a8d47e443db6423cf972e23c89`，报告 `Logs/verify/inventory/20261019-231741/report.md`。覆盖真实 B／RB 动作、手柄从分类进入网格、暂停与 Esc／返回恢复、1601 图标／禁用按钮、缺料不扣物、成功合成扣料与产物入包、已有产物拒绝、使用药水扣数与列表同步、多页焦点滚入、最大 int 数量、线索筛选、长标题／描述阅读与实际滚轮、中途禁用和重开动画。物品是回放经 Loot 公开口发放的夹具，不冒称正式剧情奖励已配置。

首轮回放暴露选中底图未接射线：补料后再次点击选中格失败。已修正选中格／分类底图的 Raycast，并让检查确认首个命中属于目标 Button、再走父级点击事件；保持真实命中要求，重跑通过。此前刷新期间读取到的输入释放与 NullReference 记录未在随后重新编译及完整回放中复现，不能据此确定原始根因。既有场景调试面板和透过原 alpha 的名字未改动，报告中的运行时异常计数不等于“没有任何警告”。

游戏内截图：[空背包](ui-inventory-20261019/runtime-empty.png)、[名簿与禁用按钮](ui-inventory-20261019/runtime-name-register.png)、[末项与最大数量](ui-inventory-20261019/runtime-scroll.png)、[线索筛选](ui-inventory-20261019/runtime-filter.png)、[长标题／描述](ui-inventory-20261019/runtime-long.png)。[选择动效 GIF](ui-inventory-20261019/selection-motion.gif) 来自 23:13 成功批次的 13 张连续采样，跨度 655ms，末帧额外停留 800ms；之后仅调整回放导航检查和截图等待，运行时动效未变。截图读回影响间隔，不能当性能测量。

隔离预制体渲染并查看：[1920×1080](ui-inventory-20261019/inventory-1920x1080.png)、[1280×720](ui-inventory-20261019/inventory-1280x720.png)、[2560×1080](ui-inventory-20261019/inventory-2560x1080.png)。遮罩右侧列表显示／隐藏探针分别比较 50,240／33,088／50,240 像素，差异均为 0。真实入口回放为 1920×1080，另外两种为隔离布局检查。

动作按钮已渲染并查看 [Normal](ui-inventory-20261019/inventory-button-Normal.png)、[Highlighted](ui-inventory-20261019/inventory-button-Highlighted.png)、[Selected](ui-inventory-20261019/inventory-button-Selected.png)、[Pressed](ui-inventory-20261019/inventory-button-Pressed.png)、[Disabled](ui-inventory-20261019/inventory-button-Disabled.png)，附带选中 Sprite、正常／聚焦／禁用字色与 0.94 按压缩放终值断言。静态 lint 与代码／文档空白检查通过。性能未实测，不宣称 GC、批次或重建改善。视觉验收由用户在 Game 视图／截图确认。

最终收尾：完整 gc_scan 通过，控制台 Error 为 0；编辑器非 Play、无编译／测试任务，恢复干净 SampleScene 与原 GameView 尺寸选项。两份动态字体缓存经 Unity API 清理，源字体、材质与图集配置逐项比较不变，资产为 6404／6329 bytes；无遗留 InitTestScene。ProjectSettings 仍只有本批前已有的三份改动，未提交或推送。
