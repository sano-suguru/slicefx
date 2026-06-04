# SliceFx 日本語ドキュメント

[English README](../../README.md)

> この日本語版は参考訳です。仕様・リリース情報・セキュリティ上の判断は英語版ドキュメントを正本とします。

このディレクトリには、SliceFx の主要 Markdown ドキュメントの日本語版を置きます。未翻訳ページへのリンクは英語版へ fallback します。

## 初回翻訳範囲

| Topic | 日本語 | English |
| --- | --- | --- |
| Root README | [README.ja.md](../../README.ja.md) | [README.md](../../README.md) |
| Design decisions FAQ | [design-decisions.md](design-decisions.md) | [docs/design-decisions.md](../design-decisions.md) |
| Production readiness | [production-readiness.md](production-readiness.md) | [docs/production-readiness.md](../production-readiness.md) |
| CLI | [cli.md](cli.md) | [docs/cli.md](../cli.md) |
| Source generator | [source-generator.md](source-generator.md) | [docs/source-generator.md](../source-generator.md) |
| Lambda | [lambda.md](lambda.md) | [docs/lambda.md](../lambda.md) |
| Parameter binding | [guides/parameter-binding.md](guides/parameter-binding.md) | [docs/guides/parameter-binding.md](../guides/parameter-binding.md) |
| Return types | [guides/return-types.md](guides/return-types.md) | [docs/guides/return-types.md](../guides/return-types.md) |
| ASP.NET features | [guides/aspnet-features.md](guides/aspnet-features.md) | [docs/guides/aspnet-features.md](../guides/aspnet-features.md) |
| Minimal API migration | [migrations/from-minimal-api.md](migrations/from-minimal-api.md) | [docs/migrations/from-minimal-api.md](../migrations/from-minimal-api.md) |

## 翻訳方針

- 英語版を正本、日本語版を参考訳として扱います。
- API 名、package 名、diagnostic ID、command、code block は原則そのまま残します。
- `Feature`、`Slice`、`Minimal API`、`source generator`、`route manifest`、`portable`、`partial`、`aspnet-only` などの用語は、必要に応じて日本語説明を添えつつ原語を維持します。
- experimental / preview / unstable upstream toolchain の注意書きは弱めません。
