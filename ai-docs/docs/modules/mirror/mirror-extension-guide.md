---
type: extension-guide
module: mirror
layer: runtime
maturity: seed
---

# Mirror 扩展指南

**模块状态：冻结（2026-10-06）。** 策划已换为《聚光灯》（`docs/design/spotlight/`），本模块依据的旧版 `docs/design/features/` [01][02][03][13] 已降为旧版。代码与测试保留（Boot 仍挂 MirrorInstaller，玩家血量归零时的镜碎页仍生效），不再推进、不做视觉验收；聚光灯的「暴露 → 死亡」流程（`docs/design/features-spotlight/02_身份暴露与怀疑.md`）落地时再替换或删除。可能复用：阶段九「随身镜识破」（`docs/design/features-spotlight/07_关卡专属机制.md`）。

## 加一只妖（场景内容 + 一行数据表，不用改代码）

1. 在 `Tables/Defines/yao.xml` 对应的数据目录 `Tables/Data/yao/` 下新增一个 `<id>.json`，字段齐全：`id`（全局唯一）、
   `disguise_name`、`true_name`、`true_desc`、`true_image`（Addressables 地址，需已导入并登记）、`flaw`、`obsession`、`clan`、
   `clue_items`（tbitem id 列表，空 = 一照就见）、`sealable`、`mask`、`tier`（只能是 `A` / `B` / `C`）、`killable`、
   `defeat_method`（只能是白名单五个值，见下）、`drop_items`（tbitem id 列表，没定就写 `[]`）。
   **JSON 里一个字段都不能缺**，空列表写 `[]`、布尔显式写 `true` / `false`。
   除 `tier` / `killable` / `defeat_method` / `drop_items` 外都有列在用的旧行可以照抄；每列的归属与谁读见 `Tables/Defines/yao.xml` 表头，
   字段含义见 `Assets/_Project/Scripts/Core/Config/Generated/yao/Yao.cs` 的注释。
   `killable` 与 `defeat_method` 要一起填（填错会互相矛盾）：`killable` 答「能不能按常规一路杀掉」，
   `defeat_method` 答「为什么不能常规杀、有没有替代途径」，取值只能是
   `可击杀（方式没写）` / `暗杀` / `特殊条件` / `需收服` / `不可杀`。
   真源里出现过的四种「不能常规杀」的说法（不可击杀、常态不可击杀、无法被常规击杀、持有篮子时不可击杀）都进 `killable = false`，
   `defeat_method` 写归纳后的落点，不要把原文整句照抄进来——照抄会被 `YaoCatalog.ValidateDefeatMethod` 拦下。
2. Luban 生成后确认 `Assets/_Project/Data/Config/yao_tbyao.bytes` 有更新（内容不变则不重写文件，见 pitfalls）。
3. 场景里给这只妖挂 `MirrorSubject`（`kind = Yao`，`yaoId` 填新 id）；跟随巡逻怪的还要勾 `followsMonster`。
   走 Unity 编辑器 / Unity MCP 接线，不手改 YAML。
4. 需要真形图就把 Sprite 放进 `Mirror` 组（Addressables 地址与 `true_image` 一致，见 mirror-module-guide「Addressables 用 Mirror 组」）。
5. 这就是 PRD V12 的验收依据：新增一只妖只加数据表与场景标记，`MirrorRules` / `MirrorService` 都不用改
   （`MirrorRules.Resolve` 按 `MirrorCandidate.YaoId` 查 `HasClues` 委托，见 `Assets/_Project/Scripts/Runtime/Mirror/MirrorRules.cs:55-58`）。

## 按种类读配置（收押 / 画皮 / 账簿 / 怪物分层）

要按 id 查这只妖的层级、能否击杀、怎么杀 / 有没有替代途径、掉什么、能否收押 / 制面具、族属时，注入 **`YaoCatalog`**（`AsSelf` 根作用域单例），
不要注入 `MirrorService`（那是照镜门面，带存档与玩家状态），也不要自己去读 `IConfigService.Tables.TbYao`
（各处自己 try/catch 配置未就绪会让容错散开）。对外签名见 [`mirror-external-api.md`](mirror-external-api.md)。

