package services

import (
	"math/rand"
	"strings"

	"go-server/internal/protocol"
)

// ExecuteEventAction 实现 Event 房间行为规则。room_content 为 npc / device / inscription 之一。
func ExecuteEventAction(req *protocol.ActionRequest) *protocol.ActionResult {
	if req == nil {
		return &protocol.ActionResult{Success: false, ResultText: "无效请求"}
	}
	out := &protocol.ActionResult{}

	switch req.ActionKey {
	case "investigate":
		out = eventInvestigate(req)
	case "destroy":
		out = eventDestroy(req)
	case "ignore":
		out = eventIgnore(req)
	case "gamble":
		out = eventGamble(req)
	default:
		out.Success = false
		out.ResultText = "未知行为: " + req.ActionKey
		return out
	}

	out.HpDelta, out.ApDelta = protocol.ClampDeltas(req.Hp, req.Ap, out.HpDelta, out.ApDelta)
	return out
}

func eventInvestigate(req *protocol.ActionRequest) *protocol.ActionResult {
	// INT 检定；device 失败时 new_status="中毒"；消耗 AP 时疲劳阈值+3
	th14 := int32(protocol.FatigueThreshold(14, req.Ap, true))
	th10 := int32(protocol.FatigueThreshold(10, req.Ap, true))
	intVal := req.IntVal
	if intVal >= th14 {
		return &protocol.ActionResult{Success: true, ResultText: "深入调查，发现了关键线索", ApDelta: -2, Reward: "隐藏信息"}
	}
	if intVal >= th10 {
		if rand.Float32() < 0.7 {
			return &protocol.ActionResult{Success: true, ResultText: "有所发现，但信息不完整", ApDelta: -2, Reward: "普通信息"}
		}
	}
	out := &protocol.ActionResult{Success: false, ResultText: "调查失败，触发了机关", ApDelta: -2, HpDelta: -5}
	if strings.Contains(req.RoomContent, "device") {
		out.NewStatus = "中毒"
	}
	return out
}

func eventDestroy(req *protocol.ActionRequest) *protocol.ActionResult {
	str := req.Str
	if str >= 14 {
		return &protocol.ActionResult{Success: true, ResultText: "强行破坏，获得大量资源但受了伤", Reward: "资源x2", HpDelta: -5}
	}
	if str >= 10 {
		if rand.Float32() < 0.7 {
			return &protocol.ActionResult{Success: true, ResultText: "破坏成功，代价不小", Reward: "资源x1", HpDelta: -10}
		}
	}
	out := &protocol.ActionResult{Success: false, ResultText: "破坏失败，触发爆炸", HpDelta: -20}
	if strings.Contains(req.RoomContent, "device") {
		out.NewStatus = "燃烧"
	}
	return out
}

func eventIgnore(req *protocol.ActionRequest) *protocol.ActionResult {
	return &protocol.ActionResult{Success: true, ResultText: "选择无视，继续前进，错过了潜在收益", HpDelta: 0, ApDelta: 0}
}

func eventGamble(req *protocol.ActionRequest) *protocol.ActionResult {
	lck := req.Lck
	if lck >= 15 {
		return &protocol.ActionResult{Success: true, ResultText: "神明眷顾！获得了珍贵的奖励", Reward: "稀有物品", HpDelta: 20}
	}
	if lck >= 10 {
		if rand.Float32() < 0.7 {
			return &protocol.ActionResult{Success: true, ResultText: "运气不错，小有收获", Reward: "普通物品", HpDelta: 5}
		}
	}
	return &protocol.ActionResult{Success: false, ResultText: "运气太差，遭受了严重惩罚", HpDelta: -30, NewStatus: "中毒"}
}
