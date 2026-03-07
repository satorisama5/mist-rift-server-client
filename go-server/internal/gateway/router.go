package gateway

import (
	"bytes"
	"context"
	"encoding/json"
	"fmt"
	"log"
	"net/http"
	"os"
	"regexp"
	"strconv"
	"strings"
	"time"
)

const doubaoChatURL = "https://ark.cn-beijing.volces.com/api/v3/chat/completions"

// getDoubaoChatModel 从环境变量 DOUBAO_CHAT_MODEL 读取，默认带版本 doubao-seed-1-8-251228。
func getDoubaoChatModel() string {
	if s := strings.TrimSpace(os.Getenv("DOUBAO_CHAT_MODEL")); s != "" {
		return s
	}
	return "doubao-seed-1-8-251228"
}

// getDoubaoTimeout 从环境变量 DOUBAO_TIMEOUT 或 GEMINI_TIMEOUT 读取（如 30、30s），默认 30s。
func getDoubaoTimeout() time.Duration {
	s := os.Getenv("DOUBAO_TIMEOUT")
	if s == "" {
		s = os.Getenv("GEMINI_TIMEOUT")
	}
	if s == "" {
		return 30 * time.Second
	}
	if d, err := time.ParseDuration(s); err == nil {
		return d
	}
	if n, err := strconv.Atoi(strings.TrimSpace(s)); err == nil && n > 0 {
		return time.Duration(n) * time.Second
	}
	return 30 * time.Second
}

const systemPrompt = `你是《迷雾裂隙》游戏的AI路由器。
+;
你会收到一个JSON，包含：
- message：玩家的自然语言指令（中文，需按语义理解）
- room_type：当前房间类型（这是强制约束）
- 角色属性：hp/ap/str/int_val/lck/stealth/status
- recent_decisions：最近历史决策（可能含「玩家风格:...」描述该玩家偏好与成功率，请参考）
- path_hints：路径提示
- future_paths：从当前节点出发后续2～3层房间类型序列，可结合后续路径推荐省AP或补资源的策略

重要规则：
1. 你只能从 room_type 对应的 action_key 中选择，绝对不能跨房间类型。
   Combat:   strong_attack / lure / stealth_pass / negotiate
   Event:    investigate / destroy / ignore / gamble
   Resource: grab_all / careful_check / trade / mark_location
   PathChoice: 返回路径索引 0/1/2/3

2. 语义对应必须识别：玩家说法只要和某个行为明显对应，就必须返回该 action_key，不得判为无关。
   Combat 常见说法示例（含同义表述）：
   - "潜行/逃走/溜走/悄悄/绕过/不想损失/不想受伤/想少掉血/潜行逃走/想先潜行过去" → stealth_pass
   - "直接打/硬刚/强攻" → strong_attack
   - "引开/分散再打" → lure
   - "谈一谈/撤退/不打了" → negotiate
   Event: 调查/研究→investigate，破坏/砸→destroy，忽略/不管→ignore，赌/祈祷→gamble
   Resource: 全拿/拾取→grab_all，检查/小心→careful_check，交易/买→trade，标记→mark_location

3. 如果玩家说的话和当前房间类型无关但可推断（比如在战斗房间说"我想要宝藏"），
   结合当前房间、属性、path_hints、future_paths 推断最优解（如潜行省AP为后续资源房做准备）。

4. 仅当玩家输入完全无关、无法对应到任何当前房间行为时（如"今天天气真好"），才返回：
   { "service": "当前room_type", "action_key": "", "reasoning": "我不太理解，请输入与当前房间相关的指令，如：强攻、潜行、调查等" }

5. service 字段必须永远等于输入的 room_type。

只返回一个JSON，不要多余文字：
{"service":"Combat","action_key":"stealth_pass","reasoning":"理由"}
`

