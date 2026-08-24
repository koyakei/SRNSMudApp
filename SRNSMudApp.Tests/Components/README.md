# SRNSMudApp.Tests/Components — bUnit残置ポリシー

このディレクトリのテストは、以下のいずれかに該当するため bUnit コンポーネントテストとして残置しています。
単なる表示計算・状態判定ロジックは ViewModel へ抽出し、`SRNSMudApp.Tests/Components/**/*ViewModelTests.cs`
またはトップレベルの xUnit テストとして直接検証してください（新規ロジックを @code ブロックに直接書かないこと）。

## 残置基準
- 実データベース（InMemory EF Core）への書き込み・永続化を伴うフロー検証
- MudDialog の実際の開閉・結果受け渡しの検証
- MudAutocomplete 等 MudBlazor コンポーネントとの実インタラクション検証
- 複数コンポーネントの連携（親子間のイベント伝播、カスケード値等）検証
- レンダリング結果（DOM/Markup）そのものが検証対象であるスモークテスト

## ファイル一覧と残置理由

### Admin / Contract / Pages

| ファイル | 残置理由 |
|---|---|
| `Admin/RequireConfirmedAccountTests.cs` | 実际の UserManager + InMemory DB によるメール確認トグル操作を検証しているため |
| `Contract/DuplicateTaggingRequestCancelTests.cs` | 重複コントラクトの承認が他ユーザーの同一リクエストに波及しないことを実 DB 書き込み込みで検証しているため |
| `Pages/PageRenderSmokeTests.cs` | 主要ページが例外なくレンダリングされること自体を検証するスモークテストであるため |

### Item

| ファイル | 残置理由 |
|---|---|
| `Item/AddItemTests.cs` | フォーム送信 → 実 DB へのアイテム保存・ユーザー重複排除を検証しているため |
| `Item/ItemCardViewModelTests.cs` | **bUnit不使用**。投票スコア・表示計算の純粋ViewModelテスト |
| `Item/ItemDetailDeepLinkTests.cs` | タブ選択・行選択の実レンダリングと URL 反映（双方向同期）を検証する統合テストのため（純粋変換は `ItemDetailQueryStateTests` に抽出済み） |
| `Item/ItemDetailQueryStateTests.cs` | **bUnit不使用**。ディープリンク状態の純粋変換テスト（Phase 4 で抽出済み） |
| `Item/ItemDetailTagWeightTests.cs` | Weight 変更ボタンクリック → 実 DB 更新 → 表示反映の一連の流れを検証しているため |
| `Item/ItemListAutocompleteTests.cs` | MudAutocomplete のサジェスト選択が検索ボックスへ反映される実インタラクションを検証しているため |
| `Item/ItemListExportServiceTests.cs` | **bUnit不使用**。エクスポートDTO構築の純粋なサービステスト |
| `Item/ItemListExportTests.cs` | エクスポート実行時に LinkPreview 取得（HTTP モック含む）まで含む統合フローを検証しているため |
| `Item/ItemListFocusTests.cs` | カードクリック → フォーカススタイル適用 → focus クエリパラメータ更新の双方向同期を検証しているため |
| `Item/ItemListQueryStateTests.cs` | **bUnit不使用**。URLクエリ解析の純粋変換テスト |
| `Item/ItemListTagSearchTests.cs` | タグ検索実行 → タグチップ表示 → フィルタクエリパラメータ反映を検証しているため |

### Notifications / PublicOffer

| ファイル | 残置理由 |
|---|---|
| `Notifications/NotificationsPageTests.cs` | 通知の承認/却下が実 DB ステータスと UI 表示の両方に反映されることを検証しているため |
| `Pages/NotificationsViewModelTests.cs` | **bUnit不使用**。通知の関連アイテム解決等の純粋ViewModelテスト（Phase 3 で抽出済み） |
| `PublicOffer/CreatePublicOfferDialogTests.cs` | ダイアログ送信 → 実 DB への公開オファー作成を検証しているため |
| `PublicOffer/PublicOfferBoardTests.cs` | オファーボードでの関係生成とオファー無効化を実 DB 込みで検証しているため |
| `PublicOffer/TriggerPublicOfferDialogTests.cs` | ダイアログ承認 → リレーション生成 → オファー非活性化のフローを検証しているため |

### Tag

