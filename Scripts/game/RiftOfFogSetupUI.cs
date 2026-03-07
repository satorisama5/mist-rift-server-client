using System;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 《迷雾裂隙》开局「设置人物属性」面板。确认后写入 RiftOfFogPlayerStats，影响后续 LLM 路径选择。
/// </summary>
public static class RiftOfFogSetupUI
{
    private static GameObject _root;
    private static bool _visible;

    public static bool IsVisible => _visible;

    /// <summary> 显示设置面板；确认时写入 PlayerStats 并调用 onConfirm。若 Instance 为 null 则不显示。 </summary>
    public static void Show(Action onConfirm)
    {
        if (RiftOfFogPlayerStats.Instance == null)
        {
            onConfirm?.Invoke();
            return;
        }

        EnsureCanvas();
        _visible = true;
        ClearChildren(_root.transform);

        var s = RiftOfFogPlayerStats.Instance;
        var panel = CreatePanel(_root.transform);
        var layout = panel.AddComponent<VerticalLayoutGroup>();
        layout.spacing = 10f;
        layout.padding = new RectOffset(24, 24, 24, 24);
        layout.childAlignment = TextAnchor.MiddleCenter;
        layout.childControlHeight = layout.childControlWidth = false;

        CreateText(panel.transform, "设置人物属性（可拖拽滑块调数值，影响 AI 路径建议）", 22);

        var hp = CreateSliderRow(panel.transform, "HP", s.Base.HP, 0, 100);
        var ap = CreateSliderRow(panel.transform, "AP", s.Base.AP, 0, 10);
        var str = CreateSliderRow(panel.transform, "STR", s.Base.STR, 1, 20);
        var intVal = CreateSliderRow(panel.transform, "INT", s.Base.INT, 1, 20);
        var lck = CreateSliderRow(panel.transform, "LCK", s.Base.LCK, 1, 20);
        var stealth = CreateSliderRow(panel.transform, "STEALTH", s.Base.STEALTH, 1, 20);

        var goalRow = CreateInputRow(panel.transform, "当前目标（选填：宝藏/出口/战斗）", s.AIMemory.CurrentGoal ?? "出口");
        CreateButton(panel.transform, "确认开始", () =>
        {
            s.Base.HP = (int)hp.value;
            s.Base.AP = (int)ap.value;
            s.Base.STR = (int)str.value;
            s.Base.INT = (int)intVal.value;
            s.Base.LCK = (int)lck.value;
            s.Base.STEALTH = (int)stealth.value;
            s.Base.Clamp();
            var goalInput = goalRow.GetComponentInChildren<InputField>();
            if (goalInput != null && !string.IsNullOrWhiteSpace(goalInput.text))
                s.AIMemory.CurrentGoal = goalInput.text.Trim();
            s.RefreshFatigue();
            _visible = false;
            ClearChildren(_root.transform);
            if (_root != null) _root.SetActive(false);
            onConfirm?.Invoke();
        });

        var rect = panel.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = new Vector2(420, 420);
        rect.anchoredPosition = Vector2.zero;
    }

    public static void Hide()
    {
        _visible = false;
        if (_root != null)
        {
            ClearChildren(_root.transform);
            _root.SetActive(false);
        }
    }

    private static void EnsureCanvas()
    {
        if (_root != null) return;
        _root = new GameObject("RiftOfFogSetupUI");
        var c = _root.AddComponent<Canvas>();
        c.renderMode = RenderMode.ScreenSpaceOverlay;
        c.sortingOrder = 110;
        _root.AddComponent<CanvasScaler>().uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        _root.AddComponent<GraphicRaycaster>();
        UnityEngine.Object.DontDestroyOnLoad(_root);
    }

    private static GameObject CreatePanel(Transform parent)
    {
        var go = new GameObject("Panel");
        go.transform.SetParent(parent, false);
        var rect = go.AddComponent<RectTransform>();
        var img = go.AddComponent<Image>();
        img.color = new Color(0.1f, 0.12f, 0.2f, 0.98f);
        return go;
    }

    private static GameObject CreateText(Transform parent, string content, int fontSize)
    {
        var go = new GameObject("Text");
        go.transform.SetParent(parent, false);
        var rect = go.AddComponent<RectTransform>();
        rect.sizeDelta = new Vector2(380, fontSize + 8);
        var text = go.AddComponent<Text>();
        text.text = content;
        text.fontSize = fontSize;
        text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        text.color = Color.white;
        text.alignment = TextAnchor.MiddleCenter;
        return go;
    }

