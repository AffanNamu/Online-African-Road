\set ON_ERROR_STOP on
-- Garage: skins (catalogue, states, purchase, equip), customization keeps the skin, the service quote agrees with what repair and refuel really charge.
create function pg_temp.j(p_uid uuid, p_sql text) returns jsonb language sql as $$ select testkit.as_user(p_uid, p_sql)::jsonb $$;
create temp table fx(k text primary key, v text);
insert into fx select k, testkit.make_user(n)::text from (values ('A','GaA'),('B','GaB'),('C','GaC')) t(k, n);
create function pg_temp.u(k text) returns uuid language sql as $$ select v::uuid from fx where fx.k = $1 $$;
create function pg_temp.veh(k text) returns uuid language sql as $$ select id from vehicle_ownership where player_id = pg_temp.u($1) order by acquired_at limit 1 $$;
create function pg_temp.fund(k text, amt bigint) returns void language sql as $$ select _apply_transaction(pg_temp.u($1), 'BONUS', $2, 'fund:' || gen_random_uuid()) $$;
create function pg_temp.bal(k text) returns bigint language sql as $$ select balance from player_wallets where player_id = pg_temp.u($1) $$;
create function pg_temp.skins(k text) returns jsonb language sql as $$ select pg_temp.j(pg_temp.u($1), format('select list_vehicle_skins(%L)::text', pg_temp.veh($1))) $$;
create function pg_temp.skin(k text, p_id text) returns jsonb language sql as $$ select e from jsonb_array_elements(pg_temp.skins($1)->'items') e where e->>'id' = $2 $$;
create function pg_temp.st(k text, p_id text) returns text language sql as $$ select pg_temp.skin($1, $2)->>'state' $$;
create function pg_temp.cust(k text) returns jsonb language sql as $$ select customization from vehicle_ownership where id = pg_temp.veh($1) $$;

-- ===== the skin catalogue
select testkit.assert_eq('eight skins', jsonb_array_length(pg_temp.skins('A')->'items')::text, '8');
select testkit.assert_eq('the first is the vehicle own livery', pg_temp.skins('A')->'items'->0->>'id', 'aro_original');
select testkit.assert_eq('the default skin has no colour override', pg_temp.skin('A', 'aro_original')->>'base_color', null);
select testkit.assert_eq('nothing equipped means ARO Original', pg_temp.skins('A')->>'equipped_id', 'aro_original');
select testkit.assert_eq('ARO Original is shown as equipped', pg_temp.st('A', 'aro_original'), 'equipped');
select testkit.assert_eq('a free skin is owned by everyone', pg_temp.st('A', 'midnight_black'), 'owned');
select testkit.assert_eq('a paid skin is available to someone with money... ', pg_temp.st('A', 'sunset_gold'), 'insufficient_funds');
select testkit.assert_eq('and says how much is missing', pg_temp.skin('A', 'sunset_gold')->>'need_more', (15000 - pg_temp.bal('A'))::text);
select testkit.assert_eq('colours come as hex', pg_temp.skin('A', 'ocean_blue')->>'base_color', '#1d5fc5');
select testkit.assert_eq('material values come with the skin', pg_temp.skin('A', 'ocean_blue')->>'metallic', '0.55');
select testkit.expect_error('authenticated', pg_temp.u('B'), format($$select list_vehicle_skins(%L)$$, pg_temp.veh('A')), 'vehicle_not_owned');
select testkit.expect_error('anon', null, format($$select list_vehicle_skins(%L)$$, pg_temp.veh('A')), 'permission denied');
select testkit.expect_error('authenticated', pg_temp.u('A'), 'select * from vehicle_skins', 'permission denied');
select testkit.expect_error('authenticated', pg_temp.u('A'), 'select * from player_skins', 'permission denied');
select testkit.expect_error('authenticated', pg_temp.u('A'), $$update vehicle_skins set price = 0$$, 'permission denied');
select testkit.expect_error('authenticated', pg_temp.u('A'), format($$insert into player_skins(player_id, skin_id) values (%L, 'white')$$, pg_temp.u('A')), 'permission denied');

