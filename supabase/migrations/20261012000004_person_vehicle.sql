-- Friends screen: the public player card now says which vehicle the player drives.
-- Same rule as presence (activity, route): only the player themselves, their friends and people in the same convoy see it ('full' visibility);
-- strangers and people met in passing ('basic' / 'none') do not. The vehicle is the selected one (else the oldest owned), as _member_vehicle already chooses.
-- Both the name (for the card) and the catalogue id (for the picture) are given; no coins, no e-mail, nothing private is added.
create or replace function _person_json(p_viewer uuid, p_other uuid) returns jsonb
language plpgsql stable security definer set search_path = public as $$
declare p profiles; v_vis text; v_pres jsonb; v_veh jsonb;
begin
  select * into p from profiles where id = p_other;
  if not found then return null; end if;
  v_vis := _presence_visibility(p_viewer, p_other);
  v_pres := case v_vis when 'full' then _presence_of(p_other)
                       when 'basic' then (select jsonb_build_object('online', x->'online', 'last_seen_at', x->'last_seen_at') from (select _presence_of(p_other) x) q)
                       else null end;
  if v_vis = 'full' then
    select jsonb_build_object('vehicle', d.name, 'vehicle_id', d.id) into v_veh
      from vehicle_ownership vo join vehicle_definitions d on d.id = vo.definition_id
     where vo.player_id = p_other
     order by (vo.id = p.selected_vehicle_id) desc, vo.acquired_at limit 1;
  end if;
  return jsonb_build_object('player_id', p.id, 'display_name', p.display_name, 'username', p.username, 'avatar_id', p.avatar_id,
    'country_code', p.country_code, 'level', p.level, 'friend_status', _friend_status(p_viewer, p_other), 'presence', v_pres) || coalesce(v_veh, '{}'::jsonb);
end $$;
