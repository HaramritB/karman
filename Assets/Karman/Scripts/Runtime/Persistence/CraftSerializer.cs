using System;
using System.IO;
using Karman.Core.Vehicles;
using UnityEngine;

namespace Karman.Persistence
{
    /// <summary>Versioned JSON craft files (*.craft.json). Older formats are migrated on load.</summary>
    public static class CraftSerializer
    {
        public static string ToJson(Craft craft)
        {
            craft.FormatVersion = Craft.CurrentFormatVersion;
            return JsonUtility.ToJson(craft, true);
        }

        public static Craft FromJson(string json)
        {
            var craft = JsonUtility.FromJson<Craft>(json);
            if (craft == null) throw new InvalidDataException("Not a craft file");
            if (craft.FormatVersion > Craft.CurrentFormatVersion)
                throw new InvalidDataException($"Craft format {craft.FormatVersion} is newer than this build ({Craft.CurrentFormatVersion})");
            Migrate(craft);
            return craft;
        }

        /// <summary>Upgrade path; each step bumps FormatVersion by one.</summary>
        static void Migrate(Craft craft)
        {
            if (craft.FormatVersion < 1) craft.FormatVersion = 1; // v0 → v1: no structural changes (pre-release files)
        }

        public static string CraftDirectory
        {
            get
            {
                string dir = Path.Combine(Application.persistentDataPath, "Craft");
                Directory.CreateDirectory(dir);
                return dir;
            }
        }

        public static string Save(Craft craft)
        {
            string safe = string.Join("_", craft.Name.Split(Path.GetInvalidFileNameChars()));
            string path = Path.Combine(CraftDirectory, safe + ".craft.json");
            File.WriteAllText(path, ToJson(craft));
            return path;
        }

        public static Craft Load(string path) => FromJson(File.ReadAllText(path));
    }
}
