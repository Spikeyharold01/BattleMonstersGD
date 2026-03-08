using Godot;
using System;
using System.Collections.Generic;

// =================================================================================================
// FILE: SoundGlyphRenderer.cs (GODOT VERSION)
// PURPOSE: Diegetic, tactical visualization of emitted sound events.
// ATTACH TO: Do not attach (spawned at runtime by SoundSystem).
// =================================================================================================
public static class SoundGlyphRenderer
{
    private static SoundGlyphRuntime runtime;

    public static void TryRender(SoundEvent sound)
    {
        var tree = Engine.GetMainLoop() as SceneTree;
        var scene = tree?.CurrentScene;
        if (scene == null) return;

        if (!GodotObject.IsInstanceValid(runtime))
        {
            runtime = new SoundGlyphRuntime
            {
                Name = "SoundGlyphRuntime"
            };
            scene.AddChild(runtime);
        }

        runtime.Spawn(sound);
    }
}

public partial class SoundGlyphRuntime : Node3D
{
    private const float GroundOffset = 0.06f;
    private const float VerticalOffset = 0.2f;
    private static readonly Color NeutralColor = new(1f, 1f, 1f, 1f);
    private static readonly Color MovementColor = new(1f, 0.58f, 0.16f, 1f);
    private static readonly Color CombatColor = new(1f, 0.2f, 0.2f, 1f);
    private static readonly Color MagicColor = new(0.62f, 0.34f, 0.96f, 1f);
    private static readonly Color ChantingColor = new(0.53f, 0.66f, 1f, 1f);
    private static readonly Color LaughterColor = new(1f, 0.84f, 0.22f, 1f);

    public void Spawn(SoundEvent sound)
    {
        var glyph = new SoundGlyphInstance();
        AddChild(glyph);
        glyph.Initialize(sound);
    }

    private sealed class SoundGlyphInstance : Node3D
    {
        private SoundEvent sound;
        private MeshInstance3D groundCone;
        private MeshInstance3D column;
        private MeshInstance3D halo;
        private MeshInstance3D mediumOverlay;
        private StandardMaterial3D groundMaterial;
        private StandardMaterial3D columnMaterial;
        private StandardMaterial3D haloMaterial;
        private StandardMaterial3D mediumMaterial;
        private float groundBaseAlpha;
        private float columnBaseAlpha;
        private float haloBaseAlpha;
        private float mediumBaseAlpha;

        private float totalDuration;
        private float elapsed;
        private float coneLength;
        private float coneHalfAngle;
        private float maxColumnHeight;
        private float urgency;

        public void Initialize(SoundEvent s)
        {
            sound = s;
            GlobalPosition = sound.WorldPosition;

            var profile = BuildProfile(sound.NoiseIntensity, sound.DurationSeconds);
            totalDuration = profile.Duration;
            coneLength = profile.Length;
            coneHalfAngle = profile.HalfAngle;
            maxColumnHeight = profile.ColumnHeight;
            urgency = profile.Urgency;

            Color typeColor = GetTypeColor(sound);
            float confidence = Mathf.Clamp(sound.NoiseIntensity / 6f, 0.1f, 1f);

            groundBaseAlpha = profile.Brightness * confidence * 0.75f;
            columnBaseAlpha = profile.Brightness * 0.4f;
            haloBaseAlpha = profile.Brightness * 0.5f;
            mediumBaseAlpha = profile.Brightness * 0.35f;

            groundMaterial = MakeMaterial(typeColor, groundBaseAlpha);
            columnMaterial = MakeMaterial(typeColor.Lightened(0.1f), columnBaseAlpha);
            haloMaterial = MakeMaterial(GetHeightHaloColor(sound.WorldPosition), haloBaseAlpha);
            mediumMaterial = MakeMaterial(GetMediumColor(sound.WorldPosition, typeColor), mediumBaseAlpha);

            groundCone = MakeGroundConeMesh(coneLength, coneHalfAngle, groundMaterial);
            column = MakeColumnMesh(maxColumnHeight, columnMaterial);
            halo = MakeHaloMesh(GetHaloRadius(profile), haloMaterial);
            mediumOverlay = MakeMediumOverlay(sound.WorldPosition, profile, mediumMaterial);

            AddChild(groundCone);
            AddChild(column);
            AddChild(halo);
            AddChild(mediumOverlay);

            Vector3 forward = ResolveDirection(sound);
            if (forward != Vector3.Zero)
            {
                Basis = Basis.LookingAt(GlobalTransform.Origin + forward, Vector3.Up);
            }
        }

