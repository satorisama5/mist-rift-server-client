"""
删除 Qdrant 中的旧集合，解决「维度冲突」导致的写入失败。

当 embedding 模型从 Gemini(768 维) 换为豆包(2048 维) 时，已有集合的维度不可改，
必须删掉旧集合，让 vectordb 在下次写入时按 VECTOR_SIZE 重新创建。

用法（在 memory-rag 目录下）:
  python reset_collections.py

或用 curl:
  curl -X DELETE http://localhost:6333/collections/rift_decisions
  curl -X DELETE http://localhost:6333/collections/player_styles
"""
from qdrant_client import QdrantClient

# 与 vectordb.py 保持一致，避免导入 vectordb（不需要 DOUBAO_API_KEY）
COLLECTION_NAME = "rift_decisions"
PLAYER_STYLES_COLLECTION = "player_styles"
QDRANT_HOST = "localhost"
QDRANT_PORT = 6333

def main():
    client = QdrantClient(host=QDRANT_HOST, port=QDRANT_PORT)
    for name in [COLLECTION_NAME, PLAYER_STYLES_COLLECTION]:
        try:
            client.delete_collection(name)
            print(f"已删除集合: {name}")
        except Exception as e:
            # 集合不存在时可能抛错，视客户端版本而定
            print(f"删除 {name} 时: {e}")
    print("完成。下次 save_decision / search_similar 时会自动创建新集合。")

if __name__ == "__main__":
    main()
