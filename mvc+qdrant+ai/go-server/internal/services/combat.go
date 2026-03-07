package services

import (
	"math/rand"
	"strings"

	"go-server/internal/protocol"
)

// hasStatus 判断 status 字符串中是否包含某状态（如 "中毒"、"燃烧"、"减速"）。
func hasStatus(status, s string) bool {
	return strings.Contains(status, s)
}

// ExecuteAction 实现 Combat 房间行为规则。
func ExecuteCombatAction(req *protocol.ActionRequest) *protocol.ActionResult {
	if req == nil {
		return &protocol.ActionResult{Success: false, ResultText: "无效请求"}
	}
	out := &protocol.ActionResult{}

	switch req.ActionKey {
	case "strong_attack":
		out = combatStrongAttack(req)
	case "lure":
		out = combatLure(req)
	case "stealth_pass":
		out = combatStealthPass(req)
	case "negotiate":
		out = combatNegotiate(req)
	default:
		out.Success = false
		out.ResultText = "未知行为: " + req.ActionKey
		return out
	}

	// 通用：HP/AP 不低于 0 截断
	out.HpDelta, out.ApDelta = protocol.ClampDeltas(req.Hp, req.Ap, out.HpDelta, out.ApDelta)
	return out
}

func combatStrongAttack(req *protocol.ActionRequest) *protocol.ActionResult {
	str := req.Str
	if hasStatus(req.Status, "中毒") {
		str -= 3
	}
	thresholdSuccess := protocol.FatigueThreshold(15, req.Ap, true)
	thresholdPartial := protocol.FatigueThreshold(10, req.Ap, true)
	if req.EnemyType == "guard" {
		thresholdSuccess += 3
		thresholdPartial += 3
	}
	hpDelta := int32(0)
	apDelta := int32(-1)
	if str >= int32(thresholdSuccess) {
		hpDelta = -5
		apDelta = -1
		return &protocol.ActionResult{Success: true, ResultText: "一击制敌，势如破竹", HpDelta: hpDelta, ApDelta: apDelta}
	}
	if str >= int32(thresholdPartial) {
		// 达标但未满：约 70% 部分成功，30% 仍判失败，增加随机性
		if rand.Float32() < 0.7 {
			hpDelta = -15
			apDelta = -2
			out := &protocol.ActionResult{Success: true, ResultText: "击败敌人，但受了不轻的伤", HpDelta: hpDelta, ApDelta: apDelta}
			if req.EnemyType == "swarm" {
				out.HpDelta -= 10
			}
			return out
		}
		// 随机判为失败，走下方失败分支
	}
	hpDelta = -25
	apDelta = -2
	out := &protocol.ActionResult{Success: false, ResultText: "力量不足，被敌人反击重创", HpDelta: hpDelta, ApDelta: apDelta}
	if req.EnemyType == "swarm" {
		out.HpDelta -= 10
	}
	return out
}

func combatLure(req *protocol.ActionRequest) *protocol.ActionResult {
	if hasStatus(req.Status, "燃烧") {
		return &protocol.ActionResult{Success: false, ResultText: "你正在燃烧，无法保持冷静引诱"}
	}
	th14 := int32(protocol.FatigueThreshold(14, req.Ap, true))
	th10 := int32(protocol.FatigueThreshold(10, req.Ap, true))
	intVal := req.IntVal
	if intVal >= th14 {
		return &protocol.ActionResult{Success: true, ResultText: "成功将敌人分散，逐一击破", HpDelta: -3, ApDelta: -2}
	}
	if intVal >= th10 {
		if rand.Float32() < 0.7 {
			return &protocol.ActionResult{Success: true, ResultText: "部分引诱成功，仍受到一些伤害", HpDelta: -12, ApDelta: -3}
		}
		return &protocol.ActionResult{Success: false, ResultText: "引诱失败，敌人一拥而上", HpDelta: -20, ApDelta: -2}
	}
	return &protocol.ActionResult{Success: false, ResultText: "引诱失败，敌人一拥而上", HpDelta: -20, ApDelta: -2}
}

func combatStealthPass(req *protocol.ActionRequest) *protocol.ActionResult {
	stealth := req.Stealth
	if hasStatus(req.Status, "减速") {
		stealth -= 4
	}
	th14 := int32(protocol.FatigueThreshold(14, req.Ap, true))
	th10 := int32(protocol.FatigueThreshold(10, req.Ap, true))
	if stealth >= th14 {
		return &protocol.ActionResult{Success: true, ResultText: "悄无声息地绕过，敌人毫无察觉", HpDelta: 0, ApDelta: -1}
	}
	if stealth >= th10 {
		if rand.Float32() < 0.7 {
			return &protocol.ActionResult{Success: true, ResultText: "勉强绕过，但惊动了一只敌人", HpDelta: -8, ApDelta: -2}
		}
		return &protocol.ActionResult{Success: false, ResultText: "被发现！遭到突袭", HpDelta: -20, ApDelta: -2}
	}
	return &protocol.ActionResult{Success: false, ResultText: "被发现！遭到突袭", HpDelta: -20, ApDelta: -2}
}

func combatNegotiate(req *protocol.ActionRequest) *protocol.ActionResult {
	th14 := int32(protocol.FatigueThreshold(14, req.Ap, true))
	th10 := int32(protocol.FatigueThreshold(10, req.Ap, true))
	lck := req.Lck
	if lck >= th14 {
		return &protocol.ActionResult{Success: true, ResultText: "谈判成功，消耗体力但安全撤退", HpDelta: 0, ApDelta: -3}
	}
	if lck >= th10 {
		if rand.Float32() < 0.7 {
			return &protocol.ActionResult{Success: true, ResultText: "勉强谈成，付出了一些代价", HpDelta: -10, ApDelta: -2}
		}
		return &protocol.ActionResult{Success: false, ResultText: "谈判破裂，被迫仓皇撤退", HpDelta: -15, ApDelta: -1}
	}
	return &protocol.ActionResult{Success: false, ResultText: "谈判破裂，被迫仓皇撤退", HpDelta: -15, ApDelta: -1}
}
