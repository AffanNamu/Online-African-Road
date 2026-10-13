using System.Collections.Generic;
using ARO.Input;
using ARO.NetCore;
using ARO.Vehicles;
using NUnit.Framework;
using UnityEngine;

namespace ARO.Tests
{
    /// <summary>
    /// The camera manager inside Unity: real GameObjects, a test vehicle with the placeholder's body boxes as child colliders, real physics queries. The choice rules and the
    /// numbers are covered by tests/NetCore.Tests (CameraModelTests); these tests prove they are applied: where each view puts the camera, that it blends,
    /// that obstacles pull it in but the vehicle's own colliders never do, that a vehicle change keeps or falls back the view, and that requests arrive through the input.
    /// What they cannot prove is how it looks or feels: that needs the running game (docs/CAMERA.md, "Manual validation").
    /// </summary>
    public class CameraManagerTests
    {
        readonly List<Object> _made = new List<Object>();
        GameObject _camGo; FollowCamera _fc; Camera _camera; VehicleController _veh;

        [SetUp] public void SetUp()
        {
            _camGo = Own(new GameObject("Main Camera", typeof(Camera))); _camGo.tag = "MainCamera";
            _camera = _camGo.GetComponent<Camera>(); _fc = _camGo.AddComponent<FollowCamera>();
            _veh = Spawn(VehicleCategory.Truck, new Vector3(0f, 1f, 0f));
        }

        [TearDown] public void TearDown() { foreach (var o in _made) if (o != null) Object.DestroyImmediate(o); _made.Clear(); }

        T Own<T>(T o) where T : Object { _made.Add(o); return o; }

        /// <summary>
        /// A vehicle with the same body boxes as the TruckFactory placeholder (the factory itself touches renderer.material, which logs an error in Edit Mode),
        /// real colliders as CHILDREN of the root (as on a real model) and a real VehicleController, so the camera meets what it meets in the game.
        /// </summary>
        VehicleController Spawn(VehicleCategory cat, Vector3 at)
        {
            var def = Own(ScriptableObject.CreateInstance<VehicleDefinition>()); def.id = "camtest_" + cat; def.displayName = "Camera test " + cat; def.category = cat;
            var root = Own(new GameObject("TestVehicle_" + cat)); root.transform.position = at;
            var rb = root.AddComponent<Rigidbody>(); rb.useGravity = false;
            bool bus = cat == VehicleCategory.Bus;
            Box(root, "Body", bus ? new Vector3(0f, 2.0f, -0.4f) : new Vector3(0f, 1.35f, -0.4f), bus ? new Vector3(2.5f, 2.6f, 10f) : new Vector3(2.4f, 1.3f, 6f));
            if (!bus) Box(root, "Cab", new Vector3(0f, 2.3f, 2.4f), new Vector3(2.3f, 1.6f, 1.8f));
            var v = root.AddComponent<VehicleController>(); v.definition = def; v.axles = new WheelAxle[0];
            Physics.SyncTransforms(); return v;
        }

        static void Box(GameObject root, string name, Vector3 centre, Vector3 size)
        {
            var g = new GameObject(name); g.transform.SetParent(root.transform, false); g.transform.localPosition = centre; g.AddComponent<BoxCollider>().size = size;
        }

        /// <summary>Bind the camera to the vehicle and run it long enough for any transition and smoothing to settle.</summary>
        void Settle(int frames = 90) { _fc.target = _veh.transform; for (int i = 0; i < frames; i++) _fc.Step(0.02f); }

        Vector3 Local(Vector3 world) => _veh.transform.InverseTransformPoint(world);

