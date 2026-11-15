#!/usr/bin/env bash
# Đặt phiên bản MacExtend cho các lần build sau. CI lấy đúng chuỗi trong version.txt, không cộng thêm số lần chạy.
#
#   tools/set-version.sh                  xem phiên bản hiện tại và bản phát hành mới nhất
#   tools/set-version.sh 1.1.0            đặt phiên bản
#   tools/set-version.sh 1.1.0-beta.1     bản thử (pre-release): không thành bản Latest, app không tự cập nhật lên nó
#   tools/set-version.sh patch            tăng số cuối  (1.0.22 -> 1.0.23)
#   tools/set-version.sh minor            tăng số giữa  (1.0.22 -> 1.1.0)
#   tools/set-version.sh major            tăng số đầu   (1.0.22 -> 2.0.0)
#   --force                               bỏ qua kiểm tra so với bản phát hành mới nhất
#
# patch/minor/major tính từ số lớn hơn giữa version.txt và bản phát hành mới nhất. App đã cài chỉ tự cập nhật khi
# X.Y.Z lớn hơn bản nó đang chạy, nên script từ chối X.Y.Z không lớn hơn bản phát hành mới nhất (trừ khi --force).
# Script chỉ sửa version.txt; commit và push vẫn do bạn làm.
set -euo pipefail

root="$(cd "$(dirname "$0")/.." && pwd)"
file="$root/version.txt"
workflow="$root/.github/workflows/build.yml"

force=0
arg=""
for a in "$@"; do
  case "$a" in
    --force) force=1 ;;
    -h|--help) sed -n '2,15p' "$0" | sed 's/^# \{0,1\}//'; exit 0 ;;
    *) if [ -n "$arg" ]; then echo "Chỉ nhận một phiên bản." >&2; exit 2; fi; arg="$a" ;;
  esac
done

current="$(tr -d ' \r\n' < "$file" 2>/dev/null || true)"

# the repository the releases go to, from the workflow, so the two never disagree
repo="$(sed -n 's/^  RELEASE_REPOSITORY: *//p' "$workflow" | tr -d ' \r' | head -n 1)"

# the newest non-pre-release tag, without its "v"; empty when it cannot be read (offline, no gh and no curl)
latest_release() {
  local tag=""
  if command -v gh >/dev/null 2>&1; then
    tag="$(gh release list --repo "$repo" --exclude-pre-releases --limit 1 --json tagName -q '.[0].tagName' 2>/dev/null || true)"
  fi
  if [ -z "$tag" ] && command -v curl >/dev/null 2>&1; then
    tag="$(curl -fsSL --max-time 10 "https://api.github.com/repos/$repo/releases/latest" 2>/dev/null \
           | sed -n 's/^ *"tag_name": *"\([^"]*\)".*/\1/p' | head -n 1 || true)"
  fi
  printf '%s' "${tag#v}"
}

# X.Y.Z of a version ("1.0" counts as 1.0.0, a pre-release suffix is dropped)
triple() {
  local n="${1%%-*}"
  local a b c
  IFS=. read -r a b c <<< "$n"
  printf '%d %d %d' "${a:-0}" "${b:-0}" "${c:-0}"
}

# 0 when $1 > $2 by X.Y.Z
greater() {
  local a1 a2 a3 b1 b2 b3
  read -r a1 a2 a3 <<< "$(triple "$1")"
  read -r b1 b2 b3 <<< "$(triple "$2")"
  [ "$a1" -gt "$b1" ] && return 0; [ "$a1" -lt "$b1" ] && return 1
  [ "$a2" -gt "$b2" ] && return 0; [ "$a2" -lt "$b2" ] && return 1
  [ "$a3" -gt "$b3" ]
}

valid() {
  [[ "$1" =~ ^([0-9]+)\.([0-9]+)\.([0-9]+)(-[0-9A-Za-z]+(\.[0-9A-Za-z]+)*)?$ ]] || return 1
  # Windows file versions stop at 65534 per part
  local p
  for p in "${BASH_REMATCH[1]}" "${BASH_REMATCH[2]}" "${BASH_REMATCH[3]}"; do
    [ "$((10#$p))" -le 65534 ] || return 1
  done
}

latest="$(latest_release)"

if [ -z "$arg" ]; then
  echo "version.txt:          ${current:-(trống)}"
  echo "Bản phát hành mới nhất: ${latest:-(không đọc được)} ($repo)"
  echo
  echo "Đặt phiên bản: tools/set-version.sh 1.1.0 | 1.1.0-beta.1 | patch | minor | major"
  exit 0
fi

case "$arg" in
  patch|minor|major)
    base="$current"
    if [ -n "$latest" ] && greater "$latest" "$base"; then base="$latest"; fi
    read -r x y z <<< "$(triple "$base")"
    case "$arg" in
      patch) next="$x.$y.$((z + 1))" ;;
      minor) next="$x.$((y + 1)).0" ;;
      major) next="$((x + 1)).0.0" ;;
    esac
    ;;
  *)
    next="${arg#v}"
    ;;
esac

if ! valid "$next"; then
  echo "'$next' không phải phiên bản hợp lệ. Dạng đúng: X.Y.Z hoặc X.Y.Z-beta.1 (mỗi số tối đa 65534)." >&2
  exit 2
fi

if [ "$force" -eq 0 ]; then
  if [ -z "$latest" ]; then
    echo "Không đọc được bản phát hành mới nhất của $repo; vẫn đặt phiên bản. CI sẽ từ chối nếu bản này đã có." >&2
  elif ! greater "$next" "$latest"; then
    echo "$next không lớn hơn bản phát hành mới nhất $latest: app đã cài sẽ không tự cập nhật lên nó." >&2
    echo "Chọn số lớn hơn, hoặc thêm --force nếu bạn cố ý." >&2
    exit 1
  fi
fi

printf '%s\n' "$next" > "$file"
echo "version.txt: ${current:-(trống)} -> $next"
case "$next" in
  *-*) echo "Đây là bản thử (pre-release): release không thành Latest, app đã cài không tự cập nhật lên nó." ;;
esac
echo
echo "Bước tiếp theo (bạn tự làm):"
echo "  git add version.txt && git commit -m \"chore: version $next\" && git push"
