using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI; // 必须引用 UI

public class MapEdge
{

    public int srcX, srcY;
    public int dstX, dstY;
    public bool taken;
    public Color color;
    private List<GameObject> dots = new List<GameObject>();

    // 视觉常量
    private const float ICON_SRC_RADIUS = 29f;
    private const float ICON_DST_RADIUS = 20f;
    private const float SPACING = 17f; // 采样间距

    //和mapnode一致
    private const float SPACE_X = 256f;
    private const float SPACE_Y = 180f;


    private static readonly Color DISABLED_COLOR = new Color(0f, 0f, 0f, 0.25f);


    public MapEdge(int srcX, int srcY, int dstX, int dstY)
    {
        this.srcX = srcX; this.srcY = srcY;
        this.dstX = dstX; this.dstY = dstY;
        this.taken = false;
        this.color = DISABLED_COLOR;
    }

    // 这里生成虚线点
    private void CreateDots(RectTransform parent, Vector2 startPos, Vector2 endPos)
    {
        float parentWidth = parent.rect.width;
        float parentHeight = parent.rect.height;

        Vector2 dir = (endPos - startPos).normalized; // 用传入的坐标算方向
        float distance = Vector2.Distance(startPos, endPos); // 用传入的坐标算距离

        float startOffset = Random.Range(0f, SPACING / 2f);

        // 循环生成点
        for (float i = startOffset + ICON_DST_RADIUS; i < distance - ICON_SRC_RADIUS; i += SPACING)
        {
            Vector2 basePos = startPos + dir * i;

            Vector2 finalPos = basePos + new Vector2(Random.Range(-2f, 2f), Random.Range(-2f, 2f));

            // 创建空物体
            GameObject dotGO = new GameObject("Dot");
            RectTransform dotRT = dotGO.AddComponent<RectTransform>();
            dotRT.SetParent(parent, false);

            // 设置位置
            dotRT.anchoredPosition = finalPos;
            dotRT.sizeDelta = new Vector2(8, 8); // 点的大小

   
            Image img = dotGO.AddComponent<Image>();
            img.color = this.color;
            img.raycastTarget = false; // 优化性能，不挡射线

            dots.Add(dotGO);
        }
    }

    public void MarkAsTaken()
    {
        taken = true;
        color = MapRoomNode.AVAILABLE_COLOR;
        foreach (var dot in dots)
        {
            // 【关键修改】：获取 Image 组件修改颜色
            if (dot != null) dot.GetComponent<Image>().color = color;
        }
    }

    public void Render(RectTransform parent, Vector2 startPos, Vector2 endPos)
    {
        if (dots.Count == 0)
        {
            // 传给 CreateDots
            CreateDots(parent, startPos, endPos);
        }

        foreach (var dot in dots)
        {
            if (dot != null) dot.GetComponent<Image>().color = color;
        }
    }
}