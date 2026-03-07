using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 《迷雾裂隙》房间 UI：A/B/C 三区域（状态栏+行为按钮+自然语言输入），服务端返回后显示 LLM 建议与 [听从建议][忽略]。
/// </summary>
public static class RiftOfFogRoomUI
{
    private static GameObject _root;
    private static Canvas _canvas;
    private static bool _visible;
    private static Transform _currentPanelTransform;
    private static Action _currentOnComplete;
    private static bool _suggestionAreaShown;
    /// <summary> 当前建议区块（忽略时只销毁此块，不关房，让玩家继续点四个行为按钮）。 </summary>
    private static GameObject _suggestionBlock;
    /// <summary> 路径选择时「听从建议」会直接调用此回调并关房，不再发请求。 </summary>
    private static Action<int> _pathChoiceOnChosen;
    private const int ActionApCost = 2;

    public static bool IsVisible => _visible;

    private static void EnsureCanvas()
    {
        if (_root != null) return;
        _root = new GameObject("RiftOfFogRoomUI");
        _canvas = _root.AddComponent<Canvas>();
        _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        _canvas.sortingOrder = 100;
        _root.AddComponent<CanvasScaler>().uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        _root.AddComponent<GraphicRaycaster>();
        UnityEngine.Object.DontDestroyOnLoad(_root);
    }

    /// <summary> 服务端返回 LLM 建议后调用，显示「💡 AI建议：xxx」与 [听从建议][忽略]。路径选择时需已设置 _currentPanelTransform。 </summary>
    public static void ReceiveLLMSuggestion(string suggestionText)
    {
        if (_currentPanelTransform == null) return;
        _suggestionAreaShown = true;
        if (_suggestionBlock != null) { UnityEngine.Object.Destroy(_suggestionBlock); _suggestionBlock = null; }
        _suggestionBlock = new GameObject("SuggestionBlock");
        _suggestionBlock.transform.SetParent(_currentPanelTransform, false);
        var blockRect = _suggestionBlock.AddComponent<RectTransform>();
        blockRect.anchorMin = new Vector2(0, 0);
        blockRect.anchorMax = new Vector2(1, 0);
        blockRect.pivot = new Vector2(0.5f, 0);
        blockRect.anchoredPosition = Vector2.zero;
        blockRect.sizeDelta = new Vector2(0, 130);
        var blockLE = _suggestionBlock.AddComponent<LayoutElement>();
        blockLE.preferredHeight = 130;
        blockLE.flexibleWidth = 1f;
        var blockVL = _suggestionBlock.AddComponent<VerticalLayoutGroup>();
        blockVL.spacing = 8f;
        blockVL.padding = new RectOffset(0, 0, 0, 0);
        blockVL.childAlignment = TextAnchor.UpperCenter;
        blockVL.childControlHeight = blockVL.childControlWidth = false;
        string line = string.IsNullOrEmpty(suggestionText) ? "（暂无建议）" : suggestionText;
        CreateSuggestionText(_suggestionBlock.transform, "💡 AI建议：" + line);
        var btnRow = new GameObject("SuggestionButtons");
        btnRow.transform.SetParent(_suggestionBlock.transform, false);
        var btnRowRect = btnRow.AddComponent<RectTransform>();
        btnRowRect.sizeDelta = new Vector2(260, 40);
        var btnRowLE = btnRow.AddComponent<LayoutElement>();
        btnRowLE.preferredHeight = 40;
        btnRowLE.preferredWidth = 260;
        var btnRowH = btnRow.AddComponent<HorizontalLayoutGroup>();
        btnRowH.spacing = 12f;
        btnRowH.childAlignment = TextAnchor.MiddleCenter;
        btnRowH.childControlHeight = btnRowH.childControlWidth = false;
        CreateButton(btnRow.transform, "听从建议", OnFollowSuggestionClick, 120, 36);
        CreateButton(btnRow.transform, "忽略", OnIgnoreSuggestionClick, 80, 36);
    }

    /// <summary> 忽略建议：只关闭建议区块，不关房，玩家可继续点强攻/引诱/潜行/谈判。 </summary>
    private static void OnIgnoreSuggestionClick()
    {
        if (_suggestionBlock != null) { UnityEngine.Object.Destroy(_suggestionBlock); _suggestionBlock = null; }
        _suggestionAreaShown = false;
        if (WebSocketClient.Instance != null) WebSocketClient.Instance.PendingSuggestedAction = null;
    }

