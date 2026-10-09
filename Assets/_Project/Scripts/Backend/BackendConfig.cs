using UnityEngine;

namespace ARO.Backend
{
    /// <summary>
    /// Supabase connection settings. Holds the PUBLIC anon key only - the service-role key
    /// must never exist in a Unity project. Create via Assets > Create > African Roads > Backend Config
    /// and save as Resources/BackendConfig.asset (gitignored if it contains a real project URL).
    /// </summary>
    [CreateAssetMenu(menuName = "African Roads/Backend Config")]
    public class BackendConfig : ScriptableObject
    {
        public string supabaseUrl = "https://YOUR-PROJECT.supabase.co";
        public string anonKey = "";
        public bool IsConfigured => !string.IsNullOrEmpty(anonKey) && !supabaseUrl.Contains("YOUR-PROJECT");
    }
}
