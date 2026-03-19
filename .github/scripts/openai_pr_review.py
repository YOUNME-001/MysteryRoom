import json
import os
import sys
import urllib.error
import urllib.request

OPENAI_API_KEY = os.getenv("OPENAI_API_KEY", "")
GITHUB_TOKEN = os.getenv("GITHUB_TOKEN", "")
REPO = os.getenv("GITHUB_REPOSITORY", "")
PR_NUMBER = os.getenv("PR_NUMBER", "")

COMMENT_MARKER = "<!-- openai-unity-pr-review -->"
MAX_DIFF_CHARS = 90000


def http_json(url, method="GET", headers=None, data=None, timeout=120):
    req = urllib.request.Request(url, method=method)
    for k, v in (headers or {}).items():
        req.add_header(k, v)

    payload = None
    if data is not None:
        payload = json.dumps(data, ensure_ascii=False).encode("utf-8")
        req.add_header("Content-Type", "application/json")

    try:
        with urllib.request.urlopen(req, data=payload, timeout=timeout) as resp:
            raw = resp.read().decode("utf-8")
            return json.loads(raw) if raw else None
    except urllib.error.HTTPError as e:
        detail = e.read().decode("utf-8", errors="ignore")
        raise RuntimeError(f"{method} {url} failed: {e.code} {detail}")


def http_text(url, method="GET", headers=None, timeout=120):
    req = urllib.request.Request(url, method=method)
    for k, v in (headers or {}).items():
        req.add_header(k, v)

    try:
        with urllib.request.urlopen(req, timeout=timeout) as resp:
            return resp.read().decode("utf-8", errors="ignore")
    except urllib.error.HTTPError as e:
        detail = e.read().decode("utf-8", errors="ignore")
        raise RuntimeError(f"{method} {url} failed: {e.code} {detail}")


def github_json(url, method="GET", data=None):
    return http_json(
        url,
        method=method,
        headers={
            "Authorization": f"Bearer {GITHUB_TOKEN}",
            "Accept": "application/vnd.github+json",
        },
        data=data,
        timeout=60,
    )


def fetch_pr_diff():
    return http_text(
        f"https://api.github.com/repos/{REPO}/pulls/{PR_NUMBER}",
        headers={
            "Authorization": f"Bearer {GITHUB_TOKEN}",
            "Accept": "application/vnd.github.v3.diff",
        },
        timeout=60,
    )


def extract_cs_diff(diff_text):
    blocks = []
    current = []
    include = False

    for line in diff_text.splitlines():
        if line.startswith("diff --git "):
            if current and include:
                blocks.append("\n".join(current))
            current = [line]
            include = False

            parts = line.split(" b/", 1)
            if len(parts) == 2 and parts[1].endswith(".cs"):
                include = True
        else:
            current.append(line)
            if line.startswith("+++ b/") and line[6:].endswith(".cs"):
                include = True

    if current and include:
        blocks.append("\n".join(current))

    return "\n\n".join(blocks)


def extract_output_text(response_json):
    for item in response_json.get("output", []):
        if item.get("type") != "message":
            continue
        for content in item.get("content", []):
            if content.get("type") == "output_text":
                return content.get("text", "")
    return ""


