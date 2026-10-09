using EpochsUnbound.CameraControl;
using EpochsUnbound.WorldGen;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.InputSystem;

namespace EpochsUnbound.Settlement
{
    /// <summary>
    /// Player side of the settlement: building placement with a ghost preview, citizen selection and
    /// direct orders (the manual half of hybrid control), and the resource HUD.
    /// </summary>
    public sealed class SettlementController : MonoBehaviour
    {
        public SettlementSettings Settings;
        public WorldSettings World;
        public RtsCamera Rig;
        public CitizenRenderer Citizens;
        public Material BuildingMaterial;
        [Tooltip("Citizens spawned by the F9 stress test.")]
        public int StressTestCount = 10000;

        EntityManager _em;
        TerrainParams _terrain;
        BuildingDef _placing;
        GameObject _ghost;
        MaterialPropertyBlock _mpb;
        Material _material;
        Mesh _stoneBlock, _soilBlock;
        readonly System.Collections.Generic.Dictionary<BuildingKind, Mesh> _models = new();
        string _message = "Place your Town Centre on flat, dry land (left click).";
        bool _hasHit;
        float3 _hit;
        Rect _panel;
        bool _started;
        GUIStyle _hintStyle;
        SettlementRules _rules;
        static readonly int BaseColor = Shader.PropertyToID("_BaseColor");

        /// <summary>Building currently being placed, if any.</summary>
        public BuildingKind? Placing => _placing?.Kind;

        /// <summary>Status line shown in the HUD.</summary>
        public string Message => _message;

        void Start()
        {
            _em = Unity.Entities.World.DefaultGameObjectInjectionWorld.EntityManager;
            _terrain = World.ToParams();
            _mpb = new MaterialPropertyBlock();
            _material = new Material(BuildingMaterial);
            _material.SetTexture("_BaseMap", Models.CreatePalette());
            _stoneBlock = Models.Block(Models.Stone);
            _soilBlock = Models.Block(Models.Soil);
            SettlementOps.CreateColony(_em, Settings, _terrain);
            _rules = Settings.ToRules();
            _started = true;
            BeginPlacing(BuildingKind.TownCentre);
        }

        void Update()
        {
            var kb = Keyboard.current;
            var mouse = Mouse.current;
            if (kb == null || mouse == null) return;

            if (kb.tKey.wasPressedThisFrame) BeginPlacing(BuildingKind.TownCentre);
            if (kb.hKey.wasPressedThisFrame) BeginPlacing(BuildingKind.House);
            if (kb.fKey.wasPressedThisFrame) BeginPlacing(BuildingKind.Farm);
            if (kb.lKey.wasPressedThisFrame) BeginPlacing(BuildingKind.LumberCamp);
            if (kb.escapeKey.wasPressedThisFrame) { CancelPlacing(); Citizens.Selected.Clear(); }
            if (kb.f9Key.wasPressedThisFrame && Settings.DeveloperMode) StressTest();

            Vector2 mp = mouse.position.ReadValue();
            bool overUi = _panel.Contains(new Vector2(mp.x, Screen.height - mp.y));
            var ray = Rig.Camera.ScreenPointToRay(mp);
            _hasHit = !overUi && TerrainSampler.Raycast(ray.origin, ray.direction, Rig.Camera.farClipPlane, _terrain, out _hit);
            Citizens.Selected.RemoveAll(e => !_em.Exists(e));

            if (_placing != null) UpdatePlacing(mouse, kb);
            else if (_hasHit) UpdateSelection(mouse, kb);
        }

        // ---------- placement ----------

        void BeginPlacing(BuildingKind kind)
        {
            CancelPlacing();
            _placing = Settings.Def(kind);
            _ghost = CreateModel(_placing, "Ghost");
        }

        void CancelPlacing()
        {
            _placing = null;
            if (_ghost != null) Destroy(_ghost);
        }

