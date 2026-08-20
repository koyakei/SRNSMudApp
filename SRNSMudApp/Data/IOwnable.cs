namespace SRNSMudApp.Data;

public interface IOwnable
{
    
    // 外部キー
    public string OwnerId { get; set; }

    // ナビゲーションプロパティ
    public ApplicationUser Owner { get; set; }

    public bool IsOwnedBy(string userId)
    {
        return OwnerId == userId;
    }
}


