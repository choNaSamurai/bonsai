# UI Implementation (Unity uGUI)

> Власник: **UI/UX Developer** · Статус: Draft v0.1 · 2026-10-03
> Візуальна мова, токени, типографіка, психологія — без змін з [DESIGN_SYSTEM_AND_UI_SPEC (reference)](../reference/flutter-v3.2/DESIGN_SYSTEM_AND_UI_SPEC.md). Цей документ — як це зібрати в Unity (ADR-0006).

## 1. Мапінг компонентів Flutter → Unity

| Компонент (DS §5) | Unity-реалізація | Prefab |
|---|---|---|
| `TreeHUD` | Canvas `HUD` (static), TMP-тексти, `CanvasGroup` для fade-on-idle (5 с → 600 мс) | `UI/TreeHUD.prefab` |
| `BottomToolDock` | Canvas `Dock`, `HorizontalLayoutGroup`, 4 `ToolButton` (Idle/Active/Disabled/Pressed), toggle-to-deselect | `UI/BottomToolDock.prefab` |
| `PrunerBladeOverlay` | **Не UI**: `TrailRenderer`/процедурна стрічка у world-space шарі `FX` (Technical Artist), керується `PrunerTool` | `FX/PrunerTrail.prefab` |
| `TimelapseSheet` | Bottom sheet (власний `DraggableSheet` на `ScrollRect`-подібній логіці), слайдер-«бамбук» з hitbox ≥ 48 dp | `UI/TimelapseSheet.prefab` |
| `ZenBreatheOverlay` | Canvas `Overlay`, кільце — Image з radial-gradient шейдером, підписи TMP; дихання крони — параметр шейдера дерева | `UI/ZenOverlay.prefab` |
| `PolaroidCard` | Офскрін-рендер у RenderTexture (камера знімка + UI-композиція) → PNG → NativeShare | `UI/PolaroidCard.prefab` |
| Settings / Restore Purchases | Модальна шторка | `UI/SettingsSheet.prefab` |

## 2. Токени

- Кольори DS §3 → `UiTokens` ScriptableObject (+ `OrganicTokens`, `CircadianTokens`). Жодних хардкод-кольорів у префабах — лише через `TokenColorBinder`.
- Типографіка DS §4.2 → TMP Font Asset `ZenMaruGothic-SDF` (static, subset) + `TMP_Style`-и: `Display`, `H1`, `H2`, `Body`, `Caption`, `Mono`.
- Табулярні цифри: окремий стиль `Mono` з `<mspace=0.6em>` для лічильників (вік, таймер).

## 3. Layout і ергономіка

- Canvas Scaler: Scale With Screen Size, 390×844, match 0.5.
- `SafeAreaFitter` на кореневих панелях; док: `safeArea.bottom + 16 dp`.
- Кожна кнопка ≥ 48×48 dp (прозорий hit-padding), іконка 24×24 dp.
- Frosted glass: `GlassPanel` матеріал (blur Camera Sorting Layer Texture); fallback `#232724EB` при `QualityProfile.LowEnd`.

## 4. Анімації

- Тільки PrimeTween; криві `easeOutCubic` та органічний затухаючий синус (DS §7.1). Заборонено bounce/elastic-in.
- Перемикання інструменту: 180 мс `easeOutCubic`; натиск: scale 0.92 + `selection` хаптика.

## 5. Іконки

- Line-art 1.5 pt, SVG → спрайт-атлас `UI_Icons`. Емоджі в UI заборонені (DS §5.7).
- Набір MVP: hasami (секатор), copper-wire, hishaku/лійка, пульверизатор, лотос, шестерня, сонце↔місяць (морф), hanko.

## 6. Локалізація

- `com.unity.localization`, String Tables `UI`, `Onboarding`, `Store`. Мови MVP: en (+ ja за умови вичитки носієм); uk — v1.1 (див. GDD §10.6).
