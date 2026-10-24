namespace SRNSMudApp.Controllers;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

using SRNSMudApp.Client.Models.Api;
using SRNSMudApp.Models;
using SRNSMudApp.Services;

/// <summary>
/// タグの一括インポートに関する Web API コントローラー。
/// </summary>
[ApiController]
[Route("api/tag/import")]
[Authorize]
public sealed class TagImportController(
    IImportTagDataProvider importTagDataProvider,
    ITaggingImportDataProvider? taggingImportDataProvider = null) : ControllerBase
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly IImportTagDataProvider _importTagDataProvider =
        importTagDataProvider ?? throw new ArgumentNullException(nameof(importTagDataProvider));
    private readonly ITaggingImportDataProvider? _taggingImportDataProvider = taggingImportDataProvider;

    /// <summary>
    /// CSV 形式のタグ階層データを親タグ配下にインポートします。
    /// </summary>
    [HttpPost("csv")]
    public async Task<ActionResult<ImportTagCsvResponse>> ImportCsv(
        [FromBody] ImportTagCsvRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        string? currentUserId = User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(currentUserId))
        {
            return Unauthorized("インポートを実行するにはログインが必要です。");
        }

        if (string.IsNullOrWhiteSpace(request.ParentTagName))
        {
            return BadRequest("親タグ名の指定は必須です。");
        }

        if (string.IsNullOrWhiteSpace(request.CsvContent))
        {
            return BadRequest("インポートするCSVデータが空です。");
        }

        bool asSystem = (User?.IsInRole("Admin") ?? false) && request.AsSystem;

        TagImportResult result = await _importTagDataProvider.ImportCsvTagsAsync(
            currentUserId,
            request.ParentTagName.Trim(),
            request.CsvContent,
            asSystem);

        string message = $"{result.CreatedCount}個のタグを新規作成、{result.UpdatedCount}個のタグを更新しました。";
        return Ok(new ImportTagCsvResponse(result.CreatedCount, result.UpdatedCount, message));
    }

    /// <summary>
    /// JSON 形式のタグ付け構造データ（Item, TagRelation, TagEdge）を一括インポートします。
    /// </summary>
    [HttpPost("tagging")]
    public async Task<ActionResult<ImportTaggingResponse>> ImportTagging(
        [FromBody] ImportTaggingJsonRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (_taggingImportDataProvider is null)
        {
            return StatusCode(StatusCodes.Status501NotImplemented);
        }

        string? currentUserId = User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(currentUserId))
        {
            return Unauthorized("インポートを実行するにはログインが必要です。");
        }

        if (string.IsNullOrWhiteSpace(request.JsonText))
        {
            return BadRequest("JSONテキストが空です。");
        }

        TaggingImportRoot? root;
        try
        {
            root = JsonSerializer.Deserialize<TaggingImportRoot>(request.JsonText, JsonOptions);
        }
        catch (JsonException ex)
        {
            return BadRequest($"JSONの解析に失敗しました: {ex.Message}");
        }

        if (root?.TaggingRequestEntity is null)
        {
            return BadRequest("有効な TaggingRequestEntity が含まれていません。");
        }

        TaggingImportPayload payload = root.TaggingRequestEntity;
        Dictionary<string, int> relationIdToTagMap = [];

        // 管理者のみシステムタグとしてインポート可能 (Issue 3-7)
        bool isAdmin = User?.IsInRole("Admin") ?? false;

        // 1. TagRelations のタグを検索または自動作成
        foreach (var relDto in payload.TagRelations)
        {
            if (string.IsNullOrWhiteSpace(relDto.Tag?.Name)) continue;

            bool isSystem = isAdmin && string.Equals(relDto.Tag.TagKind, "SystemClassificationTag", StringComparison.OrdinalIgnoreCase);
            // 非同期 null 合体演算子 (?? await) によるタグ取得または自動生成の集約
            var tag = await _taggingImportDataProvider.FindExistingTagAsync(
                relDto.Tag.Name, currentUserId, isSystem, cancellationToken)
                ?? await _taggingImportDataProvider.CreateTagAsync(
                    relDto.Tag.Name, currentUserId, isSystem, parentTagId: null, cancellationToken);

            relationIdToTagMap[relDto.RelationId] = tag.Id;
        }

        // 2. TagEdges の AppliedTags を検索または自動作成
        foreach (var edgeDto in payload.TagEdges)
        {
            foreach (var appliedTagName in edgeDto.AppliedTags)
            {
                if (string.IsNullOrWhiteSpace(appliedTagName)) continue;

                var existingTag = await _taggingImportDataProvider.FindExistingTagAsync(
                    appliedTagName, currentUserId, false, cancellationToken);

                if (existingTag is null)
                {
                    _ = await _taggingImportDataProvider.CreateTagAsync(
                        appliedTagName, currentUserId, false, parentTagId: null, cancellationToken);
                }
            }
        }

        // 3. Item コンテンツの抽出
        var processedContents = payload.Item.Select(i => i.Content).ToList();

        // 4. データベースへの永続化トランザクション実行
        TaggingImportResult importResult = await _taggingImportDataProvider.ExecuteImportAsync(
            currentUserId,
            payload,
            relationIdToTagMap,
            processedContents,
            cancellationToken);

        return Ok(new ImportTaggingResponse(
            CreatedItemsCount: importResult.CreatedItemsCount,
            CreatedTagsCount: importResult.CreatedTagsCount,
            CreatedRelationsCount: importResult.CreatedRelationsCount,
            CreatedEdgesCount: importResult.CreatedEdgesCount,
            CreatedAttachmentsCount: importResult.CreatedAttachmentsCount,
            Messages: importResult.Messages
        ));
    }
}