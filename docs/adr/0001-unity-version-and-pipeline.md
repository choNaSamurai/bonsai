# ADR-0001: Unity 6.6 (6000.6.4f1) + URP з 2D Renderer

- **Статус**: Proposed
- **Дата**: 2026-10-03 (оновлено 2026-10-04: версія рушія; 2026-10-05: бюджети розміру, PA-101b)
- **Автор**: Unity Architect
- **Пов'язані задачі**: PA-001, PA-101, PA-003, PA-003b, PA-101b

## Контекст
Проєкт переходить з Flutter/Impeller на Unity. Гра — 2D-сцена (вид збоку на бонсай у горщику), процедурна геометрія, 120 FPS на мобільних, жорсткий бюджет розміру (у ТЗ: .ipa < 25 МБ, .aab < 20 МБ; перевизначено 2026-10-04, див. Наслідки), циркадне освітлення, кілька легких шейдерів.

## Рішення
- **Unity 6000.6.4f1** (Unity 6.6) — встановлено власником 2026-10-04. Фіксується в `ProjectSettings/ProjectVersion.txt`; зміна версії — лише новим ADR.
- Початкова пропозиція була 6.3 LTS. Якщо 6.6 не має статусу LTS, приймаємо компроміс: коротше вікно патчів в обмін на новіші API; перед релізом (Sprint 4) переглянути, чи переходити на актуальний LTS.
- **URP** з **2D Renderer**: `Light2D` для циркадного світла, Sprite-Lit Shader Graph для гілок/листя, Camera Sorting Layer Texture для blur під UI.
- Scripting backend **IL2CPP**, API Compatibility **.NET Standard 2.1**, ARM64.
- Color space **Linear**; Graphics APIs: iOS — Metal; Android — Vulkan + OpenGLES3 fallback.

## Розглянуті альтернативи
| Варіант | Плюси | Мінуси |
|---|---|---|
| Built-in RP | Найменший оверхед | Застарілий, без 2D Lights і Shader Graph на рівні URP |
| URP Universal (3D) Renderer | Більше фіч | Не потрібні для 2D; 2D Lights недоступні |
| Unity 2022 LTS | Стабільний | Закінчується підтримка; нема нових API рендеру/Android |
| Unity 6.3 LTS | Довге вікно підтримки | Старіші API; власник обрав 6.6 |

## Наслідки
- + 1 draw call на гілки і 1 на листя досяжні; 2D-світло вирішує циркадну палітру без кастомних пасів.
- − Spike **PA-003** (2026-10-04): порожній URP-2D проєкт — Android 18.2 МБ download, iOS ~30 МБ download / 40 МБ install. Бюджети ТЗ недосяжні; власник затвердив нові: **Android download arm64 ≤ 25 МБ (межа 30), iOS download ≤ 35 МБ, install ≤ 55 МБ**. URP коштує ~6 МБ (Android) / +13.6 МБ `.app` (iOS) — приймаємо заради 2D Lights і Shader Graph; Built-in RP deprecated з Unity 6.5.
- − Spike **PA-003b** (2026-10-05) на скафолді PA-101: зі стеком (Unity IAP 5 → Newtonsoft → System.Xml/System.Data, Localization) бюджети 25 / 35 / 55 недосяжні. Власник затвердив **варіант C**: **Android download arm64 ≤ 28 МБ (жорстка межа 30), iOS download ≤ 45 МБ, install ≤ 60 МБ**; переглядаються після вертикального зрізу, якщо контент вимагатиме. Після PA-101b (OptimizeSize, C++ Release, R8, URP-стрипінг, звужений `link.xml`, без Localization, без сплешу) скафолд: Android **25.06**, iOS `.app` **55.95** / download ~43.1 (верхня межа 45.55). Найбільший резервний важіль — [ADR-0009](0009-billing-unity-iap.md).
- Graphics APIs не змінюються: Vulkan + GLES3 на Android (GLES3-only дав би лише −0.36 МБ після URP-стрипінгу).
- − Статус підтримки 6.6 (LTS чи update-реліз) треба перевірити в Unity Hub; від цього залежить план оновлень перед релізом.
