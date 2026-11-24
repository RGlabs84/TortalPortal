using HarmonyLib;
using UnityEngine;
using System;

namespace TortalPortal
{
    [HarmonyPatch]
    public static class PortalBehaviorPatch
    {
        // The portal we have already auto-opened the menu for. This used to be a single shared bool, but every
        // loaded portal ticks UpdatePortal, so whichever other portal the player happened to be out of range of
        // cleared the flag each tick and the menu re-opened the instant it was dismissed. Tracking the instance
        // keeps "only auto-open once per approach" scoped to the portal actually being approached.
        private static TeleportWorld _autoOpenedFor;

        // The portal the player has just arrived through. Every loaded portal ticks UpdatePortal, so the
        // destination sees the player standing well inside its activation range the moment the trip finishes
        // and would throw the menu straight back up - which read as the menu never having closed at all.
        //
        // Recognised THREE ways, because the ZDOID alone was not enough. Travelling from the destination
        // list closed the menu correctly and travelling by double-clicking the map did not, and the two
        // paths call the same ExecuteTeleport with the same entry - so whatever the difference was, it was
        // in the state around the call rather than the call itself. Rather than pick one theory, all three
        // ways of noticing "this player has just come out of a gate" are checked, and the log records which
        // one answered so a future report is a diagnosis rather than another hunt.
        private static ZDOID _arrivedThrough = ZDOID.None;
        private static Vector3 _arrivedPos;

        // A trip has been started and its landing not yet gone stale. Everything below is scoped to it, so a
        // player who has not travelled can never be mistaken for one who has: the clock this now runs on
        // reads zero on a freshly spawned player too, and without this flag it would hold the menu shut at
        // the gate somebody logged out beside.
        private static bool _tripPending;

        // And whose trip it was. A world reload builds a new Player whose clock starts at zero all over
        // again, which would otherwise read as "just landed" to a note left over from before the reload.
        private static Player _tripBy;

        // The frame SuppressAutoOpenFor was last called on. Both routes this mod owns call it immediately
        // before TeleportTo, so the hook below can tell "the caller already wrote down which gate this is"
        // from "this trip came from somewhere else entirely" by whether the note was written this very frame.
        private static int _notedOnFrame = -1;

        // How long after LANDING a portal reporting the player in range is answering the ARRIVAL rather than
        // an approach.
        //
        // After landing, and that is the whole of this fix. It used to be measured from departure, which
        // could not ever work: vanilla's Player.UpdateTeleport will not end a DISTANT teleport before
        // m_teleportTimer passes 8s - and every trip this mod starts is distant - and then holds on until
        // ZNetScene.IsAreaReady and ZoneSystem.FindFloor both answer, up to 15s. A window of a few seconds
        // opened before TeleportTo had therefore always shut again before the player could possibly arrive,
        // so the gate they stepped out of was never recognised as an arrival at all and did the only other
        // thing it knows how to do: it opened its menu. No value of this constant could have saved that -
        // the clock it was read off was the wrong clock.
        //
        // The right one is vanilla's own. Player.m_teleportCooldown is zeroed when the trip starts, pinned
        // at zero every frame while it runs, and counts up from zero the instant it ends - it already IS
        // "seconds since this player landed", whatever the loading screen cost. No amount of world
        // streaming can outlast a window measured on it.
        private const float ArrivalGraceSeconds = 6f;

        // The blanket "they cannot have walked here yet" answer gets a far shorter leash than the two that
        // actually name the gate, because it suppresses EVERY gate rather than the one travelled to. The
        // gates of a hub stand a second's walk apart, and a player who lands and steps to the one next door
        // is asking for that menu deliberately.
        private const float UnidentifiedGraceSeconds = 1.5f;

        // Horizontal, and wide enough to cover the exit point (which is m_exitDistance out in front of the
        // gate) plus the portal's own activation range.
        private const float ArrivalRadius = 8f;

