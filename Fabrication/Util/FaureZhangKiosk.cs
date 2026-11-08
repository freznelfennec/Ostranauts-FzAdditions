using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Freznel.FzAdditions.Util
{
    //Faure-Zhang's kiosk at OKLG, selling the company's public line. The station's layout isn't changed, which other
    //mods may change too: the kiosk is put in the first time OKLG's main station ("OKLG Entrance", where its supply
    //kiosks are) fully loads, in new games and old saves alike, and the station is marked so it's only done once.
    //
    //It goes in the nearest spot to the station's other kiosks that the game's own fit test passes, standing with its
    //back to a wall and clear of doors, so it doesn't end up in the middle of a walkway.
    public static class FaureZhangKiosk
    {
        public const string KioskCO = "ItmFzKioskFaureZhang01";
        public const string PlacedCond = "IsFzKioskPlaced";
        public const string StationTemplate = "OKLG Entrance";

        private const int SearchRadius = 18; //Tiles round each of the station's kiosks
        private const float DoorClearance = 3f; //Tiles from any door or hatch
        private const int MaxTries = 6000; //Fit tests, so a station with no room can't hold up its load

        private static readonly float[] Rotations = { 0f, 90f, 180f, 270f };

        public static bool IsTarget(Ship ship) => ship?.json != null && ship.json.strName == StationTemplate;

        //Puts the kiosk in if it's due. Force puts one on any station, for testing. Returns what happened, or null
        //when nothing was due.
        public static string TryPlace(Ship ship, bool force = false)
        {
            if (ship == null || ship.bDestroyed || ship.ShipCO == null) return null;
            if (!force && (!IsTarget(ship) || ship.ShipCO.HasCond(PlacedCond))) return null;

            List<CondOwner> all = ship.GetCOs(null, bSubObjects: false, bAllowDocked: false, bAllowLocked: true) ?? new List<CondOwner>();
            if (all.Any(c => c != null && c.strName == KioskCO))
            {
                ship.ShipCO.AddCondAmount(PlacedCond, 1.0);
                return "already has the kiosk";
            }

            List<Vector2> anchors = all.Where(c => c != null && c.HasCond("IsKiosk")).Select(c => (Vector2)c.tf.position).ToList();
            if (anchors.Count == 0) anchors = all.Where(c => c != null && c.HasCond("IsInstalled")).Select(c => (Vector2)c.tf.position).Take(1).ToList();
            if (anchors.Count == 0) return "no room to search";
            List<Vector2> doors = all.Where(c => c != null && c.strName != null && (c.strName.Contains("Door") || c.strName.Contains("Hatch") || c.strName.Contains("DockSys")))
                .Select(c => (Vector2)c.tf.position).ToList();

            //A 2 by 2 kiosk is centred on a tile corner
            var candidates = new HashSet<Vector2>();
            foreach (Vector2 anchor in anchors)
            {
                float ax = Mathf.Floor(anchor.x), ay = Mathf.Floor(anchor.y);
                for (int dx = -SearchRadius; dx <= SearchRadius; dx++)
                    for (int dy = -SearchRadius; dy <= SearchRadius; dy++)
                        candidates.Add(new Vector2(ax + dx, ay + dy));
            }
            List<Vector2> ordered = candidates
                .Where(c => doors.All(d => (d - c).sqrMagnitude > DoorClearance * DoorClearance))
                .OrderBy(c => anchors.Min(a => (a - c).sqrMagnitude)).ToList();

            CondOwner kiosk = DataHandler.GetCondOwner(KioskCO);
            if (kiosk?.Item == null)
            {
                if (kiosk != null) kiosk.Destroy();
                return "couldn't make the kiosk";
            }
            int tries = 0;
            try
            {
                foreach (Vector2 centre in ordered)
                {
                    foreach (float rotation in Rotations)
                    {
                        if (!WallBehind(ship, centre, rotation)) continue;
                        if (++tries > MaxTries) break;
                        Pose(kiosk, centre, rotation);
                        if (!kiosk.Item.CheckFit(kiosk.Item.rend.bounds.center, ship)) continue;

                        ship.AddCO(kiosk, bTiles: true);
                        ship.ShipCO.AddCondAmount(PlacedCond, 1.0);
                        FzAdditions.Logger.LogInfo($"Placed the Faure-Zhang kiosk on {ship.strRegID} at {centre}, facing {rotation}");
                        return $"placed at {centre}";
                    }
                    if (tries > MaxTries) break;
                }
            }
            catch (Exception ex)
            {
                FzAdditions.Logger.LogError($"Placing the Faure-Zhang kiosk on {ship.strRegID} failed: {ex}");
            }
            kiosk.Destroy();
            FzAdditions.Logger.LogWarning($"No room for the Faure-Zhang kiosk on {ship.strRegID}, it'll look again next time the station loads");
            return "no room found";
        }

        private static void Pose(CondOwner kiosk, Vector2 centre, float rotation)
        {
            kiosk.tf.position = new Vector3(centre.x, centre.y, kiosk.tf.position.z);
            kiosk.tf.rotation = Quaternion.Euler(0f, 0f, rotation);
            kiosk.Item.fLastRotation = rotation;
            kiosk.Item.ResetTransforms(centre.x, centre.y);
        }

        //Unturned, the kiosk faces south with its use point below it, so its back is the row of two tiles above it.
        //Turns go anticlockwise.
        private static bool WallBehind(Ship ship, Vector2 centre, float rotation)
        {
            foreach (Vector2 offset in new[] { new Vector2(-0.5f, 1.5f), new Vector2(0.5f, 1.5f) })
            {
                Vector2 at = centre + Turn(offset, rotation);
                Tile tile = ship.GetTileAtWorldCoords1(at.x, at.y, bAllowDocked: false);
                if (tile?.coProps == null || !tile.coProps.HasCond("IsWall")) return false;
            }
            return true;
        }

        private static Vector2 Turn(Vector2 v, float degrees)
        {
            float r = degrees * Mathf.Deg2Rad, c = Mathf.Cos(r), s = Mathf.Sin(r);
            return new Vector2(v.x * c - v.y * s, v.x * s + v.y * c);
        }
    }
}
