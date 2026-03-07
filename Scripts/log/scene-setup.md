
## 一、场景结构（最小可运行）

- Scene: RiftOfFogMap（或任意场景名）
- Main Camera（保留默认即可）
- EventSystem（UI 点击需要，保留默认）
- RiftOfFog（空物体）
  - MapManager（挂 **game/MapManager** 脚本）
  - PlayerStats（挂 **game/RiftOfFogPlayerStats** 脚本）
  - **WebSocketClient**（空物体，挂 **game/WebSocketClient** 脚本，用于连接网关、收发 AI 建议与行为结果）
  - Canvas_Map（UI 画布）
    - MapContent（空物体，带 RectTransform，拖给 MapManager.mapContent）

---

## 二、步骤

### 1. 创建画布与地图容器

1. 右键 Hierarchy → UI → Canvas，命名为 Canvas_Map。
2. 选中 Canvas_Map：Render Mode 用 Screen Space - Overlay；Canvas Scaler 用 Scale With Screen Size，例如 1920×1080，Match = 0.5。
3. 在 Canvas_Map 下 Create Empty，命名为 MapContent。
4. 选中 MapContent：添加 Rect Transform；Anchor 可 Stretch 全屏；Width/Height 建议 3000×1200 以便拖拽。

### 2. 挂 MapManager 并绑定引用

1. 根下 Create Empty 命名 RiftOfFog，其下再 Create Empty 命名 MapManager。
2. 选中 MapManager，Add Component → **MapManager**（脚本在 Assets/game/ 下）。
3. Inspector 中 Map Content 拖入 MapContent；Map Root 可空；Map Width/Height/Path Density 默认 20、7、4 即可。

运行后 mapContent 已赋值则 Start() 会自动 InitMap()，生成节点与连线。

### 2.5 挂 WebSocketClient（对接服务端）

1. 在 RiftOfFog 下 Create Empty，命名 WebSocketClient。
2. Add Component → **WebSocketClient**（脚本在 Assets/game/ 下）。
3. 无需在 Inspector 中填引用；运行时会自动连接 `ws://localhost:8080/ws`，并订阅消息/行为事件。确保先启动 go-server 网关再运行 Unity，否则会走本地模拟建议。

### 3. 人物属性（推荐）

1. 在 RiftOfFog 下 Create Empty，命名 PlayerStats。
2. Add Component → RiftOfFogPlayerStats。
3. Inspector 中设置 Base（HP/AP/STR/INT/LCK/STEALTH）、Negative/Positive/Fatigue、AIMemory。

进入 A/B/C 房间占位 UI 时若有 RiftOfFogPlayerStats.Instance 会显示当前属性。

### 4. 可选：地图节点图标

在 Assets/Resources/MapNodes/ 下放入 Combat、Event、Resource、PathChoice 的 Sprite（如 Combat.png）。不放则用代码内灰色占位图。详见 RESOURCES_README.md。