        void UpdatePlacing(Mouse mouse, Keyboard kb)
        {
            if (mouse.rightButton.wasPressedThisFrame) { CancelPlacing(); return; }
            _ghost.SetActive(_hasHit);
            if (!_hasHit) return;

            var result = SettlementOps.CanPlace(_em, _placing, _hit, _terrain, out float yield);
            PlaceModel(_ghost, _placing, _hit, result == SettlementOps.PlaceResult.Ok ? new Color(0.5f, 1f, 0.5f) : new Color(1f, 0.35f, 0.3f));
            float wood = SettlementOps.GetColony(_em).Wood;
            _message = result == SettlementOps.PlaceResult.Ok
                ? $"{_placing.Name}: {Describe(_placing, yield)}, cost {_placing.WoodCost} wood (you have {wood:0})  - left click to build, right click to cancel"
                : $"{_placing.Name}: {Reason(result, _placing, wood)}";

            if (!mouse.leftButton.wasPressedThisFrame || result != SettlementOps.PlaceResult.Ok) return;
            float3 at = new float3(_hit.x, TerrainSampler.SurfaceHeight(_hit.xz, _terrain), _hit.z);
            if (SettlementOps.TryPlace(_em, Settings, _placing, at, _terrain, out _) != SettlementOps.PlaceResult.Ok) return;

            PlaceModel(CreateModel(_placing, _placing.Name), _placing, at, Color.white);
            _message = $"Built {_placing.Name} for {_placing.WoodCost} wood.";
            if (SettlementOps.GetColony(_em).Wood < _placing.WoodCost)
            {
                _message += $" Not enough wood for another (need {_placing.WoodCost}).";
                CancelPlacing();
            }
            else if (!kb.shiftKey.isPressed) CancelPlacing(); // hold Shift to keep placing
        }

        static string Describe(BuildingDef def, float yield) => def.Kind switch
        {
            BuildingKind.Farm => $"fertility {yield:P0}",
            BuildingKind.LumberCamp => $"forest {yield:P0}",
            BuildingKind.House => $"houses {def.Housing}",
            _ => "OK",
        };

        static string Reason(SettlementOps.PlaceResult r, BuildingDef def, float wood) => r switch
        {
            SettlementOps.PlaceResult.NeedTownCentre => "build a Town Centre first",
            SettlementOps.PlaceResult.OnlyOneTownCentre => "only one Town Centre is allowed for now",
            SettlementOps.PlaceResult.NotEnoughWood => $"not enough wood: need {def.WoodCost}, you have {wood:0}",
            SettlementOps.PlaceResult.Water => "must be on dry land",
            SettlementOps.PlaceResult.TooSteep => "ground too steep",
            SettlementOps.PlaceResult.Overlaps => "overlaps another building",
            SettlementOps.PlaceResult.PoorSite => "poor site (farms need fertile land, lumber camps need forest)",
            _ => "",
        };

        /// <summary>Building model plus a foundation block (child 0) that reaches down to the lowest ground.</summary>
        GameObject CreateModel(BuildingDef def, string name)
        {
            if (!_models.TryGetValue(def.Kind, out var mesh)) _models[def.Kind] = mesh = Models.Building(def);
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = _material;

            var foundation = new GameObject("Foundation");
            foundation.transform.SetParent(go.transform, false);
            foundation.AddComponent<MeshFilter>().sharedMesh = Models.Foundation(def.Kind).colour == Models.Stone ? _stoneBlock : _soilBlock;
            foundation.AddComponent<MeshRenderer>().sharedMaterial = _material;
            return go;
        }