-- ===== buying a skin
select testkit.expect_error('authenticated', pg_temp.u('A'), $$select buy_vehicle_skin('sunset_gold')$$, 'insufficient_funds');
select testkit.assert_eq('a refused purchase costs nothing', (select count(*)::text from transactions where player_id = pg_temp.u('A') and kind = 'SKIN_PURCHASE'), '0');
select testkit.expect_error('authenticated', pg_temp.u('A'), $$select buy_vehicle_skin('nope')$$, 'unknown_skin');
select testkit.expect_error('authenticated', pg_temp.u('A'), $$select buy_vehicle_skin('midnight_black')$$, 'not_for_sale');
select testkit.expect_error('authenticated', pg_temp.u('A'), $$select buy_vehicle_skin('aro_original')$$, 'not_for_sale');
select testkit.expect_error('anon', null, $$select buy_vehicle_skin('sunset_gold')$$, 'permission denied');
select pg_temp.fund('A', 100000);
select testkit.assert_eq('with money the paid skin is available', pg_temp.st('A', 'sunset_gold'), 'available');
do $$ declare before bigint := pg_temp.bal('A'); begin
  perform testkit.assert_eq('buy returns the skin', testkit.as_user(pg_temp.u('A'), $q$select buy_vehicle_skin('sunset_gold')$q$), 'sunset_gold');
  perform testkit.assert_eq('debited exactly the server price', pg_temp.bal('A')::text, (before - 15000)::text);
end $$;
select testkit.assert_eq('one ledger row of the right kind', (select count(*)::text from transactions where player_id = pg_temp.u('A') and kind = 'SKIN_PURCHASE' and amount = -15000), '1');
select testkit.assert_eq('the skin is now owned', pg_temp.st('A', 'sunset_gold'), 'owned');
select testkit.expect_error('authenticated', pg_temp.u('A'), $$select buy_vehicle_skin('sunset_gold')$$, 'skin_owned');
select testkit.assert_eq('a second click does not charge again', pg_temp.bal('A')::text, (105000 - 15000)::text);
select testkit.assert_eq('ownership is per player', pg_temp.st('B', 'sunset_gold'), 'insufficient_funds');
update vehicle_skins set unlock_level = 6 where id = 'crimson_red';
select testkit.assert_eq('a level-locked skin says so (even for someone rich)', pg_temp.st('A', 'crimson_red'), 'level_required');
select testkit.expect_error('authenticated', pg_temp.u('A'), $$select buy_vehicle_skin('crimson_red')$$, 'skin_locked');
select testkit.assert_eq('a locked purchase costs nothing', pg_temp.bal('A')::text, '90000');
update vehicle_skins set unlock_level = 1 where id = 'crimson_red';
select testkit.assert_eq('poor and locked: the level comes first', (select e->>'state' from (select jsonb_array_elements(pg_temp.skins('C')->'items') e) t where e->>'id' = 'white'), 'insufficient_funds');
update vehicle_skins set unlock_level = 6 where id = 'white';
select testkit.assert_eq('poor AND too low a level: level first', pg_temp.st('C', 'white'), 'level_required');
update vehicle_skins set unlock_level = 1 where id = 'white';

