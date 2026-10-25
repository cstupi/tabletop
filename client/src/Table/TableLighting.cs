using Godot;

namespace Tabletop.Client.Table;

/// <summary>
/// A well-lit gaming table (spec 2026-10-19-tabletop-look-design.md → Lighting): a warm key from the near left that casts soft
/// shadows, a cool unshadowed fill from the far right, and ambient light and reflections from a procedural sky (warm below, cool
/// above), with SSAO and AgX tone mapping. The background stays the theme's clear colour. The compatibility renderer skips SSAO
/// and the contact-hardened shadows; the baked wash on the meshes carries their recesses there. The key light's shadow splits and
/// distance, its soft shadows, SSAO and the atlas follow Settings → Graphics (<see cref="GraphicsApplier"/>).
/// </summary>
public static class TableLighting
{
    /// <summary>The key light's angular size, degrees: its shadows soften with distance from the caster (off on Low).</summary>
    public const float KeyAngularDistance = 1f;

    public static (DirectionalLight3D Key, Godot.Environment Environment) AddTo(Node3D parent)
    {
        var key = new DirectionalLight3D
        {
            Name = "Key", Rotation = new Vector3(Mathf.DegToRad(-52), Mathf.DegToRad(35), 0), LightColor = new Color(1f, 0.93f, 0.82f), LightEnergy = 1.5f,
            ShadowEnabled = true, ShadowBlur = 1.5f, LightAngularDistance = KeyAngularDistance,
            ShadowBias = 0.03f, ShadowNormalBias = 1f,
        };
        parent.AddChild(key);
        parent.AddChild(new DirectionalLight3D
        {
            Name = "Fill", Rotation = new Vector3(Mathf.DegToRad(-30), Mathf.DegToRad(215), 0), LightColor = new Color(0.62f, 0.72f, 1f), LightEnergy = 0.3f,
            LightSpecular = 0.2f,
        });
        var sky = new Sky
        {
            SkyMaterial = new ProceduralSkyMaterial
            {
                SkyTopColor = new Color(0.55f, 0.62f, 0.75f), SkyHorizonColor = new Color(0.72f, 0.7f, 0.66f),
                GroundHorizonColor = new Color(0.5f, 0.42f, 0.34f), GroundBottomColor = new Color(0.28f, 0.22f, 0.17f), SunAngleMax = 0,
            },
        };
        var environment = new Godot.Environment
        {
            BackgroundMode = Godot.Environment.BGMode.ClearColor, Sky = sky,
            AmbientLightSource = Godot.Environment.AmbientSource.Sky, AmbientLightEnergy = 0.55f, AmbientLightSkyContribution = 1f,
            ReflectedLightSource = Godot.Environment.ReflectionSource.Sky,
            TonemapMode = Godot.Environment.ToneMapper.Agx, TonemapExposure = 1.0f,
            SsaoEnabled = true, SsaoRadius = 1f, SsaoIntensity = 1.6f, SsaoPower = 1.5f, SsaoDetail = 0.5f, SsaoHorizon = 0.06f, SsaoLightAffect = 0.15f,
            AdjustmentEnabled = true, AdjustmentSaturation = 1.04f,
        };
        parent.AddChild(new WorldEnvironment { Environment = environment });
        return (key, environment);
    }
}
