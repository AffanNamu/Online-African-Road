#!/usr/bin/env bash
# The real content of the PLAYABLE build (owner decision of 2026-11-16: the real people and building models go into the playable build once the review pictures are seen).
# Builds FROM THE CODE, exactly as the review build does (.github/workflows/blender-unity.yml): the residential kit (v3), the environmental kit with its E4 corrections, the transport kit
# (city buildings, fuel stations, mosques, terminals, furniture), stages them under Assets/ThirdParty/ARO (git-ignored), and builds the realistic people on the CC0 MakeHuman base into
# Assets/ThirdParty/ARO/Resources/People. Every asset must pass the toolchain validation, or this fails. Nothing is committed; no binary comes from anywhere but this code.
# The Unity side then makes prefabs only for the route props that have a model (ARO.Editor.BuildScript.BuildWebGLWithContent).
# Usage (repository root, Python 3.13 with tools/blender/requirements.txt):  tools/blender/build_content.sh [out]
set -euo pipefail
OUT=${1:-Assets/ThirdParty/ARO}
W=${RUNNER_TEMP:-/tmp}/aro-content; mkdir -p "$W"
export PYTHONPATH=tools/blender

check() {   # report.json, what: every asset passed validation, at least one built
  python3 - "$1" "$2" <<'PY'
import json, sys
r = json.load(open(sys.argv[1])); bad = [x["id"] for x in r if not x["passed"]]
print(f"{len(r)} {sys.argv[2]} assets built; failed validation: {bad or 'none'}")
sys.exit(1 if bad or not r else 0)
PY
}

ARO_WORKSPACE="$W/ws" python -m kano_kit.build3 --report "$W/kit3.json";                 check "$W/kit3.json" "residential (v3)"
ARO_WORKSPACE="$W/ws" python -m env_kit.build_env --report "$W/env.json";                 check "$W/env.json" "environmental"
ARO_WORKSPACE="$W/ws" python -m env_kit.build_e4 --report "$W/e4.json";                   check "$W/e4.json" "environmental E4"
ARO_WORKSPACE="$W/ws-tk" python -m transport_kit.build_transport --report "$W/tk.json";   check "$W/tk.json" "transport-kit"
ARO_WORKSPACE="$W/ws" ARO_ENV_KIT=e4 python -m kano_kit.stage_unity --out "$OUT"
ARO_WORKSPACE="$W/ws-tk" python -m transport_kit.stage_unity --out "$OUT"
export ARO_MAKEHUMAN_DATA="$(tools/blender/people_kit/fetch_makehuman.sh "$W/mpfb2" | tail -1)"
python -m people_kit.export --out "$OUT/Resources/People"
echo "content staged under $OUT:"; ls "$OUT" "$OUT/Resources/People"