- 表没就绪不算异常：`IsReady` 为 false、`TryGet` 一律 false，启动早期先探 `IsReady`。
- 新增一只妖只加一行数据 + 场景标记，`YaoCatalog` 的查询自动覆盖，不用改代码。
- 本类**只读不判**：`IsSealable` / `CanMask` / `IsKillable` / `DefeatMethodOf` 只回答表里怎么写的，收押与画皮的业务规则由各自模块实现。
- `tier` 只认 `A` / `B` / `C`；`docs/design/features-spotlight/06_怪物分层.md:126` 的「A·下 / B·下」是排版记号，不是层级值。
- `defeat_method` 只认五个值；它和 `IsKillable` 要**一起读**：查勘使是 `IsKillable=false` + `特殊条件`（能用地形隐匿击杀），
  籍中吏是 `IsKillable=false` + `不可杀`（没有途径），只看 `IsKillable` 会把两者看成一回事。
- 列白名单（tier / defeat_method）**只在首次读表时校验一次**（B13）。换过表数据要重新校验就调 `Invalidate()`，
  它会清缓存与「已校验」标记；不要指望改坏数据但不调 `Invalidate` 也会立刻报错。
- 要在规则层消费这两列（例如结算时判「这只怪该不该给掉落」）时，判断逻辑放调用方自己的规则类，
  不要把业务判定塞进 `YaoCatalog`——它只做「表 → 查询」这一层。

## 加一种照镜结果（新增 `MirrorResultKind`）

当前结果种类固定五种 + 一种自照恒定值（`Assets/_Project/Scripts/Runtime/Mirror/MirrorResultKind.cs:8-27`）。要新增一种（例如「照见已收押的妖」）：

1. `MirrorResultKind` 加新枚举值；`MirrorRules.Resolve` 的 `switch`（`Assets/_Project/Scripts/Runtime/Mirror/MirrorRules.cs:49-61`）按新条件返回它——判断逻辑本身仍是纯函数，
   EditMode 直接穷举新分支。
2. `MirrorResultInfo.Compose`（`Assets/_Project/Scripts/Runtime/Mirror/MirrorResultInfo.cs:49-73`）的 `switch` 加一支，从 `MirrorConfig` 取新标题 / 说明文案；
   `MirrorConfig` 相应加 `[SerializeField] private string` 字段（同 `blurryTitle` / `nothingTitle` 的写法，`Assets/_Project/Scripts/Runtime/Mirror/MirrorConfig.cs:62-90`）。
3. 如果新结果要单独记存档（同 `Identified` / `GlimpsedBlurry`），在 `MirrorSaveData` 加字段并升版本号
   （`Version` 属性见 `Assets/_Project/Scripts/Runtime/Mirror/MirrorSaveData.cs:12`，迁移写在 `Migrate` 见
   `Assets/_Project/Scripts/Runtime/Mirror/MirrorSaveData.cs:26-29`），
   `MirrorRules.Record`（`Assets/_Project/Scripts/Runtime/Mirror/MirrorRules.cs:94-112`）加对应分支。
4. `MirrorResultView.Apply`（`Assets/_Project/Scripts/Runtime/Mirror/MirrorResultView.cs:84-102`）一般不用改：它只读 `MirrorResultInfo` 的通用字段（标题 / 名字 / 说明 / 图 / 是否压暗）；
   新结果如果要专属观感（不是「压暗」也不是「空镜面」）才需要在 `ShowImage`（`Assets/_Project/Scripts/Runtime/Mirror/MirrorResultView.cs:105-113`）加分支。
5. 不要把新结果种类塞进 `MirrorSubjectKind`：那是「对象是什么」，不是「照镜结果是什么」，两者故意分开
   （`Assets/_Project/Scripts/Runtime/Mirror/MirrorResultKind.cs:1-3` 文件头注释）。

新增结果需要异步加载图片时，沿用 `MirrorInputPresenter.ComposeAsync` 返回内容与句柄、`ShowAsync` 接管所有权的路径
（`Assets/_Project/Scripts/Runtime/Mirror/MirrorInputPresenter.cs:226`、`:160`），并补取消 / 打开失败回归；不要在加载回调里直接覆盖呈现器的图片字段。

## 加一个通灵视条件

当前生效条件固定「雨 / 夜 / 昏暗任一为真」（`Assets/_Project/Scripts/Runtime/Mirror/SpiritSightRules.cs:15`）。要加新条件（例如「持有某道具」）：

