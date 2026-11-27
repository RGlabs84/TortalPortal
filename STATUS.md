<!-- CHECKPOINT -->
- **Updated:** 2026-11-27 (session with the owner)
- **Current task:** 1.6.5 PUBLISHED (owner: "approved to pub on all"): Thunderstore + Hexium `Wubarrk-TortalPortal-1.6.5` (Hexium upload id 562), GitHub RGlabs84/TortalPortal release/v1.6.5 PR + tag + release v1.6.5. One zip everywhere (sha256 3c990f23...). 1.6.5 = the grave ward (CorpseWard.cs, `CorpseRunArrivalWardSeconds` synced, default 20, 0 = off, ends on a swing) + corpse gate no longer latches dead inside vanilla's 2 s teleport cooldown.
- **Tested:** client autotest (tools/tortal-autotest, single-player 1.0.31) PASS 39 of 39, 0 error lines (~/valheim-testbed/autotest-tortal/results-20261127-082217). Rerun: `~/valheim-testbed/heavy.sh 10G tortal-client tools/tortal-autotest/run-autotest.sh`.
- **Next step:** none required. If a store API still shows 1.6.4, re-read `latest` a few minutes later (both lag an upload).
- **Blocked on:** nothing.
- **Files in progress:** none. This local copy is ahead of TheWall's; the owner does the backup.
- **Open questions for the owner:** four stray files still uncommitted on purpose (Prefabs.zip, odinmount.blob, valtima.jpeg, addIcon.png).
<!-- /CHECKPOINT -->

# TortalPortal status

- 1.6.4 checks (code identical to the tested 1.6.3-numbered build; the 1.6.4 rebuild was NOT re-tested, owner said skip): Release build 0 errors / 0 warnings; pdb_match PASS (no home path); fold PASS (15 entries, DLL a0b2f228..., PDB 2f60bd0f...);
  forge PASS (36 of 36 badges 200); Thunderstore render 36 of 36 images, 14 of 14 details, URLs identical; screenshots looked at.
- Server test (Linux 1.0.21 dedicated, 121-portal world, export total_portals): 5 of 5 PASS, 0 errors. Script: ~/valheim-testbed is the rig
  (profile-tortal-skip, saves-tortal-skip are throwaway copies).
- NOT ASSESSED: client side of SkipPortalPrefabs (Interact/SetText/Update/Teleport fall-through) - needs a real client.
- README: edit README.src.md, never HexiumDist/README.md; `python3 tools/fold.py [--check-badges]` forges and folds.
