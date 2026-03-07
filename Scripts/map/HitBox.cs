using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Hitbox
{
    private RectTransform targetRT; // 绑定的 UI 矩形
    public bool justHovered = false;

    public Hitbox(float width, float height, RectTransform rt)
    {
        // 在 UI 模式下，width 和 height 其实由 RectTransform 的 sizeDelta 决定
        // 这里主要存下引用
        this.targetRT = rt;
    }

    public void Move(Vector2 pos) { }
    public void Update() => justHovered = IsHovered();
    public bool IsHovered()
    {
        if (targetRT == null) return false;
        return RectTransformUtility.RectangleContainsScreenPoint(
            targetRT,
            Input.mousePosition,
            Camera.main
        );
    }
}
