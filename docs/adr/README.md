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
| [0003](0003-tree-data-model.md) | Плоска таблиця вузлів замість рекурсивного immutable-дерева (ред. 2, 2026-10-05) | Proposed |
| [0004](0004-procedural-tree-rendering.md) | Процедурний меш гілок уздовж C1-осей + листя подушками з пуфів (ред. 2, 2026-10-05) | Proposed |
| [0005](0005-input-gesture-router.md) | Input System + глобальний GestureRouter | Proposed |
| [0006](0006-ui-ugui-canvas.md) | uGUI Canvas + TextMeshPro + PrimeTween | Proposed |
| [0007](0007-native-platform-layer.md) | Native-плагіни: хаптика, аудіосесія, віджети, геолокація | Proposed |
| [0008](0008-persistence.md) | AtomicFileStore з SHA-256 envelope; схема сейву v1 (ред. 2, 2026-10-05) | Proposed |
