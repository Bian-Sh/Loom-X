# LoomX Split Divider 液态磁吸交互 Spec

## 1. 设计目标
为 LoomX 左右面板之间的 Split Divider 增加一种基于鼠标接近的隐式交互。

目标不是制作传统 Hover Button，而是让 Divider 本身成为可探索的交互材质：

> 鼠标靠近 → Divider 中央被“吸出”液态鼓包 → 鼓包沿鼠标上下移动 → Divider 局部产生受约束的液态挤压 → 鼓包内部出现方向提示 → 点击完成折叠/展开。

视觉与交互参考：
- MagPoint：https://github.com/satocchi0416sh/magpoint
- 参考重点：鼠标 proximity、局部磁性形变、液态/柔性响应。
- LoomX 只采用垂直方向的一维跟随，不需要完整二维磁场。

## 2. 适用对象与架构原则
- 目标对象是 LoomX 左右面板之间的垂直 Split Divider。
- 磁性效果只作用于 Divider 的视觉层和交互层。
- 鼠标靠近、上下移动时不得改变实际 Split Layout。
- 只有点击交互区域后才真正执行 Split Toggle。

推荐结构：

Split Layout
├─ Left Panel
├─ Right Panel
└─ Divider
   ├─ Hit Area
   └─ Visual Layer
      └─ Magnetic Liquid Deformation

核心原则：视觉形变 ≠ Layout 位置变化。

## 3. 中央交互区域
Divider 中央预留一个很短的隐式交互区域，设计目标约为 1cm 的视觉长度。

建议第一版以 DPI / UI Scale 适配后的约 32～40px 作为初始实现值，而不是硬编码物理厘米。

交互区域同时承担：
1. Proximity 响应区域。
2. 鼓包的主要活动区域。
3. 鼠标点击命中区域。

但它不应表现成传统按钮的矩形 Hitbox。

## 4. 鼠标接近检测
主要使用鼠标 Y 坐标计算与 Divider 中央交互区域的距离。

定义：
- DividerCenterY：Divider 中央 Y。
- MouseY：当前鼠标 Y。
- DetectionRange：磁性检测范围。

distance = abs(MouseY - DividerCenterY)

当 distance > DetectionRange 时为 Idle；否则进入 Tracking / Deforming。

第一版建议 DetectionRange = 120px。

进入范围后使用平滑 proximity response，避免刚进入范围就突然出现明显形变：

t = 1 - distance / DetectionRange
response = smoothstep(0, 1, t)

## 5. 鼓包是核心视觉对象
鼠标靠近 Divider 后，中央产生一个液态鼓包。

关键要求：鼓包的位置必须跟随鼠标上下移动。

错误模型：Mouse → 固定中央 Button。

正确模型：
Mouse Y → Target Bulge Y → Spring / Damping → Actual Bulge Y

### 5.1 鼓包 Y 位置
鼓包中心由鼠标 Y 驱动，但必须受到活动范围限制：

BulgeY = clamp(MouseY, DividerCenterY - MaxTravel, DividerCenterY + MaxTravel)

建议第一版 MaxTravel = 80px。

鼠标可以继续上下移动，但鼓包不能脱离 Divider 中央的设计区域。

### 5.2 液态跟随感
不要直接让 BulgeY = MouseY。

应存在轻微的跟随延迟、弹性和阻尼，使鼠标上下移动时形成类似液体/磁性软物质的“撸动”感觉。

鼓包和内部方向指示器必须作为一个视觉整体移动。

## 6. 第一阶段：垂直对称展开
鼠标靠近后，Divider 中央区域首先沿 Y 轴对称展开。

展开以鼓包中心为锚点，向上和向下同时增长。

目标：ExpandedLength = OriginalLength × 2。

禁止只向上或只向下增长。

## 7. 第二阶段：向左侧挤压
当中央区域完成主要的垂直展开后，继续增强磁性响应时，局部 Divider 产生向左侧的挤压/偏移。

注意：不是把整个 Divider 的 X 坐标向左移动，而是中央局部区域产生连续形变。

远离交互区域的 Divider 必须保持稳定。

完整视觉过程：
Mouse Proximity → Vertical Symmetric Expansion → Reach ~2× length → Local Leftward Compression → Continuous Fill

## 8. 空白区域必须连续填充
局部形变产生的空白区域不能出现明显断裂或空洞。

填充区域应保持 Divider / 液态材质的连续视觉。

如果 Divider 使用 Liquid Glass / 半透明材质，形变产生的填充区域必须继承相同视觉材质。

## 9. 鼓包内部的方向指示器
鼓包内部增加一个纯白色小三角形。

它不是装饰，而是操作语义提示。

最重要的规则：三角形表示“点击之后要执行的动作方向”，而不是当前 Split 状态。

### 9.1 当前为 Expanded
点击后：Expanded → Collapsed。
因此显示纯白向左三角：◀。

### 9.2 当前为 Collapsed
点击后：Collapsed → Expanded。
因此显示纯白向右三角：▶。

| 当前 Split 状态 | 点击动作 | 指示器 |
| --- | --- | --- |
| Expanded | Collapse | ◀ |
| Collapsed | Expand | ▶ |

## 10. 鼠标上下移动时，三角形也必须跟随
鼠标在检测区域内上下移动时：
- 鼓包跟随鼠标 Y。
- Divider 局部形变跟随鼓包。
- 三角形跟随鼓包。
- 三角形不固定在 Divider 中心。

目标体验：鼠标像在“撸动”一块藏在 Divider 中的磁性液体，鼓包随光标上下游动。

## 11. Expanded / Collapsed 两种状态
两种 Split 状态共用完全相同的磁性视觉响应。

