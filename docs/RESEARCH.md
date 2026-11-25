# Технические находки

Проверено по исходникам 25.11.2026. Ссылки на коммиты нужны, чтобы перепроверять
после обновлений upstream.

## tModLoader

- Ветки: `stable`, `preview`, `1.4.4`, `1.4.5` (основная ветка разработки).
  Стабильный tML работает на Terraria 1.4.4.9. Порт на 1.4.5 ещё не завершён,
  tML для 1.4.6 не существует.
- Целевая платформа: **.NET 8** (`tModLoader.runtimeconfig.json`).
- Ключевые пакеты в ветке `stable` (`patches/tModLoader/Terraria/Terraria.csproj.patch`):
  - `MonoMod.RuntimeDetour` **25.3.2**
  - `Steamworks.NET.AnyCPU` 2025.162.4
  - `System.Reflection.MetadataLoadContext` 8.0.0, `Hjson`, `DotNetZip`.
- `ReLogic.dll` встроен в `tModLoader.dll` как EmbeddedResource.
- Нативные библиотеки tML (`patches/TerrariaNetCore/Terraria/Libraries/Native/<OS>`):
  SDL2, FNA3D, FAudio, theorafile, nfd (диалоги выбора файла). Для Linux-arm64 они
  есть, но собраны под glibc, **на Android (bionic) не подходят**: пересобираем через NDK.

## FNA (форк tModLoader)

- `github.com/tModLoader/FNA`, закреплён в tML stable на коммите `2d16be6c`.
- Использует **SDL2** (SDL2-CS), не SDL3.
- Подмодули: FNA3D `a5b6c07b` (окт. 2023), FAudio `221bfe43` (сент. 2022),
  Theorafile `9cddd3fb`, SDL2-CS `e4afbb84`.
- `FNADllMap`: на Android `OperatingSystem.IsLinux()` даёт false, платформа
  определяется как `unknown`, и библиотеки грузятся по базовым именам. Значит, в APK
  должны лежать **`libSDL2.so`, `libFNA3D.so`, `libFAudio.so`, `libtheorafile.so`**.
- FNA3D (OpenGL-драйвер) **сам включает GLES3**, если `SDL_GetPlatform() == "Android"`.
  Принудительно можно задать хинтом `FNA3D_OPENGL_FORCE_ES3`.
- У CMakeLists FNA3D и FAudio `cmake_minimum_required(2.8.12)`, поэтому CMake 4.x
  требует `-DCMAKE_POLICY_VERSION_MINIMUM=3.5`.

## MonoMod — главный риск

- Поддержка **ARM64** появилась в MonoMod.Core 1.3.0 / RuntimeDetour **25.3.0**
  (коммит `e8df8ef`, «Minor version bump for arm64», август 2025). tML stable (25.3.2)
  её уже содержит.
- **Android не поддерживается**:
  `PlatformTriple.CreateCurrentSystem()` → `OSKind.Android => throw new NotImplementedException()`.
  Android определяется в `PlatformDetection` так: Linux + есть `/data` и `/system/build.prop`.
- `LinuxSystem` помечен `internal`. Вызывает libc через `DllImport("libc")`, на bionic
  это работает.
- На arm64 `LinuxSystem` загружает нативный хелпер `exhelper_linux_arm64.so`
  (EmbeddedResource сборки MonoMod.Core):
  - по умолчанию кладёт его в `/tmp/mm-exhelper.so.XXXXXX`. На Android `/tmp` нет.
    Путь меняется публичным свитчем `MonoMod.Switches.SetSwitchValue("HelperDropPath", dir)`
    или переменной окружения `MONOMOD_HelperDropPath`;
  - хелпер собран с `DT_NEEDED libgcc_s.so.1` и импортирует
    `_Unwind_RaiseException`, `_Unwind_GetLanguageSpecificData`, `_Unwind_SetIP`,
    `_Unwind_SetGR`. На Android `libgcc_s` нет.
- `PlatformDetection` кэширует ОС в приватных статических полях `os`, `arch`,
  `platInitState`. Есть и публичный `PlatformTriple.SetPlatformTriple(...)`.

- В NuGet-пакете MonoMod есть MSBuild-проверка: сборка под `net10.0-android` падает с ошибкой
  «MonoMod.Core and MonoMod.RuntimeDetour do not support the net10.0-android runtime».
  Проверка срабатывает на любой TFM новее .NET 9 (это про CoreCLR; на Android работает Mono).
  Текст ошибки предлагает `MonoMod_CheckTargetRuntime=false`, но реально читается свойство
  **`MonoMod_ReallySkipCheckTargetRuntime=true`** (`tools/MonoMod.CheckTargetRuntime.targets`).
- Хуки на generic-методы MonoMod не поддерживает ни на одной платформе
  («generic hooks are not supported»), это общее ограничение tML, а не Android.

### Результаты CI (desktop, MonoMod 25.3.2)

Linux x64 и Linux arm64 (CoreCLR): 11/11 обязательных тестов проходят — детуры, IL-хуки,
исключения через хук, HFA/retbuf-структуры, загрузка сборки и хук в ней, 300 хуков ≈0.4 с.
Collectible ALC выгружается.

### Гипотеза обхода (проверяет `probes/MonoModProbe`)

1. До первого обращения к MonoMod выставить через рефлексию
   `PlatformDetection.os = OSKind.Linux`, `platInitState = 1`.
2. Выставить `HelperDropPath` = `<cacheDir>/monomod`.
3. Заранее загрузить `libmm_gcc_s_shim.so`. Это наша библиотека с
   `SONAME libgcc_s.so.1`, внутри статически слинкован libunwind из NDK. Загрузчик
   bionic сопоставляет `DT_NEEDED` с уже загруженными библиотеками по soname.
