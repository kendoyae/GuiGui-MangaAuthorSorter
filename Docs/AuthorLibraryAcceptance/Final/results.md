# Author library acceptance

2026-10-08T18:16:37.0913959Z

- User CRUD, aliases, role isolation, conflict decisions, relation disable/unlink, delete reference checks, cache lifecycle and JSON import/export passed.
- Session identity cache refresh, collision-free IDs, persistent promotion with provider metadata/relationships and temporary deletion passed.
- Provider namespaces, translation-source exclusion, confirmed identity protection and independent legacy import identities passed.
- Read-only public DB, stable-key ID changes, split/delete/missing identities, retained overrides, version-bound fallback, explicit rebind and restore passed.
- Single-author edits invalidate only related recognition and durable plan rows; file identity index is preserved.
- UI fixture: 5,000 authors, zh-CN, font scale 100%, construction/render 1162 ms.
- UI fixture: 5,000 authors, zh-CN, font scale 150%, construction/render 842 ms.
- UI fixture: 5,000 authors, en-US, font scale 100%, construction/render 705 ms.
- UI fixture: 5,000 authors, en-US, font scale 150%, construction/render 749 ms.
- UI fixture: 5,000 authors, de-DE, font scale 100%, construction/render 695 ms.
- UI fixture: 5,000 authors, de-DE, font scale 150%, construction/render 740 ms.
- Unified entity page constructed and rendered with 5,000 entities in Chinese, English and German at 100%/150% font scales.
- Real public database: 68936 artists, 94280 lookup keys; cold index 1739 ms.
- Real names: 10000 queries, 363 ms; resolved 9870, ambiguous 130.
- Controlled pre-migration vs unified data, same file order/target/settings: 8490 files, 0 author/identity/target/status differences.
- First durable plan/dependency write: 852 ms.
- Real indexed scan (configured source/target, read-only files): 8490 files; first planning 5603 ms; durable repeated-plan read 97 ms; hits 8490; recalculated 0; source rediscovery 0. Author source 0 ms, target index 68 ms, recognition preparation 197 ms.
- Public-overlay scan dependency regression: 8490 files, 455 ms, zero per-file public identity queries; cancelable plan reads and transactional write cancellation passed.
- Prior saved scan comparison: 8490 comparable plans, 0 differing author/identity/target/status results.
- Real public names, 8490-file simulation and actual persistent-index planning passed; all original user/public/index/settings bytes unchanged.