    private static void OnFollowSuggestionClick()
    {
        // 路径选择：直接按推荐索引选路并关房，不再发请求
        if (_pathChoiceOnChosen != null && WebSocketClient.Instance != null &&
            int.TryParse(WebSocketClient.Instance.PendingSuggestedAction ?? "", out int pathIdx))
        {
            var cb = _pathChoiceOnChosen;
            _pathChoiceOnChosen = null;
            _currentPanelTransform = null;
            _currentOnComplete = null;
            _suggestionAreaShown = false;
            if (WebSocketClient.Instance != null) WebSocketClient.Instance.PendingSuggestedAction = null;
            cb(pathIdx);
            return;
        }
        if (WebSocketClient.Instance != null && !string.IsNullOrEmpty(WebSocketClient.Instance.PendingSuggestedAction))
        {
            WebSocketClient.Instance.SendFollowSuggestion();
        }
        else
        {
            FinishRoomAndInvokeComplete(false);
        }
    }

    private static void FinishRoomAndInvokeComplete(bool _)
    {
        _visible = false;
        _suggestionAreaShown = false;
        var cb = _currentOnComplete;
        _currentOnComplete = null;
        _currentPanelTransform = null;
        cb?.Invoke();
    }

    /// <summary> A 类 战斗房间：状态栏 + 强攻/引诱/潜行/谈判 + 自然语言 +（服务端返回后）LLM 建议区。 </summary>
    public static void ShowCombatRoom(Action onComplete, Action<MapManager.RoomType, string> onActionOrMessageSent)
    {
        const string title = "⚔️ 战斗房间";
        string statusLine = "敌人：巡逻型 ×2";
        var buttons = new List<(string label, string key)>
        {
            ("强攻", "strong_attack"),
            ("引诱分散", "lure"),
            ("潜行绕过", "stealth"),
            ("谈判撤退", "negotiate")
        };
        ShowRoomWithActions(MapManager.RoomType.Combat, title, statusLine, buttons, onComplete, onActionOrMessageSent);
    }

    /// <summary> B 类 事件房间：状态栏 + 调查/破坏/忽略/祈祷 + 自然语言 + LLM 建议区。 </summary>
    public static void ShowEventRoom(Action onComplete, Action<MapManager.RoomType, string> onActionOrMessageSent)
    {
        const string title = "🔮 事件房间";
        string statusLine = "发现：神秘机关";
        var buttons = new List<(string label, string key)>
        {
            ("调查研究", "investigate"),
            ("强行破坏", "destroy"),
            ("忽略继续", "ignore"),
            ("祈祷赌博", "gamble")
        };
        ShowRoomWithActions(MapManager.RoomType.Event, title, statusLine, buttons, onComplete, onActionOrMessageSent);
    }

    /// <summary> C 类 资源房间：状态栏 + 检查/拾取/交易/标记 + 自然语言 + LLM 建议区。 </summary>
    public static void ShowResourceRoom(Action onComplete, Action<MapManager.RoomType, string> onActionOrMessageSent)
    {
        const string title = "💰 资源房间";
        string statusLine = "发现：稀有宝箱 + 商人";
        var buttons = new List<(string label, string key)>
        {
            ("仔细检查", "careful_check"),
            ("快速拾取", "grab_all"),
            ("与商人交易", "trade"),
            ("留下标记", "mark_location")
        };
        ShowRoomWithActions(MapManager.RoomType.Resource, title, statusLine, buttons, onComplete, onActionOrMessageSent);
    }

