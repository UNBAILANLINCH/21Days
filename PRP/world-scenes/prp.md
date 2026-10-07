# PRP: 两界场景流转（world-scenes，roadmap A4 + A6）

> 状态：草案（2026-10-07 建立），待 Q3 接线波释放 `MonsterEncounterState.cs` 后执行。
> 范围：roadmap **A4**（多场景流转：场景表、传送点、出生点、跨场景状态）+ **A6 的表现层那一半**（相机边界体与死区落到 `SmoothCameraFollow`）。
> 上游：[`10_两界与场景结构.md`](../docs/design/features-spotlight/10_两界与场景结构.md)（两界同构、区域、转场）、[`00_功能总览.md`](../docs/design/features-spotlight/00_功能总览.md) §8.2 步 3
> 相关：[`S组落地总规划.md`](../docs/planning/S组落地总规划.md)（波次与文件所有权）、[`world-module-guide.md`](../ai-docs/docs/modules/world/world-module-guide.md)（World 模块现状与建议补丁）、[`story-facts.md`](../ai-docs/docs/story-facts.md)

---

## 1·上下文快照

### 1.1 已经就位（不要重造）

| 能力 | 位置 | 说明 |
| --- | --- | --- |
| **世界表** | `Tables/Defines/world.xml` + `Tables/Data/world/**` | 2 场景 / 26 区域 / 2 传送点；逐列真源出处写在 xml 列注释里 |
| **只读查询与全表校验** | `Runtime/World/WorldCatalog.cs`、`WorldCatalogValidator.cs` | 惰性读表、白名单校验、`Invalidate`；9 类校验含「未实装不许写地址」 |
| **出生点选择（纯规则）** | `Runtime/World/WorldRules.cs` | `Resolve`（失败抛 `WorldResolveException`）+ `TryResolveSpawn`（失败是正常分支，供调用方判定） |
| **传送点组件** | `Runtime/World/PortalAnchor.cs` | 两种触发（进入范围 / 交互键）；**交互键不由组件读**、不依赖 Physics、不做转场 |
| **跨场景状态** | `Runtime/World/{WorldSaveData,SceneStateKey,SceneStateScope}.cs` | `场景键::实体标识` 两段键；复用 `QuestSaveData` 承载任务点，新增 `WorldSaveData` 记箱子/怪物已死 |
| **相机约束（纯数学）** | `Runtime/IsometricExploration/CameraConstraintRules.cs` | 边界矩形 + 死区 + 逐轴推 + 钳制 + 防抖；**表现层没接** |
| **加载黑幕** | `Core/Flow/ILoadingCurtain` + `LoadingCurtain` + `LoadingView` | E4 已完成：`GameFlow` 在场景切换前后统一落/揭，失败也揭 |
| **场景状态基类** | `Core/Flow/SceneGameState.cs` | `SceneKey`（抽象属性，Addressables 地址）、`OnSceneReadyAsync` / `OnSceneUnloadingAsync` 钩子 |

### 1.2 真正的缺口（本 PRP 要解决的）

1. **`EncounterSaveData` 写死只认一个地址**（`Runtime/Monster/EncounterSaveData.cs:23`：`if (SceneKey != "IsometricEncounter" || …) throw`）——**第二张图一存就读不回来**。这是接线的硬阻塞点。
2. **没有任何地方决定「进这张图站哪」**：`SceneGameState` 只加载场景，不选出生点。
3. **`PortalAnchor` 没有调用方**，`WorldRules.Resolve` 没有调用方，`SceneStateScope` 没注册进容器。
4. **两界场景都没实装**：`human_jingyang` / `yao_fangshi` 在 Addressables 的 Scenes 组里**都不存在**（表里 `implemented=false`、地址空串）。
5. **相机约束没接到表现层**（`SmoothCameraFollow` 一个字没改）。
6. **NPC 状态没有落点**（见 §2.5）。

### 1.3 必须规避的 pitfalls

- **生成物不手改**：改 `world.xml` 必须**同一次**把 `Tables/Data/world/**` 补齐再跑 `gen-tables.ps1`——只改 schema 会让**全表生成失败、卡住所有会话**（2026-10-07 真实发生过）。
- **`Boot.unity` 与 `GameInput.inputactions` 是共享点**：本 PRP 要动 `Boot.unity`（挂新状态/新组件时），必须独占一波。
- **改 `IReplayState` 或已注册状态的序列化字段必须升 `ReplayFormat.CurrentFormatVersion`**（当前 4）。

---

## 2·架构决策

### 2.1 场景状态怎么按「数据驱动的场景键」选（本 PRP 的核心决策）

**问题**：`GameFlow.GoToAsync<TState>` 在编译期就要知道状态**类型**（`GameFlow.cs:153` 用 `resolver.Resolve(stateType)` 解析），而传送点的目标是**表里的 `scene_key` 字符串**。如果一张图一个状态类，就得到处写 `switch (sceneKey) → typeof(XxxState)`，而场景数会随十二阶段增长。

**决策：一个 `WorldSceneState` + 一个「待处理转场」服务。**

