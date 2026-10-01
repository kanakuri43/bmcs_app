# コードスタイル・規約

正は `docs/architecture.md` 10章・15〜16章と `docs/database-schema.md` 3章。ここはその要約。

## C# / .NET全般
- Nullable参照型 **有効**、null関連警告は **エラー扱い**（`Directory.Build.props`の`WarningsAsErrors=nullable`）。伝票金額・消込を扱うため、null起因の実行時例外を実装時に潰す方針。
- `LangVersion=latest`, `ImplicitUsings=enable`。
- 非同期処理を適切に使い、DBアクセス等でUIスレッドをブロックしない。
- リポジトリ抽象化・DIによる差し替えを見据えた抽象化は**行わない**（過剰設計を避ける方針、TODO.md）。**例外: `IUnitPriceCalculator`（単価決定ロジック）は将来の掛け率マスタ実装を見据えてインターフェース化している（M-3・2026-09-10、既定方針への明示的な例外としてユーザー確認済み）。** 他の計算ロジック（`ConsumptionTaxCalculator`、`SalesTaxAmountAssigner`等）は原則どおり static クラスのまま。
- 編集ロック判定・消込再計算等のユースケース固有ロジックは、専用クラスを新設するかサービスの public メソッドに直接実装するかを都度判断する（判定条件が数行程度でDomain純粋関数へ抽出する再利用先が無ければ、サービスのメソッドに留める。`ReceiptEntryService.EvaluateEditLockAsync`が例。複数エンティティのプロパティを見る分岐が複雑なら`SalesEditLockEvaluator`のようにDomain層へ抽出する）。

## 層の依存規約
- `Presentation → Application → Infrastructure → Domain` の一方向のみ。ViewModelは`DbContext`を直接触らず、DBアクセスは必ずApplication層経由。
- **この規約はコンパイラで強制されていない**（ProjectReferenceの推移的伝播のため）。コードレビューで担保する。
- DBに依存しない単体テストの対象はDomainに集める（消費税計算・端数処理・状態判定）。消込・締めは開発用DBへの結合テストで担保する想定。

## 命名規則（DB ↔ C#）
- テーブル名: 複数形 `snake_case`（`orders`・`tax_rates` は業務上の呼称）。カラム名: `snake_case`。C#: クラス名・プロパティ名は `PascalCase`。
- PascalCase↔snake_case変換は `EFCore.NamingConventions`（`UseSnakeCaseNamingConvention()`）に一任。数字を含む列名（`address1`等）も同じ規約に任せ、`HasColumnName`は書かない。
- **識別子（テーブル名・カラム名・enumメンバ名）に具体的な数値（税率%など）をハードコードしない**。例: 税率別内訳カラムは `standard_rate_taxable_amount`（旧`taxable_10_amount`は廃止済み）。`TaxCategory`enumも`Standard`/`Reduced`/`TaxExempt`（旧`Standard10`/`Reduced8`は廃止済み）。
- 区分値: DB側`tinyint`、C#側`enum`（`HasConversion<byte>()`）。文字列コードは使わない。
- `ToTable()`でテーブル名を必ず明示する。
- 業務コードをそのまま主キーにする（サロゲートキー不使用）。
- 監査列（`is_deleted`, `created_by/at`, `updated_by/at`, `row_version`）は共通基底クラス（`TrackedEntity`/`AuditableEntity`）＋`ConfigureAuditColumns()`拡張メソッドで一元化。ナビゲーションプロパティは持たせない（`HasOne().WithMany().HasForeignKey()`のみ、FK関係は必ず登録）。
- `decimal`は`HasPrecision(p,s)`必須。`varchar`/`char`は`.IsUnicode(false)`必須。`date`は`DateOnly`にマッピング。
- 名前空間`bmcs_app.Application.Receipt`と、その配下から裸で参照する`bmcs_app.Domain.Entities.Receipt`型が衝突するため、この名前空間配下では`ReceiptEntity`等のエイリアスを使う（enclosing namespaceがusing導入の型より優先されるC#の解決規則のため）。

## ドキュメント運用
- DB関連の詳細（設計方針・スキーマ・未確定事項）は必ず`docs/database-schema.md`に書き、`CLAUDE.md`や他docsには書かない。
- 暫定設定を採用した場合は必ず`docs/`に「暫定」と明記して記録する（TODO.md）。
- 画面番号（SCR-xxx等）は暫定のため使わず、画面は名称で参照する。

## テスト（2026-09-15時点。件数は変動するため`dotnet test`の実行結果を都度確認すること）
- `tests/bmcs_app.Domain.Tests/`（xUnit v2、DB不要）: 消費税計算・単価決定・税額分岐・伝票区分正規化・編集ロック判定・消込配分ロジック等の単体テスト。
- `tests/bmcs_app.Application.Tests/`（DB結合テスト）: `DevDatabaseFixture`経由で開発用ライブDB（`172.16.3.171`/`bmcs_db`）に実接続。採番・各画面の登録/訂正/取消・消込・フェーズレビューの結合テスト。テスト内で作成した行は`__`接頭辞のキーを使い、テスト内で物理削除して後始末する（本番運用の「物理削除しない」方針とは別、検証データの後始末）。
- **重要**: 自前で`BeginTransactionAsync`→`SaveChangesAsync`→`CommitAsync`を行うユースケースメソッド（`SalesService`/`OrderService`/`ReceiptEntryService`/`DetailReceiptEntryService`等の新規登録・訂正・取消系）を呼ぶ結合テストは「外側をトランザクションで包み`RollbackAsync`する」方式が使えない（ネストした`BeginTransactionAsync`はEF Coreが例外を投げる）。この場合は使い捨てデータをコミットし`finally`で物理削除する方式を使う。一方`SettlementService.RecalculateForBillingGroupAsync`のようにトランザクションを開始しないメソッドは、外側を`BeginTransactionAsync`→`RollbackAsync`で包む方式が使える（`SettlementServiceTests`）。
- テストで既存行を`ExecuteUpdateAsync`等の生SQL/バルク更新で直接書き換えた後に同じ`DbContext`でユースケースを呼ぶ場合、EF Coreの識別解決（同一インスタンスの再利用）により追跡済みエンティティのrowversionが古いまま残り、意図しない`DbUpdateConcurrencyException`になることがある。`dbContext.ChangeTracker.Clear()`を挟んで回避する。

## 作業ルール（重要）
- **指示が矛盾している場合は必ずユーザーに確認する**（自分の解釈で片方を選んで進めない）。対象は (1)過去の確定済み決定との矛盾 (2)CLAUDE.md/docsの方針との矛盾 (3)一つの指示内で両立しない要求。単なる曖昧さ（どの解釈でも成立する）は確認せず進めてよい。
- **git commit のメッセージは日本語で書く**（CLAUDE.md、2026-09-10追加）。