        public override void _Process(double delta)
        {
            elapsed += (float)delta;
            float t = Mathf.Clamp(elapsed / totalDuration, 0f, 1f);
            float expand = Mathf.Lerp(0.2f, 1.12f, t);
            float fade = 1f - t;

            groundCone.Scale = new Vector3(expand, 1f, expand);
            column.Scale = new Vector3(1f, Mathf.Lerp(0.45f, 1f, Mathf.Sqrt(fade)), 1f);
            halo.Scale = new Vector3(Mathf.Lerp(0.6f, 1.2f, t), 1f, Mathf.Lerp(0.6f, 1.2f, t));

            float flicker = 0.82f + (Mathf.Sin(elapsed * (4f + (urgency * 8f))) * 0.18f);
            SetAlpha(groundMaterial, groundBaseAlpha, fade);
            SetAlpha(columnMaterial, columnBaseAlpha, fade * flicker);
            SetAlpha(haloMaterial, haloBaseAlpha, fade * 0.85f);
            SetAlpha(mediumMaterial, mediumBaseAlpha, fade * 0.8f);

            if (t >= 1f)
            {
                QueueFree();
            }
        }

        private static GlyphProfile BuildProfile(float loudness, float suggestedDuration)
        {
            if (loudness <= 0.8f) return new GlyphProfile(8f, 0.2f, 0.10f, Mathf.Max(0.5f, suggestedDuration), 2.2f, 0.2f);
            if (loudness <= 2.0f) return new GlyphProfile(14f, 0.36f, 0.30f, Mathf.Max(1.0f, suggestedDuration), 4f, 0.45f);
            if (loudness <= 4.0f) return new GlyphProfile(22f, 0.55f, 0.60f, Mathf.Max(1.5f, suggestedDuration), 7.5f, 0.7f);
            return new GlyphProfile(34f, 0.8f, 1.0f, Mathf.Max(3.0f, suggestedDuration), 11f, 1.0f);
        }

        private static Color GetTypeColor(SoundEvent s)
        {
            if (s.IsIllusion || s.Type == SoundEventType.Illusion) return MagicColor;
            return s.Type switch
            {
                SoundEventType.Combat => CombatColor,
                SoundEventType.Chanting => ChantingColor,
                SoundEventType.Laughter => LaughterColor,
                SoundEventType.Movement => MovementColor,
                _ => NeutralColor
            };
        }

        private static Vector3 ResolveDirection(SoundEvent s)
        {
            if (s.Direction != Vector3.Zero) return new Vector3(s.Direction.X, 0f, s.Direction.Z).Normalized();
            if (s.SourceCreature != null)
            {
                Vector3 fallback = s.SourceCreature.GlobalBasis.Z;
                return new Vector3(fallback.X, 0f, fallback.Z).Normalized();
            }
            return Vector3.Forward;
        }

        private static Color GetHeightHaloColor(Vector3 soundPosition)
        {
            float referenceY = FindPlayerReferenceY();
            float deltaY = soundPosition.Y - referenceY;
            if (deltaY > 2f) return new Color(0.82f, 0.95f, 1f, 1f);
            if (deltaY < -2f) return new Color(0.28f, 0.3f, 0.36f, 1f);
            return new Color(1f, 1f, 1f, 1f);
        }

