#!/usr/bin/env bash
# Installs the probe APK on the running emulator, starts it with autorun, collects logs.
# x86_64 emulator images (API 30+) run arm64-only apps through ARM translation, so this
# catches packaging/startup crashes; arm64 JIT/MonoMod behaviour must still be checked on a phone.
set -uo pipefail

PKG=io.github.tmldroid.monomodprobe
ACT="$PKG/$PKG.MainActivity"
OUT=emulator-out
mkdir -p "$OUT"

apk="$(ls apk/*.apk | head -n1)"
echo "Installing $apk"
adb install -r "$apk" || { echo "install failed"; exit 1; }

adb logcat -c
adb shell am start -W -n "$ACT" --ez autorun true
sleep 90

adb logcat -d -v threadtime > "$OUT/logcat-full.txt"
pid="$(adb shell pidof "$PKG" | tr -d '\r')"
echo "process pid after 90s: ${pid:-<not running: crashed or exited>}"

# Everything interesting: our stdout (DOTNET), runtime errors, Java crashes, native crashes.
grep -E "DOTNET|monodroid|mono-rt|AndroidRuntime|FATAL|DEBUG +:|libc +:|$PKG|\[PASS\]|\[FAIL\]|\[INFO\]|UNHANDLED" \
  "$OUT/logcat-full.txt" > "$OUT/logcat-filtered.txt" || true

adb pull "/sdcard/Android/data/$PKG/files/" "$OUT/files" >/dev/null 2>&1 || true
adb shell 'ls /data/tombstones 2>/dev/null' > "$OUT/tombstones.txt" 2>&1 || true

echo "================ filtered logcat (last 300 lines) ================"
tail -n 300 "$OUT/logcat-filtered.txt"
echo "================ probe log files ================"
for f in "$OUT"/files/*.txt; do [ -f "$f" ] && { echo "--- $f"; cat "$f"; }; done
exit 0
