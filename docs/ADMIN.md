# Admin site

The admin site lets the people who run the game see how it is doing and look after players, without anyone touching the database by hand. This first milestone has three parts: **foundation** (who is an admin, two-step sign-in, an audit log), **live health**, and **player lookup**. Nothing is applied to the live database or deployed by committing this: see "Switching it on".

## What is built
| Part | What it does |
|---|---|
| Sign-in | E-mail and password, then a 6-digit code from an authenticator app (TOTP). An admin who has no app yet is walked through setting one up. A player who signs in is told the account is not an administrator and sees nothing else. |
| Live health | Open jobs (and how many expire within 6 hours), jobs created in 24 hours, when the last job was created, players online (by activity), total and new players, how many migrations the database has applied. Alerts: **job board empty**, **fewer than 10 jobs**, **no job created for 12 hours**. Refreshes every 30 seconds. Owner and operations can press **Generate jobs now** (1 to 40, with a reason). |
| Players | Find a player by name or username prefix, exact player id, or (owner and support) exact e-mail. Opening a record needs a written reason (a ticket number, or why) and shows profile, masked e-mail, wallet, last 30 ledger rows, vehicles, last 15 jobs, last 10 sessions and social counts. Read-only: nothing here changes a player. |
| Audit log | Every record opened, every search, every job generation and every role change: who, role, what, on whom, why, when. Owner and analyst can read it. It cannot be edited or deleted. |
| Team | The owner sees the admins and gives, changes or removes a role. The last owner can never be removed. |

## Roles
| Role | Live health | Generate jobs | Find players | Open a record | Audit log | Team |
|---|---|---|---|---|---|---|
| owner | yes | yes | yes (e-mail too) | yes | yes | yes |
| operations | yes | yes | yes (not e-mail) | no | no | no |
| support | yes | no | yes (e-mail too) | yes | no | no |
| analyst | yes | no | no | no | yes | no |

## How it is protected
* **The database decides, not the page.** Every admin action is a database function that checks, on the server and in this order: signed in; an **active** admin; the sign-in passed the second step (the token's `aal` claim is `aal2`); the role is allowed for **that** function. A player who calls one gets `not_admin`. The page hides what a role cannot do as a convenience; `tools/admin_e2e.js` also calls the API directly with each role's token to prove the server refuses.
* **No client can read or write the admin tables.** `admin_users` and `admin_audit_log` have no client privileges and row-level security with no policy. The only way in is the eight functions.
* **Reasons.** Anything that changes something, and opening one player's record, needs a reason of 3 to 300 characters, stored in the audit log. What was typed into a search is never stored (an e-mail address would be personal data), only the kind of search and how many results.
* **Append-only audit log.** Updates, deletes and truncates are refused by triggers, even for the database owner (someone with full superuser rights could still drop the triggers; that is the limit of what a database can promise about itself).
* **Limits on abuse.** Searches 120 per minute, record views 60 per minute, writes 30 per minute, per admin.
* **The page.** No framework, no third-party script, no inline script; a strict content-security policy; every value is shown as text (a test makes a hostile value appear in a record and proves it runs nothing); not allowed inside a frame; the session lives in the tab only and is dropped after 20 minutes without activity. The only values in `config.js` are the project URL and the **public** anon key.
* **Passwords and secrets** are never in the repository. The service-role key and the database password are never used by the site.

Known limits, stated plainly: a refused attempt (not an admin, no second step, role not allowed) is an error, which rolls the statement back, so it is **not** written to the audit log (it shows in the API logs); the first owner is trusted by whoever has database access; the stand-in auth server used in tests implements the same MFA endpoints as Supabase, but the real Supabase service has not been exercised by this change (nothing is applied there yet).

## Switching it on (owner steps, none of them done yet)
1. **Apply the migration** `supabase/migrations/20261021000001_admin_foundation.sql` to the live project with the existing "Supabase apply schema" workflow. It only adds tables and functions; it changes nothing that exists.
2. **Enable multi-factor in Supabase**: Authentication > Multi-Factor (or Sign In / Providers > MFA): TOTP enabled.
3. **Make the first owner** with SQL in the Supabase SQL editor, using the account you sign in to the game with (look up its id under Authentication > Users):
   ```sql
   insert into admin_users(user_id, role, note) values ('<your user id>', 'owner', 'first owner');
   ```
   There is deliberately no other way to become the first owner. After that, owners add the rest on the **Team** page.
4. **Publish the site**: any static host (Cloudflare Pages, Netlify, GitHub Pages...). Put the `admin/` folder on it, create `config.js` from `config.example.js` with the project URL and the public anon key, and make the host send the headers in `admin/_headers`. Use a separate address from the game. Do not publish it without MFA enabled (step 2).
5. Sign in, scan the QR code, check **Live health**.

## Tests
* `supabase/tests/50_admin.sql`: 135 assertions on the database side (role matrix, MFA, reasons, audit, roles, last owner, search, record, health, rate limit).
* `supabase/tests/mutants_backend.py`: 35 mutants (remove the MFA check, widen a role, drop an audit call, allow audit updates, unmask e-mail, remove LIKE escaping, ...); every one must make the tests fail.
* `tools/admin_e2e.js`: 46 browser checks against a real database, real migrations and real PostgREST (the sign-in service is a stand-in with real TOTP): player refused, two-step set-up and sign-in, wrong codes refused, each role's sections, the server refusing what the page hides, reasons, masked e-mail, hostile text, audit contents, last owner. Runs in CI in the "Backend over HTTP" job.

## Not built yet (next milestones, in the order proposed)
Content and messages (news and hero editor, broadcast, routes and servers), the reports queue with mute, suspend and ban, wallet adjustments with a second admin's approval, economy and vehicle catalogue tools, cheat review, maintenance switch and feature flags, analytics. The older operator functions (`admin_adjust_wallet`, `admin_refund_transaction`, `admin_broadcast`, `admin_set_route`, `admin_set_server`, `generate_jobs`, `sweep_stale_sessions`, `purge_old_data`) are still reachable only with SQL; the site does not call them.
