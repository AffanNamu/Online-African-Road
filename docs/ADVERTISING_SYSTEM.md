# Advertising system (built 2026-11-09, phases A1 to A5)

**Authorised by the owner on 2026-11-09** (storage, Admin Dashboard extension, owner-only approval, public enquiry form, packages and prices as data, content policy, aggregated analytics; no payments, no cross-session tracking). This document describes what is built. The plan and reasoning are in `ADVERTISING_PLAN.md`.

## 1. Verification levels (what is true today)

| Level | Backend, storage, admin | Game side (manifest, atlas, faces) |
|---|---|---|
| 1. Implemented | yes: migration `20261110000001_advertising.sql`, `supabase/storage_ads.sql`, `supabase/seed_ads.sql`, `admin/ads.js`, `advertise/` (public form) | yes: `NetCore/AdSystem.cs`, `Game/AdService.cs`, DTOs and `AdsService`, streamer events |
| 2. Automated tests passed | yes: database suite `53_advertising.sql` (about 230 assertions), the structural guards (anonymous surface, SECURITY DEFINER, RLS), **45 mutants: 44 killed, 1 documented as defence in depth**, the API contract (54 endpoints incl. the manifest and exposure) | yes: 15 .NET tests (atlas, letterboxing, manifest back-off and expiry, measured exposure, board ids over the real route, seed currency); the C# is compiled by Unity in CI |
| 3. Imported into / compiled by Unity | **pending.** CI run 326 (the first with the game-side code) **failed to compile**: `UnityWebRequestTexture` and `DownloadHandlerTexture` live in the built-in module `com.unity.modules.unitywebrequesttexture`, which the project's manifest did not list; the module is now added to `Packages/manifest.json` (a built-in module, no download) and the next CI run compiles it. The backend side of run 326 also failed its mutation test for a reason in the test setup, not in the protection: five admin role-change mutants were aimed at an older definition of `admin_set_role` that the advertising migration re-creates; they now target the live definition and are all killed | pending |
| 4. Verified in actual gameplay | **no**. `tools/webgl-ads.js` (CI, informational) proves the render path with generated test images; nothing has been shown to the owner. A live Supabase project has **not** been touched | no |
| 5. Visually reviewed | no | no |

