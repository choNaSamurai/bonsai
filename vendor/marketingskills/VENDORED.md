# Vendored: coreyhaines31/marketingskills

- **Upstream**: https://github.com/coreyhaines31/marketingskills
- **Commit**: `dda3841f0b294e01e93b1541486beefbfab0915e` (plugin version 2.11.17)
- **License**: MIT (see `LICENSE`)
- **Vendored on**: 2026-10-03

## Що де лежить

| Upstream | У цьому репо | Навіщо |
|---|---|---|
| `skills/*` | `.claude/skills/*` | Project-level skills, Claude Code підхоплює їх автоматично |
| `tools/` | `vendor/marketingskills/tools/` | CLI/інтеграції, на які посилаються деякі скіли |
| `LICENSE`, `README.md`, `VERSIONS.md` | `vendor/marketingskills/` | Ліцензія та довідка |

Коли скіл посилається на `tools/clis/<name>.js` — шукати у `vendor/marketingskills/tools/clis/<name>.js`.

Продуктовий контекст для всіх маркетингових скілів: [`.agents/product-marketing.md`](../../.agents/product-marketing.md).

## Оновлення

```bash
scripts/sync-marketingskills.sh            # останній main
scripts/sync-marketingskills.sh <commit>   # конкретний коміт
```

Скрипт перезаписує лише теки скілів, які існують в upstream. Локальні зміни у цих теках будуть втрачені — кастомізацію робимо в окремих скілах.