| ファイル | 残置理由 |
|---|---|
| `Tag/BaseTagChipTests.cs` | RenderFragment スロット（ActionContent 等）の描画結果そのものを検証するスモークテストのため |
| `Tag/ImportTagTests.cs` | CSV インポート → 実 DB への2階層タグ作成、親未選択時のボタン制御を検証しているため |
| `Tag/ItemTagChipTests.cs` | チップの削除・Weight変更インタラクションが実 DB と連動することを検証しているため |
| `Tag/ItemTagChipViewModelTests.cs` | **bUnit不使用**。ハイライト・配色計算の純粋ViewModelテスト |
| `Tag/ItemTagRequestChipViewModelTests.cs` | **bUnit不使用**。可視性判定の純粋ViewModelテスト（Phase 2 で抽出済み） |
| `Tag/TagDeletionTrackingTests.cs` | ダイアログ経由のタグ追加 → チップ削除 → 実 DB からの削除（EF トラッキング例外の回帰）を検証しているため |
| `Tag/TaggingRequestApprovalTests.cs` | 承認ボタン → 実 DB のステータス遷移 → 表示更新を検証しているため |
| `Tag/TaggingRequestCancelTests.cs` | 取り下げ操作の実 DB 反映とアイコン表示切替を検証しているため |
| `Tag/TaggingRequestListDialogLauncherTests.cs` | 却下ボタンが IDialogLauncher 経由で正しい型・タイトル・オプションでダイアログを起動することを検証しているため |
| `Tag/TaggingRequestRejectTests.cs` | 却下コメント入力ダイアログの実開閉と RequestInfoAlert のステータス表示を検証するスモークテストのため（ロジック分岐は `RequestInfoAlertViewModelTests` に抽出済み） |
| `Tag/TaggingRequestReplyTests.cs` | スレッドダイアログへの返信投稿が実 DB に保存されることを検証しているため（バッジ表示のスモークテストも含む。可視性ロジックは `ItemTagRequestChipViewModelTests` に抽出済み） |
| `Tag/TagListConcurrencyTests.cs` | 同時レンダリング時の EF 同一 Scoped DbContext 競合例外を検証する統合テストのため（DbContext 直接注入チェックは `Architecture/DbContextInjectionRuleTests` に分離済み） |
| `Tag/TagSearchTests.cs` | タグ埋め込み類似検索の候補表示を MudAutocomplete 実インタラクションで検証しているため |
| `Tag/TagTableViewModelTests.cs` | **bUnit不使用**。検索フィルタ・添付タグ表示計算の純粋ViewModelテスト |
| `Tag/TagTreePopoverViewModelTests.cs` | **bUnit不使用**。ツリー行構築の純粋ViewModelテスト |
| `Tag/TagTreeTests.cs` | タグ階層管理ページの jqTree 初期化・子タグ追加・D&D移動の権限チェックを検証しているため |

### UI / User / その他

| ファイル | 残置理由 |
|---|---|
| `GlobalConcurrencyTests.cs` | **bUnit不使用**。全コンポーネントの DbContext 直接注入を禁止するアーキテクチャ規約のリフレクションテスト |
| `UI/AsyncPageStateTests.cs` | **bUnit不使用**。ページ状態 union の状態遷移テスト |
| `UI/AsyncPageViewTests.cs` | 状態別フラグメントの描画結果そのものを検証する汎用コンポーネントのレンダリングテストのため |
| `UI/ItemReplyThreadTests.cs` | リプライスレッド子コンポーネントが SRNS サービスなしで独立してレンダリングできることを検証しているため |
| `UI/RequestInfoAlertViewModelTests.cs` | **bUnit不使用**。ステータステキスト・カラー・アイコン種別の純粋ViewModelテスト（Phase 3 で抽出済み） |
| `UI/ResourceListViewModelTests.cs` | **bUnit不使用**。システムタグ検索・スクロール先計算の純粋ViewModelテスト |
| `UI/TagCardViewModelTests.cs` | **bUnit不使用**。投票状態・チップ表示計算の純粋ViewModelテスト |
| `UI/UrlPreviewCardViewModelTests.cs` | **bUnit不使用**。テキストフラグメント解析の純粋ViewModelテスト（Phase 4 で抽出済み） |
| `User/UserDetailTreeTests.cs` | jqTreeInterop.init に渡されるツリー JSON の実レンダリング経由での受け渡しを検証する薄い統合テストのため（JSON 生成ロジック自体は `TagTreeViewModel.SerializeTreeData` に一元化済み） |
| `User/UserManagementTests.cs` | メール確認トグルの Identity 操作（UserManager + InMemory DB）を検証しているため |
| `User/UserSearchTests.cs` | ユーザー検索のモック済み DbContext ファクトリ経由の検索結果表示を検証しているため |

## 運用ルール

1. 新規ロジックは @code ブロックに書かず ViewModel / state クラスへ抽出し、純粋 xUnit テスト (`*ViewModelTests` / `*QueryStateTests`) を追加すること。
2. 上記 bUnit テストは「統合・スモーク」の位置づけのため、分岐網羅を目的としたパラメータ化テストの増設は行わない（純粋 ViewModel テスト側で網羅する）。
3. E2E 残置ルールは `.github/AGENTS.md` を参照。
