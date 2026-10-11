-- Players Nearby is opt-in.
-- 20261011000003_discovery added profiles.appear_nearby with DEFAULT TRUE, so every account (new or already playing) would appear in other
-- players' Players Nearby lists without ever having been asked. Nobody can have chosen that: no database holds a published player-made choice yet.
-- This forward migration changes ONLY that one preference:
--   * the default for new accounts is false (a player turns it on in onboarding or Settings, which both call set_nearby_visibility(true));
--   * every account that exists now is set to false, because the true they hold is the old default, not a choice;
--   * set_nearby_visibility(null) now means "not visible" (it used to mean visible), so a client that sends nothing never exposes anyone.
-- It does not read or write country_code, display_name, username, level, XP, wallets, vehicles, friends, convoys, cities, routes or any other column.
-- The published discovery migration is untouched (forward-only).
alter table profiles alter column appear_nearby set default false;
update profiles set appear_nearby = false where appear_nearby;       -- touches only this column; profiles_touch fires on name/handle/avatar/country updates only, so updated_at is not moved

create or replace function set_nearby_visibility(p_visible boolean) returns boolean
language plpgsql security definer set search_path = public as $$
begin
  if auth.uid() is null then raise exception 'not_authenticated'; end if;
  perform _rate_limit('profile_write', 30, 60);
  update profiles set appear_nearby = coalesce(p_visible, false) where id = auth.uid();
  return coalesce(p_visible, false);
end $$;
