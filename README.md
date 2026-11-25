# TMLDroid

> Неофициальный Android-хост для **tModLoader** (совместим с Terraria®).
> Статус: **ранний PoC**. Играть пока нельзя.

TMLDroid — попытка запустить **немодифицированный** `tModLoader.dll` и моды к нему
(в первую очередь Calamity) на Android-устройствах arm64, а позже — играть в мультиплеере
с ПК-игроками на той же сборке tModLoader.

## Важно: что проект НЕ содержит

- Ни одного файла Terraria (код, ассеты, музыка) и ни одного файла tModLoader.
- APK не запускается без ваших собственных файлов: вы копируете папку tModLoader
  и папку `Content` из **легально купленной** копии Terraria (Steam/GOG).
- Проект не связан с Re-Logic и командой tModLoader и не одобрен ими.
  Terraria® — товарный знак Re-Logic, Inc.

## Как это устроено (коротко)

```
L0 android-host   Activity, импорт файлов, жизненный цикл
L1 runtime        .NET for Android + нативки arm64 (SDL2, FNA3D, FAudio)
L2 shim           заглушка Steam, пути, ввод текста, сеть
L3 core           tModLoader.dll без изменений + патчи MonoMod в рантайме
L4 mobile-ux      «системный мод»: тач-управление, курсор, масштаб UI
L5 net            свой транспорт поверх стандартного протокола tML
```

Подробнее: [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md), план: [docs/ROADMAP.md](docs/ROADMAP.md),
технические находки: [docs/RESEARCH.md](docs/RESEARCH.md).

## Что уже есть

| Часть | Где | Статус |
|---|---|---|
| Тест хуков MonoMod (desktop + Android APK) | `probes/MonoModProbe` | написан, ждёт первого прогона в CI и на телефоне |
| Сборка нативок arm64-v8a (SDL2, FNA3D, FAudio, Theorafile, замена libgcc_s) | `native/` | скрипт + CI |
| CI | `.github/workflows` | сборка нативок, desktop-тест на x64 и arm64 Linux, сборка APK |

## Как проверить на телефоне (без установки чего-либо на ПК)

1. Откройте вкладку **Actions** → workflow **probe** → последний успешный запуск.
2. Скачайте артефакт `MonoModProbe-apk`, распакуйте, установите `.apk` на телефон
   (нужно разрешить установку из неизвестных источников).
3. Запустите **MonoMod Probe**, нажмите **Run all**.
4. Нажмите **Share log** и приложите лог к issue «Probe results».

## Сборка локально

Нужно: .NET SDK 10, `dotnet workload install android`, Android SDK + NDK r27+, CMake, Ninja.

```bash
# нативки
ANDROID_NDK_HOME=/path/to/ndk ./native/build-android-natives.sh
# desktop-тест
dotnet run -c Release --project probes/MonoModProbe/Desktop
# APK теста
dotnet build -c Release probes/MonoModProbe/Android
```

## Лицензия

Код этого репозитория — MIT (см. [LICENSE](LICENSE)).
Сторонние компоненты и их лицензии: [THIRD_PARTY.md](THIRD_PARTY.md).