    private static Slider CreateSliderRow(Transform parent, string label, int initial, int min, int max)
    {
        var row = new GameObject("Row_" + label);
        row.transform.SetParent(parent, false);
        var rowRect = row.AddComponent<RectTransform>();
        rowRect.sizeDelta = new Vector2(360, 32);
        var h = row.AddComponent<HorizontalLayoutGroup>();
        h.spacing = 8;
        h.childAlignment = TextAnchor.MiddleCenter;
        h.childControlWidth = true;
        h.childControlHeight = true;

        var lbl = new GameObject("Label");
        lbl.transform.SetParent(row.transform, false);
        var lrt = lbl.AddComponent<RectTransform>();
        lrt.sizeDelta = new Vector2(80, 28);
        var lt = lbl.AddComponent<Text>();
        lt.text = label + ":";
        lt.fontSize = 16;
        lt.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        lt.color = Color.white;

        var sl = new GameObject("Slider");
        sl.transform.SetParent(row.transform, false);
        var srt = sl.AddComponent<RectTransform>();
        srt.sizeDelta = new Vector2(200, 24);
        var bg = sl.AddComponent<Image>();
        bg.color = new Color(0.2f, 0.2f, 0.25f, 1f);
        bg.raycastTarget = true;

        var fillArea = new GameObject("Fill Area");
        fillArea.transform.SetParent(sl.transform, false);
        var fillAreaRect = fillArea.AddComponent<RectTransform>();
        fillAreaRect.anchorMin = new Vector2(0, 0.25f);
        fillAreaRect.anchorMax = new Vector2(1, 0.75f);
        fillAreaRect.offsetMin = new Vector2(5, 2);
        fillAreaRect.offsetMax = new Vector2(-5, -2);

        var fill = new GameObject("Fill");
        fill.transform.SetParent(fillArea.transform, false);
        var fillRect = fill.AddComponent<RectTransform>();
        fillRect.anchorMin = Vector2.zero;
        fillRect.anchorMax = Vector2.one;
        fillRect.offsetMin = fillRect.offsetMax = Vector2.zero;
        var fillImg = fill.AddComponent<Image>();
        fillImg.color = new Color(0.2f, 0.5f, 0.35f, 1f);
        fillImg.raycastTarget = false;

        var slider = sl.AddComponent<Slider>();
        slider.minValue = min;
        slider.maxValue = max;
        slider.value = initial;
        slider.fillRect = fillRect;
        slider.targetGraphic = bg;

        var val = new GameObject("Value");
        val.transform.SetParent(row.transform, false);
        var vrt = val.AddComponent<RectTransform>();
        vrt.sizeDelta = new Vector2(40, 28);
        var vt = val.AddComponent<Text>();
        vt.text = initial.ToString();
        vt.fontSize = 16;
        vt.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        vt.color = Color.white;
        slider.onValueChanged.AddListener(v => vt.text = ((int)v).ToString());

        return slider;
    }

    private static GameObject CreateInputRow(Transform parent, string label, string initial)
    {
        var row = new GameObject("Row_Goal");
        row.transform.SetParent(parent, false);
        var rowRect = row.AddComponent<RectTransform>();
        rowRect.sizeDelta = new Vector2(360, 36);
        var h = row.AddComponent<HorizontalLayoutGroup>();
        h.spacing = 8;
        h.childAlignment = TextAnchor.MiddleCenter;

        var lbl = new GameObject("Label");
        lbl.transform.SetParent(row.transform, false);
        var lt = lbl.AddComponent<Text>();
        lt.text = label;
        lt.fontSize = 14;
        lt.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        lt.color = Color.white;

        var inputGo = new GameObject("InputField");
        inputGo.transform.SetParent(row.transform, false);
        var ifield = inputGo.AddComponent<InputField>();
        var img = inputGo.AddComponent<Image>();
        img.color = new Color(0.2f, 0.2f, 0.25f, 1f);
        var text = new GameObject("Text");
        text.transform.SetParent(inputGo.transform, false);
        var trt = text.AddComponent<RectTransform>();
        trt.anchorMin = Vector2.zero;
        trt.anchorMax = Vector2.one;
        trt.offsetMin = new Vector2(6, 2);
        trt.offsetMax = new Vector2(-6, -2);
        var tt = text.AddComponent<Text>();
        tt.text = initial;
        tt.fontSize = 14;
        tt.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        tt.color = Color.white;
        ifield.textComponent = tt;
        ifield.text = initial;
        var place = new GameObject("Placeholder");
        place.transform.SetParent(inputGo.transform, false);
        var prt = place.AddComponent<RectTransform>();
        prt.anchorMin = Vector2.zero;
        prt.anchorMax = Vector2.one;
        prt.offsetMin = new Vector2(6, 2);
        prt.offsetMax = new Vector2(-6, -2);
        var pt = place.AddComponent<Text>();
        pt.text = "出口";
        pt.fontSize = 14;
        pt.color = new Color(0.5f, 0.5f, 0.5f, 0.8f);
        ifield.placeholder = pt;

        return row;
    }

    private static void CreateButton(Transform parent, string label, Action onClick)
    {
        var go = new GameObject("Button");
        go.transform.SetParent(parent, false);
        var rect = go.AddComponent<RectTransform>();
        rect.sizeDelta = new Vector2(200, 44);
        var img = go.AddComponent<Image>();
        img.color = new Color(0.2f, 0.45f, 0.3f, 1f);
        var btn = go.AddComponent<Button>();
        btn.targetGraphic = img;
        var child = new GameObject("Text");
        child.transform.SetParent(go.transform, false);
        var crt = child.AddComponent<RectTransform>();
        crt.anchorMin = Vector2.zero;
        crt.anchorMax = Vector2.one;
        crt.offsetMin = crt.offsetMax = Vector2.zero;
        var t = child.AddComponent<Text>();
        t.text = label;
        t.fontSize = 20;
        t.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        t.color = Color.white;
        t.alignment = TextAnchor.MiddleCenter;
        btn.onClick.AddListener(() => onClick?.Invoke());
    }

    private static void ClearChildren(Transform t)
    {
        for (int i = t.childCount - 1; i >= 0; i--)
            UnityEngine.Object.Destroy(t.GetChild(i).gameObject);
        if (_root != null) _root.SetActive(true);
    }
}
