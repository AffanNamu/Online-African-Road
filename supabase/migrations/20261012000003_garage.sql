-- Garage: paint skins (catalogue, ownership, purchase, equip) and the service quote (what a repair and a full tank cost RIGHT NOW).
--   * a skin is cosmetic: its colours and material values are what the 3D showroom and the game paint the vehicle with; nothing here touches stats
--   * price, level and ownership are decided here; the client asks, the server answers
--   * free skins belong to every player without a row; paid skins need a purchase (one ledger row, one ownership row, one transaction)
--   * the equipped skin is stored in the vehicle's customization ("skin"); set_vehicle_customization keeps it when it replaces colour / livery / plate
--   * get_vehicle_service_quote gives the repair cost and the refuel cost from the same formulas repair_vehicle and buy_fuel charge
-- Colours, metallic and roughness of the skins are authored game-design values; the prices follow the Garage mockup.
create temp table _fn_before as select oid from pg_proc where pronamespace = 'public'::regnamespace;

create table vehicle_skins (
  id          text primary key check (id ~ '^[a-z0-9_]{1,32}$'),
  name        text not null check (char_length(name) between 1 and 40),
  official    boolean not null default true,
  price       bigint not null default 0 check (price >= 0),
  unlock_level integer not null default 1 check (unlock_level >= 1),
  base_color  text check (base_color is null or base_color ~ '^#[0-9a-fA-F]{6}$'),          -- null = the model's own livery ("ARO Original")
  accent_color text check (accent_color is null or accent_color ~ '^#[0-9a-fA-F]{6}$'),
  metallic    numeric not null default 0.3 check (metallic between 0 and 1),
  roughness   numeric not null default 0.4 check (roughness between 0 and 1),
  sort_order  integer not null default 100
);
create table player_skins (
  player_id   uuid not null references profiles(id) on delete cascade,
  skin_id     text not null references vehicle_skins(id),
  acquired_at timestamptz not null default now(),
  primary key (player_id, skin_id)
);
alter table vehicle_skins enable row level security;
alter table player_skins enable row level security;
revoke all on vehicle_skins, player_skins from anon, authenticated;

insert into vehicle_skins(id, name, official, price, unlock_level, base_color, accent_color, metallic, roughness, sort_order) values
 ('aro_original',   'ARO Original',  true,     0, 1, null,      null,      0.35, 0.40, 10),
 ('midnight_black', 'Midnight Black', true,    0, 1, '#16181d', '#c9a227', 0.55, 0.28, 20),
 ('sunset_gold',    'Sunset Gold',    true, 15000, 1, '#d9a21b', '#2a2a2a', 0.60, 0.30, 30),
 ('desert_sand',    'Desert Sand',    true, 15000, 1, '#c8b48c', '#6b4f2a', 0.30, 0.45, 40),
 ('ocean_blue',     'Ocean Blue',     true, 15000, 1, '#1d5fc5', '#e8e8e8', 0.55, 0.30, 50),
 ('forest_green',   'Forest Green',   true, 15000, 1, '#1f7a3b', '#e8c14a', 0.45, 0.35, 60),
 ('crimson_red',    'Crimson Red',    true, 15000, 1, '#b3262e', '#1a1a1a', 0.55, 0.30, 70),
 ('white',          'White',          true, 15000, 1, '#ecebe6', '#c9a227', 0.30, 0.38, 80)
on conflict (id) do nothing;

