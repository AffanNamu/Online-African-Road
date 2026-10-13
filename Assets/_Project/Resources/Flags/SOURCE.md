# National flags

`{iso2 lower case}.png`, 96x72 RGBA (4:3), one per country in `ARO.NetCore.CountryFlags.Codes`: every African state plus Western Sahara (55 flags).

* **Source**: [flag-icons](https://github.com/lipis/flag-icons) 7.2.3 (npm `flag-icons`), `flags/4x3/{code}.svg`.
* **Licence**: MIT, Copyright (c) 2013 Panayiotis Lipiridis. The licence text is `LICENSE-flag-icons.txt` in this folder and must stay with the files. MIT allows redistribution (including in this public repository) with that notice.
* **How they were made**: `python3 tools/gen_flags.py --src <extracted flag-icons package>` renders each SVG at 192x144 and averages it down to 96x72. `manifest.json` records the SVG and PNG SHA-256 of every flag.
* **Check**: `python3 tools/gen_flags.py --check` (no source needed) verifies the file set equals the C# code list, every PNG is 96x72 RGBA and matches `manifest.json`. CI runs it.
* **Use**: only through `Ui.Flag` / `Ui.CountryTag` / `Ui.TextWithCountry` (`Scripts/Game/Ui.cs`) and `FlagArt.Get`, which choose the file from the stored `country_code` and nothing else (never a name, address or location). A missing, malformed or artwork-less code draws a neutral placeholder box with a globe.
* **Adding a country**: add its code to `CountryFlags.Codes` (keep it sorted), run the generator, commit the new PNG, `manifest.json` and the updated licence file if it changed.
* Flags are national symbols; the artwork here is the flag-icons rendition. Disputed or changed flags (for example if a state adopts a new flag) are corrected by updating the source version and regenerating.
