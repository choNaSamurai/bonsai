# Architecture Decision Records

Значущі, важко-зворотні рішення (рушій, пайплайн, структура коду, платформні інтеграції, формат збереження).
Дрібні технічні рішення — у [`../decisions/TECH_DECISIONS.md`](../decisions/TECH_DECISIONS.md).

Правила:
- Новий ADR = копія [`0000-template.md`](0000-template.md) з наступним номером.
- Статус `Proposed` → `Accepted` **лише після погодження власника проєкту** в чаті.
- Прийнятий ADR не редагується по суті — його замінює новий (`Superseded by`).

| # | Назва | Статус |
|---|---|---|
| [0001](0001-unity-version-and-pipeline.md) | Unity 6.6 (6000.6.4f1) + URP 2D Renderer | Proposed |
| [0002](0002-layered-assemblies.md) | Шари через asmdef, VContainer, R3, UniTask | Proposed |
| [0003](0003-tree-data-model.md) | Плоска таблиця вузлів замість рекурсивного immutable-дерева (ред. 3, 2026-10-05: `LastCutDay`, `InPad`/`BackBudDist`, `rulesVersion`; доповнення PA-102R: `StableHash`, `GrowthRules.Version` 4) | Proposed |
| [0004](0004-procedural-tree-rendering.md) | Процедурний меш гілок уздовж C1-осей + листя подушками з кластерів спрайтів (ред. 3, 2026-10-05: корені Небарі, подушки, горщик-масштаб; ред. 4, 2026-10-05: гаусове розширення основи, корені над мохом, кущики моху; ред. 5, 2026-10-05: корені — продовження стовбура, маска стику, скручування кори, пласкі/мінімальні подушки, `y_rim`, кадр за bounds усього дерева) | Proposed |
| [0005](0005-input-gesture-router.md) | Input System + глобальний GestureRouter | Proposed |
| [0006](0006-ui-ugui-canvas.md) | uGUI Canvas + TextMeshPro + PrimeTween | Proposed |
| [0007](0007-native-platform-layer.md) | Native-плагіни: хаптика, аудіосесія, віджети, геолокація | Proposed |
| [0008](0008-persistence.md) | AtomicFileStore з SHA-256 envelope; схема сейву v1 (rev.3 — JsonUtility, double як long-біти; + доповнення 2a: `lastCutDay`, `rulesVersion`) | Proposed |
| [0009](0009-billing-unity-iap.md) | Білінг: Unity IAP 5 для MVP; власний плагін StoreKit 2 + Play Billing — опція v1.1 | Proposed |
| [0010](0010-shape-style-profile.md) | `ShapeStyleProfile`: стиль як накладка на параметри форми породи (`ShapeParams`); стиль не зберігається в сейві (MVP) (ред. 2, 2026-10-05: розміщення полів BALANCE §1.5) | Proposed |
