<!-- CHECKPOINT -->
- **Updated:** 2026-11-23 (session with the owner)
- **Current task:** 1.6.3 = `SkipPortalPrefabs` (leave other mods' portals alone) + README schema v3.1 + matching PDB. Built, server-tested, folded. NOT published, NOT committed.
- **Next step:** owner reviews `HexiumDist/README.md` and the CHANGELOG entry; when he says "hex web", upload `HexiumDist/TortalPortal-v1.6.3-hexium.zip` (sha256 53145509...). Then a real-client check at a modded portal listed in SkipPortalPrefabs (window must NOT open, the portal's own behaviour must work).
- **Blocked on:** owner go for publishing; a client playtest.
- **Files in progress:** none (all saved). This local copy (`~/WubarrkCODING/TortalPortal`) is AHEAD of TheWall's `ValheimMods/TortalPortal`; the owner does the backup.
- **Open questions for the owner:** sync floor kept at 1.5.0 (a 1.6.2 client still takes a skipped gate over for itself only); raise to 1.6.3 instead?
<!-- /CHECKPOINT -->

# TortalPortal status

- 1.6.3 checks: Release build 0 errors / 0 warnings; pdb_match PASS (no home path); fold PASS (15 entries, DLL a0b2f228..., PDB 2f60bd0f...);
  forge PASS (36 of 36 badges 200); Thunderstore render 36 of 36 images, 14 of 14 details, URLs identical; screenshots looked at.
- Server test (Linux 1.0.21 dedicated, 121-portal world, export total_portals): 5 of 5 PASS, 0 errors. Script: ~/valheim-testbed is the rig
  (profile-tortal-skip, saves-tortal-skip are throwaway copies).
- NOT ASSESSED: client side of SkipPortalPrefabs (Interact/SetText/Update/Teleport fall-through) - needs a real client.
- README: edit README.src.md, never HexiumDist/README.md; `python3 tools/fold.py [--check-badges]` forges and folds.
