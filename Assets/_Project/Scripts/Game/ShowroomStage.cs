using System;
using System.Collections.Generic;
using ARO.Multiplayer;
using ARO.NetCore;
using ARO.Vehicles;
using ARO.World;
using UnityEngine;

namespace ARO.Game
{
    public enum ShowroomState { Idle, Loading, Ready, Failed }

    /// <summary>
    /// The Garage's 3D showroom: a small workshop built far below the world (so nothing else can see it), a camera that renders it into a RenderTexture, and the
    /// vehicle model loaded into it. The camera is the reusable inspection camera (<see cref="OrbitCameraModel"/>: orbit, zoom, pan, presets, smoothing, limits),
    /// rendered ON DEMAND (only while it moves or something changes), so an idle Garage costs no GPU time on WebGL.
    /// The vehicle comes from ShowroomFactory: the production prefab in Resources/Vehicles/{id} when it exists, otherwise a marked placeholder; the Garage
    /// treats both the same. While the Garage is open the world's sun and ambient are suspended and restored on exit.
    /// </summary>
    public sealed class ShowroomStage : MonoBehaviour
    {
        public const int Layer = 30;
        static readonly Vector3 Origin = new Vector3(0f, -4000f, 0f);

        public ShowroomState State { get; private set; } = ShowroomState.Idle;
        public string Error { get; private set; }
        public string DefinitionId { get; private set; }
        public RenderTexture Output { get; private set; }
        public ShowroomModel Model { get; private set; }
        public OrbitCameraModel Orbit { get; private set; }
        public string ViewName { get; private set; } = "default";
        public ShowroomPartInfo Highlighted { get; private set; }
        public bool LightsOn { get; private set; }

        Camera _cam; Transform _env, _host; Light _key; readonly List<Light> _lights = new List<Light>();
        bool _dirty, _entered, _moved, _interior; Material _template;
        // paint state (applied through property blocks, so the shared materials are never touched)
        Color? _base, _accent; float _metallic = 0.35f, _smooth = 0.6f;
        MaterialPropertyBlock _mpb; readonly Dictionary<Renderer, int> _kind = new Dictionary<Renderer, int>();       // 1 paint, 2 accent
        // restored on exit
        Light _sun; bool _sunWas, _fogWas; UnityEngine.Rendering.AmbientMode _ambMode; Color _amb, _ambSky, _ambEq, _ambGround; int _lodForced = -2;
        float _interiorFov = 62f, _outsideFov = 32f;

        public static ShowroomStage Create(int width, int height)
        {
            var go = new GameObject("ShowroomStage"); var s = go.AddComponent<ShowroomStage>(); s.Build(width, height); return s;
        }

        void Build(int w, int h)
        {
            _mpb = new MaterialPropertyBlock();
            _template = NetVisualContext.BodyMaterial;
            Output = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32) { name = "ShowroomRT", antiAliasing = 1, useMipMap = false };
            _host = new GameObject("ModelHost").transform; _host.SetParent(transform, false); _host.position = Origin;
            _env = new GameObject("Workshop").transform; _env.SetParent(transform, false); _env.position = Origin;
            var camGo = new GameObject("ShowroomCamera"); camGo.transform.SetParent(transform, false);
            _cam = camGo.AddComponent<Camera>(); _cam.targetTexture = Output; _cam.enabled = false; _cam.clearFlags = CameraClearFlags.SolidColor; _cam.backgroundColor = new Color(0.025f, 0.022f, 0.022f);
            _cam.cullingMask = 1 << Layer; _cam.fieldOfView = _outsideFov; _cam.nearClipPlane = 0.1f; _cam.farClipPlane = 90f; _cam.allowHDR = false; _cam.allowMSAA = false;
            BuildWorkshop();
            gameObject.SetActive(true);
        }

