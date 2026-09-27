# Unity 拓扑重映射空间：两类场景的具体实现方案

> 适用场景：固定/半固定斜俯视、3D 场景结构 + 2D/面片角色、规则网格或模块化场景。  
> 目标不是复刻某款游戏的内部实现，而是在 Unity 中实现“有限空间通过位置/朝向/连接关系重映射，被玩家感知为无限或异常空间”的可复用系统。

---

## 0. 总体设计原则

两种需求共用同一套底层：

1. **战斗房无限循环**：一个有限 Arena 在 X/Z 方向重复平铺，玩家、敌人、投射物跨越边界后无缝映射到另一侧；视觉上显示邻近副本，使空间看起来连续。
2. **剧情鬼打墙 / 八卦生门**：场景物理尺寸有限，但出口之间的“逻辑连接”独立于真实坐标，可按剧情状态把同一扇门连接到不同目标，也可以旋转、镜像、循环回当前空间。

建议不要把系统命名成 `InfiniteRoomManager`，而是按职责拆成：

```text
Topology/
├── Core/
│   ├── TopologySpace.cs
│   ├── TopologyPortal.cs
│   ├── TopologyTraveller.cs
│   └── TopologyMath.cs
│
├── Combat/
│   ├── ToroidalArena.cs
│   ├── ArenaReplicaRenderer.cs
│   ├── TopologyTargeting.cs
│   └── TopologyProjectile.cs
│
├── Narrative/
│   ├── NarrativeTopologyGraph.cs
│   ├── NarrativeTopologyController.cs
│   ├── LoopStateRule.cs
│   └── BaguaGateRule.cs
│
└── Debug/
    ├── TopologyGizmos.cs
    └── TopologyDebugHUD.cs
```

核心思想：

```text
视觉位置 != 逻辑邻接关系
物理坐标 != 拓扑距离
场景实例 != 世界状态
```

---

# 方案一：战斗房中的 Manifold Garden 风格无限循环空间

## 1. 目标效果

玩家进入一个有限战斗区域，但：

- 向右越界后从左侧对应位置继续前进；
- 向上/向下或前/后同理；
- 敌人、投射物、AOE、锁定系统遵循同样的拓扑规则；
- 玩家在边界附近能看见“另一侧”的敌人或场景；
- 周围可显示 3×3 视觉副本，让空间在画面中连续；
- 真正运行战斗逻辑的 Arena 只保留一份。

对于你当前这种斜俯视场景，第一版只做 **X/Z 两轴循环**，不要先做 Y 轴垂直循环。

---

## 2. 场景结构

建议测试场景：

```text
CombatTopologyScene
├── Systems
│   ├── ToroidalArena
│   ├── ArenaReplicaRenderer
│   └── CombatManager
│
├── ArenaRoot
│   ├── Floor
│   ├── Walls / Railings
│   ├── Props
│   ├── SpawnPoints
│   └── VFX
│
├── DynamicRoot
│   ├── Player
│   ├── Enemies
│   └── Projectiles
│
├── VisualReplicas
│   ├── Replica_-1_-1
│   ├── Replica_0_-1
│   ├── ...
│   └── Replica_1_1
│
└── CameraRig
```

`ArenaRoot` 是唯一真实场景。  
`VisualReplicas` 仅用于“看起来无限”，不能运行 AI、碰撞、战斗脚本。

第一版 Arena 建议为 12×12 或 16×16 Unity Unit 的规则矩形。

---

## 3. Arena 的基本数学模型

设：

```text
MinX = -6
MaxX = +6
MinZ = -6
MaxZ = +6

Width  = MaxX - MinX = 12
Depth  = MaxZ - MinZ = 12
```

玩家位置为：

```text
(x, y, z)
```

若：

```text
x > MaxX
```

则：

```text
x -= Width
```

若：

```text
x < MinX
```

则：

```text
x += Width
```

Z 轴同理。

这等价于把一个有限矩形的左右边、上下边分别识别为同一组边界，得到二维环面式的循环空间。

---

