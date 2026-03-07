using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using static MapRoomNode;

/// <summary>
/// 《迷雾裂隙》Rift of Fog - 地图与房间类型。
/// A=战斗 B=事件 C=资源 D=路径选择（分叉路口）。
/// </summary>
public class MapManager : MonoBehaviour
{
    /// <summary> 房间类型：对应后端 A/B/C/D 类与微服务路由。 </summary>
    public enum RoomType
    {
        Combat,     // A 类 - 战斗房间 → Combat Service
        Event,      // B 类 - 事件房间 → Event Service
        Resource,   // C 类 - 资源房间 → Resource Service
        PathChoice, // D 类 - 路径选择房间（分叉路口，语义提示）
    }

    public static RoomType CurrentRoomType { get; private set; } = RoomType.Combat;
    public static MapManager Instance { get; private set; }


    [Header("地图大小/密度")]
    public int mapWidth = 20;
    public int mapHeight = 7;
    public int pathDensity = 4;

    [Header("地图容器")]
    [Tooltip("拖入 Map_Content_Draggable（实际拖拽的容器，节点与连线生成在此）")]
    public GameObject mapContent;
    public GameObject mapRoot;

    private Vector3 lastMousePos;
    private bool dragging = false;
    private List<List<MapRoomNode>> mapNodes = new List<List<MapRoomNode>>();
    /// <summary> Map_Content_Draggable 的 RectTransform，用于移动和生成节点。 </summary>
    private RectTransform _mapContentRect;
    /// <summary> Map_System_Root 的 RectTransform，用于拖拽边界与点击检测（mapContent 的父物体）。 </summary>
    private RectTransform _mapCanvasParentRect;
    private Canvas _mapCanvas;

    /// <summary> 当前进入的房间节点（供 LLM 场景上下文使用）。 </summary>
    private MapRoomNode _currentEnteredNode;
    /// <summary> D 类路径选择时的语义提示列表。 </summary>
    private List<string> _currentPathHints = new List<string>();

    /// <summary> 当前房间节点 X，无则为 -1。 </summary>
    public int CurrentNodeX => _currentEnteredNode != null ? _currentEnteredNode.x : -1;
    /// <summary> 当前房间节点 Y，无则为 -1。 </summary>
    public int CurrentNodeY => _currentEnteredNode != null ? _currentEnteredNode.y : -1;
    /// <summary> 当前房间类型。 </summary>
    public RoomType CurrentNodeRoomType => _currentEnteredNode != null ? _currentEnteredNode.roomType : CurrentRoomType;
    /// <summary> D 类时的路径提示（只读）。 </summary>
    public IReadOnlyList<string> CurrentPathHints => _currentPathHints;

    /// <summary> 从当前进入的节点出发，递归获取后续 depth 层的房间类型序列，供 LLM 看后续路径。每条可能路径为 "Combat→Resource→PathChoice" 格式。 </summary>
    public List<string> GetFuturePathsForCurrentNode(int depth = 3)
    {
        if (_currentEnteredNode == null || depth <= 0) return new List<string>();
        return GetFuturePaths(_currentEnteredNode, depth);
    }

    /// <summary> 从 node 出发递归收集后续 depth 层的房间类型路径序列。 </summary>
    private List<string> GetFuturePaths(MapRoomNode node, int depth)
    {
        if (node == null || depth <= 0) return new List<string>();
        string head = node.roomType.ToString();
        if (depth == 1) return new List<string> { head };
        var edges = node.GetEdges();
        if (edges == null || edges.Count == 0) return new List<string> { head };
        var result = new List<string>();
        foreach (var edge in edges)
        {
            if (edge.dstY >= mapNodes.Count || edge.dstX >= mapNodes[edge.dstY].Count) continue;
            var nextNode = mapNodes[edge.dstY][edge.dstX];
            if (nextNode == null || !nextNode.IsActive()) continue;
            var subPaths = GetFuturePaths(nextNode, depth - 1);
            foreach (var sub in subPaths)
                result.Add(head + "→" + sub);
        }
        if (result.Count == 0) result.Add(head);
        return result;
    }

    private void Awake()
    {
        if (Instance == null) Instance = this;
        else Destroy(gameObject);

        if (mapContent != null)
        {
            _mapContentRect = mapContent.GetComponent<RectTransform>();
            // 边界与遮罩：mapContent 的直接父物体（Map_System_Root），节点坐标基于其尺寸计算
            if (mapContent.transform.parent != null)
            {
                _mapCanvasParentRect = mapContent.transform.parent.GetComponent<RectTransform>();
                _mapCanvas = mapContent.transform.parent.GetComponentInParent<Canvas>();
            }
        }
    }

