// African Roads Online - admin site: the Advertising section (docs/ADVERTISING_SYSTEM.md). Loaded after admin.js, which gives it the same helpers, the same RPC wrapper and the same error words.
// Like the rest of the site this page decides nothing: every button is only a convenience, and the DATABASE refuses what a role may not do (only the owner approves artwork and campaigns, sets prices, publishes
// files or terminates a campaign). All data is shown as text; an advertiser's image is only ever drawn onto a canvas (never put into the page as an image, never opened as a file).
'use strict';
(function () {
  const A = window.ARO_ADMIN; if (!A) return;
  const { el, clear, rpc, ask, reasonField, words, when, num, table, badge, field, authToken, cfg } = A;

  Object.assign(A.WORDS, {
    invalid_business_name: 'The business name must be 2 to 120 characters.', invalid_contact_name: 'The contact name must be 2 to 80 characters.', invalid_email: 'That e-mail address is not valid.',
    invalid_phone: 'Phone numbers use digits, spaces, + ( ) and - (7 to 24 characters).', contact_number_required: 'Give a phone or WhatsApp number.', category_not_allowed: 'That category cannot be advertised (gambling, tobacco or nicotine, alcohol, adult, weapons, political).',
    rights_confirmation_required: 'The advertiser must confirm that they have the rights to their brand and artwork.', enquiry_not_found: 'No such enquiry.', already_converted: 'That enquiry is already a campaign.',
    enquiry_not_approved: 'Approve the enquiry first.', policy_ack_required: 'The screening flagged this one. Tick the acknowledgement after checking it.', campaign_not_found: 'No such campaign.',
    campaign_locked: 'The campaign has been sent for review; it can only change through a new review.', package_not_found: 'No such package.', invalid_url: 'The link must be https, with a normal web address (no password, not a number).',
    schedule_required: 'Set the start and end dates first.', creative_required: 'Add artwork first.', placement_required: 'Reserve at least one board face first.', invalid_transition: 'That cannot be done in the campaign\'s present state.',
    creative_not_ready: 'Every artwork used by a placement must be approved and published first.', slot_not_found: 'No such board face.', board_not_available: 'That board is not available.', creative_not_found: 'No such artwork.',
    aspect_mismatch: 'The artwork shape does not fit that board face.', creative_not_usable: 'That artwork was rejected or withdrawn.', slot_conflict: 'Another campaign already has that board face on those dates.', assignment_not_found: 'No such placement.',
    invalid_path: 'That file path is not valid.', artwork_not_uploaded: 'The file is not in storage yet.', artwork_too_large: 'The file is over 300 KB.', invalid_artwork: 'The file is not a usable image.', invalid_aspect: 'Unknown artwork shape.',
    creative_not_approved: 'Approve the artwork first.', invalid_variants: 'The published images do not match the expected files.', unexpected_path: 'That file is not where the approval said it must be.', invalid_hash: 'Bad image fingerprint.',
    invalid_dimensions: 'The image size is not allowed.', variant_not_uploaded: 'The processed image is not in the public bucket yet.', variant_too_large: 'The processed image is over 150 KB.', invalid_variant_type: 'The processed image type is not allowed.',
    board_not_found: 'No such board.', invalid_estimate: 'The estimate must be between 0 and 10,000,000.', invalid_code: 'Package codes use a-z, 0-9 and _.', invalid_name: 'The name must be 2 to 100 characters.',
    invalid_board_type: 'Unknown board type.', invalid_size_class: 'Unknown size class.', invalid_durations: 'Durations are 1 to 365 days, at most 8 of them.', invalid_currency: 'A currency code is three capital letters, for example NGN.',
    invalid_amount: 'The price is not valid.', invalid_period: 'The price period must be 1 to 365 days.', price_not_found: 'There is no price in that currency.', invalid_range: 'The dates are not a valid range.',
    invalid_description: 'The description is missing or too long.', invalid_notes: 'The notes are too long.', storage_unavailable: 'Storage is not set up on this project.'
  });

  const ASPECTS = ['2:1', '3:1', '1:2', '16:9', '5:3', '1:1'];
  const SIZE = { '2:1': [1024, 512], '3:1': [1024, 341], '1:2': [512, 1024], '16:9': [512, 288], '5:3': [640, 384], '1:1': [512, 512] };
  const SUBS = [
    { id: 'overview', label: 'Overview', roles: ['owner', 'advertising', 'analyst'] }, { id: 'enquiries', label: 'Enquiries', roles: ['owner', 'advertising'] },
    { id: 'campaigns', label: 'Campaigns', roles: ['owner', 'advertising', 'analyst'] }, { id: 'artwork', label: 'Artwork', roles: ['owner', 'advertising'] },
    { id: 'inventory', label: 'Inventory', roles: ['owner', 'advertising', 'analyst'] }, { id: 'packages', label: 'Packages & prices', roles: ['owner', 'advertising', 'analyst'] },
    { id: 'analytics', label: 'Analytics', roles: ['owner', 'advertising', 'analyst'] }
  ];
  const isOwner = () => A.role() === 'owner';
  const say = (box, text, cls) => { box.textContent = text; box.className = cls || 'muted'; };
  const money = (minor, cur) => (minor == null ? '-' : (Number(minor) / 100).toLocaleString(undefined, { minimumFractionDigits: 2, maximumFractionDigits: 2 }) + ' ' + cur);
  const stateBadge = s => badge(s, ({ live: 'live', approved: 'ok', scheduled: 'scheduled', in_review: 'scheduled', draft: '', paused: 'scheduled', ended: 'expired', terminated: 'expired', rejected: 'expired', new: 'scheduled', contacted: 'scheduled', converted: 'ok',
    submitted: 'scheduled', available: 'ok', planned: 'scheduled', maintenance: 'expired', retired: 'expired' })[s] || '');

  // ------------------------------------------------------------------ images: decode, shrink to the board's shape, encode (JPEG, at most 150 KB), fingerprint, upload. Done in THIS browser; the server only checks the result.
  async function storage(method, path, body, contentType) {
    const token = await authToken();
    const headers = { apikey: cfg.anonKey, Authorization: 'Bearer ' + token };
    if (contentType) headers['Content-Type'] = contentType;
    const res = await fetch(cfg.supabaseUrl.replace(/\/$/, '') + '/storage/v1/object/' + path, { method, headers, body });
    if (!res.ok) { let j = null; try { j = await res.json(); } catch (e) { j = null; } throw new A.ApiError((j && (j.message || j.error)) || 'http_' + res.status, res.status); }
    return res;
  }
  async function decode(blob) {
    let bmp; try { bmp = await createImageBitmap(blob); } catch (e) { throw new A.ApiError('invalid_artwork', 0); }
    if (bmp.width < 256 || bmp.height < 128 || bmp.width > 6000 || bmp.height > 6000) throw new A.ApiError('invalid_dimensions', 0);
    return bmp;
  }
  function cover(bmp, w, h) {
    const c = document.createElement('canvas'); c.width = w; c.height = h; const g = c.getContext('2d');
    const s = Math.max(w / bmp.width, h / bmp.height), dw = bmp.width * s, dh = bmp.height * s; g.fillStyle = '#ffffff'; g.fillRect(0, 0, w, h); g.drawImage(bmp, (w - dw) / 2, (h - dh) / 2, dw, dh); return c;
  }
  async function encode(canvas) {      // JPEG, because the game decodes JPEG and PNG (the engine has no WebP decoder)
    for (const q of [0.9, 0.82, 0.74, 0.66, 0.58, 0.5, 0.42, 0.34]) {
      const b = await new Promise(r => canvas.toBlob(r, 'image/jpeg', q));
      if (b && b.type === 'image/jpeg' && b.size <= 150 * 1024) return b;
    }
    throw new A.ApiError('variant_too_large', 0);
  }
  async function sha256(blob) { const d = await crypto.subtle.digest('SHA-256', await blob.arrayBuffer()); return Array.from(new Uint8Array(d)).map(x => x.toString(16).padStart(2, '0')).join(''); }
  // a preview of a stored submission: fetched with the admin's token and DRAWN on a canvas (a hostile file is never interpreted as part of the page)
  async function preview(path) {
    const box = el('div', { class: 'thumb' }, el('span', { class: 'muted' }, 'Loading the image...'));
    try {
      const blob = await (await storage('GET', 'authenticated/ad-submissions/' + path.split('/').map(encodeURIComponent).join('/'))).blob();
      const bmp = await decode(blob), s = Math.min(1, 480 / bmp.width), c = document.createElement('canvas'); c.width = Math.round(bmp.width * s); c.height = Math.round(bmp.height * s);
      c.getContext('2d').drawImage(bmp, 0, 0, c.width, c.height); c.setAttribute('role', 'img'); c.setAttribute('aria-label', 'Submitted artwork, ' + bmp.width + ' by ' + bmp.height + ' pixels');
      clear(box); box.append(c, el('div', { class: 'muted' }, bmp.width + ' x ' + bmp.height + ' px, ' + Math.round(blob.size / 1024) + ' KB, ' + (blob.type || 'unknown type')));
    } catch (e) { clear(box); box.append(el('span', { class: 'err' }, words(e))); }
    return box;
  }

  // ------------------------------------------------------------------ the section
  async function viewAds(root) {
    const subs = SUBS.filter(s => s.roles.includes(A.role())), nav = el('div', { class: 'acts', id: 'adsnav' }), body = el('div', { id: 'adsbody' });
    root.append(el('p', { class: 'muted' }, 'Roadside advertising. Everyone prepares; only the owner approves artwork and campaigns, sets prices and publishes. Nothing here takes a payment.'), nav, body);
    const views = { overview: vOverview, enquiries: vEnquiries, campaigns: vCampaigns, artwork: vArtwork, inventory: vInventory, packages: vPackages, analytics: vAnalytics };
    async function open(id) {
      for (const b of nav.children) b.className = b.dataset.sub === id ? '' : 'ghost';
      clear(body); try { await views[id](body, open); } catch (e) { if (e.message === 'not_authenticated' || e.message === 'mfa_required') throw e; body.append(el('p', { class: 'err', role: 'alert' }, words(e))); }
    }
    for (const s of subs) nav.append(el('button', { type: 'button', id: 'ads_' + s.id, 'data-sub': s.id, class: 'ghost', onclick: () => open(s.id) }, s.label));
    await open(subs[0].id);
  }

  async function vOverview(root) {
    const o = await rpc('admin_ad_overview');
    root.append(el('div', { class: 'tiles' },
      el('div', { class: 'tile' }, el('div', { class: 'v' }, num(o.boards.available) + ' / ' + num(o.boards.total)), el('div', { class: 'l' }, 'Boards available'), el('div', { class: 'n' }, num(o.boards.planned) + ' planned, ' + num(o.boards.maintenance) + ' in maintenance')),
      el('div', { class: 'tile' }, el('div', { class: 'v' }, num(o.slots.occupied_now) + ' / ' + num(o.slots.total)), el('div', { class: 'l' }, 'Faces showing an advertisement now'), el('div', { class: 'n' }, num(o.slots.reserved_total) + ' reserved in all')),
      el('div', { class: 'tile' }, el('div', { class: 'v' }, num(o.enquiries.new || 0)), el('div', { class: 'l' }, 'New enquiries'), el('div', { class: 'n' }, num(o.creatives_waiting) + ' artwork waiting for review')),
      el('div', { class: 'tile' }, el('div', { class: 'v' }, num(o.campaigns.live || 0)), el('div', { class: 'l' }, 'Campaigns live'), el('div', { class: 'n' }, Object.entries(o.campaigns).filter(([k]) => k !== 'live').map(([k, v]) => k + ' ' + v).join(', ') || 'none else'))));
    root.append(el('p', { class: 'muted' }, 'Server date (Lagos) ' + o.today + '. Campaign states follow the server clock.'));
  }

  // ---- enquiries
  async function vEnquiries(root, go) {
    const filter = el('select', { id: 'enqfilter' }, ['', 'new', 'contacted', 'approved', 'rejected', 'converted'].map(s => el('option', { value: s }, s || 'all'))), out = el('p', { id: 'enqout', class: 'muted', role: 'status' }), list = el('div'), detail = el('div');
    root.append(el('div', { class: 'row' }, field('Show', filter, 'enqfilter')), out, detail, list);
    async function load() {
      const r = await rpc('admin_ad_list_enquiries', { p_state: filter.value || null, p_limit: 100 }); clear(list);
      list.append(table(['Received', 'Business', 'Category', 'Contact', 'Screening', 'State', ''], r.items.map(e => [when(e.created_at), e.business_name, e.category, e.contact_name, e.policy_flags.length ? e.policy_flags.map(f => badge(f, 'ban')) : '-', stateBadge(e.state),
        el('button', { type: 'button', class: 'ghost', 'data-enq': e.id, onclick: () => show(e) }, 'Open')])));
    }
    function show(e) {
      clear(detail);
      const kv = el('dl', { class: 'kv' }); const add = (k, v) => { kv.append(el('dt', {}, k), el('dd', {}, v == null || v === '' ? '-' : v)); };
      add('Business', e.business_name); add('Contact', e.contact_name + ', ' + e.email); add('Phone / WhatsApp', [e.phone, e.whatsapp].filter(Boolean).join(' / ')); add('Category', e.category);
      add('Wants', [e.preferred_placement, e.preferred_city, e.duration_days ? e.duration_days + ' days' : null].filter(Boolean).join(', ')); add('Description', e.description); add('Message', e.message); add('Notes', e.notes);
      add('Screening', e.policy_flags.length ? 'Flagged: ' + e.policy_flags.join(', ') + '. Check the wording before approving.' : 'No screening terms found.');
      const actions = el('div', { class: 'acts' });
      const decide = (state, label) => el('button', { type: 'button', class: 'ghost', 'data-set': state, onclick: async () => {
        const fields = [{ name: 'notes', label: 'Notes (optional)', max: 1000 }]; if (state === 'approved' && e.policy_flags.length) fields.push({ name: 'ack', label: 'Type YES to confirm you checked the flagged wording', check: v => (v !== 'YES' ? 'Type YES.' : '') });
        fields.push(reasonField());
        const a = await ask({ title: label + ' - ' + e.business_name, text: 'This is a decision about the ENQUIRY. It does not publish anything.', fields, ok: label }); if (!a) return;
        try { await rpc('admin_ad_update_enquiry', { p_id: e.id, p_state: state, p_notes: a.notes || null, p_policy_ack: a.ack === 'YES', p_reason: a.reason }); say(out, label + ': done.', 'ok'); clear(detail); await load(); } catch (x) { say(out, words(x), 'err'); }
      } }, label);
      actions.append(decide('contacted', 'Mark contacted'), decide('approved', 'Approve enquiry'), decide('rejected', 'Reject enquiry'));
      if (e.state === 'approved') actions.append(el('button', { type: 'button', id: 'convert', onclick: async () => {
        const a = await ask({ title: 'Make a draft campaign', text: 'Creates the advertiser and a DRAFT campaign. Nothing is published.', fields: [{ name: 'title', label: 'Campaign title', value: e.business_name, check: v => (v.length < 2 ? 'Give a title.' : '') }, reasonField()], ok: 'Create draft' }); if (!a) return;
        try { await rpc('admin_ad_convert_enquiry', { p_id: e.id, p_title: a.title, p_reason: a.reason }); say(out, 'Draft campaign created. Open the Campaigns tab.', 'ok'); clear(detail); await load(); } catch (x) { say(out, words(x), 'err'); }
      } }, 'Convert to a draft campaign'));
      const art = e.artwork_path ? el('div', { id: 'enqart' }, el('h2', {}, 'Submitted artwork')) : null;
      detail.append(el('div', { class: 'card', id: 'enqdetail' }, el('h2', {}, e.business_name), kv, art, actions));
      if (art) preview(e.artwork_path).then(n => art.append(n));
    }
    filter.addEventListener('change', () => load().catch(x => say(out, words(x), 'err')));
    await load();
  }

  // ---- campaigns
  async function vCampaigns(root, go) {
    const out = el('p', { id: 'campout', class: 'muted', role: 'status' }), list = el('div'), detail = el('div', { id: 'campdetail' });
    const canPrepare = ['owner', 'advertising'].includes(A.role());
    root.append(canPrepare ? el('p', {}, el('button', { type: 'button', id: 'newcampaign', onclick: () => create() }, 'New campaign (direct sale)')) : null, out, detail, list);
    async function load() {
      const r = await rpc('admin_ad_list_campaigns', { p_limit: 200 }); clear(list);
      list.append(table(['Campaign', 'Advertiser', 'State', 'Dates', 'Faces', 'Agreed price', ''], r.items.map(c => [c.title, c.advertiser, stateBadge(c.effective_state), c.start_date ? c.start_date + ' to ' + c.end_date : 'not set', c.placements.filter(p => p.status === 'reserved').length,
        c.agreed_amount_minor != null ? money(c.agreed_amount_minor, c.agreed_currency) + ' (' + c.payment_status + ')' : '-', el('button', { type: 'button', class: 'ghost', 'data-camp': c.id, onclick: () => show(c) }, 'Open')])));
    }
    async function create() {
      const a = await ask({ title: 'New campaign (direct sale)', text: 'For an advertiser who did not use the public form. It starts as a DRAFT.', ok: 'Create draft', fields: [
        { name: 'business', label: 'Business name', check: v => (v.length < 2 ? 'Give the business name.' : '') }, { name: 'contact', label: 'Contact name', check: v => (v.length < 2 ? 'Give a contact name.' : '') },
        { name: 'email', label: 'E-mail', type: 'email', check: v => (!/^[^@\s]+@[^@\s]+\.[^@\s]+$/.test(v) ? 'Give a valid e-mail.' : '') }, { name: 'phone', label: 'Phone (optional)' },
        { name: 'category', label: 'Category', type: 'select', options: ['retail', 'food_drink', 'telecom', 'banking_insurance', 'automotive', 'fuel_energy', 'logistics_transport', 'education', 'health_pharmacy', 'real_estate', 'technology', 'hospitality_travel', 'agriculture', 'entertainment_events', 'fashion_beauty', 'ngo_public_service', 'other'] },
        { name: 'title', label: 'Campaign title', check: v => (v.length < 2 ? 'Give a title.' : '') }, { name: 'rights', label: 'Type YES: the advertiser confirms they hold the rights to their brand and artwork', check: v => (v !== 'YES' ? 'Type YES.' : '') }, reasonField()] });
      if (!a) return;
      try { const c = await rpc('admin_ad_create_campaign', { p_business: a.business, p_contact: a.contact, p_email: a.email, p_phone: a.phone || null, p_category: a.category, p_title: a.title, p_rights_confirmed: true, p_reason: a.reason }); say(out, 'Draft created.', 'ok'); await load(); show(c); } catch (x) { say(out, words(x), 'err'); }
    }
    async function show(c0) {
      const r = await rpc('admin_ad_list_campaigns', { p_limit: 200 }); const c = r.items.find(x => x.id === c0.id) || c0; clear(detail);
      const card = el('div', { class: 'card' }, el('div', { class: 'row-head' }, el('h2', {}, c.title), stateBadge(c.effective_state)));
      const kv = el('dl', { class: 'kv' }); const add = (k, v) => { kv.append(el('dt', {}, k), el('dd', {}, v == null || v === '' ? '-' : v)); };
      add('Advertiser', c.advertiser); add('Package', c.package_code); add('Dates', c.start_date ? c.start_date + ' to ' + c.end_date : 'not set'); add('Sponsored label', c.sponsored_label ? 'shown' : 'not shown'); add('Link', c.destination_url);
      add('Agreed price', c.agreed_amount_minor != null ? money(c.agreed_amount_minor, c.agreed_currency) : 'set when a package and dates are chosen'); add('Payment (manual note, nothing is charged)', c.payment_status); add('Review', c.review_reason);
      card.append(kv);
      card.append(el('h2', {}, 'Artwork'), table(['Name', 'Shape', 'Status', 'Published'], c.creatives.map(k => [k.name, k.aspect, stateBadge(k.status), k.public_ready ? 'yes' : 'no'])));
      card.append(el('h2', {}, 'Board faces reserved'), table(['Face', 'From', 'To', ''], c.placements.filter(p => p.status === 'reserved').map(p => [el('code', {}, p.slot_id), p.start_date, p.end_date,
        ['draft', 'rejected', 'paused'].includes(c.state) || isOwner() ? el('button', { type: 'button', class: 'ghost', 'data-unassign': p.id, onclick: () => act('admin_ad_unassign', { p_assignment: p.id }, 'Release this face', c) }, 'Release') : ''])));
      const acts = el('div', { class: 'acts' });
      const editable = ['draft', 'rejected'].includes(c.state) && canPrepare;
      if (editable) {
        acts.append(el('button', { type: 'button', id: 'editcamp', class: 'ghost', onclick: () => edit(c) }, 'Edit details'),
          el('button', { type: 'button', id: 'addart', class: 'ghost', onclick: () => addArtwork(c) }, 'Add artwork'),
          el('button', { type: 'button', id: 'addface', class: 'ghost', onclick: () => addFace(c) }, 'Reserve a board face'),
          el('button', { type: 'button', id: 'submitcamp', onclick: () => act('admin_ad_submit_campaign', { p_id: c.id }, 'Send for the owner\'s review', c) }, 'Send for review'));
      }
      if (c.state === 'in_review' && isOwner()) acts.append(el('button', { type: 'button', id: 'approvecamp', onclick: () => review(c, 'approve') }, 'Approve campaign'), el('button', { type: 'button', class: 'danger', id: 'rejectcamp', onclick: () => review(c, 'reject') }, 'Reject'));
      if (c.state === 'approved' && canPrepare) acts.append(el('button', { type: 'button', class: 'ghost', id: 'pausecamp', onclick: () => act('admin_ad_set_campaign_state', { p_id: c.id, p_action: 'pause' }, 'Pause this campaign (it leaves the road at once)', c) }, 'Pause'));
      if (c.state === 'paused' && isOwner()) acts.append(el('button', { type: 'button', id: 'resumecamp', onclick: () => act('admin_ad_set_campaign_state', { p_id: c.id, p_action: 'resume' }, 'Put this campaign back on the road', c) }, 'Resume'));
      if (isOwner() && !['terminated', 'rejected'].includes(c.state)) acts.append(el('button', { type: 'button', class: 'danger', id: 'endcamp', onclick: () => act('admin_ad_set_campaign_state', { p_id: c.id, p_action: 'terminate' }, 'Terminate this campaign for good', c) }, 'Terminate'));
      if (isOwner()) acts.append(el('button', { type: 'button', class: 'ghost', id: 'paycamp', onclick: async () => {
        const a = await ask({ title: 'Payment note', text: 'A manual note. The site charges nothing and talks to no payment provider.', ok: 'Save', fields: [{ name: 'status', label: 'Status', type: 'select', options: ['unbilled', 'invoiced', 'paid', 'waived'], value: c.payment_status }, reasonField()] }); if (!a) return;
        try { await rpc('admin_ad_set_payment_status', { p_id: c.id, p_status: a.status, p_reason: a.reason }); await load(); show(c); } catch (x) { say(out, words(x), 'err'); }
      } }, 'Payment note'));
      card.append(acts); detail.append(card); detail.scrollIntoView();
    }
    async function act(fn, args, title, c) {
      const a = await ask({ title, fields: [reasonField()], ok: 'Do it' }); if (!a) return;
      try { await rpc(fn, Object.assign({}, args, { p_reason: a.reason })); say(out, 'Done.', 'ok'); await load(); show(c); } catch (x) { say(out, words(x), 'err'); }
    }
    async function review(c, decision) {
      const flagged = decision === 'approve';
      const a = await ask({ title: (decision === 'approve' ? 'Approve ' : 'Reject ') + c.title, text: decision === 'approve' ? 'This makes the campaign eligible to be shown on its dates. You are the only person who can do this.' : 'The campaign goes back to the preparers.', ok: decision === 'approve' ? 'Approve' : 'Reject',
        fields: (flagged ? [{ name: 'ack', label: 'If the screening flagged the enquiry, type YES to confirm you checked it (leave empty otherwise)' }] : []).concat([reasonField()]) }); if (!a) return;
      try { await rpc('admin_ad_review_campaign', { p_id: c.id, p_decision: decision, p_policy_ack: a.ack === 'YES', p_reason: a.reason }); say(out, 'Campaign ' + decision + 'd.', 'ok'); await load(); show(c); } catch (x) { say(out, words(x), 'err'); }
    }
    async function edit(c) {
      const pk = await rpc('admin_ad_list_packages');
      const a = await ask({ title: 'Edit ' + c.title, ok: 'Save', fields: [{ name: 'title', label: 'Title', value: c.title, check: v => (v.length < 2 ? 'Give a title.' : '') },
        { name: 'pkg', label: 'Package', type: 'select', options: pk.items.filter(p => p.active).map(p => p.code), value: c.package_code || undefined }, { name: 'start', label: 'Start date', type: 'date', value: c.start_date || '', check: v => (!v ? 'Choose a start date.' : '') },
        { name: 'end', label: 'End date', type: 'date', value: c.end_date || '', check: v => (!v ? 'Choose an end date.' : '') }, { name: 'link', label: 'Link when a player asks to learn more (https only, optional)', value: c.destination_url || '' },
        { name: 'label', label: 'Show the "Sponsored" label', type: 'select', options: ['yes', 'no'], value: c.sponsored_label ? 'yes' : 'no' }, reasonField()] }); if (!a) return;
      try { await rpc('admin_ad_save_campaign', { p_id: c.id, p_title: a.title, p_package_code: a.pkg, p_start: a.start, p_end: a.end, p_sponsored: a.label === 'yes', p_destination_url: a.link || null, p_reason: a.reason }); await load(); show(c); say(out, 'Saved.', 'ok'); } catch (x) { say(out, words(x), 'err'); }
    }
    function addArtwork(c) {
      const f = el('input', { id: 'artfile', type: 'file', accept: 'image/jpeg,image/png,image/webp', required: true }), name = el('input', { id: 'artname', maxlength: 100, required: true }),
        aspect = el('select', { id: 'artaspect' }, ASPECTS.map(x => el('option', { value: x }, x))), reason = el('input', { id: 'artreason', maxlength: 300, required: true, placeholder: 'why (kept in the audit log)' }), err = el('p', { class: 'err', role: 'alert', id: 'arterr' });
      const form = el('form', { id: 'artform', onsubmit: async ev => {
        ev.preventDefault(); err.textContent = ''; const file = f.files[0]; if (!file) { err.textContent = 'Choose an image file.'; return; }
        const ext = ({ 'image/jpeg': 'jpg', 'image/png': 'png', 'image/webp': 'webp' })[file.type]; if (!ext) { err.textContent = 'Only JPEG, PNG or WebP images.'; return; } if (file.size > 300 * 1024) { err.textContent = 'The file is over 300 KB.'; return; }
        const btn = form.querySelector('button[type=submit]'); btn.disabled = true;
        try {
          await decode(file);                                                                 // it must be a real image of a sensible size before it is even uploaded
          const path = 'admin/' + crypto.randomUUID() + '.' + ext;
          await storage('POST', 'ad-submissions/' + path, file, file.type);
          await rpc('admin_ad_register_creative', { p_campaign: c.id, p_name: name.value, p_aspect: aspect.value, p_path: path, p_reason: reason.value });
          say(out, 'Artwork added. The owner reviews it in the Artwork tab.', 'ok'); await load(); show(c);
        } catch (x) { err.textContent = words(x); btn.disabled = false; }
      } }, el('div', { class: 'row' }, field('Image file (JPEG, PNG or WebP, at most 300 KB)', f, 'artfile'), field('Name', name, 'artname'), field('Shape of the board face', aspect, 'artaspect')), field('Reason', reason, 'artreason'), err,
        el('div', { class: 'row' }, el('button', { type: 'submit', id: 'artsubmit' }, 'Upload and add'), el('button', { type: 'button', class: 'ghost', onclick: () => clear(art) }, 'Cancel')));
      const art = detail.querySelector('#artarea') || el('div', { id: 'artarea' }); if (!art.parentNode) detail.append(art); clear(art); art.append(el('div', { class: 'card' }, el('h2', {}, 'Add artwork'), form));
    }
    async function addFace(c) {
      const r = await rpc('admin_ad_list_boards', { p_status: 'available' }); const arts = c.creatives.filter(k => ['submitted', 'approved'].includes(k.status));
      if (!arts.length) { say(out, 'Add artwork first.', 'err'); return; }
      const opts = []; for (const b of r.items) for (const s of b.slots) opts.push(s.id + '  (' + b.board_type + ', ' + (b.city || '') + ' ' + (b.road_label || '') + ', ' + s.aspect + (s.occupied_now ? ', busy now' : '') + ')');
      const a = await ask({ title: 'Reserve a board face', text: 'The database refuses a face that another campaign holds on any of the campaign\'s dates.', ok: 'Reserve', fields: [{ name: 'slot', label: 'Face', type: 'select', options: opts }, { name: 'art', label: 'Artwork', type: 'select', options: arts.map(k => k.name + ' [' + k.aspect + '] ' + k.id) }, reasonField()] }); if (!a) return;
      try { await rpc('admin_ad_assign', { p_campaign: c.id, p_slot: a.slot.split('  (')[0], p_creative: a.art.split(' ').pop(), p_reason: a.reason }); say(out, 'Face reserved.', 'ok'); await load(); show(c); } catch (x) { say(out, words(x), 'err'); }
    }
    await load();
  }

  // ---- artwork: preview, owner decision, then the owner's browser makes the public image and the database checks it
  async function vArtwork(root) {
    const out = el('p', { id: 'artout', class: 'muted', role: 'status' }), list = el('div');
    root.append(el('p', { class: 'muted' }, 'Submitted artwork waits here. Only the owner approves it. After approval the owner\'s browser makes the 150 KB public image and uploads it; until then nothing is public.'), out, list);
    async function load() {
      const r = await rpc('admin_ad_list_creatives', { p_limit: 100 }); clear(list);
      for (const k of r.items) {
        const card = el('div', { class: 'card' }, el('div', { class: 'row-head' }, el('strong', {}, k.name), badge(k.aspect, ''), stateBadge(k.status), k.public_ready ? badge('published', 'ok') : null));
        card.append(await preview(k.submission_path));
        const acts = el('div', { class: 'acts' });
        if (k.status === 'submitted' && isOwner()) acts.append(el('button', { type: 'button', 'data-approve': k.id, onclick: () => decide(k, 'approve') }, 'Approve artwork'), el('button', { type: 'button', class: 'danger', 'data-reject': k.id, onclick: () => decide(k, 'reject') }, 'Reject'));
        if (k.status === 'approved' && !k.public_ready && isOwner()) acts.append(el('button', { type: 'button', 'data-publish': k.id, onclick: () => publish(k) }, 'Make the public image and publish'));
        if (k.review_reason) card.append(el('p', { class: 'muted' }, 'Review: ' + k.review_reason));
        card.append(acts); list.append(card);
      }
      if (!r.items.length) list.append(el('p', { class: 'muted' }, 'No artwork yet.'));
    }
    async function decide(k, d) {
      const a = await ask({ title: (d === 'approve' ? 'Approve ' : 'Reject ') + k.name, text: d === 'approve' ? 'Check the image against the content rules: no gambling, tobacco or nicotine, alcohol, adult content, weapons, scams or misleading claims, no brand or artwork the advertiser has no right to.' : '', ok: d === 'approve' ? 'Approve' : 'Reject',
        fields: (d === 'approve' ? [{ name: 'ack', label: 'Type YES if the screening flagged the enquiry and you checked it (empty otherwise)' }] : []).concat([reasonField()]) }); if (!a) return;
      try { await rpc('admin_ad_review_creative', { p_id: k.id, p_decision: d, p_policy_ack: a.ack === 'YES', p_reason: a.reason }); say(out, 'Done.', 'ok'); await load(); } catch (x) { say(out, words(x), 'err'); }
    }
    async function publish(k) {
      const a = await ask({ title: 'Publish ' + k.name, text: 'This browser will resize the image to the board shape, re-encode it as a JPEG (all metadata is dropped), upload it to the public bucket and tell the database. Players can then be shown it once a campaign using it is approved.', ok: 'Publish', fields: [reasonField()] }); if (!a) return;
      try {
        say(out, 'Reading the submission...', 'muted');
        const blob = await (await storage('GET', 'authenticated/ad-submissions/' + k.submission_path.split('/').map(encodeURIComponent).join('/'))).blob(), bmp = await decode(blob);
        const [w, h] = SIZE[k.aspect], web = await encode(cover(bmp, w, h)), hash = await sha256(web), path = k.expected_paths[0];
        say(out, 'Uploading ' + Math.round(web.size / 1024) + ' KB...', 'muted');
        await storage('POST', 'ad-creatives-public/' + path.split('/').map(encodeURIComponent).join('/'), web, 'image/jpeg');
        await rpc('admin_ad_confirm_variants', { p_id: k.id, p_variants: [{ path, width: w, height: h, sha256: hash }], p_reason: a.reason });
        say(out, 'Published (' + w + ' x ' + h + ', ' + Math.round(web.size / 1024) + ' KB).', 'ok'); await load();
      } catch (x) { say(out, words(x), 'err'); }
    }
    await load();
  }

  // ---- inventory
  async function vInventory(root) {
    const type = el('select', { id: 'invtype' }, ['', 'roadside', 'highway', 'urban', 'commercial', 'flyover', 'terminal', 'wall', 'pylon', 'led', 'promo'].map(t => el('option', { value: t }, t || 'all types')));
    const out = el('p', { id: 'invout', class: 'muted', role: 'status' }), list = el('div');
    root.append(el('p', { class: 'muted' }, 'Every physical board in the game and its faces. Boards come from the route data; this page changes only their status and the owner\'s traffic estimate.'), el('div', { class: 'row' }, field('Type', type, 'invtype')), out, list);
    async function load() {
      const r = await rpc('admin_ad_list_boards', { p_type: type.value || null }); clear(list);
      list.append(table(['Board', 'Where', 'Type', 'Size', 'Status', 'Faces', 'Estimated daily passes', ''], r.items.map(b => [el('code', {}, b.id), [b.city, b.district, b.road_label, b.station_m != null ? Math.round(b.station_m) + ' m' : null].filter(Boolean).join(', '), b.board_type,
        b.width_m + ' x ' + b.height_m + ' m' + (b.illuminated ? ', lit' : '') + (b.digital ? ', digital' : ''), stateBadge(b.status), b.slots.map(s => s.aspect + (s.occupied_now ? ' busy' : s.reserved_until ? ' held to ' + s.reserved_until : ' free')).join('; '),
        b.est_daily_passes == null ? 'not set' : num(b.est_daily_passes) + ' (estimate)', isOwner() ? el('button', { type: 'button', class: 'ghost', 'data-board': b.id, onclick: () => set(b) }, 'Change') : ''])));
    }
    async function set(b) {
      const a = await ask({ title: 'Board ' + b.id, ok: 'Save', fields: [{ name: 'status', label: 'Status', type: 'select', options: ['planned', 'available', 'maintenance', 'retired'], value: b.status }, { name: 'est', label: 'Estimated passes per day (an estimate, never shown as a measurement; empty = unknown)', type: 'number', value: b.est_daily_passes == null ? '' : String(b.est_daily_passes), min: 0, maxv: 10000000 }, reasonField()] }); if (!a) return;
      try { await rpc('admin_ad_set_board_status', { p_id: b.id, p_status: a.status, p_est_daily_passes: a.est === '' ? null : parseInt(a.est, 10), p_reason: a.reason }); say(out, 'Saved.', 'ok'); await load(); } catch (x) { say(out, words(x), 'err'); }
    }
    type.addEventListener('change', () => load().catch(x => say(out, words(x), 'err')));
    await load();
  }

  // ---- packages and prices (the owner sets them; amounts are stored in minor units: 100 kobo = 1 naira)
  async function vPackages(root) {
    const out = el('p', { id: 'pkgout', class: 'muted', role: 'status' }), list = el('div');
    root.append(el('p', { class: 'muted' }, 'No price is built into the game or this site. The owner sets a price per package and currency (naira first; dollars can be added). Campaigns copy the price at the time they are saved. Nothing is charged: there is no payment provider.'),
      isOwner() ? el('p', {}, el('button', { type: 'button', id: 'newpkg', onclick: () => savePkg(null) }, 'New package')) : null, out, list);
    async function load() {
      const r = await rpc('admin_ad_list_packages'); clear(list);
      list.append(table(['Package', 'Type', 'Size', 'Days', 'Prices', 'Boards available', 'Active', ''], r.items.map(p => [el('div', {}, el('strong', {}, p.name), el('div', { class: 'muted' }, el('code', {}, p.code))), p.board_type + (p.digital ? ' (digital)' : ''), p.size_class, p.durations.join(', '),
        p.prices.length ? p.prices.map(x => money(x.amount_minor, x.currency) + ' per ' + x.period_days + ' days').join('; ') : el('span', { class: 'warn' }, 'no price set'), p.boards_available, p.active ? 'yes' : 'no',
        isOwner() ? el('span', { class: 'acts' }, el('button', { type: 'button', class: 'ghost', 'data-pkg': p.code, onclick: () => savePkg(p) }, 'Edit'), el('button', { type: 'button', class: 'ghost', 'data-price': p.code, onclick: () => price(p) }, 'Set a price')) : ''])));
    }
    async function savePkg(p) {
      const a = await ask({ title: p ? 'Edit ' + p.code : 'New package', ok: 'Save', fields: [{ name: 'code', label: 'Code (a-z, 0-9, _)', value: p ? p.code : '', check: v => (!/^[a-z0-9_]{2,40}$/.test(v) ? 'Use a-z, 0-9 and _ (2 to 40).' : '') }, { name: 'name', label: 'Name', value: p ? p.name : '', check: v => (v.length < 2 ? 'Give a name.' : '') },
        { name: 'type', label: 'Board type', type: 'select', options: ['roadside', 'highway', 'urban', 'commercial', 'flyover', 'terminal', 'wall', 'pylon', 'led', 'promo'], value: p ? p.board_type : undefined }, { name: 'size', label: 'Size', type: 'select', options: ['small', 'medium', 'large', 'xl'], value: p ? p.size_class : undefined },
        { name: 'days', label: 'Booking lengths in days, comma separated', value: p ? p.durations.join(',') : '14,28,56', check: v => (!/^\d{1,3}(,\s*\d{1,3}){0,7}$/.test(v) ? 'For example 14,28,56.' : '') }, { name: 'desc', label: 'Description (optional)', value: p ? p.description || '' : '' },
        { name: 'active', label: 'Active', type: 'select', options: ['yes', 'no'], value: p && !p.active ? 'no' : 'yes' }, reasonField()] }); if (!a) return;
      try { await rpc('admin_ad_save_package', { p_code: a.code, p_name: a.name, p_board_type: a.type, p_size_class: a.size, p_durations: a.days.split(',').map(x => parseInt(x.trim(), 10)), p_digital: a.type === 'led', p_description: a.desc || null, p_active: a.active === 'yes', p_reason: a.reason }); say(out, 'Saved.', 'ok'); await load(); } catch (x) { say(out, words(x), 'err'); }
    }
    async function price(p) {
      const a = await ask({ title: 'Price for ' + p.name, text: 'Enter the price in the main unit (naira, dollars). Leave it empty to remove the price in that currency.', ok: 'Save', fields: [{ name: 'cur', label: 'Currency (3 capital letters)', value: 'NGN', check: v => (!/^[A-Z]{3}$/.test(v) ? 'For example NGN or USD.' : '') },
        { name: 'amount', label: 'Amount for one period', type: 'number', min: 0 }, { name: 'days', label: 'Period in days', type: 'number', value: '28', min: 1, maxv: 365 }, reasonField()] }); if (!a) return;
      try { await rpc('admin_ad_set_price', { p_code: p.code, p_currency: a.cur, p_amount_minor: a.amount === '' ? null : Math.round(parseFloat(a.amount) * 100), p_period_days: parseInt(a.days || '28', 10), p_reason: a.reason }); say(out, 'Saved.', 'ok'); await load(); } catch (x) { say(out, words(x), 'err'); }
    }
    await load();
  }

  // ---- analytics: measured and estimated are different columns and are explained next to the numbers
  async function vAnalytics(root) {
    const from = el('input', { id: 'anfrom', type: 'date' }), to = el('input', { id: 'anto', type: 'date' }), out = el('p', { id: 'anout', class: 'muted', role: 'status' }), box = el('div'), defs = el('div', { class: 'card' });
    root.append(el('div', { class: 'row' }, field('From', from, 'anfrom'), field('To', to, 'anto'), el('button', { type: 'button', id: 'anload', onclick: () => load() }, 'Show')), out, box, defs);
    async function load() {
      try {
        const r = await rpc('admin_ad_analytics', { p_from: from.value || null, p_to: to.value || null }); clear(box); clear(defs); say(out, 'From ' + r.from + ' to ' + r.to + '.', 'muted');
        box.append(table(['Campaign', 'Advertiser', 'State', 'Faces', 'Measured seconds', 'Measured views', 'Clicks', 'Image loaded (diagnostic)', 'ESTIMATED passes'], r.campaigns.map(c => [c.title, c.advertiser, stateBadge(c.state), c.placements, num(Math.round(c.measured_seconds)), num(c.measured_views),
          c.clicks_supported ? num(c.clicks) : 'not supported yet', num(c.loaded), num(c.estimated_passes) + ' (estimate)'])));
        defs.append(el('h2', {}, 'What the numbers mean'), el('p', {}, el('strong', {}, 'Measured: '), r.definitions.measured), el('p', {}, el('strong', {}, 'Loaded: '), r.definitions.loaded), el('p', {}, el('strong', {}, 'Estimated: '), r.definitions.estimated));
      } catch (x) { say(out, words(x), 'err'); }
    }
    await load();
  }

  A.tabs.push({ id: 'ads', label: 'Advertising', roles: ['owner', 'advertising', 'analyst'], view: viewAds });
})();
