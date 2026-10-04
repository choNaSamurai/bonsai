# ADR-0006: UI на uGUI Canvas + TextMeshPro; твіни — PrimeTween

- **Статус**: Proposed
- **Дата**: 2026-10-03 (rev.2 2026-10-05: локалізація, PA-101b)
- **Автор**: Unity Architect, UI/UX Developer
- **Пов'язані задачі**: PA-106, PA-107, PA-303, PA-304, PA-305, PA-101b

## Контекст
UI мінімальний: TreeHUD, BottomToolDock, PrunerBladeOverlay, TimelapseSheet, ZenBreatheOverlay, PolaroidCard, Settings, Store. Frosted glass, safe area, 48×48 dp hitbox, табулярні цифри, Emoji-Free стандарт, затухання HUD.

## Рішення
- **uGUI** (Screen Space – Overlay Canvas, Canvas Scaler «Scale With Screen Size», референс 390×844, match 0.5) + `SafeAreaFitter`.
- **TextMeshPro** зі static SDF-атласом ZenMaru Gothic (subset), `tnum` через моноширинні цифри у TMP (`<mspace>` або окремий font asset для цифр).
- Розділення Canvas-ів: статичний HUD / динамічні оверлеї — щоб не перебудовувати весь Canvas.
- **PrimeTween** для всіх UI-анімацій (zero-alloc), криві з DESIGN_SYSTEM §7.1 (без bounce).
- Іконки — векторні SVG → спрайти (через `com.unity.vectorgraphics` на етапі імпорту або експорт у PNG-атлас), без емоджі.
- **Локалізація (rev.2, рішення власника 2026-10-05)** — власні таблиці рядків замість `com.unity.localization` (пакет прибрано в PA-101b разом з Addressables):
  - Джерело — CSV/TSV у репо (`key, en, ja, uk…`), Editor-імпортер → `StringTable` ScriptableObject на мову: масив `string[]`, індекс = `int`-ключ із згенерованого `static class L` (`L.Hud_Age`, …) — без пошуку за рядком у рантаймі, 0 алокацій при зверненні.
  - Порт `ILocalizer` (Application: `string Get(int key)`, `ReadOnlyReactiveProperty<LocaleId> Current`), реалізація в Presentation; `LocalizedText` (компонент на TMP) перепідписується на зміну мови. Мова — `PlayerPrefs`, за замовчуванням з `Application.systemLanguage`.
  - Плюралізація — ручні ключі `_one/_other` (ja/en їх майже не потребують; uk — правило CLDR `one/few/many` у `PluralRules`, v1.1); форматування чисел/дат — `CultureInfo` мови, без Smart Strings.
  - Шрифти: ZenMaru субсет (static SDF) + TMP fallback-атлас для ja (subset з таблиці рядків + static) — charset генерується з CSV тим самим імпортером.
  - Тест: усі ключі мають непорожній `en`; відсутній переклад → fallback на `en` + warning у Editor.

## Розглянуті альтернативи
| Варіант | Плюси | Мінуси |
|---|---|---|
| UI Toolkit | Сучасний, USS-стилі | Слабша підтримка кастомних шейдерів/blur, world-space анімацій; менше прикладів для мобільних ігор |
| DOTween | Популярний | Алокації, закрита Pro-версія |
| Unity Localization (`com.unity.localization`) | Smart Strings, плюралізація, Google Sheets, Editor-UI | Тягне Addressables (+ build-пайплайн контенту) і Newtonsoft; −0.65 МБ Android без нього і передумова прибрати Newtonsoft (PA-003b); для ~200–400 рядків і 2–3 мов надмірно |

## Наслідки
- + Звичний, добре задокументований стек для мобільних ігор; легко робити blur-підкладки шейдером.
- − Потрібна дисципліна з розбиттям Canvas для продуктивності.
- − Локалізація — наш код (імпортер, компонент, плюралізація uk) ~2–3 SP у PA-107; немає Editor-UI перекладача — переклади живуть у CSV.
- Перевірити: обсяг рядків і мови MVP з GDD §10.6 (game-designer); якщо знадобляться Smart Strings/синхронізація з Google Sheets — новий ADR із замірами розміру.