    private void Start()
    {
        if (mapRoot != null)
            mapRoot.SetActive(false);
        if (RiftOfFogPlayerStats.Instance != null)
        {
            RiftOfFogSetupUI.Show(() =>
            {
                if (mapRoot != null)
                    mapRoot.SetActive(true);
                InitMap();
            });
        }
        else
        {
            if (mapRoot != null)
                mapRoot.SetActive(true);
            InitMap();
        }
        WebSocketClient.OnServerResponseReceived += HandleServerResponse;
    }

    private void OnDestroy()
    {
        WebSocketClient.OnServerResponseReceived -= HandleServerResponse;
    }

    private void HandleServerResponse(WebSocketClient.ServerResponseData data)
    {
        if (data == null) return;
        if (data.type == "llm_suggestion")
        {
            // 优先显示 suggestion，为空时用 ai_reasoning，确保终端里的完整建议能显示在 UI
            string text = !string.IsNullOrEmpty(data.suggestion) ? data.suggestion : (data.ai_reasoning ?? "");
            RiftOfFogMessageBridge.DeliverLLMResponse(text);
            if (WebSocketClient.Instance != null)
                WebSocketClient.Instance.PendingSuggestedAction = data.action;
        }
        else if (data.type == "action_result")
        {
            if (RiftOfFogPlayerStats.Instance != null && data.state_changes != null)
            {
                var sc = data.state_changes;
                RiftOfFogPlayerStats.Instance.ApplyStateChanges(sc.hp_delta, sc.ap_delta, sc.new_status ?? "", sc.clear_status ?? "");
            }
            if (_currentEnteredNode != null)
            {
                var node = _currentEnteredNode;
                OnPlaceholderRoomFinished(node);
            }
        }
    }

    public void InitMap()
    {
        if (mapContent == null || _mapContentRect == null)
        {
            Debug.LogWarning("[MapManager] mapContent 未赋值，无法生成地图。");
            return;
        }

        System.Random rng = new System.Random();

        // 1. 生成全量网格 (此时屏幕上会出现整齐的方阵)
        mapNodes = MapGenerator.GenerateDungeon(mapWidth, mapHeight, pathDensity, rng, _mapContentRect);

        if (mapNodes != null)
        {
            foreach (var row in mapNodes)
            {
                foreach (var node in row)
                {
                    bool isValid = false;

                    // 【修改点 1】：判断 x == 0 (第一列) 作为起点
                    if (node.x == 0)
                    {
                        isValid = node.GetEdges().Count > 0;
                    }
                    else
                    {
                        isValid = node.GetParents().Count > 0;
                    }

                    // 如果无效，直接隐藏
                    if (!isValid)
                    {
                        node.SetActive(false);
                    }
                }
            }
        }

        // 3. 渲染连线 (只渲染有效节点的连线)
        RenderLines();

        // 4. 激活第一层可见的节点
        if (mapNodes != null)
        {
            // 【修改点 2】：遍历每一行，取第 0 个元素（即第一列）
            for (int y = 0; y < mapNodes.Count; y++)
            {
                if (mapNodes[y].Count > 0)
                {
                    var node = mapNodes[y][0]; // 获取当前行 x=0 的节点

                    if (node.IsActive()) // 只激活显示的
                    {
                        node.state = MapRoomNode.RoomState.Available;
                        node.StartFlashing();
                    }
                }
            }
        }

        // 初始显示第一列节点（地图从左侧生成，避免居中时看不到起点）
        if (_mapCanvasParentRect != null && _mapContentRect != null)
        {
            float parentW = _mapCanvasParentRect.rect.width;
            float contentW = _mapContentRect.rect.width;
            float leftLimit = (contentW - parentW) / 2f;
            _mapContentRect.anchoredPosition = new Vector2(leftLimit, 0f);
        }
    }

    // 把画线的逻辑单独提出来
    private void RenderLines()
    {
        if (mapNodes == null) return;

        foreach (var row in mapNodes)
        {
            foreach (var node in row)
            {
                // 只有节点自己是显示的，才画它的连线
                if (node.IsActive())
                {
                    foreach (var edge in node.GetEdges())
                    {
                        // 1. 获取当前节点（起点）的真实坐标
                        Vector2 startPos = node.GetPosition();

                        // 2. 找到目标节点（终点）
                        // edge 存了 dstX 和 dstY，我们去 mapNodes 里取出来
                        MapRoomNode targetNode = mapNodes[edge.dstY][edge.dstX];

                        // 3. 获取目标节点的真实坐标
                        Vector2 endPos = targetNode.GetPosition();

                        // 4. 把两个真实坐标传给 Edge 进行渲染
                        edge.Render(_mapContentRect, startPos, endPos);
                    }
                }
            }
        }
    }