        // Called by the UI just before it hands off to TeleportTo, when it knows which gate is being
        // travelled to. No longer the only way a trip gets noticed - see the TeleportTo hook below.
        public static void SuppressAutoOpenFor(ZDOID portalId, Vector3 destination)
        {
            _arrivedThrough = portalId;
            _arrivedPos = destination;
            _notedOnFrame = Time.frameCount;
            _tripPending = true;
            _tripBy = Player.m_localPlayer;
        }

        // A trip that never happened must not leave a suppression behind - TeleportTo refuses during
        // cooldowns, and the stale entry would eat the next legitimate auto-open at that gate.
        public static void ClearArrival()
        {
            _arrivedThrough = ZDOID.None;
            _arrivedPos = Vector3.zero;
            _notedOnFrame = -1;
            _tripPending = false;
            _tripBy = null;
        }

        // Every trip the local player takes, whoever started it. The two routes this mod owns have written
        // the gate down already by the time this runs; a console teleport, another mod, or whatever route
        // gets added to this one next arrives with nothing - and still lands guarded, because the position
        // vanilla was handed is enough to recognise the gate the player steps out of. That is the part meant
        // to keep this dead: no future way of travelling can forget to tell the guard.
        [HarmonyPatch(typeof(Player), nameof(Player.TeleportTo))]
        [HarmonyPostfix]
        public static void Player_TeleportTo_Postfix(Player __instance, Vector3 pos, bool __result)
        {
            try
            {
                // A refused trip - cooldown, or somebody else's player to move - changes nothing.
                if (!__result || __instance == null || __instance != Player.m_localPlayer) return;

                if (_notedOnFrame != Time.frameCount)
                {
                    _arrivedThrough = ZDOID.None;
                    _arrivedPos = pos;
                }

                _tripPending = true;
                _tripBy = __instance;
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"TortalPortal: {nameof(Player_TeleportTo_Postfix)} failed (non-fatal, the destination gate may greet the arrival with its menu). Reason: {ex}");
            }
        }

        // Has this player just come out of THIS gate? Returns the reason it decided so, or null.
        private static string ArrivalReason(Player local, ZDOID portalId, Vector3 gate)
        {
            if (!_tripPending || local == null) return null;

            // A note from before a world reload belongs to a player who no longer exists.
            if (_tripBy != local)
            {
                ClearArrival();
                return null;
            }

            // Vanilla's own clock, read straight off the player: zero for the whole trip however long the
            // world takes to stream in, then counting up from the moment it ended.
            float sinceLanded = local.m_teleportCooldown;
            if (sinceLanded > ArrivalGraceSeconds)
            {
                // Answered, or never taken. Either way it stops standing over the next walk up to a gate.
                //
                // Logged with the figure, because this line is the fingerprint of the bug this whole guard
                // exists for: a note going stale BEFORE the gate it names has had its say is the shape the
                // failure takes, and the next time somebody reports the menu greeting them at the far end
                // the answer will be sitting in the log rather than waiting to be re-derived.
                Configuration.LogDebug($"A trip note was still standing {sinceLanded:0.0}s after this player landed, past the {ArrivalGraceSeconds}s grace. Treating this as an approach, not an arrival, and clearing it.");
                ClearArrival();
                return null;
            }

            // Every reason carries the figure the decision turned on, for the same reason.
            if (_arrivedThrough != ZDOID.None && _arrivedThrough == portalId)
                return $"matched the gate's ID, {sinceLanded:0.0}s after landing";

            // The ID comes from the published portal index, and an index row can outlive the ZDO it was
            // written from. Where the player asked to GO cannot go stale in the same way.
            Vector3 d = _arrivedPos - gate;
            d.y = 0f;
            if (d.sqrMagnitude <= ArrivalRadius * ArrivalRadius)
                return $"matched the destination position, {sinceLanded:0.0}s after landing";

            // Neither matched, and this is not the gate the trip was aimed at - but the player has only just
            // materialised and cannot have walked anywhere yet, so for a moment it answers the arrival too.
            if (sinceLanded <= UnidentifiedGraceSeconds)
                return $"within the arrival window, {sinceLanded:0.0}s after landing";

            return null;
        }

