# Vendored Front-End Libraries

All third-party static assets are **vendored** (committed to the repo — no CDN, no
package manager at runtime). **This file is the authoritative version manifest.**
When you upgrade a library, update this file first.

| Library | Pinned version | Folder | License | Used by |
|---|---|---|---|---|
| Bootstrap | **5.3.3** | `lib/bootstrap/dist/` | MIT | `index.html` (only `css/bootstrap.min.css`; the `dist/js` bundle is vendored but **not referenced** — Blazor handles interactivity) |
| Bootstrap Icons | **1.13.1** | `lib/bootstrap-icons/` | MIT | `index.html` (`bootstrap-icons.min.css` + `fonts/`) |
| Peyda (PeydaWeb) | **4.1** | `wwwroot/fonts/Peyda/` | Commercial (fontiran.com) | `@font-face` rules in `css/app.css` (NOT the vendor's `fontiran.css`/`style.css` — those are unused) |

> Folder names are intentionally **not** versioned (`lib/bootstrap/`, not
> `lib/bootstrap-5.3.3/`): Visual Studio's file watchers lock `lib/` and block
> renames on Windows. Pin the version here instead. The version is also embedded
> in each file's header comment (e.g. `Bootstrap v5.3.3` in the minified CSS).

## Upgrade checklist

1. Replace the files under the folder above (keep the folder path the same).
2. **Verify the Bootstrap font contract is intact** — see the *Typography & Fonts
   (Peyda)* section in `AGENTS.md`. Specifically:
   - `css/app.css` `:root` still overrides `--bs-font-sans-serif`,
     `--bs-body-font-family`, `--bs-btn-font-family` with `var(--font-fa)`.
   - In a browser, `getComputedStyle` on `.tooltip`, `.popover`, `.btn` and
     `.form-control` still starts with `PeydaWeb`.
3. Bump the table above.

## Version provenance

- Bootstrap 5.3.3 — downloaded from https://getbootstrap.com/ (dist zip, CSS + JS bundle).
- Bootstrap Icons 1.13.1 — downloaded from https://icons.getbootstrap.com/.
- Peyda 4.1 — WebFonts distribution from https://fontiran.com/ (commercial license;
  see the header of `wwwroot/fonts/Peyda/01-Standard/WebFonts/fontiran.css`).
