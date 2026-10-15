#!/usr/bin/env bash
# Console-mode scenarios for SoundSleuth. Run from Git Bash:
#   bash tests/verify_console.sh [a|b|c|d|e|f ...]     (no argument runs all)
#   a  one continuous 3 s tone  -> exactly 1 powershell event, log line, valid clip
#   b  two 1.5 s bursts, 3 s gap -> exactly 2 events
#   c  a Windows system sound    -> "Windows System Sounds" line with a foreground hint
#   d  LOCALAPPDATA is a file    -> exit 0, an error message, no crash
#   e  bad argument              -> exit 2 and usage text
#   f  mute probe against a running test tone
# Every scenario uses its own LOCALAPPDATA so nothing touches your real log or clips.
# The tones are quiet (amplitude 0.1) and play from a powershell process started here.

HERE=$(cd "$(dirname "$0")" && pwd)
EXE="$HERE/../bin/SoundSleuth.exe"
PROBE="$HERE/bin/MuteProbe.exe"
WORK=${SCRATCH:-$(mktemp -d)}
export MSYS_NO_PATHCONV=1
fail=0

win() { cygpath -w "$1"; }
pass() { echo "PASS $1"; }
bad() { echo "FAIL $1"; fail=1; }

tone() { # seconds bursts gap
  powershell.exe -NoProfile -ExecutionPolicy Bypass -File "$(win "$HERE/tone.ps1")" -Seconds "$1" -Bursts "$2" -Gap "$3" >/dev/null 2>&1
}

# run_console <name> <seconds> <tone-delay-seconds> <command to start the sound...>
run_console() {
  local name=$1 secs=$2 delay=$3; shift 3
  local dir="$WORK/$name"
  rm -rf "$dir"; mkdir -p "$dir/app"
  LOCALAPPDATA=$(win "$dir/app") "$EXE" --console "$secs" >"$dir/out.txt" 2>&1 &
  local pid=$!
  sleep "$delay"
  "$@"
  wait "$pid"
  RC=$?
  OUT="$dir/out.txt"
  APP="$dir/app/SoundSleuth"
}

scenario_a() {
  run_console a 11 2 tone 3 1 0
  local n; n=$(grep -cE '\| powershell.* \| pid [0-9]+ \| .*powershell.exe \|' "$OUT")
  [ "$RC" = 0 ] && pass "a exit code 0" || bad "a exit code $RC"
  [ "$n" = 1 ] && pass "a exactly 1 powershell event" || { bad "a expected 1 powershell event, got $n"; cat "$OUT"; }
  [ "$(grep -c SoundSleuth "$OUT")" = 0 ] && pass "a own process excluded" || bad "a output mentions SoundSleuth"
  grep -E 'peak 0\.[0-9]{3} \| [0-9]+\.[0-9] s' "$OUT" >/dev/null && pass "a peak and duration present" || bad "a peak/duration missing"
  [ "$(grep -cE '\| powershell' "$APP/sounds.log" 2>/dev/null)" -ge 1 ] && pass "a sounds.log has the event" || bad "a sounds.log missing the event"
  local clip; clip=$(ls -t "$APP"/clips/*powershell*.wav 2>/dev/null | head -1)
  if [ -n "$clip" ] && python "$(win "$HERE/check_wav.py")" "$(win "$clip")"; then pass "a clip passes check_wav"; else bad "a clip missing or invalid"; fi
}

scenario_b() {
  run_console b 15 2 tone 1.5 2 3
  local n; n=$(grep -cE '\| powershell.* \| pid [0-9]+ \| .*powershell.exe \|' "$OUT")
  [ "$RC" = 0 ] && pass "b exit code 0" || bad "b exit code $RC"
  [ "$n" = 2 ] && pass "b exactly 2 powershell events" || { bad "b expected 2 events, got $n"; cat "$OUT"; }
  [ "$(grep -cE 'peak 0\.[0-9]{3} \| [0-9]+\.[0-9] s' "$OUT")" -ge 2 ] && pass "b peak and duration on each line" || bad "b peak/duration missing"
}

system_sound() {
  powershell.exe -NoProfile -Command "[System.Media.SystemSounds]::Asterisk.Play(); Start-Sleep -Milliseconds 1500" >/dev/null 2>&1
}

scenario_c() {
  run_console c 8 2 system_sound
  [ "$RC" = 0 ] && pass "c exit code 0" || bad "c exit code $RC"
  grep -E 'Windows System Sounds .*hint: [^ |]+' "$OUT" >/dev/null && pass "c System Sounds line with hint" || { bad "c no System Sounds line with a hint"; cat "$OUT"; }
}

scenario_d() {
  local dir="$WORK/d"; rm -rf "$dir"; mkdir -p "$dir"
  echo "not a folder" > "$dir/blocker"
  LOCALAPPDATA=$(win "$dir/blocker") "$EXE" --console 2 >"$dir/out.txt" 2>&1
  local rc=$?
  [ "$rc" = 0 ] && pass "d exit code 0" || bad "d exit code $rc"
  grep -i 'error' "$dir/out.txt" >/dev/null && pass "d error reported" || { bad "d no error text"; cat "$dir/out.txt"; }
}

scenario_e() {
  local out; out=$("$EXE" --console abc 2>&1); local rc=$?
  [ "$rc" = 2 ] && pass "e exit code 2" || bad "e exit code $rc"
  echo "$out" | grep Usage >/dev/null && pass "e usage text" || bad "e no usage text"
}

scenario_f() {
  [ -f "$PROBE" ] || { bad "f MuteProbe.exe not built (run tests/build_probe.cmd)"; return; }
  tone 12 1 0 &
  local tpid=$!
  local out; out=$("$PROBE" 2>&1); local rc=$?
  echo "$out"
  wait "$tpid"
  [ "$rc" = 0 ] && echo "$out" | grep 'MUTE OK' >/dev/null && pass "f MUTE OK" || bad "f mute probe failed"
}

if [ $# -eq 0 ]; then set -- a b c d e f; fi
for s in "$@"; do
  echo "== scenario $s =="
  "scenario_$s"
done
[ "$fail" = 0 ] && echo "ALL PASS" || echo "SOME FAILED"
exit $fail