        [HarmonyPatch(typeof(TeleportWorld), "Awake")]
        [HarmonyPostfix]
        public static void TeleportWorld_Awake_Postfix(TeleportWorld __instance)
        {
            try
            {
                ApplyVFXMode(__instance);
                PortalGuard.ApplyTargeting(__instance);
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"TortalPortal: {nameof(TeleportWorld_Awake_Postfix)} failed. Reason: {ex}");
            }
        }

        // Bring one portal into line with whatever EnableCustomVFX currently says. Called from Awake for a
        // portal loading in, and from the config listener for every portal already standing.
        public static void ApplyVFXMode(TeleportWorld portal)
        {
            if (portal == null || portal.gameObject == null) return;

            TortalVanillaVFXState state = portal.gameObject.GetComponent<TortalVanillaVFXState>();

            // A skipped gate wears whatever its own prefab gave it - the same as the gateway switched off.
            if (Configuration.enableCustomVFX.Value && !SkippedPortals.IsSkipped(portal))
            {
                if (state == null) state = portal.gameObject.AddComponent<TortalVanillaVFXState>();
                state.Strip(portal);

                if (portal.gameObject.GetComponent<TortalPortalRuneVFX>() == null)
                {
                    portal.gameObject.AddComponent<TortalPortalRuneVFX>();
                }

                // The piece's own orange glow, brought over to the rune colour. Its own dial, because
                // somebody may well want the gateway green and the gate itself left as Valheim built it.
                state.SyncTint(portal, Configuration.vfxTintPortalFrame != null && Configuration.vfxTintPortalFrame.Value);
            }
            else
            {
                TortalPortalRuneVFX vfx = portal.gameObject.GetComponent<TortalPortalRuneVFX>();
                // Destroy tears down every emitter it built through its own OnDestroy, so the gateway leaves
                // nothing behind - without that this would strand a dozen orphaned child objects per portal.
                if (vfx != null) UnityEngine.Object.Destroy(vfx);

                // Vanilla VFX off means vanilla colours back too - the frame tint is part of the same look.
                if (state != null)
                {
                    state.RestoreTint();
                    state.Restore(portal);
                }
            }
        }

        // Every portal currently loaded. Only ever runs off a config change, so the cost of the scan is paid
        // once per dial turn rather than per frame.
        public static void RefreshLoadedPortals(bool rebuildGateway)
        {
            try
            {
                foreach (TeleportWorld portal in UnityEngine.Object.FindObjectsOfType<TeleportWorld>())
                {
                    if (portal == null) continue;

                    // Colour and orb height are baked when the gateway is built, so changing them means
                    // building it again. Density and brightness are read live every frame and do not.
                    if (rebuildGateway && Configuration.enableCustomVFX.Value && !SkippedPortals.IsSkipped(portal))
                    {
                        TortalPortalRuneVFX existing = portal.gameObject.GetComponent<TortalPortalRuneVFX>();
                        if (existing != null) existing.Rebuild();
                        else portal.gameObject.AddComponent<TortalPortalRuneVFX>();

                        // The frame tint is derived from the same colour, so it has to move with it - this
                        // branch skips ApplyVFXMode, which is where the tint is normally settled.
                        TortalVanillaVFXState state = portal.gameObject.GetComponent<TortalVanillaVFXState>();
                        if (state != null)
                        {
                            state.SyncTint(portal, Configuration.vfxTintPortalFrame != null && Configuration.vfxTintPortalFrame.Value);
                        }
                        continue;
                    }

                    ApplyVFXMode(portal);
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"TortalPortal: refreshing portal visuals after a settings change failed (non-fatal). Reason: {ex}");
            }
        }

        [HarmonyPatch(typeof(TeleportWorld), nameof(TeleportWorld.UpdatePortal))]
        [HarmonyPrefix]
        public static bool TeleportWorld_UpdatePortal_Prefix(TeleportWorld __instance, ref float ___m_colorAlpha, ref bool ___m_hadTarget)
        {
            try
            {
                var nview = __instance.GetComponent<ZNetView>();
                if (nview == null || !nview.IsValid() || __instance.m_proximityRoot == null) return true;
                if (SkippedPortals.IsSkipped(nview.GetZDO())) return true;

                Vector3 gate = __instance.m_proximityRoot.position;

                // Kept on the portal's own activation range: this drives the glow and the connection
                // effect, which should read the same for everyone regardless of anyone's menu preference.
                Player closestPlayer = Player.GetClosestPlayer(gate, __instance.m_activationRange);
                bool anyPlayerInRange = closestPlayer != null && !closestPlayer.IsTeleporting();

                // Measured against the local player directly rather than taken from GetClosestPlayer.
                // Standing at a portal with a friend a step nearer than you made that call return them, so
                // the window never opened for you at all.
                Player local = Player.m_localPlayer;
                bool localPlayerInRange = false;

                // The configuration window is opened deliberately - E at the gate, or the Configure button -
                // and E reaches roughly two metres further than MenuOpenDistance's 0.8m default. Judged by
                // the same rule as the destination window it was therefore "out of range" the instant it
                // appeared, and the out-of-range branch below closed it again on the very next frame: open,
                // shut, open, shut, for as long as you stood there. It gets the portal's own activation
                // range instead, which is the distance at which the gate is meaningfully yours to edit.
                bool configOpenHere = PortalUIManager.IsConfigUIVisible() && PortalUIManager.CurrentSourcePortal == __instance;

                // Either window - destination or configuration - already standing open on THIS gate. Both
                // get the wider boundary to leave by than to enter, so hovering on the line does not strobe.
                bool uiOpenHere = PortalUIManager.IsUIVisible() && PortalUIManager.CurrentSourcePortal == __instance;

                if (local != null && !local.IsTeleporting())
                {
                    float openRange = configOpenHere
                        ? __instance.m_activationRange
                        : Mathf.Min(Configuration.menuOpenDistance.Value, __instance.m_activationRange);
                    float closeMargin = Mathf.Max(0f, Configuration.menuCloseMargin.Value);
                    // A little further out to close than to open, so hovering on the boundary does not
                    // strobe the window. How much further is MenuCloseMargin.
                    float range = uiOpenHere ? openRange + closeMargin : openRange;

                    // TODO (flagged, not yet fixed): reported from play - the window opens about
                    // 1-1.2m IN FRONT of the gate whatever MenuOpenDistance is set to, and not at
                    // all when you stand at the gate itself. Suspicion is that m_proximityRoot is
                    // offset FORWARD on the vanilla portal prefab, not centred on it, so this test
                    // measures a disc that hangs out in front rather than one centred on the stones.
                    // Wanted instead: the trigger centred on the portal, protruding ~0.3m. VERIFY
                    // the prefab's local offset before changing anything - if that is the cause the
                    // fix is to measure from the portal transform, not from m_proximityRoot.
                    //
                    // HORIZONTAL distance only. m_proximityRoot sits above the ground while a player's
                    // transform is down at their feet, so a full 3D measure always carried a vertical
                    // component of roughly a metre - which is why no MenuOpenDistance below 1 could ever
                    // be satisfied, however close you stood. Flattening it also makes the setting mean
                    // what a player reads it to mean: how far away across the ground.
                    //
                    // But flat ALONE makes this an infinite column: someone on the floor above the gate, or
                    // flying over it, is nought metres away across the ground and the window opened in their
                    // face. The vertical component is not discarded any more, only judged on its own dial
                    // below, so the test is a disc of a cylinder rather than a column.
                    Vector3 toPlayer = local.transform.position - gate;
                    toPlayer.y = 0f;
                    bool withinDisc = toPlayer.magnitude <= range;

                    // Measured from the portal's own transform, not from m_proximityRoot: the transform is
                    // where the gate is planted and the proximity node hangs about a metre above it, so
                    // "how far above the gate are you standing" wants the footing or every player at the
                    // stones starts a metre in the hole.
                    float rise = local.transform.position.y - __instance.transform.position.y;
                    float verticalRange = Configuration.menuVerticalDistance != null
                        ? Mathf.Max(0.25f, Configuration.menuVerticalDistance.Value)
                        : 2f;
                    if (uiOpenHere) verticalRange += closeMargin;

                    localPlayerInRange = withinDisc && Mathf.Abs(rise) <= verticalRange;

                    // Metres on their own cannot tell the storey above a gate from a rock beside it - both
                    // read as two up. So when the player is genuinely above, look along the gate's own
                    // column for something solid between its footing and their feet: a floor there means
                    // another storey, and the window stays shut.
                    //
                    // Asked LAST and only when everything cheap has already passed, because this is a
                    // raycast and UpdatePortal ticks for every loaded portal every frame. Not asked at all
                    // while the window is already open here - a door swinging shut or a cart rolling
                    // through the column would otherwise slam it - and never asked below the gate, where
                    // the ground the portal is planted in is itself in the way.
                    if (localPlayerInRange
                        && !uiOpenHere
                        && rise > VerticalSightMinRise
                        && Configuration.menuRequireVerticalLineOfSight != null
                        && Configuration.menuRequireVerticalLineOfSight.Value
                        && !HasVerticalLineOfSight(__instance, rise))
                    {
                        Configuration.LogDebug($"Local player is {rise:0.0}m above this gate with something solid in between. Treating them as on another floor.");
                        localPlayerInRange = false;
                    }
                }

                bool canTeleport = false;
                if (localPlayerInRange)
                {
                    // 1.0: IsTeleportable takes the portal's own allow-all flag, exactly as vanilla's
                    // TeleportWorld now passes m_allowAllItems. The OR stays so this reads as it did pre-1.0.
                    canTeleport = local.IsTeleportable(__instance.m_allowAllItems) || __instance.m_allowAllItems || Configuration.IsAdmin() || Configuration.filterMode.Value == FilterMode.BypassAll;
                    if (!canTeleport) Configuration.LogDebug("Local player is in range but cannot teleport (likely carrying restricted items).");
                }

                UpdatePortalVisuals(__instance, anyPlayerInRange, canTeleport && localPlayerInRange, ref ___m_hadTarget);

                ZDO zdo = nview.GetZDO();
                ZDOID portalId = zdo.m_uid;

                // A corpse gate has no menu at either end. The owner walking up to the bed side is sent
                // straight through, once per approach - the same once-per-approach rule the menu uses, on the
                // same field, so backing off and returning goes again. Anyone else, and the grave side, get the
                // glow and nothing more.
                if (CorpseRun.IsCorpseGate(zdo))
                {
                    if (localPlayerInRange && canTeleport)
                    {
                        if (_autoOpenedFor != __instance && CorpseRun.IsOrigin(zdo) && CorpseRun.IsMine(zdo))
                        {
                            _autoOpenedFor = __instance;
                            CorpseRun.TravelToGrave(__instance);
                        }
                    }
                    else if (!localPlayerInRange && _autoOpenedFor == __instance)
                    {
                        _autoOpenedFor = null;
                    }
                    return false;
                }

                // A concealed gate nobody has found opens no menu on approach - it is found by pressing E on it,
                // not by walking past it, and a window that listed every destination BUT the gate you are
                // standing at would be a strange way to be told you had found nothing. The visuals above still
                // run, so the gate glows and reads as alive; only the menu waits.
                if (ConcealedPortals.IsUndiscoveredHere(zdo))
                {
                    if (!localPlayerInRange && _autoOpenedFor == __instance) _autoOpenedFor = null;
                    return false;
                }

                if (localPlayerInRange && canTeleport)
                {
                    // The latch is asked FIRST. Once this gate has been answered for this approach - menu
                    // opened, or arrival held shut - there is nothing left to decide until the player leaves
                    // its range and clears it below, so the arrival note is read once per approach rather
                    // than re-read every frame the player stands there.
                    if (_autoOpenedFor != __instance)
                    {
                        string arrival = ArrivalReason(local, portalId, gate);
                        if (arrival != null)
                        {
                            // Claim the portal as already handled instead of opening on it. Leaving its
                            // range clears _autoOpenedFor as usual, so walking away and coming back opens
                            // normally.
                            //
                            // The note is deliberately NOT consumed here. It used to be, which left it good
                            // for exactly one gate and one instance of that gate: whichever portal ticked
                            // first ate it, so a second gate close enough to the exit point opened its menu
                            // in the same frame, and a destination whose object was rebuilt by zone churn a
                            // moment after landing came back with nothing left holding it shut. It expires
                            // on its own clock instead, so every gate and every rebuild inside the window
                            // reads the same answer.
                            //
                            // Nor is it cleared from the out-of-range branch below: a player mid-teleport
                            // counts as out of range, so that would hand the menu straight back.
                            Configuration.LogDebug($"Arrived through portal ZDO {portalId} ({arrival}). Holding its menu closed until the player leaves and returns.");
                            _autoOpenedFor = __instance;
                        }
                        else if (!PortalUIManager.IsUIVisible())
                        {
                            Configuration.LogDebug($"Local player in range and can teleport. Showing UI for portal ZDO {portalId}.");
                            _autoOpenedFor = __instance;
                            PortalUIManager.ShowUI(__instance);
                        }
                    }
                }
                else if (!localPlayerInRange)
                {
                    if (_autoOpenedFor == __instance)
                    {
                        Configuration.LogDebug("Local player left range. Clearing auto-open state for this portal.");
                        _autoOpenedFor = null;
                    }
                    if (PortalUIManager.IsUIVisible() && PortalUIManager.CurrentSourcePortal == __instance)
                    {
                        Configuration.LogDebug("Local player left range while UI was open. Hiding UI.");
                        PortalUIManager.HideUI();
                    }
                }

                return false; // Skip vanilla target connections
            }
            catch (Exception ex)
            {
                if (_autoOpenedFor == __instance) _autoOpenedFor = null;
                Plugin.Log.LogError($"TortalPortal: {nameof(TeleportWorld_UpdatePortal_Prefix)} failed, falling back to vanilla portal update. Reason: {ex}");
                return true;
            }
        }

        // How far above the gate's footing a player has to be before looking for a floor between them is
        // worth a raycast. Under this they are on the gate's own level - a step, a slope, the top of a
        // jump - and there is nothing that could be in the way.
        private const float VerticalSightMinRise = 0.6f;

        // The cast starts this far above the footing so it begins clear of whatever the gate is planted on.
        private const float VerticalSightSkin = 0.1f;

        // Reused, because this runs from UpdatePortal and UpdatePortal ticks for every loaded portal every
        // frame. Sixteen is far more than a couple of metres of column can hold; were one ever to overflow
        // it, the hits that did fit still answer, and any one of them is enough to say "blocked".
        private static readonly RaycastHit[] _verticalSightHits = new RaycastHit[16];
        private static int _verticalSightMask = -1;
        private static bool _verticalSightWarned;

        // Is the gate's own vertical column clear between its footing and a player standing above it?
        //
        // Metres alone cannot tell the storey above a portal from a rock beside it - both read as two up -
        // so this asks whether anything solid is in between. The column looked along is the GATE'S, not the
        // line between the two of them: a line-of-sight ray would be cut by the edge of the very platform a
        // portal stands on, which is one of the commonest places to build one. Nothing you put beside a gate
        // can shut its window; only something over it.
        //
        // Fails OPEN. A raycast that throws, or a physics scene with nothing to say, must never be the
        // reason a gate stops answering - the distance tests have already had their say by the time we
        // get here.
        private static bool HasVerticalLineOfSight(TeleportWorld portal, float rise)
        {
            try
            {
                if (_verticalSightMask == -1)
                {
                    // Solid world only. Default, static_solid and Default_small are the world's own props
                    // and rock, piece is everything players build, terrain is the ground, vehicle is a deck
                    // you can stand on. Deliberately NOT piece_nonsolid - a ladder, a banner or a chain
                    // hung over a gate is not a floor - and not the character layers, because a friend
                    // standing on the stones is not a ceiling.
                    _verticalSightMask = LayerMask.GetMask("Default", "static_solid", "Default_small", "piece", "terrain", "vehicle");
                }

                float distance = rise - VerticalSightSkin;
                if (distance <= 0f) return true;

                Vector3 origin = portal.transform.position + Vector3.up * VerticalSightSkin;
                int count = Physics.RaycastNonAlloc(origin, Vector3.up, _verticalSightHits, distance, _verticalSightMask, QueryTriggerInteraction.Ignore);
                for (int i = 0; i < count; i++)
                {
                    Collider hit = _verticalSightHits[i].collider;
                    if (hit == null) continue;
                    // The gate's own stones stand in the gate's own column, naturally. IsChildOf counts
                    // the transform itself, so this covers both the root and everything hung off it -
                    // which is how a custom portal prefab carries its arch.
                    if (hit.transform.IsChildOf(portal.transform)) continue;
                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                // Once. This sits on a per-frame path, and a fault here would otherwise write a line a
                // frame for as long as somebody stood on a roof.
                if (!_verticalSightWarned)
                {
                    _verticalSightWarned = true;
                    Plugin.Log.LogWarning($"TortalPortal: the vertical line-of-sight check failed, so gates will answer on distance alone. Reason: {ex}");
                }
                return true;
            }
        }

        // Purely cosmetic, and kept in its own try/catch on purpose: EffectFade.SetActive reaches into
        // ParticleSystem modules, so anything that invalidates those systems throws from here. Losing the glow
        // must never cost us the teleport menu, which is exactly what happened when this ran inline.
        private static void UpdatePortalVisuals(TeleportWorld portal, bool anyPlayerInRange, bool localCanTeleport, ref bool hadTarget)
        {
            try
            {
                // Connection effect fires for ANY player (multiplayer friendly), not just the local one.
                if (anyPlayerInRange && !hadTarget && portal.m_connected != null)
                {
                    portal.m_connected.Create(portal.transform.position, portal.transform.rotation);
                }

                // The last vanilla visual: the "target found" flare. With the custom VFX on it is replaced
                // outright by the rune swirl and stays dark; with the custom VFX off, vanilla behaviour -
                // lit only when the LOCAL player can actually teleport.
                if (portal.m_target_found != null)
                {
                    portal.m_target_found.SetActive(!Configuration.enableCustomVFX.Value && localCanTeleport);
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"TortalPortal: portal visuals update failed (non-fatal, teleport menu unaffected). Reason: {ex}");
            }
            finally
            {
                hadTarget = anyPlayerInRange;
            }
        }

        [HarmonyPatch(typeof(TeleportWorld), "Update")]
        [HarmonyPrefix]
        public static bool TeleportWorld_Update_Prefix(TeleportWorld __instance, ref float ___m_colorAlpha, ref bool ___m_hadTarget)
        {
            try
            {
                if (SkippedPortals.IsSkipped(__instance)) return true;

                ___m_colorAlpha = Mathf.MoveTowards(___m_colorAlpha, ___m_hadTarget ? 1f : 0f, Time.deltaTime);
                __instance.m_model.material.SetColor("_EmissionColor", Color.Lerp(__instance.m_colorUnconnected, __instance.m_colorTargetfound, ___m_colorAlpha));

                var vfx = __instance.GetComponent<TortalPortalRuneVFX>();
                if (vfx != null)
                {
                    // Ensure the effect matches vanilla target discovery activation state
                    vfx.Charge = ___m_hadTarget ? 1f : 0.2f; 
                }

                PortalGuard.Tick(__instance);

                return false;
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"TortalPortal: {nameof(TeleportWorld_Update_Prefix)} failed, falling back to vanilla update. Reason: {ex}");
                return true;
            }
        }

        // Intercept vanilla teleport to do nothing if called manually, since we handle it directly - except on
        // a gate in SkipPortalPrefabs, which travels the way vanilla (or the mod that owns it) sends it.
        [HarmonyPatch(typeof(TeleportWorld), nameof(TeleportWorld.Teleport))]
        [HarmonyPrefix]
        public static bool TeleportWorld_Teleport_Prefix(TeleportWorld __instance)
        {
            try
            {
                if (SkippedPortals.IsSkipped(__instance)) return true;
                return false;
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"TortalPortal: {nameof(TeleportWorld_Teleport_Prefix)} failed, falling back to vanilla teleport. Reason: {ex}");
                return true;
            }
        }
    }
}
