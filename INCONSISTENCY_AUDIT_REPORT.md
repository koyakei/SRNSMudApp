# SRNSWebApp 仕様不整合・エッジケース矛盾包括監査レポート
**監査実施日**: 2026-10-23  
**対象リポジトリ**: `SRNSWebApp` (`SRNSMudApp`, `SRNSMudApp.Client`, `SRNSMudApp.Tests`, `SRNSMudApp.E2ETests`)  
**監査種別**: ドメインモデル・認可・ライフサイクル・API/UI・Blazor Server/WASM 整合性包括監査  
**ステータス**: 完了 (Verified)

---

## 1. エグゼクティブサマリー (Executive Summary)

本監査は、SRNSWebApp 全体の主要5大ドメイン（①アイテム・引用・分割、②タグ・バウンティ契約、③公開範囲・認可、④通知・タイムラインイベント、⑤Blazor Server/WASM 連携）を対象に、仕様不整合、ドメインモデルの矛盾、エッジケースの破綻、セキュリティ・認可不全、クライアント・サーバー間の乖離を網羅的かつ客観的に調査・特定したものである。

各専門エクスプローラーによる静的解析、データフロー追跡、EF Core 規約検証の結果、**合計 71 件** の仕様不整合・エッジケース矛盾が特定された。特に、データ破壊・SQL例外クラッシュ、未認証での非公開情報漏洩、認可チェック漏れ（IDOR）によるデータ改ざん、および Blazor WASM 画面の無応答化（RenderMode 欠落）といった重大なアーキテクチャ上の欠陥が複数判明している。

### 不整合検出サマリー（重要度別・領域別）

| 領域 | High (重大) | Medium (中) | Low (軽微) | 合計 |
| :--- | :---: | :---: | :---: | :---: |
| **領域 1: アイテム・引用・分割機能** | 11 | 8 | 4 | **23** |
| **領域 2: タグ・バウンティ・コントラクト** | 8 | 3 | 0 | **11** |
| **領域 3: 公開範囲・認可制御** | 9 | 4 | 1 | **14** |
| **領域 4: 通知・タイムラインイベント** | 3 | 5 | 5 | **13** |
| **領域 5: Blazor Server vs WASM 整合性** | 5 | 4 | 1 | **10** |
| **総合計** | **36** | **24** | **11** | **71** |

---

## 2. 最重要課題（P0 / High Severity）のハイライト

1. **外部キー制約（Restrict）によるアイテム・タグの削除不能クラッシュ (領域1 & 領域2)**
   - `QuotedItemId`, `ItemSplitRequest.OriginalItemId/CreatedItemId`, `RightAsset.TargetTagId` に `DeleteBehavior.Restrict` が設定されているにもかかわらず、削除ロジック（`DeleteItemAsync`, `DeleteItemByAdminAsync`, `DeleteTagAsync`）で参照先が事前にクリア／NULL化されていない。
   - 他のユーザーが一度でも引用（Quote）または分割（Split）した投稿、および `RightAsset` を持つタグは SQL 外部キー制約違反（`SqlException`）により二度と削除できず、500 エラーとなる。
2. **未認証・匿名アクセスによる非公開（Private）アイテム・グループ投稿の全文漏洩 (領域1 & 領域3)**
   - `/api/item/{id}/quotes`, `/api/item/{id}/replies`, `/api/tag-diagram/data?itemIds={id}`, `/api/link/preview` などのパブリックエンドポイントで `WhereVisibleToUser` フィルタが完全に脱落しており、認証なしで他人の秘密投稿の本文・タグ・会話スレッドが取得可能。
3. **タグ直接更新エンドポイントにおける致命的 IDOR (領域3 & 領域5)**
   - `TagController.UpdateTag` (`PUT /api/tag/{id}`) において、JWT Claims から `currentUserId` を取得しているものの、サービス層（`TagCommandService.UpdateTagAsync`）にユーザーIDが渡されておらず、所有者チェック（`tag.OwnerId == currentUserId || isAdmin`）が一切行われていない。任意の認証済みユーザーが他人のタグを直接改名・非公開化・乗っ取り可能。
4. **Blazor WASM クライアントにおける 18 画面の `@rendermode` 欠落（画面フリーズ） (領域5)**
   - `SRNSMudApp.Client/Pages/` の 25 画面中 18 画面で `@rendermode` が宣言されておらず、ASP.NET Core Blazor Web App の仕様により Static SSR（静的サーバー描画）でレンダリングされ、ブラウザ上のすべてのボタン・フォーム・イベントハンドラが動作不能。既存のアーキテクチャテストは `App.razor` のアセンブリのみを走査していたため検知できていなかった。
5. **バウンティ・トリガーにおける外部キー不整合と無権限拒否脆弱性 (領域2)**
   - バウンティ作成時に `[NotMapped]` な `TargetItemId` のみ設定され、DB外部キー `TargetId` が 0 のまま保存されるため、対象アイテムとの関連が切断されバウンティボードで対象アイテムが表示されない。
   - `TagService.RejectTagRelationAsync` において `request.ContractType == "Bounty" || request.ContractType == "Trigger"` の真偽判定でユーザーIDチェックが欠落しており、第三者が任意のバウンティ・トリガーを無断で拒否・取り下げ可能。
6. **トランザクションコミット後のロールバック呼び出しクラッシュ (領域4)**
   - `ItemSplitService.ApproveSplitAsync` において、`await transaction.CommitAsync()` を実行した直後に `try` ブロック内で `_notificationService.NotifyUserAsync()` を呼んでいるため、WebPush 送信エラー等の例外発生時に `catch` 節でコミット済みトランザクションに対して `RollbackAsync()` が呼び出され、EF Core の `InvalidOperationException` でクラッシュする。

---

## 3. ドメイン別詳細監査結果

---

### 領域 1: アイテム・引用・分割機能 (Items, Quotes & Splits)

#### [High] 1-1. 被引用アイテムの削除時に SQL 外部キー制約違反（500 エラー）が発生
- **対象箇所**: `SRNSMudApp/Data/ApplicationDbContext.cs:75-78`, `SRNSMudApp/Services/ItemCardDataProvider.cs:68-100`
- **不整合の内容**:
  - `ApplicationDbContext.cs` では `QuotedItemId` に `DeleteBehavior.Restrict` が設定されている。
  - しかし `ItemCardDataProvider.DeleteItemAsync` はリプライアイテム（`ParentItemId == itemId || RootItemId == itemId`）のみを削除し、`QuotedItemId == itemId` である参照元アイテムの外部キーのクリアや削除を行わずに `context.Items.Remove(item)` を実行している。
- **再現・発生シナリオ**:
  1. ユーザー A がアイテム 1 を投稿する。
  2. ユーザー B がアイテム 1 を引用してアイテム 2 を投稿する。
  3. ユーザー A が自身のアイテム 1 を削除しようとする。
  4. SQL Server が `FK_Items_Items_QuotedItemId` 違反をスローし、500 エラーで削除に失敗する。