    private static void ShowRoomWithActions(MapManager.RoomType roomType, string title, string statusLine,
        List<(string label, string key)> buttons, Action onComplete, Action<MapManager.RoomType, string> onActionOrMessageSent)
    {
        EnsureCanvas();
        _visible = true;
        _suggestionAreaShown = false;
        _currentOnComplete = onComplete;
        ClearChildren(_root.transform);

        var panel = CreatePanel(_root.transform);
        _currentPanelTransform = panel.transform;
        var layout = panel.AddComponent<VerticalLayoutGroup>();
        layout.spacing = 10f;
        layout.padding = new RectOffset(20, 20, 20, 20);
        layout.childAlignment = TextAnchor.MiddleCenter;
        layout.childControlHeight = layout.childControlWidth = false;

        CreateText(panel.transform, title, 22);
        CreateText(panel.transform, statusLine, 16);
        if (RiftOfFogPlayerStats.Instance != null)
        {
            var s = RiftOfFogPlayerStats.Instance;
            CreateText(panel.transform, $"HP:{s.Base.HP}  AP:{s.Base.AP}  STR:{s.Base.STR} INT:{s.Base.INT} LCK:{s.Base.LCK} STEALTH:{s.Base.STEALTH}", 14);
        }

        var row1 = new GameObject("ActionRow1");
        row1.transform.SetParent(panel.transform, false);
        var r1 = row1.AddComponent<RectTransform>();
        r1.sizeDelta = new Vector2(400, 40);
        var h1 = row1.AddComponent<HorizontalLayoutGroup>();
        h1.spacing = 8;
        h1.childAlignment = TextAnchor.MiddleCenter;
        CreateButton(row1.transform, buttons[0].label, () => OnActionClick(roomType, buttons[0].key, onActionOrMessageSent), 180, 38);
        CreateButton(row1.transform, buttons[1].label, () => OnActionClick(roomType, buttons[1].key, onActionOrMessageSent), 180, 38);
        var row2 = new GameObject("ActionRow2");
        row2.transform.SetParent(panel.transform, false);
        var r2 = row2.AddComponent<RectTransform>();
        r2.sizeDelta = new Vector2(400, 40);
        var h2 = row2.AddComponent<HorizontalLayoutGroup>();
        h2.spacing = 8;
        h2.childAlignment = TextAnchor.MiddleCenter;
        CreateButton(row2.transform, buttons[2].label, () => OnActionClick(roomType, buttons[2].key, onActionOrMessageSent), 180, 38);
        CreateButton(row2.transform, buttons[3].label, () => OnActionClick(roomType, buttons[3].key, onActionOrMessageSent), 180, 38);

        var (inputField, sendBtn) = CreateMessageRow(panel.transform);
        if (sendBtn != null && inputField != null)
        {
            sendBtn.onClick.AddListener(() =>
            {
                string msg = inputField.text;
                if (!string.IsNullOrWhiteSpace(msg))
                {
                    if (RiftOfFogPlayerStats.Instance != null)
                        RiftOfFogPlayerStats.Instance.TryConsumeAP(ActionApCost);
                    RiftOfFogMessageBridge.SubmitMessage(msg);
                    onActionOrMessageSent?.Invoke(roomType, "message");
                    inputField.text = "";
                }
            });
        }

        var rect = panel.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = new Vector2(460, 340);
        rect.anchoredPosition = Vector2.zero;
    }

    private static void OnActionClick(MapManager.RoomType roomType,
    string actionKey, Action<MapManager.RoomType, string> onActionOrMessageSent)
    {
        if (RiftOfFogPlayerStats.Instance != null &&
            !RiftOfFogPlayerStats.Instance.TryConsumeAP(ActionApCost))
            return;
        RiftOfFogMessageBridge.NotifyActionChosen(roomType, actionKey);
        onActionOrMessageSent?.Invoke(roomType, actionKey);

        // 点按钮直接完成房间，不等LLM建议
        FinishRoomAndInvokeComplete(false);
    }

    /// <summary> 兼容旧逻辑的占位房间（直接显示战斗房间布局）。 </summary>
    public static void ShowRoomPlaceholder(string title, string description, Action onComplete)
    {
        ShowCombatRoom(onComplete, (_, _) => { });
    }

    /// <summary> D 类路径选择：显示多条语义提示 + 自然语言输入；服务端返回后显示「💡 AI建议」与 [听从建议][忽略]。 </summary>
    public static void ShowPathChoice(string title, List<string> hints, Action<int> onChosen)
    {
        EnsureCanvas();
        _visible = true;
        _suggestionAreaShown = false;
        _pathChoiceOnChosen = onChosen;
        _currentPanelTransform = null;
        _currentOnComplete = () => { }; // 仅用于满足显示建议区块的条件，路径选择用 onChosen 收尾
        ClearChildren(_root.transform);

        var panel = CreatePanel(_root.transform);
        _currentPanelTransform = panel.transform;
        var layout = panel.AddComponent<VerticalLayoutGroup>();
        layout.spacing = 12f;
        layout.padding = new RectOffset(24, 24, 24, 24);
        layout.childAlignment = TextAnchor.MiddleCenter;
        layout.childControlHeight = layout.childControlWidth = false;

        CreateText(panel.transform, title, 24);
        var (inputField, sendBtn) = CreateMessageRow(panel.transform);
        if (sendBtn != null && inputField != null)
        {
            sendBtn.onClick.AddListener(() =>
            {
                string msg = inputField.text;
                if (!string.IsNullOrWhiteSpace(msg))
                {
                    RiftOfFogMessageBridge.SubmitMessage(msg);
                    inputField.text = "";
                }
            });
        }
        for (int i = 0; i < hints.Count; i++)
        {
            int index = i;
            CreateButton(panel.transform, hints[i], () =>
            {
                _pathChoiceOnChosen = null;
                _visible = false;
                _currentPanelTransform = null;
                _currentOnComplete = null;
                onChosen?.Invoke(index);
            });
        }

        var rect = panel.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = new Vector2(460, Mathf.Max(320, 140 + hints.Count * 48));
        rect.anchoredPosition = Vector2.zero;
    }

