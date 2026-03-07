using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 《迷雾裂隙》人物属性系统：基础属性、状态标签、疲劳、AI 记忆占位。
/// </summary>

/// <summary>
/// 基础属性（数值型，与后端/数据库一致）。
    /// 范围：HP 0–100，AP 0–10，STR/INT/LCK/STEALTH 1–20。
    /// </summary>
    [Serializable]
    public class BaseStats
    {
        [Header("生命与行动")]
        [Range(0, 100)] public int HP = 100;
        [Range(0, 10)] public int AP = 10;

        [Header("六维属性 1–20")]
        [Range(1, 20)] public int STR = 10;   // 力量：战斗/搬运
        [Range(1, 20)] public int INT = 10;   // 智力：解谜/交涉
        [Range(1, 20)] public int LCK = 10;   // 幸运：随机事件
        [Range(1, 20)] public int STEALTH = 10; // 潜行：隐藏内容

        public void Clamp()
        {
            HP = Mathf.Clamp(HP, 0, 100);
            AP = Mathf.Clamp(AP, 0, 10);
            STR = Mathf.Clamp(STR, 1, 20);
            INT = Mathf.Clamp(INT, 1, 20);
            LCK = Mathf.Clamp(LCK, 1, 20);
            STEALTH = Mathf.Clamp(STEALTH, 1, 20);
        }

        public BaseStats Clone()
        {
            return new BaseStats
            {
                HP = HP, AP = AP, STR = STR, INT = INT, LCK = LCK, STEALTH = STEALTH
            };
        }
    }

/// <summary>
/// 负面状态（布尔，影响行为可用性）。
    /// </summary>
    [Serializable]
    public class NegativeStatus
    {
        public bool Poisoned;   // 中毒
        public bool Burning;    // 燃烧
        public bool Slowed;     // 减速
    }

/// <summary>
/// 正面 buff（布尔）。
    /// </summary>
    [Serializable]
    public class PositiveStatus
    {
        public bool Enchanted;  // 附魔
        public bool Invisible;  // 隐身
        public bool Berserk;    // 狂暴
    }

/// <summary>
/// 疲劳：AP 不足时由逻辑设置，限制高耗能行为。
    /// </summary>
    [Serializable]
    public class FatigueStatus
    {
        public bool IsFatigued;
        /// <summary> 触发疲劳的 AP 阈值，低于此值则 IsFatigued=true。 </summary>
        public int Threshold = 2;
    }

/// <summary>
/// AI 记忆（客户端占位，实际存 Qdrant 向量库）。用于展示或本地缓存，服务端推送后可更新。
/// </summary>
[Serializable]
public class AIMemoryPlaceholder
{
    /// <summary> 历史决策记录摘要，如 "上次在陷阱房选择了绕行，成功"。 </summary>
    public List<string> DecisionHistory = new List<string>();
    /// <summary> 房间类型偏好权重（Combat/Event/Resource 等），用两列表存键值便于 Unity 序列化。 </summary>
    public List<string> RoomPreferenceKeys = new List<string>();
    public List<float> RoomPreferenceValues = new List<float>();
    /// <summary> 当前探索目标：宝藏 / 出口 / 战斗 等。 </summary>
    public string CurrentGoal = "出口";
}

