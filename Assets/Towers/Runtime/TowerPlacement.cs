using System;
using System.Collections.Generic;
using RoomSiege.Data;
using RoomSiege.Scan;
using UnityEngine;

namespace RoomSiege.Towers
{
    public static class TowerCellPicker
    {
        public const float SnapMargin = .12f;
        // Only snap to real, currently allowed cells; never synthesize support.
        public static bool TryPick(GridModel grid, Ray ray, out GridCell cell, out Vector3 hit,
            Func<float, bool> blocked = null)
        {
            cell = default; hit = ray.GetPoint(2);
            if (grid == null) return false;
            float nearest = float.PositiveInfinity, bestOffset = float.PositiveInfinity;
            bool found = false;
            foreach (var candidate in grid.PlaceableCells)
            {
                if (!new Plane(Vector3.up, candidate.Center).Raycast(ray, out float d) || d < 0 || d > 10) continue;
                var point = ray.GetPoint(d);
                float dx = point.x - candidate.Center.x, dz = point.z - candidate.Center.z;
                float outsideX = Mathf.Max(0, Mathf.Abs(dx) - GridModel.CellSize * .5f);
                float outsideZ = Mathf.Max(0, Mathf.Abs(dz) - GridModel.CellSize * .5f);
                if (outsideX * outsideX + outsideZ * outsideZ > SnapMargin * SnapMargin) continue;
                float offset = dx * dx + dz * dz;
                if (found && (d > nearest + .001f || Mathf.Abs(d - nearest) <= .001f && offset >= bestOffset)) continue;
                if (blocked != null && blocked(d)) continue;
                nearest = d; bestOffset = offset; cell = candidate; hit = point; found = true;
            }
            return found;
        }
    }

    public interface ITowerWallet
    {
        int Coins { get; }
        bool TrySpend(int amount);
        void Credit(int amount);
    }

    public sealed class GameTowerWallet : ITowerWallet
    {
        private readonly GameManager manager;
        public GameTowerWallet(GameManager manager) { this.manager = manager; }
        public int Coins => manager.Coins;
        public bool TrySpend(int amount) => manager.TrySpendCoins(amount);
        public void Credit(int amount) => manager.AddCoins(amount);
    }

    public sealed class TowerInstance
    {
        // Editor codex previews need a valid tower instance without touching gameplay placement/economy.
        public static TowerInstance CreatePreview(TowerDefinition definition, GridCell cell)
        {
            if (definition == null) throw new ArgumentNullException(nameof(definition));
            return new TowerInstance { Definition = definition, Cell = cell };
        }

        public int Id { get; internal set; }
        public TowerDefinition Definition { get; internal set; }
        public GridCell Cell { get; internal set; }
        public int Level { get; internal set; } = 1;
        public int InvestedGold { get; internal set; }
        public bool Removed { get; internal set; }
        public bool Consumed { get; internal set; }
        public CampaignPerks Perks { get; internal set; }
        public RoomSiege.Progression.SkinDefinition Skin {get;set;}
        public float OverloadRemaining {get;set;}
        public float OverloadLifetimeGranted {get;set;}
        public float OverloadControlIncrease=>OverloadControlBonus+.05f*(Perks?.Rank("overload",0,true)??0);
        public float OverloadStrength {get;set;}=1.35f;
        public float OverloadRangeBonus {get;set;}
        public float OverloadControlBonus {get;set;}=.1f;
        public float OverloadOutputMultiplier=>OverloadRemaining>0?OverloadStrength+.25f*(Perks?.Rank("overload",0,true)??0):1;
        public bool FrostEnchanted {get;set;}
        public float AuraRateBonus,AuraRangeBonus;
        public float RateMultiplier=>(Skin?.rate??1)*OverloadOutputMultiplier*(1+AuraRateBonus)*(Perks?.WeaponRate(Definition.id)??1);
        public float DamageMultiplier=>(Skin?.damage??1)*(Perks?.WeaponDamage(Definition.id)??1);
        public int PowerRank=>Perks?.Rank(Definition.id,0)??0;
        public int MasteryRank=>Perks?.Rank(Definition.id,1)??0;
        public float EffectMultiplier=>Perks?.WeaponStrength(Definition.id)??1;
        public float ControlBonus=>Perks?.WeaponControl(Definition.id)??0;
        public TowerLevelDefinition Stats => Definition.levels[Level - 1];
        public float Range => (1+(OverloadRemaining>0?OverloadRangeBonus+.1f*(Perks?.Rank("overload",2,true)??0):0))*Definition.rangeMeters.value * (Perks?.WeaponRange(Definition.id)??1) * Stats.rangeMultiplier*(Skin?.range??1)*(1+AuraRangeBonus)*(Definition.id=="MG"&&Perks!=null?Perks.MgRange:1);
        public float MinimumRange => Definition.minimumRangeMeters;
        public float ImpactDelay => Definition.impactDelaySeconds;
    }

