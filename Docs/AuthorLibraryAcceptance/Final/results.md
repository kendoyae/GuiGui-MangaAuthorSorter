# Author library acceptance

2026-10-08T17:35:51.8566751Z

- User CRUD, aliases, role isolation, conflict decisions, relation disable/unlink, delete reference checks, cache lifecycle and JSON import/export passed.
- Session identity cache refresh, collision-free IDs, persistent promotion with provider metadata/relationships and temporary deletion passed.
- Provider namespaces, translation-source exclusion, confirmed identity protection and independent legacy import identities passed.
- Read-only public DB, stable-key ID changes, split/delete/missing identities, retained overrides, version-bound fallback, explicit rebind and restore passed.
- Single-author edits invalidate only related recognition and durable plan rows; file identity index is preserved.
- UI fixture: 5,000 authors, zh-CN, font scale 100%, construction/render 1302 ms.
- UI fixture: 5,000 authors, zh-CN, font scale 150%, construction/render 847 ms.
- UI fixture: 5,000 authors, en-US, font scale 100%, construction/render 757 ms.
- UI fixture: 5,000 authors, en-US, font scale 150%, construction/render 777 ms.
- UI fixture: 5,000 authors, de-DE, font scale 100%, construction/render 751 ms.
- UI fixture: 5,000 authors, de-DE, font scale 150%, construction/render 810 ms.
- Unified entity page constructed and rendered with 5,000 entities in Chinese, English and German at 100%/150% font scales.
- Real public database: 68936 artists, 94280 lookup keys; cold index 1893 ms.
- Real names: 10000 queries, 400 ms; resolved 9870, ambiguous 130.
- Controlled pre-migration vs unified data, same file order/target/settings: 8490 files, 0 author/identity/target/status differences.
- First durable plan/dependency write: 182 ms.
- Real indexed scan (configured source/target, read-only files): 8490 files; first planning 5897 ms; durable repeated-plan read 89 ms; hits 8490; recalculated 0; source rediscovery 0. Author source 0 ms, target index 72 ms, recognition preparation 207 ms.
- Prior saved scan comparison: 8490 comparable plans, 41 differing author/identity/target/status results.
- Explained historical change: 6 files. Entity identity conflict: automatic new-author assignment is now blocked and user confirmation is required.
- Explained historical change: 34 files. Canonical identity is unchanged. New-author group allocation shifts because conflicting synthetic authors are no longer allocated earlier in the batch.
- Explained historical change: 1 files. The file already occupies the selected existing author folder. Removing an earlier conflicting synthetic author prevents virtual-folder interference; no move is proposed.
- Real public names, 8490-file simulation and actual persistent-index planning passed; all original user/public/index/settings bytes unchanged.