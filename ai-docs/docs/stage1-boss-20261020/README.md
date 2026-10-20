# 阶段一 Boss 与共用皮图（2026-10-20）

本批来源为用户直接交付的16张PNG，无飞书附件token。1号为共用皮图，7号为组合参考，11号为图层顺序参考，余13张为静态拆件。原始字节保留，不重画、不导入PSD/PSB。

## 身份与实际接线

用户明确指定阶段一最终回合制Boss。项目原酒肆主人 `shop_boss` 经 `shop_boss_battle/fight` 使用 `sample_boss` 名册；本批替换 `Main_4.prefab/Actors/shop_boss` 下的视觉子树和该名册的 `stagePrefab`，使用 `Chibi_stage1_boss.prefab`。SampleScene中的原酒肆实例已跟随预制体更新。客栈仅为既有战斗背景，掌柜没有新增挑战。阶段九 `stage9_boss` 不替换，不能因龙形外观推断为钱塘君。

原角色根、交互、划拳、属性、奖励、任务、故事结果键及存档语义均保留。名册显示名去掉“占位”；其它数值不改。世界战场仍从真实目标复制外观。

## 原始文件与图层

源图位于 `Assets/_Project/Art/Sprites/Characters/stage1_boss/parts/`，均为512×512 RGBA。透明区存在任意黑白和彩色RGB，alpha=0即不可见，禁止据图像查看器的背景裁剪。

| 图号 | 文件 | 从后到前序号 |
| --- | --- | --- |
| 2 | 左侧衣服 | 0 |
| 3 | 红丝带 | 1 |
| 12 | 衣服 | 2 |
| 15 | 右衣服下 | 3 |
| 13 | 右衣服上 | 4 |
| 6 | 毛领 | 5 |
| 14 | 珠子左 | 6 |
| 16 | 珠子右 | 7 |
| 10 | 鞋子 | 8 |
| 4 | 胡子 | 9 |
| 5 | 脸 | 10 |
| 8 | 头冠 | 11 |
| 9 | 头冠珠子 | 12 |

13件齐全，无复制镜像补件。原画布叠加与7号参考的可见合成最大通道误差1/255，超3/255差异像素0。散列、alpha边界与帧检查见 [source-check.json](source-check.json)，诊断合成见 [assembled-check.png](assembled-check.png)。

## 动画与重建顺序

复用王录业的专用刚性拆件标定、Unity剪辑采样与 `FramePuppetGenerator`。`Chibi_stage1_boss_parts.prefab` 保存13渲染件、躯干、头、肩肘、胡子、冠珠、双胸饰与丝带关节；源剪辑在同名 `Art/Animations/Characters/stage1_boss/rig/`。

1. `Tools/build-stage1-boss-animation.txt` 首次标定，发现已存在预制体即拒绝覆盖。
2. `Tools/bake-stage1-boss-frames.txt` 从已保存剪辑生成169张640×640透明帧：Idle72、兼容Walk72、Attack25。24fps、PPU320、脚底pivot(0.5,0.05)，可见原角色高约1.6单位。画布安全边用于冠珠和挥袖。
3. `FramePuppetGenerator.Generate`，FrameDirectory指向本角色，TargetHeight=2、Fps=24、DefaultFacesLeft=true、CreateAtlas=false。
4. **生成器会重置新状态速度和循环标记**，必须再执行 `Tools/bind-stage1-boss-art.txt`：attack不循环、状态速度0，由战斗阶段采样；同时维护真实绑定。
5. `Tools/verify-stage1-boss-art.py` 检查源合成、数值序号、数量、透明边界，并打包 [待机预览](idle.gif) 与 [攻击预览](attack.gif)。

Idle为3秒闭合呼吸、胡子与饰件微动，鞋层固定；Attack有回收预备、肩肘挥袖、躯干前倾和延迟随动，末尾复位。兼容Walk复用呼吸，未新增步态或奔跑动画。

Unity采样需完整写位置三轴：只绑Y会把未绑X归零，本批已补齐X/Z常量并重烘焙。源贴图使用无损压缩与alphaIsTransparency；只改导入设置，不改源PNG。预备与出手均抬袖，避免后摆下压入地。

