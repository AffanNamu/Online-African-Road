#!/usr/bin/env bash
# Runs INSIDE the unityci/editor container. Usage: license-exp.sh E1|E2|E3|E4
# Prints bounded, filtered licensing output only. Secrets are masked by the workflow (add-mask) before this runs.
U=$(command -v unity-editor || echo /opt/unity/Editor/Unity)
LIC=/lic/Unity_lic.ulf
filt() { grep -iE 'licens|entitle|activat|serial|headless|login|sign.?in' "$1" | sed -E 's/[A-Za-z0-9]{2}(-[A-Za-z0-9]{4}){5}/<serial>/g' | sort | uniq -c | sort -rn | head -${2:-20}; }
verify() { # does a fresh editor start accept the license? (a valid license keeps running until the timeout; an invalid one exits fast with 198)
  local t0=$(date +%s)
  timeout 100 "$U" -batchmode -nographics -quit -createProject /tmp/vp -logFile - > /tmp/verify.log 2>&1; local rc=$?
  echo "verify: exit=$rc after $(( $(date +%s) - t0 ))s  (124 = still running at timeout = license accepted; 198 = no valid license)"
  if grep -qE 'No valid Unity Editor license|editor.headless. was not found' /tmp/verify.log; then echo "VERDICT $1: license NOT usable"; else echo "VERDICT $1: no licensing error seen"; fi
  filt /tmp/verify.log 12
}
echo "=== $1 ($($U -version 2>/dev/null | head -1)) ==="
case "$1" in
 E1) echo "ulf copied to the legacy per-user path"; mkdir -p /root/.local/share/unity3d/Unity; cp $LIC /root/.local/share/unity3d/Unity/Unity_lic.ulf; verify E1 ;;
 E2) echo "-manualLicenseFile"; timeout 120 "$U" -batchmode -nographics -quit -manualLicenseFile $LIC -logFile - > /tmp/act.log 2>&1; echo "activate: exit=$?"; filt /tmp/act.log; verify E2 ;;
 E3) echo "-serial (from the ulf's DeveloperData) + -username/-password"
     timeout 150 "$U" -batchmode -nographics -quit -username "$UNITY_EMAIL" -password "$UNITY_PASSWORD" -serial "$(cat /lic/serial)" -logFile - > /tmp/act.log 2>&1; echo "activate: exit=$?"; filt /tmp/act.log; verify E3 ;;
 E4) echo "licensing client binary and options"
     find /opt/unity -iname 'Unity.Licensing.Client*' 2>/dev/null | head -5
     C=$(find /opt/unity -iname 'Unity.Licensing.Client' -type f 2>/dev/null | head -1)
     if [ -n "$C" ]; then "$C" --help 2>&1 | head -40; fi ;;
esac
