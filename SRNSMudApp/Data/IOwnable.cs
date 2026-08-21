namespace SRNSMudApp.Data;

public interface IOwnable
{
    // 外部キー
    string OwnerId { get; set; }

    // ナビゲーションプロパティ
    ApplicationUser Owner { get; set; }

    bool IsOwnedBy(string userId) => OwnerId == userId;
}


