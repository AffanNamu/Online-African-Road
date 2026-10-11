-- Leaderboards screen: two additions next to get_leaderboard (which is unchanged).
--   * get_friends_leaderboard(metric, period): the caller and their accepted friends ranked among themselves by the same server records
--     (profiles for all time; completed jobs / the ledger for this week). Friends with nothing yet are listed at the bottom, so "you vs your friends" is complete.
--   * search_leaderboard(metric, period, query, limit): ranked players whose name or username contains the text, each with their GLOBAL rank on that board.
--     It only ever returns players the board itself lists (value > 0), so it exposes nothing the leaderboard does not; text is matched with strpos, so % and _ are plain characters.
-- Nothing is accepted from the client except the metric, the period and the text: ranks and values are computed here.
create temp table _fn_before as select oid from pg_proc where pronamespace = 'public'::regnamespace;

create function get_friends_leaderboard(p_metric text, p_period text default 'all') returns jsonb
language plpgsql security definer set search_path = public as $$
declare v_uid uuid := auth.uid(); v_week timestamptz := date_trunc('week', now()); v_items jsonb; v_me jsonb; v_total integer;
begin
  if v_uid is null then raise exception 'not_authenticated'; end if;
  perform _rate_limit('leaderboard', 60, 60);
  if p_metric not in ('xp','distance','jobs','earnings') then raise exception 'invalid_metric'; end if;
  if p_period not in ('all','weekly') then raise exception 'invalid_period'; end if;
  if p_period = 'weekly' and p_metric = 'xp' then raise exception 'unsupported_period'; end if;

  create temp table if not exists _flb(player_id uuid, value numeric) on commit drop;
  delete from _flb;
  insert into _flb
  select i.id,
         case when p_period = 'all' then
                (select (case p_metric when 'xp' then pr.experience when 'distance' then pr.distance_km when 'jobs' then pr.jobs_completed else pr.lifetime_earnings end)::numeric from profiles pr where pr.id = i.id)
              when p_metric = 'jobs' then
                (select count(*)::numeric from job_assignments a where a.player_id = i.id and a.status = 'completed' and a.completed_at >= v_week)
              when p_metric = 'distance' then
                (select coalesce(sum(coalesce(a.reported_distance_km, 0)), 0)::numeric from job_assignments a where a.player_id = i.id and a.status = 'completed' and a.completed_at >= v_week)
              else
                (select coalesce(sum(t.amount), 0)::numeric from transactions t where t.player_id = i.id and t.kind in ('JOB_REWARD','BONUS') and t.amount > 0 and t.created_at >= v_week)
         end
    from (select v_uid as id
          union
          select case when f.requester_id = v_uid then f.addressee_id else f.requester_id end
            from friendships f where f.status = 'accepted' and v_uid in (f.requester_id, f.addressee_id)) i;

  select count(*)::int into v_total from _flb;
  select coalesce(jsonb_agg(jsonb_build_object('rank', x.rank, 'player_id', x.player_id, 'display_name', pr.display_name, 'username', pr.username,
           'avatar_id', pr.avatar_id, 'level', pr.level, 'country_code', pr.country_code, 'value', x.value) order by x.rank, x.player_id), '[]'::jsonb) into v_items
    from (select player_id, value, rank() over (order by value desc) as rank from _flb) x join profiles pr on pr.id = x.player_id;
  select jsonb_build_object('rank', r.rank, 'value', r.value) into v_me
    from (select player_id, value, rank() over (order by value desc) as rank from _flb) r where r.player_id = v_uid;
  return jsonb_build_object('metric', p_metric, 'period', p_period, 'scope', 'friends', 'total', v_total, 'limit', v_total, 'offset', 0,
                            'items', v_items, 'me', v_me, 'server_time', now());
end $$;

create function search_leaderboard(p_metric text, p_period text default 'all', p_query text default '', p_limit integer default 10) returns jsonb
language plpgsql security definer set search_path = public as $$
declare v_uid uuid := auth.uid(); v_week timestamptz := date_trunc('week', now()); v_q text := left(lower(trim(coalesce(p_query, ''))), 24);
        v_lim integer := least(greatest(coalesce(p_limit, 10), 1), 25); v_col text; v_items jsonb; v_total integer;