## 4. `ToroidalArena.cs`

```csharp
using UnityEngine;

public class ToroidalArena : MonoBehaviour
{
    [Header("Local Bounds")]
    public float minX = -6f;
    public float maxX =  6f;
    public float minZ = -6f;
    public float maxZ =  6f;

    public float Width => maxX - minX;
    public float Depth => maxZ - minZ;

    public Vector3 WrapLocalPosition(Vector3 localPos)
    {
        if (localPos.x > maxX)
            localPos.x -= Width;
        else if (localPos.x < minX)
            localPos.x += Width;

        if (localPos.z > maxZ)
            localPos.z -= Depth;
        else if (localPos.z < minZ)
            localPos.z += Depth;

        return localPos;
    }

    public Vector3 WrapWorldPosition(Vector3 worldPos)
    {
        Vector3 local = transform.InverseTransformPoint(worldPos);
        local = WrapLocalPosition(local);
        return transform.TransformPoint(local);
    }
}
```

这里先把世界坐标转为 Arena 本地坐标，再做 Wrap，最后还原成世界坐标。这样 ArenaRoot 后续可以整体移动或旋转，而循环逻辑仍然有效。

---

## 5. 玩家 / 敌人的越界重定位

不要直接把所有角色逻辑写进 `ToroidalArena`。给任何可穿越拓扑边界的对象挂：

```csharp
using UnityEngine;

public class TopologyTraveller : MonoBehaviour
{
    public ToroidalArena arena;
    private Rigidbody rb;

    private void Awake()
    {
        rb = GetComponent<Rigidbody>();
    }

    private void FixedUpdate()
    {
        Vector3 oldPos = rb != null ? rb.position : transform.position;
        Vector3 newPos = arena.WrapWorldPosition(oldPos);

        if ((newPos - oldPos).sqrMagnitude < 0.000001f)
            return;

        if (rb != null)
            rb.position = newPos;
        else
            transform.position = newPos;
    }
}
```

如果角色使用 `CharacterController`，可以在重定位时暂时 Disable Controller：

```csharp
controller.enabled = false;
transform.position = newPos;
controller.enabled = true;
```

不要使用 `MovePosition` 做这种跨边界瞬移；`MovePosition` 更适合连续运动，而边界映射本质上是位置重定位。

---

## 6. 战斗系统必须使用“拓扑距离”

这是战斗房版本最容易漏掉的部分。

假设：

```text
Player.x = +5.8
Enemy.x  = -5.8
Width    = 12
```

普通欧氏距离会认为两者相距 11.6，实际上穿过循环边界只差 0.4。

因此锁定、AI、AOE、攻击判定中的距离不能只写：

```csharp
Vector3.Distance(a, b)
```

需要：

```csharp
public static class TopologyMath
{
    public static Vector3 ShortestDelta(
        Vector3 fromLocal,
        Vector3 toLocal,
        float width,
        float depth)
    {
        float dx = toLocal.x - fromLocal.x;
        float dz = toLocal.z - fromLocal.z;

        if (dx > width * 0.5f) dx -= width;
        if (dx < -width * 0.5f) dx += width;

        if (dz > depth * 0.5f) dz -= depth;
        if (dz < -depth * 0.5f) dz += depth;

        return new Vector3(dx, toLocal.y - fromLocal.y, dz);
    }
}
```

使用：

```csharp
Vector3 playerLocal =
    arena.transform.InverseTransformPoint(player.position);

Vector3 enemyLocal =
    arena.transform.InverseTransformPoint(enemy.position);

Vector3 delta = TopologyMath.ShortestDelta(
    playerLocal,
    enemyLocal,
    arena.Width,
    arena.Depth);

float topologyDistance = delta.magnitude;
```

AI 转向也应该使用这个 `delta`：

```csharp
Vector3 direction = delta.normalized;
```

否则敌人处于边界另一侧时，会选择绕整个房间而不是直接跨边界接近玩家。

---

## 7. 投射物

投射物同样挂 `TopologyTraveller`。

第一版建议：

