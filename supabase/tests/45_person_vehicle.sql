\set ON_ERROR_STOP on
-- The public player card carries the vehicle for people who may see presence in full (self, friends, convoy mates) and never for anyone else.
create function pg_temp.j(p_uid uuid, p_sql text) returns jsonb language sql as $$ select testkit.as_user(p_uid, p_sql)::jsonb $$;
create temp table fx(k text primary key, v text);
insert into fx select k, testkit.make_user(n)::text from (values ('A','PvA'),('B','PvB'),('C','PvC'),('D','PvD'),('E','PvE')) t(k, n);
create function pg_temp.u(k text) returns uuid language sql as $$ select v::uuid from fx where fx.k = $1 $$;
create function pg_temp.card(viewer text, other text) returns jsonb language sql as $$ select pg_temp.j(pg_temp.u($1), format('select get_public_profile(%L)::text', pg_temp.u($2))) $$;

-- A and B are friends; D has met A in a convoy (a recorded encounter, no longer sharing one); E is a stranger; C shares A's convoy
insert into fx select 'req', pg_temp.j(pg_temp.u('A'), format('select send_friend_request(%L)::text', pg_temp.u('B')))->>'request_id';
select testkit.as_user(pg_temp.u('B'), format('select respond_friend_request(%L, true)', (select v from fx where k = 'req')));
insert into player_encounters(player_id, other_id, last_at, times) values (pg_temp.u('A'), pg_temp.u('D'), now(), 1);
select testkit.assert_eq('everyone starts with the starter truck', (select count(*)::text from vehicle_ownership where player_id = pg_temp.u('B')), '1');

-- ===== a friend sees the vehicle, by name and by catalogue id
select testkit.assert_eq('a friend sees the vehicle name', pg_temp.card('A', 'B')->>'vehicle', (select d.name from vehicle_definitions d where d.id = 'truck_light_01'));
select testkit.assert_eq('and its catalogue id', pg_temp.card('A', 'B')->>'vehicle_id', 'truck_light_01');
select testkit.assert_eq('a player sees their own', pg_temp.card('A', 'A')->>'vehicle_id', 'truck_light_01');
select testkit.assert_eq('the friends list carries it too', pg_temp.j(pg_temp.u('A'), 'select list_friends()::text')->'items'->0->>'vehicle_id', 'truck_light_01');

-- ===== strangers and passers-by do not
select testkit.assert_eq('a stranger sees no vehicle', (pg_temp.card('E', 'B') ? 'vehicle')::text, 'false');
select testkit.assert_eq('nor its id', (pg_temp.card('E', 'B') ? 'vehicle_id')::text, 'false');
select testkit.assert_eq('someone met in passing sees none', (pg_temp.card('A', 'D') ? 'vehicle')::text, 'false');
select testkit.assert_eq('find_player (exact handle) shows none to a stranger', (pg_temp.j(pg_temp.u('E'), format('select find_player(%L)::text', (select username from profiles where id = pg_temp.u('B')))) ? 'vehicle')::text, 'false');
select testkit.assert_eq('a stranger sees none of A either', (pg_temp.card('E', 'A') ? 'vehicle_id')::text, 'false');

-- ===== the selected vehicle is the one shown
insert into vehicle_ownership(player_id, definition_id, fuel_l) values (pg_temp.u('B'), 'bus_city_01', 100);
select testkit.assert_eq('with two vehicles the oldest is shown until one is selected', pg_temp.card('A', 'B')->>'vehicle_id', 'truck_light_01');
update profiles set selected_vehicle_id = (select id from vehicle_ownership where player_id = pg_temp.u('B') and definition_id = 'bus_city_01') where id = pg_temp.u('B');
select testkit.assert_eq('the selected vehicle is shown once chosen', pg_temp.card('A', 'B')->>'vehicle_id', 'bus_city_01');
select testkit.assert_eq('and its name', pg_temp.card('A', 'B')->>'vehicle', (select name from vehicle_definitions where id = 'bus_city_01'));

-- ===== a convoy mate sees it
insert into fx select 'cv', testkit.as_user(pg_temp.u('A'), $$select create_convoy('Pv Run')$$);
select testkit.as_user(pg_temp.u('C'), format('select join_convoy(%L)', (select v::uuid from fx where k = 'cv')));
select testkit.assert_eq('someone in a convoy with A still does not see B (not a friend, no shared convoy)', (pg_temp.card('C', 'B') ? 'vehicle_id')::text, 'false');
select testkit.assert_eq('a convoy mate sees the other mate vehicle', pg_temp.card('C', 'A')->>'vehicle_id', 'truck_light_01');
select testkit.assert_eq('the convoy member list carries it', (select m->>'vehicle_id' from jsonb_array_elements(pg_temp.j(pg_temp.u('A'), 'select get_my_convoy()::text')->'convoy'->'members') m where m->>'player_id' = pg_temp.u('C')::text), 'truck_light_01');

-- ===== no private data came along
select testkit.assert_eq('no coins on the card', (pg_temp.card('A', 'B') ? 'coins')::text, 'false');
select testkit.assert_eq('no e-mail on the card', (pg_temp.card('A', 'B') ? 'email')::text, 'false');
select 'PERSON CARD TESTS PASSED' as result;
