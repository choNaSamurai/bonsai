# ADR-0006: UI на uGUI Canvas + TextMeshPro; твіни — PrimeTween

- **Статус**: Proposed
- **Дата**: 2026-10-03
- **Автор**: Unity Architect, UI/UX Developer
- **Пов'язані задачі**: PA-106, PA-303, PA-304, PA-305

## Контекст
UI мінімальний: TreeHUD, BottomToolDock, PrunerBladeOverlay, TimelapseSheet, ZenBreatheOverlay, PolaroidCard, Settings, Store. Frosted glass, safe area, 48×48 dp hitbox, табулярні цифри, Emoji-Free стандарт, затухання HUD.

## Рішення
- **uGUI** (Screen Space – Overlay Canvas, Canvas Scaler «Scale With Screen Size», референс 390×844, match 0.5) + `SafeAreaFitter`.
- **TextMeshPro** зі static SDF-атласом ZenMaru Gothic (subset), `tnum` через моноширинні цифри у TMP (`<mspace>` або окремий font asset для цифр).
- Розділення Canvas-ів: статичний HUD / динамічні оверлеї — щоб не перебудовувати весь Canvas.
- **PrimeTween** для всіх UI-анімацій (zero-alloc), криві з DESIGN_SYSTEM §7.1 (без bounce).
- Іконки — векторні SVG → спрайти (через `com.unity.vectorgraphics` на етапі імпорту або експорт у PNG-атлас), без емоджі.

## Розглянуті альтернативи
| Варіант | Плюси | Мінуси |
|---|---|---|
| UI Toolkit | Сучасний, USS-стилі | Слабша підтримка кастомних шейдерів/blur, world-space анімацій; менше прикладів для мобільних ігор |
| DOTween | Популярний | Алокації, закрита Pro-версія |

## Наслідки
- + Звичний, добре задокументований стек для мобільних ігор; легко робити blur-підкладки шейдером.
- − Потрібна дисципліна з розбиттям Canvas для продуктивності.
