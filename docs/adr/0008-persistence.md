# ADR-0008: AtomicFileStore з SHA-256 envelope, `.bak` і Poison-Pill захистом

- **Статус**: Proposed
- **Дата**: 2026-10-03
- **Автор**: Unity Architect
- **Пов'язані задачі**: PA-205

## Контекст
ТЗ §5.3 / Архітектура §2.3: атомарний запис, захист від ENOSPC, bit-rot, урізаних файлів, «отруєного» перезапису бекапу.

## Рішення
- `AtomicFileStore` (Infrastructure, реалізує порт `ISaveStore`):
  1. `TreeSnapshot` + `SoilState` + метадані → JSON (Newtonsoft, `schemaVersion`) → UTF-8 → SHA-256 → `[64 hex][json]`.
  2. Запис у `tree_state.json.tmp` (`FileStream`, `Flush(true)`).
  3. `File.Replace(tmp, primary, bak)`; якщо primary ще не існує — `File.Move`. Fallback на `IOException` — `Copy + Delete`.
  4. Читання: `length <= 64` → corrupted; хеш не збігся → `.bak`; успіх з `.bak` → **негайний перезапис primary**; битий файл → `quarantine/<timestamp>.json`.
- Налаштування — `PlayerPrefs` (лише примітиви).
- Міграції схеми — `ISaveMigrator` по ланцюжку версій.
- Сейв викликається: після кожної дії (debounce 2 с) та синхронно в `OnApplicationPause(true)`.

## Розглянуті альтернативи
| Варіант | Плюси | Мінуси |
|---|---|---|
| `PlayerPrefs` для всього | Просто | Ліміти розміру, не атомарно, немає бекапу |
| SQLite | Транзакції | +розмір, нативні бібліотеки, надмірно |
| Easy Save (asset) | Готово | Платний, менше контролю |

## Наслідки
- + Повний паритет з вимогами ТЗ; логіка тестується EditMode-тестами з тимчасовою текою (обрізані файли, битий хеш, відсутній `.bak`).
- − `File.Replace` на Android/iOS поводиться як rename у межах одного тому — підтверджується тестом на пристрої (PA-205).
