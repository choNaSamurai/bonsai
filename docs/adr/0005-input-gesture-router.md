# ADR-0005: Input System (EnhancedTouch) + глобальний GestureRouter

- **Статус**: Proposed
- **Дата**: 2026-10-03
- **Автор**: Unity Architect, Gameplay Developer
- **Пов'язані задачі**: PA-104

## Контекст
ТЗ §4.2–4.3: 2 пальці будь-де (навіть над доком) скасовують інструмент; 2 пальці завжди pan/zoom; deadzones з Ingress/Egress; Slash-to-Commit; pinch-window 35–45 мс; snapping у screen space з гістерезисом.

## Рішення
- `com.unity.inputsystem`, `EnhancedTouchSupport.Enable()`; старий Input Manager вимкнено (`Active Input Handling = Input System Package`).
- Один `GestureRouter` (Presentation) читає `Touch.activeTouches` кожен кадр (без алокацій) і роутить у: `CameraRig` (≥2 пальці), активний `IToolGesture` (1 палець), або UI.
- UI-кнопки (док, HUD) працюють через `EventSystem` + `InputSystemUIInputModule`; але `GestureRouter` **бачить дотики над UI теж** і застосовує правило abort.
- Усі пороги — у `InputConfig` (ScriptableObject) у **dp**; `ScreenMetrics` переводить у px.
- Логіку порогів (slash, pinch-window, hysteresis, deadzones) винесено у чисті класи без Unity → EditMode-тести; інтеграційні — PlayMode з `InputTestFixture`.

## Розглянуті альтернативи
| Варіант | Плюси | Мінуси |
|---|---|---|
| Legacy `Input.touches` | Просто | Deprecated-шлях, гірша тестованість |
| EventSystem drag handlers | Звично для UI | Не бачить мультитач глобально, правило abort не реалізується |
| Lean Touch (asset) | Готові жести | Стороння залежність, менше контролю над порогами |

## Наслідки
- + Повний контроль і тестованість жестів через `InputTestFixture`.
- − Власна реалізація pinch/pan — ~1 день роботи.
