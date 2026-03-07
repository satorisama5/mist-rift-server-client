"""
Qdrant 向量存取，向量化使用豆包官方 Ark multimodal_embeddings（与 test.py 一致，DOUBAO_API_KEY）。
玩家风格统计存 collection player_styles，key 为 session_id。
"""
import os
try:
    from dotenv import load_dotenv
    load_dotenv(os.path.join(os.path.dirname(__file__), "..", ".env"))
    load_dotenv()
except ImportError:
    pass
import time
import uuid
from typing import Optional

from qdrant_client import QdrantClient
from qdrant_client.models import Distance, VectorParams, PointStruct
from volcenginesdkarkruntime import Ark

COLLECTION_NAME = "rift_decisions"
PLAYER_STYLES_COLLECTION = "player_styles"
VECTOR_SIZE = 2048
# player_styles 用零向量即可，只存 payload
STYLE_VECTOR_SIZE = 8
QDRANT_HOST = "localhost"
QDRANT_PORT = 6333
DOUBAO_EMBED_MODEL = "ep-20260305105321-5lb48"

_ark_client = None
_qdrant = None


def _get_ark_client():
    global _ark_client
    if _ark_client is None:
        api_key = (os.environ.get("DOUBAO_API_KEY") or "").strip()
        if not api_key:
            raise ValueError("DOUBAO_API_KEY not set (见 go-server/.env)")
        _ark_client = Ark(api_key=api_key)
    return _ark_client


def _get_qdrant():
    global _qdrant
    if _qdrant is None:
        _qdrant = QdrantClient(host=QDRANT_HOST, port=QDRANT_PORT)
    return _qdrant


def _get_existing_vector_size(client, collection_name: str) -> Optional[int]:
    """获取已存在集合的向量维度，若无法获取则返回 None。"""
    try:
        info = client.get_collection(collection_name)
        vparams = info.config.params.vectors
        if hasattr(vparams, "size"):
            return int(vparams.size)
        if hasattr(vparams, "values"):
            for p in vparams.values():
                if hasattr(p, "size"):
                    return int(p.size)
                break
        return None
    except Exception:
        return None


def _ensure_collection():
    client = _get_qdrant()
    collections = client.get_collections().collections
    names = [c.name for c in collections]
    if COLLECTION_NAME not in names:
        client.create_collection(
            collection_name=COLLECTION_NAME,
            vectors_config=VectorParams(size=VECTOR_SIZE, distance=Distance.COSINE),
        )
        return client
    # 已存在集合：检查维度是否与当前 VECTOR_SIZE 一致，避免 768 vs 2048 等维度冲突
    existing = _get_existing_vector_size(client, COLLECTION_NAME)
    if existing is not None and existing != VECTOR_SIZE:
        raise ValueError(
            f"集合 {COLLECTION_NAME} 当前维度为 {existing}，与当前配置 VECTOR_SIZE={VECTOR_SIZE} 不一致。"
            "请删除旧集合后重试：在 memory-rag 目录运行 python reset_collections.py ，或执行 "
            "curl -X DELETE http://localhost:6333/collections/rift_decisions"
        )
    return client


def _embed(text: str) -> list[float]:
    """与 test.py 一致：使用 Ark SDK multimodal_embeddings.create，维度 2048。"""
    client = _get_ark_client()
    resp = client.multimodal_embeddings.create(
        model=DOUBAO_EMBED_MODEL,
        input=[{"type": "text", "text": text}],
    )
    return resp.data.embedding


def save_decision(session_id: str, scene: str, action: str, result: str, room_type: str) -> None:
    """将一次决策写入 Qdrant。scene+action+result 拼成文本后向量化，collection 不存在时自动创建。"""
    text = f"{scene}\n{action}\n{result}"
    vector = _embed(text)
    client = _ensure_collection()
    payload = {
        "session_id": session_id,
        "scene": scene,
        "action": action,
        "result": result,
        "room_type": room_type,
        "timestamp": time.time(),
    }
    point = PointStruct(
        id=str(uuid.uuid4()),
        vector=vector,
        payload=payload,
    )
    try:
        client.upsert(collection_name=COLLECTION_NAME, points=[point])
    except Exception as e:
        msg = str(e)
        hint = ""
        if "dimension" in msg.lower() or "wrong vector" in msg.lower() or len(vector) != VECTOR_SIZE:
            hint = (
                " 若为维度冲突，请删除旧集合：在 memory-rag 目录运行 python reset_collections.py"
            )
        raise Exception(f"upsert failed: {e}, vector_len={len(vector)}.{hint}")