```text
Projectile
├── Rigidbody
├── Collider
├── ProjectileLogic
└── TopologyTraveller
```

跨边界时：

- 位置重映射；
- 速度保持不变；
- 生命周期不重置；
- 不生成新 Projectile；
- TrailRenderer 需要特殊处理。

### TrailRenderer 问题

瞬移时 Trail 会从旧位置拉一条很长的线到新位置。

重定位前：

```csharp
trail.emitting = false;
trail.Clear();
```

重定位后：

```csharp
trail.emitting = true;
```

如果希望视觉上跨边界仍连续，可以后续做“边界分段 Trail”，第一版不需要。

---

## 8. 3×3 视觉副本

真实 Arena：

```text
(0,0)
```

周围显示：

```text
(-1,+1) (0,+1) (+1,+1)
(-1, 0) (0, 0) (+1, 0)
(-1,-1) (0,-1) (+1,-1)
```

每个副本偏移：

```text
offset.x = ix * Width
offset.z = iz * Depth
```

### 注意

副本最好只复制：

- Floor MeshRenderer
- Wall MeshRenderer
- Props MeshRenderer
- 静态 VFX

不要复制：

- Collider
- Rigidbody
- AI
- Animator Controller 状态机
- Trigger
- AudioListener
- 战斗逻辑

第一版可以直接生成 8 个纯视觉 prefab。

---

## 9. 动态对象的“镜像影像”

如果玩家站在右边界附近，而敌人在左边界附近，理论上玩家应该直接在右侧外面看到敌人的重复像。

因此只有静态场景副本还不够。

可选两种做法：

### 做法 A：为动态对象生成 Ghost

```text
Enemy_Real
├── EnemyVisual
└── EnemyGhostRenderer
```

当 Enemy 靠近边界时，根据需要在：

```text
+Width
-Width
+Depth
-Depth
```

位置绘制 Ghost。

Ghost：

- 无 Collider
- 无 AI
- 无 Rigidbody
- 不参与 Hit
- 只复制 Sprite/Mesh/Animator 当前视觉状态

这是更适合战斗房的做法。

### 做法 B：GPU Instancing / Graphics.DrawMesh

如果后期动态对象很多，可以只复制 Renderer，而不是 GameObject。

第一版先用 Ghost GameObject。

---

## 10. 锁定与攻击方向

普通写法：

```csharp
Vector3 dir = target.position - player.position;
```

必须改为：

```csharp
Vector3 playerLocal =
    arena.transform.InverseTransformPoint(player.position);

Vector3 targetLocal =
    arena.transform.InverseTransformPoint(target.position);

Vector3 localDelta =
    TopologyMath.ShortestDelta(
        playerLocal,
        targetLocal,
        arena.Width,
        arena.Depth);

Vector3 worldDir =
    arena.transform.TransformDirection(localDelta.normalized);
```

这样角色会朝“拓扑上最近的那个目标影像”攻击。

---

## 11. AOE

圆形 AOE 判断：

```csharp
float d = TopologyDistance(center, target);
if (d <= radius)
{
    ApplyDamage();
}
```

不要依赖：

```csharp
Physics.OverlapSphere()
```

作为唯一判定。

原因是物理引擎不知道你的左右边界实际上连接在一起。

推荐：

1. 用逻辑角色列表；
2. 对每个候选目标计算拓扑距离；
3. 再进行命中。

---

## 12. 摄像机

对于当前固定斜俯视：

- Camera 本身不需要 Wrap；
- Camera 跟随真实 Player；
- Player 越界后 Camera 也会出现位置跃迁。

解决方式有两类。

### 第一版：摄像机同步瞬移

玩家发生 Wrap 时：

```text
Player delta = newPos - oldPos
CameraRig.position += delta
```

这样屏幕中 Player 不会突然跳到另一边。

### 后期：逻辑中心重定位

长期可以反过来：

- 玩家始终尽量保持在原点附近；
- 整个 WorldRoot 在幕后重新对齐。

这样更适合长时间运行，避免坐标越来越大。

