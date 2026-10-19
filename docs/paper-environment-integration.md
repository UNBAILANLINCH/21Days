# 纸片角色环境融合（2026-10-20）

## 来源与接入范围

交付包 `21Days-Paper-Environment-Integration-v1.zip` 的四个源码 SHA256 与 manifest 一致，基线为 `d2f241d`；当前接入基线为 `1834c17` 加已有未提交工作。实际环境是 Unity 2022.3.62f2 / URP 14.0.12。包内历史测试不是本次验证结果。

| 项目 | 处理与理由 |
| --- | --- |
| 环境色、主方向光实时阴影、亮度下限、饱和度 | 增量合入现有 SpriteDepthClip；固定世界向上 SH，避免翻面法线引起明暗变化 |
| 编辑器窗口、临时材质输入校验与 Undo 测试 | 采用；位于 Editor / EditMode 程序集，正式场景不挂测试组件 |
| AlphaTest / 深度偏移 / 四个 Pass / 法线 / 雾 | 已有，保留；包的旧 shader 不整份覆盖 |
| 潜行覆盖率裁剪与透明度跟随 | 当前工程比包更新，保留四通道 ClipSpriteAlpha 和前向顶点透明度语义 |
| 两份角色共享材质 | 经实际引用检查后调参；原 GUID、Tint、Cutoff、队列、基础偏移 0 / 玩家偏移 0.02 保留 |
| 世界标记与光环 | 发现 6 个可见标记复用基础角色材质，已经 Unity MCP 隔离到 `M_SpriteDepthClip_Markers.mat`，避免跟着角色变暗；不改几何、层或交互 |
| BlobShadow、贴地投影、排序、动画与表情 | 沿用现有实现；不增第二层脚底阴影，不重生成角色 |
| 点光/聚光、烘焙 ShadowMask、角色实时投影、额外 SSAO | 本包未提供，不补全假定能力；祭坛火光等局部灯光不会经本 shader 给角色上色 |
| 渲染资产、依赖、全局曝光、剧情控制 | 不改；读取现有主光/环境色/雾，由剧情原控制器继续管理 |

## 调整与回退

菜单 **21Days → 角色 → 纸片环境融合…** 读取基础材质当前值。按钮先填值，点击“应用并仅保存两份角色材质”才写入。全部目标和四项有限数值先校验；有未保存材质修改时拒绝整批写入。窗口只保存目标材质，Undo 后还需保存撤销结果。

| 参数 | 兼容默认值 | 本次保存值 |
| --- | ---: | ---: |
| Environment Blend | 0 | 0.2 |
| Main Light Shadow Blend | 0 | 0.25 |
| Environment Minimum | 0.45 | 0.45 |
| Artwork Saturation | 1 | 1 |

融合先于原雾。环境倍率夹到 0.45–1.25 后按强度混合，再独立乘阴影；这不是最终画面亮度保底。Shader 参数默认关闭，只有材质设置后生效。无有效阴影资源时不产生建筑阴影；低档使用既有 1024 / 单级联 / 25 m 硬阴影，高档使用既有 2048 / 两级联 / 40 m 软阴影。

只关闭效果：窗口填入旧版表现值并应用（环境 0、阴影 0、下限 0.45、饱和度 1）。不恢复整个 shader 到旧 HEAD，否则会丢失本次接入前已经存在的潜行裁剪工作。完整撤销应按本次增量逐项反向处理，保留原 shader/meta、原材质/meta 及其它聊天成果，禁止整仓 reset/clean。

完整撤销时，先关闭两份材质的效果，再在 Unity 中把六个标记/光环的材质槽换回基础材质，随后通过编辑器删除新增标记材质（连同其 meta）。删除新增窗口/测试也由编辑器处理；若同时移除 shader 的融合属性，还须移除接线测试里对应四项参数与标记融合断言。Shader 只撤销新增融合属性、CBUFFER 项、前向光照变体和混色，不撤销 `ClipSpriteAlpha` 或 `ApplyDepthBias`。人物回放的开场就绪修正独立于材质能力，可保留。

## 本次验证

已核实包完整性与版本；C# lint 通过。源码断言确认三个非前向 Pass 相对本次前的实现不变、ApplyDepthBias 不变、四次覆盖率裁剪和四个 ZWrite 保留。

两份原材质已保存上表参数。当前 SampleScene 有 35 个 ChibiPuppet、149 个角色渲染件（含拆件和未激活分组）；这些角色沿原引用使用基础/玩家材质。未批量改写其它角色材质、预制体或 UI 材质。旧表现标记材质只接到三个 `Npc_*/MarkerIdle` 与三个 `StoryPoint_*/InteractionGlow/LightRing`；隐藏朝向纸片和 BlobShadow 未动。新增脚本与标记材质的 meta 均由 Unity 生成。

