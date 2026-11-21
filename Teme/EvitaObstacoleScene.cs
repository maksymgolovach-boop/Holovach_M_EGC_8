// Varianta B: evită obstacolele
using OpenTK.Mathematics;
using OpenTK.Windowing.GraphicsLibraryFramework;

namespace Egc;

/// <summary>
/// Tema 6, varianta B: evită obstacolele.
/// Un drum cu trei benzi. Jucătorul (cubul cyan) stă pe loc pe Z, iar obstacolele (cutiile magenta)
/// vin spre el (spre +Z).
///  - stânga / dreapta (APĂSARE) mută jucătorul pe banda vecină, lin: Tween cu EaseOut, 0,15 s;
///  - Space = salt cu gravitație (Euler semi-implicit, secțiunea 5.4);
///  - obstacolele sunt JOASE (se pot sări) sau ÎNALTE (trebuie ocolite);
///  - viteza lumii și frecvența obstacolelor cresc în timp;
///  - coliziunea se testează cu cutii aliniate cu axele (AABB);
///  - la coliziune jocul se oprește, titlul arată scorul (timpul supraviețuit) și recordul; R = joc nou.
/// Bonus: liniile întrerupte de pe drum se deplasează odată cu lumea.
///
/// Ierarhii (secțiunea 6):
///  - jucătorul: rig (banda, X) → pivotul de salt (Y, înclinare, căzătură) → cubul;
///  - fiecare obstacol: un pivot (poziția pe drum, „răsărirea” cu Tween) → cutia lui;
///  - marcajele: un pivot care alunecă pe Z; liniile (copiii lui) îl urmează.
/// Toate animațiile folosesc doar dt (timpul de joc): pauza, scara timpului și blocarea (B) funcționează.
/// </summary>
public sealed class EvitaObstacoleScene : Scene
{
    // --- Acțiunile scenei (nume diferite de cele ale scenei laboratorului) -------------------------
    private const string LaneLeft = "bandă stânga";
    private const string LaneRight = "bandă dreapta";
    private const string Jump = "salt";
    private const string NewGame = "joc nou";

    // --- Parametrii jocului -----------------------------------------------------------------------
    private const float LaneWidth = 1.6f;            // distanța dintre centrele benzilor
    private const float LaneSwitchTime = 0.15f;      // secunde (sub 0,2 s, cu EaseOut)
    private const float Gravity = 40.0f;             // unități / s²
    private const float JumpSpeed = 11.0f;           // înălțimea maximă v₀² / 2g ≈ 1,5 unități

    private const float StartSpeed = 12.0f;          // viteza lumii la început (unități / s)
    private const float MaxSpeed = 30.0f;
    private const float StartInterval = 1.4f;        // secunde între două rânduri de obstacole
    private const float MinInterval = 0.65f;
    private const float RampTime = 60.0f;            // după 60 s dificultatea este maximă

    private const float SpawnZ = -80.0f;             // aici apar obstacolele
    private const float DespawnZ = 6.0f;             // aici dispar (în spatele jucătorului)
    private const float RiseTime = 0.35f;            // obstacolele „răsar” din drum

    private const float LowHeight = 0.6f;            // obstacol jos: se poate sări (0,6 < 1,5)
    private const float HighHeight = 2.2f;           // obstacol înalt: nu se poate sări (2,2 > 1,5)

    private const float MaxStep = 0.25f;             // cel mai lung drum al lumii într-un sub-pas
    private const float DashSpacing = 6.0f;          // distanța dintre începuturile a două linii
    private const float DashLength = 2.6f;

    // Camera, față de pivotul jucătorului (rig): îl urmează pe X, deci jucătorul rămâne la mijlocul ecranului.
    private static readonly Vector3 CameraOffset = new(0.0f, 3.0f, 8.5f);
    private static readonly Vector3 CameraTargetOffset = new(0.0f, 0.8f, -20.0f);

    // Cutia jucătorului (cubul cu latura 1), față de baza lui; puțin mai mică, ca să fie „corect”.
    private static readonly Vector3 PlayerBoxMin = new(-0.45f, 0.02f, -0.45f);
    private static readonly Vector3 PlayerBoxMax = new(0.45f, 0.98f, 0.45f);

