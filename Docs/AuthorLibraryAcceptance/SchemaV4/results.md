# Author library acceptance

2026-10-08T18:48:12.9921347Z

- User CRUD, aliases, role isolation, conflict decisions, relation disable/unlink, delete reference checks, cache lifecycle and JSON import/export passed.
- Session identity cache refresh, collision-free IDs, persistent promotion with provider metadata/relationships and temporary deletion passed.
- Provider namespaces, translation-source exclusion, confirmed identity protection and independent legacy import identities passed.
- Read-only public DB, stable-key ID changes, split/delete/missing identities, retained overrides, version-bound fallback, explicit rebind and restore passed.
- Schema v4 compatibility views, restored SourceId labels, Danbooru tags, provider external identity, search/group lookup and read-only overrides passed.
- Single-author edits invalidate only related recognition and durable plan rows; file identity index is preserved.
- UI fixture: 5,000 authors, zh-CN, font scale 100%, construction/render 1277 ms.
- UI fixture: 5,000 authors, zh-CN, font scale 150%, construction/render 749 ms.
- UI fixture: 5,000 authors, en-US, font scale 100%, construction/render 672 ms.
- UI fixture: 5,000 authors, en-US, font scale 150%, construction/render 721 ms.
- UI fixture: 5,000 authors, de-DE, font scale 100%, construction/render 669 ms.
- UI fixture: 5,000 authors, de-DE, font scale 150%, construction/render 731 ms.
- Unified entity page constructed and rendered with 5,000 entities in Chinese, English and German at 100%/150% font scales.
- Actual builder Schema v4 output: 68936 artists, 94280 lookup keys, cold index 1885 ms; source labels, provider identities, search and overrides passed; database bytes unchanged.