#!/usr/bin/env python3
"""supabase/storage_ads.sql must stay an exact copy of the storage block of the advertising migration (so a project that gets Storage later ends up with the same buckets and policies)."""
import os, sys
root = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
mig = open(os.path.join(root, "supabase", "migrations", "20261110000001_advertising.sql")).read()
a = mig.index("-- ================================================================ storage: two buckets and their policies")
b = mig.index("-- ================================================================ lock-down")
block = mig[a:b].strip()
copy = open(os.path.join(root, "supabase", "storage_ads.sql")).read()
if block not in copy:
    print("supabase/storage_ads.sql no longer contains the storage block of the advertising migration verbatim"); sys.exit(1)
print("storage_ads.sql matches the migration's storage block")
