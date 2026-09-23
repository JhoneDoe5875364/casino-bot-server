using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PragmaticBot.Server.Contracts;
using PragmaticBot.Server.Data;

namespace PragmaticBot.Server.Controllers;

[Route("api/betting")]
public class BettingController(AppDbContext db) : ApiControllerBase
{
    const string Pending = "Pending";

    [HttpPost("history")]
    public async Task<IActionResult> SaveHistory([FromBody] BettingHistoryDto dto)
    {
        db.BettingHistories.Add(new BettingHistory
        {
            UserId = UserId,
            BetType = dto.BetType,
            BetAmount = dto.BetAmount,
            Status = dto.Status,
            BalanceBefore = dto.BalanceBefore,
            BalanceAfter = dto.BalanceAfter,
            IsVirtual = dto.IsVirtual,
            TableId = dto.TableId,
            GameId = dto.GameId,
            BetAmountType = dto.BetAmountType,
            MatrixRow = dto.MatrixRow,
            MatrixCol = dto.MatrixCol
        });
        await db.SaveChangesAsync();
        return Ok(new { ok = true });
    }

    [HttpPost("history/with-allocations")]
    public async Task<IActionResult> SaveHistoryWithAllocations([FromBody] BettingHistoryWithAllocationsDto dto)
    {
        db.BettingHistories.Add(new BettingHistory
        {
            UserId = UserId,
            BetType = dto.BetType,
            BetAmount = dto.BetAmount,
            Status = dto.Status,
            BalanceBefore = dto.BalanceBefore,
            BalanceAfter = dto.BalanceAfter,
            IsVirtual = dto.IsVirtual,
            TableId = dto.TableId,
            GameId = dto.GameId,
            BetAmountType = dto.BetAmountType,
            MatrixRow = dto.MatrixRow,
            MatrixCol = dto.MatrixCol,
            Allocations = (dto.Allocations ?? []).Select(a => new BettingAllocation
            {
                SiteSlot = a.SiteSlot,
                Domain = a.Domain,
                AllocatedAmount = a.AllocatedAmount,
                IsSent = a.IsSent,
                Status = a.Status
            }).ToList()
        });
        await db.SaveChangesAsync();
        return Ok(new { ok = true });
    }

    /// <summary>Closes out the pending bet for a round with its result.</summary>
    [HttpPost("history/settle")]
    public async Task<IActionResult> Settle([FromBody] SettleBettingRequest dto)
    {
        var bet = await FindPendingAsync(dto.BetType, dto.BetAmount, dto.TableId, dto.GameId, dto.IsVirtual);
        if (bet is null)
            return Fail(StatusCodes.Status404NotFound, "정산할 배팅 기록을 찾을 수 없습니다.");

        bet.Status = dto.Status;
        bet.WinAmount = dto.WinAmount;
        bet.Profit = dto.Profit;
        bet.SettledUtc = DateTime.UtcNow;
        await db.SaveChangesAsync();
        return Ok(new { ok = true });
    }

    [HttpPost("history/cancel")]
    public async Task<IActionResult> Cancel([FromBody] CancelBettingRequest dto)
    {
        var bet = await FindPendingAsync(dto.BetType, dto.BetAmount, dto.TableId, dto.GameId, null);
        if (bet is null)
            return Fail(StatusCodes.Status404NotFound, "취소할 배팅 기록을 찾을 수 없습니다.");

        bet.Status = "Cancelled";
        bet.SettledUtc = DateTime.UtcNow;
        await db.SaveChangesAsync();
        return Ok(new { ok = true });
    }

    [HttpPost("session")]
    public async Task<IActionResult> SaveSession([FromBody] BettingSessionRequest dto)
    {
        db.BettingSessions.Add(new BettingSession
        {
            UserId = UserId,
            StartBalance = dto.StartBalance,
            EndBalance = dto.EndBalance,
            TotalBetAmount = dto.TotalBetAmount,
            IsVirtual = dto.IsVirtual
        });
        await db.SaveChangesAsync();
        return Ok(new { ok = true });
    }

    /// <summary>Win/loss/tie tallies per matrix cell, for the 행렬 통계 window.</summary>
    [HttpGet("matrix-stats")]
    public async Task<IActionResult> GetMatrixStats(
        [FromQuery] DateTime? fromUtc,
        [FromQuery] DateTime? toUtc,
        [FromQuery] int? betAmountType,
        [FromQuery] bool includeVirtual = false)
    {
        var q = db.BettingHistories.AsNoTracking()
            .Where(b => b.UserId == UserId
                && b.BetAmountType != null
                && b.MatrixRow != null
                && b.MatrixCol != null);

        if (!includeVirtual)
            q = q.Where(b => !b.IsVirtual);
        if (fromUtc.HasValue)
            q = q.Where(b => b.CreatedUtc >= fromUtc.Value);
        if (toUtc.HasValue)
            q = q.Where(b => b.CreatedUtc <= toUtc.Value);
        if (betAmountType.HasValue)
            q = q.Where(b => b.BetAmountType == betAmountType.Value);

        var stats = await q
            .GroupBy(b => new { Type = b.BetAmountType!.Value, Row = b.MatrixRow!.Value, Col = b.MatrixCol!.Value })
            .Select(g => new MatrixStatDto(
                g.Key.Type,
                g.Key.Row,
                g.Key.Col,
                g.Count(x => x.Status == "Win"),
                g.Count(x => x.Status == "Loss"),
                g.Count(x => x.Status == "Tie"),
                g.Sum(x => x.BetAmount),
                g.Sum(x => x.Profit ?? 0m)))
            .ToListAsync();

        return Ok(stats.OrderBy(s => s.BetAmountType).ThenBy(s => s.MatrixRow).ThenBy(s => s.MatrixCol).ToList());
    }

    /// <summary>
    /// Matches the most recent still-open bet for the round. Table/game are the reliable keys;
    /// type and amount guard against settling the wrong row when several are open at once.
    /// </summary>
    async Task<BettingHistory?> FindPendingAsync(
        string betType, decimal betAmount, string? tableId, string? gameId, bool? isVirtual)
    {
        var q = db.BettingHistories.Where(b => b.UserId == UserId && b.Status == Pending);

        if (isVirtual.HasValue)
            q = q.Where(b => b.IsVirtual == isVirtual.Value);
        if (!string.IsNullOrEmpty(tableId))
            q = q.Where(b => b.TableId == tableId);
        if (!string.IsNullOrEmpty(gameId))
            q = q.Where(b => b.GameId == gameId);

        var exact = await q
            .Where(b => b.BetType == betType && b.BetAmount == betAmount)
            .OrderByDescending(b => b.Id)
            .FirstOrDefaultAsync();

        // the bot may round the amount it reports back, so fall back to the round's newest bet
        return exact ?? await q.OrderByDescending(b => b.Id).FirstOrDefaultAsync();
    }
}
