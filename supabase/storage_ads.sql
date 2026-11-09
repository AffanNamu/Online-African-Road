-- Advertising storage: the two buckets and their access policies (idempotent). The advertising migration (20261110000001) already runs this block when the project has Supabase Storage.
-- Run THIS file by hand (supabase/apply.sh or the SQL editor, as the project owner) on a project where Storage was switched on AFTER that migration ran. It is a copy of the migration's block; a test in
-- supabase/tests/53_advertising.sql proves the buckets and every policy exist after the migration, and tools/check_storage_ads_copy.py fails if the two copies differ.
-- ================================================================ storage: two buckets and their policies (a database without Supabase Storage skips this part)
do $$ begin
  if to_regclass('storage.buckets') is not null and to_regclass('storage.objects') is not null then
    insert into storage.buckets(id, name, public, file_size_limit, allowed_mime_types) values
      ('ad-creatives-public', 'ad-creatives-public', true, 153600, array['image/webp', 'image/jpeg', 'image/png']),
      ('ad-submissions', 'ad-submissions', false, 307200, array['image/webp', 'image/jpeg', 'image/png'])
    on conflict (id) do update set public = excluded.public, file_size_limit = excluded.file_size_limit, allowed_mime_types = excluded.allowed_mime_types;
    execute 'alter table storage.objects enable row level security';
    -- public bucket: the OWNER may add an object, and only at a path an approved creative expects; the owner may remove one (a takedown). Everyone may read what is there (public URL).
    execute 'drop policy if exists aro_ad_public_owner_insert on storage.objects';
    execute 'create policy aro_ad_public_owner_insert on storage.objects for insert to authenticated with check (bucket_id = ''ad-creatives-public'' and public._ad_may_publish(name))';
    execute 'drop policy if exists aro_ad_public_owner_delete on storage.objects';
    execute 'create policy aro_ad_public_owner_delete on storage.objects for delete to authenticated using (bucket_id = ''ad-creatives-public'' and public._ad_is_owner())';
    execute 'drop policy if exists aro_ad_public_read on storage.objects';
    execute 'create policy aro_ad_public_read on storage.objects for select to anon, authenticated using (bucket_id = ''ad-creatives-public'')';
    -- submissions bucket: an anonymous advertiser may add ONE object at a path with a live ticket; only owner and advertising may read, add or remove anything there
    execute 'drop policy if exists aro_ad_sub_anon_insert on storage.objects';
    execute 'create policy aro_ad_sub_anon_insert on storage.objects for insert to anon with check (bucket_id = ''ad-submissions'' and public._ad_ticket_ok(name))';
    execute 'drop policy if exists aro_ad_sub_admin_select on storage.objects';
    execute 'create policy aro_ad_sub_admin_select on storage.objects for select to authenticated using (bucket_id = ''ad-submissions'' and public._ad_is_reviewer())';
    execute 'drop policy if exists aro_ad_sub_admin_insert on storage.objects';
    execute 'create policy aro_ad_sub_admin_insert on storage.objects for insert to authenticated with check (bucket_id = ''ad-submissions'' and public._ad_is_reviewer() and name ~ ''^admin/'')';
    execute 'drop policy if exists aro_ad_sub_admin_delete on storage.objects';
    execute 'create policy aro_ad_sub_admin_delete on storage.objects for delete to authenticated using (bucket_id = ''ad-submissions'' and public._ad_is_reviewer())';
  else
    raise notice 'storage schema not present: the advertising buckets and their policies were not created (run supabase/storage_ads.sql once Storage is on)';
  end if;
end $$;

