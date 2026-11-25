# build/

- `MonoModAndroid.targets` — packages a MonoMod.Core.dll patched for Android/bionic
  (see `tools/MonoModAndroidPatcher`). Imported by every Android project that uses MonoMod.
- `tmldroid-dev.keystore` — **public development signing key** (PKCS#12, alias `tmldroid`,
  password `tmldroid-dev`). It only exists so that CI builds of the TMLDroid app can be
  installed over each other without losing imported game files. Because it is public, anyone
  can sign an APK with it: install TMLDroid builds only from this repository's Actions/Releases.
  Real releases must use a private key stored in CI secrets.
