# DDD work list

## Done (0.1.0, 2026-11-17)
- Core: WMI inventory, Windows Update / Update Catalog / NVIDIA / BIOS (Dell, HP, Lenovo, board-maker link) sources, merge, HTML/JSON report
- Catalog: throttle detection + backoff, one search at a time, 6-hour disk cache
- PC maker drivers (Dell PCIInfo, HP .cva [Devices], Lenovo _Driver/_PnPID) + "prefer maker's drivers" merge
- CLI `ddd` (incl. `ddd model`), dark Avalonia GUI, 77 unit tests

## Done (0.2.0, 2026-11-17)
- Download all / Install: downloads to %TEMP%\DDD\downloads, SHA-1 (catalog file name) / SHA-256 (Dell, HP, Lenovo) / Authenticode checks,
  .cab extracted and installed with pnputil in one elevated script (restore point first), vendor .exe installers run in turn; BIOS download-only
- ddd-cli scan --download

## Ideas
- AMD Radeon and Intel Arc/UHD drivers straight from the vendors (Intel's catalog entries lag the Intel site)
- Optional: download to a folder with SHA-256 check
