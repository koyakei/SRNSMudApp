namespace SRNSMudApp.Controllers;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

using SRNSMudApp.Client.Models.Api;
using SRNSMudApp.Models;
using SRNSMudApp.Services;

/// <summary>
/// リンクプレビュー取得および内部タグリンク自動変換に関する API を提供するコントローラー。
/// </summary>
[ApiController]
[Route("api/link")]
public sealed class LinkPreviewController(
    ILinkPreviewService linkPreviewService,
    IInternalLinkConversionService internalLinkConversionService) : ControllerBase
{
    private readonly ILinkPreviewService _linkPreviewService =
        linkPreviewService ?? throw new ArgumentNullException(nameof(linkPreviewService));
    private readonly IInternalLinkConversionService _internalLinkConversionService =
        internalLinkConversionService ?? throw new ArgumentNullException(nameof(internalLinkConversionService));

    /// <summary>
    /// 指定された URL（外部 URL または内部リンク）のメタデータ・OGP プレビューを取得します。
    /// </summary>
    [HttpPost("preview")]
    [AllowAnonymous]
    public async Task<ActionResult<LinkPreviewDto>> GetPreview(
        [FromBody] LinkPreviewRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(request.Url))
        {
            return BadRequest("URL は必須です。");
        }

        string? currentUserId = User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        bool isAdmin = User?.IsInRole("Admin") ?? false;

        LinkPreviewData preview = await _linkPreviewService.GetPreviewAsync(
            request.Url.Trim(), currentUserId, isAdmin, cancellationToken);

        var tags = preview.Tags
            .Select(t => new TagPreviewItemDto(t.Id, t.Name, t.OwnerName, t.Weight))
            .ToList();

        var dto = new LinkPreviewDto(
            Url: preview.Url,
            Title: preview.Title,
            Description: preview.Description,
            ImageUrl: preview.ImageUrl,
            SiteName: preview.SiteName,
            IsSuccess: preview.IsSuccess,
            DisplayText: preview.DisplayText,
            Tags: tags
        );

        return Ok(dto);
    }

    /// <summary>
    /// 入力テキストからタグ候補を検出し、内部リンク置換候補を返却します。
    /// </summary>
    [HttpPost("convert-candidates")]
    [AllowAnonymous]
    public async Task<ActionResult<InternalLinkConversionResultDto>> DetectLinkCandidates(
        [FromBody] DetectLinkCandidatesRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(request.Content))
        {
            return Ok(new InternalLinkConversionResultDto([], []));
        }

        InternalLinkConversionResult result = await _internalLinkConversionService.DetectLinkCandidatesAsync(
            request.Content, request.AutoReplaceThreshold, cancellationToken);

        var autoCandidates = result.AutoReplaceCandidates
            .Select(c => new LinkConversionCandidateDto(
                c.OriginalText, c.TagId, c.TagName, c.Similarity, c.StartIndex, c.Length, c.IsAutoReplace))
            .ToList();

        var manualCandidates = result.ManualCandidates
            .Select(c => new LinkConversionCandidateDto(
                c.OriginalText, c.TagId, c.TagName, c.Similarity, c.StartIndex, c.Length, c.IsAutoReplace))
            .ToList();

        return Ok(new InternalLinkConversionResultDto(autoCandidates, manualCandidates));
    }

    /// <summary>
    /// 選択された変換候補をテキストに適用し、内部タグリンクに置換した文字列を返却します。
    /// </summary>
    [HttpPost("apply-replacements")]
    [AllowAnonymous]
    public ActionResult<ApplyLinkReplacementsResponse> ApplyReplacements(
        [FromBody] ApplyLinkReplacementsRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(request.Content) || request.Candidates.Count == 0)
        {
            return Ok(new ApplyLinkReplacementsResponse(request.Content ?? string.Empty));
        }

        var domainCandidates = request.Candidates
            .Select(c => new LinkConversionCandidate(
                c.OriginalText, c.TagId, c.TagName, c.Similarity, c.StartIndex, c.Length, c.IsAutoReplace))
            .ToList();

        string convertedContent = _internalLinkConversionService.ApplyReplacements(request.Content, domainCandidates);
        return Ok(new ApplyLinkReplacementsResponse(convertedContent));
    }
}