Mouse Proximity → Bulge Tracking → Vertical Expansion → Left Compression → Arrow。

状态只决定点击后的目标：
- Expanded → click → Collapsed。
- Collapsed → click → Expanded。

不要为 Expanded / Collapsed 编写两套不同的磁性动画。

## 12. 点击逻辑
当 Pointer 在中央 Interaction Zone 内发生有效点击：

PointerDown → PointerUp → Hit Test = Interaction Zone → ToggleSplit()

要求：
- 交互区域内点击必须触发 Toggle。
- Expanded 点击后进入 Collapsed。
- Collapsed 点击后进入 Expanded。
- 交互区域外点击不得触发 Toggle。
- 磁性视觉形变不得改变真实 Split Layout。
- 鼠标上下移动不得误触发点击。
- 建议按照正常 PointerDown / PointerUp 语义判断点击，而不是仅监听 PointerDown。

## 13. 鼠标离开与恢复
当鼠标离开 DetectionRange：Tracking / Deforming → Returning → Idle。

鼓包、局部 Divider 形变和指示器都应平滑恢复。

推荐 Spring、Damping 或 Ease-Out；避免瞬间 Reset、明显弹跳和长时间振荡。

最终必须完全恢复到普通 Divider。

## 14. 状态模型
视觉交互状态：
Idle → Tracking → Deforming → Returning → Idle。

Split 状态独立：
SplitState = Expanded | Collapsed。

两者正交，不要混为一个状态机。

## 15. 推荐参数
SplitDividerMagneticInteraction：
- interactionZone.height = 40px
- detection.range = 120px
- bulge.maxTravel = 80px
- bulge.maxLengthMultiplier = 2.0
- deformation.direction = left
- deformation.maxDisplacement = 18px
- tracking 使用 spring / damping
- indicator.color = white
- indicator.size ≈ 6px
- indicator.expandedStateDirection = left
- indicator.collapsedStateDirection = right

其中垂直方向是主要运动轴；最大长度约 2×、向左挤压、鼓包跟随鼠标 Y、箭头语义均属于设计约束，不应被实现随意改变。

## 16. 技术实现原则
### 16.1 不要直接移动 Split Divider Transform
错误：Divider.transform.translateX(...)。

正确：Split Layout 保持稳定；Divider Layout Position 固定；仅 Divider Visual Layer 做 Local Deformation。

### 16.2 一维输入、局部二维视觉
输入主要是 MouseY，但视觉结果包含：
1. BulgeY。
2. Vertical Expansion。
3. Local X Compression。
4. Arrow Position。

因此不需要复制 MagPoint 的完整二维磁场算法。

### 16.3 优先使用 LoomX 当前 UI 技术栈
第一版优先使用现有 UI 能稳定实现的 Mask / Clip、Border / Path、Transform、Scale、Translate、Spring / easing；必要时再使用 SVG / Path deformation。

不要为了这个单一交互引入新的 WebView、Three.js 或大型渲染依赖。

## 17. MagPoint 参考
参考仓库：https://github.com/satocchi0416sh/magpoint

参考目的：
1. 研究鼠标 proximity 到局部形变的映射方式。
2. 研究受约束磁性运动。
3. 研究液态/柔性轮廓在光标接近时的视觉响应。
4. 参考其交互调参方式。

不要求原样复制 MagPoint 的 UI、二维磁场、完整运行时架构，也不要求 LoomX Divider 在 X/Y 两个方向自由追踪鼠标。

LoomX 的明确差异：MagPoint 是二维磁性交互参考；LoomX Split Divider 是垂直方向的一维磁性液态交互。

## 18. 验收标准
### 默认状态
- [ ] Divider 为普通垂直分割线。
- [ ] 中央存在约 1cm 视觉长度的隐式交互区域。
- [ ] 不显示传统按钮。
- [ ] 不显示 Tooltip。

### 鼠标接近
- [ ] 进入 DetectionRange 后响应平滑出现。
- [ ] 中央 Divider 首先沿 Y 轴对称展开。
- [ ] 最大长度约为原始长度 2×。
- [ ] 鼓包中心沿 Y 轴跟随鼠标。
- [ ] 鼓包跟随具有弹性和阻尼。
- [ ] 鼠标上下移动时鼓包连续上下移动。
- [ ] 鼓包不会超出 MaxTravel。
- [ ] 随后产生局部向左挤压。
- [ ] 形变不会移动真实 Split Layout。
- [ ] 形变区域连续填充，不出现明显空洞。

### 方向提示
- [ ] Expanded 状态显示纯白 ◀。
- [ ] Collapsed 状态显示纯白 ▶。
- [ ] 箭头跟随鼓包上下移动。
- [ ] 箭头表达点击后的动作方向。

### 点击
- [ ] Interaction Zone 内点击触发 Split Toggle。
- [ ] Expanded → Collapsed。
- [ ] Collapsed → Expanded。
- [ ] 区域外点击不会触发。
- [ ] 鼠标移动不会误触发点击。

### 离开
- [ ] 离开 DetectionRange 后形变平滑恢复。
- [ ] 无瞬移。
- [ ] 无明显抖动。
- [ ] 最终完全恢复普通 Divider。

## 19. 核心体验定义
普通 Divider → 鼠标靠近 → “这里好像有东西” → 液态鼓包出现 → 鼓包跟着鼠标上下撸动 → Divider 被局部挤压 → ◀ / ▶ 暗示操作方向 → 点击 → Split 折叠 / 展开。

核心原则：
> 不是让一个按钮追随鼠标，而是让 Divider 中隐藏的交互能力被鼠标“探索出来”。