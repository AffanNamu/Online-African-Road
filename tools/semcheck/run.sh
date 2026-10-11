#!/usr/bin/env bash
# Prints compile errors of the Unity scripts that are NOT stub gaps (see README.md). Exit 1 if any.
cd "$(dirname "$0")"
out=$(dotnet build -nologo -v q 2>&1 | grep -E " error " | sed 's/\[.*//' | sed 's#.*/Assets/_Project/##' | sort -u)
while IFS= read -r pat; do [ -n "$pat" ] && out=$(echo "$out" | grep -vF "$pat"); done < known-noise.txt
if [ -n "$(echo "$out" | tr -d '[:space:]')" ]; then echo "$out"; exit 1; fi
echo "semcheck: no errors outside the known stub gaps"
