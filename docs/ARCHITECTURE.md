# Архитектура

## Принципы

1. **tModLoader не форкаем.** `tModLoader.dll` пользователя грузится как есть, все
   изменения вносятся патчами MonoMod в рантайме (`src/Patches`, позже). Так переход
   1.4.4 → 1.4.5 → 1.4.6 обходится дёшево, и мы не распространяем чужой код.
2. **Мобильная логика только на клиенте.** Никаких своих сетевых пакетов в MVP.
   Тогда мультиплеер с ПК-клиентом той же сборки tML совместим автоматически.
3. **Платформенные вещи — за интерфейсами** (L2), чтобы 1.4.6 с новым сетевым слоем
   и платформенными сервисами не потребовал переписывать хост.

## Слои

| Слой | Ответственность | Будущие папки |
|---|---|---|
| L0 Android Host | Activity (на базе SDLActivity), SAF-импорт, lifecycle, настройки | `src/Host.Android` |
| L1 Runtime | .NET for Android (Mono), нативки arm64, фиксы MonoMod | `native/`, `src/Runtime` |
| L2 Platform Shim | заглушка Steamworks, режим без Steam, пути, IME, `ISocket` | `src/Shim` |
| L3 Core | загрузка `tModLoader.dll`, патчи в рантайме | `src/Patches` |
| L4 Mobile UX | «системный мод»: тач-оверлей → эмуляция геймпада, виртуальный курсор | `src/MobileUX` |
| L5 Net | TCP/relay-транспорт, манифест совместимости | `src/Net` |

## Порядок запуска (целевой)

```
Activity.onCreate
 └─ SDL init (libSDL2.so)
     └─ Managed entry
         ├─ MonoModAndroidFix.Apply()      ← до любого обращения к MonoMod
         ├─ preload libmm_gcc_s_shim.so
         ├─ env: FNA3D_OPENGL_FORCE_ES3, рабочая папка, -savedirectory
         ├─ SteamStub / non-Steam path
         └─ Assembly.Load(tModLoader.dll) → Main(args)
```

## Задел на кроссплей

- **Манифест совместимости** = версия tML + хэш `tModLoader.dll` + список
  `(мод, версия, хэш .tmod)`. Показывается в UI и логах с первого дня.
- **Моды без Workshop**: ручной импорт `.tmod` и штатная загрузка модов с сервера tML.
- **Идентичность**: стабильный UUID клиента вместо SteamID.
- **NAT**: выделенный Linux-сервер tML → прямой IP/LAN → туннели (Tailscale, ZeroTier,
  playit) → свой relay. Steam P2P на Android недоступен.
