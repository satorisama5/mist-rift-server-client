package protocol

// ActionRequest 行为请求，与 game.proto 一致。
type ActionRequest struct {
	ActionKey   string `json:"action_key"`
	Str         int32  `json:"str"`
	IntVal      int32  `json:"int_val"`
	Lck         int32  `json:"lck"`
	Stealth     int32  `json:"stealth"`
	Hp          int32  `json:"hp"`
	Ap          int32  `json:"ap"`
	Status      string `json:"status"`
	EnemyType   string `json:"enemy_type"`
	RoomContent string `json:"room_content"`
}

// ActionResult 行为结果，与 game.proto 一致。HP/AP 变化由调用方按「不低于 0」截断后填入。
type ActionResult struct {
	Success     bool   `json:"success"`
	ResultText  string `json:"result_text"`
	HpDelta     int32  `json:"hp_delta"`
	ApDelta     int32  `json:"ap_delta"`
	NewStatus   string `json:"new_status"`
	ClearStatus string `json:"clear_status"`
	Reward      string `json:"reward"`
}

// ClampDeltas 保证 (hp+hpDelta) 与 (ap+apDelta) 不低于 0，返回截断后的 delta。
func ClampDeltas(hp, ap int32, hpDelta, apDelta int32) (clampedHp, clampedAp int32) {
	if hp+hpDelta < 0 {
		hpDelta = -hp
	}
	if ap+apDelta < 0 {
		apDelta = -ap
	}
	return hpDelta, apDelta
}

// FatigueThreshold 当 AP<=0 且为消耗 AP 的行为时，阈值加 3。
func FatigueThreshold(base int, ap int32, consumesAP bool) int {
	if consumesAP && ap <= 0 {
		return base + 3
	}
	return base
}

// StateChanges 状态变化，Unity 收到后更新 PlayerStats。
type StateChanges struct {
	HpDelta     int32  `json:"hp_delta"`
	ApDelta     int32  `json:"ap_delta"`
	NewStatus   string `json:"new_status"`
	ClearStatus string `json:"clear_status"`
}

// UnityRequest 与 Unity 端发送的 JSON 一一对应。字段名统一下划线，与 game.proto 风格一致。
type UnityRequest struct {
	RoomType         string   `json:"room_type"`
	NodeX            int      `json:"node_x"`
	NodeY            int      `json:"node_y"`
	PathHints        []string `json:"path_hints,omitempty"`
	FuturePaths      []string `json:"future_paths,omitempty"` // 从当前节点出发后续 2～3 层房间类型序列，如 ["Combat→Resource→PathChoice"]
	Hp               int      `json:"hp"`
	Ap               int      `json:"ap"`
	Str              int      `json:"str"`
	IntVal           int      `json:"int_val"`
	Lck              int      `json:"lck"`
	Stealth          int      `json:"stealth"`
	Status           string   `json:"status"`
	CurrentGoal      string   `json:"current_goal"`
	RecentDecisions  []string `json:"recent_decisions,omitempty"`
	Message          string   `json:"message,omitempty"`
	ActionKey        string   `json:"action_key,omitempty"`
	SessionID        string   `json:"session_id"`
	Type             string   `json:"type"`
	AvailableActions []string `json:"available_actions,omitempty"`
	EnemyType        string   `json:"enemy_type,omitempty"`   // 仅 Combat：patrol/guard/swarm
	RoomContent      string   `json:"room_content,omitempty"` // 仅 Event/Resource：npc/device/inscription、chest_*/merchant/spring
}

// ServerResponse 与服务端返回给 Unity 的 JSON 一一对应。
type ServerResponse struct {
	SessionID    string         `json:"session_id"`
	Type         string         `json:"type"`
	Service      string         `json:"service"`
	Action       string         `json:"action"`
	Result       string         `json:"result"`
	AIReasoning  string         `json:"ai_reasoning"`
	Suggestion   string         `json:"suggestion"`
	StateChanges *StateChanges  `json:"state_changes,omitempty"`
}
