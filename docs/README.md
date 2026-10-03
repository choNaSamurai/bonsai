# Shared Project Context & Memory

Єдине джерело правди для всіх AI-агентів і людей. **Агенти не мають пам'яті між викликами — все, що має пережити задачу, записується сюди.**

```
                         ┌──────────────────────────────┐
                         │   Оркестратор (головна сесія) │  ← власник проєкту погоджує
                         └──────────────┬───────────────┘
        ┌───────────────┬───────────────┼───────────────┬───────────────┬───────────────┐
        ▼               ▼               ▼               ▼               ▼               ▼
  Game Designer   Unity Architect  Gameplay Dev    UI/UX Dev      Technical Artist   QA Engineer
        │               │               │               │               │               │
        └───────────────┴───────────────┴──── read/write ───────────────┴───────────────┘
                                        ▼
   docs/  GDD · BALANCE · ARCHITECTURE · ADR · TASKS · QA REPORTS · TECH DECISIONS · UI · PROCESS
```

## Карта пам'яті

| Артефакт | Файл | Власник (пише) | Читають |
|---|---|---|---|
| **GDD** | [`gdd/GDD.md`](gdd/GDD.md) | Game Designer | усі |
| **Баланс** | [`gdd/BALANCE.md`](gdd/BALANCE.md) | Game Designer | GP, QA, TA |
| **Архітектура** | [`architecture/ARCHITECTURE.md`](architecture/ARCHITECTURE.md) | Unity Architect | усі розробники, QA |
| **ADR** | [`adr/`](adr/README.md) | Unity Architect (інші — пропонують) | усі |
| **Technical Decisions** | [`decisions/TECH_DECISIONS.md`](decisions/TECH_DECISIONS.md) | будь-який агент (append) | усі |
| **Tasks** | [`tasks/BACKLOG.md`](tasks/BACKLOG.md), `tasks/PA-xxx.md` | Оркестратор; агенти — статус/нотатки своїх задач | усі |
| **QA Reports** | [`qa/reports/`](qa/reports/), [`qa/TEST_STRATEGY.md`](qa/TEST_STRATEGY.md) | QA Engineer | усі |
| **UI-реалізація** | [`design/UI_IMPLEMENTATION.md`](design/UI_IMPLEMENTATION.md) | UI/UX Developer | TA, GP |
| **Процес / Git** | [`process/GIT_WORKFLOW.md`](process/GIT_WORKFLOW.md) | Unity Architect | усі |
| **Маркетинг-контекст** | [`../.agents/product-marketing.md`](../.agents/product-marketing.md) | Game Designer / оркестратор | marketing skills |
| **Архів вимог (Flutter)** | [`reference/flutter-v3.2/`](reference/flutter-v3.2/README.md) | — (read-only) | усі |

## Протокол роботи агента

1. **Перед роботою** прочитати: цей файл → задачу `tasks/PA-xxx.md` (або рядок у BACKLOG) → документи з поля «Посилання» задачі. Не читати весь архів без потреби.
2. **Писати лише у свої артефакти** (таблиця вище). Хочеш змінити чужий — додай пропозицію в розділ «Нотатки/Пропозиції» задачі або створи ADR зі статусом `Proposed`.
3. **Кожне рішення фіксується**: значуще → ADR (Proposed), дрібне → рядок у TECH_DECISIONS.
4. **Наприкінці** оновити статус задачі і додати рядок у її «Журнал»; у відповіді оркестратору — коротко: що зроблено, які файли змінено, що відкрито/заблоковано.
5. **Ніколи не комітити.** Git-операції, що змінюють історію/гілки, — тільки оркестратор після погодження власника.
6. Конфлікт між документами: GDD/BALANCE перемагають щодо *що* (геймплей, числа); ARCHITECTURE/ADR — щодо *як* (код, рушій). Нерозв'язне → питання власнику.
