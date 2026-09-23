using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PragmaticBot.Server.Contracts;
using PragmaticBot.Server.Data;

namespace PragmaticBot.Server.Controllers;

[Route("api/settings")]
public class SettingsController(AppDbContext db) : ApiControllerBase
{
    // ---------- 서식 (betting patterns) ----------

    [HttpGet("formats")]
    public async Task<IActionResult> GetFormats() =>
        Ok(await db.FormatSettings.AsNoTracking()
            .Where(f => f.UserId == UserId)
            .OrderBy(f => f.Number)
            .Select(f => new FormatSettingDto(f.Number, f.Text))
            .ToListAsync());

    [HttpPost("formats")]
    public async Task<IActionResult> SaveFormats([FromBody] List<FormatSettingDto> formats)
    {
        await ReplaceAsync(
            db.FormatSettings.Where(f => f.UserId == UserId),
            formats ?? [],
            dto => new FormatSetting { UserId = UserId, Number = dto.Number, Text = dto.Text });
        return Ok(new { ok = true });
    }

    // ---------- 예외 서식 ----------

    [HttpGet("exception-formats")]
    public async Task<IActionResult> GetExceptionFormats() =>
        Ok(await db.ExceptionFormats.AsNoTracking()
            .Where(f => f.UserId == UserId)
            .OrderBy(f => f.Number)
            .Select(f => new ExceptionFormatDto(f.Number, f.Pattern))
            .ToListAsync());

    [HttpPost("exception-formats")]
    public async Task<IActionResult> SaveExceptionFormats([FromBody] List<ExceptionFormatDto> formats)
    {
        await ReplaceAsync(
            db.ExceptionFormats.Where(f => f.UserId == UserId),
            formats ?? [],
            dto => new ExceptionFormat { UserId = UserId, Number = dto.Number, Pattern = dto.Pattern });
        return Ok(new { ok = true });
    }

    // ---------- 방 게이트 서식 ----------

    [HttpGet("room-gate-formats")]
    public async Task<IActionResult> GetRoomGateFormats() =>
        Ok(await db.RoomGateFormats.AsNoTracking()
            .Where(f => f.UserId == UserId)
            .OrderBy(f => f.GateType).ThenBy(f => f.Number)
            .Select(f => new RoomGateFormatDto(f.Number, f.GateType, f.Pattern))
            .ToListAsync());

    [HttpPost("room-gate-formats")]
    public async Task<IActionResult> SaveRoomGateFormats([FromBody] List<RoomGateFormatDto> formats)
    {
        await ReplaceAsync(
            db.RoomGateFormats.Where(f => f.UserId == UserId),
            formats ?? [],
            dto => new RoomGateFormat
            {
                UserId = UserId,
                Number = dto.Number,
                GateType = dto.GateType,
                Pattern = dto.Pattern
            });
        return Ok(new { ok = true });
    }

    // ---------- 배팅 금액 ----------

    [HttpGet("bet-amounts")]
    public async Task<IActionResult> GetBetAmounts([FromQuery] int betAmountType = 0) =>
        Ok(await db.BetAmountSettings.AsNoTracking()
            .Where(b => b.UserId == UserId && b.BetAmountType == betAmountType)
            .OrderBy(b => b.Number)
            .Select(b => new BetAmountSettingDto(b.Number, b.Amount, b.BetAmountType, b.BettingMethod))
            .ToListAsync());

    /// <summary>
    /// The payload can carry several bet-amount types at once, so only the types it mentions
    /// are replaced — the others stay untouched.
    /// </summary>
    [HttpPost("bet-amounts")]
    public async Task<IActionResult> SaveBetAmounts([FromBody] List<BetAmountSettingDto> betAmounts)
    {
        betAmounts ??= [];
        var types = betAmounts.Select(b => b.BetAmountType).Distinct().ToList();

        var stale = db.BetAmountSettings.Where(b => b.UserId == UserId && types.Contains(b.BetAmountType));
        db.BetAmountSettings.RemoveRange(await stale.ToListAsync());

        db.BetAmountSettings.AddRange(betAmounts.Select(dto => new BetAmountSetting
        {
            UserId = UserId,
            BetAmountType = dto.BetAmountType,
            Number = dto.Number,
            Amount = dto.Amount,
            BettingMethod = dto.BettingMethod
        }));

        await db.SaveChangesAsync();
        return Ok(new { ok = true });
    }

    // ---------- 행렬 (matrix) ----------

    [HttpGet("matrix-cells")]
    public async Task<IActionResult> GetMatrixCells([FromQuery] int betAmountType = 11) =>
        Ok(await db.MatrixCells.AsNoTracking()
            .Where(c => c.UserId == UserId && c.BetAmountType == betAmountType)
            .OrderBy(c => c.RowIndex).ThenBy(c => c.ColIndex)
            .Select(c => new MatrixCellDto(c.RowIndex, c.ColIndex, c.Amount))
            .ToListAsync());