1. `SpiritSightZone` 加一个 `[SerializeField] private bool` 字段（同 `rain`/`night`/`dim` 的写法，`Assets/_Project/Scripts/Runtime/Mirror/SpiritSightZone.cs:19-26`），
   或者如果条件与「区域」无关（比如「持有道具」是全局条件、不挂在某个区域上），改造 `SpiritSightRules.IsActive` 的签名，
   在 `SpiritSightPresenter.Tick`（`Assets/_Project/Scripts/Runtime/Mirror/SpiritSightPresenter.cs:66`）里把新判断项传进去。
2. `SpiritSightRules.IsActive`（`Assets/_Project/Scripts/Runtime/Mirror/SpiritSightRules.cs:15`）是纯函数，加一个 `bool` 参数、`||` 上新条件即可，EditMode 直接补用例。
3. 不要把新条件的判断逻辑写进 `SpiritSightPresenter` 或 `SpiritSightZone` 本身：规则必须留在 `SpiritSightRules` 里保持可脱离场景单测
   （同 `MirrorRules` / `MirrorCrackRules` 的分法，`Assets/_Project/Scripts/Runtime/Mirror/SpiritSightRules.cs:1-5` 文件头注释）。
4. 条件为「持有道具」一类需要跨模块读取时，走 `MirrorService` 或 `MirrorSceneBinder` 一样的路径：构造函数注入对应服务的只读接口
   （例如 `LootService.Items`），不要让 `SpiritSightZone`（场景组件）自己注入服务——场景组件只做标记，不读输入、不认识容器
   （同 `MirrorSubject` / `SpiritSightZone` 现在的边界）。

## 依赖方向约束

新扩展只能 `Game.Mirror → Game.Player` / `Game.Monster` / `Game.Loot` / `Game.Session` / `Game.Dialogue` / `Game.Core`（均只读）；
不要引用 `Editor` / `Tests` 程序集；不要让 Mirror 反向依赖任何读它的未来模块（收押、画皮）——那些模块应该只读
`MirrorService` 的公开方法（见 `mirror-external-api.md`），不应该被 Mirror 引用。场景组件（`MirrorSubject` / `SpiritSightZone`）
不注入服务，扩展逻辑放规则类或入口点里。

## 验证

规则改动配 EditMode 测试（`Assets/_Project/Scripts/Tests/EditMode/Mirror/`）；玩家可见行为（照镜结果、通灵视提示、镜裂 / 镜碎）走
`Assets/_Project/Scripts/Tests/Showcase/Mirror/MirrorShowcase.cs`。场景资产只通过 Unity 编辑器或 Unity MCP 修改；Luban 表改动后
跑一次生成（`scripts/gen-tables.ps1`），确认 `Tables.cs` 与 `yao_tbyao.bytes` 都同步。

加列 / 改列时的固定动作（2026-10-07 加 `defeat_method` 那轮的流程）：

1. 改 `Tables/Defines/yao.xml` 的 `<var>`，**在 comment 里写清它和相邻列的分工**（`killable` / `defeat_method` 这种会语义重叠的列尤其要写），
   数据行在 `Tables/Data/yao/*.json` 补齐新字段。
2. 跑 `scripts/gen-tables.ps1` 重新生成；生成器把产物写成 CRLF，`git status` 里出现的**无关**生成物行尾变化要统一回 LF
   （只改换行、内容不变，判据：`git hash-object <文件>` 与 `git rev-parse HEAD:<文件>` 相同）。
3. `YaoCatalog` 补只读访问器 + 白名单校验（照 `ValidateTier` / `ValidateDefeatMethod` 的写法，报错带 id、原值与 `yao.xml`），
   `YaoTableTests` 补逐列读取、缺 id、非法取值（含「照抄了排版记号」）三类用例。
4. 同步本目录三件套；`YaoTableTests` 用例数变了要一并改 [`mirror-module-guide.md`](mirror-module-guide.md) 的验证入口那一节。
5. 编译门（`refresh_unity(mode="force", scope="all", compile="request")` → `read_console` 查 `error CS`）过了再跑
   `run_tests(mode="EditMode", group_names="Game.Tests.EditMode.Mirror")`，把 `total / passed / failed / skipped` 原样记进 module-guide。