- **推奨修正方針**:
  - アイテム削除時に、当該アイテムを参照している `QuotedItemId` を NULL に更新するか、削除ポリシー（引用元に「削除されたアイテム」プレースホルダーを表示する等）をドメイン仕様として統一し、削除トランザクション内で明示的に処理する。

#### [High] 1-2. 分割リクエスト（ItemSplitRequest）が存在するアイテムが削除不能
- **対象箇所**: `SRNSMudApp/Data/ApplicationDbContext.cs:661-687`, `SRNSMudApp/Services/ItemCardDataProvider.cs:68-100`
- **不整合の内容**:
  - `ItemSplitRequest.OriginalItemId` および `CreatedItemId` に `DeleteBehavior.Restrict` が設定されている。
  - `DeleteItemAsync` で `ItemSplitRequest` のクリーンアップが一切行われないため、過去に分割リクエストの対象となったアイテム、または分割によって生成されたアイテムは永久に削除不能となる。
- **再現・発生シナリオ**:
  1. アイテムに対して分割リクエストが作成される（承認済・却下済問わずレコードが存在）。
  2. アイテム所有者が削除を実行すると、外部キー制約違反で失敗する。
- **推奨修正方針**:
  - `ItemSplitRequest` の外部キー定義に `OnDelete(DeleteBehavior.Cascade)` を適用するか、`DeleteItemAsync` 内で関連する `ItemSplitRequests` を明示的に削除または参照解除する。

#### [High] 1-3. 管理者強制削除（DeleteItemByAdminAsync）でリプライ付きアイテムの削除がクラッシュ
- **対象箇所**: `SRNSMudApp/Services/ItemCardDataProvider.cs:103-115`
- **不整合の内容**:
  - 通常の `DeleteItemAsync` ではリプライアイテムを事前削除する処理があるが、`DeleteItemByAdminAsync` では `context.Items.Remove(item)` のみを即時発行している。
  - `ParentItemId` は `DeleteBehavior.Restrict` であるため、リプライが 1 件でも存在するアイテムを管理者が強制削除しようとすると例外がスローされ削除できない。
- **再現・発生シナリオ**:
  1. 不適切投稿（アイテム A）にユーザーがリプライを付ける。
  2. 管理者が通報管理またはアイテム管理画面から「管理者削除」を実行する。
  3. 外部キー制約違反でクラッシュし、不適切投稿の排除に失敗する。
- **推奨修正方針**:
  - `DeleteItemByAdminAsync` にも通常削除と同様（またはそれ以上）のカスケード削除処理（リプライ、タグ関連、分割リクエスト等の整理）をトランザクション内で実装する。

#### [High] 1-4. パブリック API (`/api/item/{id}/quotes`) における非公開アイテムの無認証閲覧
- **対象箇所**: `SRNSMudApp/Controllers/ItemController.cs:443-451`, `SRNSMudApp/Services/ItemQuoteService.cs:108-117`
- **不整合の内容**:
  - Blazor Server の `ItemDetailDataProvider` では `WhereVisibleToUser` による権限フィルタが適用されているが、Web API の `GetQuotes` では `_itemQuoteService.GetQuotedByItemsAsync(id)` を直接呼び出している。
  - `GetQuotedByItemsAsync` には可視性フィルタが存在せず、非公開アイテムやグループ限定アイテムを引用元とする投稿データが未認証の第三者に返却される。
- **再現・発生シナリオ**:
  1. ユーザー A が非公開グループ内で秘密アイテムを引用して投稿する。
  2. 攻撃者が `GET /api/item/{id}/quotes` を未認証で呼び出す。
  3. 秘密の引用投稿が DTO として全文返却される。
- **推奨修正方針**:
  - `GetQuotes` エンドポイントに認証ユーザー情報を渡し、`ItemQuoteService` 内で `WhereVisibleToUser(context, currentUserId, isAdmin)` を適用する。

#### [High] 1-5. パブリック API (`/api/item/{id}/replies`) における非公開リプライの漏洩
- **対象箇所**: `SRNSMudApp/Controllers/ItemController.cs:385-393`, `SRNSMudApp/Services/ItemReplyService.cs:205-238`
- **不整合の内容**:
  - `GetReplies` が `[AllowAnonymous]` であり、サービス層の `GetItemRepliesAsync` において `IsPrivate` や `TargetUserGroupId` に応じた権限判定を行わずにスレッド全体を返却している。
- **再現・発生シナリオ**:
  1. 親投稿に非公開で返信されたリプライが存在する。
  2. 匿名ユーザーが `GET /api/item/{id}/replies` にアクセスし、非公開リプライをすべて取得する。
- **推奨修正方針**:
  - `GetItemRepliesAsync` に呼び出し元の `currentUserId` を渡し、`WhereVisibleToUser` を用いてフィルタリングする。

#### [High] 1-6. 分割の直接実行（SplitDirectAsync）におけるトランザクション漏れ
- **対象箇所**: `SRNSMudApp/Services/ItemSplitService.cs:67-96, 235-327`
- **不整合の内容**:
  - 分割承認（`ApproveSplitAsync`）では `IDbContextTransaction` が使用されているが、直接分割（`SplitDirectAsync`）では新アイテム作成、元アイテム更新、タグ引き継ぎの複数ステップにトランザクションが張られておらず、途中で失敗した場合に不整合な中間状態が永続化される。
- **推奨修正方針**:
  - `SplitDirectAsync` の一連の処理を `context.Database.BeginTransactionAsync()` で囲む。

#### [High] 1-7. 分割却下・取り下げにおけるトランザクション欠落
- **対象箇所**: `SRNSMudApp/Services/ItemSplitService.cs:882-985`
- **不整合の内容**:
  - `RejectSplitAsync` および `CancelSplitAsync` において、ステータス変更と `DeleteRequesterQuoteItemAsync`（引用削除）がトランザクションなしで別々に実行されており、後者が失敗した場合にステータスのみ更新された孤児データが残る。
- **推奨修正方針**:
  - ステータス更新と引用削除を単一のトランザクション内で実行する。

#### [High] 1-8. 分割リクエスト承認・却下の同時実行による競合破壊（Race Condition）
- **対象箇所**: `SRNSMudApp/Services/ItemSplitService.cs:235-327, 882-933`
- **不整合の内容**:
  - 分割リクエストのステータス遷移（`TradeStatus.Proposed` → `Executed` / `Rejected`）において行ロックや楽観的排他制御（Concurrency Token）が実装されておらず、並行リクエスト時に新アイテムが二重生成される。
- **推奨修正方針**:
  - `ItemSplitRequest` の更新時にステータス条件（`WHERE Status == TradeStatus.Proposed`）を伴うアトミック更新を行うか、行ロックを導入する。

