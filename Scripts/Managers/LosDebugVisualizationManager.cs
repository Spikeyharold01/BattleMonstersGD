using Godot;
using System.Collections.Generic;

// =================================================================================================
// FILE: LosDebugVisualizationManager.cs
// PURPOSE: Debug-only visualization overlay for LOS, cover, and visibility tiles.
// ATTACH TO: Optional debug node in scene; disabled by default.
// =================================================================================================

public partial class LosDebugVisualizationManager : Node3D
{
    [ExportGroup("Debug Toggle")]
    [Export] public bool EnableLosDebug = false;
    [Export] public bool ShowLosRays = true;
    [Export] public bool ShowCoverNodes = true;
    [Export] public bool ShowCoverPercentages = true;
    [Export] public bool ShowVisibilityTiles = true;

    [ExportGroup("Sampling")]
    [Export] public float RefreshIntervalSeconds = 0.25f;
    [Export(PropertyHint.Range, "1,24,1")] public int VisibilityTileRadius = 8;
    [Export] public float LabelHeightOffset = 2.0f;

    [ExportGroup("Viewer")]
    [Export] public NodePath ViewerOverridePath;

    private MeshInstance3D _lineMeshInstance;
    private ImmediateMesh _lineMesh;
    private StandardMaterial3D _lineMaterial;
    private readonly List<Label3D> _dynamicLabels = new();
    private double _accumulated;