-- ===== equipping
select testkit.expect_error('authenticated', pg_temp.u('A'), format($$select apply_vehicle_skin(%L, 'ocean_blue')$$, pg_temp.veh('A')), 'skin_not_owned');
select testkit.expect_error('authenticated', pg_temp.u('A'), format($$select apply_vehicle_skin(%L, 'nope')$$, pg_temp.veh('A')), 'unknown_skin');
select testkit.expect_error('authenticated', pg_temp.u('B'), format($$select apply_vehicle_skin(%L, 'midnight_black')$$, pg_temp.veh('A')), 'vehicle_not_owned');
select testkit.expect_error('anon', null, format($$select apply_vehicle_skin(%L, 'midnight_black')$$, pg_temp.veh('A')), 'permission denied');
select testkit.assert_eq('a free skin can be equipped', testkit.as_user(pg_temp.u('A'), format($$select apply_vehicle_skin(%L, 'midnight_black')$$, pg_temp.veh('A'))), 'midnight_black');
select testkit.assert_eq('it is stored on the vehicle', pg_temp.cust('A')->>'skin', 'midnight_black');
select testkit.assert_eq('and listed as equipped', pg_temp.st('A', 'midnight_black'), 'equipped');
select testkit.assert_eq('the previous skin is no longer equipped', pg_temp.st('A', 'aro_original'), 'owned');
select testkit.assert_eq('a bought skin can be equipped', testkit.as_user(pg_temp.u('A'), format($$select apply_vehicle_skin(%L, 'sunset_gold')$$, pg_temp.veh('A'))), 'sunset_gold');
select testkit.assert_eq('only one skin is equipped', (select count(*)::text from jsonb_array_elements(pg_temp.skins('A')->'items') e where (e->>'equipped')::boolean), '1');
select testkit.assert_eq('equipping costs nothing', pg_temp.bal('A')::text, '90000');
select testkit.assert_eq('another player cannot use my purchase', (select (e->>'owned') from jsonb_array_elements(pg_temp.skins('B')->'items') e where e->>'id' = 'sunset_gold'), 'false');
select testkit.expect_error('authenticated', pg_temp.u('B'), format($$select apply_vehicle_skin(%L, 'sunset_gold')$$, pg_temp.veh('B')), 'skin_not_owned');
-- customization keeps the skin when colour / livery / plate are replaced, and clients cannot write the skin directly
select testkit.as_user(pg_temp.u('A'), format($$select set_vehicle_customization(%L, '{"color":"#112233","plate":"ARO 9"}')::text$$, pg_temp.veh('A')));
select testkit.assert_eq('the skin survives a customization', pg_temp.cust('A')->>'skin', 'sunset_gold');
select testkit.assert_eq('and the colour was set', pg_temp.cust('A')->>'color', '#112233');
select testkit.as_user(pg_temp.u('A'), format($$select set_vehicle_customization(%L, '{"plate":"ARO 10"}')::text$$, pg_temp.veh('A')));
select testkit.assert_eq('colour / livery / plate are replaced, not accumulated', pg_temp.cust('A')->>'color', null);
select testkit.expect_error('authenticated', pg_temp.u('A'), format($$select set_vehicle_customization(%L, '{"skin":"ocean_blue"}')$$, pg_temp.veh('A')), 'invalid_customization');
select testkit.assert_eq('ARO Original removes the skin key', (select (testkit.as_user(pg_temp.u('A'), format($$select apply_vehicle_skin(%L, 'aro_original')$$, pg_temp.veh('A'))) = 'aro_original')::text), 'true');
select testkit.assert_eq('the key is gone (not stored as a skin)', (pg_temp.cust('A') ? 'skin')::text, 'false');
select testkit.assert_eq('the listing agrees', pg_temp.skins('A')->>'equipped_id', 'aro_original');