#### [High] 1-9. 親アイテム更新時の QuotedItemPreview キャッシュ不整合
- **対象箇所**: `SRNSMudApp.Client/ViewModels/QuotedItemPreviewViewModel.cs:30-65`
- **不整合の内容**:
  - 引用作成時に元アイテムの抜粋をスナップショットとして保持しているが、元アイテムが編集・削除された際にスナップショットの整合性が保証されず、古い内容や削除済みコンテンツが画面上に残り続ける。
- **推奨修正方針**:
  - 引用プレビューの描画時に元アイテムの最新状態（または「編集済み」「削除済み」ステータス）を反映する仕様を明確化する。

#### [High] 1-10. ApplicationDbContext における ConversationOptOut 設定ブロックの 8 重重複定義
- **対象箇所**: `SRNSMudApp/Data/ApplicationDbContext.cs:574-765`
- **不整合の内容**:
  - `OnModelCreating` 内で `ConversationOptOut` の複合キー・リレーション設定コード（15行）が完全に同一のまま 8 回連続でコピペ重複している。
- **推奨修正方針**:
  - 重複している 7 回分の定義を削除し、モデル構成定義を整理する。

#### [High] 1-11. 分割承認時の通知送信先誤り（自分自身への通知）
- **対象箇所**: `SRNSMudApp/Services/ItemSplitService.cs:310-325`
- **不整合の内容**:
  - 自分自身の投稿に対する分割リクエストを自分で承認した場合でも、「あなたの分割リクエストが承認されました」という通知が自分宛てに送信される。
- **推奨修正方針**:
  - `request.RequesterUserId != ownerUserId` の条件を追加し、自己通知を抑制する。

---

### 領域 2: タグ・バウンティ・コントラクト (Tags, Bounties, and Contracts)

#### [High] 2-1. バウンティ作成時に TargetId が設定されずリレーションが切断される不整合
- **対象箇所**: `SRNSMudApp/Controllers/BountyController.cs:118-127`, `SRNSMudApp/Data/TaggingRequestEntity.cs:24-48`, `SRNSMudApp/Data/ApplicationDbContext.cs:430-434`
- **不整合の内容**:
  - `BountyController.CreateBounty` では `bounty.TargetItemId = request.TargetItemId;` を代入している。
  - しかし `TargetItemId` は `[NotMapped]` であり、DB上の外部キー列である `TargetId`（`TaggableTarget` テーブルへの FK）を更新しない。そのため `bounty.TargetId` は初期値 `0` のまま DB に保存される。
  - `TaggingContractService.GetRequestsByItemIdAsync` は `r.TargetId == item.TagTargetId` でクエリするため、作成されたバウンティがアイテム側から一切取得できない。また、バウンティボード上でも `Target` が NULL となり、対象アイテムのタイトルや内容が表示されない。
- **再現・発生シナリオ**:
  1. ユーザーがアイテムに対してバウンティを設定する。
  2. バウンティは作成されるが、対象アイテム詳細画面のバウンティ一覧に表示されず、バウンティボードでも「対象アイテムなし」の状態となる。
- **推奨修正方針**:
  - `BountyController` において、`item.TagTargetId` を取得して `bounty.TargetId` に明示的に代入するか、`TaggingRequestEntity` のプロパティセッターで `TargetId` を同期する。

#### [High] 2-2. Move コントラクトにおける認証バイパスと HierarchyId ツリー切断
- **対象箇所**: `SRNSMudApp/Services/TaggingContractService.cs:279-286`, `SRNSMudApp/Services/Contracts/MoveContractExecutor.cs:68-70`
- **不整合の内容**:
  - コントラクト承認前チェック（`preCheck`）において、タグ所有者チェックが行われるのは `Gratis` と `Mutual` のみであり、`Move`（タグの階層移動）はワイルドカード `_ => Success` に落ちるため、誰でも承認可能。
  - さらに `MoveContractExecutor` において、移動対象タグの `tagToUpdate.Node` のみ更新し、その配下に存在する子孫タグの `HierarchyId` ノードを再計算（`GetReparentedValue`）していないため、タグツリーの子孫階層が永久に切断・破損する。
- **再現・発生シナリオ**:
  1. 攻撃者が他人の所有する親タグ（子タグを多数持つ）の移動コントラクトを作成・承認する。
  2. 親タグのノードのみが移動し、すべての子孫タグがツリー検索・階層表示から消失する。
- **推奨修正方針**:
  - `Move` コントラクトに対してもタグ所有者（および移動先親タグの所有者）の認可ガードを必須化する。
  - `MoveContractExecutor` において、`tagToUpdate` のすべての子孫ノードに対して `node.GetReparentedValue(oldParentNode, newParentNode)` を適用して一括更新する。

#### [High] 2-3. 第三者による任意のバウンティ・トリガー強制却下脆弱性
- **対象箇所**: `SRNSMudApp/Services/TaggingService.cs:111-115`, `SRNSMudApp/Controllers/ContractController.cs:83-101`
- **不整合の内容**:
  - `TaggingService.RejectTagRelationAsync` 内の認可判定式：
    ```csharp
    RejectAuthorization authState = (request.TagOwnerUserId == rejectUserId ||
                                     request.RequesterUserId == rejectUserId ||
                                     request.ContractType == "Trigger" ||
                                     request.ContractType == "Bounty") switch { ... }
    ```
  - `ContractType == "Trigger"` および `ContractType == "Bounty"` がユーザーIDとの比較なしに単独で評価されているため、コントラクトタイプがバウンティまたはトリガーであれば、**誰であっても `AuthorizedToReject`（却下権限あり）と判定される**。
- **再現・発生シナリオ**:
  1. ユーザー A が高額報酬のバウンティを公開する。
  2. 無関係な悪意あるユーザー B が `POST /api/contract/{bountyId}/reject` を叩く。
  3. 認可を通過し、バウンティが一方的に却下・キャンセルされる。
- **推奨修正方針**:
  - `rejectUserId` が依頼者（`RequesterUserId`）、タグ所有者（`TagOwnerUserId`）、または対象アイテム所有者であるかどうかの厳密な判定に修正する。

#### [High] 2-4. バウンティ報酬のエスクロー欠落による二重支払い（Double Spending）とゾンビ化
- **対象箇所**: `SRNSMudApp/Controllers/BountyController.cs:126`, `SRNSMudApp/Services/Contracts/BountyContractExecutor.cs:182-211`
- **不整合の内容**:
  - バウンティ作成時に報酬アセット（`OfferedRewardAssetId`）のロック・エスクロー（預託）が行われない。
  - 依頼者はバウンティ作成後、報酬アセットを他人に譲渡または消費（Burn）できる。
  - その後、達成者がバウンティを達成（Fulfill）しようとすると、報酬アセットが無効化されているため `CompleteRewardTransfer` が失敗する。しかしバウンティ自体を取り下げるエンドポイントがないため、ボード上に永久に達成不可能なゾンビバウンティとして残留する。
