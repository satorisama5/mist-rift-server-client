using System;

/// <summary>
/// 房间内行为与自然语言消息桥接。供后续服务端接入：发送行为/消息+场景上下文，接收 LLM 建议后注入 UI。
/// </summary>
public static class RiftOfFogMessageBridge
{
    /// <summary> 玩家在房间内输入并点击发送时触发，参数为输入框文本。 </summary>
    public static event Action<string> OnPlayerMessageSubmitted;

    /// <summary> 玩家点击房间内行为按钮时触发，参数为房间类型与行为 key。服务端可据此+场景上下文请求 LLM。 </summary>
    public static event Action<MapManager.RoomType, string> OnRoomActionChosen;

    /// <summary> 服务端返回 LLM 建议后调用，将文案注入当前房间 UI 并显示 [听从建议][忽略]。 </summary>
    public static void DeliverLLMResponse(string suggestionText)
    {
        RiftOfFogRoomUI.ReceiveLLMSuggestion(suggestionText ?? "");
    }

    /// <summary> 由房间 UI 在点击「发送」时调用。 </summary>
    public static void SubmitMessage(string message)
    {
        if (!string.IsNullOrWhiteSpace(message))
            OnPlayerMessageSubmitted?.Invoke(message.Trim());
    }

    /// <summary> 由房间 UI 在点击行为按钮时调用。 </summary>
    public static void NotifyActionChosen(MapManager.RoomType roomType, string actionKey)
    {
        if (!string.IsNullOrEmpty(actionKey))
            OnRoomActionChosen?.Invoke(roomType, actionKey);
    }
}
