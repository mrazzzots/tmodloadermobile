#!/usr/bin/env bash
# Runs the FNA probe on the emulator, collects logcat and a screenshot, and checks that
# (1) managed code reached the game loop and (2) the screen is not black.
set -uo pipefail
PKG=io.github.tmldroid.fnaprobe
ACT="$PKG/$PKG.FnaActivity"
OUT=fna-emulator-out
mkdir -p "$OUT"

apk="$(ls apk/*.apk | head -n1)"
adb install -r "$apk" || { echo "install failed"; exit 1; }
adb logcat -c
adb shell am start -W -n "$ACT" --ez autorun true
sleep 40
adb exec-out screencap -p > "$OUT/screen.png" || true
adb logcat -d -v threadtime > "$OUT/logcat-full.txt"
pid="$(adb shell pidof "$PKG" | tr -d '\r')"
echo "process: ${pid:+alive (pid $pid)}${pid:-NOT running}"

echo "---- FNA probe / SDL / runtime / crashes"
grep -E "FNAPROBE|TMLDroid|SDL|FNA3D|FAudio| DOTNET |monodroid.*(E|F) |F libc|F DEBUG|AndroidRuntime|UNHANDLED" "$OUT/logcat-full.txt" \
  | grep -vE "F DEBUG +: +(r[0-9]|x[0-9]|rax|rbx|rcx|rdx|r8 |r12|rdi|rbp|lr |sp |pc )" | head -n 250

python3 - "$OUT/screen.png" <<'PY'
import sys, zlib, struct
data = open(sys.argv[1], 'rb').read()
if not data.startswith(b'\x89PNG'):
    print("SCREEN: no valid screenshot"); sys.exit(0)
pos, w, h, idat, ctype = 8, 0, 0, b'', 0
while pos < len(data):
    ln, typ = struct.unpack('>I4s', data[pos:pos+8]); body = data[pos+8:pos+8+ln]; pos += 12 + ln
    if typ == b'IHDR': w, h, _, ctype = struct.unpack('>IIBB', body[:10])
    elif typ == b'IDAT': idat += body
raw = zlib.decompress(idat); bpp = 4 if ctype == 6 else 3; stride = w * bpp
prev = bytearray(stride); nonblack = total = 0; rsum = gsum = bsum = 0; i = 0
for y in range(h):
    f = raw[i]; line = bytearray(raw[i+1:i+1+stride]); i += 1 + stride
    for x in range(stride):
        a = line[x-bpp] if x >= bpp else 0; b = prev[x]; c = prev[x-bpp] if x >= bpp else 0
        if f == 1: line[x] = (line[x] + a) & 255
        elif f == 2: line[x] = (line[x] + b) & 255
        elif f == 3: line[x] = (line[x] + (a + b) // 2) & 255
        elif f == 4:
            p = a + b - c; pa, pb, pc = abs(p-a), abs(p-b), abs(p-c)
            line[x] = (line[x] + (a if pa <= pb and pa <= pc else b if pb <= pc else c)) & 255
    prev = line
    if y % 4: continue
    for x in range(0, w, 4):
        r, g, bl = line[x*bpp], line[x*bpp+1], line[x*bpp+2]
        total += 1; rsum += r; gsum += g; bsum += bl
        if r + g + bl > 30: nonblack += 1
print(f"SCREEN: {w}x{h}, non-black {100*nonblack/max(total,1):.1f}%, mean rgb ({rsum//max(total,1)}, {gsum//max(total,1)}, {bsum//max(total,1)})")
PY
exit 0
