# Driving camera (milestone 1)

One component, `ARO.Vehicles.FollowCamera`, on the scene's Main Camera, follows the player's vehicle in one of **eight views**. It is the same Main Camera as before, so
the minimap, rain (`WeatherSystem`), nameplates (`NetworkVehicle`) and the rendering rig (`RenderingRig`) are untouched. It is purely local presentation: no camera
state is part of the vehicle state, nothing is sent to other players, and other players' puppets never inherit it (a test fails if `CameraView` appears in the
network code).

## The eight views

| # | View | Kind | What it does | Status |
|---|---|---|---|---|
| 1 | Cockpit | rigid | Driver's eye position, looking forward. Fixed to the vehicle: no lag, no shake, no obstruction test | **Limited**: no dashboard, steering wheel, mirrors, interior or windscreen exist (P-05, P-93) |
| 2 | Driver cabin | rigid | The driver at the wheel | **Unavailable**: no driver model, rig or steering animation exists (P-92). Selecting it shows a message and logs `[Camera] unavailable`; next / previous skip it |
| 3 | Chase | following | The original behaviour: behind and above, damped, speed FOV, brake/accelerate pitch, road shake (Camera Shake setting), mouse wheel 8 to 24 m | Working |
| 4 | Far chase | following | Wider and further behind, slower, narrower FOV; mouse wheel 14 to 45 m; no shake or pitch | Working |
| 5 | Left | following | From the left of the vehicle, turning with it | Working |
| 6 | Right | following | From the right | Working |
| 7 | Overhead | following | 30 m above, looking down, rotating with the vehicle's heading | Working |
| 8 | Front bumper | rigid | Low at the front, looking forward | Working |

*Rigid* views sit on the vehicle (a rig anchor when the model has one, else a per-category position). *Following* views live in "yaw space" (the vehicle's position and
heading only, so bumps do not tilt the view), are damped, and are pulled in by obstacles.

### Behaviour
* **Switching** blends from the camera's current pose over 0.45 s (smooth-step). A new vehicle snaps, it does not blend from the old vehicle's position.
* **Obstacles**: following views sweep a sphere from the vehicle's focus point to the wanted camera position. The first thing that is not the player's own vehicle (the check is
  `collider.transform.IsChildOf(vehicle)`) pulls the camera in just before it. The camera pulls in fast and relaxes out slowly, and never goes closer than a per-view floor
  (7 m chase, 9 m far chase, 3 to 4 m side views, 8 m overhead) so it cannot be dragged into the vehicle. Triggers are ignored. A test places the camera at the floor distance for
  every view and category and checks it is outside the placeholder vehicle's bounds.
* **Chase effects** (pitch with acceleration, road-roughness shake scaled by Camera Shake) apply to the chase view only. They are not applied to any rigid view or to the other
  following views. A stationary vehicle never drifts (shake is proportional to speed).
* **Frame time** is clamped to 0.1 s everywhere, so a stall cannot fling the camera; damping is frame-rate independent.
* **The selected view is kept** across jobs, free drive and respawns (the component lives on the Main Camera). When the vehicle changes, the view is kept if the new vehicle
  offers it, else it falls back to the chase view.

### Known limits
* A wall closer than a view's floor distance leaves the camera inside the wall (back faces are not drawn, so you see through it). The floor is the price of never entering the vehicle.
* Another vehicle (traffic) between you and the camera pulls the camera in; a close tailgater can make the chase view shorten and relax repeatedly.
* The offsets are tuned by geometry for the placeholder bodies, not by looking at them (P-94). Production models set `VehicleDefinition.cameraFollowScale` / `cameraHeightScale`
  and the rig anchors.

## Controls

| Action | Keyboard | Gamepad | Touch / mouse |
|---|---|---|---|
| Next available view | `C` | `Y` (north) | Camera button |
| Previous available view | `Shift` + `C` | not bound | not bound |
| View by number | `1` to `8` | not bound | not bound |
| Chase distance | mouse wheel (chase and far chase) | not bound | not bound |

