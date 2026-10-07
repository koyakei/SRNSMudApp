namespace SRNSMudApp.Components.Diagram;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Components;

using MudBlazor;

using TagEntity = SRNSMudApp.Data.Tag;

/// <summary>
///     ダイアグラム上のエッジ新規作成ダイアログコンポーネントのコードビハインド。
///     始点(Source)と終点(Target)タグのオートコンプリート検索、バリデーション、ダイアログ結果の返却を制御する。
/// </summary>
public partial class CreateEdgeDialog : ComponentBase
{
    [CascadingParameter]
    private IMudDialogInstance MudDialog { get; set; } = null!;

    [Inject]
    private IServiceProvider ServiceProvider { get; set; } = null!;

    public CreateEdgeViewModel ViewModel { get; private set; } = null!;

    [Parameter]
    public IReadOnlyList<TagEntity> AvailableTags { get; set; } = [];

    [Parameter]
    public TagEntity? InitialSourceTag { get; set; }

    [Parameter]
    public TagEntity? InitialTargetTag { get; set; }

    protected override void OnInitialized()
    {
        ViewModel = ServiceProvider.GetService<CreateEdgeViewModel>() ?? new CreateEdgeViewModel();
        ViewModel.Initialize(AvailableTags, InitialSourceTag, InitialTargetTag);
    }



    private Task<IEnumerable<TagEntity>> SearchSourceTags(string value, CancellationToken token) =>
        Task.FromResult(ViewModel.SearchSourceTags(value));

    private Task<IEnumerable<TagEntity>> SearchTargetTags(string value, CancellationToken token) =>
        Task.FromResult(ViewModel.SearchTargetTags(value));

    private void Cancel() => MudDialog.Cancel();

    private void Submit()
    {
        if (ViewModel.SubmitResult is { } result)
        {
            MudDialog.Close(DialogResult.Ok((SourceTagId: result.SourceTagId, TargetTagId: result.TargetTagId)));
        }
    }
}