def search_similar(scene: str, top_k: int = 5) -> list[str]:
    """按 scene 向量检索最相似的 top_k 条决策，返回可读描述列表。"""
    try:
        query_vector = _embed(scene)
    except Exception:
        return []
    client = _ensure_collection()
    # 兼容 qdrant-client 新旧 API：旧版用 .search()，新版用 .query_points() 且返回带 .points
    try:
        hits = client.search(
            collection_name=COLLECTION_NAME,
            query_vector=query_vector,
            limit=top_k,
        )
    except AttributeError:
        res = client.query_points(
            collection_name=COLLECTION_NAME,
            query=query_vector,
            limit=top_k,
        )
        hits = res.points if hasattr(res, "points") else res
    out = []
    for h in hits:
        p = getattr(h, "payload", None) or {}
        room = p.get("room_type", "")
        action = p.get("action", "")
        result = p.get("result", "")
        out.append(f"在{room}房间选了{action}，结果：{result}")
    return out


def _style_point_id(session_id: str):
    return str(uuid.uuid5(uuid.NAMESPACE_DNS, "player_style_" + session_id))


def _ensure_player_styles_collection():
    client = _get_qdrant()
    names = [c.name for c in client.get_collections().collections]
    if PLAYER_STYLES_COLLECTION not in names:
        client.create_collection(
            collection_name=PLAYER_STYLES_COLLECTION,
            vectors_config=VectorParams(size=STYLE_VECTOR_SIZE, distance=Distance.COSINE),
        )
    return client


def update_player_style(session_id: str, action: str, result: str, room_type: str) -> None:
    """按 session 统计决策偏好：各房间类型下各 action 次数、成功率，写入 player_styles。"""
    client = _ensure_player_styles_collection()
    point_id = _style_point_id(session_id)
    # 判定是否“成功”：result 里不含明显失败词则计为成功
    success = 1 if result and "失败" not in result and "受伤" not in result and "失去" not in result else 0
    key_room_action = f"{room_type}:{action}"
    try:
        pts = client.retrieve(collection_name=PLAYER_STYLES_COLLECTION, ids=[point_id])
    except Exception:
        pts = []
    if pts:
        p = pts[0].payload or {}
        room_actions = dict(p.get("room_actions") or {})
        room_actions[key_room_action] = room_actions.get(key_room_action, 0) + 1
        successes = p.get("successes", 0) + success
        total = p.get("total", 0) + 1
    else:
        room_actions = {key_room_action: 1}
        successes = success
        total = 1
    payload = {
        "session_id": session_id,
        "room_actions": room_actions,
        "successes": successes,
        "total": total,
        "last_updated": time.time(),
    }
    vector = [0.0] * STYLE_VECTOR_SIZE
    point = PointStruct(id=point_id, vector=vector, payload=payload)
    client.upsert(collection_name=PLAYER_STYLES_COLLECTION, points=[point])


def get_player_style(session_id: str) -> str:
    """返回该 session 的玩家风格描述，供 LLM 参考。"""
    try:
        client = _ensure_player_styles_collection()
        point_id = _style_point_id(session_id)
        pts = client.retrieve(collection_name=PLAYER_STYLES_COLLECTION, ids=[point_id])
    except Exception:
        return ""
    if not pts:
        return ""
    p = pts[0].payload or {}
    room_actions = p.get("room_actions") or {}
    successes = p.get("successes", 0)
    total = p.get("total", 0)
    success_rate = f"{int(100 * successes / total)}%" if total else "0%"
    # 按房间类型聚合：Combat 里哪种 action 最多等
    by_room = {}
    for k, cnt in room_actions.items():
        if ":" in k:
            room, action = k.split(":", 1)
            by_room.setdefault(room, []).append((action, cnt))
    parts = [f"该玩家总决策{total}次，成功率{success_rate}"]
    for room, pairs in sorted(by_room.items()):
        pairs.sort(key=lambda x: -x[1])
        top = pairs[0]
        pct = int(100 * top[1] / sum(c for _, c in pairs)) if pairs else 0
        parts.append(f"{room}房间偏好{top[0]}({pct}%)")
    return "；".join(parts)
