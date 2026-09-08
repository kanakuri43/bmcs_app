// 開発用ライブDBに対する結合テスト（TODO.md 4-1）。テストクラス同士が
// slip_number_sequence の行を奪い合わないよう、クラス間の並列実行を無効化する。
// テストメソッド内部で行う N タスクの並列実行（同時採番の検証そのもの）には影響しない。
[assembly: CollectionBehavior(DisableTestParallelization = true)]