    public static void HideIfVisible()
    {
        _suggestionAreaShown = false;
        _pathChoiceOnChosen = null;
        _currentOnComplete = null;
        _currentPanelTransform = null;
        if (_root != null)
        {
            ClearChildren(_root.transform);
            _root.SetActive(false);
        }
        _visible = false;
    }

    private static GameObject CreatePanel(Transform parent)
    {
        var go = new GameObject("Panel");
        go.transform.SetParent(parent, false);
        var rect = go.AddComponent<RectTransform>();
        var img = go.AddComponent<Image>();
        img.color = new Color(0.12f, 0.12f, 0.18f, 0.95f);
        return go;
    }

    private static GameObject CreateText(Transform parent, string content, int fontSize)
    {
        var go = new GameObject("Text");
        go.transform.SetParent(parent, false);
        var rect = go.AddComponent<RectTransform>();
        rect.sizeDelta = new Vector2(400, fontSize + 10);
        var text = go.AddComponent<Text>();
        text.text = content;
        text.fontSize = fontSize;
        text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        text.color = Color.white;
        text.alignment = TextAnchor.MiddleCenter;
        return go;
    }

    private static GameObject CreateTextLeft(Transform parent, string content, int fontSize)
    {
        var go = new GameObject("Text");
        go.transform.SetParent(parent, false);
        var rect = go.AddComponent<RectTransform>();
        rect.sizeDelta = new Vector2(420, fontSize + 8);
        var text = go.AddComponent<Text>();
        text.text = content;
        text.fontSize = fontSize;
        text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        text.color = new Color(1f, 0.95f, 0.7f, 1f);
        text.alignment = TextAnchor.MiddleLeft;
        return go;
    }

    /// <summary> 创建可换行、多行显示的建议文案，完整显示服务端返回的长文本。 </summary>
    private static GameObject CreateSuggestionText(Transform parent, string content)
    {
        var go = new GameObject("SuggestionText");
        go.transform.SetParent(parent, false);
        var rect = go.AddComponent<RectTransform>();
        rect.sizeDelta = new Vector2(420, 72);
        var le = go.AddComponent<LayoutElement>();
        le.preferredHeight = 72;
        le.preferredWidth = 420;
        var text = go.AddComponent<Text>();
        text.text = content ?? "";
        text.fontSize = 14;
        text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        text.color = new Color(1f, 0.95f, 0.7f, 1f);
        text.alignment = TextAnchor.UpperLeft;
        text.horizontalOverflow = HorizontalWrapMode.Wrap;
        text.verticalOverflow = VerticalWrapMode.Overflow;
        text.supportRichText = false;
        return go;
    }

