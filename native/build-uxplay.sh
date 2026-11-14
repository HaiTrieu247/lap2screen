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

# Direct control maps clicks on the picture to the device's screen, so it needs the mirrored picture's size, which
# uxplay logs only at debug level ("raop_rtp_mirror width_source = ..."), again after every rotation. That one line
# goes up to info; the build stops if upstream moved it.
mirror="$src/lib/raop_rtp_mirror.c"
if ! awk '{ line[NR] = $0 }
     END {
       for (i = 1; i <= NR; i++) {
         if (i < NR && index(line[i + 1], "\"raop_rtp_mirror width_source = ") && sub(/LOGGER_DEBUG,/, "LOGGER_INFO,", line[i])) n++
         print line[i]
       }
       exit (n == 1 ? 0 : 1)
     }' "$mirror" > "$mirror.new"; then
  echo "the picture-size log line was not found in $mirror" >&2
  exit 1
fi
mv "$mirror.new" "$mirror"

# NO_MARCH_NATIVE: the CI CPU is not the user's CPU.
cmake -S "$src" -B "$out" -G Ninja \
  -DCMAKE_BUILD_TYPE=Release \
  -DNO_MARCH_NATIVE=ON \
  -DNO_X11_DEPS=ON
cmake --build "$out" --parallel

test -f "$out/uxplay.exe"
echo "built $out/uxplay.exe from $commit"
