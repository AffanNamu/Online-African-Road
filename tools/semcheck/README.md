# semcheck - a type-check of the Unity C# without Unity

`dotnet build tools/semcheck/semcheck.csproj` compiles the game's scripts (and the EditMode tests) against the real `UnityEngine.*` module assemblies
(NuGet `UnityEngine.Modules`, Unity 2021.3 API) plus hand-written stubs for what that package lacks (`UnityEngine.UI`, the Input System, Netcode, Unity
Gaming Services, URP: see `Stubs.cs`). It exists so a typo, a wrong member or a bad argument is found in seconds in a dev sandbox instead of 25 minutes
into the Unity CI run.

It is NOT authoritative: Unity 6 has APIs 2021.3 does not (`Rigidbody.linearVelocity`) and the stubs are incomplete, so a few errors are always reported
(they are listed in `known-noise.txt`). The real compile is the Unity job in CI. `./tools/semcheck/run.sh` prints only errors that are not known noise.
