using Godot;

// =================================================================================================
// FILE: ScentGlyphRenderer.cs (GODOT VERSION)
// PURPOSE: Visual cue renderer for scent events (green mist arcs + vertical fade + freshness pulse).
// ATTACH TO: Do not attach (spawned at runtime by ScentSystem).
// =================================================================================================
public static class ScentGlyphRenderer
{
    private static ScentGlyphRuntime runtime;

    public static void TryRender(ScentEvent scent)
    {
        var tree = Engine.GetMainLoop() as SceneTree;
        var scene = tree?.CurrentScene;
        if (scene == null) return;

        if (!GodotObject.IsInstanceValid(runtime))
        {
            runtime = new ScentGlyphRuntime
            {
                Name = "ScentGlyphRuntime"
            };
            scene.AddChild(runtime);
        }

        runtime.Spawn(scent);
    }
}

public partial class ScentGlyphRuntime : Node3D
{
    public void Spawn(ScentEvent scent)
    {
        var glyph = new ScentGlyphInstance();
        AddChild(glyph);
        glyph.Initialize(scent);
    }

    private sealed class ScentGlyphInstance : Node3D
    {
        private const float GroundOffset = 0.08f;
        private ScentEvent scent;
        private MeshInstance3D arcMesh;
        private MeshInstance3D mistDisk;
        private StandardMaterial3D arcMaterial;
        private StandardMaterial3D mistMaterial;
        private float arcBaseAlpha;
        private float mistBaseAlpha;
        private float elapsed;
        private float duration;

        public void Initialize(ScentEvent evt)
        {
            scent = evt;
            GlobalPosition = scent.Position;

            float scale = Mathf.Clamp(0.5f + scent.Intensity * 0.55f, 0.5f, 2.6f);
            duration = Mathf.Max(0.8f, scent.DurationSeconds);

            Color scentColor = GetScentColor(evt.Type);
            float verticalFade = ComputeVerticalFade();
            float freshness = scent.Freshness;

            arcBaseAlpha = Mathf.Clamp(0.45f * freshness * verticalFade, 0.06f, 0.6f);
            mistBaseAlpha = Mathf.Clamp(0.35f * freshness * verticalFade, 0.05f, 0.5f);

            arcMaterial = MakeMaterial(scentColor.Lightened(0.1f), arcBaseAlpha);
            mistMaterial = MakeMaterial(scentColor.Darkened(0.08f), mistBaseAlpha);

            arcMesh = MakeArcMesh(scale * 4f, scale * 0.75f, arcMaterial);
            mistDisk = MakeMistDisk(scale * 2.5f, mistMaterial);
            mistDisk.Position = new Vector3(0f, GroundOffset, 0f);

            AddChild(arcMesh);
            AddChild(mistDisk);

            float pitchByBias = Mathf.Clamp(-scent.VerticalBias * 0.22f, -0.3f, 0.3f);
            Rotation = new Vector3(pitchByBias, 0f, 0f);
        }

        public override void _Process(double delta)
        {
            elapsed += (float)delta;
            float t = Mathf.Clamp(elapsed / duration, 0f, 1f);
            float remaining = 1f - t;

            float pulse = 0.72f + (Mathf.Sin(elapsed * 7.5f) * 0.28f);
            float freshnessPulse = Mathf.Lerp(0.65f, 1.25f, scent.Freshness) * pulse;

            arcMesh.Scale = new Vector3(Mathf.Lerp(0.92f, 1.28f, t), 1f, Mathf.Lerp(0.92f, 1.28f, t));
            mistDisk.Scale = new Vector3(Mathf.Lerp(0.9f, 1.15f, t), 1f, Mathf.Lerp(0.9f, 1.15f, t));

            SetAlpha(arcMaterial, arcBaseAlpha * remaining * freshnessPulse);
            SetAlpha(mistMaterial, mistBaseAlpha * remaining * (0.85f + freshnessPulse * 0.4f));

            if (t >= 1f)
            {
                QueueFree();
            }
        }

        private float ComputeVerticalFade()
        {
            float referenceY = FindPlayerReferenceY();
            float deltaY = Mathf.Abs(scent.Position.Y - referenceY);
            return Mathf.Clamp(1f - (deltaY / 24f), 0.2f, 1f);
        }

        private static Color GetScentColor(ScentType type)
        {
            return type switch
            {
                ScentType.Poison => new Color(0.5f, 1f, 0.45f, 1f),
                ScentType.Fire => new Color(0.62f, 1f, 0.5f, 1f),
                ScentType.Smoke => new Color(0.56f, 0.85f, 0.56f, 1f),
                ScentType.Undead => new Color(0.45f, 0.92f, 0.55f, 1f),
                _ => new Color(0.35f, 0.98f, 0.45f, 1f)
            };
        }

        private static StandardMaterial3D MakeMaterial(Color color, float alpha)
        {
            return new StandardMaterial3D
            {
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                CullMode = BaseMaterial3D.CullModeEnum.Disabled,
                VertexColorUseAsAlbedo = true,
                AlbedoColor = new Color(color.R, color.G, color.B, Mathf.Clamp(alpha, 0f, 1f)),
                NoDepthTest = false
            };
        }

        private static MeshInstance3D MakeArcMesh(float radius, float thickness, StandardMaterial3D material)
        {
            var torus = new TorusMesh
            {
                InnerRadius = Mathf.Max(0.1f, radius - thickness),
                OuterRadius = radius,
                Rings = 24,
                RingSegments = 16
            };

            var mesh = new MeshInstance3D
            {
                Mesh = torus,
                MaterialOverride = material,
                Position = new Vector3(0f, GroundOffset + 0.2f, 0f),
                Rotation = new Vector3(Mathf.DegToRad(80f), 0f, 0f),
                Scale = new Vector3(1f, 0.25f, 0.8f)
            };

            return mesh;
        }

        private static MeshInstance3D MakeMistDisk(float radius, StandardMaterial3D material)
        {
            var cylinder = new CylinderMesh
            {
                TopRadius = radius,
                BottomRadius = radius * 0.9f,
                Height = 0.08f,
                RadialSegments = 20
            };

            return new MeshInstance3D
            {
                Mesh = cylinder,
                MaterialOverride = material
            };
        }

        private static float FindPlayerReferenceY()
        {
            var tree = Engine.GetMainLoop() as SceneTree;
            if (tree == null) return 0f;

            var players = tree.GetNodesInGroup("Player");
            if (players.Count == 0) return 0f;

            float total = 0f;
            int count = 0;
            foreach (var node in players)
            {
                if (node is CreatureStats stats)
                {
                    total += stats.GlobalPosition.Y;
                    count++;
                }
            }

            return count > 0 ? total / count : 0f;
        }

        private static void SetAlpha(StandardMaterial3D material, float alpha)
        {
            if (material == null) return;
            Color c = material.AlbedoColor;
            material.AlbedoColor = new Color(c.R, c.G, c.B, Mathf.Clamp(alpha, 0f, 1f));
        }
    }
}