/// <summary>
/// 《迷雾裂隙》人物属性单例。负责基础属性、状态标签、疲劳、AI 记忆占位。
    /// 服务端接入后由 WebSocket 推送更新；当前为本地初始化与房间结果模拟。
    /// </summary>
    public class RiftOfFogPlayerStats : MonoBehaviour
    {
        public static RiftOfFogPlayerStats Instance { get; private set; }

        [Header("基础属性")]
        public BaseStats Base = new BaseStats();

        [Header("状态标签")]
        public NegativeStatus Negative = new NegativeStatus();
        public PositiveStatus Positive = new PositiveStatus();
        public FatigueStatus Fatigue = new FatigueStatus();

        [Header("AI 记忆（占位，实际在 Qdrant）")]
        public AIMemoryPlaceholder AIMemory = new AIMemoryPlaceholder();

        public event Action OnStatsChanged;

        private void Awake()
        {
            if (Instance == null) Instance = this;
            else Destroy(gameObject);
        }

        private void Start()
        {
            RefreshFatigue();
        }

        /// <summary> 根据当前 AP 刷新疲劳状态。 </summary>
        public void RefreshFatigue()
        {
            Fatigue.IsFatigued = Base.AP < Fatigue.Threshold;
            OnStatsChanged?.Invoke();
        }

        /// <summary> 消耗行动点，并刷新疲劳。 </summary>
        public bool TryConsumeAP(int amount)
        {
            if (Base.AP < amount) return false;
            Base.AP -= amount;
            Base.Clamp();
            RefreshFatigue();
            return true;
        }

        /// <summary> 回合开始或进入新房间时恢复 AP（具体数值可由配置或服务端决定）。 </summary>
        public void RestoreAP(int amount = 10)
        {
            Base.AP = Mathf.Clamp(Base.AP + amount, 0, 10);
            RefreshFatigue();
        }

        /// <summary> 应用伤害/治疗（服务端结果推送时调用）。 </summary>
        public void ApplyHPDelta(int delta)
        {
            Base.HP = Mathf.Clamp(Base.HP + delta, 0, 100);
            OnStatsChanged?.Invoke();
        }

        /// <summary> 应用服务端返回的 state_changes：HP/AP 变化及 new_status/clear_status。 </summary>
        public void ApplyStateChanges(int hpDelta, int apDelta, string newStatus, string clearStatus)
        {
            Base.HP = Mathf.Clamp(Base.HP + hpDelta, 0, 100);
            Base.AP = Mathf.Clamp(Base.AP + apDelta, 0, 10);
            if (!string.IsNullOrEmpty(clearStatus))
            {
                if (clearStatus.Contains("中毒")) Negative.Poisoned = false;
                if (clearStatus.Contains("燃烧")) Negative.Burning = false;
                if (clearStatus.Contains("减速")) Negative.Slowed = false;
            }
            if (!string.IsNullOrEmpty(newStatus))
            {
                if (newStatus.Contains("中毒")) Negative.Poisoned = true;
                if (newStatus.Contains("燃烧")) Negative.Burning = true;
                if (newStatus.Contains("减速")) Negative.Slowed = true;
            }
            RefreshFatigue();
            OnStatsChanged?.Invoke();
        }

        /// <summary> 添加一条决策记录（占位，正式由服务端/Qdrant 同步）。 </summary>
        public void AddDecisionRecord(string summary)
        {
            AIMemory.DecisionHistory.Add(summary);
            while (AIMemory.DecisionHistory.Count > 50) AIMemory.DecisionHistory.RemoveAt(0);
            OnStatsChanged?.Invoke();
        }

        /// <summary> 是否有任意负面状态。 </summary>
        public bool HasAnyNegative => Negative.Poisoned || Negative.Burning || Negative.Slowed;

        /// <summary> 是否有任意正面 buff。 </summary>
        public bool HasAnyPositive => Positive.Enchanted || Positive.Invisible || Positive.Berserk;

        /// <summary> 获取简短状态描述，用于 UI 或日志。 </summary>
        public string GetStatusSummary()
        {
            var parts = new List<string>();
            if (Negative.Poisoned) parts.Add("中毒");
            if (Negative.Burning) parts.Add("燃烧");
            if (Negative.Slowed) parts.Add("减速");
            if (Positive.Enchanted) parts.Add("附魔");
            if (Positive.Invisible) parts.Add("隐身");
            if (Positive.Berserk) parts.Add("狂暴");
            if (Fatigue.IsFatigued) parts.Add("疲劳");
            return parts.Count > 0 ? string.Join(" ", parts) : "无";
        }
    }
