using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Freznel.FzAdditions.Util
{
    //Reading the mod's own data folders (such as fz/fabrication and fz/sites) from every loaded mod, in the game's load
    //order, with the game's own JSON reader
    public static class ModData
    {
        //aModPaths has each mod put at its front as it loads, so the load order is the list reversed
        public static IEnumerable<string> ModPathsInLoadOrder()
        {
            if (DataHandler.aModPaths == null) return Enumerable.Empty<string>();
            return DataHandler.aModPaths.AsEnumerable().Reverse().Select(p => p.EndsWith("/") || p.EndsWith("\\") ? p : p + "/").ToList();
        }

        public static string ModName(string modPath)
        {
            string trimmed = modPath.TrimEnd('/', '\\');
            return trimmed == DataHandler.strAssetPath.TrimEnd('/', '\\') ? "core" : Path.GetFileName(trimmed);
        }

        //Every JSON file under the folder, skipping any the game's ignore patterns name, the same as its own data
        public static IEnumerable<string> Files(string folder)
        {
            if (!Directory.Exists(folder)) return Enumerable.Empty<string>();
            string[] ignore = DataHandler.dictModList != null && DataHandler.dictModList.TryGetValue("Mod Loading Order", out JsonModList list) ? list.aIgnorePatterns : null;
            return Directory.GetFiles(folder, "*.json", SearchOption.AllDirectories).OrderBy(f => f, StringComparer.Ordinal)
                .Where(f => ignore == null || !ignore.Any(pattern => !string.IsNullOrEmpty(pattern) && DataHandler.PathSanitize(f).IndexOf(pattern) >= 0));
        }

        //Read with the game's own reader, which keys entries by strName and logs a file it can't read
        public static IEnumerable<T> Read<T>(string file)
        {
            var entries = new Dictionary<string, T>();
            DataHandler.JsonToData(file, entries);
            return entries.Values;
        }
    }
}
