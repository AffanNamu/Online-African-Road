-- Convoy <-> multiplayer session link. The Unity Multiplayer join code is only revealed to convoy MEMBERS,
-- and only the leader can change it. Realtime movement stays in Unity Netcode/Relay, never in Postgres.

alter table convoys add constraint convoy_code_fmt check (session_code is null or session_code ~ '^[A-Za-z0-9]{4,16}$');

-- Hide session_code: replace table-wide SELECT with column grants that omit it.
revoke select on convoys from authenticated;
grant select (id, leader_id, name, created_at, disbanded_at) on convoys to authenticated;

create function get_convoy_session_code(p_convoy uuid) returns text
language plpgsql security definer set search_path = public as $$
declare v_code text;
begin
  if auth.uid() is null then raise exception 'not_authenticated'; end if;
  if not exists (select 1 from convoy_members where convoy_id = p_convoy and player_id = auth.uid()) then
    raise exception 'not_a_member';
  end if;
  select session_code into v_code from convoys where id = p_convoy and disbanded_at is null;
  return v_code;
end $$;

create function set_convoy_session_code(p_convoy uuid, p_code text) returns void
language plpgsql security definer set search_path = public as $$
begin
  if auth.uid() is null then raise exception 'not_authenticated'; end if;
  update convoys set session_code = p_code
   where id = p_convoy and leader_id = auth.uid() and disbanded_at is null;
  if not found then raise exception 'not_leader'; end if;
exception
  when unique_violation then raise exception 'code_taken';
  when check_violation then raise exception 'invalid_code';
end $$;

revoke execute on function get_convoy_session_code(uuid), set_convoy_session_code(uuid, text) from public, anon, authenticated;
grant execute on function get_convoy_session_code(uuid), set_convoy_session_code(uuid, text) to authenticated;
