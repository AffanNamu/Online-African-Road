# People you can see (P2 of the functional places, approved by the owner on 2026-11-14)

The owner's words: *"bus terminal ... put some people, visuals of people moving around ... passengers until passengers fill the bus ... we are building reality."* Decisions: P2 approved, **people built by code** (`docs/DECISIONS.md`).

**State:** level 1 (implemented) and level 2 (17 .NET tests, and the figures checked by eye in Blender renders). Level 3 (Unity compile and the browser check) waits for CI. Levels 4 and 5 are not reached. **Nothing is approved.**

## 1. What is in the game

* **Ten figures in Kano dress**, built by code (`NetCore/People.cs`):
  * men: a man in a kaftan and kufi cap, a man in a wide babban riga over trousers, a man in shirt and trousers, an older man in a plain kaftan;
  * women: a woman in hijab, blouse and wrapper, a woman in a long dress and long hijab, a woman in a black abaya, a market woman with a head-tie and an enamel basin on her head;
  * children: a schoolboy with a school bag, a schoolgirl in a white hijab.
* **Shape:** bodies, robes, heads and hijabs are turned shapes with smooth rings: rounded shoulders, a waist, a neck, a jaw. Men have short hair, a kufi cap or (the older man) a grey beard. A hijab is one drape from the head over the shoulders with the face left open. Arms hang with the elbow a little bent.
* **Variety:** each figure has three colourways and three skin tones. There are two levels of detail (700 to 1,010 and 350 to 525 triangles). No faces are modelled: believable proportions and dress, a stylised look, not photoreal people.
* **Checked by eye:** the figures were exported from the game's own mesh code and rendered in Blender, with any wrongly facing surface painted red. The first version failed that check (slab bodies, bald heads, open sleeve and collar ends, legs swinging out through the robes) and was rebuilt. The renders are not in this repository (it holds no binaries).
* **Movement:** a rig of five rigid parts (body, legs, arms) gives a walk cycle (legs and arms in opposition, a small bob) and an idle sway. Under a long robe the stride is short (30 to 36% of the full swing), so the feet step out under the hem and no leg leaves it.
* **Where people are** (`NetCore/Crowd.cs`, `CrowdPlanner`), deterministic: the same people at the same places on every client, whichever chunks are loaded.
  * At every **fuel station**: an attendant beside the first pump (not where a vehicle stands), and a customer walking between the mart and the forecourt.
  * In front of **shops, markets, offices, banks, hotels, restaurants, clinics, hospitals and public buildings**: one to three people chatting in pairs, and someone walking along the frontage of the larger ones.
  * In front of **mosques**: two to five people.
  * At one **home** in four: someone at the gate.
  * At **bus stops** and at the **bus terminal** of the Kano city streets (its four shelters on the terminal plate): two to six people waiting under each shelter, facing the road or the bays, and someone walking behind it. People of a plate's own shelters, kiosks and stalls may stand on the plate; nobody else does.
  * At **kiosks and market stalls**: customers at the counter and the seller behind it.
  * On the **walkways of city streets**: walkers going back and forth (about 1.6 per 100 m of walkway a side). A walkway stretch ends wherever it would cross another road, a site plate or an island.
* **The hour:** everyone is out from 07:00 to 19:00. About 60% are out in the evening, 35% early in the morning and 12% at night. It is the same people every night.
* **The bus fills up.** When a bus halts at a stop, the server answers how many board and how many alight (`serve_stop`). That many people walk in front of the bus: first those getting off step out of the door and walk away, then those boarding walk from the kerb to the door one after the other and disappear into the bus. At most 12 of each are drawn; the HUD keeps the exact numbers.
* **Drawing** (`World/CrowdRenderer.cs`): the nearest people first, at most **60** (20 on the low tier), within 140 m. Everyone is in **one mesh with one material** (a small palette texture), rebuilt each frame from pre-posed frames. People cast shadows except on the low tier.
* **On the ground:** everyone stands at the ground height of the spot they are on (walkway, plate or terrain), and the mesh is written relative to the chunk under the camera, so people do not shimmer 100 km from the origin.
* **Visual only:** people have no collider and do not react to vehicles. Nothing about them is sent over the network.

## 2. How this differs from the proposal (honest)

| Proposal | Done | Why |
|---|---|---|
| A Blender family with vertex-animation textures | The figures are built by code in the game's own C# (NetCore), with a five-part rig posed on the CPU | They then appear in the public build as well as the review build, need no binary assets or private staging, and are fully unit-tested. One mesh still means one draw call. A Blender-built production family can replace the geometry later with the same rig (one bone per vertex). |
| Crowds at terminals and shelters | The game's bus stops (`bus_stop`), including the four shelters of the E1 bus terminal plate; not the kit's terminal hall and long shelters | The kit's terminal and shelter models are not in the game's catalogue yet: they stand only in review scenes. |
| Density by district and hour | By hour; the district shows through where the shops, mosques and walkways are | The simpler rule gave believable streets; a per-district factor can be added in `CrowdPlanner`. |

## 3. Tests (`tests/NetCore.Tests/PeopleTests.cs`, 17)

* Ten designs, two of them children, three colourways each, valid palette indices.
* Every face of every part looks outwards (the game culls back faces), and each figure encloses a person-sized volume.
* The poses are shared by every colourway and skin tone (one pose set per design and level).
* Everyone stands on the ground where they are (within 2 cm), on E1, F1 and the shipped road.
* Every figure in every colourway and skin tone builds within 400 to 1,100 triangles and 620 vertices, and LOD 1 is cheaper. Adults are 1.65 to 1.9 m (2.05 m with the basin), children 1.0 to 1.35 m. The feet are on the ground and the skin tone is used.
* The walk: at a quarter cycle the left foot is forward and the right back, the arms swing in opposition, the body bobs. The idle keeps the feet still. Under a robe the step is 8 to 22 cm and the rear shin stays inside the hem.
* The crowd is the same when the route is rebuilt, and every person belongs to one chunk. E1 has 150 or more people, 20 or more walkers, an attendant at each of its stations, people at its mosques and eight or more people waiting at the terminal's shelters. The shipped road has its station attendant and a waiting group at its bus stop.
* **Nobody stands or walks on a carriageway, a shoulder, a site plate or an island, or inside a solid prop.** This covers every spot and every point and mid-point of every path, on E1, F1 and the shipped road.
* Walkers stay beyond the kerb at walkway height.
* The night has fewer people than the day, and they are among the day's people. The budget is held (60, or 20 on the low tier), nearest first.
* The bus: the server's numbers (at most 12 drawn of each), first off then on. Boarding passengers wait on the kerb side, end at the door and are gone after it.

In the browser, `tools/webgl-pump.js` (public build, and review build with the real models) logs the crowd at the station and takes a picture of it (informational).

## 4. Not done
* A Blender production family (realistic textures; the same rig).
* Crowds at mosques by prayer time, attendants at every pump, market bustle (P4).
* People stepping aside for vehicles.
* The kit's bus terminal and shelters in the game's routes, with their boarding points.