The camera button (`Btn_Camera`) is in the top right of the HUD, **left of** the minimap (150 x 76 on the 1920 x 1080 canvas, 18 px clear of the minimap column), and shows the
current view. It is an ordinary uGUI `Button`: a finger, a mouse and the EventSystem's pointer all work, and nothing in the HUD requires a mouse. `TouchVehicleInput` has the
same camera edges (`PressCamera`, `PressCameraPrevious`, `PressCameraSelect`) for an on-screen control layer, which does not exist yet (milestone B).

Requests travel one path: the key / pad / touch edge is read by the vehicle's input (`VehicleInputState.CameraToggled / CameraPrevious / CameraSelect`), the vehicle raises
`CameraRequested`, `DrivingSession` forwards it to `FollowCamera.Request`. Requests are ignored while the game is paused behind a menu, so typing digits into a menu field never
changes the camera. (The direct `C` read in `DrivingSession` was removed; it was the second, separate key reader.)

### Key map, current and planned (why the number keys are safe)
| Used now | Keys |
|---|---|
| Driving | `W A S D`, arrow keys, `Space` (handbrake), `H` (horn), `L` (lights), `Q` / `E` (indicators) |
| Menu | `Escape` |
| Camera | `C`, `Shift`, `1` to `8` |

No current control uses a digit. The planned driving controls (gear selection, high beams, hazard lights, wipers, pause, settings) must **not** use `C`, `Shift+C` or the digits
`1` to `8`; suggested keys when they are built: `R` / `F` (gear up / down), `K` (high beams), `T` (hazards), `V` (wipers). `NoCameraKeyIsAlsoAFixedDrivingKey` (xunit and EditMode) fails if a
camera key is also one of the fixed driving keys.

**Rebinding**: the camera keys are declared in one place, `ARO.Input.CameraBindings` (`Next`, the two `Shift` keys, `Direct[0..7]`), and read through it
(`kb[CameraBindings.Next]`). A future controls screen assigns those fields and calls `ResetDefaults()`; the input code does not change. There is no rebinding UI yet, and the Controls
tab in Settings shows the defaults read-only. The other (driving) keys are still fixed `kb.wKey`-style reads.

## Configuration
* **Per category**: `ARO.NetCore.CameraProfiles.Build("truck" | "bus" | "van", ...)` holds the eight configurations (offsets, look target, FOV, lag, obstruction radius and floor, zoom
  range). Buses sit further back and further to the side; vans closer. Unknown categories get the truck set.
* **Per model**: `VehicleDefinition.cameraFollowScale` and `cameraHeightScale` (0.75 to 2) scale the following views.
* **Per prefab**: `VehicleRig.cockpitCameraAnchor`, `VehicleRig.bumperCameraAnchor` and `VehicleRig.driverRig.cabinCameraAnchor` replace the placeholder positions of the rigid views.
* **The driver cabin contract**: add a `DriverRig` under the `VehicleRig` and fill `cabinCameraAnchor`, `driverModel`, `steeringWheel` and `animator` (with a controller). View 2
  becomes available when `DriverRig.IsComplete` is true. Until then the camera says which part is missing. The steering-animation feed is **not written**: it belongs to the
  asset work.
* `CameraProfiles.Validate` checks every profile (FOV range, no lag on rigid views, distances, obstruction floors, names, reasons for unavailability); the game logs an error if one is invalid.

## Code map
| File | Role |
|---|---|
| `NetCore/CameraModel.cs` | views, configurations, `CameraSelector` (rules), `CameraMath` (smoothing, pull-in, zoom, FOV); pure, no Unity |
| `Vehicles/FollowCamera.cs` | the Unity component: binds to a vehicle, computes the pose each frame, blends, sweeps for obstacles |
| `Vehicles/DriverRig.cs`, `VehicleRig.cs` | the anchor / driver contract for production models |
| `Vehicles/VehicleDefinition.cs` | per-model framing scales |
| `Vehicles/VehicleController.cs` | `CameraRequested` event |
| `Input/CameraBindings.cs`, `KeyboardVehicleInput.cs`, `TouchVehicleInput.cs`, `VehicleInputState.cs` | the key map and the request edges |
| `Game/Hud.cs`, `Game/DrivingSession.cs` | the camera button; forwarding requests and the "unavailable" message |

