#!/usr/bin/env bash
# Copies uxplay.exe, the GStreamer plugins listed in gst-elements.txt and every DLL they
# load from the MSYS2 prefix into dist/uxplay, mirroring the MSYS2 layout so GStreamer's
# relocatable plugin lookup works:
#   bin/                    uxplay.exe, gst-inspect-1.0.exe, gst-launch-1.0.exe, DLLs
#   lib/gstreamer-1.0/      plugins
#   libexec/gstreamer-1.0/  gst-plugin-scanner.exe
# Run inside an MSYS2 UCRT64 shell after build-uxplay.sh.
set -Eeuo pipefail
trap 'echo "error: line $LINENO: \"$BASH_COMMAND\" exited with status $?" >&2' ERR

here="$(cd "$(dirname "$0")" && pwd)"
root="$(cd "$here/.." && pwd)"
exe="$root/build/uxplay/uxplay.exe"
src="$root/build/uxplay-src"
out="$root/dist/uxplay"

for tool in cygpath gst-inspect-1.0 ldd awk sed tr; do
  if ! command -v "$tool" >/dev/null 2>&1; then
    echo "error: '$tool' is not on PATH; run this in an MSYS2 UCRT64 shell with the packages from build.yml" >&2
    exit 1
  fi
done
prefix="$(cygpath -u "${MINGW_PREFIX:?run this in an MSYS2 UCRT64 shell}")"
[ -f "$exe" ] || { echo "error: $exe not found; run native/build-uxplay.sh first" >&2; exit 1; }

rm -rf "$out"
mkdir -p "$out/bin" "$out/lib/gstreamer-1.0" "$out/libexec/gstreamer-1.0" "$out/licenses"

cp "$exe" "$out/bin/"
cp "$prefix/bin/gst-inspect-1.0.exe" "$prefix/bin/gst-launch-1.0.exe" "$out/bin/"
cp "$prefix/libexec/gstreamer-1.0/gst-plugin-scanner.exe" "$out/libexec/gstreamer-1.0/"

plugin_file() {
  # "Filename" of an element or plugin, empty when GStreamer does not know the name.
  # gst-inspect exits non-zero for unknown names; that is an answer here, not a failure.
  { gst-inspect-1.0 "$1" 2>/dev/null || true; } | tr -d '\r' | sed -n 's/^ *Filename *//p' | sed -n '1p'
}

trim() {
  local s="${1%%#*}"
  s="${s//$'\r'/}"
  s="${s#"${s%%[![:space:]]*}"}"
  s="${s%"${s##*[![:space:]]}"}"
  printf '%s' "$s"
}

while IFS= read -r raw || [ -n "$raw" ]; do
  line="$(trim "$raw")"
  [ -z "$line" ] && continue
  optional=0
  if [[ "$line" == \?* ]]; then optional=1; line="${line#\?}"; fi
  element="${line%%@*}"
  plugin=""
  if [[ "$line" == *@* ]]; then plugin="${line#*@}"; fi

  file="$(plugin_file "$element")"
  if [ -z "$file" ] && [ -n "$plugin" ]; then
    file="$(plugin_file "$plugin")"
    if [ -z "$file" ] && [ -f "$prefix/lib/gstreamer-1.0/libgst$plugin.dll" ]; then
      # GPU plugins may refuse to register on a machine without a GPU (CI); ship the DLL anyway
      file="$prefix/lib/gstreamer-1.0/libgst$plugin.dll"
    fi
    if [ -n "$file" ]; then echo "note: $element is not registered on this machine; bundling plugin $plugin"; fi
  fi
  if [ -z "$file" ]; then
    if [ "$optional" = 1 ]; then
      echo "warning: optional element $element not available"
      continue
    fi
    echo "error: required element $element not found (is its gst-plugins package installed?)" >&2
    exit 1
  fi
  cp -f "$(cygpath -u "$file")" "$out/lib/gstreamer-1.0/"
  echo "ok   $element <- $(basename "$file")"
done < "$here/gst-elements.txt"

# DLL closure: ldd lists every DLL an image loads; keep the ones from the MSYS2 prefix.
# A DLL already in bin/ has been queued before, so the folder itself is the "seen" set.
queue=("$out"/bin/*.exe "$out"/libexec/gstreamer-1.0/*.exe "$out"/lib/gstreamer-1.0/*.dll)
while ((${#queue[@]})); do
  image="${queue[0]}"
  queue=("${queue[@]:1}")
  while IFS= read -r dep; do
    [ -z "$dep" ] && continue
    name="$(basename "$dep")"
    [ -e "$out/bin/$name" ] && continue
    cp "$dep" "$out/bin/"
    queue+=("$out/bin/$name")
  done < <({ ldd "$image" 2>/dev/null || true; } | tr -d '\r' | awk '{print $3}' | { grep -i "^$prefix/" || true; })
done

for pkg in gstreamer gst-plugins-base gst-plugins-good gst-plugins-bad gst-libav ffmpeg glib2 libplist openssl; do
  if [ -d "$prefix/share/licenses/$pkg" ]; then cp -r "$prefix/share/licenses/$pkg" "$out/licenses/"; fi
done
cp "$src/LICENSE" "$out/licenses/UxPlay-LICENSE.txt"

# The bundled uxplay must start from its own folder; a missing DLL shows up here as exit 127.
if ! version_line="$("$out/bin/uxplay.exe" -v 2>&1)"; then
  echo "error: bundled uxplay.exe does not start: $version_line" >&2
  echo "       missing DLLs according to ldd:" >&2
  { ldd "$out/bin/uxplay.exe" 2>&1 || true; } | { grep -i "not found\|???" || true; } >&2
  exit 1
fi
uxplay_version="$(printf '%s\n' "$version_line" | tr -d '\r' | sed -n 's/^UxPlay version \([^;]*\).*/\1/p')"
commit=$(sed -n 's/^COMMIT=//p' "$here/uxplay.ref" | tr -d '\r')
gst_version="$({ gst-inspect-1.0 --version || true; } | tr -d '\r' | sed -n 's/^GStreamer \(.*\)/\1/p' | sed -n '1p')"
printf 'UxPlay %s (%s)\nGStreamer %s\n' "${uxplay_version:-unknown}" "${commit:0:7}" "${gst_version:-unknown}" > "$out/VERSION.txt"

# Fingerprint of what went into the runtime. Rebuilt binaries differ byte for byte (timestamps),
# so the updater compares this instead: same inputs means the installed runtime can stay.
sha256() { if command -v sha256sum >/dev/null 2>&1; then sha256sum; else shasum -a 256; fi; }
runtime_id="$({
  cat "$here/uxplay.ref" "$here/gst-elements.txt" "$here/build-uxplay.sh" "$here/bundle-runtime.sh"
  { pacman -Q 2>/dev/null || true; } | { grep '^mingw-w64-' || true; }
} | tr -d '\r' | sha256 | cut -d' ' -f1)"
printf '%s\n' "$runtime_id" > "$out/RUNTIME_ID"

echo "bundled $(find "$out" -type f | wc -l | tr -d ' ') files, $(du -sh "$out" | cut -f1) into $out"
cat "$out/VERSION.txt"
