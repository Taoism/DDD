# DDD work list

## Done (0.1.0, 2026-11-17)
- Core: WMI inventory, Windows Update / Update Catalog / NVIDIA / BIOS (Dell, HP, Lenovo, board-maker link) sources, merge, HTML/JSON report
- Catalog: throttle detection + backoff, one search at a time, 6-hour disk cache
- PC maker drivers (Dell PCIInfo, HP .cva [Devices], Lenovo _Driver/_PnPID) + "prefer maker's drivers" merge
- CLI `ddd` (incl. `ddd model`), dark Avalonia GUI, 77 unit tests

## Ideas
- AMD Radeon and Intel Arc/UHD drivers straight from the vendors (Intel's catalog entries lag the Intel site)
- Installer + GitHub release (like TaoExif)
- Optional: download to a folder with SHA-256 check
