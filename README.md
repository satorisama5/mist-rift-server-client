# Rift of Fog Server

**《迷雾裂隙》服务端** — 基于 Go 的 WebSocket 网关 + gRPC 微服务 + LLM 意图路由 + Qdrant RAG，为 Roguelike 地牢游戏提供自然语言理解与房间行为结算。

---

## 项目简介

本仓库为《迷雾裂隙》（Rift of Fog）的**服务端实现**，与 Unity 客户端通过 WebSocket 通信。玩家在战斗/事件/资源房中可用自然语言下达指令（如「潜行过去」「先调查」），服务端结合**玩家风格**与**历史相似决策**（Qdrant 向量检索）调用大模型做意图分类，再按房间类型将行为路由到 Combat / Event / Resource 三个 gRPC 微服务执行数值检定与状态结算；结果写回客户端，决策异步写入 Qdrant 供后续 RAG 使用。

**核心能力**：自然语言 → LLM 意图路由（含关键词兜底）→ RAG 增强上下文 → gRPC 行为执行 → Redis 审计与 Qdrant 决策记忆。

---

## 技术栈

| 层级 | 技术 |
|------|------|
| 网关 | Go 1.24、Gorilla WebSocket、标准库 HTTP |
| 微服务 | gRPC、Protobuf（Combat / Event / Resource 三服务） |
| AI | 豆包 Ark（Chat 意图分类 + Multimodal Embeddings） |
| 记忆与 RAG | Qdrant（决策向量 + 玩家风格）、Python FastAPI memory-rag（:8001） |
| 存储与审计 | Redis（交换记录、会话缓存） |

---

## 目录结构

```
.
├── go-server/                 # Go 服务端（网关 + 协议 + 内存/RAG 客户端）
│   ├── cmd/
│   │   ├── server/            # 主入口：HTTP + WebSocket，连 Redis、gRPC、memory-rag
│   │   ├── combat/            # Combat gRPC 服务 (:50051)
│   │   ├── event/             # Event gRPC 服务 (:50052)
│   │   └── resource/          # Resource gRPC 服务 (:50053)
│   ├── internal/
│   │   ├── gateway/           # WebSocket 处理、意图路由（豆包）、路由注册
│   │   ├── protocol/          # 与 Unity 及业务层协议定义
│   │   ├── memory/            # 调 memory-rag 的 HTTP 客户端（SaveDecision / SearchSimilar / GetPlayerStyle）
│   │   ├── repository/        # Redis 交换记录与会话缓存
│   │   └── services/          # Combat/Event/Resource 行为与检定逻辑
│   ├── api/proto/riftofog/    # Protobuf 定义
│   ├── pkg/protocol/          # 由 proto 生成的 Go 代码
│   └── memory-rag/            # Python：FastAPI + Qdrant + 豆包 Embedding（:8001）
├── 系统思维导图与学习指南.md   # 架构与学习路径说明
├── 面试项目介绍.md             # 技术栈 / 功能 / 亮点（面试用）
└── README.md                  # 本文件
```

*Unity 客户端与地图等逻辑在独立仓库或同仓库其他目录，本仓库仅服务端。*

---

## 快速运行

1. **环境**：Go 1.24+、Python 3.x（memory-rag）、Redis、Qdrant。可选：豆包 API Key（无则走关键词兜底）。

2. **配置**：在 `go-server/` 下创建 `.env`，例如：
   ```env
   REDIS_ADDR=localhost:6379
   HTTP_ADDR=:8080
   DOUBAO_API_KEY=your_key
   ```
   memory-rag 需 `DOUBAO_API_KEY`（Embedding + 可选 Chat）。

3. **启动顺序**（示例）：
   ```bash
   # 1. Redis、Qdrant 已启动（如 Docker）
   # 2. memory-rag（在 go-server/memory-rag 下）
   pip install -r requirements.txt && python main.py
   # 3. 三个 gRPC 服务（各开一终端）
   go run ./cmd/combat
   go run ./cmd/event
   go run ./cmd/resource
   # 4. 网关
   go run ./cmd/server
   ```
   网关监听 `:8080`，WebSocket 路径 `/ws`；Unity 连接 `ws://127.0.0.1:8080/ws`。

4. **健康检查**：`GET /health`、`GET /health/doubao`（检测豆包可用性）。

---

## 协议与文档

- 与客户端的请求/响应结构见 `go-server/internal/protocol/protocol.go`（UnityRequest、ServerResponse、StateChanges）。
- gRPC 定义见 `go-server/api/proto/riftofog/game.proto`。
- 架构与 LLM/RAG 流程见仓库内 **《系统思维导图与学习指南》**、**《面试项目介绍》**。

---

## 开源与许可

按项目实际许可说明使用；若未单独声明，可视为仅供学习与参考。
