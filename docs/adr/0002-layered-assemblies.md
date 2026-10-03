# ADR-0002: Шарова архітектура через Assembly Definitions; VContainer, R3, UniTask

- **Статус**: Proposed
- **Дата**: 2026-10-03
- **Автор**: Unity Architect
- **Пов'язані задачі**: PA-101, PA-102

## Контекст
Оригінал вимагає «100% Pure Dart Domain» — вся математика росту/ґрунту тестується без рушія за мікросекунди. Стан — реактивний (Riverpod), побічні ефекти — тільки у презентаційному шарі.

## Рішення
- 5 збірок: `PocketArbor.Domain` і `PocketArbor.Application` з `noEngineReferences: true`; `Infrastructure`, `Presentation`, `Bootstrap` — з UnityEngine. Напрямок залежностей: `Bootstrap → Presentation → Application → Domain`, `Infrastructure → Application/Domain`.
- **VContainer** — DI та composition root (`RootLifetimeScope`). Жодних `FindObjectOfType`/синглтонів-статиків.
- **R3** — реактивний стан (`ReactiveProperty<T>`, `Subject<T>`) у Application; UI підписується у Presentation з `AddTo(this)`.
- **UniTask** — async у Infrastructure/Presentation (мережа, файли, native-виклики). Application оперує портами з `ValueTask`/колбеками, щоб лишатися без рушія.
- Пакети через **OpenUPM** scoped registry.

## Розглянуті альтернативи
| Варіант | Плюси | Мінуси |
|---|---|---|
| Zenject/Extenject | Відомий | Не розвивається, повільніший, більше алокацій |
| Без DI (ServiceLocator) | Простіше | Гірша тестованість, приховані залежності |
| UniRx | Знайомий | Архівований; R3 — наступник від того ж автора |
| C# events без бібліотек | 0 залежностей | Багато шаблонного коду для підписок/відписок |

## Наслідки
- + Domain-тести — EditMode, без сцени, секунди на весь набір.
- + Порушення шарів не компілюється.
- − 3 сторонні пакети: треба `link.xml` під stripping і контроль розміру.
