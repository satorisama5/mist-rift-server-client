import os
from dotenv import load_dotenv
load_dotenv("../.env")

from volcenginesdkarkruntime import Ark

api_key = os.environ.get("DOUBAO_API_KEY")
print("key:", api_key[:8] if api_key else "None")

client = Ark(api_key=api_key)
resp = client.multimodal_embeddings.create(
    model="ep-20260305105321-5lb48",
    input=[
        {"type": "text", "text": "测试文本"}
    ]
)
print(resp.data.embedding)
print("维度:", len(resp.data.embedding))