    [HttpPost("matrix-cells")]
    public async Task<IActionResult> SaveMatrixCells(
        [FromBody] List<MatrixCellDto> cells, [FromQuery] int betAmountType = 11)
    {
        await ReplaceAsync(
            db.MatrixCells.Where(c => c.UserId == UserId && c.BetAmountType == betAmountType),
            cells ?? [],
            dto => new MatrixCell
            {
                UserId = UserId,
                BetAmountType = betAmountType,
                RowIndex = dto.RowIndex,
                ColIndex = dto.ColIndex,
                Amount = dto.Amount
            });
        return Ok(new { ok = true });
    }

    [HttpGet("matrix-row-rules")]
    public async Task<IActionResult> GetMatrixRowRules([FromQuery] int betAmountType = 11) =>
        Ok(await db.MatrixRowRules.AsNoTracking()
            .Where(r => r.UserId == UserId && r.BetAmountType == betAmountType)
            .OrderBy(r => r.RowIndex)
            .Select(r => new MatrixRowRuleDto(
                r.RowIndex, r.WinCount, r.TargetRow, r.WinMode, r.RestMinutes, r.FirstRowCapAmount))
            .ToListAsync());

    [HttpPost("matrix-row-rules")]
    public async Task<IActionResult> SaveMatrixRowRules(
        [FromBody] List<MatrixRowRuleDto> rules, [FromQuery] int betAmountType = 11)
    {
        await ReplaceAsync(
            db.MatrixRowRules.Where(r => r.UserId == UserId && r.BetAmountType == betAmountType),
            rules ?? [],
            dto => new MatrixRowRule
            {
                UserId = UserId,
                BetAmountType = betAmountType,
                RowIndex = dto.RowIndex,
                WinCount = dto.WinCount,
                TargetRow = dto.TargetRow,
                WinMode = dto.WinMode,
                RestMinutes = dto.RestMinutes,
                FirstRowCapAmount = dto.FirstRowCapAmount
            });
        return Ok(new { ok = true });
    }

    // ---------- 한도 ----------

    [HttpGet("limits")]
    public async Task<IActionResult> GetLimits()
    {
        var l = await db.LimitSettings.AsNoTracking().FirstOrDefaultAsync(x => x.UserId == UserId);
        return Ok(l is null
            ? new LimitSettingDto(null, false, null, false)
            : new LimitSettingDto(
                l.UpperLimitPercent, l.IsUpperLimitEnabled, l.LowerLimitPercent, l.IsLowerLimitEnabled,
                l.MaxActiveBettingRooms, l.UpperLimitMode, l.TargetProfitAmount, l.IsTargetCycleEnabled,
                l.RestIntervalMinutes, l.SessionCount, l.KeepAliveTableId));
    }

    [HttpPost("limits")]
    public async Task<IActionResult> SaveLimits([FromBody] LimitSettingDto dto)
    {
        var l = await db.LimitSettings.FirstOrDefaultAsync(x => x.UserId == UserId);
        if (l is null)
        {
            l = new LimitSetting { UserId = UserId };
            db.LimitSettings.Add(l);
        }
        l.UpperLimitPercent = dto.UpperLimitPercent;
        l.IsUpperLimitEnabled = dto.IsUpperLimitEnabled;
        l.LowerLimitPercent = dto.LowerLimitPercent;
        l.IsLowerLimitEnabled = dto.IsLowerLimitEnabled;
        l.MaxActiveBettingRooms = dto.MaxActiveBettingRooms;
        l.UpperLimitMode = dto.UpperLimitMode;
        l.TargetProfitAmount = dto.TargetProfitAmount;
        l.IsTargetCycleEnabled = dto.IsTargetCycleEnabled;
        l.RestIntervalMinutes = dto.RestIntervalMinutes;
        l.SessionCount = dto.SessionCount;
        l.KeepAliveTableId = dto.KeepAliveTableId;
        await db.SaveChangesAsync();
        return Ok(new { ok = true });
    }

    // ---------- 방 쿨다운 ----------

    [HttpGet("room-cooldown")]
    public async Task<IActionResult> GetRoomCooldown()
    {
        var s = await db.RoomCooldownSettings.AsNoTracking().FirstOrDefaultAsync(x => x.UserId == UserId);
        // a user who has never opened the dialog simply has the feature off — that is an answer,
        // not a failure, so hand back the same defaults the form would show (cf. GetLimits)
        return Ok(s is null
            ? new RoomCooldownSettingDto(false, 5, 3)
            : new RoomCooldownSettingDto(s.IsEnabled, s.LossThreshold, s.CooldownMinutes));
    }

    [HttpPut("room-cooldown")]
    public async Task<IActionResult> SaveRoomCooldown([FromBody] RoomCooldownSettingDto dto)
    {
        var s = await db.RoomCooldownSettings.FirstOrDefaultAsync(x => x.UserId == UserId);
        if (s is null)
        {
            s = new RoomCooldownSetting { UserId = UserId };
            db.RoomCooldownSettings.Add(s);
        }
        s.IsEnabled = dto.IsEnabled;
        s.LossThreshold = dto.LossThreshold;
        s.CooldownMinutes = dto.CooldownMinutes;
        await db.SaveChangesAsync();
        return Ok(new { ok = true });
    }