        void PlaceModel(GameObject go, BuildingDef def, float3 at, Color tint)
        {
            SettlementOps.VisualExtent(def, at, _terrain, out float bottom, out float baseY);
            go.transform.position = new Vector3(at.x, baseY, at.z);
            var foundation = go.transform.GetChild(0);
            foundation.localPosition = new Vector3(0, bottom - baseY, 0);
            float fs = Models.Foundation(def.Kind).scale;
            foundation.localScale = new Vector3(def.Size.x * fs, baseY - bottom + 0.05f, def.Size.z * fs);
            _mpb.SetColor(BaseColor, tint);
            foreach (var r in go.GetComponentsInChildren<Renderer>()) r.SetPropertyBlock(_mpb);
        }

        // ---------- selection and orders ----------

        void UpdateSelection(Mouse mouse, Keyboard kb)
        {
            if (mouse.leftButton.wasPressedThisFrame)
            {
                if (!kb.shiftKey.isPressed) Citizens.Selected.Clear();
                var picked = NearestCitizen(_hit, 2f + Rig.Distance * 0.01f);
                if (picked != Entity.Null && !Citizens.Selected.Contains(picked)) Citizens.Selected.Add(picked);
            }

            if (mouse.rightButton.wasPressedThisFrame && Citizens.Selected.Count > 0)
            {
                var building = BuildingAt(_hit);
                int ok = 0;
                for (int i = 0; i < Citizens.Selected.Count; i++)
                {
                    var c = Citizens.Selected[i];
                    if (building != Entity.Null) { if (SettlementOps.OrderWork(_em, c, building)) ok++; }
                    else
                    {
                        // Spread a group around the clicked point.
                        float a = i * 2.4f, r = 1.5f * math.sqrt(i);
                        SettlementOps.OrderMove(_em, c, _hit + new float3(math.cos(a) * r, 0, math.sin(a) * r));
                    }
                }
                _message = building != Entity.Null
                    ? $"{ok} of {Citizens.Selected.Count} assigned to work there."
                    : $"Moving {Citizens.Selected.Count} citizen(s). They stay until given work.";
            }
        }

        Entity NearestCitizen(float3 at, float radius)
        {
            using var q = _em.CreateEntityQuery(typeof(Citizen), typeof(CitizenMotion));
            using var entities = q.ToEntityArray(Unity.Collections.Allocator.Temp);
            using var motions = q.ToComponentDataArray<CitizenMotion>(Unity.Collections.Allocator.Temp);
            Entity best = Entity.Null;
            float bestD = radius * radius;
            for (int i = 0; i < entities.Length; i++)
            {
                float d = math.distancesq(motions[i].Position.xz, at.xz);
                if (d < bestD) { bestD = d; best = entities[i]; }
            }
            return best;
        }

        Entity BuildingAt(float3 at)
        {
            using var q = _em.CreateEntityQuery(typeof(Building));
            using var entities = q.ToEntityArray(Unity.Collections.Allocator.Temp);
            using var data = q.ToComponentDataArray<Building>(Unity.Collections.Allocator.Temp);
            for (int i = 0; i < entities.Length; i++)
                if (data[i].JobSlots > 0 && math.distance(data[i].Position.xz, at.xz) < data[i].Radius) return entities[i];
            return Entity.Null;
        }

        void StressTest()
        {
            var colony = SettlementOps.GetColony(_em);
            if (!_em.Exists(colony.TownCentre)) { _message = "Place a Town Centre before the stress test."; return; }
            var tc = _em.GetComponentData<Building>(colony.TownCentre);
            SettlementOps.SpawnAdults(_em, StressTestCount, tc.Position, 400f, wander: true);
            colony = SettlementOps.GetColony(_em);
            colony.Food += StressTestCount * 10f; // keep them fed so the test measures load, not a famine
            SettlementOps.SetColony(_em, colony);
            _message = $"DEV: stress test spawned {StressTestCount} citizens and free food, ignoring housing and resources.";
        }

        // ---------- HUD ----------

        void OnGUI()
        {
            if (!_started) return;
            var c = SettlementOps.GetColony(_em);

            // Scale the HUD with screen height so it stays readable on large displays.
            float scale = Mathf.Max(1f, Screen.height / 800f);
            GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1f));
            float w = Screen.width / scale, h = Screen.height / scale;

