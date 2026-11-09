// Public advertiser enquiry form (docs/ADVERTISING_SYSTEM.md). Anonymous: it holds only the PUBLIC anon key. It can do exactly two things, both through reviewed database functions:
// request_ad_upload (a one-time upload ticket for one small image) and submit_ad_enquiry (the enquiry). The database validates everything again, caps the traffic, and never publishes anything.
'use strict';
(function () {
  const cfg = window.ARO_ADVERTISE_CONFIG || {}; const $ = id => document.getElementById(id);
  const CATEGORIES = [['retail', 'Retail and shops'], ['food_drink', 'Food and drink (no alcohol)'], ['telecom', 'Telecom'], ['banking_insurance', 'Banking and insurance'], ['automotive', 'Vehicles and spares'], ['fuel_energy', 'Fuel and energy'],
    ['logistics_transport', 'Logistics and transport'], ['education', 'Education'], ['health_pharmacy', 'Health and pharmacy'], ['real_estate', 'Real estate'], ['technology', 'Technology'], ['hospitality_travel', 'Hotels and travel'],
    ['agriculture', 'Agriculture'], ['entertainment_events', 'Entertainment and events'], ['fashion_beauty', 'Fashion and beauty'], ['ngo_public_service', 'Charity and public service'], ['other', 'Something else']];
  const PLACEMENTS = [['', 'No preference'], ['roadside', 'Roadside billboard'], ['highway', 'Highway board'], ['urban', 'City street board'], ['commercial', 'Shopping and market streets'], ['flyover', 'Flyover or interchange'], ['terminal', 'Bus terminal'],
    ['wall', 'Warehouse or depot wall'], ['pylon', 'Fuel station'], ['led', 'Digital screen'], ['promo', 'Small promotional sign']];
  const fill = (id, list) => { const s = $(id); for (const [v, t] of list) { const o = document.createElement('option'); o.value = v; o.textContent = t; s.append(o); } };
  fill('category', CATEGORIES); fill('preferred_placement', PLACEMENTS); fill('duration_days', [['28', '4 weeks'], ['14', '2 weeks'], ['56', '8 weeks'], ['84', '12 weeks'], ['180', '6 months']]);
  const WORDS = { invalid_email: 'That e-mail address is not valid.', invalid_phone: 'Phone numbers use digits, spaces, + ( ) and - (7 to 24 characters).', contact_number_required: 'Please give a phone or WhatsApp number.',
    category_not_allowed: 'Sorry, we cannot advertise that kind of business or product.', rights_confirmation_required: 'Please confirm that you have the right to use your brand and artwork.', invalid_business_name: 'Please check the business name.',
    invalid_contact_name: 'Please check the contact name.', invalid_description: 'Please describe your business and advertisement (10 to 1000 characters).', rate_limited: 'Too many enquiries just now. Please try again later.',
    invalid_duration: 'Please choose how long.', invalid_artwork: 'The image could not be used. Use a JPEG, PNG or WebP image.', artwork_too_large: 'The image is over 300 KB.', artwork_not_uploaded: 'The image did not upload. Please try again.' };
  const words = m => WORDS[m] || 'Something went wrong. Please try again in a moment.';
  async function post(path, body, headers) {
    const res = await fetch(cfg.supabaseUrl.replace(/\/$/, '') + path, { method: 'POST', headers: Object.assign({ apikey: cfg.anonKey, Authorization: 'Bearer ' + cfg.anonKey }, headers), body });
    let j = null; try { j = await res.json(); } catch (e) { j = null; } if (!res.ok) throw new Error((j && (j.message || j.error)) || 'http_' + res.status); return j;
  }
  const rpc = (fn, args) => post('/rest/v1/rpc/' + fn, JSON.stringify(args), { 'Content-Type': 'application/json' });
  $('enquiry').addEventListener('submit', async ev => {
    ev.preventDefault(); const err = $('error'); err.textContent = ''; const btn = $('send'); btn.disabled = true;
    try {
      if (!cfg.supabaseUrl || !cfg.anonKey || /YOUR-/.test(cfg.supabaseUrl + cfg.anonKey)) throw new Error('This page is not configured.');
      const v = id => $(id).value.trim();
      if (!$('rights').checked) throw new Error('rights_confirmation_required');
      if (!v('phone') && !v('whatsapp')) throw new Error('contact_number_required');
      let artwork_path = null; const file = $('artwork').files[0];
      if (file) {
        const ext = { 'image/jpeg': 'jpg', 'image/png': 'png', 'image/webp': 'webp' }[file.type]; if (!ext) throw new Error('invalid_artwork'); if (file.size > 300 * 1024) throw new Error('artwork_too_large');
        const t = await rpc('request_ad_upload', { p_ext: ext });
        await post('/storage/v1/object/' + t.bucket + '/' + t.path.split('/').map(encodeURIComponent).join('/'), file, { 'Content-Type': file.type });
        artwork_path = t.path;
      }
      const data = { business_name: v('business_name'), contact_name: v('contact_name'), email: v('email'), phone: v('phone') || null, whatsapp: v('whatsapp') || null, category: v('category'), description: v('description'),
        preferred_city: v('preferred_city') || null, preferred_placement: v('preferred_placement') || null, duration_days: parseInt(v('duration_days'), 10), message: v('message') || null, artwork_path, rights_confirmed: 'true', website_url: v('website_url') };
      const r = await rpc('submit_ad_enquiry', { p_data: data });
      $('enquiry').hidden = true; $('done').hidden = false; $('ref').textContent = r.reference || 'received';
    } catch (e) { err.textContent = words(e.message); btn.disabled = false; }
  });
})();
