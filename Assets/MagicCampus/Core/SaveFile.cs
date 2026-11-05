using System.IO;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

namespace MagicCampus.Core
{
    /// <summary>R5: our code writes the file. Write a temp file, swap it in keeping one .bak; a load that fails falls back to the .bak.</summary>
    public static class SaveFile
    {
        static readonly JsonSerializerSettings Settings = new JsonSerializerSettings
        {
            ContractResolver = new DefaultContractResolver { NamingStrategy = new CamelCaseNamingStrategy() },
            Formatting = Formatting.Indented
        };

        public static string ToJson(GameState state) => JsonConvert.SerializeObject(state, Settings);

        public static GameState FromJson(string json) => JsonConvert.DeserializeObject<GameState>(json, Settings);

        public static void Write(string path, GameState state)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));
            var temp = path + ".tmp";
            File.WriteAllText(temp, ToJson(state), new UTF8Encoding(false));
            if (File.Exists(path)) File.Replace(temp, path, path + ".bak");
            else File.Move(temp, path);
        }

        /// <summary>The saved state, or null when neither the file nor its .bak can be read.</summary>
        public static GameState Read(string path)
        {
            foreach (var candidate in new[] { path, path + ".bak" })
            {
                if (!File.Exists(candidate)) continue;
                try
                {
                    var state = FromJson(File.ReadAllText(candidate, Encoding.UTF8));
                    if (state != null) return state;
                }
                catch (JsonException) { }
            }
            return null;
        }
    }
}
