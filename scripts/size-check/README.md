# Size check

Вимірювання розміру білда під бюджети з [ARCHITECTURE §10](../../docs/architecture/ARCHITECTURE.md).

| Скрипт | Що міряє | Бюджет |
|---|---|---|
| `android-download-size.sh <app.aab>` | Play download size через bundletool (arm64) | ≤ 25 МБ, межа 30 |
| `ios-download-size.sh <App.xcarchive>` | `.app` install size, zip-нижня і encrypted-верхня межа download | download ≤ 35 МБ, install ≤ 55 МБ |

iOS-архів без підпису:
```bash
xcodebuild archive -project Unity-iPhone.xcodeproj -scheme Unity-iPhone -configuration Release \
  -destination 'generic/platform=iOS' -archivePath build/App.xcarchive \
  CODE_SIGNING_ALLOWED=NO CODE_SIGNING_REQUIRED=NO CODE_SIGN_IDENTITY=""
```

`spike-pa003/` — збережені скрипти спайка PA-003 для відтворення: Editor-скрипт `Spike.cs` (білд з параметрами `-gfx`, `-entry`, `-strip`, `-codegen`, `-cfg`, `-splash`, `-stripEngine`), `SpikeWhy.cs` (чому модулі потрапляють у білд), мінімальний `manifest.json` проєкту A, послідовності прогонів `seqA*.sh`. Скрипти розраховані на структуру тимчасової теки спайка (`ProjA/`, `out/`, `logs/`) — див. [звіт PA-003 §9](../../docs/qa/reports/PA-003-2026-10-04.md).
