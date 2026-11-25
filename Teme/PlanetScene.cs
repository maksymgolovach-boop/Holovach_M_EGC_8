// Varianta C: planeta
using OpenTK.Mathematics;
using OpenTK.Windowing.GraphicsLibraryFramework;

namespace Egc;

public sealed class PlanetScene : Scene
{
    private const string NewPlanet = "planetă nouă";
    private const string ToggleClouds = "nori: ascunde / arată";
    private const string ToggleCities = "orașe luminate: pornit / oprit";

    private const float SeedDuration = 2.5f;
    private const float CloudDuration = 0.8f;

    private Material _planet = null!, _atmosphere = null!;
    private Tween _seed = null!, _clouds = null!;
    private bool _cloudsVisible = true;
    private bool _cityLights = true;
    private int _planetNumber = 1;
    private readonly Random _random = new(2026);

    public override string Name => "tema C: planeta";

    public override IEnumerable<string> ActionNames => new[] { NewPlanet, ToggleClouds, ToggleCities };

    public override string Status =>
        $"planeta #{_planetNumber} | nori: {(_cloudsVisible ? "da" : "nu")} | orașe: {(_cityLights ? "da" : "nu")}";

    protected override void Load(Input input)
    {
        CameraPosition = new Vector3(0.0f, 0.8f, 4.2f);
        CameraTarget = Vector3.Zero;

        input.Bind(NewPlanet, Keys.G);
        input.Bind(ToggleClouds, Keys.Space);
        input.Bind(ToggleCities, Keys.K);

        Mesh sphere = AddMesh(Shapes.Sphere(1.0f, 128, 64));
        Mesh shell = AddMesh(Shapes.Sphere(1.0f, 64, 32));

        _planet = AddMaterial("planetă", "planet.vert", "planet.frag")
            .Param("uSeaLevel", 0.2f, 0.8f, 0.52f)
            .Param("uNoiseScale", 0.5f, 4.0f, 1.6f)
            .Param("uOctaves", 1, 8, 6)
            .Param("uIceLatitude", 0.5f, 1.05f, 0.82f)
            .Param("uCloudCover", 0.3f, 0.9f, 0.6f)
            .Param("uCloudSpeed", 0.0f, 0.5f, 0.06f);

        Vector3 seed = RandomSeed();
        _seed = new Tween(seed, seed, SeedDuration, Easing.EaseInOut);
        _clouds = Tween.Float(1.0f, 1.0f, CloudDuration, Easing.EaseInOut);
        _planet.Uniforms.Set("uSeed", seed);
        _planet.Uniforms.Set("uCloudAmount", 1.0f);
        _planet.Uniforms.Set("uCityLights", _cityLights);
        _planet.Uniforms.Set("uLightDir", new Vector3(1.0f, 0.35f, 0.55f));

        _atmosphere = AddMaterial("atmosferă", "atmosphere.vert", "atmosphere.frag")
            .Param("uAtmosphereStrength", 0.0f, 3.0f, 1.2f);
        _atmosphere.Transparent = true;
        _atmosphere.Uniforms.Set("uAtmosphereColor", new Vector3(0.35f, 0.6f, 1.0f));

        SceneObject globe = AddObject("planetă", sphere, _planet);
        globe.Transform.Rotation = new Vector3(0.0f, 0.0f, 23.5f);
        globe.Spin = new Vector3(0.0f, 8.0f, 0.0f);

        SceneObject air = AddObject("atmosferă", shell, _atmosphere);
        air.Transform.Scale = new Vector3(1.08f);
    }

    public override void Update(float dt, Input input)
    {
        base.Update(dt, input);

        if (input.WasPressed(NewPlanet))
        {
            _seed.GoTo(RandomSeed(), SeedDuration);
            _planetNumber++;
            ConsolePanel.PrintMessage($"planeta #{_planetNumber}");
        }

        if (input.WasPressed(ToggleClouds))
        {
            _cloudsVisible = !_cloudsVisible;
            _clouds.GoTo(_cloudsVisible ? 1.0f : 0.0f, CloudDuration);
            ConsolePanel.PrintMessage(_cloudsVisible ? "nori: vizibili" : "nori: ascunși");
        }

        if (input.WasPressed(ToggleCities))
        {
            _cityLights = !_cityLights;
            _planet.Uniforms.Set("uCityLights", _cityLights);
            ConsolePanel.PrintMessage(_cityLights ? "orașe: pornite" : "orașe: oprite");
        }

        _seed.Update(dt);
        _clouds.Update(dt);
        _planet.Uniforms.Set("uSeed", _seed.Value);
        _planet.Uniforms.Set("uCloudAmount", _clouds.Scalar);
    }

    private Vector3 RandomSeed()
    {
        return new Vector3(
            (_random.NextSingle() - 0.5f) * 40.0f,
            (_random.NextSingle() - 0.5f) * 40.0f,
            (_random.NextSingle() - 0.5f) * 40.0f);
    }

    public override IEnumerable<string> Describe()
    {
        foreach (string line in base.Describe()) yield return line;
        Vector3 s = _seed.Value;
        yield return $"Planeta        : #{_planetNumber}   sămânța: ({s.X:0.##}, {s.Y:0.##}, {s.Z:0.##}){(_seed.IsFinished ? "" : " (în tranziție)")}";
        yield return $"Nori           : {(_cloudsVisible ? "vizibili" : "ascunși")}   uCloudAmount = {_clouds.Scalar:0.##}";
        yield return $"Orașe luminate : {(_cityLights ? "da" : "nu")}";
    }
}