```
PortalAnchor 触发
      │ 写入目标（scene_key + 目标出生点 + 到达方式）
      ▼
IWorldTransition  （新，单例；一次只挂一个待处理转场，重复请求按后到覆盖并 Warn）
      │
      ▼
GameFlow.GoToAsync<WorldSceneState>()
      │  容器解析出 WorldSceneState（单例）
      ▼
WorldSceneState.OnSceneReadyAsync
      │  ① 从 IWorldTransition 取目标 → WorldRules.Resolve 选出生点
      │  ② 把玩家放到出生点、相机对准
      │  ③ SceneStateScope 绑定该场景键（箱子/怪物状态按场景隔离）
      │  ④ 用掉待处理转场（清空，避免重进时误用旧目标）
      ▼
Addressables 按 scene_key 对应的地址加载（`SceneGameState.SceneKey` 不能是常量）
```

**但 `SceneKey` 是抽象属性、`EnterAsync` 在 `OnSceneReadyAsync` 之前就要用它加载场景**（`SceneGameState.cs:41/56`）——所以地址必须在**进入前**就确定。因此：

- `WorldSceneState` 覆写 `SceneKey`，**从 `IWorldTransition.Current` 读地址**；没有待处理转场时用表里的 `default_spawn_id` 所属场景或直接报错（二选一，实现时定，**报错更安全**：悄悄进错图比崩更难查）。
- ⚠️ **不要**把 `SceneKey` 改成可外部写的公开属性——那会削弱基类的不可变契约（`Roadmap` 里 World 模块指南建议过「两个子类」，但那样无法数据驱动，且场景数增长后要维护一张 `scene_key → 类型` 映射表，等于第二份真相）。

**为什么不做「一场景一状态类」**：十二阶段 × 两界，状态类会膨胀到两位数，且每加一张图要改代码——而表里已经有这张表了。**规则描述现状，不重新发明现状。**

### 2.2 `EncounterSaveData` 的去写死

`Runtime/Monster/EncounterSaveData.cs:23` 的 `SceneKey != "IsometricEncounter"` 改成**「非空 + 能在 `TbScene` 里查到」**：

- 校验需要表，而 `Validate()` 是纯数据方法、不该依赖 `IConfigService`。**做法**：`Validate()` 只查「非空 + 格式合法」，**「地址存在」交给 `WorldCatalogValidator` 或调用方**（`GameSession` 落盘/读档时校验）。
- **要动 `Validate` 的判据就是改了存档分区的校验语义**：`Version` 要不要升？**不升**——`SceneKey` 字段本身没变、`Migrate` 无迁移动作，只是把「必须是某个字面量」放宽成「必须在表里」。但**要补测试**证明旧档（`IsometricEncounter`）仍通过。
- 同一处 `MonsterEncounterState.cs:46` 的 `SceneKey => "IsometricEncounter"` 与 `:58-59` 抛错文案里写死的场景名一并改掉。

### 2.3 出生点与相机

- 出生点选择只走 `WorldRules`（纯函数，已测）。**调用点是新代码**：`WorldSceneState.OnSceneReadyAsync`。
- **相机边界**：把 `CameraConstraintRules` 接进表现层。场景侧的边界体来源有两种，**选一种并写进模块指南**：
  - ① 视图上一个 `Bounds`（或 `Collider`）序列化字段，作者在场景里摆；
  - ② 从 `TbRegion` 的层高/可走区域推（需要区域到世界坐标的映射，现在**没有**）。
  **本 PRP 选 ①**：`TbRegion` 没有世界坐标列，②要先给表加列（属内容侧，等 C 类答复）。①能在两张灰盒图上先跑通。
- **A6 的另一半**（进对话时的构图切换）依赖 `CameraConstraintRules` 的边界已知，**放在接线之后**，不在本 PRP 首批。

### 2.4 场景实装（Addressables）

两界场景要真的存在才能验。本 PRP 的做法：
1. 先用**灰盒**建两张场景（`Assets/_Project/Scenes/`，与 `Boot` 同级；**不要动 `Assets/Scenes/`**——那是模板目录，规则要求原位不动）。
2. 登记进 Addressables 的 Scenes 组，地址与表里 `scene_key` 对应。
3. 把 `Tables/Data/world/scene/*.json` 的 `implemented` 改 `true`、填 `scene_address`，跑 `gen-tables.ps1`。
4. **补一个编辑器侧校验工具**（`Scripts/Editor/World/`）：`WorldCatalog.Validate()` 里 `Implemented=true` 的地址**真的在 Scenes 组里**。Runtime 不能引 `UnityEditor`，所以这半只能放编辑器——**World 模块指南已把它列为「做不到的那一半」**。

### 2.5 NPC 状态：本 PRP **不做**，但要说清为什么

