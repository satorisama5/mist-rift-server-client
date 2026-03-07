from qdrant_client import QdrantClient

client = QdrantClient(
    host="0.0.0.0",
    port=6333,
    prefer_grpc=False,
    check_compatibility=False
)
print(client.get_collections())