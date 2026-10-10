-- Run against the LIVE database after applying. Raises on any violation; prints a summary otherwise.
do $$ declare bad text; n int; begin
  select string_agg(c.relname, ',') into bad from pg_class c join pg_namespace s on s.oid = c.relnamespace
   where s.nspname = 'public' and c.relkind = 'r' and not c.relrowsecurity;
  if bad is not null then raise exception 'RLS disabled on: %', bad; end if;

  select string_agg(table_name, ',') into bad from information_schema.role_table_grants
   where table_schema = 'public' and grantee = 'anon';
  if bad is not null then raise exception 'anon has table privileges on: %', bad; end if;

  select string_agg(distinct table_name, ',') into bad from information_schema.role_table_grants
   where table_schema = 'public' and grantee = 'authenticated' and privilege_type in ('INSERT','UPDATE','DELETE','TRUNCATE');
  if bad is not null then raise exception 'authenticated can write tables directly: %', bad; end if;

  select string_agg(p.oid::regprocedure::text, ', ') into bad from pg_proc p join pg_namespace s on s.oid = p.pronamespace
   where s.nspname = 'public' and has_function_privilege('authenticated', p.oid, 'execute')
     and p.proname not in ('set_display_name','accept_job','start_job','submit_telemetry','complete_job','abandon_job',
                           'buy_fuel','repair_vehicle','create_convoy','join_convoy','leave_convoy',
                           'get_convoy_session_code','set_convoy_session_code',
                           'buy_vehicle','start_bus_run','submit_bus_telemetry','serve_stop','abandon_bus_run');
  if bad is not null then raise exception 'unexpected client-executable functions: %', bad; end if;

  select string_agg(p.proname, ',') into bad from pg_proc p join pg_namespace s on s.oid = p.pronamespace
   where s.nspname = 'public' and p.prosecdef and not coalesce(p.proconfig::text like '%search_path=public%', false);
  if bad is not null then raise exception 'SECURITY DEFINER without pinned search_path: %', bad; end if;

  select count(*) into n from vehicle_definitions; if n < 3 then raise exception 'seed missing: % vehicle definitions', n; end if;
  select count(*) into n from bus_route_stops;     if n < 3 then raise exception 'seed missing: bus route stops'; end if;
  raise notice 'live schema OK: RLS everywhere, anon has nothing, clients cannot write tables, only intended RPCs executable';
end $$;