---

## 13. 战斗房状态

只存一份：

```csharp
public class ArenaCombatState
{
    public int phase;
    public float bossHP;
    public bool switchA;
    public bool switchB;
}
```

视觉副本不能拥有独立状态。

否则会出现：

```text
中央 Boss 死了
右侧副本 Boss 还活着
```

---

## 14. 战斗房扩展玩法

基础循环跑通后，可以加入：

### 14.1 Boss 从右侧离开，从左侧出现

不需要特殊传送技能，直接利用 Wrap。

### 14.2 贯穿世界的投射物

Projectile 持续跨边界，直到生命周期结束。

### 14.3 跨边界夹击

两个敌人视觉上位于玩家两侧，实际上可能是同一拓扑方向附近。

### 14.4 Boss Phase 改变空间尺寸

例如：

```text
Phase 1: 16×16
Phase 2: 12×12
Phase 3: 8×8
```

注意不能直接突然修改边界，需要同时重新映射所有角色的本地位置。

### 14.5 单轴循环

某些战斗只让 X 循环：

```text
Left <-> Right
North/South 保持实体墙
```

这种更容易让玩家理解。

---

# 方案二：剧情鬼打墙 / 八卦生门

## 15. 目标效果

这一类不需要让整个战斗物理空间成为环面，而是把“门和房间的连接关系”独立出来。

例如真实场景只存在：

```text
Room_A
Room_B
Room_C
Room_D
```

逻辑上可以形成：

```text
A → B → C → B → C → B ...
```

直到触发条件：

```text
A → B → C → D
```

八卦生门则进一步把多个出口映射到不同目标，并根据当前规则实时变化。

---

## 16. 场景结构

```text
NarrativeTopologyScene
├── Systems
│   ├── NarrativeTopologyController
│   └── StoryState
│
├── Rooms
│   ├── Room_A
│   │   ├── Geometry
│   │   ├── NorthPortal
│   │   ├── EastPortal
│   │   ├── SouthPortal
│   │   └── WestPortal
│   │
│   ├── Room_B
│   ├── Room_C
│   └── Room_D
│
└── Player
```

每个 `Portal` 只负责：

- 检测玩家进入；
- 查询当前目标；
- 将玩家映射到目标 Portal；
- 根据规则修改位置与朝向。

---

## 17. Portal 数据结构

```csharp
using UnityEngine;

public class TopologyPortal : MonoBehaviour
{
    public string portalId;
    public Transform exitAnchor;
}
```

出口不直接写死：

```csharp
public TopologyPortal target;
```

而是由 `NarrativeTopologyController` 查询当前状态。

---

## 18. 拓扑连接表

```csharp
[System.Serializable]
public class PortalLink
{
    public string fromPortalId;
    public string toPortalId;

    public float yawOffset;
    public bool mirrorX;
}
```

基本规则：

```text
A_East → B_West
B_East → C_West
C_East → B_West
```

就形成：

```text
A → B → C → B → C → ...
```

玩家永远无法通过这个方向离开。

---

## 19. 玩家穿门时的位置映射

不要简单把玩家放到目标 Portal 的中心，否则会出现明显“吸附”。

正确做法是：

1. 读取玩家相对入口 Portal 的局部位置；
2. 转换为目标 Portal 的局部空间；
3. 把前后方向翻转；
4. 应用额外旋转；
5. 写回世界坐标。

示例：

```csharp
public static Vector3 MapPosition(
    Transform sourcePortal,
    Transform targetPortal,
    Vector3 worldPosition)
{
    Vector3 local =
        sourcePortal.InverseTransformPoint(worldPosition);

    // 穿过平面后，局部 Z 翻转
    local.z = -local.z;

    return targetPortal.TransformPoint(local);
}
```

方向映射：

```csharp
public static Vector3 MapDirection(
    Transform sourcePortal,
    Transform targetPortal,
    Vector3 worldDirection)
{
    Vector3 local =
        sourcePortal.InverseTransformDirection(worldDirection);

    local.z = -local.z;

    return targetPortal.TransformDirection(local);
}
```