-- ===== the service quote agrees with what is really charged
select testkit.expect_error('authenticated', pg_temp.u('B'), format($$select get_vehicle_service_quote(%L)$$, pg_temp.veh('A')), 'vehicle_not_owned');
select testkit.expect_error('anon', null, format($$select get_vehicle_service_quote(%L)$$, pg_temp.veh('A')), 'permission denied');
update vehicle_ownership set damage_pct = 20, fuel_l = 20 where id = pg_temp.veh('A');
create function pg_temp.q(k text) returns jsonb language sql as $$ select pg_temp.j(pg_temp.u($1), format('select get_vehicle_service_quote(%L)::text', pg_temp.veh($1))) $$;
select testkit.assert_eq('quote: repair is needed', pg_temp.q('A')->>'repair_needed', 'true');
select testkit.assert_eq('quote: repair cost is 10 per percent of damage', pg_temp.q('A')->>'repair_cost', '200');
select testkit.assert_eq('quote: litres missing', pg_temp.q('A')->>'refuel_liters', '100');
select testkit.assert_eq('quote: refuel cost', pg_temp.q('A')->>'refuel_cost', '200');
select testkit.assert_eq('quote: condition', pg_temp.q('A')->>'condition_pct', '80.0');
select testkit.assert_eq('quote: can afford both', (pg_temp.q('A')->>'can_repair') || '/' || (pg_temp.q('A')->>'can_refuel'), 'true/true');
do $$ declare qr bigint := (pg_temp.q('A')->>'repair_cost')::bigint; qf bigint := (pg_temp.q('A')->>'refuel_cost')::bigint; b0 bigint := pg_temp.bal('A'); begin
  perform testkit.as_user(pg_temp.u('A'), format($q$select repair_vehicle(%L)::text$q$, pg_temp.veh('A')));
  perform testkit.assert_eq('repair really charged the quoted price', pg_temp.bal('A')::text, (b0 - qr)::text);
  perform testkit.as_user(pg_temp.u('A'), format($q$select buy_fuel(%L, 9999)::text$q$, pg_temp.veh('A')));
  perform testkit.assert_eq('refuel really charged the quoted price', pg_temp.bal('A')::text, (b0 - qr - qf)::text);
end $$;
select testkit.assert_eq('after both: nothing to repair', pg_temp.q('A')->>'repair_needed', 'false');
select testkit.assert_eq('after both: repair costs nothing', pg_temp.q('A')->>'repair_cost', '0');
select testkit.assert_eq('after both: tank full', pg_temp.q('A')->>'refuel_cost', '0');
select testkit.assert_eq('after both: nothing to refuel', pg_temp.q('A')->>'can_refuel', 'false');
update vehicle_ownership set damage_pct = 50, fuel_l = 0 where id = pg_temp.veh('B');
select testkit.assert_eq('a poor player is told they cannot afford the repair', (pg_temp.q('B')->>'can_repair'), (pg_temp.bal('B') >= 500)::text);
update player_wallets set balance = 10 where player_id = pg_temp.u('B');
select testkit.assert_eq('cannot repair with 10 coins', pg_temp.q('B')->>'can_repair', 'false');
select testkit.assert_eq('cannot refuel a whole tank with 10 coins', pg_temp.q('B')->>'can_refuel', 'false');
select testkit.assert_eq('the price per litre is the configured one', (pg_temp.q('B')->>'fuel_price_per_l')::numeric::text, cfg('fuel_price_per_l')::numeric::text);

-- ===== schema guards
do $$ begin
  begin insert into vehicle_skins(id, name, base_color) values ('bad_1', 'Bad', 'red'); raise exception 'colour accepted'; exception when check_violation then null; end;
  begin insert into vehicle_skins(id, name, price) values ('bad_2', 'Bad', -1); raise exception 'negative price accepted'; exception when check_violation then null; end;
  begin insert into vehicle_skins(id, name, metallic) values ('bad_3', 'Bad', 1.5); raise exception 'metallic accepted'; exception when check_violation then null; end;
  begin insert into vehicle_skins(id, name) values ('Bad Id!', 'Bad'); raise exception 'id accepted'; exception when check_violation then null; end;
  begin insert into player_skins(player_id, skin_id) values (pg_temp.u('A'), 'sunset_gold'); raise exception 'second ownership row accepted'; exception when unique_violation then null; end;
end $$;
select testkit.expect_error('authenticated', pg_temp.u('A'), 'select _skin_owned(null, null)', 'permission denied');
select 'GARAGE TESTS PASSED' as result;