    public void OnRoomClicked(MapRoomNode node)
    {
        if (node.state != MapRoomNode.RoomState.Available)
            return;
        Debug.Log("[迷雾裂隙] 点击房间 (" + node.x + "," + node.y + ")，类型: " + node.roomType);

        CurrentRoomType = node.roomType;

        // D 类路径选择房间：先弹出选择 UI，选完再完成节点并只解锁所选路径
        if (node.roomType == RoomType.PathChoice)
        {
            ShowPathChoiceUI(node);
            return;
        }

        // A/B/C 类：进入对应房间（当前为占位，待服务端接入）
        switch (node.roomType)
        {
            case RoomType.Combat:
                EnterCombatRoom(node);
                break;
            case RoomType.Event:
                EnterEventRoom(node);
                break;
            case RoomType.Resource:
                EnterResourceRoom(node);
                break;
            default:
                FinishRoomAndUnlockNext(node);
                break;
        }
    }

    /// <summary> 完成当前房间并解锁所有下一层节点（A/B/C 通用）。 </summary>
    private void FinishRoomAndUnlockNext(MapRoomNode node)
    {
        LockSameColumnOthers(node);
        node.SetCompleted();
        UnlockNextLayer(node);
    }

    private void LockSameColumnOthers(MapRoomNode node)
    {
        for (int y = 0; y < mapNodes.Count; y++)
        {
            var sameColumnNode = mapNodes[y][node.x];
            if (sameColumnNode != node && sameColumnNode.IsActive())
                sameColumnNode.ForceLock();
        }
    }

    /// <summary> 解锁当前节点的所有下一层节点（两条路就两个都亮、都可选）。先按边解锁，再兜底：下一列中所有以本节点为父且仍为 Locked 的也解锁。 </summary>
    private void UnlockNextLayer(MapRoomNode node)
    {
        int nextX = node.x + 1;
        if (nextX >= mapNodes[0].Count) return;

        // 1) 按边解锁
        foreach (var edge in node.GetEdges())
        {
            if (edge.dstY >= 0 && edge.dstY < mapNodes.Count && edge.dstX < mapNodes[edge.dstY].Count)
            {
                MapRoomNode nextNode = mapNodes[edge.dstY][edge.dstX];
                if (nextNode != null && nextNode.IsActive() && nextNode.state == MapRoomNode.RoomState.Locked)
                {
                    nextNode.state = MapRoomNode.RoomState.Available;
                    nextNode.StartFlashing();
                }
            }
        }

        // 2) 兜底：下一列里所有以当前节点为父且仍为 Locked 的节点也解锁，避免漏解导致“只亮一条”
        for (int y = 0; y < mapNodes.Count; y++)
        {
            if (nextX >= mapNodes[y].Count) continue;
            MapRoomNode nextNode = mapNodes[y][nextX];
            if (nextNode == null || !nextNode.IsActive() || nextNode.state != MapRoomNode.RoomState.Locked) continue;
            if (!nextNode.GetParents().Contains(node)) continue;
            nextNode.state = MapRoomNode.RoomState.Available;
            nextNode.StartFlashing();
        }
    }

    // ---------- A 类：战斗房间 ----------
    private void EnterCombatRoom(MapRoomNode node)
    {
        _currentEnteredNode = node;
        _currentPathHints.Clear();
        RiftOfFogRoomUI.ShowCombatRoom(
            () => OnPlaceholderRoomFinished(node),
            (roomType, actionKey) => OnRoomActionOrMessageSent(roomType, actionKey)
        );
    }

    // ---------- B 类：事件房间 ----------
    private void EnterEventRoom(MapRoomNode node)
    {
        _currentEnteredNode = node;
        _currentPathHints.Clear();
        RiftOfFogRoomUI.ShowEventRoom(
            () => OnPlaceholderRoomFinished(node),
            (roomType, actionKey) => OnRoomActionOrMessageSent(roomType, actionKey)
        );
    }

