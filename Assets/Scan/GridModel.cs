using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using UnityEngine;

namespace RoomSiege.Scan
{
    public enum ScanMode { RealRoom, Sandbox }
    public enum SurfaceKind { Floor, Table, LowCabinet }

    // Positions are Unity world coordinates, in metres. Consumers receive a new snapshot on change.
    public readonly struct GridCell
    {
        public string SurfaceId { get; }
        public SurfaceKind Surface { get; }
        public Vector2Int Index { get; }
        public Vector3 Center { get; }
        public GridCell(string surfaceId, SurfaceKind surface, Vector2Int index, Vector3 center)
        { SurfaceId = surfaceId; Surface = surface; Index = index; Center = center; }
    }

    public readonly struct SpawnPoint
    {
        public string AnchorId { get; }
        public Vector3 Position { get; }
        public Vector3 Forward { get; }
        public Vector2 FootprintSize { get; }
        public bool IsManual { get; }
        public SpawnPoint(string anchorId, Vector3 position, Vector3 forward, Vector2 size, bool manual = false)
        {
            AnchorId = anchorId; Position = position;
            Forward = Vector3.ProjectOnPlane(forward, Vector3.up).normalized;
            if (Forward.sqrMagnitude < 0.1f) Forward = Vector3.forward;
            FootprintSize = size; IsManual = manual;
        }
    }

    public sealed class GridModel
    {
        public const float CellSize = 0.4f;
        public const float SafeRadius = 0.8f;
        // The leak/objective ring is not a construction exclusion. Reserve only
        // the core mesh (rotated 20cm square) plus a small physical margin.
        public const float CoreBuildRadius = 0.16f;
        public const float BossBodyRadius = 0.28f;
        public ScanMode Mode { get; }
        public Vector3 SafeZoneCenter { get; }
        public IReadOnlyList<GridCell> PlaceableCells { get; }
        // Physical support survives a moving safety exclusion. Existing purchases
        // are checked against support; new purchases against PlaceableCells.
        public IReadOnlyList<GridCell> SupportedCells { get; }
        public IReadOnlyList<GridCell> ReservedRouteCells { get; private set; }
        public IReadOnlyList<SpawnPoint> SpawnPoints { get; }
        public RoomWalkMap WalkMap { get; }
        public GridModel(ScanMode mode, Vector3 safeCenter, IList<GridCell> cells, IList<SpawnPoint> spawns,RoomWalkMap walkMap=null,IList<GridCell> supportedCells=null,IList<GridCell> reservedRouteCells=null)
        {
            Mode = mode; SafeZoneCenter = safeCenter;
            WalkMap=walkMap;
            PlaceableCells = new ReadOnlyCollection<GridCell>(new List<GridCell>(cells));
            SupportedCells = new ReadOnlyCollection<GridCell>(new List<GridCell>(supportedCells??cells));
            ReservedRouteCells = new ReadOnlyCollection<GridCell>(new List<GridCell>(reservedRouteCells??new List<GridCell>()));
            SpawnPoints = new ReadOnlyCollection<SpawnPoint>(new List<SpawnPoint>(spawns));
        }

        private GridModel(GridModel source, Vector3 center, IList<GridCell> cells)
        {
            Mode=source.Mode; SafeZoneCenter=center; WalkMap=source.WalkMap;
            SupportedCells=source.SupportedCells; SpawnPoints=source.SpawnPoints;
            ReservedRouteCells=source.ReservedRouteCells;
            PlaceableCells=new ReadOnlyCollection<GridCell>(new List<GridCell>(cells));
        }
        internal GridModel WithSafeZone(Vector3 center,IList<GridCell> cells,IList<GridCell> reserved=null)
        {
            var copy=new GridModel(this,center,cells);
            if(reserved!=null)copy.ReservedRouteCells=new ReadOnlyCollection<GridCell>(new List<GridCell>(reserved));
            return copy;
        }
    }

    // Intermediate geometry is detached from native handles. Polygon vertices are world X/Z, height is Y.
    [Serializable]
    public sealed class DeploySurface
    {
        public string Id;
        public SurfaceKind Kind;
        public float Height;
        public Vector2[] Polygon;
        public bool HasAnchorPose;
        public Pose AnchorPose;
    }

    [Serializable]
    public sealed class RoomVolume
    {
        public string Id;
        public Vector3 Center;
        public Quaternion Rotation = Quaternion.identity;
        public Vector3 Size;
        public bool SupportsDeployment;
    }

    public sealed class RoomSnapshot
    {
        public readonly List<DeploySurface> Surfaces = new List<DeploySurface>();
        public readonly List<SpawnPoint> Entrances = new List<SpawnPoint>();
        public readonly List<RoomVolume> Volumes = new List<RoomVolume>();
        public float FloorHeight;
    }
}