跨阶段 NPC 消失（`10_两界与场景结构.md:144` R16）需要一个「按场景记 NPC」的落点。现有两个候选都不合适：`DialogueSaveData` 是「当前对话稳定恢复点」（单棵树）、`NarrativeSaveData.StoryFlags` 是剧情标记。**本 PRP 不硬塞**：先用 `Narrative` 的剧情标记表达「某 NPC 在阶段 N 之后不再出现」这类**内容侧**需求；真正的「NPC 实体状态」等有明确需求（哪个 NPC、什么状态）再做，避免又造一个没人读的分区。

### 2.6 埋点

`world.scene_entered`（scene_key、到达方式、出生点）、`world.portal_triggered`（portal_id、trigger_kind）、`world.resolve_failed`（失败原因，**这是内容配错时的唯一现场**）。

---

## 3·验证清单

机器可判（EditMode，组 `Game.Tests.EditMode.World` + `Monster` + `Session`）：

1. `EncounterSaveData.Validate()`：旧地址 `IsometricEncounter` **仍通过**；`SceneKey` 为空 → 拒；表里查不到的地址 → 由表校验/调用方拒（**两处都要有负对照**）。
2. `IWorldTransition`：一次只有一个待处理转场；重复请求按后到覆盖**并 Warn**；用掉之后清空（重进不会误用旧目标）。
3. `WorldSceneState` 按待处理转场取地址；**没有待处理转场时行为明确**（报错，不是悄悄进默认图）。
4. 出生点四种组合（指名存在 / 留空回退默认 / 指名不存在 / 未知场景）——World 组已有，别重复写，**补的是「从状态侧调用」的那一层**。
5. 跨场景状态：同一实体 id 在两张图各记一份、互不影响；`SceneStateScope` 每次操作重新读分区（读档后不被旧实例缓存误导）。
6. 相机约束接表现层后：目标在死区内相机不动、越界被推、边界小于视口时居中、**单轴越界不漂移另一轴**（World 组已有纯规则用例，补的是「表现层真的在调它」）。
7. 编辑器侧地址校验工具：表里 `implemented=true` 但地址不在 Scenes 组 → 报错；对着真实配置跑通。

肉眼可验（回放）：在两张灰盒图之间走传送点来回一趟，黑幕节奏正常、落点正确、箱子/怪物状态各自记住。报告落 `Logs/verify/world/`。

---

## 4·风险 / 回滚

| 风险 | 处置 |
| --- | --- |
| `SceneGameState.SceneKey` 从常量改成「读待处理转场」会动到既有 `MonsterEncounterState` 的行为 | **`MonsterEncounterState` 保持常量地址不动**（它是遗留原型路径）；新状态另起 `WorldSceneState`。两条路并存，回归时两组都要跑 |
| 改 `EncounterSaveData.Validate` 影响所有存档回归 | 先补「旧档仍通过」的测试再改；分区 `Version` 不升（字段与 `Migrate` 都没变） |
| 两张灰盒场景会引入大量资产与 `.unity` 冲突 | 场景改动走「一波一个 agent」；`.meta` 交给 Unity 生成；**不要动 `Assets/Scenes/`** |
| `Addressables` 设置改动（`ProjectSettings`/`AddressableAssetsData`）属共享点 | 与本 PRP 的其他改动分开提交，改前说明理由（硬规则 3） |
| 相机边界体摆放需要美术/关卡知识 | 灰盒先用粗矩形，把「按层分段」留给策划答复（`10_两界与场景结构.md:216` Q12） |

---

## 5·需拍板 / 需协调（不阻塞本 PRP 的机制实现）

| # | 事项 | 卡住谁 | 出处 |
| --- | --- | --- | --- |
| 1 | **阶段三在妖界还是人间（泾阳）**；两界之间怎么切换（找镜子？固定传送点？剧情自动？） | `TbPortal` 的 `anchor_id`/`trigger_kind` 只能填占位 | `10_两界与场景结构.md:206` Q2、`:211` Q7；`00` §8.1 #12、§5 C12 |
| 2 | 街面那一排是几层；2 层的洞庭府/井底与街面谁更低 | `TbRegion.floor` 的取值与出生点的层归属 | `10:205` Q1 |
| 3 | 原文明说「与两张俯视图怎么拼没定」的地点（官署·药行、水口→里巷→决堤…） | 我登记成 `StageExtension`（`scene_key` 指向主图）但**没有传送点** | `10:207` Q3、`:146` R18 |
| 4 | 同物异名是否同一处（乡集祠堂/分福祠；洞庭府/龙窟/龙府） | `region_id` 一行还是三行 | `10:208` Q4 |
| 5 | 「8 个建筑」具体哪 8 类；两界各做一套还是复用 | 区域与出生点的最小集合 | `10:209` Q5、`:140` R12 |
| 6 | 俯视图名为俯视、可走区域却是一条横带：镜头是俯视、斜俯视还是侧视 | **A6 的边界形状**（本 PRP 按矩形做，若是侧视要改成带状走廊） | `10:217` Q13 |
| 7 | 山石「制造层高」是纯视觉还是有可走高差 | 相机边界要不要按层分段 | `10:216` Q12 |
| 8 | **Taming 是否解除「不接 Boot」** | 与 S1 附身载体共用绕边判定时的接线方式 | `taming-module-guide.md:11` |
