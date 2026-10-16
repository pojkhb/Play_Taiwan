# 檔案路徑：tools\embedding_service\embedding_server.py
# 商家描述的 embedding 小服務：後端（appsettings 的 EmbeddingService:BaseUrl）呼叫這裡把文字轉成向量，
# 再寫進 Neo4j 景點節點的 description_embedding。
# 模型與寫法跟資料清洗（D:\play_taiwan_data_cleaning 的 process_*.py）相同：
# intfloat/multilingual-e5-large、1024 維、直接編碼描述原文（不加 query: / passage: 前綴）、正規化成長度 1，
# 算出來的向量才能跟政府資料景點的向量互相比較。
#
#   GET  /health  → {"status": "ok", "model": "...", "dimension": 1024}
#   POST /embed   {"texts": ["..."]} → {"model": "...", "dimension": 1024, "embeddings": [[...], ...]}
import argparse
import json
import logging
import threading
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer

import torch
from sentence_transformers import SentenceTransformer

MODEL_NAME = "intfloat/multilingual-e5-large"
MAX_TEXTS = 64   # 一次最多幾段文字，避免一個請求吃光記憶體

logger = logging.getLogger("embedding_server")


class EmbeddingHandler(BaseHTTPRequestHandler):
    model: SentenceTransformer = None
    lock = threading.Lock()   # 同一個模型不要同時跑多個 encode

    def do_GET(self):
        if self.path != "/health":
            return self._send(404, {"error": f"沒有這個路徑：{self.path}"})
        self._send(200, {"status": "ok", "model": MODEL_NAME, "dimension": self.model.get_sentence_embedding_dimension()})

    def do_POST(self):
        if self.path != "/embed":
            return self._send(404, {"error": f"沒有這個路徑：{self.path}"})

        try:
            body = json.loads(self._read_body() or b"{}")
        except (ValueError, json.JSONDecodeError):
            return self._send(400, {"error": "請送 JSON：{\"texts\": [\"...\"]}"})

        texts = body.get("texts") if isinstance(body, dict) else None
        if not isinstance(texts, list) or not texts or not all(isinstance(t, str) and t.strip() for t in texts):
            return self._send(400, {"error": "texts 要是非空白字串的陣列"})
        if len(texts) > MAX_TEXTS:
            return self._send(400, {"error": f"texts 一次最多 {MAX_TEXTS} 筆"})

        with self.lock:
            vectors = self.model.encode([t.strip() for t in texts], normalize_embeddings=True, show_progress_bar=False)

        self._send(200, {
            "model": MODEL_NAME,
            "dimension": int(vectors.shape[1]),
            "embeddings": [v.tolist() for v in vectors],
        })

    def _read_body(self):
        # .NET 的 PostAsJsonAsync 會用 chunked 傳送、不帶 Content-Length，兩種都要能讀
        if "chunked" not in self.headers.get("Transfer-Encoding", "").lower():
            return self.rfile.read(int(self.headers.get("Content-Length", 0)))

        chunks = []
        while True:
            size = int(self.rfile.readline().split(b";")[0].strip(), 16)
            if size == 0:
                while self.rfile.readline() not in (b"\r\n", b"\n", b""):   # 最後一段後面可能有 trailer，讀到空行為止
                    pass
                return b"".join(chunks)
            chunks.append(self.rfile.read(size))
            self.rfile.readline()   # 每一段後面的 \r\n

    def _send(self, status, payload):
        data = json.dumps(payload, ensure_ascii=False).encode("utf-8")
        self.send_response(status)
        self.send_header("Content-Type", "application/json; charset=utf-8")
        self.send_header("Content-Length", str(len(data)))
        self.end_headers()
        self.wfile.write(data)

    def log_message(self, format, *args):
        logger.info("%s - %s", self.address_string(), format % args)


def main():
    parser = argparse.ArgumentParser(description="商家描述 embedding 服務（multilingual-e5-large）")
    parser.add_argument("--host", default="127.0.0.1")
    parser.add_argument("--port", type=int, default=8003)
    parser.add_argument("--device", default="cuda" if torch.cuda.is_available() else "cpu")
    args = parser.parse_args()

    logging.basicConfig(level=logging.INFO, format="%(asctime)s %(levelname)s %(message)s")
    logger.info("載入模型 %s（device=%s），第一次執行會先下載約 2.2GB", MODEL_NAME, args.device)
    EmbeddingHandler.model = SentenceTransformer(MODEL_NAME, device=args.device)

    server = ThreadingHTTPServer((args.host, args.port), EmbeddingHandler)
    logger.info("embedding 服務啟動：http://%s:%d", args.host, args.port)
    server.serve_forever()


if __name__ == "__main__":
    main()
