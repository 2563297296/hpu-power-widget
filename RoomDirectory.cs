using System;
using System.IO;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;

namespace HpuPowerWindows
{
    internal sealed class CatalogChoice
    {
        public string id { get; set; }
        public string name { get; set; }
        public override string ToString() { return name; }
    }

    internal static class RoomDirectory
    {
        public static CatalogChoice[] Buildings(string campus)
        {
            return Read<CatalogChoice>(campus, "buildings.json");
        }

        public static CatalogChoice[] Floors(string campus, string building)
        {
            return Read<CatalogChoice>(campus, building, "floors.json");
        }

        public static RoomOption[] Rooms(string campus, string building, string floor)
        {
            return Read<RoomOption>(campus, building, floor + ".json");
        }

        public static RoomOption Find(string id)
        {
            if (String.IsNullOrEmpty(id)) return null;
            string[] parts = id.Split('|');
            if (parts.Length != 7 || (parts[0] != "north" && parts[0] != "south") ||
                !Regex.IsMatch(parts[1], @"\A[0-9]+\z") || !Regex.IsMatch(parts[2], @"\A[0-9]+\z"))
                return null;
            try
            {
                return Array.Find(Rooms(parts[0], parts[1], parts[2]), room => room.id == id);
            }
            catch (IOException) { return null; }
            catch (UnauthorizedAccessException) { return null; }
            catch (ArgumentException) { return null; }
            catch (InvalidOperationException) { return null; }
        }

        private static T[] Read<T>(params string[] parts)
        {
            string name = "room-directory/" + String.Join("/", parts);
            return new JavaScriptSerializer().Deserialize<T[]>(BundledAssets.ReadText(name)) ?? new T[0];
        }
    }
}