// RouteIntent 用豆包对自然语言做意图分类，返回 service、action_key、reasoning。
// 解析失败时按 room_type 做 fallback。
func RouteIntent(ctx context.Context, req *UnityRequest) (service, actionKey, reasoning string, err error) {
	log.Printf("[intent] RouteIntent start message=%q room=%s", req.Message, req.RoomType)
	apiKey := os.Getenv("DOUBAO_API_KEY")
	if apiKey == "" {
		log.Printf("[intent] DOUBAO_API_KEY empty, using fallback")
		service, actionKey, reasoning = fallbackIntent(req.RoomType, req.Message)
		return service, actionKey, reasoning, nil
	}

	userJSON, err := json.Marshal(req)
	if err != nil {
		log.Printf("[intent] marshal req err=%v, fallback", err)
		service, actionKey, reasoning = fallbackIntent(req.RoomType, req.Message)
		return service, actionKey, reasoning, nil
	}
	fullText := systemPrompt + "\n\n" + string(userJSON)

	timeout := getDoubaoTimeout()
	log.Printf("[intent] callDoubao start timeout=%v model=%s", timeout, getDoubaoChatModel())
	text, err := callDoubao(ctx, fullText, timeout)
	if err != nil {
		log.Printf("[intent] callDoubao err=%v, fallback", err)
		service, actionKey, reasoning = fallbackIntent(req.RoomType, req.Message)
		return service, actionKey, reasoning, err
	}
	log.Printf("[intent] callDoubao done response_len=%d", len(text))

	service, actionKey, reasoning, err = parseIntentResponse(text)
	if err != nil {
		log.Printf("[intent] parseIntentResponse err=%v raw=%q, fallback", err, truncStr(text, 120))
		service, actionKey, reasoning = fallbackIntent(req.RoomType, req.Message)
		return service, actionKey, reasoning, nil
	}
	// 安全网：LLM 返回 action_key 为空但玩家输入明显对应某行为时，用关键词覆盖，避免误判为无关
	if actionKey == "" && strings.TrimSpace(req.Message) != "" {
		if overrideAction, overrideReason, ok := tryKeywordOverride(req.RoomType, req.Message); ok {
			actionKey = overrideAction
			reasoning = overrideReason
			log.Printf("[intent] keyword override applied actionKey=%q (message had clear intent)", actionKey)
		}
	}
	log.Printf("[intent] parse done service=%s actionKey=%q reasoning=%q", service, actionKey, truncStr(reasoning, 60))
	return service, actionKey, reasoning, nil
}

func truncStr(s string, max int) string {
	if len(s) <= max {
		return s
	}
	return s[:max] + "..."
}

// tryKeywordOverride 当 LLM 返回 action_key 为空时，用关键词推断并覆盖。返回 (actionKey, reasoning, true) 或 ("", "", false)。
func tryKeywordOverride(roomType string, message string) (actionKey, reasoning string, ok bool) {
	msg := strings.TrimSpace(message)
	if msg == "" {
		return "", "", false
	}
	switch roomType {
	case "Combat":
		if strings.Contains(msg, "潜行") || strings.Contains(msg, "逃走") || strings.Contains(msg, "溜走") || strings.Contains(msg, "悄悄") ||
			strings.Contains(msg, "绕过") || strings.Contains(msg, "不想损失") || strings.Contains(msg, "不想受伤") || strings.Contains(msg, "偷偷") {
			return "stealth_pass", "根据你的表述（潜行/少损失）推荐潜行绕过", true
		}
		if strings.Contains(msg, "引") || strings.Contains(msg, "分散") {
			return "lure", "根据你的表述推荐引诱分散", true
		}
		if strings.Contains(msg, "谈判") || strings.Contains(msg, "撤退") {
			return "negotiate", "根据你的表述推荐谈判撤退", true
		}
		if strings.Contains(msg, "强攻") || strings.Contains(msg, "打") || strings.Contains(msg, "硬刚") {
			return "strong_attack", "根据你的表述推荐强攻", true
		}
	case "Event":
		if strings.Contains(msg, "调查") || strings.Contains(msg, "研究") {
			return "investigate", "根据表述推荐调查", true
		}
		if strings.Contains(msg, "破坏") || strings.Contains(msg, "砸") {
			return "destroy", "根据表述推荐破坏", true
		}
		if strings.Contains(msg, "忽略") || strings.Contains(msg, "不管") {
			return "ignore", "根据表述推荐忽略", true
		}
		if strings.Contains(msg, "赌") || strings.Contains(msg, "祈祷") {
			return "gamble", "根据表述推荐赌博", true
		}
	case "Resource":
		if strings.Contains(msg, "全拿") || strings.Contains(msg, "拾取") || strings.Contains(msg, "拿") {
			return "grab_all", "根据表述推荐快速拾取", true
		}
		if strings.Contains(msg, "检查") || strings.Contains(msg, "小心") {
			return "careful_check", "根据表述推荐仔细检查", true
		}
		if strings.Contains(msg, "交易") || strings.Contains(msg, "买") {
			return "trade", "根据表述推荐交易", true
		}
		if strings.Contains(msg, "标记") {
			return "mark_location", "根据表述推荐标记", true
		}
	}
	return "", "", false
}

func fallbackIntent(roomType string, message string) (service, actionKey, reasoning string) {
	if actionKey, reasoning, ok := tryKeywordOverride(roomType, message); ok {
		return roomType, actionKey, "（意图解析不可用，" + reasoning + "）"
	}
	switch roomType {
	case "Combat":
		return "Combat", "strong_attack", "（意图解析不可用，使用默认：强攻）"
	case "Event":
		return "Event", "investigate", "（意图解析不可用，使用默认：调查）"
	case "Resource":
		return "Resource", "careful_check", "（意图解析不可用，使用默认：仔细检查）"
	case "PathChoice":
		return "PathChoice", "0", "（意图解析不可用，使用默认：路径0）"
	default:
		return roomType, "", "（未知房间类型，未选择行为）"
	}
}

