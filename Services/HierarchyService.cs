using Microsoft.EntityFrameworkCore;
using PragmaticBot.Server.Data;

namespace PragmaticBot.Server.Services;

/// <summary>
/// Everything about who-can-see-whom in the 본사 → 총판 → 대리점 → 회원 chain.
///
/// Visibility is a subtree test done with the materialised <see cref="User.TreePath"/>:
/// an account sees itself plus every row whose path starts with its own path.
/// </summary>
public class HierarchyService(AppDbContext db)
{
    /// <summary>The path a child of <paramref name="parent"/> gets.</summary>
    public static string ChildPathOf(User parent) => $"{parent.TreePath}{parent.Id}/";

    /// <summary>Path prefix that matches the account and everything under it.</summary>
    public static string SubtreePrefixOf(User user) => $"{user.TreePath}{user.Id}/";

    /// <summary>Roles an account is allowed to create — strictly below its own.</summary>
    public static IReadOnlyList<UserRole> CreatableRoles(UserRole role) => role switch
    {
        UserRole.Owner => [UserRole.Distributor, UserRole.Agency, UserRole.Member],
        UserRole.Distributor => [UserRole.Agency, UserRole.Member],
        UserRole.Agency => [UserRole.Member],
        _ => []
    };

    public static string Label(UserRole role) => role switch
    {
        UserRole.Owner => "본사",
        UserRole.Distributor => "총판",
        UserRole.Agency => "대리점",
        _ => "회원"
    };

    /// <summary>
    /// Every account <paramref name="viewer"/> may act on. The 본사 sees everything;
    /// anyone else sees their own branch, themselves included.
    /// </summary>
    public IQueryable<User> VisibleUsers(User viewer)
    {
        if (viewer.Role == UserRole.Owner)
            return db.Users;

        var prefix = SubtreePrefixOf(viewer);
        return db.Users.Where(u => u.Id == viewer.Id || u.TreePath.StartsWith(prefix));
    }

    /// <summary>Accounts below the viewer, excluding the viewer itself.</summary>
    public IQueryable<User> Descendants(User viewer)
    {
        if (viewer.Role == UserRole.Owner)
            return db.Users.Where(u => u.Id != viewer.Id);

        var prefix = SubtreePrefixOf(viewer);
        return db.Users.Where(u => u.TreePath.StartsWith(prefix));
    }

    /// <summary>Ids the viewer may see — use to scope betting rows, logs and settings.</summary>
    public Task<List<int>> VisibleUserIdsAsync(User viewer) =>
        VisibleUsers(viewer).Select(u => u.Id).ToListAsync();

    /// <summary>Loads a target only if the viewer is allowed to see it.</summary>
    public Task<User?> FindVisibleAsync(User viewer, int targetId) =>
        VisibleUsers(viewer).FirstOrDefaultAsync(u => u.Id == targetId);

    /// <summary>
    /// True when the viewer may change the target. You can manage anything strictly below you,
    /// never a peer or an ancestor, and never yourself through the user-management screens.
    /// </summary>
    public bool CanManage(User viewer, User target)
    {
        if (viewer.Id == target.Id)
            return false;
        if (viewer.Role <= target.Role)
            return false;
        if (viewer.Role == UserRole.Owner)
            return true;
        return target.TreePath.StartsWith(SubtreePrefixOf(viewer));
    }

    /// <summary>Creates an account directly under <paramref name="parent"/>.</summary>
    /// <summary>Korean label for a provider set, e.g. "프라그마틱 · 에볼루션".</summary>
    public static string ProvidersLabel(GameProviders p) => p switch
    {
        GameProviders.All => "전체",
        GameProviders.PragmaticPlay => "프라그마틱",
        GameProviders.Evolution => "에볼루션",
        _ => "없음"
    };

    /// <summary>
    /// A branch can only hand down what it holds itself, so the granted set is intersected with the
    /// parent's. Without this a 대리점 limited to Pragmatic could quietly give its members Evolution.
    /// </summary>
    public static GameProviders GrantableProviders(GameProviders parent, GameProviders wanted)
    {
        var granted = parent & wanted;
        return granted == GameProviders.None ? parent : granted;
    }

    public async Task<User> CreateUnderAsync(
        User parent, string username, string displayName, string password,
        UserRole role, DateTime? expirationUtc, GameProviders providers = GameProviders.PragmaticPlay)
    {
        if (!CreatableRoles(parent.Role).Contains(role))
            throw new InvalidOperationException($"{Label(parent.Role)}은(는) {Label(role)} 계정을 만들 수 없습니다.");

        var user = new User
        {
            Providers = GrantableProviders(parent.Providers, providers),
            Username = username,
            DisplayName = string.IsNullOrWhiteSpace(displayName) ? username : displayName,
            PasswordHash = PasswordHasher.Hash(password),
            Role = role,
            ParentId = parent.Id,
            TreePath = ChildPathOf(parent),
            IsActive = true,
            ExpirationDate = expirationUtc
        };

        db.Users.Add(user);
        await db.SaveChangesAsync();
        return user;
    }

    /// <summary>
    /// Re-parents an account and repairs the paths of everything beneath it. Used when a
    /// 대리점 moves to a different 총판.
    /// </summary>
    public async Task MoveAsync(User target, User newParent)
    {
        if (newParent.Id == target.Id)
            throw new InvalidOperationException("자기 자신의 하위로 옮길 수 없습니다.");

        var oldPrefix = SubtreePrefixOf(target);
        if (newParent.TreePath.StartsWith(oldPrefix) || newParent.Id == target.Id)
            throw new InvalidOperationException("자기 하위 조직으로는 옮길 수 없습니다.");

        var descendants = await db.Users.Where(u => u.TreePath.StartsWith(oldPrefix)).ToListAsync();

        target.ParentId = newParent.Id;
        target.TreePath = ChildPathOf(newParent);
        var newPrefix = SubtreePrefixOf(target);

        foreach (var d in descendants)
            d.TreePath = string.Concat(newPrefix, d.TreePath.AsSpan(oldPrefix.Length));

        await db.SaveChangesAsync();
    }

    /// <summary>Ancestors from the root down to (but excluding) the account itself.</summary>
    public async Task<List<User>> AncestorsOfAsync(User user)
    {
        var ids = user.TreePath.Split('/', StringSplitOptions.RemoveEmptyEntries)
            .Select(int.Parse)
            .ToList();
        if (ids.Count == 0)
            return [];

        var rows = await db.Users.AsNoTracking().Where(u => ids.Contains(u.Id)).ToListAsync();
        return ids.Select(id => rows.FirstOrDefault(r => r.Id == id)).Where(r => r is not null).ToList()!;
    }
}
