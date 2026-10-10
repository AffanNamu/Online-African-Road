mergeInto(LibraryManager.library, {
  // Same-tab navigation (used for OAuth: works from async code, unlike window.open which popup blockers stop).
  ARO_Redirect: function (url) { window.location.href = UTF8ToString(url); },
  // Remove the #access_token=... fragment from the address bar once the session has been taken from it.
  ARO_ClearUrlHash: function () { try { history.replaceState(null, '', window.location.pathname + window.location.search); } catch (e) {} }
});
