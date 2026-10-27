#!/usr/bin/env bash
# Builds uxplay.exe from the commit pinned in native/uxplay.ref.
# Run inside an MSYS2 UCRT64 shell. Output: build/uxplay/uxplay.exe
set -euo pipefail

here="$(cd "$(dirname "$0")" && pwd)"
root="$(cd "$here/.." && pwd)"
src="$root/build/uxplay-src"
out="$root/build/uxplay"

url=$(sed -n 's/^URL=//p' "$here/uxplay.ref" | tr -d '\r')
commit=$(sed -n 's/^COMMIT=//p' "$here/uxplay.ref" | tr -d '\r')

rm -rf "$src" "$out"
git init -q "$src"
git -C "$src" fetch -q --depth 1 "$url" "$commit"
git -C "$src" checkout -q FETCH_HEAD

# NO_MARCH_NATIVE: the CI CPU is not the user's CPU.
cmake -S "$src" -B "$out" -G Ninja \
  -DCMAKE_BUILD_TYPE=Release \
  -DNO_MARCH_NATIVE=ON \
  -DNO_X11_DEPS=ON
cmake --build "$out" --parallel

test -f "$out/uxplay.exe"
echo "built $out/uxplay.exe from $commit"