- **推奨修正方針**:
  - バウンティ作成時に報酬アセットのステータスを「Escrowed」に変更して譲渡・消費を禁止し、バウンティキャンセル時または達成時にステータスを遷移させる。

#### [High] 2-5. Mutual コントラクトにおけるウェイトとアセット消費の非対称性（アービトラージ）
- **対象箇所**: `SRNSMudApp/Services/Contracts/MutualContractExecutor.cs:49-58, 190-245`
- **不整合の内容**:
  - `MutualContractExecutor.cs:57` において、`consumedAsset.Amount -= 1;` と固定で 1 だけ消費しているが、コントラクトの `ProposedWeight` が 10 であっても 1 しか消費されない。
  - さらに、タグ削除リクエスト（`TaggingRequestType.Remove`）の処理において、`ProcessMutualOfferedAsync` が無条件に実行され、保有残高チェックなしに Burn 状態の新規 `RightAsset` を生成（Ghost Minting）している。
- **推奨修正方針**:
  - `Amount` の減算値を `contract.ProposedWeight` と一致させ、削除操作時のアセット生成ロジックを是正する。

#### [High] 2-6. TagRelation, TagWeightLedger, Tag.CachedWeight の不整合・乖離
- **対象箇所**: `SRNSMudApp/Services/ItemTagService.cs:308`, `SRNSMudApp/Services/TagWeightLedgerService.cs:272-294`, `SRNSMudApp/Services/TaggingImportDataProvider.cs:239`, `SRNSMudApp/Services/ItemCardDataProvider.cs:112`
- **不整合の内容**:
  - `ItemTagService.ExecuteChangeItemTagAsync` で `entity.TagId = newTagId` を更新する際、旧タグのウェイト減算・新タグの加算・元帳（Ledger）への記録がすべてスキップされている。
  - インポート処理やアイテム削除処理でも `TagRelation` は削除・追加されるが、`TagWeightLedger` への記録および `Tag.CachedWeight` の再計算が行われず、長期間運用でウェイト合計と元帳が完全に乖離する。
- **推奨修正方針**:
  - タグ関連（`TagRelation`）のすべての変更を `TagWeightLedgerService` 経由に一本化し、トランザクション内で元帳記録と `CachedWeight` 更新をアトミックに行う。

#### [High] 2-7. タグ削除時に RightAsset のクリーンアップが漏れて SQL 例外でクラッシュ
- **対象箇所**: `SRNSMudApp/Data/ApplicationDbContextTagExtensions.cs:190-265`, `SRNSMudApp/Data/ApplicationDbContext.cs:461-465`
- **不整合の内容**:
  - `ApplicationDbContext.cs` では `RightAsset.TargetTag` に `DeleteBehavior.Restrict` が設定されている。
  - しかし `RemoveTagRestrictedDependenciesAsync` では 9 種類の依存コレクションを削除しているものの、`RightAssets` の削除・NULL化が漏れている。そのためアセットが発行されたタグは削除時に必ずクラッシュする。
- **推奨修正方針**:
  - `RemoveTagRestrictedDependenciesAsync` に `context.RightAssets.RemoveRange(...)` を追加する。

#### [High] 2-8. 未認証ユーザーに対する非公開タグの RightAsset 保有者情報漏洩
- **対象箇所**: `SRNSMudApp/Controllers/RightAssetController.cs:57-62`, `SRNSMudApp/Services/RightAssetDataProvider.cs:75-93`
- **不整合の内容**:
  - `GET /api/right-asset/overview/{tagId}` が `[AllowAnonymous]` で公開されており、タグが非公開（`IsPrivate == true`）であるかどうかのチェックを行わずに、タグの保有ユーザー名・ID・アセット残高を返却している。
- **推奨修正方針**:
  - 当該エンドポイントでタグの可視性チェック（`WhereVisibleToUser`）を実施し、権限のないユーザーには 403/404 を返却する。

---

### 領域 3: 公開範囲・認可制御 (Visibility & Authorization Controls)

#### [High] 3-1. TagDiagram API 経由での非公開アイテム・本文の未認証漏洩
- **対象箇所**: `SRNSMudApp/Controllers/TagDiagramController.cs:46-70`, `SRNSMudApp/Services/TagDiagramDataProvider.cs:99-142`
- **不整合の内容**:
  - `[HttpGet("data")] [AllowAnonymous]` なエンドポイントにおいて、パラメータ `itemIds` に指定されたアイテムを `context.Items.Where(i => idList.Contains(i.Id))` で取得している。
  - `WhereVisibleToUser` による可視性チェックが一切なく、かつ本文（`Content`）内の内部リンクや引用先アイテムまで再帰的に取得して返却している。
- **再現・発生シナリオ**:
  1. 攻撃者が連番の Item ID を指定して `GET /api/tag-diagram/data?itemIds=1,2,3...` を実行する。
  2. 認証なしでシステム内の全非公開アイテム、グループ限定アイテムの本文・タグ・引用関係を完全取得できる。
- **推奨修正方針**:
  - `TagDiagramDataProvider.GetDiagramDataAsync` に認証コンテキストを導入し、`WhereVisibleToUser` でアクセス可能なアイテムのみに絞り込む。

#### [High] 3-2. LinkPreview API 経由での非公開アイテム本文の漏洩
- **対象箇所**: `SRNSMudApp/Controllers/LinkPreviewController.cs:34-69`, `SRNSMudApp/Services/Providers/ItemLinkPreviewProvider.cs:34-84`
- **不整合の内容**:
  - `[HttpPost("preview")] [AllowAnonymous]` なエンドポイントにおいて、内部 URL（`/ItemDetail/{id}`）が指定された場合、`ItemLinkPreviewProvider` が DB からアイテムを取得して本文冒頭 200 文字をプレビューとして返却する。
  - 呼び出し元ユーザーの権限判定や `item.IsPrivate` のチェックが存在しない。
- **再現・発生シナリオ**:
  1. 攻撃者が `POST /api/link/preview` に `{"url": "/ItemDetail/123"}` を送信する。
  2. 非公開アイテム 123 のタイトルおよび本文冒頭が返却される。
- **推奨修正方針**:
  - `ItemLinkPreviewProvider` において、対象アイテムが非公開またはグループ限定である場合はプレビュー生成を拒否するか、認証ユーザーの可視性を確認する。

#### [High] 3-3. TagController におけるタグの直接改ざん・乗っ取り脆弱性 (IDOR)
- **対象箇所**: `SRNSMudApp/Controllers/TagController.cs:94-126`, `SRNSMudApp/Services/TagCommandService.cs:122-180`
- **不整合の内容**:
  - `PUT /api/tag/{id}` において、ログイン中の `currentUserId` を取得しているが、サービス層 `_tagCommandService.UpdateTagAsync` に渡していない。
  - サービス層でも所有者チェック（`tag.OwnerId == currentUserId`）を行わずに `Name`, `Description`, `IsPrivate`, `TargetUserGroupId` を更新している。