**Not applied to any live Supabase project.** The migration, storage script and seed are ready; applying them is a separate step the owner (or an agent holding the project's credentials, which this repository does not have) takes. See section 7.

## 2. Roles, and why a hidden button is not the protection
Every admin function checks, in this order and inside the database: signed in, active admin, second step (`aal2`), role for THAT function, a reason for any change, rate limit. A client cannot skip any of it.

| Action | owner | advertising | analyst | other roles / players / anonymous |
|---|---|---|---|---|
| read overview, inventory, packages, campaigns, analytics | yes | yes | yes (aggregates only) | no |
| read enquiries (contact details) and artwork | yes | yes | **no** | no |
| create a direct-sale campaign, edit a draft, convert an enquiry, register artwork, reserve a board face, submit for review | yes | yes | no | no |
| **approve or reject artwork**, **approve or reject a campaign** | **yes** | **no** | no | no |
| **publish an image to the public bucket** | **yes** (storage policy) | **no** | no | no |
| pause a live campaign | yes | yes | no | no |
| **resume**, **terminate**, set the manual payment note | **yes** | **no** | no | no |
| change a package, a price, a board's status or estimate | **yes** | **no** | no | no |
| submit an enquiry, ask for an upload ticket | anyone, anonymous, rate limited and validated | | | |
| read the manifest, report exposure | any signed-in player | | | |

Tested three ways: 12 owner-only functions refused to `advertising` by the database suite, by the admin e2e through the real HTTP API with the role's own token, and by 12 mutants that grant the role and must fail the suite. All approvals, rejections, price changes (old and new amount), activations (resume), pauses, terminations, assignments and publications are written to the append-only audit log with the actor's role and reason.

## 3. The data (migration `20261110000001_advertising.sql`)
`ad_packages` and `ad_package_prices` (ISO 4217 currency, amount in **minor units**, per period; NGN first, USD is just another row; the migration ships **no price**), `ad_boards` and `ad_slots` (inventory: one row per physical board and face; from the route data), `ad_advertisers`, `ad_enquiries`, `ad_upload_tickets`, `ad_campaigns` (draft, in_review, approved, rejected, paused, terminated; the *effective* state scheduled/live/ended follows the server clock in Lagos), `ad_creatives`, `ad_assignments` (**an exclusion constraint makes two reserved placements on one face on overlapping dates impossible**, whatever a client does), `ad_exposure_daily` (aggregates only), `ad_policy_terms`, `ad_public_limits`. RLS is on for every table and no client holds a grant on any of them.

A campaign copies the agreed price at the moment it is saved, so a later price change never rewrites it. `payment_status` is a manual note only the owner sets. **There is no payment provider, no checkout, no billing, no invoice.**

## 4. Storage (two buckets)
* `ad-creatives-public` (public read, JPEG/PNG/WebP, 150 KB): holds **only the owner's processed variants**. The storage policy accepts an insert only from the owner at the second step and only at a path that an *approved* creative expects (`creatives/<id>/<aspect>_v1.jpg`); a rejected, withdrawn, unknown or foreign path is refused (tested for each). Nothing pending or rejected can ever be public.
* `ad-submissions` (private, 300 KB): advertiser submissions and admin uploads. An **anonymous** advertiser may add one object at a path with a live one-time ticket (`request_ad_upload`); only the owner and the advertising role can read, add (under `admin/`) or delete anything there; a player or analyst reads nothing.
* Validation: the database checks the object exists, its size and MIME type (from the storage metadata), and the hash and dimensions the owner's browser reports; the **image bytes are validated in the owner's browser** (decoded on a canvas, resized to the board's shape, re-encoded as JPEG so all metadata is dropped). The database cannot decode an image; that limit is real and the human review is the control.
* Why JPEG: the engine decodes JPEG and PNG, not WebP.
* A project without Storage skips this block (a notice); `supabase/storage_ads.sql` is a verbatim copy (CI checks) to run once Storage is on.

## 5. Public enquiry form (`advertise/`)
A static page holding only the public anon key. It calls two reviewed functions. Business, contact person, e-mail, phone/WhatsApp, category (from a list; gambling, tobacco or nicotine, alcohol, adult, weapons and political are refused with a clear message), description, preferred city and placement, duration, message, artwork (optional, 300 KB), a rights confirmation, a honeypot. Caps without any identity: 40 per hour and 150 per day globally, 3 per e-mail per day, 60 uploads per hour. A keyword screen flags (does not block) text that mentions banned things; a flagged enquiry or campaign needs the approver's explicit acknowledgement. **An enquiry never creates a published advertisement**: the team contacts the advertiser outside the system, approves the *enquiry*, converts it to a *draft* campaign, prepares it, and only the owner approves it.

## 6. In the game
`get_ad_manifest` returns the live placements (a few bytes when unchanged). `AdService` is off without a backend, silent when the service is down, never blocks sign-in, loading or driving, takes no collider and touches no physics. Boards without a campaign keep the shipped look; a board with a campaign shows its image from **one shared 2048 x 1024 atlas (8 MB) drawn as one mesh with one material (+1 draw call)**, fixed cells, least-recently-used reuse, letterboxing (never stretched), neutral fallback until an image arrives or after a failure, a manifest older than a day dropped, back-off 1 to 30 minutes. Only images of boards within 400 m are downloaded, one at a time. The label *Sponsored* is in the manifest.
**Measured exposure** counts only while the board is within its reading distance, in view, facing the player within 60 degrees, not hidden (a ray check) and showing a real advertisement; a view is 1.5 s in one pass; *loaded is never a view* (a test). Daily sums per face and campaign are the only thing stored: no player, session or device id (a test inspects the table's columns). **Estimated** passes are the owner's own per-board estimate times the days live and are labelled so everywhere, never added to the measured numbers.

## 7. Applying it (a deliberate step, not done here)
1. `supabase/apply.sh` (applies the migration once, tracked in `_aro_migrations`). Take a database backup first.
2. Switch Supabase Storage on, if it is not; run `supabase/storage_ads.sql`.
3. `psql -f supabase/seed_ads.sql` (the route's billboards as boards, status `planned`; idempotent, never overwrites the owner's changes). It is deliberately **not** in `apply.sh`.
4. Give the advertising role to the team with the existing Team page (owner only).
5. Host `admin/` (as before, it now loads `ads.js`) and `advertise/` (copy `config.example.js` to `config.js`).
6. The owner sets prices and switches boards to `available`.
**Rollback.** Nothing existing was changed except `admin_set_role` (re-created to accept the new role): to roll back, drop the `ad_*` tables and functions and the storage policies named `aro_ad_*`, delete the two buckets; the game ignores a missing service. A forward-only fix is preferred.

## 8. Not done / honest limits
* The 3D billboard assets with a dedicated `AdFace` mesh, LED screens that rotate frames, and flyover/terminal/wall/pylon boards: the placeholder `billboard` is the only board in the game, and only its existing label face is used. Night: the face is lit by the scene, not by a lamp factor (no emissive brightening yet).
* Clicks: the report says "not supported yet" (the data path exists, no in-game prompt).
* The player-facing privacy notice wording, legal review for Nigeria or any other market, and a CAPTCHA are not done (the form uses a honeypot and caps instead).
* Image bytes are not scanned for malware server-side (impossible in this stack); the browser decode, JPEG re-encode and the human approval are the controls.
* No test has run against a real Supabase Storage service (the tests use a stand-in with the same table and row-level-security shape).
* Real-GPU performance of the face mesh and the atlas upload is unmeasured.
