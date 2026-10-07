using Microsoft.AspNetCore.Identity;

using SRNSMudApp.Data;
using SRNSMudApp.Services;

namespace SRNSMudApp.Components.User;

/// <summary>
///     UserDetail コンポーネントにおけるユーザー操作を集約する ViewModel（後方互換用）。
///     新コードでは <see cref="UserDetailViewModel" /> を使用する。
/// </summary>
public class UserDetailActionViewModel : UserDetailViewModel
{
    public UserDetailActionViewModel(
        IUserDataProvider userDataProvider,
        UserManager<ApplicationUser>? userManager = null,
        IServiceScopeFactory? scopeFactory = null)
        : base(userDataProvider, userManager, scopeFactory)
    {
    }
}