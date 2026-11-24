using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace TortalPortal
{
    // Portal markers that stay on the map when nothing is open.
    //
    // The destination window has always marked portals, but only its own - added as it opened and removed
    // "the moment the window closes", which meant the one time you could see where your gates were was the
    // one time you were already standing at one. These are the same markers kept up permanently, drawn from
    // the server's published index, so the map is a map of the portal network whether or not anybody is
    // looking through a gate.
    //
    // Every portal in the world, by design: the index is the server's whole-world view and that is what gets
    // marked. On a server where people would rather not advertise where they live, PortalPinsOnMap is the
    // off switch.
    public static class PortalMapPins
    {
        private const float PollSeconds = 2f;

        private static Coroutine _routine;
        private static readonly List<Minimap.PinData> Pins = new List<Minimap.PinData>();

        // What the current markers were built from. The index blob changes only when the server republishes,
        // and the library revision only when icons arrive, so on a quiet world every poll costs one string
        // comparison and stops.
        private static string _builtFrom;
        private static int _builtRevision = -1;

        // Concealed gates reach the list on their own channel, so the index blob is unchanged when one of them
        // arrives. Without this the marker for a gate just discovered would not appear until the server's next
        // index publish happened to differ.
        private static int _builtConcealed = -1;
        private static bool _builtEnabled;
        private static bool _suspended;

        public static void OnZNetStart()
        {
            try
            {
                Reset();
                _routine = Plugin.Instance.StartCoroutine(PinRoutine());
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"TortalPortal: could not start the persistent map markers (non-fatal, the destination window still marks its own). Reason: {ex}");
            }
        }

        public static void Reset()
        {
            if (_routine != null && Plugin.Instance != null) Plugin.Instance.StopCoroutine(_routine);
            _routine = null;

            Clear();
            _builtFrom = null;
            _builtRevision = -1;
            _builtConcealed = -1;
            _builtEnabled = false;
            _suspended = false;
        }

        // The destination window marks the same portals, with a highlight on the selected one and the gate
        // underfoot added. Both sets at once would be two pins on every portal, so the window takes the map
        // while it is open and hands it back on close.
        public static void Suspend()
        {
            _suspended = true;
            Clear();
        }

        public static void Resume()
        {
            _suspended = false;

            // Forces the next poll to rebuild rather than deciding nothing has changed since the index is
            // still the one these markers were last built from.
            _builtFrom = null;
        }

        private static IEnumerator PinRoutine()
        {
            while (true)
            {
                yield return new WaitForSeconds(PollSeconds);
                Rebuild();
            }
            // ReSharper disable once IteratorNeverReturns
        }

        // Kept out of the coroutine so it can sit in a try/catch - C# forbids "yield return" inside a try
        // block with a catch clause.
        private static void Rebuild()
        {
            try
            {
                if (Minimap.instance == null || ZNet.instance == null) return;

                bool enabled = Configuration.showPortalPinsOnMap.Value;

                if (_suspended || !enabled)
                {
                    if (Pins.Count > 0) Clear();
                    _builtEnabled = enabled;
                    return;
                }

                string blob = PortalRegistry.SyncedIndex.Value ?? "";
                int revision = TortalPinLibrary.Revision;
                int concealed = PortalRegistry.ConcealedRevision;
                if (_builtEnabled == enabled && blob == _builtFrom && revision == _builtRevision && concealed == _builtConcealed) return;

                Clear();

                // The same icon rules the destination window applies in AddMapPins: a portal's own icon wins,
                // and falls back to the player's chosen one whenever this client cannot draw it yet.
                PortalPinIcon chosen = Configuration.portalPinIcon.Value;
                bool vanillaIcon = (int)chosen < 100;
                Minimap.PinType fallbackType = vanillaIcon ? (Minimap.PinType)(int)chosen : Minimap.PinType.Icon4;
                Sprite fallbackSprite = vanillaIcon ? null : TortalPinIcon.Get(chosen);

                foreach (PortalRegistry.Entry e in PortalRegistry.GetEntries())
                {
                    Sprite own = TortalPinIcon.GetByName(e.Icon);
                    Sprite sprite = own ?? fallbackSprite;
                    Minimap.PinType type = own != null ? Minimap.PinType.Icon4 : fallbackType;

                    // save:false, without exception. These are drawn and removed by us; one of them reaching
                    // the player's own saved pin list would be a marker they can never get rid of.
                    Minimap.PinData pin = Minimap.instance.AddPin(e.Pos, type, e.Tag ?? "", false, false);
                    if (pin == null) continue;

                    if (sprite != null) pin.m_icon = sprite;
                    Pins.Add(pin);
                }

                _builtFrom = blob;
                _builtRevision = revision;
                _builtConcealed = concealed;
                _builtEnabled = enabled;

                Minimap.instance.m_pinUpdateRequired = true;
                Configuration.LogDebug($"Persistent map markers rebuilt: {Pins.Count} portals.");
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"TortalPortal: the persistent map markers could not be rebuilt (non-fatal). Reason: {ex}");
            }
        }

        private static void Clear()
        {
            if (Pins.Count == 0) return;

            try
            {
                if (Minimap.instance != null)
                {
                    foreach (Minimap.PinData pin in Pins) Minimap.instance.RemovePin(pin);
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"TortalPortal: could not clear the persistent map markers (non-fatal). Reason: {ex}");
            }

            Pins.Clear();
        }
    }
}
