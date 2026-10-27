#!/usr/bin/env bash
# Copies uxplay.exe, the GStreamer plugins listed in gst-elements.txt and every DLL they
# load from the MSYS2 prefix into dist/uxplay, mirroring the MSYS2 layout so GStreamer's
# relocatable plugin lookup works:
#   bin/                    uxplay.exe, gst-inspect-1.0.exe, gst-launch-1.0.exe, DLLs
#   lib/gstreamer-1.0/      plugins
#   libexec/gstreamer-1.0/  gst-plugin-scanner.exe
# Run inside an MSYS2 UCRT64 shell after build-uxplay.sh.
set -euo pipefail

here="$(cd "$(dirname "$0")" && pwd)"
root="$(cd "$here/.." && pwd)"
exe="$root/build/uxplay/uxplay.exe"
src="$root/build/uxplay-src"
out="$root/dist/uxplay"
prefix="$(cygpath -u "${MINGW_PREFIX:?run this in an MSYS2 UCRT64 shell}")"

rm -rf "$out"
mkdir -p "$out/bin" "$out/lib/gstreamer-1.0" "$out/libexec/gstreamer-1.0" "$out/licenses"

cp "$exe" "$out/bin/"
cp "$prefix/bin/gst-inspect-1.0.exe" "$prefix/bin/gst-launch-1.0.exe" "$out/bin/"
cp "$prefix/libexec/gstreamer-1.0/gst-plugin-scanner.exe" "$out/libexec/gstreamer-1.0/"

plugin_file() {
  # "Filename" line of gst-inspect output for an element or plugin name
  gst-inspect-1.0 "$1" 2>/dev/null | tr -d '\r' | sed -n 's/^ *Filename *//p' | head -n 1
}

while IFS= read -r line || [ -n "$line" ]; do
  line="${line%%#*}"
  line="$(echo "$line" | tr -d '\r' | xargs)"
  [ -z "$line" ] && continue
  optional=0
  if [[ "$line" == \?* ]]; then optional=1; line="${line#\?}"; fi
  element="${line%%@*}"
  plugin=""
  [[ "$line" == *@* ]] && plugin="${line#*@}"

  file="$(plugin_file "$element")"
  if [ -z "$file" ] && [ -n "$plugin" ]; then
    file="$(plugin_file "$plugin")"
    [ -n "$file" ] && echo "note: $element is not registered on this machine; bundling plugin $plugin"
  fi
  if [ -z "$file" ]; then
    if [ "$optional" = 1 ]; then echo "warning: optional element $element not available"; continue; fi
    echo "error: required element $element not found" >&2
    exit 1
  fi
  cp -u "$(cygpath -u "$file")" "$out/lib/gstreamer-1.0/"
done < "$here/gst-elements.txt"

# DLL closure: ldd lists every DLL an image loads; keep the ones from the MSYS2 prefix.
declare -A seen=()
queue=("$out"/bin/*.exe "$out"/libexec/gstreamer-1.0/*.exe "$out"/lib/gstreamer-1.0/*.dll)
while ((${#queue[@]})); do
  image="${queue[0]}"
  queue=("${queue[@]:1}")
  while IFS= read -r dep; do
    [ -z "$dep" ] && continue
    name="$(basename "$dep")"
    [ -n "${seen[$name]:-}" ] && continue
    seen[$name]=1
    cp "$dep" "$out/bin/"
    queue+=("$out/bin/$name")
  done < <(ldd "$image" | tr -d '\r' | awk '{print $3}' | grep -i "^$prefix/" || true)
done

for pkg in gstreamer gst-plugins-base gst-plugins-good gst-plugins-bad gst-libav ffmpeg glib2 libplist openssl; do
  [ -d "$prefix/share/licenses/$pkg" ] && cp -r "$prefix/share/licenses/$pkg" "$out/licenses/"
done
cp "$src/LICENSE" "$out/licenses/UxPlay-LICENSE.txt"

uxplay_version="$("$out/bin/uxplay.exe" -v | tr -d '\r' | sed -n 's/^UxPlay version \([^;]*\).*/\1/p')"
commit=$(sed -n 's/^COMMIT=//p' "$here/uxplay.ref" | tr -d '\r')
gst_version="$(gst-inspect-1.0 --version | tr -d '\r' | sed -n 's/^GStreamer \(.*\)/\1/p' | head -n 1)"
printf 'UxPlay %s (%s)\nGStreamer %s\n' "$uxplay_version" "${commit:0:7}" "$gst_version" > "$out/VERSION.txt"

echo "bundled $(find "$out" -type f | wc -l) files, $(du -sh "$out" | cut -f1) into $out"
cat "$out/VERSION.txt"
