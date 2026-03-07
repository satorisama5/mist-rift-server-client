import os
import sys

# 确保能读到 .env
from dotenv import load_dotenv
load_dotenv("../.env")  # 读取上级目录的 .env
load_dotenv()           # 读取同级目录的 .env (如果有)

# 导入你的业务逻辑
try:
    from vectordb import search_similar, _embed, save_decision
except ImportError as e:
    print(f"❌ 导入失败: {e}")
    print("请确保你在 go-server/memory-rag/ 目录下运行此脚本")
    sys.exit(1)

def test_embedding():
    print("\n--- 1. 测试 Embedding (豆包) ---")
    text = "测试文本"
    try:
        vector = _embed(text)
        print(f"✅ Embedding 成功! 向量维度: {len(vector)}")
        if len(vector) != 2048:
            print("⚠️ 警告: 维度不是 2048，请检查 Qdrant 集合配置是否匹配！")
        return True
    except Exception as e:
        print(f"❌ Embedding 失败: {e}")
        print("💡 提示: 请检查 VPN 是否已关闭，以及 DOUBAO_API_KEY 是否正确")
        return False

def test_save_and_search():
    print("\n--- 2. 测试 写入 & 检索 (Qdrant) ---")
    
    # 1. 先写入一条假数据，确保库里有东西
    session_id = "test_session_001"
    scene = "在一个黑暗的洞穴里，发现了一个发光的宝箱"
    action = "careful_check"
    result = "发现宝箱里有解毒剂"
    room = "Resource"
    
    print(f"正在写入测试数据: {action}...")
    try:
        save_decision(session_id, scene, action, result, room)
        print("✅ 写入成功")
    except Exception as e:
        print(f"❌ 写入失败: {e}")
        return

    # 2. 尝试检索
    print(f"正在检索相似场景: '{scene}'...")
    try:
        results = search_similar(scene, top_k=3)
        print(f"✅ 检索成功! 找到了 {len(results)} 条结果:")
        for i, res in enumerate(results):
            print(f"  [{i+1}] {res}")
            
        if len(results) == 0:
            print("⚠️ 检索返回为空。可能原因：")
            print("  1. 写入的数据还没持久化（Qdrant通常是实时的，这很少见）")
            print("  2. search_similar 里的查询逻辑有 Bug")
            print("  3. 集合名称对不上")
            
    except Exception as e:
        print(f"❌ 检索报错: {e}")
        print("💡 提示: 这通常是 vectordb.py 里 search/query_points 写法的问题")

if __name__ == "__main__":
    # 检查环境变量
    if not os.environ.get("DOUBAO_API_KEY"):
        print("❌ 错误: 未找到 DOUBAO_API_KEY，请检查 .env 文件")
        sys.exit(1)
        
    if test_embedding():
        test_save_and_search()