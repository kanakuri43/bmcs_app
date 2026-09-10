# bmcs_app 概要

石山商店の**販売管理システム**（受注 → 売上 → 請求 → 入金 → 元帳 → 月次締め）。新規構築中。

- 取引先は2種類、業務フローが異なる:
  1. 一般企業（締め取引）: 締め日で1ヶ月分まとめて請求・入金消込
  2. 官公庁・学校・都度取引先（明細・都度請求）: 売上1件〜数件ごとに明細請求書を都度発行、明細単位で消込
- 在庫・発注・仕入・買掛・支払は今回のスコープ外（将来連携を見据えてカラムは削らない）。

## 技術スタック

- C# / WPF / 4層構造+MVVM / MahApps.Metro / SQL Server 2022 Express（実機）/ EF Core（O/Rマッパーのみ、マイグレーション機能は使わない）/ rowversion楽観的排他 / Windows 11+
- `net10.0-windows`（WPF exe）、`net10.0`（他3層）。Nullable参照型有効、警告はエラー扱い（`Directory.Build.props`）。

## プロジェクト構成（4層=4プロジェクト、機能はフォルダで分割）

```
bmcs_app.sln
Directory.Build.props
src/
 ├─ bmcs_app/                  … Presentation (WPF, exe)。ViewModels/Views を Order/Sales/Menu/Master/Common で分割
 ├─ bmcs_app.Application/      … 業務処理層（ユースケース、トランザクション境界）。Order/Sales/Master/Common
 ├─ bmcs_app.Infrastructure/   … データアクセス層（EF Core DbContext, Configurations/）
 └─ bmcs_app.Domain/           … ドメイン層（エンティティ, enum, Calculations/, Numbering/）
tests/
 ├─ bmcs_app.Domain.Tests/     … DB不要の単体テスト（xUnit v2）。消費税計算・単価決定・税額分岐ロジック
 └─ bmcs_app.Application.Tests/… 開発用ライブDBへの結合テスト（DevDatabaseFixtureで実接続）。採番・売上登録
scripts/                       … DDL（001, 002, ... 連番。適用済みは改変しない）、seed_dev_data.sql
docs/                          … 設計文書（下記）
```

**進捗（2026-09-10時点）**: Phase 0〜5は完了（5-1〜5-7すべて実装済み）。受注入力（`OrderEntryWindow`/`OrderService`/`OrderStatusService`/`OrderQueryService`）・売上入力（`SalesEntryWindow`/`SalesService`/`SalesQueryService`/`SalesEditLockService`）が実装済み。両画面は`SlipLineViewModel`/`SlipLineControl`（Common）を共用。単価決定は`IUnitPriceCalculator`インターフェース（現時点の実装は`StandardUnitPriceCalculator`。将来の掛け率マスタ実装に備えた例外的な抽象化、M-3）。売上保存時の税額確定（`slip_tax_amount`/`tax_amount`のtax_unit別分岐）は`SalesTaxAmountAssigner`に一本化。

Phase 5-3〜5-7（2026-09-10実装）で追加した主な要素:
- **受注からの売上確定**: `SalesService.CreateAsync`が明細行の`OrderSlipNumber`/`OrderLineNumber`から受注デルタを導出し、`OrderStatusService.ApplySalesQuantityDeltasAsync`を同一トランザクションで呼ぶ。
- **返品・値引**: `sales.slip_type`を明細行ごとに選択可能にし、`SalesSlipTypeRules`（Domain）で数量符号・原価（値引は常に0）を正規化。
- **過去伝票の複写・伝票検索**: 受注/売上共用の伝票検索モーダル`SlipSearchDialog`（`ViewModels/Common`）を新設。
- **売上の訂正・取消**: `SalesService.UpdateAsync`/`CancelSlipAsync`。編集ロック判定はDomain純粋関数`SalesEditLockEvaluator`＋DB照会する`SalesEditLockService`。伝票単位の楽観的排他制御の共通処理`SlipConcurrencyGuard`（`Application/Common`）を新設（`docs/architecture.md` 9章が予告していたもの）。
- 4-4の潜在バグ（中止済み受注に紐づく売上を後から取消できない）を修正。

詳細な残タスク（Phase 6以降）は`TODO.md`を参照。

依存方向: `Presentation → Application → Infrastructure → Domain`（古典的レイヤード、インターフェースでの逆転なし）。リポジトリ抽象化なし（Applicationが`DbContext`を直接使う）。

## ドキュメント地図（実装前に必ず確認）

- `CLAUDE.md` — 作業ルール（指示が矛盾したら必ずユーザーに確認する）、Purpose、Tech stack、業務フロー概要
- `docs/architecture.md` — 層構成、DB接続情報、命名規則、DDL運用ルール
- `docs/product-spec.md` — 用語、業務フロー、共通業務ルール、伝票の状態遷移
- `docs/design_document.md` — 画面ごとの要点、未解決の不明点（DB以外）
- `docs/database-schema.md` — テーブル設計方針・定義・未確定事項（DB専用。DB関連の詳細はここに書く）
- `TODO.md` — 開発タスク一覧、モデル選択基準（Opus/Sonnet）、保留項目の扱い方針
- `REVIEW.md` — 設計上の残課題（暫定設定 or 要決定）

## 開発用DB環境

- サーバ `172.16.3.171`（SQL Server 2022 Express）、DB名 `bmcs_db`。同一サーバに他システムDBが同居するため **DDLは必ず`bmcs_db`を明示**。
- 接続文字列は `src/bmcs_app/appsettings.Development.json`（gitignore対象、`.sample`のみコミット）。
- DBスキーマ変更はEF Coreマイグレーションを使わず、**AIがSQLCMDでライブDBに直接DDLを適用**する運用（`scripts/00N_*.sql`を連番で追加、適用済みファイルは改変しない）。