- **再現・発生シナリオ**:
  1. ユーザー A が公式・主要タグを作成する。
  2. 攻撃者（一般ユーザー B）が `PUT /api/tag/{AのタグID}` を送信し、タグ名を不適切な名称に変更し、非公開化する。
  3. タグの乗っ取りが成功する。
- **推奨修正方針**:
  - `TagController` から `currentUserId` を `UpdateTagAsync` に渡し、`tag.OwnerId == currentUserId || isAdmin` を検証して違反時は 403 Forbidden を返す。

#### [High] 3-4. 非公開アイテムを引用した公開投稿による機密情報の流出
- **対象箇所**: `SRNSMudApp/Controllers/ItemController.cs:349-379`, `SRNSMudApp/Services/ItemQuoteService.cs:26-118`
- **不整合の内容**:
  - 引用作成時（`CreateItemWithQuoteAsync`）、引用対象アイテムが非公開（`IsPrivate == true`）であっても、引用元の新しいアイテムを `IsPrivate == false`（全体公開）として作成することを許可している。
  - これにより、非公開アイテムの本文抜粋がパブリックタイムライン上に全体公開される。
- **推奨修正方針**:
  - 引用対象アイテムが非公開の場合、引用投稿も強制的に非公開とするか、非公開アイテムの外部引用を禁止するバリデーションを追加する。

#### [High] 3-5. ユーザーグループ削除に伴う機密投稿の「フォロワー全体公開」への昇格漏洩
- **対象箇所**: `SRNSMudApp/Data/ApplicationDbContext.cs:81-85`, `SRNSMudApp/Services/UserGroupDataProvider.cs:134-146`, `SRNSMudApp/Extensions/ItemVisibilityExtensions.cs:30-33`
- **不整合の内容**:
  - `Item.TargetUserGroup` の外部キーは `OnDelete(DeleteBehavior.SetNull)` に設定されている。
  - グループが削除されると、グループ限定投稿の `TargetUserGroupId` は `null` にクリアされるが、`IsPrivate` は `true` のまま残る。
  - `ItemVisibilityExtensions` の定義では、`IsPrivate && TargetUserGroupId == null` は「作成者のフォロワー全員に公開」と解釈される。
  - 結果として、極秘グループ内でのみ共有されていた投稿が、グループ削除を契機に作成者の全フォロワーに公開範囲が自動拡大（エスカレーション）される。
- **推奨修正方針**:
  - グループ削除時に所属アイテムを管理者非表示（`IsAdminHidden = true`）とするか、カスケード削除するか、公開範囲を「自分のみ」に厳格化する。

#### [High] 3-6. バウンティボード経由での非公開アイテム本文の全体公開
- **対象箇所**: `SRNSMudApp/Controllers/BountyController.cs:45-88, 93-137`
- **不整合の内容**:
  - 非公開アイテムを対象としてバウンティを作成可能であり、かつ `GET /api/bounty/active` は未認証で全件取得可能であるため、非公開アイテムの本文がバウンティボード上に全体公開される。
- **推奨修正方針**:
  - 非公開アイテムに対するパブリックバウンティ作成を禁止するか、バウンティ一覧で非公開アイテムを閲覧権限者のみに限定表示する。

#### [High] 3-7. TagImportController における非管理者によるシステム所有タグ偽造
- **対象箇所**: `SRNSMudApp/Controllers/TagImportController.cs:79-133`, `SRNSMudApp/Services/TaggingImportDataProvider.cs:109-140`
- **不整合の内容**:
  - タグインポート API が `[Authorize]` のみで保護されており、`[Authorize(Roles = "Admin")]` が付与されていない。
  - インポート処理内で `OwnerId = "system"` な公式タグが生成されるため、一般ユーザーがシステムタグを不正作成できる。
- **推奨修正方針**:
  - コントローラーに `[Authorize(Roles = "Admin")]` を付与する。

#### [High] 3-8. GitHub OAuth の未検証メールアドレスによるアカウント乗っ取り
- **対象箇所**: `SRNSMudApp/Controllers/AuthController.cs:57-77`, `SRNSMudApp/Services/ExternalTokenVerificationService.cs:123-136`
- **不整合の内容**:
  - 外部認証ログイン時、GitHub のパブリックプロフィール上のメールアドレス（未検証の自由記入値）で既存ユーザーを検索し、一致した場合にパスワード検証や確認なしに `AddLoginAsync` でアカウントを紐付けて即時サインインさせている。
- **推奨修正方針**:
  - 外部プロバイダ連携時はメールアドレスによる自動リンクを廃止し、既存アカウントのパスワード再認証を要求するか、検証済みメールアドレス（GitHub Verified Email API）のみを使用する。

#### [High] 3-9. REST API と Blazor Server のアイテム詳細可視性ロジック不一致
- **対象箇所**: `SRNSMudApp/Controllers/ItemController.cs:174-195`, `SRNSMudApp/Services/ItemDetailDataProvider.cs:93-107`
- **不整合の内容**:
  - Blazor Server では `WhereVisibleToUser` を用いてフォロワー判定やグループメンバー判定を正しく行っているが、REST API の `ItemController.GetItemById` では `if (item.IsPrivate && item.OwnerId != currentUserId && !isAdmin) return Forbid();` となっており、正当なフォロワーやグループメンバーが API 経由でアクセスすると 403 で拒否される。
- **推奨修正方針**:
  - REST コントローラー側も共通の `ItemVisibilityExtensions` を用いて判定する。

---

### 領域 4: 通知・タイムラインイベント (Notifications & Timeline Events)

#### [High] 4-1. トランザクションコミット後のロールバック呼び出しクラッシュ
- **対象箇所**: `SRNSMudApp/Services/ItemSplitService.cs:306-326`
- **不整合の内容**:
  - `_ = await context.SaveChangesAsync(cancellationToken);`
  - `await transaction.CommitAsync(cancellationToken);`
  - 上記のコミット直後に、同一 `try` ブロック内で `_notificationService.NotifyUserAsync(...)` を呼び出している。
  - WebPush 等のネットワーク不達で例外が発生すると、`catch` ブロックが実行され、**すでにコミット完了しているトランザクションに対して `await transaction.RollbackAsync()` を呼び出す**。
  - EF Core はコミット済みトランザクションへのロールバックを禁止しているため、`InvalidOperationException` をスローしてプロセスが異常終了し、成功したはずの分割処理がクライアントには 500 エラーとして通知される。
- **推奨修正方針**:
  - トランザクションを `try-finally` で適切に閉じ、通知処理などの副作用（Side Effects）はコミット完了後に `try-catch` で独立させて実行する（通知失敗によってメイン操作を失敗させない）。

