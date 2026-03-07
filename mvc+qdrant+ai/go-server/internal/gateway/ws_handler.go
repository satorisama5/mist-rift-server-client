package gateway

import (
	"context"
	"encoding/json"
	"fmt"
	"log"
	"net/http"
	"strings"
	"time"

	"go-server/internal/memory"
	"go-server/internal/protocol"
	"go-server/internal/repository"
	riftofog "go-server/pkg/protocol"

	"github.com/google/uuid"
	"github.com/gorilla/websocket"
)

const (
	wsReadDeadline  = 5 * time.Minute  // 单条消息最长等待时间，超时则断开，避免客户端不发包导致 goroutine 永久阻塞
	wsWriteDeadline = 15 * time.Second // 单次写响应超时
)

const maxRecentDecisions = 10

// 与常见客户端（含 Unity/.NET ClientWebSocket）兼容：仅做协议升级，不限制 Origin
var upgrader = websocket.Upgrader{
	ReadBufferSize:  1024,
	WriteBufferSize: 1024,
	CheckOrigin:     func(r *http.Request) bool { return true },
}

// Handler 处理 WebSocket 连接，与 Unity 通信并落库。通过 gRPC 调用 Combat/Event/Resource 微服务。
type Handler struct {
	Redis    *repository.Repository
	Combat   riftofog.CombatServiceClient
	Event    riftofog.EventServiceClient
	Resource riftofog.ResourceServiceClient
}

// ServeWS 将 HTTP 升级为 WebSocket，按完整流程处理每条 UnityRequest。
// 并发说明：每个连接仅此 goroutine 读写 conn，顺序为 读 -> 处理 -> 写，无锁、无并发读写同一 conn。
func (h *Handler) ServeWS(w http.ResponseWriter, r *http.Request) {
	// 兼容部分客户端（如 Unity/.NET）发送的 Sec-WebSocket-Version 非 13 或格式不同：
	// gorilla/websocket 只认 13，先统一成 13 再升级，避免 "unsupported version: 13 not found"。
	if v := r.Header.Get("Sec-WebSocket-Version"); v != "13" {
		r.Header.Set("Sec-WebSocket-Version", "13")
	}
	conn, err := upgrader.Upgrade(w, r, nil)
	if err != nil {
		log.Printf("[ws] upgrade: %v", err)
		return
	}
	defer conn.Close()

	sessionID := uuid.New().String()
	log.Printf("[ws] client connected session=%s (Unity 已连上)", sessionID[:8])
	ctx := context.Background()

	for {
		conn.SetReadDeadline(time.Now().Add(wsReadDeadline))
		_, raw, err := conn.ReadMessage()
		if err != nil {
			if websocket.IsUnexpectedCloseError(err, websocket.CloseGoingAway, websocket.CloseAbnormalClosure) {
				log.Printf("[ws] read: %v", err)
			}
			break
		}
		log.Printf("[ws] >>> received %d bytes", len(raw))

		var req UnityRequest
		if err := json.Unmarshal(raw, &req); err != nil {
			log.Printf("[ws] unmarshal request: %v", err)
			conn.SetWriteDeadline(time.Now().Add(wsWriteDeadline))
			_ = conn.WriteJSON(ServerResponse{Type: "action_result", Result: "请求格式错误"})
			continue
		}

		if req.Message != "" {
			log.Printf("[ws] request session=%s room=%s message=%q", sessionID[:8], req.RoomType, req.Message)
		} else {
			log.Printf("[ws] request session=%s room=%s action=%s", sessionID[:8], req.RoomType, req.ActionKey)
		}

		resp := h.handleRequest(ctx, sessionID, &req)

		if resp.Type == "llm_suggestion" {
			log.Printf("[ws] response type=llm_suggestion action=%s suggestion=%q", resp.Action, resp.Suggestion)
		} else {
			log.Printf("[ws] response type=action_result service=%s action=%s result=%q", resp.Service, resp.Action, resp.Result)
		}

		// Redis：交换记录 + 会话缓存（SaveExchange 内已写 session:{id}）
		if h.Redis != nil {
			if err := h.Redis.SaveExchange(ctx, sessionID, &req, resp); err != nil {
				log.Printf("[ws] redis save: %v", err)
			} else {
				log.Printf("[ws] redis saved session=%s (list rift:ws:exchanges + key session:%s)", sessionID[:8], sessionID[:8])
			}
		}

		conn.SetWriteDeadline(time.Now().Add(wsWriteDeadline))
		if err := conn.WriteJSON(resp); err != nil {
			log.Printf("[ws] write: %v", err)
			break
		}

		// 异步写入 Qdrant：本次决策（不阻塞返回）；使用原始 req.RoomType 作为 room_type，避免 LLM 分类错误导致记录混乱
		if resp.Service == "Combat" || resp.Service == "Event" || resp.Service == "Resource" {
			scene := buildScene(&req)
			roomTypeForSave := req.RoomType
			go func(sid, sc, act, res, rt string) {
				if err := memory.SaveDecision(sid, sc, act, res, rt); err != nil {
					log.Printf("[ws] qdrant save_decision err: %v", err)
				} else {
					log.Printf("[ws] qdrant save_decision ok session=%s room=%s action=%s", sid[:8], rt, act)
				}
			}(sessionID, scene, req.ActionKey, resp.Result, roomTypeForSave)
		}
	}
}

