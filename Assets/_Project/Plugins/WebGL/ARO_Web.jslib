mergeInto(LibraryManager.library, {
  // Same-tab navigation (used for OAuth: works from async code, unlike window.open which popup blockers stop).
  ARO_Redirect: function (url) { window.location.href = UTF8ToString(url); },
  // Remove the #access_token=... fragment from the address bar once the session has been taken from it.
  ARO_ClearUrlHash: function () { try { history.replaceState(null, '', window.location.pathname + window.location.search); } catch (e) {} },
  // The browser's preferred language tag ("en-NG"): only used to suggest a country and a language during onboarding.
  ARO_BrowserLocale: function () {
    var s = ''; try { s = (navigator.languages && navigator.languages[0]) || navigator.language || ''; } catch (e) {}
    var n = lengthBytesUTF8(s) + 1, p = _malloc(n); stringToUTF8(s, p, n); return p;
  }
});
