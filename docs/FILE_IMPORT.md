# Какие файлы нужны и как их подготовить

TMLDroid не содержит файлов Terraria и tModLoader. Их нужно взять из **своей купленной**
копии игры (Steam или GOG) и перенести на телефон одним zip-архивом. Не выкладывайте этот
архив никуда публично.

Проверено на tModLoader **v2026.9.3.1** (stable, Terraria 1.4.4.9).

## Структура архива `TMLDroid.zip`

```
TMLDroid/
├─ tModLoader/                 ← содержимое папки tModLoader из Steam
│   ├─ tModLoader.dll
│   ├─ tModLoader.deps.json
│   ├─ tModLoader.runtimeconfig.json
│   ├─ tModLoader.pdb          (по желанию: понятные стектрейсы в логах)
│   ├─ Content/
│   └─ Libraries/
├─ Terraria/
│   ├─ Terraria.exe            ← нужен для проверки, что игра куплена (хэш, как в GOG-версии tML)
│   └─ Content/                ← ассеты игры
└─ Mods/                       (по желанию) *.tmod
```

**Не копировать:**
- `tModLoader/dotnet/` — рантайм .NET для Windows, на Android не нужен;
- `tModLoader/tModLoader-Logs/`, `tModPorter/`, `DedicatedServerUtils/`, `*.bat`, `*.sh`;
- из Terraria — всё, кроме `Terraria.exe` и `Content/`. Особенно **не класть `steam_api.dll`**
  рядом с `Terraria.exe`: иначе tModLoader решит, что это Steam-запуск, и попробует запустить Steam.

## Где лежат папки (Windows, Steam)

Steam → правый клик по игре → **Управление → Просмотреть локальные файлы**.
Обычно `...\steamapps\common\tModLoader` и `...\steamapps\common\Terraria`.

Моды из Мастерской: `...\steamapps\workshop\content\1281930\<id>\<версия>\*.tmod`
(появляются после подписки в Steam и первого запуска tModLoader).

## Почему нужен `Terraria.exe`

На ПК без Steam tModLoader (`ModLoader/Engine/InstallVerifier.cs`) проверяет установку:
находит `Terraria.exe` рядом и сверяет MD5 с известными хэшами Steam/GOG-сборок. TMLDroid
сохраняет эту проверку, а не обходит её.