            string hint = Hint(c);
            if (hint != null)
            {
                _hintStyle ??= new GUIStyle(GUI.skin.box) { fontSize = 22, wordWrap = true, alignment = TextAnchor.MiddleCenter };
                GUI.Box(new Rect(w * 0.5f - 330, 50, 660, 64), hint, _hintStyle);
            }

            var panel = new Rect(10, h - 190, 640, 180);
            _panel = new Rect(panel.x * scale, panel.y * scale, panel.width * scale, panel.height * scale);
            GUI.Box(panel, GUIContent.none);
            GUILayout.BeginArea(new Rect(panel.x + 8, panel.y + 6, panel.width - 16, panel.height - 12));
            GUILayout.Label($"Food {c.Food:0.0}   Wood {c.Wood:0}   Population {c.Population} / {c.Housing} housing   " +
                            $"Jobs {c.Employed} / {c.Jobs}   Births {c.Births}  Deaths {c.Deaths}" + (c.Starving ? "   STARVING" : ""));
            GUILayout.BeginHorizontal();
            foreach (var def in Settings.Buildings)
            {
                bool tcBlocked = def.Kind == BuildingKind.TownCentre && _em.Exists(c.TownCentre);
                GUI.enabled = c.Wood >= def.WoodCost && !tcBlocked;
                if (GUILayout.Button($"{def.Name} ({def.WoodCost} wood)")) BeginPlacing(def.Kind);
            }
            GUI.enabled = true;
            GUILayout.EndHorizontal();
            GUILayout.Label(SelectionText());
            GUILayout.Label(_message);
            GUILayout.Label("T/H/F/L build (Shift keeps placing)   LMB select (Shift adds)   RMB on farm/camp: work there, on ground: move   Esc cancel" + (Settings.DeveloperMode ? "   DEV: F9 stress test (ignores resources)" : ""));
            GUILayout.EndArea();
        }

        /// <summary>Next step for a new player, shown as a banner until the basics are in place.</summary>
        string Hint(Colony c)
        {
            if (_placing != null && _placing.Kind == BuildingKind.TownCentre)
                return "Move the mouse over flat, dry land and LEFT CLICK to place your Town Centre (green = OK, red = not allowed)";
            if (!_em.Exists(c.TownCentre)) return "Press T (or the Town Centre button) to place your Town Centre";
            if (!Has(BuildingKind.Farm)) return "Your people need food: press F and place a Farm on green grassland";
            if (!Has(BuildingKind.LumberCamp)) return "Press L and place a Lumber Camp next to dark-green forest for wood";
            if (c.Housing - c.Population < 2 && c.Population < 20) return "Press H to build Houses so your population can grow";
            return null;
        }

        bool Has(BuildingKind kind)
        {
            using var q = _em.CreateEntityQuery(typeof(Building));
            using var data = q.ToComponentDataArray<Building>(Unity.Collections.Allocator.Temp);
            foreach (var b in data) if (b.Kind == kind) return true;
            return false;
        }

        string SelectionText()
        {
            if (Citizens.Selected.Count == 0) return "No citizens selected.";
            var e = Citizens.Selected[0];
            if (!_em.Exists(e)) return "";
            var c = _em.GetComponentData<Citizen>(e);
            string job = _em.HasComponent<Building>(c.Job) ? _em.GetComponentData<Building>(c.Job).Kind.ToString() : "none";
            string stage = c.AgeTicks >= _rules.AdultAgeTicks ? "adult" : "child";
            string first = $"Citizen #{c.Id} ({stage}): age {c.AgeTicks / _rules.TicksPerLifeYear:0}, {c.Task}, job {job}, health {c.Health:P0}";
            return Citizens.Selected.Count == 1 ? first : $"{Citizens.Selected.Count} selected. {first}";
        }
    }
}
