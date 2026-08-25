using Microsoft.EntityFrameworkCore;
using SRNSMudApp.Data;

namespace SRNSMudApp.Tests.TestSupport;

public static class DbContextExtensions
{
    /// <summary>
    /// 指定されたユーザーID群を DB に登録する（未登録のもののみ）。
    /// </summary>
    public static async Task SeedUsersAsync(this ApplicationDbContext ctx, params string[] userIds)
    {
        foreach (var id in userIds)
        {
            if (!await ctx.Users.AnyAsync(u => u.Id == id))
            {
                _ = ctx.Users.Add(new ApplicationUser
                {
                    Id = id,
                    UserName = id,
                    NormalizedUserName = id.ToUpperInvariant()
                });
            }
        }
        _ = await ctx.SaveChangesAsync();
    }
}