`ChibiPuppet.SetAttackPose` 只选帧，`BattleActor` 的独立unscaled补间与原位移并行；`BattleStage.StrikeAsync`在预备0–0.33、冲刺0.33–0.5、既有ImpactAsync、回收0.5–1间编排。命中仍只调用原onImpact一次，没有AnimationEvent或新增伤害规则。缺attack态的其它外观走原路径；正常结束、取消、停用与清理均停止采样并回Idle。

## 共用皮图

原件 `Assets/_Project/Art/Sprites/Loot/Deliveries/20261020/skin-source.png`（854×1097 RGBA）。用户后续明确：暗杀所得NPC皮因美术产能限制共用此图，内部信息必须不同。InventoryPanelView的itemIcons为1101–1103及1901–1909共12个Skin条目引用同一个Sprite；未覆盖面具。物品表、效果映射、库存和身份不改，12个名称与描述仍互异；格子底部由“皮”改为该皮名称以区分同图物品，详情继续按ID显示。

## 验证状态

- EditMode：392/392通过，失败0、跳过0，job `590fc3e4a58d4b078f3a3dc3841e3f15`。
- 独立PlayMode：1/1通过，job `473abc222c36411e8896cbf8e108d5b3`。时停下Idle推进、普通与重击连续出招、命中帧13、每次onImpact一次、回原位与Idle、预备中取消无命中均通过。
- 真实酒肆Showcase：2/2通过、失败0、跳过0，job `5ee03ee1098e4c37a3d2857dcced933c`，耗时356秒。[报告副本](battle-replay-report.md)共145条记录、失败检查0、运行时异常0；覆盖原入口取皮、划拳、取消、失败重试、连续开战、技能与道具、胜利、掌柜交谈、等待指令时退出标题。三店成果与197阶段仍使用原明确夹具，不代表完整三店难度验收。
- 实际Game截图：[待机](battle-idle.png)、[起势](battle-ready.png)、[预备](battle-windup.png)、[命中](battle-impact.png)、[回收](battle-recover.png)、[战后掌柜](innkeeper-after-battle.png)、[退出标题](exit-title.png)。1920×1080，保留原调试覆盖层；周边原地图缺面暗区不在本批修复范围。
- 共用皮图隔离预览：[学徒皮](skin-1901.png)、[金银匠皮](skin-1908.png)。`Tools/verify-skin-inventory-art.txt`读取正式生成配置与预制体，断言12种Skin、12份互异描述及格子名称无溢出；不改表、不保存夹具。格子名称使用原Tag槽，关闭其原自动字号并移到图标下方，数量区保留。该预览验证UI绑定与排版；真实取皮、穿皮和战斗外观链另由上述回放覆盖。
- C#编译控制台零错误，适用lint与diff空白检查通过；新图片、目录、Rig、预制体与测试.meta齐全，无孤儿meta，无InitTestScene残留。
- gc_scan已执行：文档引用与钩子检查通过，全局不变量仍报告两份已有动态字体缓存（NotoSansSC Regular约2096KB、SourceHanSerifCN Bold约8211KB），未清理共享字体，不把全局扫描记为通过。测试期间扫描曾遇到正在使用的临时测试场景，测试结束后已自动清除，后续扫描仅余字体问题。
- 测试结束恢复SampleScene、非Play、场景未脏、回放倍率x1。检测到 `ProjectSettings/PackageManagerSettings.asset` 两个Unity会话instanceId变化，保留供后续同步窗口核对，未手改或回滚共享设置。

本批主要变更：`stage1_boss`源图／帧／动画／两个预制体，BossRosterConfig的sample_boss外观与显示名、Main_4的shop_boss视觉、InventoryPanelView预制体图标与Tag布局；运行时为ChibiPuppet、BattleActor、BattleStage及InventoryPanelView；测试为Stage1BossAnimationTests与TavernShowcase；另有制作／绑定／校验工具及本文、模块文档、资产映射。本批不操作暂存区；这些路径上原先已有的改动保留。

边界：数值难度、完整三店连续主线、独立包、移动端和性能未在本批重新验收。视觉表现仍由开发者通过预览及实际Game截图确认。没有暂存、提交或推送。
