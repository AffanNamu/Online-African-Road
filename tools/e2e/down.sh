#!/usr/bin/env bash
# Stops what up.sh started and drops the database.
[ -f /tmp/aro-e2e.pids ] && xargs kill < /tmp/aro-e2e.pids 2>/dev/null || true
rm -f /tmp/aro-e2e.pids
psql -q -d postgres -c "drop database if exists aro_e2e" 2>/dev/null || true
