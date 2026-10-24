// CA1508: union 型 (BurnStatus) の網羅的パターンマッチにおける解析器の誤検知のため抑制する。
#pragma warning disable CA1508

#region

using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json;

using SRNSMudApp.Models.Unions;

#endregion

namespace SRNSMudApp.Data;

public class RightAsset : BaseEntity
{
    // アセットの価値や量
    public int Amount { get; set; }

    // 論理削除（Burn）フラグ
    public bool IsBurned { get; set; }

    // 燃焼日時や状態 (Serialized JSON of BurnStatus union)
    public string BurnStatusJson { get; set; } = string.Empty;

    [NotMapped]
    public BurnStatus Status
    {
        get
        {
            if (!IsBurned || string.IsNullOrEmpty(BurnStatusJson))
            {
                return new NotBurned();
            }

            try
            {
                Burned? burned = JsonSerializer.Deserialize<Burned>(BurnStatusJson);
                return burned is not null ? burned : new NotBurned();
            }
            catch (JsonException)
            {
                return new NotBurned();
            }
        }
        set
        {
            if (value is Burned burned)
            {
                BurnStatusJson = JsonSerializer.Serialize(burned);
            }
            else
            {
                BurnStatusJson = string.Empty;
            }
        }
    }

    // 対象となるタグ
    public int TargetTagId { get; set; }
    public Tag TargetTag { get; set; } = null!;

    // 楽観的ロック用バージョン（同一アカウントからの同時操作による残高の不整合防止）
    [Timestamp]
#pragma warning disable CA1819
    public byte[] Version { get; set; } = null!;
#pragma warning restore CA1819

    /// <summary>
    ///     指定されたユーザーおよび対象タグに対して、指定数量のアセットが消費可能かを判定する。
    /// </summary>
    /// <param name="ownerId">所有者ユーザーID。</param>
    /// <param name="targetTagId">消費対象のタグID。</param>
    /// <param name="amount">消費要求数量。</param>
    /// <returns>消費可能な状態であれば true。</returns>
    public bool CanBeConsumedBy(string ownerId, int targetTagId, int amount) =>
        !IsBurned && OwnerId == ownerId && TargetTagId == targetTagId && Amount >= amount && amount > 0;

    /// <summary>
    ///     指定された数量のアセットを消費する。残量が消費量以下の場合は残量 0 となり Burned 状態となる。
    /// </summary>
    /// <param name="weightToConsume">消費する数量（1以上）。</param>
    /// <param name="burnedAt">Burned 状態になった場合の記録日時（UTC）。省略時は DateTime.UtcNow。</param>
    public void Consume(int weightToConsume, DateTime? burnedAt = null)
    {
        if (weightToConsume <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(weightToConsume), "消費量は1以上である必要があります。");
        }

        if (Amount <= weightToConsume)
        {
            Amount = 0;
            IsBurned = true;
            Status = new Burned(burnedAt ?? DateTime.UtcNow);
        }
        else
        {
            Amount -= weightToConsume;
        }
    }

    /// <summary>
    ///     アセットを完全に燃焼（Burn）させる。
    /// </summary>
    /// <param name="burnedAt">Burned 状態の記録日時（UTC）。省略時は DateTime.UtcNow。</param>
    public void Burn(DateTime? burnedAt = null)
    {
        Amount = 0;
        IsBurned = true;
        Status = new Burned(burnedAt ?? DateTime.UtcNow);
    }
}