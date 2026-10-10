-- A player is doing at most one thing at a time: a freight job OR a bus run. start_bus_run already refuses while a job
-- is active; this makes accept_job refuse while a bus run is active, so one physical drive can never be reported twice
-- (once as a truck delivery, once as a bus route) by a modified client.
create or replace function accept_job(p_job uuid, p_vehicle uuid) returns uuid
language plpgsql security definer set search_path = public as $$
declare
  v_uid uuid := auth.uid(); v_job jobs; v_veh vehicle_ownership; v_cat text; v_id uuid;
begin
  if v_uid is null then raise exception 'not_authenticated'; end if;
  if exists (select 1 from bus_runs where player_id = v_uid and status = 'active') then raise exception 'bus_run_active'; end if;
  select * into v_job from jobs where id = p_job for update;
  if not found then raise exception 'job_not_found'; end if;
  if v_job.status <> 'open' or v_job.expires_at <= now() then raise exception 'job_unavailable'; end if;
  select * into v_veh from vehicle_ownership where id = p_vehicle and player_id = v_uid;
  if not found then raise exception 'vehicle_not_owned'; end if;
  select category into v_cat from vehicle_definitions where id = v_veh.definition_id;
  if v_cat <> v_job.required_category then raise exception 'wrong_vehicle_category'; end if;
  if v_veh.damage_pct >= 100 then raise exception 'vehicle_destroyed'; end if;
  if (select count(*) from job_assignments where job_id = p_job
        and status in ('accepted','in_progress','completed')) >= v_job.max_participants then
    raise exception 'job_full';
  end if;
  insert into job_assignments(job_id, player_id, vehicle_id) values (p_job, v_uid, p_vehicle)
    returning id into v_id;
  return v_id;
exception when unique_violation then raise exception 'already_assigned';
end $$;