    // Transactions use a canonical cell from the current immutable scan snapshot.
    public sealed class TowerPlacement
    {
        private readonly BalanceCatalog catalog;
        private readonly ITowerWallet wallet;
        private readonly List<TowerInstance> towers = new List<TowerInstance>();
        private int nextId;
        private GridCell checkedCell;
        private bool hasCheckedCell,checkedRoute;
        private TowerRouteMap proposedRoutes;
        private GridModel checkedCoreGrid;
        private TowerRouteMap checkedCoreRoutes;
        public TowerRouteMap Routes { get; private set; }
        public readonly CampaignPerks Perks=new CampaignPerks();
        public IReadOnlyList<string> WeaponOrder {get;set;}
        public Func<string,bool> WeaponAllowed {get;set;}
        public Func<string,RoomSiege.Progression.SkinDefinition> SkinForWeapon {get;set;}
        public Func<IEnumerable<Vector3>> ActiveEnemyPositions { get; set; }
        public IReadOnlyList<TowerInstance> Towers => towers.AsReadOnly();
        public GridModel Grid { get; private set; }
        public bool Suspended => Grid == null;
        public event Action Changed;
        public TowerPlacement(BalanceCatalog catalog, ITowerWallet wallet)
        { this.catalog = catalog; this.wallet = wallet; }

        public void ResetCampaign()
        {
            Perks.Reset();
            // Remove without a sale/refund. Keep IDs monotonic for audio and report observers.
            foreach (var tower in towers) tower.Removed = true;
            Changed?.Invoke();
            towers.Clear();
            RebuildRoutes();
        }

        public void SetGrid(GridModel grid)
        {
            bool coreOnly=Grid!=null&&grid!=null&&ReferenceEquals(Grid.SupportedCells,grid.SupportedCells)&&
                ReferenceEquals(Grid.WalkMap,grid.WalkMap)&&ReferenceEquals(Grid.SpawnPoints,grid.SpawnPoints);
            var validatedRoutes=ReferenceEquals(checkedCoreGrid,grid)?checkedCoreRoutes:null;
            Grid = grid;
            if(coreOnly)
            {
                hasCheckedCell=false;
                Routes=validatedRoutes??new TowerRouteMap(grid,towers);
                checkedCoreGrid=null;checkedCoreRoutes=null;
                // Following changes the objective and purchase mask, not tower
                // models, levels or the shop. Do not refresh every tower view.
                return;
            }
            if (grid != null)
                foreach (var tower in towers)
                {
                    if (tower.Removed) continue;
                    if (TrySupported(tower.Cell, out var cell)) tower.Cell = cell;
                    else
                    {
                        // Re-scanning can invalidate a player's purchase; refund the full investment.
                        tower.Removed = true;
                        wallet.Credit(tower.InvestedGold);
                    }
                }
            RebuildRoutes();Changed?.Invoke();
        }

        public bool CanBuild(string towerId, GridCell requested, out GridCell cell, out string reason)
        {
            cell = default;
            if (Grid == null) { reason = "扫描中，暂停部署"; return false; }
            if (!TryCanonical(requested, out cell)) { reason = "不可放格 / Boss 保留路线 / 核心本体 / 出怪口"; return false; }
            foreach (var existing in towers)
                if (!existing.Removed && SameCell(existing.Cell, cell))
                { reason = "此格已有塔"; return false; }
            if (!CanPurchase(towerId, out reason)) return false;
            if (!KeepsEnemyRoute(cell, towerId))
            { reason = "会封死 Boss 通路，请留出至少 2 格宽的通道"; return false; }
            return true;
        }

        // Used by every shop input before a cell is chosen; a preview never spends money.
        public bool CanPurchase(string towerId, out string reason)
        {
            if (Grid == null) { reason = "扫描中，暂停部署"; return false; }
            if(WeaponAllowed!=null&&!WeaponAllowed(towerId)){reason="该武器未加入本局四武器阵容";return false;}
            TowerDefinition definition;
            try { definition = catalog.GetTower(towerId); }
            catch (KeyNotFoundException) { reason = "未知塔配置"; return false; }
            if (!definition.rangeMeters.specified) { reason = "塔射程未配置"; return false; }
            if (wallet.Coins < definition.buildCost)
            { reason = $"金币不足：需要 {definition.buildCost}G，现有 {wallet.Coins}G，还差 {definition.buildCost-wallet.Coins}G"; return false; }
            reason = "Tap 建造"; return true;
        }

        public bool TryBuild(string towerId, GridCell requested, out TowerInstance tower, out string reason)
        {
            tower = null;
            if (!CanBuild(towerId, requested, out var cell, out reason)) return false;
            var definition = catalog.GetTower(towerId);
            if (!wallet.TrySpend(definition.buildCost)) { reason = "金币不足"; return false; }
            tower = new TowerInstance { Id = ++nextId, Definition = definition, Cell = cell, InvestedGold = definition.buildCost,Perks=Perks,Skin=SkinForWeapon?.Invoke(towerId) };
            towers.Add(tower);
            RebuildRoutes();Changed?.Invoke();
            return true;
        }

        public int UpgradePrice(TowerInstance tower, int desiredLevel)
        {
            if (!Active(tower) || desiredLevel <= tower.Level || desiredLevel > 3) return -1;
            int total = 0;
            for (int level = tower.Level + 1; level <= desiredLevel; level++) total += tower.Definition.levels[level - 1].upgradeCost;
            return total;
        }

