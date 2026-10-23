# Admin site: production-readiness checklist

**Status: prepared, nothing done.** No step below has been run against the live project. Applying migrations, enabling multi-factor sign-in, creating the first owner and publishing the site each change something live, so each needs the owner's explicit go-ahead. This file is the plan and the proof you will ask for; `docs/ADMIN.md` describes what the site is.

Everything that needs a secret (database connection string, passwords, keys) is typed by the owner on their own machine or in GitHub's secret store. Nothing here asks for one to be pasted into chat or committed.

## 0. What goes live, exactly
Three migrations, applied in this order by the existing **Supabase apply schema** workflow (`supabase/apply.sh`; each runs in its own transaction and is recorded in `public._aro_migrations`, so a failure rolls that migration back and stops the run):

| Migration | Adds | Touches something that already exists? |
|---|---|---|
| `20261021000001_admin_foundation` | `admin_role`, `admin_users`, `admin_audit_log` (append-only), 8 admin functions | No. Only adds. |
| `20261022000001_admin_content_moderation` | `content` role, `player_sanctions`, `chat_report_reviews`, 14 admin functions, `admin_set_role` replaced (it only exists since the previous migration, so no live object changes) | **Two small BEFORE INSERT triggers**: on `convoy_messages` (a muted/suspended/banned player's chat line is refused) and on `game_sessions` (a suspended/banned player cannot start a session). |
| `20261023000001_account_status_enforcement` | `enforce_account_status()` and `get_my_restrictions()`; points PostgREST at the first one | **A project setting**: `alter role authenticator set pgrst.db_pre_request = 'public.enforce_account_status'`. From then on every API request runs one indexed lookup first. |

Nothing is dropped, renamed or rewritten, and no player data is changed. The seed step of `apply.sh` is idempotent and only generates jobs if fewer than 10 are open.

## 1. Decisions only the owner can take (tick before anything else)
- [ ] The Supabase project used by the game is **dedicated** to it (the apply workflow refuses otherwise and asks you to type `dedicated-project`).
- [ ] Who is the first owner: a **separate admin e-mail address** (not the one you play with) is recommended, plus a **second owner** so that losing one phone does not lock you out (section 7).
- [ ] Where the site will be published and on which address (separate from the game).
- [ ] A quiet time for step 4 (it takes about a minute; the hook adds a lookup to every request, so do it when few people are playing).
- [ ] You accept the limits in section 13.

## 2. Backup (before the first change)
Check which protection the project has: Supabase dashboard > Database > Backups. Daily backups exist on paid plans, point-in-time recovery is an add-on; a free project may have **none**, in which case the manual dump below is mandatory.
- [ ] Take a manual dump on **your own machine** (the connection string comes from Project Settings > Database > Connection string; type it into your shell only):
  ```bash
  export DATABASE_URL='postgresql://...'            # never paste this anywhere else
  pg_dump --format=custom --no-owner --file "aro-before-admin-$(date +%F).dump" "$DATABASE_URL"
  pg_restore --list "aro-before-admin-$(date +%F).dump" | head      # proves the file is readable
  ```
  Store it encrypted and offline. It contains every player's data: it must never go into the repository.
- [ ] (Optional, recommended once) Restore it into a **scratch** Supabase project or local Postgres to prove the backup works. A backup nobody has restored is a hope.
- [ ] Note the date, time and what the dump covers.

## 3. Read-only look before and after
- [ ] Run the workflow **Supabase preflight (read-only)** (type `read-only`). It cannot change anything and prints aggregates only (counts, hashes). Keep the output: after the change the same counts must match (players, wallets, transactions, vehicles, jobs).

## 4. Apply (owner starts it; nothing here is automatic)
- [ ] Merge state: `main` contains the three migrations and CI is green on that commit (check the run on the exact commit you are applying).
- [ ] Run **Supabase apply schema**, type `dedicated-project`. Expected log: `skip` for the older migrations, `apply` for the three new ones in order, `verify live schema`, `DONE`.
- [ ] Read the log for the word **WARNING**. `could not set pgrst.db_pre_request` means the host refused the setting. The migration still completed and `verify_live.sql` will have stopped the run with *ban enforcement hook is NOT configured*. Fix: run, as the project owner in the SQL editor:
  ```sql
  alter role authenticator set pgrst.db_pre_request = 'public.enforce_account_status';
  notify pgrst, 'reload config';
  ```
  then re-run the apply workflow (it skips what is already applied and re-runs the verification).
- [ ] If a migration failed: it was rolled back; nothing from it exists; later ones did not run. Send the failing line (no secrets) and do not retry blindly.

## 5. Verify the migrations (read-only queries in the SQL editor)
```sql
select name, applied_at from public._aro_migrations where name >= '20261021' order by name;          -- three rows
select relname, relrowsecurity from pg_class where relname in ('admin_users','admin_audit_log','player_sanctions','chat_report_reviews');  -- all true
select grantee, privilege_type from information_schema.role_table_grants where table_name in ('admin_users','admin_audit_log','player_sanctions','chat_report_reviews') and grantee in ('anon','authenticated');   -- no rows
select count(*) from pg_proc where pronamespace='public'::regnamespace and proname like 'admin\_%' and has_function_privilege('anon', oid, 'execute');   -- 0
select r.rolname, s.setconfig from pg_db_role_setting s join pg_roles r on r.oid = s.setrole where r.rolname = 'authenticator';              -- contains pgrst.db_pre_request=public.enforce_account_status
select tgname from pg_trigger where tgname in ('convoy_messages_sanction','game_sessions_sanction','admin_audit_no_change','admin_audit_no_truncate');  -- four rows
select count(*) from admin_users;                                                                                                              -- 0 until step 7
```
- [ ] Every query returns what the comment says. The apply workflow's own `verify_live.sql` has already checked: RLS everywhere, nothing for `anon`, no client writes, only the reviewed function list executable, pinned search paths, the hook configured.

## 6. Multi-factor sign-in (Supabase dashboard)
- [ ] Authentication > Sign In / Providers (or Multi-Factor): **enable TOTP** (enrolment and verification). Names move between dashboard versions; the goal is that a signed-in user can enrol an authenticator app and that their token gets `aal2` after verifying.
- [ ] Prove it with a **throwaway** account before using a real owner: sign in to the (not yet public) admin site locally (`python3 -m http.server --directory admin`, with your `config.js`), enrol the app, and check the site accepts the code. If the site says *mfa_required* after a correct code, the project's MFA is not active: stop here.

## 7. First owner
- [ ] Sign up the admin e-mail in the game once so the account exists, then look up its id (Authentication > Users). SQL editor:
  ```sql
  insert into admin_users(user_id, role, note) values ('<owner user id>', 'owner', 'first owner');
  insert into admin_users(user_id, role, note) values ('<second owner user id>', 'owner', 'recovery owner');   -- recommended
  ```
  There is deliberately no other way to become the first owner.
- [ ] Each owner enrols the authenticator app. **Supabase gives no recovery codes.** If a phone is lost, another owner (or you, in the dashboard: Authentication > Users > the user > remove the factor) resets it. That is why there are two owners.
- [ ] Password manager for the admin accounts; strong unique passwords.

## 8. Publish the site
- [ ] Host: any static host over HTTPS (Cloudflare Pages, Netlify, GitHub Pages, ...). Publish the **`admin/` folder only**. Create `config.js` there from `config.example.js` with the project URL and the **public anon key**. Never the service-role key, never the database password.
- [ ] The host must send the headers in `admin/_headers` (Netlify and Cloudflare Pages read the file). On another host copy them into its configuration. If the project uses a custom domain, widen `connect-src` accordingly.
- [ ] Check from outside: `curl -sI https://<admin address>/ | grep -iE 'content-security|x-frame|referrer|cache-control'` shows all of them; `https://<admin address>/config.example.js` returns 404; `.../robots` is irrelevant (the page carries `noindex`).
- [ ] Optional second wall (recommended): put the address behind the host's access control (Cloudflare Access or an IP allow-list). The site is safe without it (the database decides), but a wall means strangers cannot even try to sign in.

## 9. Access checks (the site and the API refuse what they should)
Use throwaway player accounts; each role needs one account.
| Check | Expected |
|---|---|
| A normal player signs in to the admin site | "Not an administrator", no tabs |
| That player calls `admin_health` directly with their token (`curl` with their access token) | HTTP 400, `not_admin` |
| An owner signs in with password only, then calls `admin_health` with that token | `mfa_required` |
| After the authenticator code | the tabs for the role appear; the same call succeeds |
| support, operations, analyst, content each open the site | exactly the tabs in the table in `docs/ADMIN.md` |
| Each of those calls a function outside its role directly | `not_allowed` |
| Open a player record without a reason | refused in the form; with a reason it opens and an audit row appears |
- [ ] All rows behave as written. (`tools/admin_e2e.js` runs this same matrix against a throwaway backend in CI; this step proves it on the real project.)

## 10. Post-deployment validation
- [ ] **Health tab** opens; no unexpected alerts (job board not empty, last job recent).
- [ ] **Audit log** shows your own first actions.
- [ ] **Immediate restriction**, with a throwaway player who is signed in to the game: in the admin site suspend that player for 1 hour with a reason. Within seconds the game must show an error on any action, and `curl` with that player's existing token against any endpoint (for example `get_my_profile`) must return `account_suspended`, while `get_my_restrictions` still answers with the suspension. Then **lift** it on the Restrictions tab: the same token works again at once.
- [ ] **Mute** a throwaway player: they cannot chat but everything else works.
- [ ] Check the project's API logs for a rise in 4xx/5xx after the change, and request latency is unchanged (the hook is one indexed lookup).
- [ ] Re-run the **read-only preflight**; counts equal step 3.
- [ ] Record: date, who did it, commit applied, results.

## 11. Recovery procedures
| Situation | What to do |
|---|---|
| A migration fails during apply | It was rolled back (one transaction each) and later ones did not run. Nothing to undo. Fix forward in the repository and re-run. |
| After apply, everything returns `account_suspended` or the API misbehaves | **Switch the hook off** (ban enforcement drops back to the chat and session-start triggers; nothing else changes): `alter role authenticator reset pgrst.db_pre_request; notify pgrst, 'reload config';` Switch it back on with the line in section 4. |
| The admin site must go offline now | Unpublish it at the host. To also stop the API side: `update admin_users set active = false;` (every admin function then answers `not_admin`). Re-activate with `update admin_users set active = true where user_id = '<id>';` |
| An owner is locked out (lost phone, no second owner) | Dashboard > Authentication > Users > the user > remove the MFA factor; they enrol again. If the account itself is lost: `insert into admin_users(user_id, role) values ('<new owner id>', 'owner') on conflict (user_id) do update set role='owner', active=true;` |
| A player was restricted by mistake and the site is unavailable | `update player_sanctions set lifted_at = now(), lifted_by = '<owner id>', lift_reason = 'mistake' where player_id = '<id>' and lifted_at is null;` then `update auth.users set banned_until = null where id = '<id>';` (the first line restores the game at once, the second the sign-in) |
| An admin account was compromised | `update admin_users set active = false where user_id = '<id>';`, remove their MFA factor and reset their password in the dashboard, then read the audit log for everything they did (it cannot be edited). |
| Everything must be undone | There are deliberately no down-migrations. Prefer the switches above. Restoring the section-2 dump is the last resort and **loses every change since the dump** (players' progress, purchases); do it only with the owner's decision, into a scratch project first. |

## 12. Sign-off
| Step | Done by | Date | Evidence |
|---|---|---|---|
| Decisions (section 1) | | | |
| Backup and restore rehearsal (2) | | | |
| Preflight before (3) | | | |
| Apply (4) | | | run link |
| Verify (5) | | | |
| MFA (6) | | | |
| Owners (7) | | | |
| Publish (8) | | | |
| Access checks (9) | | | |
| Post-deployment (10) | | | |

## 13. What is not proven, stated plainly
* The real Supabase multi-factor service has not been exercised; tests use a stand-in with the same endpoints and real TOTP. Section 6's throwaway check is how you find out.
* The hook was tested against the real PostgREST (v12.2.3, configured by environment variable) over HTTP. On Supabase it is configured by the role setting above. If the host does not provide `request.path`, the hook **fails closed**: a restricted player is refused everywhere including `get_my_restrictions`, and a restricted *admin* would be refused on admin endpoints too. Section 10's check detects this immediately; the switch-off line is in section 11.
* `auth.users.banned_until` is set by the moderation functions; it has only run against a stand-in table. If the host refuses it, the audit entry says `auth_synced: false` and the hook and triggers still hold; the account could still sign in but could not do anything.
* A token that was already issued stays valid as a token for up to an hour, but the hook refuses every request made with it while a suspension or ban is in force.
* Anything that does not go through the API (the service role, the SQL editor) is not stopped by a suspension; that is the operator's access.