        // ------------------------------------------------------------------------------------------------ the eight views
        [Test] public void EachViewPlacesTheCameraWhereItBelongs()
        {
            Settle();
            Assert.AreEqual(CameraView.RearChase, _fc.View, "the camera starts in the chase view");
            var results = new Dictionary<int, CameraSelectResult>();
            for (int n = 1; n <= 8; n++)
            {
                results[n] = _fc.Request(CameraRequest.Direct(n), "test");
                if (n == 2) continue;   // the driver cabin is unavailable: covered separately
                for (int i = 0; i < 60; i++) _fc.Step(0.02f);
                var view = (CameraView)(n - 1); Assert.AreEqual(view, _fc.View, $"view {n}");
                var p = _camGo.transform.position; var local = Local(p); var origin = _veh.transform.position;
                switch (view)
                {
                    case CameraView.Cockpit: Assert.Less(Vector3.Distance(p, origin), 4.5f); Assert.Greater(p.y, origin.y + 1.5f); Assert.Greater(Vector3.Dot(_camGo.transform.forward, _veh.transform.forward), 0.95f); break;
                    case CameraView.RearChase: Assert.Less(local.z, -6f); Assert.Greater(local.y, 2f); break;
                    case CameraView.FarChase: Assert.Less(local.z, -15f); break;
                    case CameraView.Left: Assert.Less(local.x, -4f); break;
                    case CameraView.Right: Assert.Greater(local.x, 4f); break;
                    case CameraView.Overhead: Assert.Greater(p.y, origin.y + 15f); break;
                    case CameraView.FrontBumper: Assert.Greater(local.z, 2.5f); Assert.Less(local.y, 1.6f); Assert.Greater(Vector3.Dot(_camGo.transform.forward, _veh.transform.forward), 0.95f); break;
                }
                if (view != CameraView.Cockpit && view != CameraView.FrontBumper)
                    Assert.Greater(Vector3.Dot(_camGo.transform.forward, (origin + Vector3.up * 1.5f - p).normalized), 0.55f, $"{view} must look at the vehicle");
            }
            Assert.AreEqual(CameraSelectResult.Unavailable, results[2], "the driver cabin has no driver yet");
            foreach (int n in new[] { 1, 3, 4, 5, 6, 7, 8 }) Assert.AreEqual(CameraSelectResult.Changed, results[n], $"view {n} was selectable");
        }

        [Test] public void TheCameraManagerAddsNoCameraAndStaysTheMainCamera()
        {
            int before = Camera.allCamerasCount; Settle();
            Assert.AreEqual(before, Camera.allCamerasCount, "the manager must not add cameras");
            Assert.IsTrue(_camGo.CompareTag("MainCamera"), "nameplates, rain and the renderer all use Camera.main"); Assert.IsNotNull(Camera.main);
        }

        [Test] public void NextAndPreviousWalkTheViewsAndSkipTheUnavailableCabin()
        {
            Settle(); var seen = new List<CameraView> { _fc.View };
            for (int i = 0; i < 7; i++) { _fc.Next("test"); seen.Add(_fc.View); }
            CollectionAssert.AreEqual(new[] { CameraView.RearChase, CameraView.FarChase, CameraView.Left, CameraView.Right, CameraView.Overhead, CameraView.FrontBumper, CameraView.Cockpit, CameraView.RearChase }, seen);
            _fc.Previous("test"); Assert.AreEqual(CameraView.Cockpit, _fc.View); _fc.Previous("test"); Assert.AreEqual(CameraView.FrontBumper, _fc.View);
        }

        // ------------------------------------------------------------------------------------------------ the driver cabin
        [Test] public void TheDriverCabinIsRefusedExplicitlyAndTheViewStays()
        {
            Settle(); _fc.Select(CameraView.Left, "test"); string notice = null; _fc.Notice += m => notice = m;
            Assert.AreEqual(CameraSelectResult.Unavailable, _fc.Request(CameraRequest.Direct(2), "test"));
            Assert.AreEqual(CameraView.Left, _fc.View); Assert.IsNotNull(notice); StringAssert.Contains("driver", notice.ToLowerInvariant());
            Assert.IsFalse(_fc.Profile.IsAvailable(CameraView.DriverCabin));
        }