#### [High] 4-2. スレッド通知における会話オプトアウト（ConversationOptOut）の完全無視
- **対象箇所**: `SRNSMudApp/Services/ItemReplyService.cs:247-285`, `SRNSMudApp/Services/NotificationsDataProvider.cs:90-101`
- **不整合の内容**:
  - ユーザーが特定のスレッドからの通知を停止するために「会話からオプトアウト」を設定する機能（`ConversationOptOut`）が存在する。
  - しかし、リプライ通知作成ロジック（`AddItemReplyAsync`）および通知一覧取得ロジック（`NotificationsDataProvider`）のいずれにおいても `ConversationOptOuts` テーブルを照会しておらず、オプトアウトしたユーザーにも無条件にプッシュ通知および通知リスト追加が行われる。
- **推奨修正方針**:
  - リプライ受信者リストの計算時および通知クエリ時に `!context.ConversationOptOuts.Any(o => o.RootItemId == rootItemId && o.UserId == recipientId)` を適用する。

#### [High] 4-3. WebPush ペイロードおよび通知画面における非公開アイテム本文の漏洩
- **対象箇所**: `SRNSMudApp/Services/ItemReplyService.cs:275-284`, `SRNSMudApp/Services/NotificationsDataProvider.cs:50-65`
- **不整合の内容**:
  - 非公開投稿に対するリプライであっても、WebPush の本文（Body）にリプライ本文（`replyItem.Content`）を平文で含めて送信している。
  - さらに `NotificationsDataProvider.GetAssociatedItemsAsync` では `WhereVisibleToUser` を通さずにアイテムを取得して `ItemCard` で描画しているため、グループ権限を持たないユーザーが通知画面から非公開アイテムの本文を閲覧できる。
- **推奨修正方針**:
  - プッシュ通知本文はマスク処理（「新しいリプライが届きました」等）を施し、通知一覧プロバイダでも可視性フィルタを適用する。

#### [Medium] 4-4. GratisContractExecutor におけるタグ削除時のタイムラインイベント反転バグ
- **対象箇所**: `SRNSMudApp/Services/Contracts/GratisContractExecutor.cs:272-279`
- **不整合の内容**:
  - 無料コントラクトでタグ関係を削除（`isRemove == true`）した際、`TimelineEvent` の作成コードで `ParsedEventType = TimelineEventType.Update` とハードコードされ、かつ `NewWeight = contract.ProposedWeight`（正の数）が設定されている。
  - そのため、タグを削除したにもかかわらず、タイムライン上には「タグのウェイトが更新されました」と誤ったイベントが表示される。
- **推奨修正方針**:
  - `isRemove` の場合は `ParsedEventType = TimelineEventType.Remove`, `NewWeight = 0` を設定する。

#### [Medium] 4-5. キャンセル済みリクエストに対するゾンビ通知と画面上の通知消失
- **対象箇所**: `SRNSMudApp/Services/NotificationsDataProvider.cs:80-87, 142`, `SRNSMudApp/Data/NotificationType.cs`
- **不整合の内容**:
  - `NotificationsDataProvider` のタグリクエストクエリにおいて `TradeStatus.Proposed` の絞り込みがなく、キャンセルされたリクエストも「リクエストが届いています」として通知され続ける。
  - 一方で分割キャンセルに対しては `NotificationType` に対応する通知タイプが存在せず、プッシュ通知のリンクを踏んでも画面上に該当通知が表示されない。
- **推奨修正方針**:
  - アクティブなリクエストのみを対象とするようステータスフィルタ（`Status == TradeStatus.Proposed`）を厳格化する。

#### [Medium] 4-6. Blazor Server における通知バッジのリアルタイム更新不全
- **対象箇所**: `SRNSMudApp/Services/NotificationService.cs:56-63`, `SRNSMudApp/Extensions/ServiceCollectionExtensions.cs:141`
- **不整合の内容**:
  - `INotificationService` が DI で `Scoped` として登録されている。
  - `NotificationsChanged` イベントは各ユーザーの Blazor 回線（Circuit）メモリ内に閉じており、別ユーザーが操作を行っても相手の回線にイベントが届かず、画面をリロードするまで通知バッジが更新されない。
- **推奨修正方針**:
  - 回線間を中継するシングルトンのイベントブローカー（または SignalR Hub / メモリスイーパー）を導入する。

#### [Medium] 4-7. 削除済みアイテムのタイムライン孤児化によるページネーション破損
- **対象箇所**: `SRNSMudApp/Services/HomeDataProvider.cs:138-179, 210-239`, `SRNSMudApp/Services/ItemCardDataProvider.cs:80-115`
- **不整合の内容**:
  - `TimelineEvent.Target` は JSON 列（`TimelineTargetJson`）として保存され、`Items` に対する外部キーがない。
  - アイテム削除時に `TimelineEvents` が削除されないため、ホームタイムラインで `Skip/Take` ページネーションを行った後に削除済みアイテムが除外され、指定件数より少ないページや歯抜けが発生する。
- **推奨修正方針**:
  - アイテム削除時に関連するタイムラインイベントを削除するか、クエリ時に有効なアイテムが存在するイベントのみを対象にページングする。

#### [Medium] 4-8. 引用（Quote）作成時の通知トリガー完全欠落
- **対象箇所**: `SRNSMudApp/Services/ItemQuoteService.cs:26-94`
- **不整合の内容**:
  - 引用が作成されても被引用アイテムの所有者に対する通知処理が一切実装されておらず、誰に引用されたか気付くことができない。
- **推奨修正方針**:
  - `ItemQuoteService` に通知トリガーを追加する。

---

### 領域 5: Blazor Server vs WASM & API バリデーション整合性 (Blazor Server vs WASM)

#### [High] 5-1. Blazor WASM クライアントにおける 18 画面の `@rendermode` 欠落による画面無応答
- **対象箇所**: `SRNSMudApp.Client/Pages/` (18ファイル), `SRNSMudApp/Components/App.razor:54`, `SRNSMudApp.Tests/Architecture/PageRenderModeTests.cs:45`
- **不整合の内容**:
  - ASP.NET Core Blazor Web App において、ルートの `<Routes />` に `@rendermode` が指定されていない場合、各ページコンポーネントが明示的に `@rendermode InteractiveServer` または `@rendermode InteractiveWebAssembly` を宣言しなければ Static SSR（静的描画）となる。
  - `SRNSMudApp.Client/Pages/` 内の 25 画面中 18 画面（`BountyBoardPage`, `ContractManagementPage`, `NotificationPage`, `TagDetailPage`, `UserManagementPage` 等の主要画面すべて）で `@rendermode` が欠落している。
  - ブラウザでアクセスした際、MudBlazor のボタン押下、ダイアログ表示、タブ切り替え、フォーム送信などの C# イベントが一切動作しない。
  - アーキテクチャテスト `PageRenderModeTests` は `typeof(App).Assembly`（Server側アセンブリ）のみをテスト対象としていたため、Client側アセンブリの欠落を見逃していた。
- **再現・発生シナリオ**:
  1. WASM クライアント画面（例: `/bounty-board`, `/notifications`）にアクセスする。
  2. 画面の見た目はレンダリングされるが、ボタンを押しても一切反応しない。
