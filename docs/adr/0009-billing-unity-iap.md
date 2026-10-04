# ADR-0009: Білінг — Unity IAP 5 для MVP; власний тонкий плагін StoreKit 2 + Play Billing — опція v1.1

- **Статус**: Proposed
- **Дата**: 2026-10-05
- **Автор**: Unity Architect
- **Погоджує**: власник проєкту
- **Пов'язані задачі**: PA-101b, PA-402, PA-003b

## Контекст
Монетизація (GDD) — разові покупки (non-consumable) + Restore Purchases, Family Sharing на iOS. ARCHITECTURE §2 обрала **Unity IAP 5** (`com.unity.purchasing` 5.4.4, StoreKit 2 / Play Billing 7+).
Вимірювання PA-003b показало, що IAP 5 — найдорожчий пакет стеку за розміром:
- сам IAP + `com.unity.services.core`: **−1.08 МБ** Android download при видаленні;
- IAP 5 тягне **Newtonsoft.Json** (транзитивно), а Newtonsoft — `System.Xml` + `System.Data` + рефлексію BCL + модулі `libunity`: разом ще **~4.2 МБ** Android download і, за оцінкою, **~8–10 МБ** iOS install. Поки IAP у проєкті, ця ціна сплачена незалежно від нашого коду;
- `PaymentOption/Resources` IAP додає ~0.75 МБ брендових текстур (нестиснуто) через `Resources/`.
Разом «IAP + Newtonsoft» ≈ **5.6 МБ Android / ~12 МБ iOS install** (R5 у звіті PA-003b: Android 24.8 → 19.2).
Рішення власника 2026-10-05 (варіант C): бюджети підняті до Android ≤ 28 (межа 30), iOS download ≤ 45 / install ≤ 60, IAP 5 лишається на MVP.

## Рішення
- **MVP (Sprint 4, PA-402)**: Unity IAP 5. Увесь доступ — через порт `IPurchaseService` (Application) з реалізацією `UnityIapPurchaseService` в Infrastructure; жоден інший код не посилається на `UnityEngine.Purchasing` (перевіряється `LayerDependencyTests`). Ніякого нашого коду на Newtonsoft (ADR-0008 rev.3).
- **Опція v1.1**: замінити реалізацію порту на власний тонкий плагін за схемою ADR-0007:
  - iOS — Swift, StoreKit 2 (`Product.products`, `purchase()`, `Transaction.updates`, `AppStore.sync()` для Restore), C-ABI (`@_cdecl`) → `[DllImport("__Internal")]`;
  - Android — `.androidlib` (Kotlin), Play Billing Library (`BillingClient`, `acknowledgePurchase`, `queryPurchasesAsync`), виклик через `AndroidJavaObject`/`AndroidJavaProxy`;
  - C#-фасад у Infrastructure + Editor-заглушка (фейкові продукти) для EditMode/PlayMode-тестів;
  - обмін даними — `JsonUtility`-DTO або примітиви, без Newtonsoft.
  Після заміни прибрати `com.unity.purchasing` з manifest — це звільняє IAP + Newtonsoft (~5.6 МБ Android / ~12 МБ iOS install, PA-003b R5) за умови, що жоден інший пакет не тягне Newtonsoft (перевірити `packages-lock.json`).
- **Тригери перегляду** (будь-який): прогноз розміру після вертикального зрізу перевищує бюджет; Unity IAP вимагатиме Unity Gaming Services-ініціалізації/акаунта, який ми не хочемо; проблеми IAP 5 з Family Sharing/Restore на пристроях.

## Розглянуті альтернативи
| Варіант | Плюси | Мінуси |
|---|---|---|
| **Unity IAP 5 (обрано для MVP)** | Готовий StoreKit 2 + Play Billing, Restore/FSM, receipt-обгортки, підтримка Unity, швидко до релізу | ~1.1 МБ сам + тримає Newtonsoft (~4.2 МБ Android, ~8–10 МБ iOS); `services.core`; брендові текстури в `Resources/` |
| Власний тонкий плагін StoreKit 2 + Play Billing (v1.1) | −5.6 МБ Android / ~−12 МБ iOS install; повний контроль; без Newtonsoft і UGS | ~5–8 SP (два нативні модулі + тести на пристроях); власна відповідальність за edge cases (pending, deferred/Ask to Buy, refunds, acknowledge ≤ 3 днів) |
| RevenueCat / інший SDK | Серверна валідація, аналітика | Залежність від сервісу, свої нативні SDK (розмір не менший), часто тягне JSON-бібліотеки; підписки нам не потрібні |
| Unity IAP 4.x | Менше змін API | StoreKit 1 (deprecated), той самий Newtonsoft |

## Наслідки
- Позитивні: найкоротший шлях до релізу; порт `IPurchaseService` робить заміну локальною (одна збірка Infrastructure + плагіни).
- Негативні / борг: ~5.6 МБ Android / ~12 МБ iOS install «заблоковані» до v1.1; Newtonsoft лишається в білді транзитивно (наш код його не використовує, `link.xml` його не тримає — правила дає сам IAP).
- Що треба перевірити: R8 (`minifyRelease`) не ламає Play Billing / IAP на пристрої (consumer ProGuard-правила бібліотек) — PA-402; розмір після вертикального зрізу — повторний PA-003b-замір; оцінка трудомісткості плагіна — окрема задача v1.1.
