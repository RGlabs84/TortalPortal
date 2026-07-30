using BepInEx.Configuration;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace TortalPortal
{
    // Config migration.
    //
    // An admin's whitelist, blacklist and portal limits are real work, and BepInEx binds settings by
    // (section, key). The moment one is renamed or moved to another section the old line is an orphan: it is
    // not read, it is stripped out on the next save, and nothing anywhere says it ever existed. A server
    // owner updates the mod and their filter lists are simply gone.
    //
    // Same shape of problem as Fatty's food data file, so the same answer: stamp a version on the file, back
    // it up before touching it, and carry values across by hand.
    //
    // Note that settings which keep their section and key need nothing here - BepInEx reads those from the
    // existing file already. This exists for the ones that move.
    //
    // To add a migration:
    //   1. bump CurrentConfigVersion
    //   2. add an entry to Moves keyed by the NEW version, listing what moved where
    // Begin/Finish do the rest.
    public static class ConfigMigration
    {
        //   0 = unstamped. Any file written by 1.0.0, which had no migration system.
        //   1 = the 1.0.1 layout. Nothing was renamed between the two - PortalIndexRefreshSeconds and
        //       MenuOpenDistance were added, and BepInEx fills new keys in from their defaults on its own -
        //       so this step only stamps the file. The machinery is what matters from here on.
        public const int CurrentConfigVersion = 1;

        public sealed class Move
        {
            public string FromSection;
            public string FromKey;
            public string ToSection;
            public string ToKey;

            // Optional rewrite for when the meaning of the value changed too, not just its address.
            public Func<string, string> Transform;
        }

        // Keyed by the version the moves produce: Moves[n] takes a file at version n-1 up to version n.
        private static readonly Dictionary<int, Move[]> Moves = new Dictionary<int, Move[]>
        {
            { 1, new Move[0] }
        };

        // Raw "Section::Key" -> serialized value, read off disk before anything was bound.
        private static readonly Dictionary<string, string> Snapshot = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        // Values waiting to be pushed into their new home once binding has happened.
        private static readonly List<KeyValuePair<ConfigDefinition, string>> Pending = new List<KeyValuePair<ConfigDefinition, string>>();

        // Old slots a move has already claimed, so the "what got lost" report does not name them.
        private static readonly HashSet<string> Claimed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        private static int _fileVersion;
        private static bool _migrating;

        private static string Slot(string section, string key) => section + "::" + key;

        // ---- before any Bind ------------------------------------------------------------------------------

        public static void Begin(ConfigFile config)
        {
            Snapshot.Clear();
            Pending.Clear();
            Claimed.Clear();
            _fileVersion = CurrentConfigVersion;
            _migrating = false;

            try
            {
                string path = config.ConfigFilePath;
                if (!File.Exists(path)) return; // fresh install: nothing to carry, nothing to warn about

                ReadRaw(path, Snapshot);

                _fileVersion = ReadStampedVersion(Snapshot);
                if (_fileVersion >= CurrentConfigVersion) return;

                _migrating = true;
                Backup(path, _fileVersion);
                Plugin.Log.LogWarning($"TortalPortal: migrating {Path.GetFileName(path)} from config version {_fileVersion} to {CurrentConfigVersion}. Your settings are carried across; the previous file has been backed up beside it.");

                PlanMoves();
            }
            catch (Exception ex)
            {
                // A failed migration must never stop the mod loading - worst case the config binds as it
                // always would have, which is exactly the old behaviour.
                Plugin.Log.LogError($"TortalPortal: config migration could not start, settings will be read as-is. Reason: {ex}");
            }
        }

        private static void PlanMoves()
        {
            for (int version = _fileVersion + 1; version <= CurrentConfigVersion; version++)
            {
                if (!Moves.TryGetValue(version, out Move[] steps)) continue;

                foreach (Move move in steps)
                {
                    if (!Snapshot.TryGetValue(Slot(move.FromSection, move.FromKey), out string value)) continue;

                    // A value already sitting in the destination was put there deliberately and wins.
                    if (Snapshot.ContainsKey(Slot(move.ToSection, move.ToKey))) continue;

                    if (move.Transform != null) value = move.Transform(value);

                    Pending.Add(new KeyValuePair<ConfigDefinition, string>(new ConfigDefinition(move.ToSection, move.ToKey), value));
                    Claimed.Add(Slot(move.FromSection, move.FromKey));
                    Plugin.Log.LogInfo($"TortalPortal: carrying [{move.FromSection}] {move.FromKey} across to [{move.ToSection}] {move.ToKey}.");
                }
            }
        }

        // ---- after every Bind -----------------------------------------------------------------------------

        public static void Finish(ConfigFile config, ConfigEntry<int> versionEntry)
        {
            try
            {
                foreach (KeyValuePair<ConfigDefinition, string> carried in Pending)
                {
                    if (!config.ContainsKey(carried.Key))
                    {
                        Plugin.Log.LogWarning($"TortalPortal: nothing bound at [{carried.Key.Section}] {carried.Key.Key}, so its carried value was dropped.");
                        continue;
                    }

                    // Out-of-range values are clamped by BepInEx on the way in, so a stale value can never
                    // land somewhere the setting would not otherwise allow.
                    config[carried.Key].SetSerializedValue(carried.Value);
                }

                if (_migrating) ReportUnclaimed(config);

                if (versionEntry != null) versionEntry.Value = CurrentConfigVersion;
                config.Save();
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"TortalPortal: config migration could not finish, some settings may have reverted to defaults - check the backup beside your config file. Reason: {ex}");
            }
            finally
            {
                Snapshot.Clear();
                Pending.Clear();
                Claimed.Clear();
            }
        }

        // Anything that was in the old file, is not bound now, and was not carried anywhere is about to be
        // stripped by the next save. That is the "poof, it's gone" case, so it gets said out loud with the
        // value in the log and the backup named - rather than an admin discovering an empty whitelist weeks
        // later.
        private static void ReportUnclaimed(ConfigFile config)
        {
            List<string> lost = new List<string>();

            foreach (KeyValuePair<string, string> kv in Snapshot)
            {
                int split = kv.Key.IndexOf("::", StringComparison.Ordinal);
                if (split < 0) continue;

                string section = kv.Key.Substring(0, split);
                string key = kv.Key.Substring(split + 2);

                if (config.ContainsKey(new ConfigDefinition(section, key))) continue;
                if (Claimed.Contains(kv.Key)) continue;

                lost.Add($"[{section}] {key} = {kv.Value}");
            }

            if (lost.Count == 0) return;

            StringBuilder sb = new StringBuilder();
            sb.AppendLine($"TortalPortal: {lost.Count} setting(s) in your old config no longer exist in this version and are not carried anywhere. Their values are listed here and kept in the backup file:");
            foreach (string line in lost) sb.AppendLine("    " + line);
            Plugin.Log.LogWarning(sb.ToString().TrimEnd());
        }

        // ---- file handling --------------------------------------------------------------------------------

        private static void Backup(string path, int fromVersion)
        {
            try
            {
                string backup = path + $".v{fromVersion}.bak";
                File.Copy(path, backup, overwrite: true);
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"TortalPortal: could not back up the config before migrating ({ex.Message}). Migrating anyway.");
            }
        }

        private static int ReadStampedVersion(Dictionary<string, string> snapshot)
        {
            if (snapshot.TryGetValue(Slot(Configuration.MetaSection, Configuration.ConfigVersionKey), out string raw) &&
                int.TryParse(raw, out int version))
            {
                return version;
            }

            // No stamp at all: a 1.0.0 file, which is version 0 by definition.
            return 0;
        }

        // BepInEx config files are plain INI: [Section] headers, '#' comments, blank lines, and "Key = value"
        // where the value may itself contain '=' (a comma-separated prefab list, a keyboard shortcut).
        private static void ReadRaw(string path, Dictionary<string, string> into)
        {
            string section = "";

            foreach (string rawLine in File.ReadAllLines(path))
            {
                string line = rawLine.Trim();
                if (line.Length == 0 || line[0] == '#') continue;

                if (line[0] == '[' && line[line.Length - 1] == ']')
                {
                    section = line.Substring(1, line.Length - 2).Trim();
                    continue;
                }

                int eq = line.IndexOf('=');
                if (eq <= 0) continue;

                string key = line.Substring(0, eq).Trim();
                string value = line.Substring(eq + 1).Trim();
                if (key.Length == 0) continue;

                into[Slot(section, key)] = value;
            }
        }
    }
}