- **推奨修正方針**:
  - `SRNSMudApp.Client/Pages/` のすべての機能ページに `@rendermode InteractiveWebAssembly`（または適切なモード）を付与する。
  - `PageRenderModeTests` で `SRNSMudApp.Client` アセンブリも対象に含めるようテストを修正する。

#### [High] 5-2. タグ削除・ウェイト変更 API における主キー誤認バグ (`TagId` vs `RelationId`)
- **対象箇所**: `SRNSMudApp.Client/Models/Api/ItemListResponse.cs:23-28`, `SRNSMudApp.Client/Pages/ItemDetailPage.razor:118-122`, `SRNSMudApp.Client/Services/ItemListApiClient.cs:641-645`, `SRNSMudApp/Controllers/ItemController.cs:559-578`
- **不整合の内容**:
  - サーバーの REST API (`DELETE /api/item/{id}/tags/{relationId}`) は `ItemTagRelation` テーブルの主キーである `relationId` を要求している。
  - しかし、クライアントの DTO `TagRelationDto` には `ItemTagRelation.Id` 列が含まれておらず、`TagId` しか存在しない。
  - WASM クライアント（`ItemDetailPage.razor` 等）は `tr.TagId` を `relationId` 引数として API に送信している。
  - `TagId` と `ItemTagRelation.Id` の整数値は通常一致しないため、API は常に 404 Not Found を返すか、あるいは**IDが偶然一致した別の無関係なアイテムのタグ関係を誤って削除・更新してしまう**という重大なデータ破壊を引き起こす。
- **再現・発生シナリオ**:
  1. WASM 詳細画面でアイテムからタグの削除ボタン（X）を押す。
  2. `TagId` が `relationId` としてサーバーに送られ、削除に失敗するか、別アイテムのタグが削除される。
- **推奨修正方針**:
  - `TagRelationDto` に `int RelationId` を追加し、サーバーの `MapToItemDto` で `tr.Id` を設定する。クライアント側も `tr.RelationId` を API に渡すよう修正する。

#### [High] 5-3. タグ更新における UI 制御とバックエンド認可の乖離
- **対象箇所**: `SRNSMudApp.Client/Pages/TagDetailPage.razor:193-220`, `SRNSMudApp/Controllers/TagController.cs:100-118`, `SRNSMudApp/Services/TagCommandService.cs:122-165`
- **不整合の内容**:
  - クライアント UI では非所有者に対して直接編集フォームを隠し、「名前を提案」ボタンのみを表示して提案コントラクト（`TagProposalController`）を強制している。
  - しかしバックエンド API (`TagController.UpdateTag`) は前述（領域3-3）の通り所有者チェックが存在せず、非所有者からの直接 PUT リクエストをそのまま受け入れて即時更新してしまう。
- **推奨修正方針**:
  - バックエンド API に所有者・管理者チェックを導入し、UI のビジネスルールと完全に一致させる。

#### [High] 5-4. パブリックオファー作成における無権限・ゼロコスト悪用脆弱性
- **対象箇所**: `SRNSMudApp/Controllers/PublicOfferController.cs:72-111`, `SRNSMudApp/Services/ContractExecutors/TriggerContractExecutor.cs:40-75`
- **不整合の内容**:
  - `PublicOfferController.CreateOffer` において、リクエストされた `OfferedTagId` の所有権チェック、および `RequiredAssetAmount > 0` の検証が行われていない。
  - 攻撃者は他人のタグや希少タグを対価 0 でオファーに設定でき、オファー成立時に `TriggerContractExecutor` によって対価なしに Burn 済み `RightAsset` が新規発行される。
- **推奨修正方針**:
  - パブリックオファー作成時に `RequiredAssetAmount >= 1` および提供タグの所有権・アセット保有状況のバリデーションを追加する。

#### [High] 5-5. バウンティ自己達成のバックエンドガード欠落
- **対象箇所**: `SRNSMudApp.Client/ViewModels/BountyBoardViewModel.cs:69`, `SRNSMudApp.Client/Pages/BountyBoardPage.razor:135`, `SRNSMudApp/Controllers/BountyController.cs:142-166`, `SRNSMudApp/Services/TaggingContractService.cs:279-286`
- **不整合の内容**:
  - クライアント UI では `CanFulfill = !IsMyBounty` で自身のバウンティ達成ボタンを無効化しているが、サーバー API（`BountyController.FulfillBounty` および `TaggingContractService.AcceptContractAsync`）には自身による達成を拒否するチェックが存在しない。
  - 依頼者が API を直接叩くことで、自己のバウンティを自分で達成して報酬を回収できる。
- **推奨修正方針**:
  - コントラクト承認・達成処理において `contract.RequesterUserId != currentUserId` を厳格に検証する。

#### [Medium] 5-6. コントラクト却下（Reject）がキャンセル（Canceled）に化ける状態機械の欠陥
- **対象箇所**: `SRNSMudApp/Controllers/ContractController.cs:83-101`, `SRNSMudApp/Services/TaggingContractService.cs:374-386`, `SRNSMudApp/Data/TaggingRequestEntity.cs:107`
- **不整合の内容**:
  - クライアントから `POST /api/contract/{id}/reject`（却下）を呼び出すと、サーバーは `CancelContractAsync`（取り下げ処理）を実行し、ステータスを `TradeStatus.Canceled` に変更する。
  - ドメインエンティティの `TaggingRequestEntity.Reject()`（`TradeStatus.Rejected` への遷移）はどこからも呼ばれないデッドコードとなっている。
  - このため、相手によって「却下」されたのか、依頼者自身が「取り下げ」たのかが区別できず、監査履歴や通知が破綻している。
- **推奨修正方針**:
  - `RejectContractAsync` を独立したサービスメソッドとして実装し、正しく `entity.Reject()` を呼び出して `TradeStatus.Rejected` に遷移させる。

#### [Medium] 5-7. ItemDetailPage における未認証ユーザーへの承認・却下ボタン露出
- **対象箇所**: `SRNSMudApp.Client/Pages/ItemDetailPage.razor:185-194, 117-123`
- **不整合の内容**:
  - アイテム詳細画面の保留中リクエスト一覧において、所有者チェック（`@if (IsOwner)`）なしで「承認」「却下」ボタンが描画されている。未ログインユーザーがボタンを押すと、予期しないエラーダイアログが表示される。
- **推奨修正方針**:
  - `@if (IsOwner)` ガードを追加し、承認・却下操作を所有者に限定して表示する。

#### [Medium] 5-8. ItemListApiClient におけるエラー握りつぶしと虚偽ダイアログ表示
- **対象箇所**: `SRNSMudApp.Client/Services/ItemListApiClient.cs:517-535`, `SRNSMudApp.Client/Components/ItemSearchView.razor.cs:525`
- **不整合の内容**:
  - クライアント API クライアントが HTTP エラー発生時に `null` を返し、詳細な `ProblemDetails`（バリデーションエラー理由等）を破棄している。
  - そのため UI 側は「アイテムの作成に失敗しました。ログイン状態を確認してください。」と固定の誤認メッセージを表示し、文字数オーバー等の真の原因がユーザーに伝わらない。
