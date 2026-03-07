using System;
using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

/// <summary>
/// 与 Go 网关 WebSocket 通信：连接 ws://localhost:8080/ws，
/// 订阅 OnPlayerMessageSubmitted/OnRoomActionChosen 发送 UnityRequest，
/// 收到 ServerResponse 后触发 OnServerResponseReceived 回调。
/// </summary>
public class WebSocketClient : MonoBehaviour
{
    public const string WsUrl = "ws://127.0.0.1:8080/ws";

    public static WebSocketClient Instance { get; private set; }

    /// <summary> 收到服务端一条响应时触发（主线程）。 </summary>
    public static event Action<ServerResponseData> OnServerResponseReceived;

    /// <summary> 连接状态变化时触发（主线程），参数：(是否已连接, 说明文案)。可用于 UI 显示「已连接网关」/「未连接：xxx」。 </summary>
    public static event Action<bool, string> OnConnectionStateChanged;

    /// <summary> 当前连接是否已建立。 </summary>
    public bool IsConnected => _client?.State == WebSocketState.Open;

    /// <summary> 上次 LLM 建议返回的 action，用于「听从建议」时再发一条请求。 </summary>
    public string PendingSuggestedAction { get; set; }

    private ClientWebSocket _client;
    private CancellationTokenSource _cts;
    private readonly ConcurrentQueue<string> _incoming = new ConcurrentQueue<string>();
    private readonly ConcurrentQueue<string> _outgoing = new ConcurrentQueue<string>();
    private readonly ConcurrentQueue<(bool connected, string message)> _connectionStateQueue = new ConcurrentQueue<(bool, string)>();
    private string _sessionId;
    private bool _subscribed;

    private void Awake()
    {
        Debug.Log("[WebSocketClient] Awake — 脚本已加载，若之后没有「开始连接」请检查本物体是否被禁用");
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        DontDestroyOnLoad(gameObject);
        _sessionId = Guid.NewGuid().ToString("N");
    }

    private void OnEnable()
    {
        if (_subscribed) return;
        RiftOfFogMessageBridge.OnPlayerMessageSubmitted += OnPlayerMessageSubmitted;
        RiftOfFogMessageBridge.OnRoomActionChosen += OnRoomActionChosen;
        _subscribed = true;
        Debug.Log("[WebSocketClient] 开始连接 " + WsUrl + "，请确保网关已启动 (go run ./cmd/server)");
        Connect();
    }

    private void OnDisable()
    {
        if (!_subscribed) return;
        RiftOfFogMessageBridge.OnPlayerMessageSubmitted -= OnPlayerMessageSubmitted;
        RiftOfFogMessageBridge.OnRoomActionChosen -= OnRoomActionChosen;
        _subscribed = false;
        Disconnect();
    }

    private void OnDestroy()
    {
        Instance = null;
        Disconnect();
    }

    private void OnPlayerMessageSubmitted(string message)
    {
        EnqueueRequest(message, null);
    }

    private void OnRoomActionChosen(MapManager.RoomType _, string actionKey)
    {
        EnqueueRequest(null, actionKey);
    }

    /// <summary> 「听从建议」时调用：用 PendingSuggestedAction 再发一条请求。 </summary>
    public void SendFollowSuggestion()
    {
        if (string.IsNullOrEmpty(PendingSuggestedAction)) return;
        EnqueueRequest(null, PendingSuggestedAction);
        PendingSuggestedAction = null;
    }

    private void EnqueueRequest(string message, string actionKey)
    {
        string json = BuildRequest(message, actionKey);
        if (!string.IsNullOrEmpty(json))
            _outgoing.Enqueue(json);
    }

    private static string BuildRequest(string message, string actionKey)
    {
        string ctx = RiftOfFogSceneContext.GetCurrentContext();
        if (string.IsNullOrEmpty(ctx)) ctx = "{}";
        ctx = ctx.TrimEnd();
        if (ctx.EndsWith("}")) ctx = ctx.Substring(0, ctx.Length - 1);
        string type = !string.IsNullOrEmpty(message) ? "command" : "decision_record";
        string sessionId = Instance != null ? Instance._sessionId : Guid.NewGuid().ToString("N");
        ctx += ",\"session_id\":\"" + Escape(sessionId) + "\",\"type\":\"" + type + "\"";
        if (!string.IsNullOrEmpty(message))
            ctx += ",\"message\":\"" + Escape(message) + "\"";
        if (!string.IsNullOrEmpty(actionKey))
            ctx += ",\"action_key\":\"" + Escape(actionKey) + "\"";
        ctx += "}";
        return ctx;
    }