    // Culorile din captură: fundal închis (Game), asfalt gri-albăstrui, linii gălbui, cyan și magenta.
    private static readonly Color4 RoadColor = new(0.21f, 0.23f, 0.27f, 1.0f);
    private static readonly Color4 DashColor = new(0.88f, 0.84f, 0.58f, 1.0f);
    private static readonly Color4 PlayerColor = new(0.28f, 0.80f, 0.95f, 1.0f);
    private static readonly Color4 ObstacleColor = new(0.72f, 0.24f, 0.48f, 1.0f);
    private static readonly Color4 HitColor = new(1.0f, 0.3f, 0.25f, 1.0f);

    private enum GameState { Running, GameOver }

    /// <summary>Un obstacol din rezervă (pool): un pivot cu cutia lui și cutia de coliziune.</summary>
    private sealed class Obstacle
    {
        public SceneObject Root = null!;   // pivotul: stă pe drum, la baza obstacolului
        public SceneObject Box = null!;    // forma, copilul pivotului
        public bool High;
        public bool Active;
        public int Lane;
        public Vector3 BoxMin, BoxMax;     // cutia de coliziune, față de pivot
        public Tween Rise = null!;         // scalarea pe Y a pivotului: 0 → 1, EaseOut

        public void SetActive(bool active)
        {
            Active = active;
            Box.Visible = active;          // pivotul nu are formă; ascundem copilul
        }
    }

    // --- Starea jocului ---------------------------------------------------------------------------
    private readonly Random _random = new();
    private readonly List<Obstacle> _obstacles = new();
    private GameState _state = GameState.Running;
    private float _score;            // timpul supraviețuit (timp de joc)
    private float _record;
    private float _speed = StartSpeed;
    private float _interval = StartInterval;
    private float _spawnTimer;
    private float _distance;         // cât a „mers” lumea (pentru marcaje)
    private int _rows;               // câte rânduri de obstacole au apărut

    // --- Jucătorul --------------------------------------------------------------------------------
    private SceneObject _rig = null!;        // pivot: X = banda (Tween)
    private SceneObject _jumper = null!;     // pivot, copilul rig-ului: Y = saltul, rotații
    private SceneObject _cube = null!;       // forma jucătorului, copilul pivotului de salt
    private int _lane = 1;                   // 0 = stânga, 1 = mijloc, 2 = dreapta
    private Tween _laneTween = null!;        // poziția rig-ului pe X
    private Tween _fallTween = null!;        // căzătura la coliziune (unghiul pe X)
    private float _height;                   // înălțimea bazei cubului deasupra drumului
    private float _verticalSpeed;

    private SceneObject _dashes = null!;     // pivot: liniile întrerupte dintre benzi

    public override string Name => "tema B: evită obstacolele";

    public override IEnumerable<string> ActionNames => new[] { LaneLeft, LaneRight, Jump, NewGame };

    public override string Status => _state == GameState.Running
        ? $"scor {_score:F1} s | record {_record:F1} s | viteza {_speed:F1}"
        : $"JOC TERMINAT: scor {_score:F1} s | record {_record:F1} s | R = joc nou";

    private bool OnGround => _height <= 1e-4f;
    private static float LaneX(int lane) => (lane - 1) * LaneWidth;

    // =============================================================================================
    // Construirea scenei
    // =============================================================================================

    public override void Load(Input input)
    {
        // Camera joasă, în spatele jucătorului, privind în lungul drumului (punctul de fugă sus, la mijloc).
        CameraPosition = CameraOffset;
        CameraTarget = CameraTargetOffset;

        input.Bind(LaneLeft, Keys.Left, Keys.A);
        input.Bind(LaneRight, Keys.Right, Keys.D);
        input.Bind(Jump, Keys.Space, Keys.Up, Keys.W);
        input.Bind(NewGame, Keys.R);

        Mesh cube = AddMesh(Shapes.ShadedCube());

        BuildRoad(cube);
        BuildPlayer(cube);
        BuildObstaclePool(cube, count: 16);

        StartNewGame();
    }