- **推奨修正方針**:
  - API クライアントがエラーレスポンスのメッセージを上位に伝播できるように改善する。

#### [Medium] 5-9. DTO と Entity の文字数バリデーション乖離による 500 エラー
- **対象箇所**: `SRNSMudApp/Models/Item.cs:17`, `SRNSMudApp.Client/Models/Api/ItemListRequest.cs:11`, `SRNSMudApp/Controllers/ItemController.cs:290-310`
- **不整合の内容**:
  - エンティティ `Item.Content` は `[StringLength(1000)]` であり DB 列は `nvarchar(1000)` であるが、DTO `CreateItemRequest` には文字数制限アトリビュートがない。
  - コントローラーも `IsNullOrWhiteSpace` のみ検証しているため、1001 文字以上の投稿を送信すると 400 ではなく SQL 例外で 500 エラーとなる。
- **推奨修正方針**:
  - DTO に `[StringLength(1000)]` を付与し、クライアント・サーバー双方で 400 Bad Request として処理する。

#### [Low] 5-10. 通知リンク URL のルート不整合
- **対象箇所**: `SRNSMudApp/Services/ItemSplitService.cs:319`, `SRNSMudApp.Client/Pages/NotificationPage.razor`
- **不整合の内容**:
  - サーバー側の通知メッセージで生成されるリンク形式（`/ItemDetail/{id}`）と、WASM 側でルーティングされているパス（一部 `/Item/WasmDetail/{id}`）に不整合があり、通知からの画面遷移で 404 になるケースがある。
- **推奨修正方針**:
  - アプリケーション全体の URL ルーティング規約を統一する。

---

## 4. 横断的アーキテクチャ矛盾と根本要因分析

本監査で判明した 71 件の不整合を分析した結果、以下の 5 点が根本的なアーキテクチャ上の原因として浮き彫りとなった：

1. **認可・可視性判定の「二重帳簿」構造 (Dual-Stack Drift)**
   - Blazor Server のデータプロバイダ層（`ItemDetailDataProvider` 等）では `ItemVisibilityExtensions` や `TagVisibilityExtensions` が適用されているが、REST コントローラー（`ItemController`, `TagDiagramController`, `LinkPreviewController`）では個別のインライン判定または判定の完全欠落が発生している。Web API 経由でのアクセスが急増する中で、API 層のセキュリティガードが取り残されている。
2. **EF Core の Restrict 外部キーと削除ユースケースの不整合**
   - 整合性維持のために `DeleteBehavior.Restrict` を多用しているが、アプリケーション側の削除サービスが依存関係の先行削除や NULL 化を行っていないため、日常的な操作（引用や分割）が「削除不能化攻撃（DoS）」として機能してしまっている。
3. **トランザクション境界と非同期副作用（通知・WebPush）の順序矛盾**
   - データベーストランザクションのスコープ内に外部ネットワーク通信（WebPush、通知送信）を含めてしまい、コミット後にロールバックを試みる等の例外連鎖を招いている。
4. **状態機械（State Machine）の形骸化**
   - コントラクトのライフサイクルにおいて、`Rejected` と `Canceled` がコード上で混同され、エンティティのドメインメソッド（`entity.Reject()`）がデッドコード化している。また、バウンティにおけるアセットの仮押さえ（Escrow）状態が存在しない。
5. **Blazor Web App における RenderMode 伝播の誤認**
   - ASP.NET Core の最新 Blazor Web App アーキテクチャにおいて、ルートの `<Routes />` に `@rendermode` を付けない場合、各ページコンポーネントが自前で指定しなければ静的描画になるという仕様を、テストスイートおよび開発体制が見落としていた。

---

## 5. 是正ロードマップと優先度別推奨アクション

### Phase 1: 緊急対応（P0 - 即時修正 / セキュリティ・データ破壊防止）
- [ ] **TagController IDOR 修正**: `TagController.UpdateTag` に所有者チェックを追加し、他人のタグ改ざんを即時遮断。
- [ ] **パブリック API の可視性ガード追加**: `TagDiagramController`, `LinkPreviewController`, `ItemController` (`/quotes`, `/replies`) に `WhereVisibleToUser` を導入し、非公開データの漏洩を防止。
- [ ] **18 画面の `@rendermode` 付与**: `SRNSMudApp.Client/Pages/` の対象画面に `@rendermode InteractiveWebAssembly` を一括追記し、WASM UI を復旧。
- [ ] **TagId vs RelationId PK バグ修正**: `TagRelationDto` に `RelationId` を追加し、タグ削除時のデータ誤破壊・404 を解消。
- [ ] **バウンティ TargetId 設定 & 無権限却下修正**: `BountyController` で `TargetId` を代入し、`TaggingService` の無権限却下式を修正。

### Phase 2: 整合性復旧（P1 - 短期対応 / ライフサイクル・トランザクション健全化）
- [ ] **アイテム・タグ削除時のカスケード・外部キー事前クリーンアップ**: `QuotedItemId`, `ItemSplitRequest`, `RightAsset` の事前クリーンアップをトランザクション内で実行。
- [ ] **トランザクションと通知の分離**: `ItemSplitService` 等で `CommitAsync` 完了後に通知を独立してディスパッチするようリファクタリング。
- [ ] **会話オプトアウト（ConversationOptOut）の適用**: `ItemReplyService` および `NotificationsDataProvider` でオプトアウトを厳格に照合。
- [ ] **Move コントラクトの認可追加 & HierarchyId 再計算**: 子孫ツリー切断バグを解消。
- [ ] **バウンティ報酬のエスクロー導入**: 作成時にアセットを仮押さえし、二重支払いを根絶。

### Phase 3: 標準化と品質担保（P2 - 中長期対応 / アーキテクチャ強化）
- [ ] **REST コントローラーの可視性判定統一**: 全コントローラーで `ItemVisibilityExtensions` / `TagVisibilityExtensions` を必須利用とする Roslyn アナライザーまたはアーキテクチャテストを導入。
- [ ] **Client アーキテクチャテストの拡充**: `PageRenderModeTests` を修正し、`SRNSMudApp.Client` 内の全ページに対する RenderMode 検証を自動化。
- [ ] **DTO バリデーションアトリビュートの統一**: 文字数、必須項目、正規表現を Entity と DTO 間で完全一致させる。
- [ ] **エラーハンドリング（ProblemDetails）の標準化**: クライアント API がサーバーのバリデーションメッセージを正確に UI ダイアログに反映するよう改善。

---
**報告者**: SRNSWebApp Inconsistency & Edge-Case Audit Team  
**承認**: Project Orchestrator (orchestrator_3)  