        private static Color GetMediumColor(Vector3 soundPosition, Color fallback)
        {
            GridNode node = GridManager.Instance?.NodeFromWorldPoint(soundPosition);
            if (node == null) return fallback;

            if (node.terrainType == TerrainType.Water) return new Color(0.45f, 0.8f, 1f, 1f);
            if (node.terrainType == TerrainType.Ground || node.terrainType == TerrainType.Ice) return new Color(0.65f, 0.56f, 0.42f, 1f);
            return new Color(0.85f, 0.95f, 1f, 1f);
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

        private static float GetHaloRadius(GlyphProfile profile)
        {
            return Mathf.Max(0.9f, profile.Length * 0.08f);
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

        private static MeshInstance3D MakeGroundConeMesh(float length, float halfAngleRadians, StandardMaterial3D material)
        {
            int segments = 20;
            var st = new SurfaceTool();
            st.Begin(Mesh.PrimitiveType.Triangles);

            Vector3 origin = new(0f, GroundOffset, 0f);
            for (int i = 0; i < segments; i++)
            {
                float a0 = Mathf.Lerp(-halfAngleRadians, halfAngleRadians, i / (float)segments);
                float a1 = Mathf.Lerp(-halfAngleRadians, halfAngleRadians, (i + 1) / (float)segments);
                Vector3 p0 = new(Mathf.Sin(a0) * length, GroundOffset, -Mathf.Cos(a0) * length);
                Vector3 p1 = new(Mathf.Sin(a1) * length, GroundOffset, -Mathf.Cos(a1) * length);

                st.SetColor(material.AlbedoColor);
                st.AddVertex(origin);
                st.SetColor(material.AlbedoColor);
                st.AddVertex(p0);
                st.SetColor(material.AlbedoColor);
                st.AddVertex(p1);
            }

            st.GenerateNormals();
            var mesh = st.Commit();
            return new MeshInstance3D { Mesh = mesh, MaterialOverride = material };
        }

        private static MeshInstance3D MakeColumnMesh(float height, StandardMaterial3D material)
        {
            var mesh = new CylinderMesh
            {
                TopRadius = 0.35f,
                BottomRadius = 0.8f,
                Height = height,
                RadialSegments = 10,
                Rings = 3
            };

            return new MeshInstance3D
            {
                Mesh = mesh,
                MaterialOverride = material,
                Position = new Vector3(0f, (height * 0.5f) + VerticalOffset, 0f)
            };
        }

        private static MeshInstance3D MakeHaloMesh(float radius, StandardMaterial3D material)
        {
            int segments = 28;
            float thickness = radius * 0.22f;
            var st = new SurfaceTool();
            st.Begin(Mesh.PrimitiveType.Triangles);

            for (int i = 0; i < segments; i++)
            {
                float a0 = (i / (float)segments) * Mathf.Tau;
                float a1 = ((i + 1) / (float)segments) * Mathf.Tau;
                Vector3 o0 = new(Mathf.Cos(a0) * radius, GroundOffset + 0.02f, Mathf.Sin(a0) * radius);
                Vector3 o1 = new(Mathf.Cos(a1) * radius, GroundOffset + 0.02f, Mathf.Sin(a1) * radius);
                Vector3 i0 = new(Mathf.Cos(a0) * (radius - thickness), GroundOffset + 0.02f, Mathf.Sin(a0) * (radius - thickness));
                Vector3 i1 = new(Mathf.Cos(a1) * (radius - thickness), GroundOffset + 0.02f, Mathf.Sin(a1) * (radius - thickness));

                st.SetColor(material.AlbedoColor);
                st.AddVertex(o0);
                st.SetColor(material.AlbedoColor);
                st.AddVertex(i0);
                st.SetColor(material.AlbedoColor);
                st.AddVertex(o1);

                st.SetColor(material.AlbedoColor);
                st.AddVertex(i0);
                st.SetColor(material.AlbedoColor);
                st.AddVertex(i1);
                st.SetColor(material.AlbedoColor);
                st.AddVertex(o1);
            }

            st.GenerateNormals();
            return new MeshInstance3D { Mesh = st.Commit(), MaterialOverride = material };
        }

        private static MeshInstance3D MakeMediumOverlay(Vector3 soundPosition, GlyphProfile profile, StandardMaterial3D material)
        {
            GridNode node = GridManager.Instance?.NodeFromWorldPoint(soundPosition);
            float radius = Mathf.Max(1.4f, profile.Length * 0.2f);
            float stretch = 1f;
            if (node != null)
            {
                if (node.terrainType == TerrainType.Water) stretch = 1.25f;
                else if (node.terrainType == TerrainType.Ground || node.terrainType == TerrainType.Ice) stretch = 0.85f;
            }

            var mesh = new PlaneMesh { Size = new Vector2(radius * 2f * stretch, radius * 2f) };
            var inst = new MeshInstance3D
            {
                Mesh = mesh,
                MaterialOverride = material,
                Position = new Vector3(0f, GroundOffset + 0.01f, 0f)
            };
            inst.RotateX(-Mathf.Pi / 2f);
            return inst;
        }

        private static void SetAlpha(StandardMaterial3D material, float baseAlpha, float alphaScale)
        {
            Color c = material.AlbedoColor;
            material.AlbedoColor = new Color(c.R, c.G, c.B, Mathf.Clamp(alphaScale * baseAlpha, 0f, 1f));
        }

        private readonly struct GlyphProfile
        {
            public readonly float Length;
            public readonly float HalfAngle;
            public readonly float Brightness;
            public readonly float Duration;
            public readonly float ColumnHeight;
            public readonly float Urgency;

            public GlyphProfile(float length, float halfAngle, float brightness, float duration, float columnHeight, float urgency)
            {
                Length = length;
                HalfAngle = halfAngle;
                Brightness = brightness;
                Duration = duration;
                ColumnHeight = columnHeight;
                Urgency = urgency;
            }
        }
    }
}