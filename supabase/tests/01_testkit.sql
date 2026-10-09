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

-- Create a job Apapa Port (0,0) -> Mile 12 (2600,-900). p_distance lets a test make an inconsistent job.
create function testkit.mk_job(p_code text, p_max int default 1, p_reward bigint default 500, p_cat text default 'truck',
                               p_distance numeric default 3.0, p_expired boolean default false) returns void language sql as $$
  insert into jobs(code, origin_id, destination_id, cargo_type, cargo_weight_kg, distance_km, difficulty, reward, xp_reward,
                   required_category, max_participants, expires_at)
  select p_code, o.id, d.id, 'Test', 1000, p_distance, 2, p_reward, 40, p_cat, p_max,
         case when p_expired then now() - interval '1 hour' else now() + interval '1 day' end
  from locations o, locations d where o.slug = 'lagos-apapa-port' and d.slug = 'lagos-mile12-market' $$;

-- Simulate time passing for an assignment (shifts server-side timestamps back).
create function testkit.age(p_asg uuid, p_seconds numeric) returns void language sql as $$
  update job_telemetry set last_at = last_at - make_interval(secs => p_seconds) where assignment_id = p_asg;
  update job_assignments set started_at = started_at - make_interval(secs => p_seconds) where id = p_asg $$;

-- A player legitimately drives a straight line at p_kmh, sending a sample every 5 simulated seconds.
create function testkit.drive(p_uid uuid, p_asg uuid, x0 double precision, z0 double precision,
                              x1 double precision, z1 double precision, p_kmh numeric default 80) returns void
language plpgsql as $$
declare len double precision := sqrt(power(x1-x0,2) + power(z1-z0,2)); step double precision := p_kmh / 3.6 * 5;
        n int := ceil(len / step); i int; f double precision; r text;
begin
  for i in 1..n loop
    perform testkit.age(p_asg, 5);
    f := least(i * step / len, 1);
    r := testkit.as_user(p_uid, format('select submit_telemetry(%L,%s,%s)::text', p_asg, x0 + (x1-x0)*f, z0 + (z1-z0)*f));
    if r not like '%"accepted": true%' then raise exception 'legit drive sample rejected: %', r; end if;
  end loop;
end $$;
