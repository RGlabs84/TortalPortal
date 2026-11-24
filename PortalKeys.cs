namespace TortalPortal
{
    // Every mod-private ZDO key the corpse run, the portal guard and the health scan write, in one place.
    //
    // Portals save wholesale with no schema, so a key collision between two features corrupts a world file
    // silently. The older keys this mod already writes - "PortalPIN", "PortalSnapshot", the icon name, the
    // scatter marker - are spelled where they are used and are not moved here; everything new goes through
    // this class so the set can be read in one screen.
    public static class PortalKeys
    {
        // ---- corpse gates (server-minted, ephemeral) -----------------------------------------------------

        // What a server-minted portal is for: "corpse:origin" (the bed-side gate) or "corpse:dest" (the grave
        // gate). Absent on every player-built portal, which is how GetAllPortals tells the two apart.
        public const string Kind = "TortalPortal_kind";
        public const string KindCorpseOrigin = "corpse:origin";
        public const string KindCorpseDest = "corpse:dest";

        // The player a corpse gate belongs to - PlayerProfile.GetPlayerID(), the same number Piece.SetCreator
        // stamps into s_creator - so the client can tell its own gate from somebody else's.
        public const string Owner = "TortalPortal_owner";

        // On the origin gate: where the grave gate stands and which way it faces, so the client can travel
        // without holding the far end's ZDO. Position and rotation both, because the far end is a thousand
        // metres away in a zone the client has never been sent.
        public const string DestPos = "TortalPortal_dest";
        public const string DestRotY = "TortalPortal_destrot";

        // Set on both ends of a corpse gate: the guard refuses damage to this portal whatever the
        // SelfHealingPortals dial says.
        public const string Immune = "TortalPortal_immune";

        // A harmless counter bumped after every server-side write so the write's data revision strictly
        // exceeds anything a client might have landed in the same tick. See ClaimAndWrite.
        public const string Revision = "TortalPortal_rev";

        // ---- concealed gates (server-seeded, discovered by hand) -----------------------------------------

        // Kind of a gate the world was seeded with that nobody has been told about. Withheld from the
        // published portal index entirely, so no client holds its position until the server hands that one
        // gate to that one player - see ConcealedPortals.
        public const string KindConcealed = "concealed";

        // Who has found this gate: s_playerID values, comma-joined, written only by the server. A list rather
        // than a flag because discovery is per-player by default, and on the ZDO rather than in a side file
        // because that persists in the world save, survives a client reinstall, and is not editable by the
        // player it is about.
        public const string Found = "TortalPortal_found";

        // Set instead of appending to Found when ConcealedDiscoveryShared is on: the first player to touch the
        // gate reveals it for the whole server. Read alongside Found, never in place of it, so turning the dial
        // on does not un-find what individuals already found.
        public const string FoundAll = "TortalPortal_foundall";

        // ---- spawn hint (written by the client onto its own character ZDO) ------------------------------

        // Where this player will respawn - PlayerProfile's custom spawn point, which only the client knows.
        // The server reads it off the character ZDO the way it reads s_playerID, so a corpse gate can be
        // raised at the bed the player actually wakes up at rather than at a bed the server guessed.
        public const string SpawnPoint = "TortalPortal_spawn";
        public const string SpawnPointSet = "TortalPortal_spawnset";

        // ---- vanilla's own LoadFields keys ----------------------------------------------------------------
        //
        // Not mod-private data: ZNetView.LoadFields reads "HasFields", then "HasFields<Component>", then
        // "<Component>.<m_field>" off the ZDO and pokes the values into the live component before its Awake
        // runs. Spelled here so every writer uses the same literals.
        public const string HasFields = "HasFields";
        public const string HasFieldsPiece = "HasFieldsPiece";
        public const string PieceCanBeRemoved = "Piece.m_canBeRemoved";
        public const string PieceRandomTarget = "Piece.m_randomTarget";
        public const string PiecePrimaryTarget = "Piece.m_primaryTarget";
        public const string HasFieldsWearNTear = "HasFieldsWearNTear";
        public const string WearNTearHealth = "WearNTear.m_health";
        public const string WearNTearNoSupportWear = "WearNTear.m_noSupportWear";
        public const string WearNTearNoRoofWear = "WearNTear.m_noRoofWear";
        public const string WearNTearSnowDamageImmune = "WearNTear.m_snowDamageImmune";
        public const string WearNTearAshDamageImmune = "WearNTear.m_ashDamageImmune";
        public const string WearNTearBurnable = "WearNTear.m_burnable";
        public const string HasFieldsTeleportWorld = "HasFieldsTeleportWorld";
        public const string TeleportWorldExitDistance = "TeleportWorld.m_exitDistance";
    }
}