    /// <summary> 创建一行：标签「自然语言指令」+ 输入框 + 「发送」按钮。返回 (InputField, SendButton)。 </summary>
    private static (InputField inputField, Button sendBtn) CreateMessageRow(Transform parent)
    {
        var row = new GameObject("MessageRow");
        row.transform.SetParent(parent, false);
        var rowRect = row.AddComponent<RectTransform>();
        rowRect.sizeDelta = new Vector2(380, 36);

        var label = new GameObject("Label");
        label.transform.SetParent(row.transform, false);
        var lrt = label.AddComponent<RectTransform>();
        lrt.anchorMin = new Vector2(0, 0.5f);
        lrt.anchorMax = new Vector2(0, 0.5f);
        lrt.pivot = new Vector2(0, 0.5f);
        lrt.anchoredPosition = new Vector2(0, 0);
        lrt.sizeDelta = new Vector2(120, 28);
        var lt = label.AddComponent<Text>();
        lt.text = "自然语言指令:";
        lt.fontSize = 14;
        lt.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        lt.color = Color.white;

        var inputGo = new GameObject("Input");
        inputGo.transform.SetParent(row.transform, false);
        var irt = inputGo.AddComponent<RectTransform>();
        irt.anchorMin = new Vector2(0, 0.5f);
        irt.anchorMax = new Vector2(1, 0.5f);
        irt.pivot = new Vector2(0.5f, 0.5f);
        irt.anchoredPosition = new Vector2(0, 0);
        irt.sizeDelta = new Vector2(-140, 28);
        irt.offsetMin = new Vector2(125, -14);
        irt.offsetMax = new Vector2(-95, 14);
        var ifield = inputGo.AddComponent<InputField>();
        var img = inputGo.AddComponent<Image>();
        img.color = new Color(0.2f, 0.2f, 0.25f, 1f);
        var textGo = new GameObject("Text");
        textGo.transform.SetParent(inputGo.transform, false);
        var trt = textGo.AddComponent<RectTransform>();
        trt.anchorMin = Vector2.zero;
        trt.anchorMax = Vector2.one;
        trt.offsetMin = new Vector2(4, 2);
        trt.offsetMax = new Vector2(-4, -2);
        var tt = textGo.AddComponent<Text>();
        tt.fontSize = 14;
        tt.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        tt.color = Color.white;
        ifield.textComponent = tt;
        var ph = new GameObject("Placeholder");
        ph.transform.SetParent(inputGo.transform, false);
        var prt = ph.AddComponent<RectTransform>();
        prt.anchorMin = Vector2.zero;
        prt.anchorMax = Vector2.one;
        prt.offsetMin = new Vector2(4, 2);
        prt.offsetMax = new Vector2(-4, -2);
        var pt = ph.AddComponent<Text>();
        pt.text = "输入指令，后续对接 LLM…";
        pt.fontSize = 14;
        pt.color = new Color(0.5f, 0.5f, 0.5f, 0.8f);
        ifield.placeholder = pt;

        var sendGo = new GameObject("SendBtn");
        sendGo.transform.SetParent(row.transform, false);
        var srt = sendGo.AddComponent<RectTransform>();
        srt.anchorMin = new Vector2(1, 0.5f);
        srt.anchorMax = new Vector2(1, 0.5f);
        srt.pivot = new Vector2(1, 0.5f);
        srt.anchoredPosition = new Vector2(0, 0);
        srt.sizeDelta = new Vector2(70, 32);
        var simg = sendGo.AddComponent<Image>();
        simg.color = new Color(0.2f, 0.5f, 0.35f, 1f);
        var sendBtn = sendGo.AddComponent<Button>();
        sendBtn.targetGraphic = simg;
        var st = new GameObject("Text");
        st.transform.SetParent(sendGo.transform, false);
        var strt = st.AddComponent<RectTransform>();
        strt.anchorMin = Vector2.zero;
        strt.anchorMax = Vector2.one;
        strt.offsetMin = strt.offsetMax = Vector2.zero;
        var stxt = st.AddComponent<Text>();
        stxt.text = "发送";
        stxt.fontSize = 16;
        stxt.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        stxt.color = Color.white;
        stxt.alignment = TextAnchor.MiddleCenter;

        return (ifield, sendBtn);
    }

    private static GameObject CreateButton(Transform parent, string label, Action onClick, float width = 280f, float height = 44f)
    {
        var go = new GameObject("Button");
        go.transform.SetParent(parent, false);
        var rect = go.AddComponent<RectTransform>();
        rect.sizeDelta = new Vector2(width, height);
        var img = go.AddComponent<Image>();
        img.color = new Color(0.25f, 0.35f, 0.5f, 1f);
        var btn = go.AddComponent<Button>();
        btn.targetGraphic = img;

        var child = new GameObject("Text");
        child.transform.SetParent(go.transform, false);
        var childRect = child.AddComponent<RectTransform>();
        childRect.anchorMin = Vector2.zero;
        childRect.anchorMax = Vector2.one;
        childRect.offsetMin = childRect.offsetMax = Vector2.zero;
        var t = child.AddComponent<Text>();
        t.text = label;
        t.fontSize = height > 40 ? 20 : 16;
        t.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        t.color = Color.white;
        t.alignment = TextAnchor.MiddleCenter;

        btn.onClick.AddListener(() => onClick?.Invoke());
        return go;
    }

    private static void ClearChildren(Transform t)
    {
        for (int i = t.childCount - 1; i >= 0; i--)
            UnityEngine.Object.Destroy(t.GetChild(i).gameObject);
        if (_root != null) _root.SetActive(true);
    }
}