---

## 20. 防止 Portal 立即反复触发

玩家映射到目标 Portal 后，很容易立刻再次触发目标 Trigger。

必须有：

```text
PortalCooldown
```

或者：

```text
LastPortalId
```

示例：

```csharp
public class PortalTraveller : MonoBehaviour
{
    public float cooldown = 0.15f;

    private float nextPortalTime;

    public bool CanTeleport =>
        Time.time >= nextPortalTime;

    public void MarkTeleported()
    {
        nextPortalTime = Time.time + cooldown;
    }
}
```

更稳定的做法是等玩家完全离开目标 Portal Trigger 后，再允许重新进入。

---

# A. 鬼打墙具体方案

## 21. 设计一个可玩的最小剧情循环

真实空间：

```text
Room_A：入口
Room_B：普通走廊
Room_C：异常走廊
Room_D：出口
```

逻辑：

### Loop 0

```text
A → B
B → C
C → B
```

### Loop 1

```text
A → B
B → C
C → B
```

但 B 中：

- NPC 消失；
- 灯光改变；
- 某个道具出现。

### Loop 2

C 中增加提示。

### Solved

```text
C → D
```

因此真正的“鬼打墙”并不是每次随机换房间，而是：

```text
重复结构 + 可追踪差异 + 明确解谜条件
```

---

## 22. `LoopState`

```csharp
public enum LoopState
{
    FirstPass,
    SecondPass,
    ThirdPass,
    Solved
}
```

Controller：

```csharp
public class NarrativeTopologyController : MonoBehaviour
{
    public LoopState loopState;

    public string ResolveTarget(string fromPortal)
    {
        switch (loopState)
        {
            case LoopState.FirstPass:
            case LoopState.SecondPass:
            case LoopState.ThirdPass:
                if (fromPortal == "C_East")
                    return "B_West";
                break;

            case LoopState.Solved:
                if (fromPortal == "C_East")
                    return "D_West";
                break;
        }

        return GetDefaultTarget(fromPortal);
    }

    private string GetDefaultTarget(string portalId)
    {
        // 第一版可用 Dictionary
        return null;
    }
}
```

---

## 23. 循环次数

不要完全依赖“玩家穿了几次门”。

最好分别记录：

```csharp
public class NarrativeLoopState
{
    public int loopCount;
    public bool sawMissingNPC;
    public bool inspectedSymbol;
    public bool pickedKey;
    public bool performedCorrectAction;
}
```

只有满足设计条件时才改变拓扑。

例如：

```text
看见异常符号
+
关闭灯
+
再次进入 C
=
C_East → D_West
```

---

## 24. 重复房间的变化方式

优先做小变化，而不是每轮把场景大改：

```text
第 1 轮：正常
第 2 轮：NPC 不见
第 3 轮：墙上符号出现
第 4 轮：声音方向错误
第 5 轮：出口真正打开
```

空间重复必须足够稳定，玩家才能意识到“这是同一空间再次出现”。

---

## 25. 同房间重用，而不是大量复制 Scene

例如 B 只存在一份。

玩家离开 C 时重新连接回 B：

```text
C_East → B_West
```

进入 B 前，根据 LoopState 更新 B：

```csharp
roomB.ApplyVariant(loopState);
```

示例：

```csharp
public void ApplyVariant(LoopState state)
{
    npc.SetActive(state == LoopState.FirstPass);

    strangeSymbol.SetActive(
        state >= LoopState.ThirdPass);

    lightGroup.SetVariant(state);
}
```

---

# B. 八卦生门具体方案

## 26. 基础模型

物理场景可以只做：

```text
一个中心房
+
八个门
```

门 ID：

```text
Qian
Dui
Li
Zhen
Xun
Kan
Gen
Kun
```

或者如果设计上更直接，也可以使用：

```text
Open
Rest
Life
Hurt
Block
View
Death
Fear
```

程序不要依赖中文显示名，统一使用稳定 ID。

---

## 27. 八门不是八张地图

