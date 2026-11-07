#!/usr/bin/env python3
"""A ledger of GitHub Actions runs in which cancelled jobs are recorded SEPARATELY from real test failures (owner decision, 2026-11-07: "record all cancelled jobs separately from actual test failures").
Every job of every run is classified from what the API reports, never from guesswork:

  passed                 conclusion success
  failed (test)          conclusion failure and the failed step is not an infrastructure step
  failed (infrastructure) conclusion failure in a step that fetches or prepares the environment (pre-pull of the Unity image, checkout, install, runner loss): `no space left on device`, image pull, network, before any test body ran
  cancelled (licence queue) conclusion cancelled and the job never ran a step: it was pending in a concurrency group (the single Unity licence) and a newer pending job replaced it; NOT a test failure
  cancelled (other)      conclusion cancelled after steps ran (a person or a timeout)
  skipped                conclusion skipped (a job it needs did not run)
  running / queued       not finished

Usage: python3 tools/ci_ledger.py --repo AffanNamu/Online-African-Road [--branch main] [--limit 30] [--out docs/CI_LEDGER.md]   (needs the gh CLI, authenticated)"""
from __future__ import annotations
import argparse, collections, json, subprocess, sys

INFRA_STEPS = ("pre-pull", "set up job", "checkout", "install", "pull ", "docker", "setup-", "cache", "upload-artifact", "post run", "complete job")
INFRA_TEXT = ("no space left on device", "the runner has received a shutdown signal", "lost communication with the server")


def classify(job: dict, step_log_hint: str = "") -> str:
    c, st = job.get("conclusion"), job.get("status")
    if st != "completed": return "running" if st == "in_progress" else "queued"
    steps = job.get("steps") or []
    ran = [s for s in steps if s.get("conclusion") in ("success", "failure")]
    if c == "success": return "passed"
    if c == "skipped": return "skipped"
    if c == "neutral": return "neutral (reporting job)"
    if c == "cancelled": return "cancelled (licence queue)" if not ran else "cancelled (other)"
    if c == "failure":
        bad = [s for s in steps if s.get("conclusion") == "failure"]; name = (bad[0]["name"] if bad else "").lower()
        if any(t in step_log_hint.lower() for t in INFRA_TEXT) or any(name.startswith(p) or p in name for p in INFRA_STEPS[:5]): return "failed (infrastructure)"
        return "failed (test)"
    return f"other ({c})"


def gh(*a: str):
    r = subprocess.run(["gh", "api", *a], capture_output=True, text=True)
    if r.returncode: raise SystemExit(f"gh api {' '.join(a)}: {r.stderr.strip()[:300]}")
    return json.loads(r.stdout)


def ledger(repo: str, branch: str, limit: int) -> list:
    q = f"repos/{repo}/actions/runs?per_page={limit}" + (f"&branch={branch}" if branch else ""); rows = []
    for run in gh(q)["workflow_runs"]:
        jobs = gh(f"repos/{repo}/actions/runs/{run['id']}/jobs?per_page=100")["jobs"]
        rows.append({"run": run["id"], "name": run["name"], "branch": run["head_branch"], "sha": run["head_sha"][:7], "event": run["event"], "created": run["created_at"], "conclusion": run["conclusion"] or run["status"],
                     "jobs": [{"name": j["name"], "class": classify(j)} for j in jobs]})
    return rows


def markdown(rows: list) -> str:
    L = ["# CI ledger", "", "Cancelled jobs are recorded separately from test failures (`tools/ci_ledger.py`, classification rules in its docstring). **A `cancelled (licence queue)` job is not a failure of anything: it was waiting for the single Unity licence and a newer pending job replaced it; re-run it when the licence is free.**", ""]
    tot = collections.Counter(j["class"] for r in rows for j in r["jobs"]); L += ["Totals over these runs: " + ", ".join(f"{k} {v}" for k, v in sorted(tot.items())), "",
        "| Run | Workflow | Branch | Commit | Event | passed | failed (test) | failed (infrastructure) | cancelled (licence queue) | cancelled (other) | skipped | not finished |", "|---|---|---|---|---|---|---|---|---|---|---|---|"]
    for r in rows:
        c = collections.Counter(j["class"] for j in r["jobs"]); fin = c["running"] + c["queued"]
        L.append(f"| {r['run']} | {r['name']} | {r['branch']} | `{r['sha']}` | {r['event']} | {c['passed']} | {c['failed (test)']} | {c['failed (infrastructure)']} | {c['cancelled (licence queue)']} | {c['cancelled (other)']} | {c['skipped']} | {fin} |")
    return "\n".join(L) + "\n"


def main() -> int:
    ap = argparse.ArgumentParser(); ap.add_argument("--repo", required=True); ap.add_argument("--branch", default=""); ap.add_argument("--limit", type=int, default=30); ap.add_argument("--out", default=""); a = ap.parse_args()
    md = markdown(ledger(a.repo, a.branch, a.limit))
    if a.out: open(a.out, "w").write(md); print("wrote", a.out)
    else: print(md)
    return 0


if __name__ == "__main__":
    sys.exit(main())
