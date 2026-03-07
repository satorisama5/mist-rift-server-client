using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.U2D;
using UnityEngine.UI;


public class MapRoomNode
{
    public static int MapWidth = 20;
    public static int MapHeight = 7;

    private static readonly int IMG_WIDTH = 142;
    // 房间图片的宽度，用于计算房间之间的间距

    private static readonly float JITTER_X = 44f;
    // 节点在 X 方向的随机偏移量，使房间布局不完全整齐

    private static readonly float JITTER_Y = 48f;
    // 节点在 Y 方向的随机偏移量，增加地图自然感

    public static readonly Color AVAILABLE_COLOR = new Color(1f, 1f, 1f, 1f);

    private static readonly Color NOT_TAKEN_COLOR = new Color(0.6f, 0.6f, 0.6f, 1f);

    public static readonly Color COMPLETED_COLOR = new Color(0.8f, 0.8f, 0.8f, 1f);

    public enum RoomState { Locked, Available, Completed }

    //数据属性
    public int x, y;
    public bool highlighted;
    public RoomState state = RoomState.Locked;

    /// <summary> 房间类型：Combat / Event / Resource / PathChoice。 </summary>
    public MapManager.RoomType roomType;
    private List<MapRoomNode> parents;
    private List<MapEdge> edges;
    private float oscillateTimer;

    private GameObject roomGO;
    private Image roomImage;
    private RectTransform rt;

    private Vector2 basePos;

    public float offsetX;
    public float offsetY;
    private float scale;
    private Color color;
    public Hitbox hb;

    private bool flashing = false;
    private float flashTimer = 0f;

    public List<MapRoomNode> GetParents() => parents;


    public MapRoomNode(int x, int y, MapManager.RoomType roomType, RectTransform parent)
    {
        this.x = x;
        this.y = y;
        this.offsetX = Random.Range(-JITTER_X, JITTER_X);
        this.offsetY = Random.Range(-JITTER_Y, JITTER_Y);
        this.color = NOT_TAKEN_COLOR;
        this.scale = 0.5f;
        this.parents = new List<MapRoomNode>();
        this.edges = new List<MapEdge>();
        this.roomType = roomType;
        this.highlighted = false;
        this.oscillateTimer = Random.Range(0f, Mathf.PI * 2f);

        Sprite sprite = RiftOfFogMapResources.GetMapNodeSprite(this.roomType);

        // 创建物体
        this.roomGO = new GameObject($"Room_{x}_{y}");

        // 将组件赋值给类成员变量 this.rt
        this.rt = this.roomGO.AddComponent<RectTransform>();
        this.rt.SetParent(parent, false);

        this.roomImage = this.roomGO.AddComponent<Image>();
        this.roomImage.sprite = sprite;
        this.rt.sizeDelta = new Vector2(142, 142);    

        float usableW = parent.rect.width - 200f;
        float usableH = parent.rect.height - 200f;
        float nodeSpacingX = usableW / Mathf.Max(1, MapWidth - 1);
        float nodeSpacingY = usableH / Mathf.Max(1, MapHeight - 1);
        float parentWidth = parent.rect.width;
        float parentHeight = parent.rect.height;

        // 计算基准位置
        float finalX = (x * nodeSpacingX) - (parentWidth / 2.2f) + this.offsetX;
        float finalY = y * nodeSpacingY - (parentHeight / 4f) + this.offsetY;

        this.basePos = new Vector2(finalX, finalY);
        this.rt.anchoredPosition = this.basePos;

        // 初始化 Hitbox
        this.hb = new Hitbox(IMG_WIDTH, IMG_WIDTH, this.rt);

        var clicker = this.roomGO.AddComponent<MapRoomNodeClick>();
        clicker.owner = this;
    }

    public void SetActive(bool active)
    {
        if (roomGO != null)
        {
            roomGO.SetActive(active);
        }
    }

    public bool IsActive()
    {
        return roomGO != null && roomGO.activeSelf;
    }

    public void AddEdge(MapEdge edge)
    {
        if (!edges.Contains(edge)) edges.Add(edge);
    }

    public void AddParent(MapRoomNode parent)
    {
        if (!parents.Contains(parent)) parents.Add(parent);
    }

    public void SetCompleted()
    {
        state = RoomState.Completed;
        foreach (var edge in edges) edge.MarkAsTaken();
        StopFlashing();
    }

    public void ForceLock()
    {
        state = RoomState.Locked;
        StopFlashing();
    }

    public void StartFlashing() => flashing = true;
    public void StopFlashing() => flashing = false;

    public void Update()
    {
        float currentX = basePos.x;
        float currentY = basePos.y;

        // 更新 Hitbox 状态
        hb.Update();

        if (hb.IsHovered())
        {
            highlighted = true;
            scale = Mathf.Lerp(scale, 1f, 0.2f);
        }
        else
        {
            highlighted = false;
            scale = Mathf.Lerp(scale, 0.5f, 0.1f);
        }

        // --- 核心逻辑修改：根据状态处理颜色和呼吸效果 ---
        switch (state)
        {
            case RoomState.Locked:
                color = NOT_TAKEN_COLOR;
                color.a = 0.4f; // 锁定节点固定透明度，不闪烁
                break;
            case RoomState.Available:
                color = AVAILABLE_COLOR;
                // 只有可用节点产生呼吸效果
                oscillateTimer += Time.deltaTime * 4f;
                color.a = 0.6f + (Mathf.Cos(oscillateTimer) + 1f) / 4f; // Alpha 在 0.6 - 1.0 之间
                break;
            case RoomState.Completed:
                color = COMPLETED_COLOR;
                color.a = 1f; // 已完成节点常亮
                break;
        }

        // --- 核心逻辑修改：优化 Flashing 表现 ---
        if (flashing)
        {
            flashTimer += Time.deltaTime * 8f;
            // 使用更明显的正弦波，且确保不至于完全黑掉图标
            float pulse = (Mathf.Sin(flashTimer) + 1f) / 2f;
            color.a = Mathf.Lerp(0.5f, 1f, pulse);
        }

        Render(currentX, currentY);
    }

    private void Render(float xPos, float yPos)
    {
        if (roomGO != null)
        {
            rt.anchoredPosition = new Vector2(xPos, yPos);
            rt.localScale = Vector3.one * scale;

            if (roomImage != null) roomImage.color = color;
        }
    }

    public Vector2 GetPosition()
    {
        return rt.anchoredPosition;
    }

    public List<MapEdge> GetEdges() => edges;
}