begin
  if v_uid is null then raise exception 'not_authenticated'; end if;
  perform _rate_limit('leaderboard_search', 40, 60);
  if p_metric not in ('xp','distance','jobs','earnings') then raise exception 'invalid_metric'; end if;
  if p_period not in ('all','weekly') then raise exception 'invalid_period'; end if;
  if p_period = 'weekly' and p_metric = 'xp' then raise exception 'unsupported_period'; end if;
  if char_length(v_q) < 2 then raise exception 'query_too_short'; end if;

  if p_period = 'all' then
    v_col := case p_metric when 'xp' then 'experience' when 'distance' then 'distance_km' when 'jobs' then 'jobs_completed' else 'lifetime_earnings' end;
    execute format($q$
      select coalesce(jsonb_agg(jsonb_build_object('rank', x.rank, 'player_id', x.id, 'display_name', x.display_name, 'username', x.username,
               'avatar_id', x.avatar_id, 'level', x.level, 'country_code', x.country_code, 'value', x.value) order by x.rank, x.id), '[]'::jsonb)
        from (select p.id, p.display_name, p.username, p.avatar_id, p.level, p.country_code, p.%1$I as value,
                     (select count(*) + 1 from profiles o where o.%1$I > p.%1$I) as rank
                from profiles p where p.%1$I > 0 and (strpos(lower(p.display_name), %2$L) > 0 or strpos(p.username, %2$L) > 0)
               order by p.%1$I desc, p.id limit %3$s) x$q$, v_col, v_q, v_lim) into v_items;
    execute format($q$select count(*)::int from profiles p where p.%1$I > 0 and (strpos(lower(p.display_name), %2$L) > 0 or strpos(p.username, %2$L) > 0)$q$, v_col, v_q) into v_total;
  else
    create temp table if not exists _slb(player_id uuid, value numeric) on commit drop;
    delete from _slb;
    if p_metric = 'jobs' then
      insert into _slb select player_id, count(*) from job_assignments where status = 'completed' and completed_at >= v_week group by player_id;
    elsif p_metric = 'distance' then
      insert into _slb select player_id, sum(coalesce(reported_distance_km, 0)) from job_assignments where status = 'completed' and completed_at >= v_week group by player_id;
    else
      insert into _slb select player_id, sum(amount) from transactions where kind in ('JOB_REWARD','BONUS') and amount > 0 and created_at >= v_week group by player_id;
    end if;
    select count(*)::int into v_total from _slb l join profiles pr on pr.id = l.player_id
     where l.value > 0 and (strpos(lower(pr.display_name), v_q) > 0 or strpos(pr.username, v_q) > 0);
    select coalesce(jsonb_agg(jsonb_build_object('rank', x.rank, 'player_id', x.player_id, 'display_name', x.display_name, 'username', x.username,
             'avatar_id', x.avatar_id, 'level', x.level, 'country_code', x.country_code, 'value', x.value) order by x.rank, x.player_id), '[]'::jsonb) into v_items
      from (select l.player_id, l.value, pr.display_name, pr.username, pr.avatar_id, pr.level, pr.country_code,
                   (select count(*) + 1 from _slb o where o.value > l.value) as rank
              from _slb l join profiles pr on pr.id = l.player_id
             where l.value > 0 and (strpos(lower(pr.display_name), v_q) > 0 or strpos(pr.username, v_q) > 0)
             order by l.value desc, l.player_id limit v_lim) x;
  end if;
  return jsonb_build_object('metric', p_metric, 'period', p_period, 'scope', 'search', 'total', v_total, 'limit', v_lim, 'offset', 0, 'items', v_items, 'server_time', now());
end $$;

-- ---------------------------------------------------------------- lock-down
do $$ declare r record;
begin
  for r in select p.oid::regprocedure as sig, p.proname from pg_proc p
            where p.pronamespace = 'public'::regnamespace and p.oid not in (select oid from _fn_before) loop
    execute format('revoke execute on function %s from public, anon, authenticated', r.sig);
    if r.proname in ('get_friends_leaderboard','search_leaderboard') then
      execute format('grant execute on function %s to authenticated', r.sig);
    end if;
  end loop;
end $$;
