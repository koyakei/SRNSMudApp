using SRNSMudApp.Data;
using SRNSMudApp.Models.Unions;
using SRNSMudApp.Resources;

namespace SRNSMudApp.Tests.Data;

/// <summary>
///     ドメインエンティティ（TaggingRequestEntity, ContentReport）の状態遷移における
///     Result<T> エラーハンドリングと Resource Pattern の検証テスト。
/// </summary>
public class DomainEntityResultTests
{
    [Fact]
    public void TaggingRequestEntity_Execute_ReturnsSuccess_WhenProposed()
    {
        var entity = new TaggingRequestEntity { OwnerId = "user1" };
        Assert.Equal(TradeStatus.Proposed, entity.Status);

        Result<bool> result = entity.Execute();

        Assert.True(result is Success<bool>);
        Assert.Equal(TradeStatus.Executed, entity.Status);
    }

    [Fact]
    public void TaggingRequestEntity_Execute_IsIdempotent_WhenAlreadyExecuted()
    {
        var entity = new TaggingRequestEntity { OwnerId = "user1" };
        _ = entity.Execute();

        Result<bool> secondResult = entity.Execute();

        Assert.True(secondResult is Success<bool>);
        Assert.Equal(TradeStatus.Executed, entity.Status);
    }

    [Fact]
    public void TaggingRequestEntity_Execute_ReturnsFailure_WhenCanceled()
    {
        var entity = new TaggingRequestEntity { OwnerId = "user1" };
        _ = entity.Cancel();

        Result<bool> result = entity.Execute();

        Assert.True(result is Failure);
        if (result is Failure failure)
        {
            Assert.Contains("状態 'Canceled' から Executed への遷移は許可されていません。", failure.ErrorMessage);
        }
    }

    [Fact]
    public void TaggingRequestEntity_Cancel_ReturnsFailure_WhenExecuted()
    {
        var entity = new TaggingRequestEntity { OwnerId = "user1" };
        _ = entity.Execute();

        Result<bool> result = entity.Cancel();

        Assert.True(result is Failure);
        if (result is Failure failure)
        {
            Assert.Contains("状態 'Executed' から Canceled への遷移は許可されていません。", failure.ErrorMessage);
        }
    }

    [Fact]
    public void ContentReport_MarkAsReviewed_ReturnsSuccess_WhenPending()
    {
        var report = new ContentReport { OwnerId = "user1" };
        Assert.Equal(ReportStatus.Pending, report.Status);

        Result<bool> result = report.MarkAsReviewed("admin1", "問題なし");

        Assert.True(result is Success<bool>);
        Assert.Equal(ReportStatus.Reviewed, report.Status);
        Assert.Equal("admin1", report.HandledByAdminId);
        Assert.Equal("問題なし", report.ResolutionNote);
    }

    [Fact]
    public void ContentReport_TakeAction_ReturnsSuccess_WhenReviewed()
    {
        var report = new ContentReport { OwnerId = "user1" };
        _ = report.MarkAsReviewed("admin1");

        Result<bool> result = report.TakeAction("admin2", "コンテンツ削除");

        Assert.True(result is Success<bool>);
        Assert.Equal(ReportStatus.ActionTaken, report.Status);
        Assert.Equal("admin2", report.HandledByAdminId);
        Assert.Equal("コンテンツ削除", report.ResolutionNote);
    }

    [Fact]
    public void ContentReport_MarkAsReviewed_ReturnsFailure_WhenAlreadyActionTaken()
    {
        var report = new ContentReport { OwnerId = "user1" };
        _ = report.TakeAction("admin1", "削除完了");

        Result<bool> result = report.MarkAsReviewed("admin2");

        Assert.True(result is Failure);
        if (result is Failure failure)
        {
            Assert.Contains("状態 'ActionTaken' から Reviewed への遷移は許可されていません。", failure.ErrorMessage);
        }
    }

    [Fact]
    public void ErrorMessages_ReturnsExpectedResourceStrings()
    {
        Assert.False(string.IsNullOrWhiteSpace(ErrorMessages.LoginRequired));
        Assert.False(string.IsNullOrWhiteSpace(ErrorMessages.ContractApproveSuccess));
        Assert.Contains("テスト", ErrorMessages.FormatReportNotFound("テスト"));
    }
}
