# ADR-0007: Native-плагіни — хаптика, аудіосесія, віджети, геолокація

- **Статус**: Proposed
- **Дата**: 2026-10-03
- **Автор**: Unity Architect
- **Пов'язані задачі**: PA-002, PA-004, PA-201, PA-301, PA-302, PA-401A, PA-401B

## Контекст
Flutter-версія спиралась на плагіни (`home_widget`, `audio_session`, `HapticFeedback`, `geolocator`). У Unity аналогів «з коробки» немає або вони обмежені.

## Рішення
1. **Хаптика**: власний тонкий плагін — iOS `UIImpactFeedbackGenerator`/`UISelectionFeedbackGenerator` (`Plugins/iOS/Haptics.mm`), Android `VibrationEffect.createPredefined` / `HapticFeedbackConstants` (`.androidlib`). Тротлінг 70 мс — у C# (`HapticsThrottler`).
2. **Аудіосесія**: плагін `AudioSessionBridge` — iOS interruption + route change; Android audio focus + `ACTION_AUDIO_BECOMING_NOISY`. Події → C# через `UnitySendMessage`/колбеки.
3. **iOS-віджет**: WidgetKit extension на Swift у `Assets/Plugins/iOS/Widget~/`, підключення в `PostProcessBuild` (PBXProject + App Group entitlements). Дані: PNG + JSON (вологість, швидкість висихання) у спільному контейнері.
4. **Android-віджет**: **класичний `AppWidgetProvider` + `RemoteViews`** у `.androidlib` замість Jetpack Glance. Glance потребує Kotlin Compose compiler у Gradle-збірці Unity — високий ризик і зайвий розмір. Зображення — через `FileProvider` `content://` URI.
5. **Геолокація**: **пропозиція — не запитувати дозвіл взагалі на MVP**: IP-геолокація (місто) + ручний вибір + дефолт Кіото. Якщо власник обере coarse-permission — `Input.location` з маніфест-патчем, що прибирає `ACCESS_FINE_LOCATION`.

## Розглянуті альтернативи
| Варіант | Плюси | Мінуси |
|---|---|---|
| Nice Vibrations (Lofelt, open-source) | Готово, багато патернів | Великий, частково застарілий; потрібні лише 3 типи імпульсів |
| Jetpack Glance | Сучасний API (як в ТЗ) | Compose-тулчейн у Unity Gradle, розмір, ризик збірки |
| FMOD для аудіо | Низька латентність, interruptions | Ліцензія, +розмір, складність |

## Наслідки
- + Мінімальний розмір, повний контроль.
- − Native-код на Swift/ObjC/Kotlin — потрібні спайки **PA-002** (віджети обох платформ у Unity-збірці) і **PA-004** (вимір аудіо-латентності) до Спринту 4/3 відповідно.
