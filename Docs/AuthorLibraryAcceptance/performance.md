# Author library acceptance

2026-10-08T15:57:50.1639709Z

- User CRUD, aliases, role isolation, conflict decisions, relation disable/unlink, delete reference checks, cache lifecycle and JSON import/export passed.
- Read-only public DB, stable-key ID changes, split/delete/missing identities, retained overrides, version-bound fallback, explicit rebind and restore passed.
- Single-author edits invalidate only related recognition and durable plan rows; file identity index is preserved.
- Real public database: 68936 artists, 94280 lookup keys; cold index 1965 ms.
- Real names: 10000 queries, 1967 ms; resolved 9870, ambiguous 130.
- Simulation 首次扫描: files 8490, hits 0, recalculated 8490, total 83474 ms.
- Simulation 缓存扫描: files 8490, hits 8490, recalculated 0, total 54 ms.
- Simulation 增量扫描: files 8490, hits 8405, recalculated 85, total 1011 ms.
- Real indexed scan (configured source/target, read-only files): 8490 files; first planning 7528 ms; durable repeated-plan read 110 ms; hits 8490; recalculated 0; source rediscovery 0. Author source 0 ms, target index 59 ms, recognition preparation 172 ms.
- Real public names, 8490-file simulation and actual persistent-index planning passed; all original user/public/index/settings bytes unchanged.