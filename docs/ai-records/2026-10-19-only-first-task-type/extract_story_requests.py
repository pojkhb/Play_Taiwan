"""把 logs/ai 裡劇本任務生成的 request 與 AI 最終結果配對，輸出題型對照表與原始紀錄。

用法：python -I extract_story_requests.py <logs/ai 路徑> <輸出資料夾>
"""
import glob
import json
import os
import shutil
import sys

LOG_ROOT, OUT_DIR = sys.argv[1], sys.argv[2]
RAW_DIR = os.path.join(OUT_DIR, "raw")
os.makedirs(RAW_DIR, exist_ok=True)


def load(path):
    with open(path, encoding="utf-8-sig") as f:
        return json.load(f)


def sent_types(story_req):
    return [(p.get("p_name"), [t.get("type_id") for t in p.get("type_list") or []])
            for p in story_req.get("places") or []]


def got_types(story_result):
    return [(n.get("place_id"), [t.get("task_type") for t in n.get("tasks") or []])
            for n in (story_result or {}).get("nodes") or []]


# 先把所有 GET /api/jobs/{id} 依 task_id 分組（只留最後一筆，就是最終狀態）
job_files = {}
for path in sorted(glob.glob(os.path.join(LOG_ROOT, "*", "*_GET_api-jobs-*.json"))):
    task_id = os.path.basename(path).rsplit("api-jobs-", 1)[1][:-5]
    job_files[task_id] = path

records = []   # (time, endpoint, party_size, story_req, story_result, request_file, result_file)

for path in sorted(glob.glob(os.path.join(LOG_ROOT, "*", "*_POST_api-stories-tasks*.json"))):
    log = load(path)
    req = (log.get("request") or {}).get("body") or {}
    resp = (log.get("response") or {}).get("body") or {}

    if path.endswith("batch.json"):
        # 舊的同步批次 API：一次送多份，回應直接帶結果
        results = resp.get("stories") or [] if isinstance(resp, dict) else []
        for i, story_req in enumerate(req.get("stories") or []):
            result = results[i] if i < len(results) else None
            records.append((log["time"], "batch", req.get("party_size"), story_req, result, path, None))
    else:
        # 排隊 API：POST 拿 task_id，結果在最後一次 GET /api/jobs/{task_id}
        task_id = resp.get("task_id") if isinstance(resp, dict) else None
        result_file = job_files.get(task_id)
        result = None
        if result_file:
            job = load(result_file)["response"]["body"]
            result = job.get("result") if isinstance(job, dict) and job.get("status") == "done" else None
        records.append((log["time"], "queued", req.get("party_size"), req, result, path, result_file))

lines = []
summary = {"stories": 0, "with_result": 0, "nodes": 0, "nodes_multi_sent": 0, "nodes_multi_got": 0, "nodes_first_only": 0}

for time, endpoint, party, story_req, result, req_file, res_file in records:
    sent = sent_types(story_req)
    got = got_types(result)
    summary["stories"] += 1
    lines.append(f"## {time}（{endpoint}，{party} 人）")
    lines.append(f"- request 紀錄：logs/ai/{os.path.relpath(req_file, LOG_ROOT).replace(os.sep, '/')}")
    if res_file:
        lines.append(f"- 結果紀錄：logs/ai/{os.path.relpath(res_file, LOG_ROOT).replace(os.sep, '/')}")
    if result is None:
        lines.append("- AI 沒有成功的結果（失敗、遺失或還沒做完）")
        lines.append("")
        continue

    summary["with_result"] += 1
    lines.append("")
    lines.append("| 站 | 景點 | 後端送出的 type_list | AI 回傳的 task_type |")
    lines.append("|---|---|---|---|")
    for i, (name, types) in enumerate(sent):
        returned = got[i][1] if i < len(got) else []
        summary["nodes"] += 1
        if len(types) > 1:
            summary["nodes_multi_sent"] += 1
        if len(returned) > 1:
            summary["nodes_multi_got"] += 1
        if types and returned == types[:1]:
            summary["nodes_first_only"] += 1
        lines.append(f"| {i + 1} | {name} | {types} | {returned} |")
    lines.append("")

    # 原始檔一起複製一份
    for f in (req_file, res_file):
        if f:
            dest = os.path.join(RAW_DIR, os.path.basename(os.path.dirname(f)) + "_" + os.path.basename(f))
            if not os.path.exists(dest):
                shutil.copy2(f, dest)

head = [
    "# 劇本任務生成：後端送出的題型 vs AI 回傳的題型",
    "",
    "從 logs/ai（AiTrafficLogHandler 自動記錄）整理，每份劇本一節。原始 request／結果 JSON 複製在 raw/。",
    "",
    f"- 劇本數：{summary['stories']}（有成功結果的 {summary['with_result']}）",
    f"- 站數：{summary['nodes']}",
    f"- 後端送了 2 種以上題型的站：{summary['nodes_multi_sent']}",
    f"- AI 回傳 2 題以上的站：{summary['nodes_multi_got']}",
    f"- AI 只回傳 type_list 第一個題型的站：{summary['nodes_first_only']}",
    "",
]

with open(os.path.join(OUT_DIR, "README.md"), "w", encoding="utf-8") as f:
    f.write("\n".join(head + lines))

print(json.dumps(summary, ensure_ascii=False))
