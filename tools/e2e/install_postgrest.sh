#!/usr/bin/env bash
# Installs the PostgREST binary the end-to-end backend needs (CI runners have internet access; it is a single static executable).
set -euo pipefail
VER="${POSTGREST_VERSION:-v12.2.3}"
# release asset naming changed between versions (x64 in v12, x86-64 in older ones): try both names
ok=0
for i in 1 2 3; do
  for arch in x64 x86-64; do
    url="https://github.com/PostgREST/postgrest/releases/download/${VER}/postgrest-${VER}-linux-static-${arch}.tar.xz"
    if curl -fsSL "$url" -o /tmp/postgrest.tar.xz && tar -xJf /tmp/postgrest.tar.xz -C /tmp; then ok=1; echo "downloaded $url"; break 2; fi
    echo "download failed: $url"
  done
  sleep $((i * 5))
done
[ "$ok" = 1 ] || exit 1
sudo install -m 0755 /tmp/postgrest /usr/local/bin/postgrest
postgrest --version
