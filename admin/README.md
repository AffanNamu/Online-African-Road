# African Roads Online - admin site

A static site (HTML, CSS, one JavaScript file; no framework, no dependency, no build step). Full description, roles, set-up and security notes: `docs/ADMIN.md`.

Run it locally against a project:
1. Copy `config.example.js` to `config.js` and fill in the project's public URL and **public** anon key (never a service-role key or a database password; `config.js` is git-ignored).
2. `python3 -m http.server 8080 --directory admin` and open http://localhost:8080 (any static file server works).

It is tested by `tools/admin_e2e.js` (see `docs/ADMIN.md`). Nothing here is deployed by the repository's workflows.