def review_with_openai(diff_text):
    schema = {
        "type": "object",
        "properties": {
            "summary": {"type": "string"},
            "issues": {
                "type": "array",
                "maxItems": 5,
                "items": {
                    "type": "object",
                    "properties": {
                        "severity": {
                            "type": "string",
                            "enum": ["high", "medium", "low"],
                        },
                        "title": {"type": "string"},
                        "reason": {"type": "string"},
                        "suggestion": {"type": "string"},
                    },
                    "required": ["severity", "title", "reason", "suggestion"],
                    "additionalProperties": False,
                },
            },
        },
        "required": ["summary", "issues"],
        "additionalProperties": False,
    }

    system_prompt = """너는 Unity 게임 프로젝트 PR 리뷰어다.

규칙:
- 한국어로만 답변
- 최대한 간결하게
- 변경된 코드만 기준으로 리뷰
- 사소한 스타일 취향, 네이밍 취향, 과한 리팩토링 제안 금지
- 실제 문제 가능성이 큰 것만 지적
- 문제가 없으면 summary='핵심 이슈 없음', issues=[]

중점:
1. C# 기본 문법/안전성
2. Unity 성능
   - Update/LateUpdate/FixedUpdate 남용
   - GetComponent/Find 반복 호출
   - GC Alloc 가능성
   - Instantiate/Destroy 남용
   - LINQ/boxing/문자열 결합 과다
3. 구조
   - 책임 분리
   - MonoBehaviour 의존 과다
   - 직렬화 필드/접근 제어
   - null 위험
   - 이벤트 구독 해제 누락"""

    user_prompt = f"다음 diff를 리뷰해줘.\n\n```diff\n{diff_text}\n```"

    response = http_json(
        "https://api.openai.com/v1/responses",
        method="POST",
        headers={
            "Authorization": f"Bearer {OPENAI_API_KEY}",
        },
        data={
            "model": "gpt-5.4-mini",
            "store": False,
            "input": [
                {"role": "system", "content": system_prompt},
                {"role": "user", "content": user_prompt},
            ],
            "text": {
                "format": {
                    "type": "json_schema",
                    "name": "unity_pr_review",
                    "strict": True,
                    "schema": schema,
                }
            },
        },
        timeout=120,
    )

    text = extract_output_text(response)
    if not text:
        return {"summary": "핵심 이슈 없음", "issues": []}

    return json.loads(text)


def build_body(result):
    issues = result.get("issues", [])
    if not issues:
        return f"{COMMENT_MARKER}\n## Unity PR 핵심 리뷰\n\n핵심 이슈 없음"

    severity_map = {
        "high": "높음",
        "medium": "중간",
        "low": "낮음",
    }

    lines = [COMMENT_MARKER, "## Unity PR 핵심 리뷰", ""]
    summary = result.get("summary", "").strip()
    if summary and summary != "핵심 이슈 없음":
        lines.append(f"**요약:** {summary}")
        lines.append("")

    for idx, issue in enumerate(issues, 1):
        sev = severity_map.get(issue["severity"], issue["severity"])
        lines.append(f"{idx}. **[{sev}] {issue['title']}**")
        lines.append(f"   - 이유: {issue['reason']}")
        lines.append(f"   - 제안: {issue['suggestion']}")
        lines.append("")

    return "\n".join(lines).strip()


def write_step_summary(body):
    summary_path = os.getenv("GITHUB_STEP_SUMMARY", "")
    if not summary_path:
        print(body)
        return

    with open(summary_path, "w", encoding="utf-8") as f:
        f.write(body + "\n")


def upsert_comment(body):
    list_url = f"https://api.github.com/repos/{REPO}/issues/{PR_NUMBER}/comments?per_page=100"
    comments = github_json(list_url)

    existing = None
    for comment in comments:
        if COMMENT_MARKER in comment.get("body", ""):
            existing = comment
            break

    if existing:
        github_json(existing["url"], method="PATCH", data={"body": body})
    else:
        github_json(
            f"https://api.github.com/repos/{REPO}/issues/{PR_NUMBER}/comments",
            method="POST",
            data={"body": body},
        )


def main():
    if not GITHUB_TOKEN or not REPO or not PR_NUMBER:
        print("[warn] github env missing")
        return

    diff_text = fetch_pr_diff()
    cs_diff = extract_cs_diff(diff_text)

    if not cs_diff.strip():
        body = f"{COMMENT_MARKER}\n## Unity PR 핵심 리뷰\n\n리뷰 대상 C# 변경 없음"
        write_step_summary(body)
        upsert_comment(body)
        return

    if len(cs_diff) > MAX_DIFF_CHARS:
        cs_diff = cs_diff[:MAX_DIFF_CHARS] + "\n\n[diff 일부 생략됨]"

    if not OPENAI_API_KEY:
        body = f"{COMMENT_MARKER}\n## Unity PR 핵심 리뷰\n\n리뷰 스킵: OPENAI_API_KEY 없음"
        write_step_summary(body)
        upsert_comment(body)
        return

    result = review_with_openai(cs_diff)
    body = build_body(result)
    write_step_summary(body)
    upsert_comment(body)


if __name__ == "__main__":
    try:
        main()
    except Exception as e:
        print(f"[error] {e}")
        sys.exit(1)
