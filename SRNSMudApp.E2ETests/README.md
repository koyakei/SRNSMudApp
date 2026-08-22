# SRNSMudApp.E2ETests

Playwright によるE2Eテストプロジェクト。Phase 1〜4 のテスト移行プロジェクト完了時点での
棚卸し結果（何が・なぜ残っているか）を記録する。

## テストの役割分担（判断基準）

| プロジェクト | 役割 |
|---|---|
| `SRNSMudApp.Tests` | xUnit + bUnit によるコンポーネントテスト／サービステスト。**新機能追加時は基本的にここに書く** |
| `SRNSMudApp.E2ETests` | Playwright によるE2Eテスト。**実ブラウザAPI依存・実認証パイプライン検証など、他の手段で代替できないものだけに限定する** |

新しいテストを書く際の判断基準:

- **ブラウザのネイティブAPIに依存するか？**（IntersectionObserver、WebAuthn/CDP、実JSグローバル関数、
  実SignalR接続、Cookie発行を伴う認証リダイレクト）→ はい = E2E
- **Blazorのレンダリング結果とDB状態の検証だけで済むか？** → はい = bUnit コンポーネントテスト
- 純粋なロジック（embedding類似度、JSONエクスポート構築、URL状態同期）→ サービス層に切り出してユニットテスト

## 現存ファイル一覧と維持理由

### テストインフラ

| ファイル | 役割 |
|---|---|
| `CustomWebApplicationFactory.cs` | Testcontainers(MSSQL) + WebApplicationFactory のテスト基盤。削除しない |
| `SharedTestServerFixture.cs` | アセンブリ全体で1つのファクトリ（MSSQLコンテナ1つ）を共有する NUnit `[SetUpFixture]`（Phase 5-4）。ダミーGoogle ClientId もここで設定 |
| `WebAuthnTestHelpers.cs` | Passkey系2テストの共通セットアップ/後処理ヘルパー（Phase 5-2） |

### 維持対象のE2Eテスト

| ファイル | 検証内容 | E2Eとして残す理由 |
|---|---|---|
| `PasskeyLoginE2ETests.cs` | WebAuthnによるパスキー登録・ログイン | CDPセッション＋仮想オーセンティケータなど実ブラウザAPI依存 |
| `PasskeyRenameE2ETests.cs` | パスキーの名称変更 | 同上 |
| `ExternalLoginButtonsE2ETests.cs` | ログインページの外部認証ボタン描画と `window.customAuth.renderGoogleButton` 等の実JSグローバル関数定義確認 | ビルド後の実JS実行結果を見る以外に検証手段がない。環境変数副作用の注意点はファイル先頭コメント参照 |
| `GlobalPopoverE2ETests.cs` | 6ルート遷移時にBlazor未処理例外UIが出ないことの横断スモーク | 実SignalR接続・実JSでのポップオーバー描画を含む最終防衛線。個別ページのロジックはコンポーネントテスト側でカバー済み（Phase 4-1） |
| `ItemListFocusE2ETests.cs` | スクロールで画面中央に来たアイテムが自動フォーカスされURL更新されること（1ケースのみに縮小） | IntersectionObserverは実ブラウザJS APIのためbUnitでは再現不可（Phase 4-4-d）。クリックフォーカス・URL復元・タグフィルタ共存・作者リンクは bUnit 側 `ItemListFocusTests` へ移行済み |
| `LoginAndPostItemE2ETests.cs` | Google/LINE/GitHub各プロバイダのモックコールバック→ログインCookie発行→認証済み到達（3ケース） | 実ネットワークスタック（ASP.NET Core認証ミドルウェア・Cookie発行）の検証のためE2Eに残す。アイテム投稿部分は `AddItemTests` へ移行済み（Phase 4-6） |
| `DuplicateTaggingRequestCancelE2ETests.cs` | ユーザーAの契約承認後、ユーザーBが自分のリクエストをキャンセルできること | 移行フェーズの対象外として現状維持。複数ユーザー間の実時間相互作用シナリオ |
| `ItemDetailTagWeightE2ETests.cs` | 投票ボタンクリック後のItemDetailのタグウェイト表示 | 移行フェーズの対象外として現状維持 |

### 移行済み・削除済み（参考）

| 元ファイル | 移行先 |
|---|---|
| `VectorSearchE2ETests.cs`（4ケース+無関係コード） | `SRNSMudApp.Tests/TagEmbeddingServiceTests.cs`（実LocalEmbedderでコサイン類似度のコア検証）＋ `Components/Tag/TagSearchTests.cs`（UI配線1ケースのみに集約）（Phase 4-7） |
| `ItemDetailDeepLinkE2ETests.cs` | `Components/Item/ItemDetailDeepLinkTests.cs`（状態⇔URL双方向）（Phase 4-5） |
| `ItemListExportE2ETests.cs` | `Components/Item/ItemListExportTests.cs`（JS interop傍受によりブラウザ不要）（Phase 4-3） |
| `MudPopoverE2ETests.cs` | `Components/User/UserSearchTests.cs` に入力→候補表示ケースを追加（Phase 4-2） |
| `ContractManagementE2ETests.cs`, `PublicOfferE2ETests.cs`, `ContractAndOfferScenarioE2ETests.cs` | `Components/PublicOffer/*Tests.cs`（Phase 3） |
| `NotificationsTagRequestE2ETests.cs` | `Components/Notifications/NotificationsPageTests.cs`（Phase 3） |
| `ItemListAutocomplete/TagSearch/AddItem/ImportTag/UserDetailTree/TagListConcurrency` 系 | Phase 2 で bUnit 化済み |

## Before / After サマリー

| 指標 | Before（移行前） | After（移行後） |
|---|---|---|
| E2Eテストファイル数 | 30+ | 8テストクラス＋基盤3ファイル |
| E2Eテストケース数 | 37+ | 16 |
| E2E実行時間 | 約3〜4分（コンテナ起動×クラス数） | 約50秒（コンテナ起動1回・Phase 5-4後の実測） |
| bUnit/サービステスト | 120 | 145 |
| 単体テスト実行時間 | 数秒 | 約2秒 |

### カバレッジ（移行対象コンポーネント、Phase 5-6 実測）

| コンポーネント/サービス | 行カバー率 |
|---|---|
| CreatePublicOfferDialog | 90% |
| TagSearch | 76% |
| NotificationsPage | 74% |
| TriggerPublicOfferDialog | 69% |
| ItemDetail / PublicOfferBoard | 約64% |
| ResourceList | 58% |
| ItemList | 57% |
| TagEmbeddingService | 100% |