        [Test] public void AnIncompleteDriverRigStaysUnavailableAndSaysWhatIsMissing()
        {
            var rig = _veh.gameObject.AddComponent<VehicleRig>(); var driver = _veh.gameObject.AddComponent<DriverRig>(); rig.driverRig = driver;
            Settle(); Assert.IsFalse(driver.IsComplete); Assert.AreEqual("a cabin camera anchor", driver.FirstMissing);
            string notice = null; _fc.Notice += m => notice = m;
            Assert.AreEqual(CameraSelectResult.Unavailable, _fc.Select(CameraView.DriverCabin, "test")); StringAssert.Contains("cabin camera anchor", notice);
            driver.cabinCameraAnchor = _veh.transform; Assert.AreEqual("a driver model", driver.FirstMissing);
            driver.driverModel = _veh.transform; Assert.AreEqual("a steering wheel", driver.FirstMissing);
            driver.steeringWheel = _veh.transform; Assert.AreEqual("a steering animation", driver.FirstMissing);
        }

        [Test] public void ACompleteDriverRigMakesTheCabinViewAvailableAtItsAnchor()
        {
            var anchor = Own(new GameObject("CabinAnchor")); anchor.transform.SetParent(_veh.transform, false); anchor.transform.localPosition = new Vector3(0.9f, 2.7f, 1.2f);
            var rig = _veh.gameObject.AddComponent<VehicleRig>(); var driver = _veh.gameObject.AddComponent<DriverRig>(); rig.driverRig = driver;
            driver.cabinCameraAnchor = anchor.transform; driver.driverModel = _veh.transform; driver.steeringWheel = _veh.transform;
            var animator = _veh.gameObject.AddComponent<Animator>(); animator.runtimeAnimatorController = new UnityEditor.Animations.AnimatorController(); driver.animator = animator;
            Assert.IsTrue(driver.IsComplete);
            Settle(); Assert.IsTrue(_fc.Profile.IsAvailable(CameraView.DriverCabin));
            Assert.AreEqual(CameraSelectResult.Changed, _fc.Select(CameraView.DriverCabin, "test"));
            for (int i = 0; i < 60; i++) _fc.Step(0.02f);
            Assert.Less(Vector3.Distance(_camGo.transform.position, anchor.transform.position), 0.01f, "the camera sits on the rig's anchor");
        }

        [Test] public void RigAnchorsReplaceThePlaceholderPositions()
        {
            var anchor = Own(new GameObject("CockpitAnchor")); anchor.transform.SetParent(_veh.transform, false); anchor.transform.localPosition = new Vector3(-0.4f, 3.1f, 1.7f);
            _veh.gameObject.AddComponent<VehicleRig>().cockpitCameraAnchor = anchor.transform;
            Settle(); _fc.Select(CameraView.Cockpit, "test"); for (int i = 0; i < 60; i++) _fc.Step(0.02f);
            Assert.Less(Vector3.Distance(_camGo.transform.position, anchor.transform.position), 0.01f);
        }

        // ------------------------------------------------------------------------------------------------ obstacles
        [Test] public void NothingInTheWayLeavesTheChaseAtItsFullDistanceAndTheVehicleItselfNeverPullsItIn()
        {
            Settle(); var pivot = _veh.transform.position + Vector3.up * 2.2f;
            Assert.AreEqual(Mathf.Sqrt(2.4f * 2.4f + 13f * 13f), Vector3.Distance(_camGo.transform.position, pivot), 0.5f);   // from (0, 2.2, 0) to (0, 4.6, -13)
        }

        [Test] public void AWallBehindPullsTheChaseInButNotIntoTheVehicle()
        {
            var wall = Own(GameObject.CreatePrimitive(PrimitiveType.Cube)); wall.transform.position = new Vector3(0f, 3f, -9f); wall.transform.localScale = new Vector3(40f, 20f, 1f);
            Physics.SyncTransforms(); Settle(200);
            float d = Vector3.Distance(_camGo.transform.position, _veh.transform.position + Vector3.up * 2.2f);
            Assert.Less(d, 9.2f, "pulled in towards the wall"); Assert.GreaterOrEqual(d, 6.9f, "never closer than the floor that keeps it out of the vehicle");
            Assert.Greater(_camGo.transform.position.z, -9.05f, "in front of the wall's surface, not inside it");
        }

