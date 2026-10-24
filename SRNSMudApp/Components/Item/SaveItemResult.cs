namespace SRNSMudApp.Components.Item;

/// <summary>
/// <see cref="AddItemViewModel.SaveItemAsync"/> の実行結果。
/// タプル (bool Success, string? ErrorMessage) への暗黙変換および分解に対応し、既存テストとの後方互換性を保持する。
/// </summary>
public readonly record struct SaveItemResult(bool Success, string? ErrorMessage, bool IsDuplicate = false)
{
    public void Deconstruct(out bool success, out string? errorMessage)
    {
        success = Success;
        errorMessage = ErrorMessage;
    }

    public void Deconstruct(out bool success, out string? errorMessage, out bool isDuplicate)
    {
        success = Success;
        errorMessage = ErrorMessage;
        isDuplicate = IsDuplicate;
    }

    public (bool Success, string? ErrorMessage) ToValueTuple() =>
        (Success, ErrorMessage);

    public static implicit operator (bool Success, string? ErrorMessage)(SaveItemResult result) =>
        result.ToValueTuple();
}