| 本次检查 | 结果与边界 |
| --- | --- |
| Unity 导入 / C# 编译 / Shader 消息 | 无错误；URP 版本无需适配依赖 |
| EditMode 最终材质批次 `b095b202f096454bb86a4e2ad33a1248` | 92 通过、0 失败、0 跳过，含包内 11 项与标记隔离接线守卫；初次 `bdab5d7768f14ee99750e31a3b0837a4` 也为 92 全通过 |
| 拆件动画 PlayMode `8c7ebe4ef557400499bd093a3ecc8793` | 2 通过；走跑、时停待机、Drink 被移动打断 |
| 同条件 GPU 画面对照 | 高/低档 × 白昼/月夜 × 探索/客栈，共 24 张；固定人物动画，仅临时克隆材质，结束恢复引用、动画、环境、画质，无临时相机或材质引用残留，场景脏标记未变 |
| 正式剧情日夜与继续 | 本次 `Logs/verify/narrative/20261020-045937/report.md` PASS，32 条记录、0 失败检查、0 异常，覆盖梦中雪夜、黎明、白昼、月夜及夜档继续 |
| CharacterPuppet 回放夹具适配 | 初次两条 FAIL，共 4 个移动检查点：正式开场对白尚未退出。修为按剧情忙碌、对白、暂停及输入状态持续操作现有按钮后，`73fca2ee25ec4e2ca432c15b84be0a70` 的转身/保持/移动解除全通过；走停的功能检查也全通过，但 20 秒进场预算遇到约 41 秒加载，仍保留一次超时失败。共用示例场景预算随后对齐现有正式回放的 60 秒；功能断言未放宽，未改玩法输入或暂停逻辑 |
| 走停最终复跑 | `Logs/verify/characterpuppet/20261020-051844/report.md` PASS，16 条记录、0 失败检查、0 异常，含真实移动、左右翻面及停步。末轮 MCP job `762a7d98c5f5401a99a2e7bc613c22d9` 的状态回传报初始化超时、total=null，与实际完成的报告不一致；因此不把该 job 状态宣传为 Unity Test Runner 通过。报告、四张截图及独立控制台读取是本次实际完成的证据 |
| 客栈实际战斗回归 | `Logs/verify/tavern/20261020-045937/report.md` PASS，取消/失败/再次进入/第二招式/道具/胜利、掌柜交互、相机/输入/暂停/人物清理恢复均通过，0 异常；是本次参数下重新运行的结果 |
| C# lint / diff / meta | 本次五份 C# lint 零违规；差异空白检查通过，新增资产均有 Unity 生成的 meta |
| 收尾状态 | 已回 SampleScene 编辑态，场景未脏；画质回 5、回放速度回 1、timeScale 回 1；无临时相机/投影体/临时材质引用/InitTestScene，客栈相机停用、挂载角色为 0 |
| 全局 gc_scan | 未通过；最后扫描仅剩三份 Dynamic 字体缓存过大（NotoSansSC、SourceHanSansCN、SourceHanSerifCN），与前序聊天已报告项相同。未清除他人 UI 工作或整份回滚字体资产，不宣传全仓健康通过 |

对照脚本为 `Tools/verify-paper-environment.txt`，只供空闲编辑器的 MCP `execute_code` 显式执行；它不是运行时组件。像素统计只取人物投影区域，排除整帧水面变化；统计阈值为 RGB 总差 > 3。最后一次高档白昼探索开启两项后区域变化约 10498 像素，月夜约 12248；低档白昼约 9618，月夜约 12411。它证明参数影响真实人物画面，不证明观感一定更好。低档两个白昼站位仅阴影对照为 0 像素，不能宣传每个站位都有建筑阴影。

另做只投影、不进入相机画面的临时遮挡体探针；相同角色、环境混合关闭、阴影混合从 0 到 0.25，高/低档人物区域分别有 10121 / 10112 像素压暗，均通过至少 1000 像素的断言，确认两档主光阴影能力实际生效。探针已销毁，不写入场景。它验证接收路径，不是实际屋檐位置的美术验收。

- 白昼探索：[关闭](../Captures/paper-environment-20261020/tier5-Day-exploration-0.png) / [开启](../Captures/paper-environment-20261020/tier5-Day-exploration-2.png)
- 月夜客栈：[关闭](../Captures/paper-environment-20261020/tier5-MoonNight-inn-roof-0.png) / [开启](../Captures/paper-environment-20261020/tier5-MoonNight-inn-roof-2.png)
- 低档月夜：[关闭](../Captures/paper-environment-20261020/tier0-MoonNight-exploration-0.png) / [开启](../Captures/paper-environment-20261020/tier0-MoonNight-exploration-2.png)
- 逐项数据：[像素记录](../Captures/paper-environment-20261020/metrics.txt)
- 实际客栈：[第二招式](../Captures/paper-environment-20261020/tavern-second-skill.png)

仓库保留以上三组前后对照、客栈截图和逐项像素数据；完整 24 张同条件对照、投影探针及原始回放报告仍保留在本地 `Captures/` 与 `Logs/verify/`，不将完整截图批次写入版本历史。

共享工作区交付注意：`SampleScene.unity` 的整体 diff 同时包含此前场景、UI 和客栈聊天成果，本次场景操作只有六个材质槽，不把整份 diff 当成本次新增。`ProjectSettings/PackageManagerSettings.asset` 另出现两项编辑器临时 instance ID 差异，未手改或回滚；本任务未改变任何包依赖或管线配置。

未做目标设备 Profiler，不声称性能提升。最终需开发者判断夜晚面部可读性、环境色强弱、纸片横跨阴影边界时的切割感；局部火光不在本 shader 接光范围内。实景仍有原地图缺面/地表接缝，不能用本效果掩盖或宣称已经修复。