        [Test] public void AWallFarBehindChangesNothing()
        {
            var wall = Own(GameObject.CreatePrimitive(PrimitiveType.Cube)); wall.transform.position = new Vector3(0f, 3f, -40f); wall.transform.localScale = new Vector3(40f, 20f, 1f);
            Physics.SyncTransforms(); Settle(120);
            Assert.AreEqual(Mathf.Sqrt(2.4f * 2.4f + 13f * 13f), Vector3.Distance(_camGo.transform.position, _veh.transform.position + Vector3.up * 2.2f), 0.5f);
        }

        [Test] public void ARemovedObstacleLetsTheCameraBackOut()
        {
            var wall = Own(GameObject.CreatePrimitive(PrimitiveType.Cube)); wall.transform.position = new Vector3(0f, 3f, -9f); wall.transform.localScale = new Vector3(40f, 20f, 1f);
            Physics.SyncTransforms(); Settle(150); float near = Vector3.Distance(_camGo.transform.position, _veh.transform.position);
            Object.DestroyImmediate(wall); Physics.SyncTransforms(); for (int i = 0; i < 400; i++) _fc.Step(0.02f);
            Assert.Greater(Vector3.Distance(_camGo.transform.position, _veh.transform.position), near + 3f);
        }

        // ------------------------------------------------------------------------------------------------ transitions
        [Test] public void ChangingViewBlendsInsteadOfSnappingAndEnds()
        {
            Settle(); var from = _camGo.transform.position;
            _fc.Select(CameraView.Overhead, "test"); Assert.IsTrue(_fc.IsBlending);
            _fc.Step(0.02f); Assert.Less(Vector3.Distance(_camGo.transform.position, from), 2f, "the first frame is still near where the camera was");
            for (int i = 0; i < 60; i++) _fc.Step(0.02f);
            Assert.IsFalse(_fc.IsBlending); Assert.Greater(_camGo.transform.position.y, _veh.transform.position.y + 15f);
        }

        [Test] public void ARefusedChangeDoesNotStartATransition() { Settle(); _fc.Request(CameraRequest.Direct(2), "test"); Assert.IsFalse(_fc.IsBlending); }

        // ------------------------------------------------------------------------------------------------ vehicle changes
        [Test] public void AnotherVehicleKeepsTheChosenView()
        {
            Settle(); _fc.Select(CameraView.Overhead, "test");
            var bus = Spawn(VehicleCategory.Bus, new Vector3(100f, 1f, 0f)); _fc.target = bus.transform; _fc.Step(0.02f);
            Assert.AreEqual(CameraView.Overhead, _fc.View); Assert.AreEqual("bus", _fc.Profile.Category); Assert.IsFalse(_fc.IsBlending, "a new vehicle is not blended from the old one's position");
            _fc.Step(0.02f); Assert.Less(Vector3.Distance(_camGo.transform.position, bus.transform.position), 45f);
        }

        [Test] public void ADifferentCategoryUsesItsOwnFraming()
        {
            Settle(); float truckZ = Local(_camGo.transform.position).z;
            var bus = Spawn(VehicleCategory.Bus, new Vector3(100f, 1f, 0f)); _fc.target = bus.transform; for (int i = 0; i < 90; i++) _fc.Step(0.02f);
            Assert.Less(bus.transform.InverseTransformPoint(_camGo.transform.position).z, truckZ - 1f, "a bus is framed further back than a truck");
        }

        [Test] public void PerModelFramingScalesTheFollowingViews()
        {
            _veh.definition.cameraFollowScale = 1.5f; Settle();
            Assert.Less(Local(_camGo.transform.position).z, -13f * 1.5f * 0.9f);
        }