    private static string Escape(string s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        return s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n");
    }

    private void Connect()
    {
        if (_client != null) return;
        _client = new ClientWebSocket();
        _cts = new CancellationTokenSource();
        Task.Run(() => ConnectAndRunAsync(_cts.Token));
        Debug.Log("[WebSocketClient] 连接请求已发出，结果将在数秒内显示（成功=白字，失败=红字）");
    }

    private async Task ConnectAndRunAsync(CancellationToken ct)
    {
        bool connectionFailed = false;
        try
        {
            await _client.ConnectAsync(new Uri(WsUrl), ct);
            _connectionStateQueue.Enqueue((true, "已连接 " + WsUrl));
            // 发送与接收必须并发：否则会卡在 ReceiveAsync，点房间/发消息后请求永远发不出去（死锁）。
            var sendTask = SendLoopAsync(ct);
            await ReceiveLoopAsync(ct);
            _cts?.Cancel();
            try { await sendTask; } catch { }
        }
        catch (Exception ex)
        {
            connectionFailed = true;
            string msg = ex.Message ?? "连接失败";
            _connectionStateQueue.Enqueue((false, msg));
        }
        finally
        {
            try { _client?.Dispose(); } catch { }
            _client = null;
            if (!ct.IsCancellationRequested)
            {
                if (!connectionFailed)
                    _connectionStateQueue.Enqueue((false, "连接已断开，3秒后重试..."));
                await Task.Delay(3000, ct);
                if (!ct.IsCancellationRequested)
                {
                    _client = new ClientWebSocket();
                    _cts = new CancellationTokenSource();
                    await ConnectAndRunAsync(_cts.Token);
                }
            }
        }
    }

    /// <summary> 只负责发送 _outgoing，与 ReceiveLoopAsync 并发，避免死锁。用局部变量 c 避免 Disconnect 时竞态。 </summary>
    private async Task SendLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            var c = _client;
            if (c == null || c.State != WebSocketState.Open) break;
            try
            {
                if (_outgoing.TryDequeue(out string toSend))
                {
                    var bytes = Encoding.UTF8.GetBytes(toSend);
                    await c.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, ct);
                }
                else
                    await Task.Delay(50, ct);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception) { break; }
        }
    }

    private async Task ReceiveLoopAsync(CancellationToken ct)
    {
        var buf = new byte[4096];
        while (!ct.IsCancellationRequested)
        {
            var c = _client;
            if (c == null || c.State != WebSocketState.Open) break;
            try
            {
                var seg = new ArraySegment<byte>(buf);
                var result = await c.ReceiveAsync(seg, ct);
                if (result.MessageType == WebSocketMessageType.Close) break;
                if (result.MessageType == WebSocketMessageType.Text && result.Count > 0)
                {
                    string text = Encoding.UTF8.GetString(buf, 0, result.Count);
                    _incoming.Enqueue(text);
                }
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                Debug.LogWarning("[WebSocketClient] Receive: " + ex.Message);
                break;
            }
        }
    }

    private void Disconnect()
    {
        _cts?.Cancel();
        try { _client?.Abort(); } catch { }
        _client = null;
    }

    private void Update()
    {
        while (_connectionStateQueue.TryDequeue(out var state))
        {
            if (state.connected)
            {
                Debug.Log("[WebSocketClient] " + state.message + " — 房间内将使用服务端 AI 建议");
                OnConnectionStateChanged?.Invoke(true, state.message);
            }
            else
            {
                Debug.LogError("[WebSocketClient] 未连接: " + state.message + " — 房间内将使用本地模拟建议。请确认网关已启动且地址为 " + WsUrl);
                OnConnectionStateChanged?.Invoke(false, state.message);
            }
        }

        while (_incoming.TryDequeue(out string raw))
        {
            try
            {
                var data = JsonUtility.FromJson<ServerResponseData>(raw);
                if (data != null)
                    OnServerResponseReceived?.Invoke(data);
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[WebSocketClient] Parse response: " + ex.Message);
            }
        }
    }

    /// <summary> 服务端返回的 JSON 对应结构（下划线命名与 Go 一致）。 </summary>
    [Serializable]
    public class ServerResponseData
    {
        public string session_id;
        public string type;
        public string service;
        public string action;
        public string result;
        public string ai_reasoning;
        public string suggestion;
        public StateChangesData state_changes;
    }

    [Serializable]
    public class StateChangesData
    {
        public int hp_delta;
        public int ap_delta;
        public string new_status;
        public string clear_status;
    }
}
