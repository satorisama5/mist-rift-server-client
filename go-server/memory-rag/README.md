# memory-rag

Qdrant 向量存取 + FastAPI HTTP 接口，供 Go 网关调用。向量化使用**豆包 Ark multimodal_embeddings**（2048 维），需配置 `DOUBAO_API_KEY`。

## 已实现接口（与 rag_client.go 对应）

- **POST /save_decision**  
  Body: `session_id`, `scene`, `action`, `result`, `room_type`  
  将决策向量化后写入 Qdrant 集合 `rift_decisions`。

- **POST /search_similar**  
  Body: `scene`, `top_k`（默认 5）  
  返回: `{"decisions": ["在Combat房间选了stealth_pass，结果：成功绕过", ...]}`

- **POST /get_player_style**  
  Body: `session_id`  
  返回该 session 的玩家风格描述。

## 运行

```bash
pip install -r requirements.txt
# 在 go-server 根目录或本目录设置 .env：DOUBAO_API_KEY=xxx
# 确保 Qdrant 在 localhost:6333
python main.py
# 或: uvicorn main:app --host 127.0.0.1 --port 8001
```

监听 **localhost:8001**。Go 端 `internal/memory/rag_client.go` 会请求该地址。

## 故障排除

### 1. 维度冲突（Vector Dimension Mismatch）

**现象**：`save_decision` 报错、写入失败，或报错里出现 `vector_len=2048` / `Wrong vector dimension`。

**原因**：Qdrant 的集合一旦创建，向量维度就固定。若之前用 Gemini（768 维）建过 `rift_decisions`，现在代码改为豆包（2048 维），不删旧集合会导致维度冲突。

**解决**：删除旧集合，让程序在下次写入时按当前 `VECTOR_SIZE` 重新创建。

```bash
# 在 memory-rag 目录下
python reset_collections.py
```

或手动用 curl：

```bash
curl -X DELETE http://localhost:6333/collections/rift_decisions
curl -X DELETE http://localhost:6333/collections/player_styles
```

### 2. 代理导致连不上 localhost:6333

**现象**：本机已启动 Qdrant，但 Python 请求 `localhost:6333` 超时或连接被拒。

**原因**：环境里设置了 `HTTPS_PROXY`/`HTTP_PROXY`，代理未排除本地，请求被发到代理服务器。

**解决**：运行 main.py / uvicorn 的终端里排除本地地址：

- **PowerShell**：`$env:NO_PROXY="localhost,127.0.0.1"`
- **CMD**：`set NO_PROXY=localhost,127.0.0.1`
- 再执行 `python main.py` 或 `uvicorn main:app --host 127.0.0.1 --port 8001`