    public override void _Ready()
    {
        _lineMesh = new ImmediateMesh();
        _lineMeshInstance = new MeshInstance3D { Mesh = _lineMesh, Name = "LosDebugLines" };

        _lineMaterial = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            VertexColorUseAsAlbedo = true,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            NoDepthTest = true
        };
        _lineMeshInstance.MaterialOverride = _lineMaterial;
        AddChild(_lineMeshInstance);
    }

    public override void _Process(double delta)
    {
        _accumulated += delta;
        if (_accumulated < RefreshIntervalSeconds)
        {
            return;
        }

        _accumulated = 0;

        if (!EnableLosDebug)
        {
            ClearVisuals();
            return;
        }

        RenderDebugOverlay();
    }

    private void RenderDebugOverlay()
    {
        _lineMesh.ClearSurfaces();
        ClearLabels();

        CreatureStats viewer = ResolveViewer();
        if (viewer == null || GridManager.Instance == null || TurnManager.Instance == null)
        {
            return;
        }

        List<CreatureStats> combatants = TurnManager.Instance.GetAllCombatants();
        if (combatants.Count == 0)
        {
            return;
        }

        _lineMesh.SurfaceBegin(Mesh.PrimitiveType.Lines);

        if (ShowCoverNodes)
        {
            DrawCoverNodesAround(viewer);
        }

        if (ShowVisibilityTiles)
        {
            DrawVisibilityTiles(viewer);
        }

        foreach (CreatureStats target in combatants)
        {
            if (target == null || target == viewer)
            {
                continue;
            }

            Vector3 rayStart = viewer.GlobalPosition + Vector3.Up * 1.5f;
            Vector3 rayEnd = target.GlobalPosition + Vector3.Up;

            if (ShowLosRays)
            {
                bool hasLoe = LineOfSightManager.HasLineOfEffect(this, rayStart, rayEnd);
                bool hasVisual = hasLoe || LineOfSightManager.HasVisualLineOfSight(this, rayStart, rayEnd);
                Color rayColor = hasLoe ? Colors.LimeGreen : (hasVisual ? Colors.Gold : Colors.Red);
                DrawLine(rayStart, rayEnd, rayColor);
            }

            if (ShowCoverPercentages)
            {
                TerrainCoverDebugInfo coverInfo = LineOfSightManager.GetTerrainCoverDebugInfo(rayStart, rayEnd, target.GlobalPosition.Y + 1.0f);
                AddCoverLabel(target.GlobalPosition + Vector3.Up * LabelHeightOffset,
                    $"Cover {coverInfo.CoverPercentage:0.#}% (AC +{coverInfo.CoverBonusToAC})");
            }
        }

        _lineMesh.SurfaceEnd();
    }

    private CreatureStats ResolveViewer()
    {
        if (ViewerOverridePath != null && !ViewerOverridePath.IsEmpty)
        {
            return GetNodeOrNull<CreatureStats>(ViewerOverridePath);
        }

        return TurnManager.Instance?.GetCurrentCombatant();
    }

    private void DrawVisibilityTiles(CreatureStats viewer)
    {
        GridNode centerNode = GridManager.Instance.NodeFromWorldPoint(viewer.GlobalPosition);
        if (centerNode == null)
        {
            return;
        }

        for (int x = -VisibilityTileRadius; x <= VisibilityTileRadius; x++)
        {
            for (int z = -VisibilityTileRadius; z <= VisibilityTileRadius; z++)
            {
                Vector3 samplePosition = centerNode.worldPosition + new Vector3(x * GridManager.Instance.nodeDiameter, 0, z * GridManager.Instance.nodeDiameter);
                GridNode node = GridManager.Instance.NodeFromWorldPoint(samplePosition);
                if (node == null || node.terrainType == TerrainType.Solid)
                {
                    continue;
                }

                VisibilityResult visibility = LineOfSightManager.GetVisibilityFromPoint(this, viewer.GlobalPosition, node.worldPosition);
                Color tileColor;
                if (visibility.HasLineOfEffect)
                {
                    tileColor = new Color(0f, 1f, 0f, 0.45f);
                }
                else if (visibility.HasLineOfSight)
                {
                    tileColor = new Color(1f, 0.8f, 0f, 0.45f);
                }
                else
                {
                    tileColor = new Color(1f, 0f, 0f, 0.35f);
                }

                DrawWireSquare(node.worldPosition + Vector3.Up * 0.05f, GridManager.Instance.nodeDiameter * 0.9f, tileColor);
            }
        }
    }

    private void DrawCoverNodesAround(CreatureStats viewer)
    {
        GridNode centerNode = GridManager.Instance.NodeFromWorldPoint(viewer.GlobalPosition);
        if (centerNode == null)
        {
            return;
        }

        for (int x = -VisibilityTileRadius; x <= VisibilityTileRadius; x++)
        {
            for (int z = -VisibilityTileRadius; z <= VisibilityTileRadius; z++)
            {
                Vector3 samplePosition = centerNode.worldPosition + new Vector3(x * GridManager.Instance.nodeDiameter, 0, z * GridManager.Instance.nodeDiameter);
                GridNode node = GridManager.Instance.NodeFromWorldPoint(samplePosition);
                if (node == null || !node.providesCover)
                {
                    continue;
                }

                Color color = node.blocksLos ? new Color(0.7f, 0.2f, 1f, 0.9f) : new Color(0.9f, 0.2f, 0.9f, 0.75f);
                float size = Mathf.Max(GridManager.Instance.nodeDiameter * 0.4f, node.coverWidth * 0.5f);
                DrawWireSquare(node.worldPosition + Vector3.Up * 0.3f, size, color);
            }
        }
    }

    private void DrawWireSquare(Vector3 center, float size, Color color)
    {
        float half = size * 0.5f;
        Vector3 p1 = center + new Vector3(-half, 0, -half);
        Vector3 p2 = center + new Vector3(half, 0, -half);
        Vector3 p3 = center + new Vector3(half, 0, half);
        Vector3 p4 = center + new Vector3(-half, 0, half);

        DrawLine(p1, p2, color);
        DrawLine(p2, p3, color);
        DrawLine(p3, p4, color);
        DrawLine(p4, p1, color);
    }

    private void DrawLine(Vector3 from, Vector3 to, Color color)
    {
        _lineMesh.SurfaceSetColor(color);
        _lineMesh.SurfaceAddVertex(from);
        _lineMesh.SurfaceSetColor(color);
        _lineMesh.SurfaceAddVertex(to);
    }

    private void AddCoverLabel(Vector3 worldPosition, string text)
    {
        Label3D label = new Label3D
        {
            Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
            Modulate = Colors.White,
            NoDepthTest = true,
            FontSize = 24,
            Text = text,
            GlobalPosition = worldPosition
        };

        AddChild(label);
        _dynamicLabels.Add(label);
    }

    private void ClearLabels()
    {
        foreach (Label3D label in _dynamicLabels)
        {
            if (GodotObject.IsInstanceValid(label))
            {
                label.QueueFree();
            }
        }

        _dynamicLabels.Clear();
    }

    private void ClearVisuals()
    {
        _lineMesh?.ClearSurfaces();
        ClearLabels();
    }
}
