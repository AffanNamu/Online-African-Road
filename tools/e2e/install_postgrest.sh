#!/usr/bin/env bash
# Installs the PostgREST binary the end-to-end backend needs (CI runners have internet access; it is a single static executable).
set -euo pipefail
VER="${POSTGREST_VERSION:-v12.2.3}"
for i in 1 2 3 4; do
  if curl -fsSL "https://github.com/PostgREST/postgrest/releases/download/${VER}/postgrest-${VER}-linux-static-x86-64.tar.xz" | tar -xJ -C /tmp; then break; fi
  echo "download failed (attempt $i)"; sleep $((i * 5)); [ "$i" = 4 ] && exit 1
done
sudo install -m 0755 /tmp/postgrest /usr/local/bin/postgrest
postgrest --version
