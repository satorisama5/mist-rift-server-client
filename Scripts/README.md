# 《迷雾裂隙》Rift of Fog — C# 客户端脚本

本目录为 **Unity C# 客户端** 的脚本集合，对应游戏《迷雾裂隙》（Rift of Fog）：Roguelike 地牢地图 + 四种房间（战斗 / 事件 / 资源 / 路径选择），通过 **WebSocket** 与后端网关通信，支持自然语言输入与 AI 建议（听从/忽略）及行为结算。

---

## 项目简介

- **运行环境**：Unity（需 .NET 4.x / Unity 2021+ 等支持 `System.Net.WebSockets` 的版本）。
- **与后端关系**：客户端连接服务端 WebSocket（默认 `ws://127.0.0.1:8080/ws`），发送场景上下文 + 玩家消息或行为键，接收 `llm_suggestion`（AI 建议）或 `action_result`（HP/AP 等状态变化）。服务端仓库见 **rift-of-fog-server**（Go 网关 + gRPC + LLM + Qdrant）。
- **本仓库内容**：仅 **Scripts** 脚本与文档，不包含 Unity 工程完整资源；需自行在 Unity 中创建场景并挂载脚本。

---

## 目录结构

```
Scripts/
├── game/                          # 游戏逻辑与网络
│   ├── WebSocketClient.cs         # WebSocket 连接、收发队列、与网关协议
│   ├── RiftOfFogMessageBridge.cs  # 房间内消息/行为事件桥接（发→WebSocketClient）
│   ├── RiftOfFogSceneContext.cs   # 当前场景上下文 JSON（供发送给服务端）
│   ├── RiftOfFogPlayerStats.cs    # 人物属性与状态、ApplyStateChanges
│   ├── RiftOfFogRoomUI.cs         # 战斗/事件/资源/路径选择房间 UI、听从建议/忽略
│   ├── RiftOfFogSetupUI.cs        # 开局属性设置面板
│   ├── RiftOfFogMapResources.cs   # 地图节点图标加载（Resources/MapNodes/）
│   └── RiftOfFogMapSaveData.cs    # 地图存档数据结构（可选）
├── map/                           # 地图与节点
│   ├── MapManager.cs              # 地图生命周期、房间点击、进入房间、解锁下一层
│   ├── MapGenerator.cs            # 地牢网格与路径生成（起点、分叉、防交叉）
│   ├── MapRoomNode.cs             # 单节点数据与表现（状态、闪烁、Hitbox）
│   ├── MapRoomNodeClick.cs        # 节点点击 → MapManager.OnRoomClicked
│   ├── MapEdge.cs                 # 节点间连线（虚线点、已选路径高亮）
│   └── HitBox.cs                  # 节点悬浮/点击检测
├── log/                           # 说明与配置
│   ├── scene-setup.md             # 场景搭建步骤（MapManager、WebSocketClient、PlayerStats 等）
│   ├── resources.md               # 地图图标资源说明（Resources/MapNodes/）
│   └── ...
└── README.md                      # 本文件
```

---

## 核心流程简述

1. **地图**：`MapManager` 驱动 `MapGenerator` 生成网格与边，第一列节点可用，点击后根据房间类型进入对应 UI 或路径选择。
2. **房间内**：`RiftOfFogRoomUI` 展示行为按钮与自然语言输入框；玩家输入或点击行为 → `RiftOfFogMessageBridge` 触发事件 → `WebSocketClient` 用 `RiftOfFogSceneContext.GetCurrentContext()` 拼出 JSON 并发送。
3. **收包**：`WebSocketClient` 在主线程派发 `OnServerResponseReceived`；若为 `llm_suggestion` 则展示建议与【听从建议】【忽略】，若为 `action_result` 则 `RiftOfFogPlayerStats.ApplyStateChanges` 并关房、解锁下一层。

---

## 快速接入

1. 将本目录放入 Unity 项目 **Assets/Scripts**（或通过 asmdef 组织）。
2. 按 **log/scene-setup.md** 创建场景、画布、空物体，挂载 **MapManager**、**WebSocketClient**、**RiftOfFogPlayerStats** 等。
3. 如需地图图标，在 **Resources/MapNodes/** 下放置 Combat / Event / Resource / PathChoice 的 Sprite，参见 **log/resources.md**。
4. 启动后端网关（如 `go run ./cmd/server`），运行 Unity，连接 `ws://127.0.0.1:8080/ws`；未连接时房间内为本地模拟建议。

---

## 协议约定

- **发送**：JSON 与后端 `UnityRequest` 一致（room_type、message 或 action_key、hp/ap/str/int_val/lck/stealth/status、recent_decisions、path_hints、future_paths、session_id、type 等），由 `RiftOfFogSceneContext` 与 `WebSocketClient.BuildRequest` 拼装。
- **接收**：JSON 与后端 `ServerResponse` 一致（type、service、action、result、suggestion、state_changes 等），见 `WebSocketClient.ServerResponseData`。

---

## 许可与说明

脚本仅供学习与参考；若用于其他项目请遵守项目自身许可。服务端见 **rift-of-fog-server** 仓库。
