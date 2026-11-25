# Что делать дальше (для владельца репозитория)

## 1. Выложить на GitHub

1. Создайте на github.com пустой **публичный** репозиторий `TMLDroid`, без README.
   Публичный нужен ради бесплатных arm64-раннеров GitHub Actions.
2. В папке проекта выполните:

```bash
git init -b main
git add .
git commit -m "Initial PoC: MonoMod probe, native build, CI"
git remote add origin https://github.com/<ваш-логин>/TMLDroid.git
git push -u origin main
```

## 2. Дождаться CI

Вкладка **Actions** → workflow **probe**. Что означают результаты:

| Job | Что значит зелёный |
|---|---|
| `desktop (ubuntu-24.04-arm, 25.3.2)` | хуки MonoMod работают на arm64 — тест корректен |
| `natives / android-arm64` | SDL2, FNA3D, FAudio и замена libgcc_s собираются под Android |
| `android-apk` | APK теста собран, нативки лежат внутри |

Если какой-то job красный, скопируйте лог упавшего шага, будем чинить.

## 3. Прогнать тест на телефоне

1. В запуске **probe** скачайте артефакт `MonoModProbe-apk`, распакуйте zip, установите APK.
2. Откройте **MonoMod Probe** → **Run all (fix)** → дождитесь `Done` → **Share log**.
3. Смахните приложение из «Недавних», откройте снова → **Run as is** → **Share log**.
4. Создайте issue по шаблону **Probe results** и вставьте оба лога.

## Как читать результат

- `stock_platform_triple` = FAIL с `NotImplementedException` — **ожидаемо**: это и есть
  проблема MonoMod на Android.
- Все `hook_*`, `ilhook_*`, `modlike_*`, `stress_*` = PASS в режиме fix — **проект
  реален**, переходим к шагу 1.3 (FNA на телефоне).
- Приложение закрылось само — это нативный краш. Пришлите файлы из
  `Android/data/io.github.tmldroid.monomodprobe/files/`.
