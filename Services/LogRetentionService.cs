using Microsoft.EntityFrameworkCore;
using PragmaticBot.Server.Data;

namespace PragmaticBot.Server.Services;

/// <summary>
/// Trims <c>AppLogs</c> on a schedule.
/// <para>
/// Purging used to be a button only the 본사 could press, so in practice nothing was ever deleted —
/// a single bot wrote 189,000 rows (63 MB) in thirteen hours. Info is the bulk of that and goes stale
/// within days; warnings and errors are what anyone actually looks back at, so they live far longer.
/// </para>
/// </summary>
public class LogRetentionService(IServiceScopeFactory scopes, ILogger<LogRetentionService> log)
    : BackgroundService
{
    public static readonly TimeSpan InfoRetention = TimeSpan.FromDays(3);
    public static readonly TimeSpan ProblemRetention = TimeSpan.FromDays(90);

    private static readonly TimeSpan Interval = TimeSpan.FromHours(6);

    /// <summary>Deleted per statement so a first run against a huge table cannot lock it for minutes.</summary>
    private const int BatchSize = 20_000;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // let the app finish starting before touching the database
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await PurgeAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                log.LogWarning(ex, "로그 자동 정리 실패");
            }

            try
            {
                await Task.Delay(Interval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    private async Task PurgeAsync(CancellationToken token)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var infoCutoff = DateTime.UtcNow - InfoRetention;
        var problemCutoff = DateTime.UtcNow - ProblemRetention;

        int info = await DeleteInBatchesAsync(
            db, l => l.Level == "Info" && l.CreatedUtc < infoCutoff, token);
        int problems = await DeleteInBatchesAsync(
            db, l => l.Level != "Info" && l.CreatedUtc < problemCutoff, token);

        if (info + problems > 0)
        {
            log.LogInformation("로그 자동 정리: Info {Info:N0}건({InfoDays}일 경과), 경고·오류 {Problems:N0}건({ProblemDays}일 경과)",
                info, InfoRetention.TotalDays, problems, ProblemRetention.TotalDays);
        }
    }

    private static async Task<int> DeleteInBatchesAsync(
        AppDbContext db,
        System.Linq.Expressions.Expression<Func<AppLog, bool>> predicate,
        CancellationToken token)
    {
        int total = 0;
        while (!token.IsCancellationRequested)
        {
            var ids = await db.AppLogs.AsNoTracking().Where(predicate)
                .OrderBy(l => l.Id).Select(l => l.Id).Take(BatchSize).ToListAsync(token);
            if (ids.Count == 0)
            {
                break;
            }
            total += await db.AppLogs.Where(l => ids.Contains(l.Id)).ExecuteDeleteAsync(token);
            if (ids.Count < BatchSize)
            {
                break;
            }
        }
        return total;
    }
}
