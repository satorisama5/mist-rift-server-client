"""
FastAPI HTTP 接口，供 Go 服务端调用向量存取。
监听 localhost:8001
"""
import os
from dotenv import load_dotenv
# 当前目录 + 上级目录（go-server 根），便于在 memory-rag 下运行时读到根目录 .env
load_dotenv()
load_dotenv(os.path.join(os.path.dirname(__file__), "..", ".env"))
from contextlib import asynccontextmanager

from fastapi import FastAPI, HTTPException
from pydantic import BaseModel

from vectordb import (
    save_decision as vdb_save_decision,
    search_similar as vdb_search_similar,
    update_player_style as vdb_update_player_style,
    get_player_style as vdb_get_player_style,
)


class SaveDecisionBody(BaseModel):
    session_id: str
    scene: str
    action: str
    result: str
    room_type: str


class SearchSimilarBody(BaseModel):
    scene: str
    top_k: int = 5


class SearchSimilarResponse(BaseModel):
    decisions: list[str]


class GetPlayerStyleBody(BaseModel):
    session_id: str


class GetPlayerStyleResponse(BaseModel):
    style: str


@asynccontextmanager
async def lifespan(app: FastAPI):
    key = (os.environ.get("DOUBAO_API_KEY") or "").strip()
    if not key:
        print("Warning: DOUBAO_API_KEY not set, embedding/save_decision will fail. Set it in go-server/.env.")
    else:
        print("[memory-rag] DOUBAO_API_KEY loaded (chat + embedding use Ark/豆包官方).")
    yield


app = FastAPI(lifespan=lifespan)


@app.post("/save_decision")
async def save_decision_endpoint(body: SaveDecisionBody):
    try:
        vdb_save_decision(
            session_id=body.session_id,
            scene=body.scene,
            action=body.action,
            result=body.result,
            room_type=body.room_type,
        )
        try:
            vdb_update_player_style(
                session_id=body.session_id,
                action=body.action,
                result=body.result,
                room_type=body.room_type,
            )
        except Exception as ex:
            print(f"[memory-rag] update_player_style err: {ex}")
        print(f"[memory-rag] save_decision ok session={body.session_id[:8]} room={body.room_type} action={body.action}")
        return {"ok": True}
    except Exception as e:
        print(f"[memory-rag] save_decision err: {e}")
        raise HTTPException(status_code=500, detail=str(e))


@app.post("/search_similar")
async def search_similar_endpoint(body: SearchSimilarBody) -> SearchSimilarResponse:
    try:
        decisions = vdb_search_similar(scene=body.scene, top_k=body.top_k)
        print(f"[memory-rag] search_similar ok top_k={body.top_k} returned={len(decisions)}")
        return SearchSimilarResponse(decisions=decisions)
    except Exception as e:
        print(f"[memory-rag] search_similar err: {e}")
        raise HTTPException(status_code=500, detail=str(e))


@app.post("/get_player_style")
async def get_player_style_endpoint(body: GetPlayerStyleBody) -> GetPlayerStyleResponse:
    try:
        style = vdb_get_player_style(session_id=body.session_id)
        return GetPlayerStyleResponse(style=style or "")
    except Exception as e:
        print(f"[memory-rag] get_player_style err: {e}")
        return GetPlayerStyleResponse(style="")


if __name__ == "__main__":
    import uvicorn
    uvicorn.run(app, host="127.0.0.1", port=8001)
