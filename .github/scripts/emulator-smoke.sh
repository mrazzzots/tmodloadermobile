#!/usr/bin/env bash
# Runs each probe APK in apk/ on the emulator with autorun and collects logs per APK.
set -uo pipefail

PKG=io.github.tmldroid.monomodprobe
ACT="$PKG/$PKG.MainActivity"
ROOT=emulator-out
mkdir -p "$ROOT"

for apk in apk/*.apk; do
  name="$(basename "$apk" .apk)"
  out="$ROOT/$name"
  mkdir -p "$out"
  echo
  echo "################ $name ################"
  adb uninstall "$PKG" >/dev/null 2>&1 || true
  adb install "$apk" || { echo "install failed"; continue; }
  adb logcat -c
  adb shell am start -W -n "$ACT" --ez autorun true
  sleep 75
  adb logcat -d -v threadtime > "$out/logcat-full.txt"
  pid="$(adb shell pidof "$PKG" | tr -d '\r')"
  echo "RESULT $name: process ${pid:+alive (pid $pid)}${pid:-NOT running (crashed or exited)}"
  grep -E "DOTNET|monodroid|mono|AndroidRuntime|FATAL|F DEBUG|F libc|UNHANDLED|\[(PASS|FAIL|INFO)\]|passed [0-9]" \
    "$out/logcat-full.txt" | grep -vE "Access denied finding property" > "$out/logcat-filtered.txt" || true
  echo "---- probe output + crashes"
  grep -E " DOTNET |F libc|F DEBUG|AndroidRuntime|UNHANDLED" "$out/logcat-full.txt" | grep -vE "F DEBUG +: +(r[0-9]|x[0-9]|rax|rbx|rcx|rdx|r8 |r12|rdi|rbp|lr |sp |pc )" || true
  adb pull "/sdcard/Android/data/$PKG/files/" "$out/files" >/dev/null 2>&1 || true
  for f in "$out"/files/*.txt; do [ -f "$f" ] && { echo "---- $f"; cat "$f"; }; done
done
exit 0
