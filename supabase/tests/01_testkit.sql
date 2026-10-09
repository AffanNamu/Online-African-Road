-- Test helpers (scratch DB only). All run as the invoking superuser and switch roles explicitly.
create schema testkit;

-- Run p_sql as p_role/p_uid and require an error containing p_msg.
create function testkit.expect_error(p_role text, p_uid uuid, p_sql text, p_msg text) returns void
language plpgsql as $$
begin
  perform set_config('request.jwt.claim.sub', coalesce(p_uid::text, ''), true);
  execute format('set local role %I', p_role);
  begin
    execute p_sql;
  exception when others then
    execute 'reset role';
    if sqlerrm like '%' || p_msg || '%' then return; end if;
    raise exception 'WRONG ERROR for [%]: expected "%", got "%"', p_sql, p_msg, sqlerrm;
  end;
  execute 'reset role';
  raise exception 'NOT REJECTED: [%] (as %) should have failed with "%"', p_sql, p_role, p_msg;
end $$;

-- Run p_sql as p_uid (authenticated) and return the single scalar result as text.
create function testkit.as_user(p_uid uuid, p_sql text) returns text
language plpgsql as $$
declare r text;
begin
  perform set_config('request.jwt.claim.sub', p_uid::text, true);
  execute 'set local role authenticated';
  execute p_sql into r;
  execute 'reset role';
  return r;
exception when others then execute 'reset role'; raise;
end $$;

create function testkit.make_user(p_name text) returns uuid language plpgsql as $$
declare u uuid := gen_random_uuid();
begin
  insert into auth.users(id, email, raw_user_meta_data) values (u, p_name || '@test', jsonb_build_object('display_name', p_name));
  return u;
end $$;

create function testkit.assert_eq(p_what text, p_actual text, p_expected text) returns void language plpgsql as $$
begin
  if p_actual is distinct from p_expected then raise exception 'ASSERT % : expected %, got %', p_what, p_expected, p_actual; end if;
end $$;