    // ---------- C 类：资源房间 ----------
    private void EnterResourceRoom(MapRoomNode node)
    {
        _currentEnteredNode = node;
        _currentPathHints.Clear();
        RiftOfFogRoomUI.ShowResourceRoom(
            () => OnPlaceholderRoomFinished(node),
            (roomType, actionKey) => OnRoomActionOrMessageSent(roomType, actionKey)
        );
    }

    /// <summary> 玩家点击行为按钮或发送自然语言后调用。已连接 WebSocket 时由服务端返回驱动 UI；未连接时用本地模拟。 </summary>
    private void OnRoomActionOrMessageSent(MapManager.RoomType roomType, string actionKey)
    {
        if (WebSocketClient.Instance != null && WebSocketClient.Instance.IsConnected)
            return; // 响应由 HandleServerResponse 处理
        StartCoroutine(SimulateServerResponse(roomType, actionKey));
    }

    private static readonly string[] CombatSuggestions = new[] { "可以试试先引诱再强攻", "潜行绕过比较省 AP", "谈判撤退会更稳一些" };
    private static readonly string[] EventSuggestions = new[] { "你 INT 高，不妨先调查看看", "强行破坏有风险，建议谨慎", "忽略继续不消耗资源" };
    private static readonly string[] ResourceSuggestions = new[] { "血少的话建议先检查再拿", "可以优先和商人交易看看", "留下标记以后能回忆路线" };

    private IEnumerator SimulateServerResponse(MapManager.RoomType roomType, string actionKey)
    {
        yield return new WaitForSeconds(0.8f);
        string suggestion = roomType == RoomType.Combat ? CombatSuggestions[Random.Range(0, CombatSuggestions.Length)]
            : roomType == RoomType.Event ? EventSuggestions[Random.Range(0, EventSuggestions.Length)]
            : ResourceSuggestions[Random.Range(0, ResourceSuggestions.Length)];
        RiftOfFogMessageBridge.DeliverLLMResponse(suggestion);
    }

    private void OnPlaceholderRoomFinished(MapRoomNode node)
    {
        RiftOfFogRoomUI.HideIfVisible();
        _currentEnteredNode = null;
        _currentPathHints.Clear();
        if (RiftOfFogPlayerStats.Instance != null)
            RiftOfFogPlayerStats.Instance.RestoreAP(5);
        LockSameColumnOthers(node);
        node.SetCompleted();
        UnlockNextLayer(node);
    }

    // ---------- D 类：路径选择房间（分叉路口，语义提示） ----------
    private static readonly string[] PathHints = new[]
    {
        "似乎有战斗声", "空气很潮湿", "有焦糊味", "传来低语", "有光透出", "一片寂静"
    };

    private void ShowPathChoiceUI(MapRoomNode node)
    {
        var edges = node.GetEdges();
        if (edges.Count == 0)
        {
            FinishRoomAndUnlockNext(node);
            return;
        }

        var hints = new List<string>();
        var used = new HashSet<int>();
        for (int i = 0; i < edges.Count; i++)
        {
            int idx = Random.Range(0, PathHints.Length);
            while (used.Count < PathHints.Length && used.Contains(idx)) idx = (idx + 1) % PathHints.Length;
            used.Add(idx);
            hints.Add(PathHints[idx]);
        }

        _currentEnteredNode = node;
        _currentPathHints.Clear();
        _currentPathHints.AddRange(hints);

        RiftOfFogRoomUI.ShowPathChoice("路径选择", hints, (chosenIndex) =>
        {
            RiftOfFogRoomUI.HideIfVisible();
            _currentEnteredNode = null;
            _currentPathHints.Clear();
            LockSameColumnOthers(node);
            // 只把节点标为完成并停止闪烁，不调用 SetCompleted()，否则会把这节点的所有边都标成已走，视觉上“两条路都亮”但只有一条可点
            node.state = MapRoomNode.RoomState.Completed;
            node.StopFlashing();
            for (int i = 0; i < edges.Count; i++)
            {
                var edge = edges[i];
                if (i == chosenIndex)
                {
                    edge.MarkAsTaken();
                    if (edge.dstY < mapNodes.Count && edge.dstX < mapNodes[edge.dstY].Count)
                    {
                        MapRoomNode nextNode = mapNodes[edge.dstY][edge.dstX];
                        if (nextNode.IsActive() && nextNode.state == MapRoomNode.RoomState.Locked)
                        {
                            nextNode.state = MapRoomNode.RoomState.Available;
                            nextNode.StartFlashing();
                        }
                    }
                }
            }
        });
    }