    private void BuildRoad(Mesh cube)
    {
        const float roadLength = 130.0f, roadCenterZ = -50.0f;

        // Asfaltul: o cutie subțire, cu fața de sus la y = 0. Fără iarbă: în jur se vede fundalul.
        SceneObject road = AddObject("drum", cube);
        road.Transform.Position = new Vector3(0.0f, -0.05f, roadCenterZ);
        road.Transform.Scale = new Vector3(3.0f * LaneWidth + 0.4f, 0.1f, roadLength);
        road.Tint = RoadColor;

        // BONUS: liniile întrerupte sunt copiii unui pivot care alunecă pe Z (poziția lui =
        // distanța % DashSpacing). Copiii îl urmează; când pivotul a mers o distanță întreagă
        // revine la 0, iar imaginea este identică, deci mișcarea pare continuă.
        _dashes = AddObject("marcaje", null);
        for (float z = -114.0f; z <= 8.0f; z += DashSpacing)
        {
            foreach (float x in new[] { -0.5f * LaneWidth, 0.5f * LaneWidth })
            {
                SceneObject dash = AddObject("linie", cube, _dashes);
                dash.Transform.Position = new Vector3(x, 0.006f, z);
                dash.Transform.Scale = new Vector3(0.1f, 0.01f, DashLength);
                dash.Tint = DashColor;
            }
        }
    }

    private void BuildPlayer(Mesh cube)
    {
        // Rig-ul se mută între benzi (X); pivotul de salt este copilul lui și urcă / coboară (Y).
        // Pivotul de salt stă la baza cubului, deci înclinarea și căzătura se fac în jurul bazei.
        _rig = AddObject("jucător", null);
        _jumper = AddObject("salt", null, _rig);

        _cube = AddObject("cub", cube, _jumper);
        _cube.Transform.Position = new Vector3(0.0f, 0.5f, 0.0f);
    }

    /// <summary>Obstacolele se creează o singură dată (rezervă); la nevoie se activează și se refolosesc.</summary>
    private void BuildObstaclePool(Mesh cube, int count)
    {
        for (int k = 0; k < count; k++)
        {
            _obstacles.Add(BuildObstacle(cube, false, new Vector3(1.3f, LowHeight, 0.8f)));
            _obstacles.Add(BuildObstacle(cube, true, new Vector3(1.2f, HighHeight, 1.0f)));
        }
    }

    private Obstacle BuildObstacle(Mesh cube, bool high, Vector3 size)
    {
        var o = new Obstacle { High = high };
        o.Root = AddObject(high ? "obstacol înalt" : "obstacol jos", null);
        o.Box = AddObject("cutie", cube, o.Root);
        o.Box.Transform.Position = new Vector3(0.0f, 0.5f * size.Y, 0.0f);
        o.Box.Transform.Scale = size;
        o.Box.Tint = ObstacleColor;

        o.BoxMin = new Vector3(-0.5f * size.X, 0.0f, -0.5f * size.Z);
        o.BoxMax = new Vector3(0.5f * size.X, size.Y, 0.5f * size.Z);

        o.Rise = new Tween(new Vector3(1.0f, 0.02f, 1.0f), Vector3.One, RiseTime, Easing.EaseOut);
        o.SetActive(false);
        return o;
    }

    // =============================================================================================
    // Jocul
    // =============================================================================================

    private void StartNewGame()
    {
        _state = GameState.Running;
        _score = 0.0f;
        _speed = StartSpeed;
        _interval = StartInterval;
        _spawnTimer = 1.0f;          // primul rând apare după 1 s
        _distance = 0.0f;
        _rows = 0;

        foreach (Obstacle o in _obstacles) o.SetActive(false);

        _lane = 1;
        _laneTween = new Tween(Vector3.Zero, Vector3.Zero, LaneSwitchTime, Easing.EaseOut);
        _fallTween = new Tween(Vector3.Zero, Vector3.Zero, 0.4f, Easing.EaseOut);
        _height = 0.0f;
        _verticalSpeed = 0.0f;

        _rig.Transform.Position = Vector3.Zero;
        _jumper.Transform.Position = Vector3.Zero;
        _jumper.Transform.Rotation = Vector3.Zero;
        _cube.Tint = PlayerColor;
        _dashes.Transform.Position = Vector3.Zero;
    }

    public override void Update(float dt, Input input)
    {
        base.Update(dt, input);

        if (input.WasPressed(NewGame))
        {
            StartNewGame();
            ConsolePanel.PrintMessage("joc nou");
        }

        if (_state == GameState.Running)
        {
            UpdateDifficulty();
            HandlePlayerInput(input);
        }

        // Sub-pași: la viteză mare, un pas de 0,1 s (după o blocare, B) ar muta lumea până la 3 unități,
        // mai mult decât grosimea unui obstacol, iar coliziunea ar putea fi „sărită” (secțiunea 2.2).
        // Împărțim pasul în bucăți de cel mult MaxStep unități de drum și testăm coliziunea după fiecare.
        int steps = Math.Max(1, (int)MathF.Ceiling(_speed * dt / MaxStep));
        float h = dt / steps;
        for (int i = 0; i < steps; i++)
        {
            if (_state == GameState.Running)
            {
                MoveWorld(h);
                SpawnObstacles(h);
                _score += h;
            }

            UpdatePlayer(h);
            UpdateObstacleAnimations(h);

            if (_state == GameState.Running && CheckCollision(out Obstacle? hit))
            {
                GameOver(hit!);
            }
        }
    }