-- ---------------------------------------------------------------- customization: colour / livery / plate are replaced, the skin is kept
create or replace function set_vehicle_customization(p_vehicle uuid, p_data jsonb) returns void
language plpgsql security definer set search_path = public as $$
declare k text; v jsonb;
begin
  if auth.uid() is null then raise exception 'not_authenticated'; end if;
  perform _rate_limit('vehicle_write', 30, 60);
  if p_data is null or jsonb_typeof(p_data) <> 'object' then raise exception 'invalid_customization'; end if;
  for k, v in select * from jsonb_each(p_data) loop
    if jsonb_typeof(v) <> 'string' then raise exception 'invalid_customization'; end if;
    if k = 'color'  and (v #>> '{}') !~ '^#[0-9a-fA-F]{6}$' then raise exception 'invalid_customization'; end if;
    if k = 'livery' and (v #>> '{}') !~ '^[a-z0-9_]{1,32}$' then raise exception 'invalid_customization'; end if;
    if k = 'plate'  and (v #>> '{}') !~ '^[A-Z0-9 -]{2,10}$' then raise exception 'invalid_customization'; end if;
    if k not in ('color','livery','plate') then raise exception 'invalid_customization'; end if;     -- "skin" is only set by apply_vehicle_skin
  end loop;
  update vehicle_ownership set customization = (customization - 'color' - 'livery' - 'plate') || p_data where id = p_vehicle and player_id = auth.uid();
  if not found then raise exception 'vehicle_not_owned'; end if;
end $$;

-- ---------------------------------------------------------------- skins API
create function _skin_owned(p_player uuid, s vehicle_skins) returns boolean
language sql stable security definer set search_path = public as $$
  select s.price = 0 or exists (select 1 from player_skins where player_id = p_player and skin_id = s.id) $$;

create function list_vehicle_skins(p_vehicle uuid) returns jsonb
language plpgsql security definer set search_path = public as $$
declare v_uid uuid := auth.uid(); v_level integer; v_bal bigint; v_eq text;
begin
  if v_uid is null then raise exception 'not_authenticated'; end if;
  perform _rate_limit('vehicle_read', 120, 60);
  select customization->>'skin' into v_eq from vehicle_ownership where id = p_vehicle and player_id = v_uid;
  if not found then raise exception 'vehicle_not_owned'; end if;
  v_eq := coalesce(v_eq, 'aro_original');
  select level into v_level from profiles where id = v_uid;
  select coalesce(balance, 0) into v_bal from player_wallets where player_id = v_uid; v_bal := coalesce(v_bal, 0);
  return jsonb_build_object('vehicle_id', p_vehicle, 'equipped_id', v_eq, 'balance', v_bal, 'items', coalesce((
    select jsonb_agg(jsonb_build_object('id', s.id, 'name', s.name, 'official', s.official, 'price', s.price, 'unlock_level', s.unlock_level,
             'base_color', s.base_color, 'accent_color', s.accent_color, 'metallic', s.metallic, 'roughness', s.roughness,
             'owned', _skin_owned(v_uid, s), 'equipped', s.id = v_eq,
             'state', case when s.id = v_eq then 'equipped' when _skin_owned(v_uid, s) then 'owned' when v_level < s.unlock_level then 'level_required'
                           when v_bal < s.price then 'insufficient_funds' else 'available' end,
             'need_more', case when _skin_owned(v_uid, s) then 0 else greatest(s.price - v_bal, 0) end) order by s.sort_order, s.id)
      from vehicle_skins s), '[]'::jsonb), 'server_time', now());
end $$;

create function buy_vehicle_skin(p_skin text) returns text
language plpgsql security definer set search_path = public as $$
declare v_uid uuid := auth.uid(); s vehicle_skins; v_level integer;
begin
  if v_uid is null then raise exception 'not_authenticated'; end if;
  select * into s from vehicle_skins where id = p_skin;
  if not found then raise exception 'unknown_skin'; end if;
  if s.price <= 0 then raise exception 'not_for_sale'; end if;
  perform pg_advisory_xact_lock(hashtextextended('buy_skin:' || v_uid::text || ':' || s.id, 0));
  if exists (select 1 from player_skins where player_id = v_uid and skin_id = s.id) then raise exception 'skin_owned'; end if;
  select level into v_level from profiles where id = v_uid;
  if coalesce(v_level, 1) < s.unlock_level then raise exception 'skin_locked'; end if;
  begin
    perform _apply_transaction(v_uid, 'SKIN_PURCHASE', -s.price, 'skin:' || gen_random_uuid(), 'vehicle_skin', null);
  exception when check_violation then raise exception 'insufficient_funds';
  end;
  insert into player_skins(player_id, skin_id) values (v_uid, s.id);
  return s.id;
end $$;

create function apply_vehicle_skin(p_vehicle uuid, p_skin text) returns text
language plpgsql security definer set search_path = public as $$
declare v_uid uuid := auth.uid(); s vehicle_skins;
begin
  if v_uid is null then raise exception 'not_authenticated'; end if;
  perform _rate_limit('vehicle_write', 30, 60);
  if not exists (select 1 from vehicle_ownership where id = p_vehicle and player_id = v_uid) then raise exception 'vehicle_not_owned'; end if;
  select * into s from vehicle_skins where id = p_skin;
  if not found then raise exception 'unknown_skin'; end if;
  if not _skin_owned(v_uid, s) then raise exception 'skin_not_owned'; end if;
  update vehicle_ownership set customization = case when s.id = 'aro_original' then customization - 'skin' else jsonb_set(customization, '{skin}', to_jsonb(s.id)) end
   where id = p_vehicle and player_id = v_uid;
  return s.id;
end $$;

-- ---------------------------------------------------------------- service quote (Maintenance)
create function get_vehicle_service_quote(p_vehicle uuid) returns jsonb
language plpgsql security definer set search_path = public as $$
declare v_uid uuid := auth.uid(); v_veh vehicle_ownership; v_cap numeric; v_price numeric := cfg('fuel_price_per_l')::numeric; v_bal bigint;
        v_repair bigint; v_missing numeric; v_refuel bigint;
begin
  if v_uid is null then raise exception 'not_authenticated'; end if;
  perform _rate_limit('vehicle_read', 120, 60);
  select * into v_veh from vehicle_ownership where id = p_vehicle and player_id = v_uid;
  if not found then raise exception 'vehicle_not_owned'; end if;
  select fuel_capacity_l into v_cap from vehicle_definitions where id = v_veh.definition_id;
  select coalesce(balance, 0) into v_bal from player_wallets where player_id = v_uid; v_bal := coalesce(v_bal, 0);
  v_repair := case when v_veh.damage_pct > 0 then ceil(v_veh.damage_pct * 10) else 0 end;             -- the formula repair_vehicle charges
  v_missing := greatest(v_cap - v_veh.fuel_l, 0); v_refuel := case when v_missing > 0 then ceil(v_missing * v_price) else 0 end;   -- the price buy_fuel charges
  return jsonb_build_object('vehicle_id', p_vehicle, 'condition_pct', round(100 - v_veh.damage_pct, 1), 'damage_pct', v_veh.damage_pct,
    'fuel_l', v_veh.fuel_l, 'fuel_capacity_l', v_cap, 'fuel_price_per_l', v_price, 'balance', v_bal,
    'repair_needed', v_veh.damage_pct > 0, 'repair_cost', v_repair, 'can_repair', v_veh.damage_pct > 0 and v_bal >= v_repair,
    'refuel_liters', v_missing, 'refuel_cost', v_refuel, 'can_refuel', v_missing > 0 and v_bal >= v_refuel, 'server_time', now());
end $$;

-- ---------------------------------------------------------------- lock-down
do $$ declare r record;
begin
  for r in select p.oid::regprocedure as sig, p.proname from pg_proc p
            where p.pronamespace = 'public'::regnamespace and p.oid not in (select oid from _fn_before) loop
    execute format('revoke execute on function %s from public, anon, authenticated', r.sig);
    if r.proname in ('list_vehicle_skins','buy_vehicle_skin','apply_vehicle_skin','get_vehicle_service_quote') then
      execute format('grant execute on function %s to authenticated', r.sig);
    end if;
  end loop;
end $$;
