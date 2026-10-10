using System;
using System.IO;
using UnityEngine;

namespace ARO.Editor
{
    /// <summary>
    /// Post-build step for WebGL: turns Unity's default page (a 960x600 box on white, a Unity logo) into a full-window game with a branded loading screen.
    /// It only adds files and patches index.html, so the loader contract (#unity-canvas, #unity-loading-bar, #unity-progress-bar-full) is untouched.
    /// </summary>
    public static class WebBrand
    {
        const string Ui = "Assets/_Project/Resources/UI";

        public static void Apply(string outputDir)
        {
            try
            {
                string indexPath = Path.Combine(outputDir, "index.html");
                if (!File.Exists(indexPath)) { Console.WriteLine("[ARO] WebBrand: no index.html in " + outputDir); return; }
                string td = Path.Combine(outputDir, "TemplateData"); Directory.CreateDirectory(td);
                Copy($"{Ui}/login_bg.jpg", Path.Combine(td, "aro-bg.jpg"));
                Copy($"{Ui}/logo_mark.png", Path.Combine(td, "aro-logo.png"));
                File.WriteAllText(Path.Combine(td, "aro.css"), Css);

                string html = File.ReadAllText(indexPath);
                if (!html.Contains("aro.css"))
                {
                    html = html.Replace("</head>", "    <link rel=\"stylesheet\" href=\"TemplateData/aro.css\">\n    <link rel=\"icon\" href=\"TemplateData/aro-logo.png\">\n    <meta name=\"viewport\" content=\"width=device-width, initial-scale=1.0, user-scalable=no\">\n  </head>");
                    html = System.Text.RegularExpressions.Regex.Replace(html, @"<title>.*?</title>", "<title>African Roads Online</title>");
                    // the loading bar shows a caption; the page carries the game name for screen readers too
                    html = html.Replace("<div id=\"unity-loading-bar\">", "<div id=\"unity-loading-bar\" aria-label=\"Loading African Roads Online\">");
                    File.WriteAllText(indexPath, html);
                }
                Console.WriteLine("[ARO] WebBrand applied to " + indexPath);
            }
            catch (Exception e) { Console.Error.WriteLine("[ARO] WebBrand failed (build output is still valid): " + e.Message); }
        }

        static void Copy(string from, string to) { if (File.Exists(from)) File.Copy(from, to, true); else Console.WriteLine("[ARO] WebBrand: missing " + from); }

        const string Css = @"
html, body { margin: 0; height: 100%; overflow: hidden; background: #0a0c11; }
body { background: #0a0c11 url('aro-bg.jpg') center / cover no-repeat; font-family: 'Segoe UI', Roboto, Helvetica, Arial, sans-serif; }
body::before { content: ''; position: fixed; inset: 0; z-index: 0; background: radial-gradient(ellipse at center, rgba(6,8,12,.35), rgba(6,8,12,.78)); }
#unity-container, #unity-container.unity-desktop, #unity-container.unity-mobile { position: fixed !important; inset: 0; left: 0 !important; top: 0 !important; width: 100%; height: 100%; transform: none !important; z-index: 1; }
#unity-canvas { width: 100% !important; height: 100% !important; display: block; background: transparent !important; }
#unity-footer { display: none !important; }
#unity-loading-bar { position: absolute; left: 50%; top: 50%; transform: translate(-50%, -50%); text-align: center; width: 360px; }
#unity-logo { width: 150px !important; height: 150px !important; margin: 0 auto 28px !important; background: url('aro-logo.png') center / contain no-repeat !important; filter: drop-shadow(0 4px 18px rgba(0,0,0,.55)); }
#unity-progress-bar-empty { width: 100% !important; height: 8px; margin: 0 auto !important; padding: 0 !important; border-radius: 4px; background: rgba(255,255,255,.2) !important; overflow: hidden; }
#unity-progress-bar-full { height: 100% !important; width: 0%; border-radius: 4px; background: #f9b521 !important; box-shadow: 0 0 14px rgba(249,181,33,.7); }
#unity-loading-bar::after { content: 'AFRICAN ROADS ONLINE'; display: block; margin-top: 22px; color: #fff; font-weight: 700; letter-spacing: .32em; font-size: 14px; text-shadow: 0 2px 8px rgba(0,0,0,.7); }
#unity-warning { position: absolute; left: 50%; top: 5%; transform: translate(-50%); z-index: 3; }
";
    }
}
