using System.Collections.Generic;
using System.Text;
using UnityEngine;

/// <summary>
/// 《迷雾裂隙》场景上下文：供后续信息链路打通后，与玩家自然语言一并发给后端 LLM+Qdrant。
/// LLM 可据此读取场景信息并分析玩家语句做决策（选行为、选路径等）。
/// </summary>
public static class RiftOfFogSceneContext
{
    /// <summary>
    /// 获取当前场景上下文（房间类型、节点位置、人物属性、路径提示等）的 JSON 风格字符串。
    /// 后续打通链路时：将 GetCurrentContext() 与玩家输入的自然语言一起发给后端，
    /// 后端 LLM 结合 Qdrant 检索结果做意图分类与决策，再通过 gRPC 返回行为/路径选择等。
    /// </summary>
    public static string GetCurrentContext()
    {
        var sb = new StringBuilder();
        sb.Append("{");

        if (MapManager.Instance != null)
        {
            sb.Append("\"room_type\":\"").Append(MapManager.Instance.CurrentNodeRoomType).Append("\",");
            sb.Append("\"node_x\":").Append(MapManager.Instance.CurrentNodeX).Append(",");
            sb.Append("\"node_y\":").Append(MapManager.Instance.CurrentNodeY).Append(",");
            if (MapManager.Instance.CurrentPathHints != null && MapManager.Instance.CurrentPathHints.Count > 0)
            {
                sb.Append("\"path_hints\":[");
                for (int i = 0; i < MapManager.Instance.CurrentPathHints.Count; i++)
                {
                    if (i > 0) sb.Append(",");
                    sb.Append("\"").Append(Escape(MapManager.Instance.CurrentPathHints[i])).Append("\"");
                }
                sb.Append("],");
            }
            var futurePaths = MapManager.Instance.GetFuturePathsForCurrentNode(3);
            if (futurePaths != null && futurePaths.Count > 0)
            {
                sb.Append("\"future_paths\":[");
                for (int i = 0; i < futurePaths.Count; i++)
                {
                    if (i > 0) sb.Append(",");
                    sb.Append("\"").Append(Escape(futurePaths[i])).Append("\"");
                }
                sb.Append("],");
            }
        }

        if (RiftOfFogPlayerStats.Instance != null)
        {
            var s = RiftOfFogPlayerStats.Instance;
            sb.Append("\"hp\":").Append(s.Base.HP).Append(",");
            sb.Append("\"ap\":").Append(s.Base.AP).Append(",");
            sb.Append("\"str\":").Append(s.Base.STR).Append(",");
            sb.Append("\"int_val\":").Append(s.Base.INT).Append(",");
            sb.Append("\"lck\":").Append(s.Base.LCK).Append(",");
            sb.Append("\"stealth\":").Append(s.Base.STEALTH).Append(",");
            sb.Append("\"status\":\"").Append(Escape(s.GetStatusSummary())).Append("\",");
            sb.Append("\"current_goal\":\"").Append(Escape(s.AIMemory.CurrentGoal ?? "")).Append("\",");
            if (s.AIMemory.DecisionHistory != null && s.AIMemory.DecisionHistory.Count > 0)
            {
                sb.Append("\"recent_decisions\":[");
                int start = Mathf.Max(0, s.AIMemory.DecisionHistory.Count - 5);
                for (int i = start; i < s.AIMemory.DecisionHistory.Count; i++)
                {
                    if (i > start) sb.Append(",");
                    sb.Append("\"").Append(Escape(s.AIMemory.DecisionHistory[i])).Append("\"");
                }
                sb.Append("],");
            }
        }

        if (sb[sb.Length - 1] == ',')
            sb.Length--;
        sb.Append("}");
        return sb.ToString();
    }

    private static string Escape(string s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        return s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n");
    }
}
