using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public static class MapGenerator
{
    /// <summary>
    /// 生成整齐节点 + 路径
    /// </summary>
    public static List<List<MapRoomNode>> GenerateDungeon(int width, int height, int pathDensity, System.Random rng, RectTransform mapContent)
    {
        var map = CreateNodes(width, height, mapContent);
        map = CreatePaths(map, pathDensity, rng);
        return map;
    }

    // 每列 width，每行 height，节点整齐排列
    private static List<List<MapRoomNode>> CreateNodes(int width, int height, RectTransform mapContent)
    {
        var nodes = new List<List<MapRoomNode>>();
        for (int y = 0; y < height; y++)
        {
            var row = new List<MapRoomNode>();
            for (int x = 0; x < width; x++)
            {
                int typeCount = System.Enum.GetValues(typeof(MapManager.RoomType)).Length;
                MapManager.RoomType randomType = (MapManager.RoomType)Random.Range(0, typeCount);
                row.Add(new MapRoomNode(x, y, randomType, mapContent));
            }
            nodes.Add(row);
        }
        return nodes;
    }

    // 【修改后的版本】：增加三叉分支的概率，同时保持整体密度可控
    private static List<List<MapRoomNode>> CreatePaths(List<List<MapRoomNode>> nodes, int pathDensity, System.Random rng)
    {
        int width = nodes[0].Count;
        int height = nodes.Count;

        // 1. 确定第一列的起始点
        List<int> startYs = new List<int>();
        for (int i = 0; i < height; i++) startYs.Add(i);
        startYs = startYs.OrderBy(x => rng.Next()).Take(pathDensity).ToList();
        startYs.Sort();

        // 2. 逐列扫描生成
        for (int x = 0; x < width - 1; x++)
        {
            int activeNodeCount = 0;
            for (int y = 0; y < height; y++)
            {
                if (x == 0 && startYs.Contains(y)) activeNodeCount++;
                else if (nodes[y][x].GetParents().Count > 0) activeNodeCount++;
            }

            int currentColumnMinY = 0;

            for (int y = 0; y < height; y++)
            {
                MapRoomNode currentNode = nodes[y][x];

                bool isStartNode = (x == 0 && startYs.Contains(y));
                bool hasParents = currentNode.GetParents().Count > 0;

                if (!isStartNode && !hasParents) continue;

                int edgeCount = 1;
                int roll = rng.Next(0, 100);

                // 如果路径太少，疯狂分叉
                if (activeNodeCount < pathDensity)
                {
                    if (roll < 25) edgeCount = 2;
                    else if (roll < 35) edgeCount = 3;
                    else edgeCount = 1;
                }
                else
                {
                    if (roll < 25) edgeCount = 2;
                    else if (roll < 33) edgeCount = 3;
                    else edgeCount = 1;
                }

                // --- 步骤 B: 寻找候选目标 ---
                List<int> candidates = new List<int>();

                // 只能连 y-1, y, y+1
                int bottomLimit = Mathf.Max(0, y - 1);
                int topLimit = Mathf.Min(height - 1, y + 1);

                for (int targetY = bottomLimit; targetY <= topLimit; targetY++)
                {
                    // 防交叉：必须 >= 上一个兄弟占用的位置
                    if (targetY >= currentColumnMinY)
                    {
                        candidates.Add(targetY);
                    }
                }

                // 兜底逻辑：如果被挤得没地儿去了（候选数为0），强制汇合到 currentColumnMinY
                if (candidates.Count == 0 && currentColumnMinY < height)
                {
                    candidates.Add(currentColumnMinY);
                }

                // --- 步骤 C: 随机选取 ---
                // 打乱列表，这样连线会有随机感
                candidates = candidates.OrderBy(c => rng.Next()).ToList();

                // 取最小值：想连的数量 vs 实际能连的数量
                int finalCount = Mathf.Min(edgeCount, candidates.Count);

                // 直走优化：如果是单传且能直走，50%概率强制直走（为了美观）
                if (finalCount == 1 && candidates.Contains(y) && rng.Next(0, 100) < 50)
                {
                    candidates.Clear();
                    candidates.Add(y);
                }

                var finalTargets = candidates.Take(finalCount).ToList();
                finalTargets.Sort();

                // --- 步骤 D: 连线 ---
                if (finalTargets.Count > 0)
                {
                    foreach (int targetY in finalTargets)
                    {
                        MapRoomNode targetNode = nodes[targetY][x + 1];

                        // 获取当前两点的真实位置（其实这步只在Update里渲染用到，这里只要建立数据关联）
                        MapEdge newEdge = new MapEdge(x, y, x + 1, targetY);

                        currentNode.AddEdge(newEdge);
                        targetNode.AddParent(currentNode);
                    }
                    // 更新防交叉底线
                    currentColumnMinY = finalTargets.Max();
                }
            }
        }

        return nodes;
    }

    private static MapRoomNode GetNode(int x, int y, List<List<MapRoomNode>> nodes) => nodes[y][x];
    private static int RandRange(System.Random rng, int min, int max) => rng.Next(min, max + 1);

    // 辅助函数
    private static MapRoomNode GetCommonAncestor(MapRoomNode a, MapRoomNode b, int maxDepth, List<List<MapRoomNode>> nodes)
    {
        if (a.y != b.y || a == b) return null;
        var left = a.x < b.x ? a : b;
        var right = a.x < b.x ? b : a;
        int currentY = a.y;
        while (currentY >= 0 && currentY >= a.y - maxDepth)
        {
            if (left.GetParents().Count > 0 && right.GetParents().Count > 0)
            {
                left = GetNodeWithMaxX(left.GetParents());
                right = GetNodeWithMinX(right.GetParents());
                if (left == right) return left;
                currentY--;
                continue;
            }
            return null;
        }
        return null;
    }
    private static MapRoomNode GetNodeWithMaxX(List<MapRoomNode> list)
    {
        MapRoomNode max = list[0];
        foreach (var node in list) if (node.x > max.x) max = node;
        return max;
    }
    private static MapRoomNode GetNodeWithMinX(List<MapRoomNode> list)
    {
        MapRoomNode min = list[0];
        foreach (var node in list) if (node.x < min.x) min = node;
        return min;
    }

    private static MapEdge GetMaxEdge(List<MapEdge> edges)
    {
        edges.Sort((a, b) => a.dstY.CompareTo(b.dstY));
        return edges[edges.Count - 1];
    }
    private static MapEdge GetMinEdge(List<MapEdge> edges)
    {
        edges.Sort((a, b) => a.dstY.CompareTo(b.dstY));
        return edges[0];
    }

    //补充退出方法

}