    /// <summary>Viteza și frecvența cresc odată cu timpul supraviețuit (interpolare, ca la Tween).</summary>
    private void UpdateDifficulty()
    {
        float d = Math.Clamp(_score / RampTime, 0.0f, 1.0f);
        _speed = MathHelper.Lerp(StartSpeed, MaxSpeed, d);
        _interval = MathHelper.Lerp(StartInterval, MinInterval, d);
    }

    private void HandlePlayerInput(Input input)
    {
        // Schimbarea benzii: o APĂSARE = o bandă. GoTo pornește din poziția curentă,
        // deci merge și dacă jucătorul este încă în mișcare spre altă bandă.
        int lane = _lane;
        if (input.WasPressed(LaneLeft)) lane--;
        if (input.WasPressed(LaneRight)) lane++;
        lane = Math.Clamp(lane, 0, 2);
        if (lane != _lane)
        {
            _lane = lane;
            _laneTween.GoTo(new Vector3(LaneX(_lane), 0.0f, 0.0f), LaneSwitchTime);
        }

        // Saltul: doar de pe drum.
        if (OnGround && input.WasPressed(Jump))
        {
            _verticalSpeed = JumpSpeed;
        }
    }

    /// <summary>Lumea vine spre jucător: obstacolele și marcajele înaintează pe +Z cu viteza × dt.</summary>
    private void MoveWorld(float dt)
    {
        float step = _speed * dt;
        _distance += step;

        foreach (Obstacle o in _obstacles)
        {
            if (!o.Active) continue;
            o.Root.Transform.Position.Z += step;
            if (o.Root.Transform.Position.Z > DespawnZ)
            {
                o.SetActive(false);   // înapoi în rezervă
            }
        }

        // Pivotul marcajelor: liniile (copiii lui) îl urmează.
        _dashes.Transform.Position = new Vector3(0.0f, 0.0f, _distance % DashSpacing);
    }

    private void SpawnObstacles(float dt)
    {
        _spawnTimer -= dt;
        if (_spawnTimer > 0.0f) return;
        _spawnTimer += _interval;

        // Un rând: 1, 2 sau 3 benzi ocupate; cu cât e mai greu, cu atât mai multe.
        // Regula care face jocul mereu rezolvabil: dacă toate trei benzile sunt ocupate,
        // cel puțin un obstacol este jos (se poate sări).
        float d = Math.Clamp(_score / RampTime, 0.0f, 1.0f);
        double r = _random.NextDouble();
        int count = r < 0.55 - 0.35 * d ? 1 : r < 0.92 - 0.2 * d ? 2 : 3;

        var lanes = new List<int> { 0, 1, 2 };
        Shuffle(lanes);
        int lowLane = lanes[_random.Next(count)];   // folosit doar când sunt 3

        for (int k = 0; k < count; k++)
        {
            int lane = lanes[k];
            bool high = count == 3 ? lane != lowLane : _random.NextDouble() < 0.5;
            Obstacle? o = _obstacles.FirstOrDefault(x => !x.Active && x.High == high);
            if (o == null) continue;   // rezerva este goală (nu ar trebui să se întâmple)

            o.Lane = lane;
            o.Root.Transform.Position = new Vector3(LaneX(lane), 0.0f, SpawnZ);
            o.Rise.Restart();
            o.Root.Transform.Scale = o.Rise.Value;
            o.SetActive(true);
        }
        _rows++;
    }