    void Update()
    {
        if (mapNodes != null)
        {
            foreach (var row in mapNodes)
            {
                foreach (var node in row)
                {
                    // 只有激活的节点才需要更新动画
                    if (node.IsActive())
                        node.Update();
                }
            }
        }

        if (Input.GetMouseButtonDown(0))
        {
            Camera cam = (_mapCanvas != null && _mapCanvas.renderMode != RenderMode.ScreenSpaceOverlay) ? _mapCanvas.worldCamera : null;
            if (_mapCanvasParentRect != null && RectTransformUtility.RectangleContainsScreenPoint(_mapCanvasParentRect, Input.mousePosition, cam))
            {
                lastMousePos = Input.mousePosition;
                dragging = true;
            }
        }
        if (Input.GetMouseButtonUp(0))
        {
            dragging = false;
        }

        if (dragging && _mapContentRect != null && _mapCanvasParentRect != null)
        {
            Vector3 delta = Input.mousePosition - lastMousePos;
            Vector2 currentPos = _mapContentRect.anchoredPosition;

            currentPos.x += delta.x;
            currentPos.y += delta.y;

            float contentW = _mapContentRect.rect.width;
            float contentH = 1800;
            float parentW = _mapCanvasParentRect.rect.width;
            float parentH = _mapCanvasParentRect.rect.height;

            float leftLimit = -(contentW - parentW) / 2f;
            float rightLimit = (contentW - parentW) / 2f;
            float bottomLimit = -(contentH - parentH) / 2f;
            float topLimit = (contentH - parentH) / 2f;

            currentPos.x = Mathf.Clamp(currentPos.x, leftLimit, rightLimit);
            currentPos.y = Mathf.Clamp(currentPos.y, bottomLimit, topLimit);

            _mapContentRect.anchoredPosition = currentPos;
            lastMousePos = Input.mousePosition;
        }
    }


    public GlobalMapSaveData GetMapSaveData()
    {
        if (mapNodes == null || mapNodes.Count == 0) return null;
        GlobalMapSaveData data = new GlobalMapSaveData();
        data.width = this.mapWidth;
        data.height = this.mapHeight;
        foreach (var row in mapNodes)
        {
            foreach (var node in row)
            {
                data.nodes.Add(new NodeSaveData { x = node.x, y = node.y, state = node.state, roomType = node.roomType });
                foreach (var edge in node.GetEdges())
                {
                    data.edges.Add(new EdgeSaveData { srcX = edge.srcX, srcY = edge.srcY, dstX = edge.dstX, dstY = edge.dstY, taken = edge.taken });
                }
            }
        }
        return data;
    }

    public void LoadMapFromSaveData(GlobalMapSaveData data)
    {
        if (mapContent != null)
        {
            foreach (Transform child in mapContent.transform) Destroy(child.gameObject);
        }
        mapNodes.Clear();
        this.mapWidth = data.width;
        this.mapHeight = data.height;
        for (int y = 0; y < data.height; y++)
        {
            List<MapRoomNode> row = new List<MapRoomNode>();
            for (int x = 0; x < data.width; x++)
            {
                var nodeData = data.nodes.Find(n => n.x == x && n.y == y);
                if (nodeData == null) continue;
                MapRoomNode newNode = new MapRoomNode(x, y, nodeData.roomType, mapContent.GetComponent<RectTransform>());
                newNode.state = nodeData.state;
                row.Add(newNode);
            }
            mapNodes.Add(row);
        }
        foreach (var edgeData in data.edges)
        {
            MapRoomNode src = mapNodes[edgeData.srcY][edgeData.srcX];
            MapRoomNode dst = mapNodes[edgeData.dstY][edgeData.dstX];
            MapEdge newEdge = new MapEdge(edgeData.srcX, edgeData.srcY, edgeData.dstX, edgeData.dstY);
            if (edgeData.taken) newEdge.MarkAsTaken();
            src.AddEdge(newEdge);
            dst.AddParent(src);
        }
        RenderMapFromNodes();
    }

    // 【新增】辅助渲染方法
    private void RenderMapFromNodes()
    {
        if (mapNodes == null) return;
        foreach (var row in mapNodes)
        {
            foreach (var node in row)
            {
                bool isValid = false;

                if (node.x == 0)
                {
                    isValid = node.GetEdges().Count > 0;
                }
                else
                {
                    isValid = node.GetParents().Count > 0;
                }

                node.SetActive(isValid);
                if (node.state == MapRoomNode.RoomState.Available)
                {
                    node.StartFlashing();
                }
            }
        }
        RenderLines();
    }
}