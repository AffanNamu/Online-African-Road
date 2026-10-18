# Recoverable baselines

## Lighting baseline: commit `0a853087cbb2b5630b09c53190f5a42c2570e5cb` (0a85308)

What it contains: the tested `LightingModel` (dawn/sunset separation, golden-hour lift, night floor, headlights), Neutral tonemapper default, post-processing assigned (`SceneBuilder.AssignPostProcessData`), Low/Medium/High tier settings (Medium: grade contrast 10, vignette 0.22, shadows 110 m x 2), the Medium terrain-hole fix, and the calibrated driving test. The final Low measurements are in `docs/LIGHTING.md`.

Why it is safe: it is an ancestor of `main` and of `claude/intelligent-faraday-sykb0x`, and no history has been rewritten or force-pushed. As long as that stays true the commit is permanently reachable.

Recover it:
```
git checkout 0a85308                                   # inspect it detached
git checkout -b recover/lighting-0a85308 0a85308       # or start a branch from it
git checkout 0a85308 -- Assets/_Project/Scripts/NetCore/LightingModel.cs Assets/_Project/Scripts/World   # restore only the lighting code over a newer tree
```

Changes since it that touch the game build (all verified by diff, `git diff 0a85308 HEAD -- Assets`):
* `Assets/_Project/Scripts/Game/UIDiagnostics.cs`: E2E-mode input diagnostics only (pointer press/release and button onClick logs).
* `Assets/_Project/Scripts/World/RenderingRig.cs`: smoke-page-only URL overrides (`contrast`, `vignette`, `shadows=off`). Without those parameters the Medium tier uses the baseline values (contrast 10, vignette 0.22, shadows on). **The Medium lighting proposal and a custom sky shader are NOT implemented.**

A tag `lighting-baseline-0a85308` exists in the local clone of the working session; pushing it failed three times ("remote end hung up unexpectedly") and was not worked around. A named branch or tag on the remote would be a convenience, not a requirement, and needs the owner's permission (new branch).

## Remote baseline branch (created 2026-10-18 with the owner's approval)
`baseline/lighting-0a85308` was created on `origin` and points at exactly `0a853087cbb2b5630b09c53190f5a42c2570e5cb` (verified with `git ls-remote`). It must not be moved, rewritten or force-pushed without the owner's approval. The local-only tag `lighting-baseline-0a85308` is superseded by this branch.
