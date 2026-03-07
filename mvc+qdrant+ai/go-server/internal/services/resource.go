package services

import (
	"math/rand"
	"strings"

	"go-server/internal/protocol"
)

// ExecuteResourceAction 实现 Resource 房间行为规则。
// room_content: chest_normal / chest_rare / chest_cursed / merchant / spring
func ExecuteResourceAction(req *protocol.ActionRequest) *protocol.ActionResult {
	if req == nil {
		return &protocol.ActionResult{Success: false, ResultText: "无效请求"}
	}
	out := &protocol.ActionResult{}

	switch req.ActionKey {
	case "grab_all":
		out = resourceGrabAll(req)
	case "careful_check":
		out = resourceCarefulCheck(req)
	case "trade":
		out = resourceTrade(req)
	case "mark_location":
		out = resourceMarkLocation(req)
	default:
		out.Success = false
		out.ResultText = "未知行为: " + req.ActionKey
		return out
	}

	out.HpDelta, out.ApDelta = protocol.ClampDeltas(req.Hp, req.Ap, out.HpDelta, out.ApDelta)
	return out
}

func resourceGrabAll(req *protocol.ActionRequest) *protocol.ActionResult {
	room := req.RoomContent
	if strings.Contains(room, "chest_cursed") {
		return &protocol.ActionResult{Success: false, ResultText: "诅咒宝箱！中毒了", HpDelta: -20, NewStatus: "中毒"}
	}
	if strings.Contains(room, "chest_rare") {
		return &protocol.ActionResult{Success: true, ResultText: "快速拾取，触发了轻微陷阱", Reward: "稀有物品x2", HpDelta: -5}
	}
	return &protocol.ActionResult{Success: true, ResultText: "迅速拾取全部物品", Reward: "普通物品x2"}
}

func resourceCarefulCheck(req *protocol.ActionRequest) *protocol.ActionResult {
	if req.Ap < 2 {
		return &protocol.ActionResult{Success: false, ResultText: "行动点不足，无法仔细检查"}
	}
	room := req.RoomContent
	th12 := int32(protocol.FatigueThreshold(12, req.Ap, true))
	intVal := req.IntVal
	if strings.Contains(room, "chest_cursed") && intVal >= th12 {
		return &protocol.ActionResult{Success: true, ResultText: "发现并解除了诅咒", ApDelta: -2, Reward: "解除诅咒+物品"}
	}
	if strings.Contains(room, "chest_rare") {
		return &protocol.ActionResult{Success: true, ResultText: "仔细检查发现更多", ApDelta: -2, Reward: "稀有物品x3"}
	}
	if strings.Contains(room, "spring") {
		return &protocol.ActionResult{Success: true, ResultText: "泉水效果翻倍", ApDelta: -1, HpDelta: 30}
	}
	return &protocol.ActionResult{Success: true, ResultText: "安全获取物品", ApDelta: -2, Reward: "普通物品x1"}
}

func resourceTrade(req *protocol.ActionRequest) *protocol.ActionResult {
	if !strings.Contains(req.RoomContent, "merchant") {
		return &protocol.ActionResult{Success: false, ResultText: "这里没有商人"}
	}
	th14 := int32(protocol.FatigueThreshold(14, req.Ap, true))
	th10 := int32(protocol.FatigueThreshold(10, req.Ap, true))
	intVal := req.IntVal
	if intVal >= th14 {
		return &protocol.ActionResult{Success: true, ResultText: "谈判高手，以极优惠的价格成交", Reward: "以低价获得稀有物品", ApDelta: -1}
	}
	if intVal >= th10 {
		if rand.Float32() < 0.7 {
			return &protocol.ActionResult{Success: true, ResultText: "正常交易完成", Reward: "正常价格获得物品", ApDelta: -1}
		}
	}
	return &protocol.ActionResult{Success: false, ResultText: "被商人坑了一笔", HpDelta: -5}
}

func resourceMarkLocation(req *protocol.ActionRequest) *protocol.ActionResult {
	// 永远成功，无消耗；服务端可在此触发 Qdrant 存储。
	return &protocol.ActionResult{Success: true, ResultText: "在此留下标记，AI将记住这个地点", Reward: "标记已记录", HpDelta: 0, ApDelta: 0}
}
