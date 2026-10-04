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

> Оновлено 2026-10-05 (PA-101b, Unity Architect за рішенням власника): `com.unity.localization` + Addressables прибрано з проєкту (−0.65 МБ Android і передумова прибрати Newtonsoft, звіт PA-003b). Архітектура — ADR-0006 rev.2. **Це план**; реалізація — у задачі UI-кістяка (PA-107), власник розділу — UI/UX Developer.

- **Мови MVP**: en (+ ja за умови вичитки носієм); uk — v1.1 (див. GDD §10.6).
- **Таблиці** `UI`, `Onboarding`, `Store` — CSV/TSV у `Assets/_Project/Config/Localization/` (`key,en,ja,uk,comment`), UTF-8; джерело правди для перекладачів.
- **Editor-імпортер** (`Pocket Arbor/Localization/Import`): CSV → `StringTable` ScriptableObject на мову (`string[]` за int-індексом) + згенерований `static class L` з `const int` ключами (`L.Hud_AgeDays`), + charset-файл для субсету шрифтів (ZenMaru, ja fallback). Імпорт детермінований (сортування ключів), щоб diff був читабельним.
- **Рантайм**: порт `ILocalizer` (`string Get(int key)`, `ReadOnlyReactiveProperty<LocaleId> Current`); `LocalizedText` — компонент поруч із `TMP_Text`, у `OnEnable` бере рядок і підписується на зміну мови (R3, без лямбд-замикань у кадрі). Звертання — O(1) по масиву, 0 B GC.
- **Параметри**: `string.Format` з `CultureInfo` поточної мови лише при зміні значення (не щокадру); лічильники з табулярними цифрами — через `TMP_Text.SetText("{0}", value)` без алокацій.
- **Плюралізація**: ключі `_one`/`_other` (en), без форм для ja; uk (v1.1) — `few`/`many` за CLDR у `PluralRules`.
- **Мова**: за замовчуванням з `Application.systemLanguage` (ja → ja, uk → uk у v1.1, інакше en), вибір у Settings, зберігається в `PlayerPrefs`.
- **Fallback**: відсутній переклад → `en` + warning в Editor; EditMode-тест: кожен ключ має непорожній `en`, плейсхолдери `{0}` збігаються між мовами.
