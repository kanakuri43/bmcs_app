# コードスタイル・規約

## C# / .NET全般
- Nullable参照型 **有効**、null関連警告は **エラー扱い**（`Directory.Build.props`の`WarningsAsErrors=nullable`）。伝票金額・消込を扱うため、null起因の実行時例外を実装時に潰す方針。
- `LangVersion=latest`, `ImplicitUsings=enable`。
- 非同期処理を適切に使い、DBアクセス等でUIスレッドをブロックしない。
- リポジトリ抽象化・DIによる差し替えを見据えた抽象化は**行わない**（過剰設計を避ける方針、TODO.md）。**例外: `IUnitPriceCalculator`（単価決定ロジック）は将来の掛け率マスタ実装を見据えてインターフェース化している（M-3・2026-09-10、既定方針への明示的な例外としてユーザー確認済み）。** 他の計算ロジック（`ConsumptionTaxCalculator`、`SalesTaxAmountAssigner`等）は原則どおり static クラスのまま。

## 層の依存規約
- `Presentation → Application → Infrastructure → Domain` の一方向のみ。ViewModelは`DbContext`を直接触らず、DBアクセスは必ずApplication層経由。
- **この規約はコンパイラで強制されていない**（ProjectReferenceの推移的伝播のため）。コードレビューで担保する。
- DBに依存しない単体テストの対象はDomainに集める（消費税計算・端数処理・状態判定）。消込・締めは開発用DBへの結合テストで担保する想定。

## 命名規則（DB ↔ C#）
- テーブル名・カラム名: 単数形 `snake_case`。C#: クラス名・プロパティ名は `PascalCase`。
- PascalCase↔snake_case変換は `EFCore.NamingConventions`（`UseSnakeCaseNamingConvention()`）に一任。数字を含む列名（`address1`等）は自動変換が意図通りにならないため`HasColumnName`を明示。
- **識別子（テーブル名・カラム名・enumメンバ名）に具体的な数値（税率%など）をハードコードしない**。例: 税率別内訳カラムは `standard_rate_taxable_amount`（旧`taxable_10_amount`は廃止済み）。`TaxCategory`enumも`Standard`/`Reduced`/`TaxExempt`（旧`Standard10`/`Reduced8`は廃止済み）。
- 区分値: DB側`tinyint`、C#側`enum`（`HasConversion<byte>()`）。文字列コードは使わない。
- テーブル名は単数形だがDbSetは複数形宣言のため`ToTable()`を必ず明示。
- 業務コードをそのまま主キーにする（サロゲートキー不使用）。
- 監査列（`is_deleted`, `created_by/at`, `updated_by/at`, `row_version`）は共通基底クラス（`TrackedEntity`/`AuditableEntity`）＋`ConfigureAuditColumns()`拡張メソッドで一元化。ナビゲーションプロパティは持たせない（`HasOne().WithMany().HasForeignKey()`のみ、FK関係は必ず登録）。
- `decimal`は`HasPrecision(p,s)`必須。`varchar`/`char`は`.IsUnicode(false)`必須。`date`は`DateOnly`にマッピング。

## ドキュメント運用
- DB関連の詳細（設計方針・スキーマ・未確定事項）は必ず`docs/database-schema.md`に書き、`CLAUDE.md`や他docsには書かない。
- 暫定設定を採用した場合は必ず`docs/`に「暫定」と明記して記録する（TODO.md）。
- 画面番号（SCR-xxx等）は暫定のため使わず、画面は名称で参照する。

## テスト（2026-09-10時点で存在。旧メモの「テストプロジェクトはまだ存在しない」は廃止）
- `tests/bmcs_app.Domain.Tests/`（xUnit v2、DB不要）: 消費税計算・単価決定・税額分岐・伝票区分正規化・編集ロック判定ロジック等の単体テスト。195件。
- `tests/bmcs_app.Application.Tests/`（DB結合テスト）: `DevDatabaseFixture`経由で開発用ライブDB（`172.16.3.171`/`bmcs_db`）に実接続。採番・売上登録・受注確定・訂正取消の結合テスト。37件。テスト内で作成した行は`created_by`にマーカーを付ける、または`__TEST`接頭辞のキーを使い、テスト内で物理削除して後始末する（本番運用の「物理削除しない」方針とは別、検証データの後始末）。
- **重要**: `SalesService`/`OrderService`のユースケースメソッドは内部で独自に`BeginTransactionAsync`→`SaveChangesAsync`→`CommitAsync`を行うため、これらを呼ぶ結合テストは「外側をトランザクションで包み`RollbackAsync`する」方式が使えない（ネストした`BeginTransactionAsync`はEF Coreが例外を投げる）。この場合は使い捨てデータをコミットし`finally`で物理削除する方式（`SalesServiceTests`/`SalesServiceCorrectionTests`）を使う。一方`OrderStatusService.ApplySalesQuantityDeltasAsync`のようにトランザクションを開始しないメソッドは、外側を`BeginTransactionAsync`→`RollbackAsync`で包む方式が使える（`OrderStatusServiceTests`）。
- テストで既存行を`ExecuteUpdateAsync`等の生SQL/バルク更新で直接書き換えた後に同じ`DbContext`でユースケースを呼ぶ場合、EF Coreの識別解決（同一インスタンスの再利用）により追跡済みエンティティのrowversionが古いまま残り、意図しない`DbUpdateConcurrencyException`になることがある。`dbContext.ChangeTracker.Clear()`を挟んで回避する（`SalesServiceCorrectionTests`で発見・対処）。

## 作業ルール（重要）
- **指示が矛盾している場合は必ずユーザーに確認する**（自分の解釈で片方を選んで進めない）。対象は (1)過去の確定済み決定との矛盾 (2)CLAUDE.md/docsの方針との矛盾 (3)一つの指示内で両立しない要求。単なる曖昧さ（どの解釈でも成立する）は確認せず進めてよい。
- **git commit のメッセージは日本語で書く**（CLAUDE.md、2026-09-10追加）。
