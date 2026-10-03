# ADR-0001: Unity 6.3 LTS + URP з 2D Renderer

- **Статус**: Proposed
- **Дата**: 2026-10-03
- **Автор**: Unity Architect
- **Пов'язані задачі**: PA-001, PA-101

## Контекст
Проєкт переходить з Flutter/Impeller на Unity. Гра — 2D-сцена (вид збоку на бонсай у горщику), процедурна геометрія, 120 FPS на мобільних, жорсткий бюджет розміру (.ipa < 25 МБ, .aab < 20 МБ), циркадне освітлення, кілька легких шейдерів.

## Рішення
- **Unity 6.3 LTS** (точний патч фіксується в `ProjectSettings/ProjectVersion.txt` при скафолдингу; оновлення — лише новим ADR).
- **URP** з **2D Renderer**: `Light2D` для циркадного світла, Sprite-Lit Shader Graph для гілок/листя, Camera Sorting Layer Texture для blur під UI.
- Scripting backend **IL2CPP**, API Compatibility **.NET Standard 2.1**, ARM64.
- Color space **Linear**; Graphics APIs: iOS — Metal; Android — Vulkan + OpenGLES3 fallback.

## Розглянуті альтернативи
| Варіант | Плюси | Мінуси |
|---|---|---|
| Built-in RP | Найменший оверхед | Застарілий, без 2D Lights і Shader Graph на рівні URP |
| URP Universal (3D) Renderer | Більше фіч | Не потрібні для 2D; 2D Lights недоступні |
| Unity 2022 LTS | Стабільний | Закінчується підтримка; нема нових API рендеру/Android |

## Наслідки
- + 1 draw call на гілки і 1 на листя досяжні; 2D-світло вирішує циркадну палітру без кастомних пасів.
- − Базовий розмір рушія ~10–15 МБ — бюджет 25/20 МБ тісний. Перевіряє Spike **PA-003** (порожній білд + стрипінг).
- − Unity Hub і редактор потрібно встановити локально (зараз не встановлено).