        // ------------------------------------------------------------------------------------------------------------------ the workshop
        Material Mat(Color c, float smooth, float metal, Color? emission = null, Texture tex = null)
        {
            var m = _template != null ? new Material(_template) : new Material(Shader.Find("Universal Render Pipeline/Lit"));
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c); else m.color = c;
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", smooth);
            if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", metal);
            if (tex != null && m.HasProperty("_BaseMap")) m.SetTexture("_BaseMap", tex);
            if (emission.HasValue) { m.EnableKeyword("_EMISSION"); if (m.HasProperty("_EmissionColor")) m.SetColor("_EmissionColor", emission.Value); if (tex != null && m.HasProperty("_EmissionMap")) m.SetTexture("_EmissionMap", tex); }
            return m;
        }
        GameObject Prim(PrimitiveType t, string name, Vector3 pos, Vector3 scale, Material mat, Vector3? euler = null)
        {
            var g = GameObject.CreatePrimitive(t); g.name = name; Destroy(g.GetComponent<Collider>()); g.transform.SetParent(_env, false); g.transform.localPosition = pos; g.transform.localScale = scale;
            if (euler.HasValue) g.transform.localEulerAngles = euler.Value; g.GetComponent<Renderer>().sharedMaterial = mat; g.layer = Layer; return g;
        }

        void BuildWorkshop()
        {
            var floor = Mat(new Color(0.045f, 0.045f, 0.05f), 0.78f, 0.15f); var disc = Mat(new Color(0.085f, 0.085f, 0.095f), 0.86f, 0.25f);
            var wall = Mat(new Color(0.07f, 0.06f, 0.055f), 0.25f, 0.1f); var lamp = Mat(new Color(1, 0.9f, 0.7f), 0.2f, 0f, new Color(3.2f, 2.6f, 1.8f));
            var bar = Mat(new Color(1, 0.8f, 0.4f), 0.2f, 0f, new Color(2.2f, 1.4f, 0.6f)); var gold = Mat(new Color(0.9f, 0.62f, 0.12f), 0.4f, 0.8f);
            Prim(PrimitiveType.Cylinder, "Floor", new Vector3(0, -0.06f, 0), new Vector3(60f, 0.05f, 60f), floor);
            Prim(PrimitiveType.Cylinder, "Turntable", new Vector3(0, 0.0f, 0), new Vector3(16f, 0.04f, 16f), disc);
            Prim(PrimitiveType.Cylinder, "TurntableRim", new Vector3(0, 0.01f, 0), new Vector3(16.6f, 0.02f, 16.6f), gold);
            Prim(PrimitiveType.Cube, "BackWall", new Vector3(0, 7, -17), new Vector3(60, 14, 0.5f), wall);
            Prim(PrimitiveType.Cube, "LeftWall", new Vector3(-24, 7, 0), new Vector3(0.5f, 14, 60), wall); Prim(PrimitiveType.Cube, "RightWall", new Vector3(24, 7, 0), new Vector3(0.5f, 14, 60), wall);
            Prim(PrimitiveType.Cube, "Ceiling", new Vector3(0, 14, 0), new Vector3(60, 0.5f, 60), wall);
            foreach (float x in new[] { -14f, -7f, 0f, 7f, 14f }) Prim(PrimitiveType.Cube, "WallLamp", new Vector3(x, 6.5f, -16.6f), new Vector3(0.35f, 8f, 0.12f), bar);
            foreach (float z in new[] { -8f, 0f, 8f }) Prim(PrimitiveType.Cube, "CeilingLamp", new Vector3(0, 11.5f, z), new Vector3(10f, 0.2f, 1.1f), lamp);
            var logo = Brand.Texture("logo_mark");
            if (logo != null) { var q = Prim(PrimitiveType.Quad, "AroLogo", new Vector3(0, 8.2f, -16.55f), new Vector3(4.6f, 4.6f, 1f), Mat(Color.white, 0.2f, 0f, new Color(1.2f, 1.1f, 0.9f), logo), new Vector3(0, 180f, 0)); }

            // lights: one directional key light with real shadows (the world's sun is suspended while this room is open) + a rim, a fill and a warm floor bounce
            _key = NewLight("Key", LightType.Directional, new Vector3(0, 12, 0), new Color(1f, 0.93f, 0.82f), 1.25f); _key.transform.rotation = Quaternion.Euler(52f, -38f, 0f); _key.shadows = LightShadows.Soft; _key.shadowStrength = 0.85f;
            var rim = NewLight("Rim", LightType.Spot, new Vector3(-10, 8, -11), new Color(0.75f, 0.85f, 1f), 160f); rim.spotAngle = 75f; rim.range = 45f; rim.transform.LookAt(Origin + new Vector3(0, 1.8f, 0));
            var fill = NewLight("Fill", LightType.Point, new Vector3(9, 4, 10), new Color(1f, 0.85f, 0.65f), 70f); fill.range = 28f;
            var bounce = NewLight("Bounce", LightType.Point, new Vector3(0, 0.6f, 6), new Color(1f, 0.7f, 0.3f), 14f); bounce.range = 12f;
            SetRoomActive(false);
        }

        Light NewLight(string name, LightType type, Vector3 localPos, Color c, float intensity)
        {
            var g = new GameObject(name); g.transform.SetParent(_env, false); g.transform.localPosition = localPos; g.layer = Layer;
            var l = g.AddComponent<Light>(); l.type = type; l.color = c; l.intensity = intensity; l.shadows = LightShadows.None; _lights.Add(l); return l;
        }
        void SetRoomActive(bool on) { foreach (var l in _lights) l.enabled = on; }

        // ------------------------------------------------------------------------------------------------------------------ entering and leaving
        /// <summary>The Garage page opened: the room becomes the only lit thing (the world's sun is switched off until Exit).</summary>
        public void Enter()
        {
            if (_entered) return; _entered = true;
            _sun = RenderSettings.sun; _sunWas = _sun != null && _sun.enabled; if (_sun != null) _sun.enabled = false; TimeOfDay.Suspended = true;
            _ambMode = RenderSettings.ambientMode; _amb = RenderSettings.ambientLight; _ambSky = RenderSettings.ambientSkyColor; _ambEq = RenderSettings.ambientEquatorColor; _ambGround = RenderSettings.ambientGroundColor; _fogWas = RenderSettings.fog;
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat; RenderSettings.ambientLight = new Color(0.20f, 0.19f, 0.19f); RenderSettings.fog = false;
            SetRoomActive(true); _dirty = true;
        }
        public void Exit()
        {
            if (!_entered) return; _entered = false;
            SetRoomActive(false); RenderSettings.ambientMode = _ambMode; RenderSettings.ambientLight = _amb; RenderSettings.ambientSkyColor = _ambSky; RenderSettings.ambientEquatorColor = _ambEq; RenderSettings.ambientGroundColor = _ambGround; RenderSettings.fog = _fogWas;
            if (_sun != null) _sun.enabled = _sunWas; TimeOfDay.Suspended = false;
        }
        void OnDestroy() { Exit(); if (Output != null) { Output.Release(); Destroy(Output); } }

        // ------------------------------------------------------------------------------------------------------------------ the vehicle
        public void Unload()
        {
            if (Model != null) { Destroy(Model.gameObject); Model = null; }
            _kind.Clear(); Highlighted = null; _interior = false; State = ShowroomState.Idle; _lodForced = -2;
        }

        /// <summary>Builds the model of a vehicle definition into the room. Returns false (and says why in Error) when it cannot.</summary>
        public bool Load(string definitionId)
        {
            State = ShowroomState.Loading; Error = null; DefinitionId = definitionId; Unload(); State = ShowroomState.Loading;
            VehicleDefinition def; if (!NetVisualContext.Definitions.TryGetValue(definitionId ?? "", out def) || def == null) { State = ShowroomState.Failed; Error = "This vehicle has no model definition in the game yet."; Debug.Log($"[Showroom] failed vehicle={definitionId} error={Error}"); return false; }
            try { Model = ShowroomFactory.Create(def, NetVisualContext.BodyMaterial, NetVisualContext.WheelMaterial, _host); }
            catch (Exception e) { Model = null; Error = "The model could not be built: " + e.Message; }
            if (Model == null) { State = ShowroomState.Failed; Error = Error ?? "The model could not be built."; Debug.Log($"[Showroom] failed vehicle={definitionId} error={Error}"); return false; }
            Model.transform.localPosition = Vector3.zero; Model.transform.localRotation = Quaternion.identity; SetLayer(Model.gameObject, Layer); Model.ComputeBounds();
            foreach (var r in Model.paint) _kind[r] = 1; foreach (var r in Model.accent) _kind[r] = 2;
            var b = Model.localBounds; Orbit = new OrbitCameraModel(ShowroomFraming.LimitsFor(b.size.x, b.size.y, b.size.z), ShowroomFraming.BaseDistance(b.size.x, b.size.y, b.size.z)) { CenterX = b.center.x, CenterY = b.center.y, CenterZ = b.center.z };
            Orbit.Reset(true); ViewName = "default"; _interior = false; _cam.fieldOfView = _outsideFov; ApplyLooks(); State = ShowroomState.Ready; _dirty = true;
            Debug.Log($"[Showroom] ready vehicle={definitionId} model={(Model.isPlaceholder ? "placeholder" : "production")} lod={Model.LodLevels} size={b.size.x:0.0}x{b.size.y:0.0}x{b.size.z:0.0} parts={Model.parts.Count} interior={Model.HasInterior} rt={Output.width}x{Output.height}");
            Render(); return true;
        }
        static void SetLayer(GameObject g, int l) { foreach (var t in g.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = l; }

        // ------------------------------------------------------------------------------------------------------------------ camera
        public void SetView(ViewPreset p)
        {
            if (State != ShowroomState.Ready) return;
            if (p == ViewPreset.Interior)
            {
                if (!Model.HasInterior) return;
                _interior = true; ViewName = "interior"; _dirty = true; return;
            }
            _interior = false; _cam.fieldOfView = _outsideFov;
            var w = Model.wheelPoints.Length > 0 ? Model.wheelPoints[Math.Min(1, Model.wheelPoints.Length - 1)] : Vector3.zero;
            Orbit.SetView(p, w.x, w.y, w.z); ViewName = p.ToString().ToLowerInvariant(); _moved = true;
        }
        public bool InteriorView => _interior;
        public void ResetCamera() { if (State != ShowroomState.Ready) return; _interior = false; _cam.fieldOfView = _outsideFov; Orbit.Reset(); ViewName = "default"; _moved = true; }
        public void OrbitBy(float dx, float dy) { if (State == ShowroomState.Ready && !_interior) { Orbit.Orbit(dx, dy); ViewName = "free"; _moved = true; } }
        public void ZoomBy(float amount) { if (State == ShowroomState.Ready && !_interior) { Orbit.Zoom(amount); ViewName = "free"; _moved = true; } }
        public void PanBy(float dx, float dy) { if (State == ShowroomState.Ready && !_interior) { Orbit.Pan(dx, dy); ViewName = "free"; _moved = true; } }
        public void SetTurntable(bool on) { if (State == ShowroomState.Ready) { Orbit.AutoRotate = on; ViewName = on ? "turntable" : ViewName; _moved = true; } }

        /// <summary>Called every frame by the Garage while it is open.</summary>
        public void Tick(float dt)
        {
            if (!_entered || State != ShowroomState.Ready) return;
            if (_interior) { if (_dirty) { PoseInterior(); Render(); _dirty = false; } return; }
            Orbit.Step(dt);
            if (!Orbit.Settled || _dirty)
            {
                int lod = Orbit.Distance > Orbit.HomeDistance * 1.35f ? 1 : 0; if (lod != _lodForced && Model.LodLevels > 1) { Model.ForceLod(Mathf.Min(lod, Model.LodLevels - 1)); _lodForced = lod; }
                PoseOutside(); Render(); _dirty = false;
            }
            else if (_moved) { _moved = false; Debug.Log($"[Showroom] camera view={ViewName} yaw={Orbit.Yaw:0} pitch={Orbit.Pitch:0} distance={Orbit.Distance:0.0} min={Orbit.Limits.MinDistance:0.0} max={Orbit.Limits.MaxDistance:0.0}"); }
        }

        void PoseOutside()
        {
            Orbit.Pose(out float px, out float py, out float pz, out float lx, out float ly, out float lz);
            _cam.transform.position = _host.position + new Vector3(px, py, pz); _cam.transform.LookAt(_host.position + new Vector3(lx, ly, lz));
        }
        void PoseInterior()
        {
            var a = Model.interiorAnchor; if (a == null) { PoseOutside(); return; }
            _cam.fieldOfView = _interiorFov; _cam.transform.SetPositionAndRotation(a.position, a.rotation);
        }
        void Render() { if (_cam != null && _entered) _cam.Render(); }
        public void MarkDirty() { _dirty = true; }

        // ------------------------------------------------------------------------------------------------------------------ looks: paint, lights, highlight
        public void SetSkin(string baseHex, string accentHex, double metallic, double roughness)
        {
            _base = GarageRules.TryParseHex(baseHex, out float r, out float g, out float b) ? new Color(r, g, b) : (Color?)null;
            _accent = GarageRules.TryParseHex(accentHex, out r, out g, out b) ? new Color(r, g, b) : (Color?)null;
            _metallic = (float)metallic; _smooth = 1f - (float)roughness; ApplyLooks(); _dirty = true;
        }
        public void SetLights(bool on) { LightsOn = on; ApplyLooks(); _dirty = true; }
        public void Highlight(ShowroomPartInfo p) { Highlighted = p; ApplyLooks(); _dirty = true; }

        void ApplyLooks()
        {
            if (Model == null) return;
            foreach (var r in Model.GetComponentsInChildren<Renderer>()) { if (r != null) Look(r); }
        }
        void Look(Renderer r)
        {
            bool hl = Highlighted != null && Highlighted.renderers.Contains(r); bool head = Model.headEmitters.Contains(r), tail = Model.tailEmitters.Contains(r);
            _kind.TryGetValue(r, out int kind); bool custom = (kind == 1 && _base.HasValue) || (kind == 2 && _accent.HasValue) || head || tail || hl;
            if (!custom) { r.SetPropertyBlock(null); return; }
            r.GetPropertyBlock(_mpb); _mpb.Clear();
            Color baseCol = r.sharedMaterial != null && r.sharedMaterial.HasProperty("_BaseColor") ? r.sharedMaterial.GetColor("_BaseColor") : Color.white;
            if (kind == 1 && _base.HasValue) { baseCol = _base.Value; _mpb.SetFloat("_Metallic", _metallic); _mpb.SetFloat("_Smoothness", _smooth); }
            else if (kind == 2 && _accent.HasValue) { baseCol = _accent.Value; _mpb.SetFloat("_Metallic", Mathf.Clamp01(_metallic + 0.1f)); _mpb.SetFloat("_Smoothness", _smooth); }
            if (hl) baseCol = Color.Lerp(baseCol, new Color(1f, 0.82f, 0.35f), 0.35f);
            _mpb.SetColor("_BaseColor", baseCol); _mpb.SetColor("_Color", baseCol);
            if (head) _mpb.SetColor("_EmissionColor", LightsOn ? new Color(3.2f, 2.9f, 2.2f) : Color.black);
            if (tail) _mpb.SetColor("_EmissionColor", LightsOn ? new Color(1.6f, 0.05f, 0.05f) : Color.black);
            r.SetPropertyBlock(_mpb);
        }

        // ------------------------------------------------------------------------------------------------------------------ picking
        /// <summary>The part under a point of the picture (0..1 from the bottom-left of the viewer), or null.</summary>
        public ShowroomPartInfo Pick(Vector2 viewport)
        {
            if (State != ShowroomState.Ready || Model == null || _interior) return null;
            var ray = _cam.ViewportPointToRay(new Vector3(viewport.x, viewport.y, 0f)); ShowroomPartInfo best = null; float bestD = float.MaxValue;
            foreach (var p in Model.parts)
                foreach (var r in p.renderers)
                    if (r != null && r.bounds.IntersectRay(ray, out float d) && d < bestD) { bestD = d; best = p; }
            return best;
        }
    }
}