func truncMsg(s string, max int) string {
	if len(s) <= max {
		return s
	}
	return s[:max] + "..."
}

// buildScene 用 room_type + 属性 + recent_decisions 拼成用于检索的 scene 文本。
func buildScene(req *UnityRequest) string {
	part := fmt.Sprintf("room_type:%s hp:%d ap:%d str:%d int:%d lck:%d stealth:%d status:%s current_goal:%s",
		req.RoomType, req.Hp, req.Ap, req.Str, req.IntVal, req.Lck, req.Stealth, req.Status, req.CurrentGoal)
	if len(req.RecentDecisions) > 0 {
		part += " recent_decisions:" + strings.Join(req.RecentDecisions, " | ")
	}
	if len(req.PathHints) > 0 {
		part += " path_hints:" + strings.Join(req.PathHints, " | ")
	}
	if len(req.FuturePaths) > 0 {
		part += " future_paths:" + strings.Join(req.FuturePaths, " | ")
	}
	return part
}

// handleRequest 完整流程：玩家风格 → Qdrant 检索 → 意图路由 → 微服务执行 → 组装 ServerResponse。
func (h *Handler) handleRequest(ctx context.Context, sessionID string, req *UnityRequest) *ServerResponse {
	isNaturalLang := req.Message != ""
	log.Printf("[ws] handleRequest start natural_lang=%v room=%s message=%q", isNaturalLang, req.RoomType, truncMsg(req.Message, 60))

	// 0) 获取玩家风格并追加到上下文，供 LLM 参考
	if style := memory.GetPlayerStyle(sessionID); style != "" {
		req.RecentDecisions = append(req.RecentDecisions, "玩家风格:"+style)
	}

	// 1) 用 scene 检索历史相似决策，追加到 RecentDecisions（限制条数）
	scene := buildScene(req)
	log.Printf("[ws] step1 SearchSimilar start (scene len=%d)", len(scene))
	similar := memory.SearchSimilar(scene)
	log.Printf("[ws] step1 SearchSimilar done len=%d", len(similar))
	if len(similar) > 0 {
		log.Printf("[ws] qdrant search_similar returned %d decisions", len(similar))
	}
	seen := make(map[string]bool)
	for _, s := range req.RecentDecisions {
		seen[s] = true
	}
	for _, s := range similar {
		if !seen[s] {
			seen[s] = true
			req.RecentDecisions = append(req.RecentDecisions, s)
		}
	}
	if len(req.RecentDecisions) > maxRecentDecisions {
		req.RecentDecisions = req.RecentDecisions[len(req.RecentDecisions)-maxRecentDecisions:]
	}

	// 2) 自然语言时做意图分类，得到 action_key / reasoning。一律先返回 llm_suggestion，让客户端显示「听从建议/忽略」，不直接执行
	var aiReasoning string
	if req.Message != "" {
		log.Printf("[ws] step2 RouteIntent start")
		_, actionKey, reasoning, _ := RouteIntent(ctx, req)
		log.Printf("[ws] step2 RouteIntent done actionKey=%q reasoning=%q", actionKey, truncMsg(reasoning, 80))
		req.ActionKey = actionKey
		aiReasoning = reasoning
		if actionKey == "" {
			log.Printf("[ws] handleRequest done type=llm_suggestion (no action), sending to client")
			return &ServerResponse{
				SessionID:   req.SessionID,
				Type:        "llm_suggestion",
				Service:     req.RoomType,
				AIReasoning: aiReasoning,
				Suggestion:  aiReasoning,
			}
		}
		// 有推荐动作时也先返回建议，由用户选择「听从建议」或「忽略」，不直接执行、不关房
		log.Printf("[ws] handleRequest done type=llm_suggestion, sending to client so user can choose")
		return &ServerResponse{
			SessionID:   req.SessionID,
			Type:        "llm_suggestion",
			Service:     req.RoomType,
			Action:      actionKey,
			AIReasoning: aiReasoning,
			Suggestion:  aiReasoning,
		}
	}

	// 3) 按 req.RoomType（Unity 原始房间类型）通过 gRPC 调对应微服务（Combat / Event / Resource）
	protoReq := &riftofog.ActionRequest{
		ActionKey:   req.ActionKey,
		Str:         int32(req.Str),
		IntVal:      int32(req.IntVal),
		Lck:         int32(req.Lck),
		Stealth:     int32(req.Stealth),
		Hp:          int32(req.Hp),
		Ap:          int32(req.Ap),
		Status:      req.Status,
		EnemyType:   req.EnemyType,
		RoomContent: req.RoomContent,
	}

	var protoResp *riftofog.ActionResult
	log.Printf("[ws] step3 gRPC ExecuteAction start room=%s action=%s", req.RoomType, req.ActionKey)
	switch req.RoomType {
	case "Combat":
		if h.Combat == nil {
			return &ServerResponse{Type: "action_result", Result: "Combat 服务不可用", Service: req.RoomType, Action: req.ActionKey}
		}
		protoResp, _ = h.Combat.ExecuteAction(ctx, protoReq)
	case "Event":
		if h.Event == nil {
			return &ServerResponse{Type: "action_result", Result: "Event 服务不可用", Service: req.RoomType, Action: req.ActionKey}
		}
		protoResp, _ = h.Event.ExecuteAction(ctx, protoReq)
	case "Resource":
		if h.Resource == nil {
			return &ServerResponse{Type: "action_result", Result: "Resource 服务不可用", Service: req.RoomType, Action: req.ActionKey}
		}
		protoResp, _ = h.Resource.ExecuteAction(ctx, protoReq)
	default:
		log.Printf("[ws] step3 gRPC done (PathChoice/other, no gRPC)")
		// PathChoice 或其它：当前仅返回 LLM 的 reasoning + 路径提示占位；后续需补充：基于 Qdrant 历史 + path_hints 的 LLM 路径建议
		suggestion := "（路径选择）房间:" + req.RoomType
		if len(req.PathHints) > 0 {
			suggestion += " 路径提示:" + strings.Join(req.PathHints, " | ")
		}
		if req.ActionKey != "" {
			suggestion += " 推荐选择:" + req.ActionKey
		}
		if aiReasoning != "" {
			suggestion += " — " + aiReasoning
		}
		return &ServerResponse{
			SessionID:   req.SessionID,
			Type:        "llm_suggestion",
			Service:     req.RoomType,
			Action:      req.ActionKey,
			Result:      "",
			AIReasoning: aiReasoning,
			Suggestion:  suggestion,
		}
	}

	// 4) 将 gRPC ActionResult + routing 的 reasoning 组合成 ServerResponse
	var state *protocol.StateChanges
	if protoResp != nil && (protoResp.GetHpDelta() != 0 || protoResp.GetApDelta() != 0 || protoResp.GetNewStatus() != "" || protoResp.GetClearStatus() != "") {
		state = &protocol.StateChanges{
			HpDelta:     protoResp.GetHpDelta(),
			ApDelta:     protoResp.GetApDelta(),
			NewStatus:   protoResp.GetNewStatus(),
			ClearStatus: protoResp.GetClearStatus(),
		}
	}
	resultText := ""
	if protoResp != nil {
		resultText = protoResp.GetResultText()
	}
	log.Printf("[ws] step3 gRPC done result=%s", truncMsg(resultText, 60))
	log.Printf("[ws] handleRequest done type=action_result, sending to client")
	return &ServerResponse{
		SessionID:    req.SessionID,
		Type:         "action_result",
		Service:      req.RoomType,
		Action:       req.ActionKey,
		Result:       resultText,
		StateChanges: state,
		AIReasoning:  aiReasoning,
		Suggestion:   resultText,
	}
}
