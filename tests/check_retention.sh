#!/usr/bin/env bash
# Retention check: >500 dummy clip-named files plus one non-clip file in an isolated
# LOCALAPPDATA clips folder; one real tone event is then recorded. Asserts the clip count
# is <= 500, the new clip survives, and the non-matching file survives.
#   bash tests/check_retention.sh
HERE=$(cd "$(dirname "$0")" && pwd)
EXE="$HERE/../bin/SoundSleuth.exe"
export MSYS_NO_PATHCONV=1
win() { cygpath -w "$1"; }
D=$(mktemp -d); APP="$D/app/SoundSleuth"; CL="$APP/clips"; mkdir -p "$CL"
for i in $(seq 1 520); do
  n=$(printf '20200101-%06d_dummy_%d.wav' "$i" "$i"); head -c 1000 /dev/zero > "$CL/$n"
  touch -d "2020-01-01 00:00:00 +0000" "$CL/$n"; touch -d "@$((1577836800 + i))" "$CL/$n"
done
echo keep > "$CL/notes.txt"; head -c 1000 /dev/zero > "$CL/other.wav"; touch -d "2019-01-01" "$CL/other.wav" "$CL/notes.txt"
LOCALAPPDATA=$(win "$D/app") "$EXE" --console 9 >"$D/out.txt" 2>&1 &
pid=$!; sleep 2
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "$(win "$HERE/tone.ps1")" -Seconds 3 -Bursts 1 -Gap 0 >/dev/null 2>&1
wait $pid
fail=0
n=$(ls "$CL" | grep -cE '^[0-9]{8}-[0-9]{6}_.+_[0-9]+(-[0-9]+)?\.wav$')
new=$(ls "$CL"/*powershell*.wav 2>/dev/null | wc -l)
[ "$n" -le 500 ] && echo "PASS clip count $n <= 500" || { echo "FAIL clip count $n"; fail=1; }
[ "$new" -ge 1 ] && echo "PASS new clip kept" || { echo "FAIL new clip missing"; cat "$D/out.txt"; fail=1; }
[ -f "$CL/notes.txt" ] && [ -f "$CL/other.wav" ] && echo "PASS non-matching files survived" || { echo "FAIL non-matching file deleted"; fail=1; }
[ -f "$CL/20200101-000001_dummy_1.wav" ] && { echo "FAIL oldest dummy not pruned"; fail=1; } || echo "PASS oldest dummy pruned"
rm -rf "$D"
[ $fail = 0 ] && echo "ALL PASS" || echo "SOME FAILED"; exit $fail
