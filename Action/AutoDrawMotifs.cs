using System.Collections.Generic;
using DailyRoutines.Abstracts;
using DailyRoutines.Managers;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.ClientState.JobGauge.Types;
using FFXIVClientStructs.FFXIV.Client.Game;

namespace DailyRoutines.ModulesPublic;

public class AutoDrawMotifs : DailyModuleBase
{
    public override ModuleInfo Info { get; } = new()
    {
        Title       = GetLoc("AutoDrawMotifsTitle"),
        Description = GetLoc("AutoDrawMotifsDescription"),
        Category    = ModuleCategories.Action,
    };

    private static readonly HashSet<uint> InvalidContentTypes = [16, 17, 18, 19, 31, 32, 34, 35];
    
    private static Config ModuleConfig = null!;

    protected override void Init()
    {
        ModuleConfig = LoadConfig<Config>() ?? new();

        TaskHelper ??= new() { TimeLimitMS = 30_000 };

        DService.ClientState.TerritoryChanged += OnZoneChanged;
        DService.DutyState.DutyRecommenced    += OnDutyRecommenced;
        DService.Condition.ConditionChange    += OnConditionChanged;
        DService.DutyState.DutyCompleted      += OnDutyCompleted;
    }

    protected override void ConfigUI()
    {
        if (ImGui.Checkbox(GetLoc("AutoDrawMotifs-DrawWhenOutOfCombat"), ref ModuleConfig.DrawWhenOutOfCombat))
            SaveConfig(ModuleConfig);
    }

    private void OnConditionChanged(ConditionFlag flag, bool value)
    {
        if (flag != ConditionFlag.InCombat) return;
        
        TaskHelper.Abort();
        
        if (value || !ModuleConfig.DrawWhenOutOfCombat) return;

        TaskHelper.Enqueue(CheckCurrentJob);
    }

    // 重新挑战
    private void OnDutyRecommenced(object? sender, ushort e)
    {
        TaskHelper.Abort();
        TaskHelper.Enqueue(CheckCurrentJob);
    }

    // 完成副本
    private void OnDutyCompleted(object? sender, ushort e) => 
        TaskHelper.Abort();

    // 进入副本
    private void OnZoneChanged(ushort zone)
    {
        TaskHelper.Abort();
        
        if (!PresetSheet.Contents.ContainsKey(zone)) return;
        TaskHelper.Enqueue(CheckCurrentJob);
    }

    private bool? CheckCurrentJob()
    {
        if (BetweenAreas || OccupiedInEvent) return false;
        if (DService.ObjectTable.LocalPlayer is not { ClassJob.RowId: 42, Level: >= 30 } || !IsValidPVEDuty())
        {
            TaskHelper.Abort();
            return true;
        }

        TaskHelper.Enqueue(DrawNeededMotif, "DrawNeededMotif", 5_000, true, 1);
        return true;
    }

    private bool? DrawNeededMotif()
    {
        var gauge = DService.JobGauges.Get<PCTGauge>();

        if (DService.ObjectTable.LocalPlayer == null || BetweenAreas || DService.Condition[ConditionFlag.Casting]) return false;

        if (DService.Condition.Any(ConditionFlag.InCombat, ConditionFlag.Mounted, ConditionFlag.Mounting, ConditionFlag.InFlight))
        {
            TaskHelper.Abort();
            return true;
        }
        
        var motifAction = 0U;
        if (!gauge.CreatureMotifDrawn && IsActionUnlocked(34689))
            motifAction = 34689;
        else if (!gauge.WeaponMotifDrawn && IsActionUnlocked(34690) && !LocalPlayerState.HasStatus(3680, out _))
            motifAction = 34690;
        else if (!gauge.LandscapeMotifDrawn && IsActionUnlocked(34691))
            motifAction = 34691;

        if (motifAction == 0)
        {
            TaskHelper.Abort();
            return true;
        }

        TaskHelper.Enqueue(() => UseActionManager.UseAction(ActionType.Action, motifAction), $"UseAction_{motifAction}", 2_000, true, 1);
        TaskHelper.DelayNext(500, $"DrawMotif_{motifAction}", false, 1);
        TaskHelper.Enqueue(DrawNeededMotif, "DrawNeededMotif", 5_000, true, 1);
        return true;
    }
    
    private static bool IsValidPVEDuty()
    {
        var isPVP = GameState.IsInPVPArea;
        return !isPVP && (GameState.ContentFinderConditionData.RowId == 0 || !InvalidContentTypes.Contains(GameState.ContentFinderConditionData.ContentType.RowId));
    }

    protected override void Uninit()
    {
        DService.ClientState.TerritoryChanged -= OnZoneChanged;
        DService.DutyState.DutyRecommenced    -= OnDutyRecommenced;
        DService.Condition.ConditionChange    -= OnConditionChanged;
        DService.DutyState.DutyCompleted      -= OnDutyCompleted;

        base.Uninit();
    }

    private class Config : ModuleConfiguration
    {
        public bool DrawWhenOutOfCombat;
    }
}