    // ---------- 전략 체인 ----------

    [HttpGet("strategy-chains")]
    public async Task<IActionResult> GetChains()
    {
        var chains = await db.StrategyChains.AsNoTracking()
            .Include(c => c.Steps)
            .Where(c => c.UserId == UserId)
            .OrderBy(c => c.Id)
            .ToListAsync();
        return Ok(chains.Select(ToDto).ToList());
    }

    [HttpGet("strategy-chains/active")]
    public async Task<IActionResult> GetActiveChain()
    {
        var chain = await db.StrategyChains.AsNoTracking()
            .Include(c => c.Steps)
            .FirstOrDefaultAsync(c => c.UserId == UserId && c.IsActive);
        if (chain is null)
            return Fail(StatusCodes.Status404NotFound, "활성 전략 체인이 없습니다.");
        return Ok(ToDto(chain));
    }

    [HttpPost("strategy-chains")]
    public async Task<IActionResult> SaveChain([FromBody] StrategyChainDto dto)
    {
        StrategyChain? chain = null;
        if (dto.Id > 0)
        {
            chain = await db.StrategyChains.Include(c => c.Steps)
                .FirstOrDefaultAsync(c => c.Id == dto.Id && c.UserId == UserId);
            if (chain is null)
                return Fail(StatusCodes.Status404NotFound, "전략 체인을 찾을 수 없습니다.");
            db.StrategyChainSteps.RemoveRange(chain.Steps);
        }

        if (chain is null)
        {
            chain = new StrategyChain { UserId = UserId };
            db.StrategyChains.Add(chain);
        }

        chain.Name = dto.Name;
        chain.CompletionAction = dto.CompletionAction;
        chain.IsActive = dto.IsActive;
        chain.Steps = (dto.Steps ?? []).Select(s => new StrategyChainStep
        {
            Order = s.Order,
            StrategyType = s.StrategyType,
            MaxStepTransition = s.MaxStepTransition,
            ConsecutiveLossTransition = s.ConsecutiveLossTransition,
            LossAmountTransition = s.LossAmountTransition,
            StrategyCompleteTransition = s.StrategyCompleteTransition
        }).ToList();

        if (dto.IsActive)
            await DeactivateOthersAsync(chain);

        await db.SaveChangesAsync();
        return Ok(new { ok = true, id = chain.Id });
    }

    [HttpDelete("strategy-chains/{chainId:long}")]
    public async Task<IActionResult> DeleteChain(long chainId)
    {
        var chain = await db.StrategyChains.FirstOrDefaultAsync(c => c.Id == chainId && c.UserId == UserId);
        if (chain is null)
            return Fail(StatusCodes.Status404NotFound, "전략 체인을 찾을 수 없습니다.");
        db.StrategyChains.Remove(chain);
        await db.SaveChangesAsync();
        return Ok(new { ok = true });
    }

    [HttpPost("strategy-chains/{chainId:long}/activate")]
    public async Task<IActionResult> ActivateChain(long chainId)
    {
        var chain = await db.StrategyChains.FirstOrDefaultAsync(c => c.Id == chainId && c.UserId == UserId);
        if (chain is null)
            return Fail(StatusCodes.Status404NotFound, "전략 체인을 찾을 수 없습니다.");
        chain.IsActive = true;
        await DeactivateOthersAsync(chain);
        await db.SaveChangesAsync();
        return Ok(new { ok = true });
    }

    async Task DeactivateOthersAsync(StrategyChain keep)
    {
        var others = await db.StrategyChains
            .Where(c => c.UserId == UserId && c.IsActive)
            .ToListAsync();
        foreach (var other in others.Where(o => !ReferenceEquals(o, keep) && o.Id != keep.Id))
            other.IsActive = false;
    }

    static StrategyChainDto ToDto(StrategyChain c) => new(
        c.Id, c.Name, c.CompletionAction, c.IsActive,
        c.Steps.OrderBy(s => s.Order).Select(s => new StrategyChainStepDto(
            s.Order, s.StrategyType, s.MaxStepTransition, s.ConsecutiveLossTransition,
            s.LossAmountTransition, s.StrategyCompleteTransition)).ToList());

    /// <summary>Settings pages always POST the full list, so replace wholesale.</summary>
    async Task ReplaceAsync<TEntity, TDto>(
        IQueryable<TEntity> existing, List<TDto> incoming, Func<TDto, TEntity> map)
        where TEntity : class
    {
        db.Set<TEntity>().RemoveRange(await existing.ToListAsync());
        db.Set<TEntity>().AddRange(incoming.Select(map));
        await db.SaveChangesAsync();
    }
}
