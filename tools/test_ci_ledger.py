import sys
from pathlib import Path
sys.path.insert(0, str(Path(__file__).resolve().parent))
import ci_ledger as L


def job(conclusion, steps=(), status="completed"): return {"conclusion": conclusion, "status": status, "steps": [{"name": n, "conclusion": c} for n, c in steps]}


def test_a_job_that_never_ran_a_step_and_was_cancelled_is_the_licence_queue_not_a_failure():
    assert L.classify(job("cancelled")) == "cancelled (licence queue)"
    assert L.classify(job("cancelled", [("Run tests", "success"), ("Build", "cancelled")])) == "cancelled (other)"


def test_infrastructure_failures_are_told_apart_from_test_failures():
    assert L.classify(job("failure", [("Pre-pull Unity editor images (retry with backoff)", "failure")])) == "failed (infrastructure)"
    assert L.classify(job("failure", [("Run the rule tests", "failure")]), "ERROR: no space left on device") == "failed (infrastructure)"
    assert L.classify(job("failure", [("Run the rule tests", "failure")])) == "failed (test)"


def test_the_other_states():
    assert L.classify(job("success")) == "passed" and L.classify(job("skipped")) == "skipped"
    assert L.classify(job(None, status="in_progress")) == "running" and L.classify(job(None, status="queued")) == "queued"


def test_markdown_counts_the_classes_separately():
    rows = [{"run": 1, "name": "CI", "branch": "main", "sha": "abc1234", "event": "push", "created": "x", "conclusion": "failure", "jobs": [{"name": "a", "class": "passed"}, {"name": "b", "class": "cancelled (licence queue)"}, {"name": "c", "class": "failed (test)"}]}]
    md = L.markdown(rows); assert "| 1 | CI | main | `abc1234` | push | 1 | 1 | 0 | 1 | 0 | 0 | 0 |" in md and "not a failure of anything" in md
