-- Refuel at a pump (owner decision of 2026-11-14: docs/DECISIONS.md, docs/FUNCTIONAL_PLACES_PROPOSAL.md P1).
--   * buy_fuel takes an optional third argument, the game's current fuel reading. The server uses it only when it is LOWER than the
--     server's own level: claiming less fuel only means paying for more litres. A higher, missing or NaN reading changes nothing;
--     a negative one is refused.
--   * The server's own level counts what an activity in progress on this vehicle has already burned. complete_job and serve_stop
--     deduct the WHOLE verified distance when the activity completes, so the litres bought mid-activity are stored on top of that
--     pending deduction: the tank ends the activity at (level at the pump + litres bought - fuel burned after the pump), never
--     deducted twice. The answer's fuel_l is the level the game should show right after the pump.
--   * An activity that ends without completing (abandoned or failed) deducts nothing, as before; the tank is then capped at its
--     capacity, so a refuel mid-activity never leaves more than a full tank.
--   * Price (ceil(litres x 2), the configured fuel_price_per_l), ownership, the row lock, the ledger row and the answers
--     'invalid_amount', 'vehicle_not_owned', 'tank_full', 'insufficient_funds' are those of the original function.
--   * The two-argument call of the Garage keeps working: the reading defaults to null.
-- Tests: supabase/tests/54_pump_refuel.sql; the mutants of supabase/tests/mutation.py now break THIS definition.

drop function buy_fuel(uuid, numeric);

create function buy_fuel(p_vehicle uuid, p_liters numeric, p_current_l numeric default null) returns jsonb
language plpgsql security definer set search_path = public as $$
declare v_uid uuid := auth.uid(); v_veh vehicle_ownership; v_def vehicle_definitions; v_cost bigint;
  v_km numeric; v_pending numeric := 0; v_level numeric;
begin
  if p_liters <= 0 then raise exception 'invalid_amount'; end if;
  if p_current_l < 0 then raise exception 'invalid_reading'; end if;
  select * into v_veh from vehicle_ownership where id = p_vehicle and player_id = v_uid for update;
  if not found then raise exception 'vehicle_not_owned'; end if;
  select * into v_def from vehicle_definitions where id = v_veh.definition_id;
  -- fuel burned by an activity in progress, which it deducts when it completes (the burn rates of complete_job and serve_stop)
  select t.verified_km into v_km from job_assignments a join job_telemetry t on t.assignment_id = a.id
   where a.vehicle_id = p_vehicle and a.status = 'in_progress';
  if found then v_pending := v_pending + v_km * coalesce((v_def.stats->>'fuelBurnLPerKm')::numeric, 0.25); end if;
  select r.verified_km into v_km from bus_runs r where r.vehicle_id = p_vehicle and r.status = 'active';
  if found then v_pending := v_pending + v_km * coalesce((v_def.stats->>'fuelBurnLPerKm')::numeric, 0.28); end if;
  v_level := greatest(0, v_veh.fuel_l - v_pending);
  if p_current_l < v_level then v_level := p_current_l; end if;
  p_liters := least(p_liters, v_def.fuel_capacity_l - v_level);
  if p_liters <= 0 then raise exception 'tank_full'; end if;
  v_cost := ceil(p_liters * 2);
  if not _apply_transaction(v_uid, 'FUEL_PURCHASE', -v_cost, 'fuel:' || gen_random_uuid(), 'vehicle', p_vehicle) then
    raise exception 'duplicate';
  end if;
  update vehicle_ownership set fuel_l = v_level + p_liters + v_pending where id = p_vehicle;
  return jsonb_build_object('liters', p_liters, 'cost', v_cost, 'fuel_l', v_level + p_liters);
exception when check_violation then raise exception 'insufficient_funds';
end $$;

-- An activity that ends without completing deducts nothing: cap the tank at its capacity (a no-op unless a pump refuelled it mid-activity).
create function _activity_ended_cap_fuel() returns trigger
language plpgsql security definer set search_path = public as $$
begin
  if new.status::text in ('abandoned', 'failed') and old.status::text is distinct from new.status::text then
    update vehicle_ownership v set fuel_l = d.fuel_capacity_l
      from vehicle_definitions d where v.id = new.vehicle_id and d.id = v.definition_id and v.fuel_l > d.fuel_capacity_l;
  end if;
  return null;
end $$;
create trigger job_assignments_cap_fuel after update of status on job_assignments
  for each row execute function _activity_ended_cap_fuel();
create trigger bus_runs_cap_fuel after update of status on bus_runs
  for each row execute function _activity_ended_cap_fuel();

revoke execute on function buy_fuel(uuid, numeric, numeric), _activity_ended_cap_fuel() from public, anon, authenticated;
grant execute on function buy_fuel(uuid, numeric, numeric) to authenticated;
