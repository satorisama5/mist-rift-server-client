using UnityEngine;

/// <summary>
/// 《迷雾裂隙》地图节点图标加载。从 Resources/MapNodes/ 按房间类型加载 Sprite；
/// 若不存在则使用内置占位图（可后续替换为你的资源路径）。
/// </summary>
public static class RiftOfFogMapResources
{
    private const string MapNodesPath = "MapNodes";

    private static Sprite _defaultSprite;

    public static Sprite GetMapNodeSprite(MapManager.RoomType roomType)
    {
        string name = roomType.ToString();
        var sprite = Resources.Load<Sprite>(MapNodesPath + "/" + name);
        if (sprite != null) return sprite;
        return GetOrCreateDefaultSprite();
    }

    private static Sprite GetOrCreateDefaultSprite()
    {
        if (_defaultSprite != null) return _defaultSprite;
        var tex = new Texture2D(64, 64);
        var fill = new Color(0.4f, 0.5f, 0.6f, 0.9f);
        for (int x = 0; x < 64; x++)
            for (int y = 0; y < 64; y++)
                tex.SetPixel(x, y, fill);
        tex.Apply();
        _defaultSprite = Sprite.Create(tex, new Rect(0, 0, 64, 64), new Vector2(0.5f, 0.5f));
        return _defaultSprite;
    }
}