// callDoubao 调用豆包聊天 API（OpenAI 兼容格式），用于意图分类。
func callDoubao(ctx context.Context, prompt string, timeout time.Duration) (string, error) {
	apiKey := os.Getenv("DOUBAO_API_KEY")
	if apiKey == "" {
		return "", fmt.Errorf("DOUBAO_API_KEY not set")
	}
	model := getDoubaoChatModel()
	body := map[string]interface{}{
		"model": model,
		"messages": []map[string]interface{}{
			{"role": "user", "content": prompt},
		},
		"temperature": 0.3,
		"max_tokens":  500,
	}
	bodyBytes, err := json.Marshal(body)
	if err != nil {
		return "", err
	}
	req, err := http.NewRequestWithContext(ctx, http.MethodPost, doubaoChatURL, bytes.NewReader(bodyBytes))
	if err != nil {
		return "", err
	}
	req.Header.Set("Content-Type", "application/json")
	req.Header.Set("Authorization", "Bearer "+apiKey)
	client := &http.Client{Timeout: timeout}
	resp, err := client.Do(req)
	if err != nil {
		return "", err
	}
	defer resp.Body.Close()
	if resp.StatusCode != http.StatusOK {
		return "", fmt.Errorf("doubao api: %s", resp.Status)
	}
	var result struct {
		Choices []struct {
			Message struct {
				Content string `json:"content"`
			} `json:"message"`
		} `json:"choices"`
	}
	if err := json.NewDecoder(resp.Body).Decode(&result); err != nil {
		return "", err
	}
	if len(result.Choices) == 0 {
		return "", fmt.Errorf("doubao: empty response")
	}
	return result.Choices[0].Message.Content, nil
}

// DoubaoHealthCheck 发一条最小聊天请求，检测豆包链路是否可用。
func DoubaoHealthCheck(apiKey string) (ok bool, latencyMs int64, errMsg string) {
	start := time.Now()
	timeout := getDoubaoTimeout()
	if timeout > 25*time.Second {
		timeout = 25 * time.Second
	}
	ctx, cancel := context.WithTimeout(context.Background(), timeout)
	defer cancel()
	text, err := callDoubao(ctx, "Reply with exactly: ok", timeout)
	latencyMs = time.Since(start).Milliseconds()
	if err != nil {
		return false, latencyMs, err.Error()
	}
	if text == "" {
		return false, latencyMs, "empty response"
	}
	return true, latencyMs, ""
}

var markdownJSONBlock = regexp.MustCompile("(?s)\\s*```(?:json)?\\s*\\n?(.*?)\\n?\\s*```\\s*")

func parseIntentResponse(text string) (service, actionKey, reasoning string, err error) {
	text = strings.TrimSpace(text)
	if m := markdownJSONBlock.FindStringSubmatch(text); len(m) >= 2 {
		text = strings.TrimSpace(m[1])
	}
	var out struct {
		Service   string `json:"service"`
		ActionKey string `json:"action_key"`
		Reasoning string `json:"reasoning"`
	}
	if err := json.Unmarshal([]byte(text), &out); err != nil {
		return "", "", "", err
	}
	return out.Service, out.ActionKey, out.Reasoning, nil
}

// Route 注册 WebSocket 与健康检查等路由。
func Route(wsHandler *Handler) http.Handler {
	mux := http.NewServeMux()
	mux.HandleFunc("/ws", wsHandler.ServeWS)
	mux.HandleFunc("/health", func(w http.ResponseWriter, _ *http.Request) {
		w.Header().Set("Content-Type", "application/json")
		_, _ = w.Write([]byte(`{"status":"ok"}`))
	})
	mux.HandleFunc("/health/doubao", func(w http.ResponseWriter, r *http.Request) {
		w.Header().Set("Content-Type", "application/json")
		if r.Method != http.MethodGet {
			w.WriteHeader(http.StatusMethodNotAllowed)
			_, _ = w.Write([]byte(`{"ok":false,"error":"method not allowed"}`))
			return
		}
		apiKey := os.Getenv("DOUBAO_API_KEY")
		if apiKey == "" {
			w.WriteHeader(http.StatusServiceUnavailable)
			_, _ = w.Write([]byte(`{"ok":false,"error":"DOUBAO_API_KEY not set"}`))
			return
		}
		ok, latencyMs, errMsg := DoubaoHealthCheck(apiKey)
		if ok {
			_, _ = w.Write([]byte(fmt.Sprintf(`{"ok":true,"latency_ms":%d}`, latencyMs)))
			return
		}
		w.WriteHeader(http.StatusServiceUnavailable)
		_, _ = w.Write([]byte(fmt.Sprintf(`{"ok":false,"error":%q,"latency_ms":%d}`, errMsg, latencyMs)))
	})
	return mux
}