        public bool TryUpgrade(TowerInstance tower, int desiredLevel, out string reason)
        {
            if (!CanUpgrade(tower, desiredLevel, out int price, out reason)) return false;
            if (!wallet.TrySpend(price)) { reason = "金币不足"; return false; }
            tower.Level = desiredLevel;
            tower.InvestedGold += price;
            reason = "升级成功";
            Changed?.Invoke(); return true;
        }

        public bool CanUpgrade(TowerInstance tower, int desiredLevel, out int price, out string reason)
        {
            price = UpgradePrice(tower, desiredLevel);
            if (Grid == null) { reason = "扫描中，暂停升级"; return false; }
            if (price < 0) { reason = "等级已达标或塔已移除"; return false; }
            if (wallet.Coins < price)
            { reason = $"金币不足：需要 {price}G，现有 {wallet.Coins}G，还差 {price-wallet.Coins}G"; return false; }
            reason = "升级可用"; return true;
        }

        // Integer currency: floor 70% of all actually paid build + upgrade costs once.
        public int Refund(TowerInstance tower) => !Active(tower) ? 0 :
            (int)Math.Floor(tower.InvestedGold * Math.Round((double)catalog.economy.sellRefundFraction, 6) + .0000001d);

        public bool TrySell(TowerInstance tower, out int refund)
        {
            refund = Refund(tower);
            if (!Active(tower)) return false;
            tower.Removed = true;
            wallet.Credit(refund);
            RebuildRoutes();Changed?.Invoke(); return true;
        }

        public void Consume(TowerInstance tower)
        {
            if (!Active(tower)) return;
            tower.Consumed = true; tower.Removed = true;
            if(tower.Definition.singleUse&&Perks.TrapRefundFraction>0)
                wallet.Credit(Mathf.FloorToInt(tower.Definition.buildCost*Perks.TrapRefundFraction+.0001f));
            Changed?.Invoke();
        }
        public bool Active(TowerInstance tower) => tower != null && towers.Contains(tower) && !tower.Removed;
        public bool TryCanonical(GridCell candidate, out GridCell cell)
        {
            if (Grid != null)
                foreach (var current in Grid.PlaceableCells)
                    if (SameCell(current, candidate) && (current.Center - candidate.Center).sqrMagnitude < 0.0025f)
                    { cell = current; return true; }
            cell = default; return false;
        }
        public TowerInstance FindActiveAtCell(GridCell candidate)
        {
            if(!TryCanonical(candidate,out var canonical))return null;
            foreach(var tower in towers)
                if(!tower.Removed&&SameCell(tower.Cell,canonical)&&
                    (tower.Cell.Center-canonical.Center).sqrMagnitude<.0025f)return tower;
            return null;
        }
        private static bool SameCell(GridCell a, GridCell b) => a.SurfaceId == b.SurfaceId && a.Index == b.Index;

        private bool TrySupported(GridCell candidate,out GridCell cell)
        {
            if(Grid!=null)foreach(var current in Grid.SupportedCells)
                if(SameCell(current,candidate)&&(current.Center-candidate.Center).sqrMagnitude<.0025f)
                {cell=current;return true;}
            cell=default;return false;
        }

        public bool CanMoveCore(GridModel candidate)
        {
            checkedCoreGrid=null;checkedCoreRoutes=null;
            if(candidate?.WalkMap==null)return false;
            var routes=new TowerRouteMap(candidate,towers);
            if(!routes.AllEntrancesReachable())return false;
            if(ActiveEnemyPositions!=null)
                foreach(var enemy in ActiveEnemyPositions())if(!routes.CanReach(enemy))return false;
            checkedCoreGrid=candidate;checkedCoreRoutes=routes;
            return true;
        }

        private bool KeepsEnemyRoute(GridCell candidate, string towerId)
        {
            if (Grid?.WalkMap == null || Grid.SpawnPoints.Count == 0) return true;
            var definition = catalog.GetTower(towerId);
            if (definition.singleUse || Mathf.Abs(candidate.Center.y-Grid.SafeZoneCenter.y)>=.35f) return true;
            if(!hasCheckedCell||!SameCell(checkedCell,candidate))
            {
                checkedCell=candidate;hasCheckedCell=true;
                proposedRoutes=new TowerRouteMap(Grid,towers,candidate.Center);
                checkedRoute=proposedRoutes.AllEntrancesReachable();
            }
            if(!checkedRoute)return false;
            // Enemies can move during a preview. Recheck their exact locations on
            // each request so a battle purchase cannot enclose or overlap one.
            if(ActiveEnemyPositions!=null)
            {
                foreach(var enemy in ActiveEnemyPositions())if(!proposedRoutes.CanReach(enemy))return false;
            }
            return true;
        }
        private void RebuildRoutes()
        {
            checkedCoreGrid=null;checkedCoreRoutes=null;
            hasCheckedCell=false;
            Routes=Grid==null?null:new TowerRouteMap(Grid,towers);
        }
    }
}