4. Если что-то из этого не сработает, запасной вариант — собственный форк
   MonoMod.Core с `AndroidSystem` и PR в upstream.

## Рантайм

- .NET for Android (`net10.0-android`) по умолчанию использует **Mono**, JIT разрешён.
  В MonoMod есть `MonoRuntime`.
- Collectible `AssemblyLoadContext` (выгрузка модов) на Mono ограничен. Тест проверяет
  загрузку и выгрузку. Если выгрузка не работает, смена набора модов будет требовать
  перезапуска приложения.

## Первый прогон на устройстве (vivo V2403A, Snapdragon 8 Gen 3, Android 16, .NET 10.0.15 Mono)

- Стоковый MonoMod: `NotImplementedException` для Android — подтверждено.
- Подмена ОС на Linux работает (`PlatformDetection.OS: Android -> Linux`), LinuxSystem создаётся.
- Дальше падает `MonoMod.Core.Interop.Unix`: P/Invoke `__errno_location` (имя из glibc,
  в bionic — `__errno`). Исправление: `NativeLibrary.SetDllImportResolver` для сборок MonoMod
  отправляет `"libc"` в нашу библиотеку `libmm_gcc_s_shim.so`, которая экспортирует
  `__errno_location`, а остальное (`mmap`, `mprotect`, `dlopen`…) отдаёт через свои DT_NEEDED.
- `System.loadLibrary("mm_gcc_s_shim")` не нашёл библиотеку, хотя она в APK. Теперь она
  встраивается как ресурс сборки, записывается в `files/native/` и грузится по абсолютному пути.
- Collectible `AssemblyLoadContext` на Mono **не выгружается**: смена набора модов потребует
  перезапуска приложения (как и ожидалось).

### Второй прогон

- Библиотека совместимости грузится по пути, все символы на месте, но `SetDllImportResolver`
  для `"libc"` **не вызывается**: .NET for Android сам разрешает импорты из libc.
- Решение: `tools/MonoModAndroidPatcher` (Mono.Cecil) при сборке APK переписывает P/Invoke
  `MonoMod.Core.Interop.Unix.__errno_location` → entry point `__errno` (имя в bionic).
  Для настоящего tModLoader этот же патч нужно применять в рантайме к его `MonoMod.Core.dll`.

### Третий этап: эмулятор в CI (x86_64, Android 11, Mono)

- `mprotect` падал с EINVAL: MonoMod вызывает `sysconf(30)` (glibc `_SC_PAGESIZE`), а в bionic
  30 = `_SC_XOPEN_ENH_I18N`, правильный номер — `0x27`. Патчер теперь переписывает и это.
- С двумя патчами (`__errno`, `sysconf`) на эмуляторе проходят **все 11 обязательных тестов**:
  детуры, IL-хуки, исключения, HFA/retbuf, цепочки, хук в загруженной «мод-сборке», 300 хуков
  (~5 мс/хук на эмуляторе). Collectible ALC не выгружается (Mono).
- Осталось проверить на реальном arm64: там дополнительно нужен arm64-хелпер MonoMod
  (`libgcc_s.so.1` → наша библиотека совместимости).

## ✅ Итог шага 1.1: MonoMod работает на реальном Android arm64

vivo V2403A (Snapdragon 8 Gen 3), Android 16, .NET 10.0.15 (Mono), MonoMod.RuntimeDetour 25.3.2:
`HostTriple = (Arm64, Linux, Mono)`, **11/11 обязательных тестов PASS**, 300 хуков ≈ 1.46 с
(≈ 4.9 мс/хук; для Calamity с сотнями хуков это секунды при загрузке — приемлемо).

Необходимый набор обходов (всё это понадобится при запуске настоящего tModLoader):
1. Подмена `PlatformDetection.OS` → Linux до первого обращения к MonoMod.
2. `Switches.HelperDropPath` → папка кэша приложения.
3. Библиотека совместимости `libmm_gcc_s_shim.so` (SONAME `libgcc_s.so.1`, LLVM libunwind),
   загруженная по абсолютному пути до создания платформы MonoMod.
4. Патч `MonoMod.Core.dll` (Cecil): `__errno_location` → `__errno`, `sysconf(30)` → `sysconf(0x27)`.
   Для tML патч применяется к его собственной копии `MonoMod.Core.dll` при загрузке.
5. Collectible ALC на Mono не выгружается → смена модов = перезапуск приложения.

## Шаг 1.3: FNA на Android (эмулятор CI, x86_64, SwiftShader GLES 3.0)

Работает: запуск через SDLActivity → `libmain.so` (`SDL_main`) → `[UnmanagedCallersOnly]` C#;
.NET и Java используют один экземпляр `libSDL2.so` (`SDL_AndroidGetJNIEnv` ≠ 0);
FNA3D OpenGL ES 3.0, MojoShader `glsles`; ~60 FPS; FAudio играет звук.

Самопроверка (render target → read-back):

| тест | эмулятор |
|---|---|
| Clear | ✅ |
| BasicEffect, user primitives (цвет / текстура) | ✅ / ✅ |
| VertexBuffer+IndexBuffer → `DrawIndexedPrimitives` (static / dynamic) | ❌ |
| SpriteBatch (внутри — индексная отрисовка) | ❌ |

Гипотеза: GL-транслятор эмулятора заявляет `OES_draw_elements_base_vertex`, но не выполняет
`glDrawRangeElementsBaseVertex`. Нужна проверка на реальном Adreno.
