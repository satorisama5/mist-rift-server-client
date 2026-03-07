using System.Collections.Generic;
using static MapRoomNode;

/// <summary>
/// 《迷雾裂隙》大地图存档结构。若项目内已有 GlobalMapSaveData/NodeSaveData，请将 roomMode 改为 roomType (MapManager.RoomType)。
/// </summary>
[System.Serializable]
public class GlobalMapSaveData
{
    public int width;
    public int height;
    public List<NodeSaveData> nodes = new List<NodeSaveData>();
    public List<EdgeSaveData> edges = new List<EdgeSaveData>();
}

[System.Serializable]
public class NodeSaveData
{
    public int x, y;
    public RoomState state;
    public MapManager.RoomType roomType;
}

[System.Serializable]
public class EdgeSaveData
{
    public int srcX, srcY, dstX, dstY;
    public bool taken;
}