建议把它理解为：

```text
八个 Portal
+
一张动态连接表
```

例如状态 `Phase_A`：

```text
North     → Room_B
NorthEast → Room_C
East      → Room_B
SouthEast → Room_D
South     → Center_Rotated180
...
```

玩家看到的是八个方向，但“空间邻接关系”由规则决定。

---

## 28. ScriptableObject 数据结构

```csharp
using UnityEngine;
using System.Collections.Generic;

[CreateAssetMenu(menuName = "Topology/Topology Rule Set")]
public class TopologyRuleSet : ScriptableObject
{
    public List<TopologyRuleEntry> entries;
}

[System.Serializable]
public class TopologyRuleEntry
{
    public string sourcePortalId;
    public string targetPortalId;

    public float yawOffset;
    public bool mirrorX;

    public string requiredState;
}
```

这样策划可以直接配置：

```text
State = Normal
State = WrongPath
State = CorrectSequence_1
State = CorrectSequence_2
State = Solved
```

不用每一种组合都硬编码进 C#。

---

## 29. 正确路径序列

例如谜题要求：

```text
生 → 景 → 开 → 休
```

保存：

```csharp
List<string> correctSequence;
List<string> currentSequence;
```

玩家每穿一扇门：

```csharp
currentSequence.Add(gateId);
```

判断：

```text
如果当前序列仍是正确序列的前缀：
    保留进度

如果错误：
    重置/进入惩罚空间

如果完全匹配：
    State = Solved
```

---

## 30. 错误门如何表现

不要所有错误门都直接：

```text
返回起点
```

可以用同一个底层实现不同空间结果。

### 类型 1：Loop

```text
Gate → 当前房间另一边
```

### 类型 2：Rotate

```text
Gate → 当前房间，但旋转 90°
```

### 类型 3：Mirror

```text
Gate → 镜像版本
```

### 类型 4：StateChange

```text
Gate → 同一房间 + NPC/道具状态变化
```

### 类型 5：PunishRoom

```text
Gate → 战斗房
```

### 类型 6：FalseExit

```text
看似回到正常地图
→ 走几步后发现仍在循环
```

---

## 31. 房间旋转

如果房间本身四向近似对称，可以通过出口映射制造方向错乱，而不必真的旋转 Mesh。

例如：

```text
玩家认为：
North → North

系统实际：
North → East
East  → South
South → West
West  → North
```

对应：

```text
yawOffset = +90°
```

穿门后同时旋转：

- Player forward
- Camera Rig
- 可选：输入参考方向

如果使用世界方向输入，必须决定：

```text
W 是始终“屏幕上方”
还是始终“角色前方”
```

这需要在原型阶段固定规则，否则玩家会把输入混乱误认为 Bug。

---

## 32. 镜像空间

镜像不能只写：

```csharp
transform.localScale.x *= -1;
```

因为可能导致：

- Collider 方向问题；
- Mesh 法线问题；
- 动画问题；
- 摄像机和 UI 方向异常。

建议第一版只做“逻辑镜像”：

```text
East <-> West
North 保持 North
South 保持 South
```

同时把房间中关键 Prop 使用 Mirror Variant。

真正几何镜像放在后期。

---

# 两套系统如何共用

## 33. 共用的核心抽象

两种情况统一成：

```text
Topology Transform
=
Position Mapping
+
Direction Mapping
+
Optional State Transition
```

战斗房：

```text
右边界
→ 左边界
→ 位置平移 -Width
→ 状态不变
```

鬼打墙：

```text
C_East
→ B_West
→ Portal 局部坐标映射
→ LoopState +1
```

八卦：

```text
LifeGate
→ TargetPortal
→ Rotation +90°
→ PuzzleState 更新
```

因此可以统一定义：

```csharp
public struct TopologyTransition
{
    public Transform source;
    public Transform target;

    public float yawOffset;
    public bool mirrorX;

    public string stateEvent;
}
```

---

## 34. 推荐的开发顺序

### Phase 1：先做战斗房最小验证

完成：