    private void Shuffle(List<int> list)
    {
        for (int i = list.Count - 1; i > 0; i--)
        {
            int j = _random.Next(i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
    }

    private void UpdateObstacleAnimations(float dt)
    {
        foreach (Obstacle o in _obstacles)
        {
            if (!o.Active) continue;
            o.Rise.Update(dt);
            o.Root.Transform.Scale = o.Rise.Value;   // pivotul se scalează, cutia îl urmează
        }
    }

    private void UpdatePlayer(float dt)
    {
        // 1. Banda: Tween cu EaseOut pe X.
        _laneTween.Update(dt);
        float x = _laneTween.Value.X;
        _rig.Transform.Position = new Vector3(x, 0.0f, 0.0f);

        // Camera se mută odată cu pivotul (aceeași deplasare pe X, deci tot lin, cu EaseOut).
        // Nu urmează saltul: pivotul de salt este copilul rig-ului, iar camera ține de rig.
        CameraPosition = _rig.WorldPosition + CameraOffset;
        CameraTarget = _rig.WorldPosition + CameraTargetOffset;

        // 2. Gravitația (secțiunea 5.4): întâi viteza, apoi poziția, până la drum.
        if (!OnGround || _verticalSpeed > 0.0f)
        {
            _verticalSpeed -= Gravity * dt;
            _height += _verticalSpeed * dt;
            if (_height <= 0.0f)
            {
                _height = 0.0f;
                _verticalSpeed = 0.0f;
            }
        }
        _jumper.Transform.Position = new Vector3(0.0f, _height, 0.0f);

        // 3. O ușoară înclinare în direcția mișcării (cât mai are drum până la bandă) și căzătura la final.
        float lean = Math.Clamp(-(LaneX(_lane) - x) * 8.0f, -10.0f, 10.0f);
        _fallTween.Update(dt);
        _jumper.Transform.Rotation = new Vector3(_fallTween.Value.X, 0.0f, _state == GameState.Running ? lean : 0.0f);
    }

    // =============================================================================================
    // Coliziunea (AABB)
    // =============================================================================================

    /// <summary>Două cutii aliniate cu axele se suprapun dacă se suprapun pe toate cele trei axe.</summary>
    private static bool Overlap(Vector3 minA, Vector3 maxA, Vector3 minB, Vector3 maxB) =>
        minA.X < maxB.X && maxA.X > minB.X &&
        minA.Y < maxB.Y && maxA.Y > minB.Y &&
        minA.Z < maxB.Z && maxA.Z > minB.Z;

    private bool CheckCollision(out Obstacle? hit)
    {
        Vector3 basePoint = new(_rig.Transform.Position.X, _height, 0.0f);
        Vector3 pMin = basePoint + PlayerBoxMin;
        Vector3 pMax = basePoint + PlayerBoxMax;

        foreach (Obstacle o in _obstacles)
        {
            if (!o.Active) continue;
            Vector3 p = o.Root.Transform.Position;
            if (Overlap(pMin, pMax, p + o.BoxMin, p + o.BoxMax))
            {
                hit = o;
                return true;
            }
        }
        hit = null;
        return false;
    }

    private void GameOver(Obstacle hit)
    {
        _state = GameState.GameOver;
        bool newRecord = _score > _record;
        if (newRecord) _record = _score;

        // Cubul se răstoarnă pe spate, lin (Tween pe unghiul pivotului de salt), și se înroșește.
        _fallTween = new Tween(Vector3.Zero, new Vector3(90.0f, 0.0f, 0.0f), 0.4f, Easing.EaseOut);
        _cube.Tint = HitColor;

        string recordText = newRecord ? " (RECORD NOU!)" : $", record {_record:F1} s";
        ConsolePanel.PrintMessage($"coliziune cu un obstacol {(hit.High ? "înalt" : "jos")} pe banda {hit.Lane + 1}: " +
                                  $"scor {_score:F1} s{recordText}. R = joc nou");
    }

    // =============================================================================================
    // Tasta I
    // =============================================================================================

    public override IEnumerable<string> Describe()
    {
        foreach (string line in base.Describe()) yield return line;
        yield return $"Stare          : {(_state == GameState.Running ? "în joc" : "joc terminat")}   " +
                     $"scor {_score:F2} s   record {_record:F2} s";
        yield return $"Lumea          : viteza {_speed:F1} u/s   un rând la {_interval:F2} s   " +
                     $"distanța {_distance:F0} u   rânduri {_rows}   obstacole active {_obstacles.Count(o => o.Active)}";
        yield return $"Jucător        : banda {_lane + 1}/3   x = {_rig.Transform.Position.X:F2}   " +
                     $"h = {_height:F2}   v_y = {_verticalSpeed:F2}   {(OnGround ? "pe drum" : "în aer")}";
    }
}
