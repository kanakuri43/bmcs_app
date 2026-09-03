# bmcs_app アーキテクチャ

> Phase 0-1（骨格作成）で確定した事項のみを記載している。各層の責務の詳細、トランザクション境界、EF Core とストアドの使い分け、非同期方針、rowversion の適用単位は Phase 0-2 で追記する。

---

## 1. プロジェクト構成

**4層＝4プロジェクト。機能（受注・売上・請求…）はプロジェクトではなくフォルダで分ける。**

```
bmcs_app.sln
Directory.Build.props          … 全プロジェクト共通のビルド設定
src/
 ├─ bmcs_app/                  … プレゼンテーション層（WPF, exe）
 │   └─ Views/Menu/            … 機能別フォルダ。ViewModels/ も同じ構成にする
 ├─ bmcs_app.Application/      … 業務処理層
 ├─ bmcs_app.Infrastructure/   … データアクセス層
 └─ bmcs_app.Domain/           … ドメイン層
```

機能別にプロジェクトを分けない理由: 全11画面という規模に対して参照管理とビルド時間の負担が見合わないため。なお、機能ごとに exe を分ける必要は無いと判断している（同時起動は後述のとおり単一 exe で実現できる）。

## 2. 層間の依存方向

**古典的レイヤード（一方向）。** インターフェースによる依存関係の逆転は行わない。

```
Presentation ──► Application ──► Infrastructure ──► Domain
      │                                               ▲
      └───────────────────────────────────────────────┘
```

| プロジェクト | 参照するもの | 役割 |
|---|---|---|
| `bmcs_app.Domain` | なし | エンティティ、enum、消費税計算などの純粋ロジック |
| `bmcs_app.Infrastructure` | Domain | EF Core の `DbContext`・マッピング、端末ローカル設定の読み書き |
| `bmcs_app.Application` | Domain, Infrastructure | 伝票登録・請求締め・入金消込などのユースケース。トランザクション境界を持つ |
| `bmcs_app`（WPF） | Domain, Application | View / ViewModel、DI 構成 |

- **リポジトリのインターフェースは作らない。** Application が `DbContext` を直接使う。差し替えを見据えた抽象化を行わない方針（`TODO.md` 保留項目の扱い）に沿う。
- **DB に依存しない単体テストの対象は Domain に集める**（消費税計算・端数処理・状態判定）。消込・締めは開発用DBに対する結合テストで担保する。

### 規約: Presentation は Infrastructure を参照しない

ViewModel が `DbContext` を直接触らず、DB アクセスは必ず Application 層を経由させる。

**この規約はコンパイラで強制されていない。** .NET SDK の `ProjectReference` は推移的に伝播するため、`bmcs_app` の csproj が Infrastructure を参照していなくても、Application 経由で Infrastructure の型が見えてしまう（Phase 0-1 で実測して確認した）。

強制する手段は検討したが採用しなかった。

- `DisableTransitiveProjectReferences=true` … コンパイル参照は遮断できるが、`bmcs_app.Infrastructure.dll` が出力ディレクトリにコピーされなくなり実行時に読み込めない
- 上記に加えて `ExcludeAssets="compile"` を付けた明示参照 … dll はコピーされるがコンパイル参照も復活してしまい、遮断できない
- `ReferenceOutputAssembly="false"` ＋ 手動コピー … 実現はできるが MSBuild の細工が過剰

したがって**コードレビューで担保する**。将来ここを機械的に守らせたい場合は、MSBuild ではなくアーキテクチャテスト（参照関係を検査するテスト）を追加する方が素直である。

## 3. ターゲットフレームワーク

| プロジェクト | TFM |
|---|---|
| `bmcs_app`（WPF） | `net10.0-windows` |
| Domain / Application / Infrastructure | `net10.0` |

- .NET 10 を採用する。LTS であり、.NET 9 は 2026年5月にサポート終了済みのため。
- **下位3層に `-windows` を付けない**のは、WPF の型が下位層に混入することをコンパイラで防ぐため。こちらは推移参照の影響を受けないので確実に効く。
- 共通設定（`Nullable=enable`、`ImplicitUsings=enable`、`LangVersion=latest`、null 安全性の警告をエラー扱い）は `Directory.Build.props` に集約する。個別の csproj には TFM とプロジェクト固有の設定のみを書く。

## 4. 複数画面の同時起動

売上入力と受注入力を同時に開くなど、**複数画面の並行利用を前提とする。** 次の2通りをどちらも許可する。

1. **1プロセス・複数ウィンドウ** … メニューから各画面を別ウィンドウとして開く。`App.xaml` の `ShutdownMode="OnLastWindowClose"` により、メインメニューを閉じても他の画面が開いている間はアプリを終了しない
2. **多重起動** … 同じ exe を複数起動する。多重起動の抑止は行わない

機能ごとに exe を分ける必要はない。exe を分けても得られるのは「別プロセスで動く」ことだけで、それは同じ exe の多重起動と等価であるため。

**注意（Phase 0-3 で実装する際の前提）**: 1プロセスで複数ウィンドウを開く場合、`DbContext` のスコープをウィンドウごとに分ける必要がある。単一の `DbContext` を全ウィンドウで共有すると、受注画面の未保存の変更が売上画面の保存時に一緒に書き込まれる。