```text
[ ] 一个 12×12 Arena
[ ] Player X/Z Wrap
[ ] 一个 Enemy X/Z Wrap
[ ] 一个 Projectile X/Z Wrap
[ ] TopologyDistance
[ ] 3×3 静态场景副本
[ ] 边界附近 Enemy Ghost
```

验收：

```text
玩家和敌人在左右边界两侧时：
- 看起来相邻
- AI 选择最近方向
- 可以互相攻击
- Projectile 可以直接穿边界命中
```

---

### Phase 2：抽出 Portal 映射

完成：

```text
[ ] TopologyPortal
[ ] MapPosition
[ ] MapDirection
[ ] PortalCooldown
[ ] 两个独立房间互相传送
```

---

### Phase 3：鬼打墙

完成：

```text
[ ] A/B/C/D 四房
[ ] C → B 循环
[ ] LoopCount
[ ] 房间 Variant
[ ] 条件满足后 C → D
```

---

### Phase 4：八门

完成：

```text
[ ] 8 Portal
[ ] ScriptableObject RuleSet
[ ] CorrectSequence
[ ] WrongPath
[ ] Rotation Mapping
[ ] Solved Exit
```

---

### Phase 5：整合剧情 / 战斗

例如：

```text
普通剧情
↓
进入八卦阵
↓
走错门
↓
拓扑循环
↓
进入无限战斗房
↓
Boss 战
↓
击杀 Boss 改变 NarrativeTopology State
↓
生门成为真实出口
```

这样两个系统不是独立 gimmick，而是同一个空间规则系统的不同应用。

---

# 35. 当前固定斜俯视场景的具体适配

根据你现在的场景表现，建议：

### 墙体

不要让所有外围墙都是高实墙。战斗房需要玩家能看到邻接副本，至少保留：

- 栏杆；
- 低墙；
- 开放边缘；
- 局部透明墙；
- Camera 与 Player 之间墙体淡出。

### 地板

视觉格子可以保留，但不要让拓扑算法依赖 Tile 纹理。

使用：

```text
Arena local coordinate
```

作为唯一空间基准。

### 角色

2D/面片角色可以继续使用。

Ghost 只需要复制：

```text
SpriteRenderer
Animator 参数
Flip 状态
```

不复制行为逻辑。

### Camera

第一版保持现有斜俯视，不需要先做自由旋转。

八卦中的“90°空间旋转”可以先通过：

```text
Portal 映射 + Camera Rig 短时旋转
```

实现，而不必改变整个项目的摄像机体系。

---

# 36. 常见 Bug 清单

## 战斗房

### Bug：敌人隔着边界明明很近却往反方向跑

原因：

```text
使用普通 Vector3.Distance / target.position - self.position
```

修复：

```text
所有距离和方向改用 TopologyMath.ShortestDelta
```

### Bug：AOE 在边界失效

原因：

```text
只依赖 Physics.OverlapSphere
```

修复：

```text
候选对象 + TopologyDistance
```

### Bug：Projectile 穿边界出现长拖尾

修复：

```text
Wrap 时清 TrailRenderer
```

### Bug：角色越界后 Camera 突跳

修复：

```text
把同一 Wrap Delta 应用到 Camera Rig
```

### Bug：副本敌人被打中两次

原因：

```text
Ghost 拥有 Collider
```

修复：

```text
所有视觉 Ghost 禁止碰撞与 Gameplay Script
```

---

## 鬼打墙 / 八卦

### Bug：进入目标门后立即被传回

原因：

```text
目标 Portal Trigger 立即再次触发
```

修复：

```text
Cooldown / ExitTrigger Gate
```

### Bug：每次穿门玩家位置都吸附到门中心

原因：

```text
没有映射相对入口位置
```

修复：

```text
InverseTransformPoint → TransformPoint
```

### Bug：场景变化太大，玩家意识不到在循环

修复：

```text
保持主体布局稳定，只改变少量关键叙事对象
```

### Bug：八卦规则最后变成大量 if/else

修复：