## Logs (what the tests read)
`[Camera] manager ready ...`, `[Camera] bound vehicle=... available=7/8 ...`, `[Camera] view=Cockpit number=1 ... source=input|button main=True cameras=N`,
`[Camera] unavailable view=DriverCabin number=2 stays=... reason=...`, `[Hud] camera button x= y=`, `[Input] gamepad detected`, and `cam=` / `yaw=` on every `[Drive]` line.

## Tests
* `tests/NetCore.Tests/CameraModelTests.cs` (xunit, no Unity): view numbering; every category's profile valid; driver cabin unavailable and why; rigid views never lag or shake; chase numbers
  equal the previous camera's; next / previous / wrap-around / skipping the unavailable view / direct 1 to 8 / refusals / fallback on a vehicle change; pull-in maths (nothing in the way, wall behind,
  wall closer than the floor, never inside the vehicle, monotonic); smoothing (fast in, slow out, never overshoots, any frame time); transition length; zoom limits; FOV; key-conflict and
  "camera state is not in the network code" source checks.
* `Assets/_Project/Tests/EditMode/CameraTests.cs` (Unity EditMode, run by the CI Unity job): the real component with a test vehicle that has the placeholder's body boxes as child colliders: each view's pose, no extra camera,
  the driver cabin refused with a message, incomplete and complete `DriverRig`, rig anchors, wall pull-in and release, the vehicle's own colliders never pulling in, blending, a vehicle change keeping
  the view, per-category and per-model framing, fixed views staying on the vehicle while it moves, no drift when stationary, key / gamepad / touch requests reaching the camera, no camera key
  equal to a driving key.
* `tools/webgl-drive.js` (real WebGL build in headless Chromium, run by CI). Its original throttle-only run is unchanged and ends with the truck crashed off the road at the first bend, so the camera checks run on a freshly reloaded page with a new truck: all eight views by number (2 refused explicitly), `C` / `Shift+C` including wrap-around and skipping the cabin,
  acceleration while switching views, steering in the overhead view, braking in the far chase view, the on-screen button's position and click, and a gamepad Y press **if the game sees the pad**
  (the Gamepad API is faked; if the game does not detect it the step prints SKIPPED and verifies nothing). Screenshots of every view are published with the other CI screenshots.

What these do **not** prove: smoothness, comfort, whether any view looks right, whether the placeholder cab hides the chase view, real gamepad or touch use, performance on a real GPU.

## Manual validation (needs the running game)
1. Open the build (`docs/BUILD.md`), sign in, start a free drive.
2. Press `1` to `8`. Expect: 1 cockpit (an empty cab, road ahead), 2 a message "No driver model..." and the view unchanged, 3 chase, 4 further back and wider, 5 / 6 beside the truck, 7 from above, 8 low at the front.
3. Press `C` and `Shift+C` through the views; check the wrap-around and that 2 is skipped. Click the camera button (top right, beside the minimap); confirm it does not cover the minimap.
4. Drive with each view: accelerate, steer, brake. Watch for the camera lagging badly, jittering, or showing the inside of the cab or body.
5. Drive beside a wall, a bridge or a roadside object in the chase and side views and check the camera pulls in and releases without popping.
6. Change the vehicle (Garage, choose the bus, drive) and check the chosen view is kept and the framing suits a bus.
7. Open the menu with `Escape`, type digits into a field, resume: the camera must not have changed.
8. With a gamepad: `Y` cycles the views. On a touch screen: tap the camera button.

## Performance
One sphere sweep per frame for obstruction-aware views (a pre-allocated 32-slot buffer, no allocation), none for the rigid views; no per-frame logging (logs only on a change); no scene searches
(components are cached when a vehicle is bound). The HUD label changes only when the view changes. Not measured on a device.
