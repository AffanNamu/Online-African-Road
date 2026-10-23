-- Immediate enforcement of suspensions and bans on EVERY API request, not only on starting a session or chatting.
--
-- Until now a suspended or banned player who already held a token (valid for up to an hour) could keep calling every endpoint that is not a session start or a chat line.
-- PostgREST can run one function before every request (its `db-pre-request` setting); this migration adds that function and points PostgREST at it.
--   * enforce_account_status()  runs first on every request. A player with a live suspension or ban gets 'account_suspended' (HTTP 400, hint in the response) for everything except:
--       - the admin endpoints (/rpc/admin_*): an administrator must always be able to reach the site to recover from a mistake (admins cannot be sanctioned through the site;
--         if one is restricted by SQL, the site still opens and the admin endpoints still do their own role + multi-factor checks);
--       - get_my_restrictions: lets the game tell the player what is in force and until when.
--     A mute does not block the API (it only stops chat, enforced by the trigger from the previous migration). When the request path is unknown the function fails CLOSED.
--   * get_my_restrictions()  the player's own live mute / suspension / ban with end times. The moderator's written reason is never returned.
--   * The existing triggers (no chat line, no new session) stay as the second line of defence for callers that do not come through PostgREST.
-- The hook is configured with `alter role authenticator set pgrst.db_pre_request`, which is a setting of the hosting project. If the database refuses (permissions differ between hosts)
-- the migration still completes and prints a WARNING; supabase/verify_live.sql then FAILS LOUDLY until the single line in docs/ADMIN_GO_LIVE.md is run by the owner. Nothing is silently skipped.
create temp table _fn_before as select oid from pg_proc where pronamespace = 'public'::regnamespace;

create function enforce_account_status() returns void
language plpgsql stable security definer set search_path = public as $$
declare v_uid uuid := auth.uid(); v_path text := coalesce(nullif(current_setting('request.path', true), ''), '');
begin
  if v_uid is null then return; end if;                                                     -- anonymous: nothing to enforce (they can call nothing anyway)
  if v_path like '/rpc/admin\_%' or v_path = '/rpc/get_my_restrictions' then return; end if;
  if _sanction_live(v_uid, array['suspend', 'ban']) then
    raise exception 'account_suspended' using hint = 'This account is restricted. See get_my_restrictions.';
  end if;
end $$;

create function get_my_restrictions() returns jsonb
language plpgsql security definer set search_path = public as $$
declare v_uid uuid := auth.uid();
begin
  if v_uid is null then raise exception 'not_authenticated'; end if;
  perform _rate_limit('my_restrictions', 60, 60);
  return jsonb_build_object('server_time', now(), 'items', coalesce((
    select jsonb_agg(jsonb_build_object('kind', s.kind, 'since', s.created_at, 'expires_at', s.expires_at) order by s.created_at desc)
      from player_sanctions s where s.player_id = v_uid and s.lifted_at is null and (s.expires_at is null or s.expires_at > now())), '[]'::jsonb));
end $$;

-- lock-down (same convention as every migration)
do $$ declare r record;
begin
  for r in select p.oid::regprocedure as sig, p.proname from pg_proc p
            where p.pronamespace = 'public'::regnamespace and p.oid not in (select oid from _fn_before) loop
    execute format('revoke execute on function %s from public, anon, authenticated', r.sig);
    if r.proname in ('enforce_account_status', 'get_my_restrictions') then
      execute format('grant execute on function %s to authenticated', r.sig);
    end if;
  end loop;
end $$;

-- point PostgREST at the hook (a no-op on a database without the Supabase `authenticator` role)
do $$ begin
  if exists (select 1 from pg_roles where rolname = 'authenticator') then
    begin
      alter role authenticator set pgrst.db_pre_request = 'public.enforce_account_status';
      perform pg_notify('pgrst', 'reload config');
    exception when others then
      raise warning 'could not set pgrst.db_pre_request (%): run the line in docs/ADMIN_GO_LIVE.md as the project owner', sqlerrm;
    end;
  end if;
end $$;