```text
RuleSet + 状态机 + Portal ID
```

---

# 37. Debug 工具必须做

拓扑系统肉眼非常容易误判，因此建议一开始就做 Debug HUD：

```text
Current Room: B
Current Portal: B_East
Resolved Target: C_West

Loop Count: 2
Puzzle State: WrongPath

Arena Local Pos: (5.7, 0, -1.2)
Topology Target Delta: (0.4, 0, 1.1)
```

Scene Gizmos 显示：

```text
绿色线：当前 Portal 实际连接
黄色线：条件连接
红色线：当前错误路径
```

对于战斗 Arena，在 Scene View 画出：

```text
Arena Bounds
+
3×3 Replica Bounds
```

否则后续非常难排查“玩家看到的是哪一个副本”。

---

# 38. 性能策略

第一版不用过度优化。

优先确保：

```text
1 个真实 Arena
8 个静态视觉副本
少量动态 Ghost
```

而不是：

```text
9 套完整 Arena
9 套 AI
9 套 Collider
9 套 Animator Logic
```

当动态 Ghost 数量超过几十个以后，再考虑：

- Renderer Pool；
- Graphics.DrawMeshInstanced；
- GPU Instancing；
- 减少远处 Animator 更新。

---

# 39. 最终推荐架构

```text
TopologySystem
│
├── TopologyMath
│   ├── MapPosition()
│   ├── MapDirection()
│   ├── ShortestDelta()
│   └── TopologyDistance()
│
├── CombatTopology
│   ├── ToroidalArena
│   ├── Traveller
│   ├── ReplicaRenderer
│   ├── GhostRenderer
│   └── TopologyCombatQuery
│
└── NarrativeTopology
    ├── Portal
    ├── PortalGraph
    ├── RuleSet
    ├── StoryState
    ├── LoopRule
    └── BaguaRule
```

这套拆法可以让：

```text
“战斗房无限”
“剧情鬼打墙”
“八卦生门”
“旋转空间”
“错误出口”
“同房间不同状态”
```

共享底层，而不是每个玩法重新写一套传送脚本。

---

# 40. 第一版原型的实际工作量建议

先不要同时实现所有效果。

最小版本：

```text
Day / Session 1
- ToroidalArena
- Player Wrap
- Enemy Wrap
- TopologyDistance

Day / Session 2
- Projectile Wrap
- 3×3 Replica
- Enemy Ghost

Day / Session 3
- TopologyPortal
- A/B/C 三房循环
- PortalCooldown

Day / Session 4
- LoopState
- Room Variant
- 正确条件解除鬼打墙

Day / Session 5
- 8 门 RuleSet
- 正确序列
- 错误路径
- 90° Rotation Mapping
```

做到这里，两种玩法已经都能形成可玩的技术 Demo。

---

# 41. 参考资料

以下 API 是本方案直接依赖的 Unity 官方接口：

1. Unity `Transform.TransformPoint`：用于把 Portal/Arena 的局部坐标转换回世界坐标。  
   https://docs.unity3d.com/2023.2/Documentation/ScriptReference/Transform.TransformPoint.html

2. Unity `Transform.InverseTransformPoint`：用于把玩家/对象世界位置转换到 Arena 或 Portal 局部空间。  
   https://docs.unity3d.com/2022.3/Documentation/ScriptReference/Transform.InverseTransformPoint.html

3. Unity `Rigidbody.position`：Unity 官方文档区分了连续运动与位置重定位；拓扑越界属于重定位场景。  
   https://docs.unity3d.com/2022.3/Documentation/ScriptReference/Rigidbody-position.html

4. Unity `Rigidbody.MovePosition`：适用于固定物理步中的连续运动，不建议拿它代替本方案中的跨边界瞬时映射。  
   https://docs.unity3d.com/ScriptReference/Rigidbody.MovePosition.html

说明：本文所称 “Manifold Garden 风格” 指视觉与玩法层面的有限空间重复、周期边界与空间连接重映射，并不主张上述实现就是该游戏源码或内部工程的实际实现方式。