        // ------------------------------------------------------------------------------------------------ fixed views carry no chase effects
        [Test] public void FixedViewsStayOnTheVehicleWhileItMoves()
        {
            Settle(); _fc.Select(CameraView.Cockpit, "test"); for (int i = 0; i < 60; i++) _fc.Step(0.02f);
            var rb = _veh.GetComponent<Rigidbody>(); rb.useGravity = false; rb.linearVelocity = _veh.transform.forward * 20f;
            var expected = _veh.transform.TransformPoint(new Vector3(-0.5f, 2.5f, 2.6f));
            for (int i = 0; i < 20; i++) { _fc.Step(0.02f); Assert.Less(Vector3.Distance(_camGo.transform.position, expected), 0.001f, "no shake, no lag, no pitch in the cockpit"); }
        }

        [Test] public void AStationaryVehicleDoesNotMakeTheCameraDrift()
        {
            Settle(); var p = _camGo.transform.position; var r = _camGo.transform.rotation;
            for (int i = 0; i < 200; i++) _fc.Step(0.02f);
            Assert.Less(Vector3.Distance(_camGo.transform.position, p), 0.02f); Assert.Less(Quaternion.Angle(_camGo.transform.rotation, r), 0.2f);
        }

        // ------------------------------------------------------------------------------------------------ requests arrive through the vehicle's input
        sealed class OneShotInput : IVehicleInput
        {
            public VehicleInputState Next; public VehicleInputState Read() { var s = Next; Next = default; return s; }
        }

        [Test] public void KeyGamepadAndTouchRequestsAllReachTheCameraThroughTheVehicle()
        {
            var input = new OneShotInput(); _veh.SetInput(input);
            var got = new List<CameraRequest>(); _veh.CameraRequested += r => got.Add(r);
            input.Next = new VehicleInputState { CameraToggled = true }; _veh.SendMessage("Update");
            input.Next = new VehicleInputState { CameraPrevious = true }; _veh.SendMessage("Update");
            input.Next = new VehicleInputState { CameraSelect = 8 }; _veh.SendMessage("Update");
            input.Next = default; _veh.SendMessage("Update");
            Assert.AreEqual(3, got.Count, "an idle frame raises nothing");
            Assert.AreEqual(1, got[0].Step); Assert.AreEqual(-1, got[1].Step); Assert.AreEqual(8, got[2].Select);
        }

        [Test] public void TheTouchInputRaisesTheSameEdges()
        {
            var go = Own(new GameObject("touch")); var t = go.AddComponent<TouchVehicleInput>();
            t.PressCamera(); Assert.IsTrue(t.Read().CameraToggled); Assert.IsFalse(t.Read().CameraToggled, "an edge lasts one frame");
            t.PressCameraPrevious(); Assert.IsTrue(t.Read().CameraPrevious); t.PressCameraSelect(5); Assert.AreEqual(5, t.Read().CameraSelect); Assert.AreEqual(0, t.Read().CameraSelect);
        }

        [Test] public void NoCameraKeyIsAlsoADrivingKey()
        {
            var cams = new List<UnityEngine.InputSystem.Key> { CameraBindings.Next, CameraBindings.PreviousModifierLeft, CameraBindings.PreviousModifierRight };
            cams.AddRange(CameraBindings.Direct);
            var driving = new[] { UnityEngine.InputSystem.Key.W, UnityEngine.InputSystem.Key.A, UnityEngine.InputSystem.Key.S, UnityEngine.InputSystem.Key.D, UnityEngine.InputSystem.Key.Space,
                UnityEngine.InputSystem.Key.H, UnityEngine.InputSystem.Key.L, UnityEngine.InputSystem.Key.Q, UnityEngine.InputSystem.Key.E, UnityEngine.InputSystem.Key.Escape,
                UnityEngine.InputSystem.Key.UpArrow, UnityEngine.InputSystem.Key.DownArrow, UnityEngine.InputSystem.Key.LeftArrow, UnityEngine.InputSystem.Key.RightArrow };
            foreach (var k in cams) CollectionAssert.DoesNotContain(driving, k);
            CollectionAssert.AllItemsAreUnique(CameraBindings.Direct); Assert.AreEqual(8, CameraBindings.Direct.Length);
        }
    }
}
