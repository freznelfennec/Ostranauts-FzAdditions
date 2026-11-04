using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;

namespace Freznel.FzAdditions.Util
{
    public static class FieldContour
    {
        //Used if an emitter has no StatFzFieldSegmentMax, such as one placed in an older save
        public const float DefaultMaxSegmentLength = 15f;

        private const int MinPoints = 3;
        private const int SmoothSegments = 12;
        private const int AdaptiveProbes = 24; //Samples used to measure a stretch of curve
        private const float AdaptiveTilesPerLine = 3f;
        private const float AdaptiveDegreesPerLine = 2f;
        private const int AdaptiveMinLines = 4;
        private const int AdaptiveMaxLines = 128;
        private const float MinKnot = 0.001f;
        private const string FallingCond = "IsFallingAway";
        private const double FallDuration = 5.0;
        private const float OutsideTileOffset = 1.5f; //From the center of the emitter's wall tile to the outer edge of its exterior tile
        private const float OutsideTileCenterOffset = 1f; //From the center of the emitter's wall tile to the center of its exterior tile
        private const double NormalEmitterCondition = 0.8; //GetDamageState, 1 is undamaged

        public struct Emitter
        {
            public Vector2 Position; //World tiles
            public Vector2 Direction; //Unit vector the emitter faces
            public float Offset; //Meters
            public float MaxSegmentLength; //Meters of contour it can hold
            public CondOwner CO; //Null when not read from a ship

            public Emitter(Vector2 position, Vector2 direction, float offset, float maxSegmentLength = DefaultMaxSegmentLength, CondOwner co = null)
            {
                MaxSegmentLength = maxSegmentLength;
                CO = co;
                Position = position;
                Direction = direction;
                Offset = offset;
            }

            public Vector2 OutsideEnd => Position + Direction.normalized * OutsideTileOffset;

            public Vector2 OutsideTileCenter => Position + Direction.normalized * OutsideTileCenterOffset;

            public Vector2 ContourPoint => OutsideEnd + Direction.normalized * (Offset / CrewSim.M_PER_TILE);
        }

        //The stretch of contour one emitter handles, from halfway to the emitter before it to halfway to the one after
        public struct Segment
        {
            public Emitter Emitter;
            public float Length; //Meters
            public float Strength; //0 at or over the emitter's limit, otherwise 1 to 100 percent

            public Segment(Emitter emitter, float length)
            {
                Emitter = emitter;
                Length = length;
                Strength = GetStrength(length, emitter.MaxSegmentLength);
            }
        }

        public enum EmitterStatus { Normal, Warn, Danger }

        //One top-level object for the field map
        public struct MapObject
        {
            public Vector2 Center; //World tiles
            public int Width; //Tiles
            public int Height;
            public bool Outside; //Lost to the field, wholly outside the contour or crossing it
        }

        //Normal while every emitter is above the condition limit, Warn once one is damaged, Danger once one is
        //broken. No emitters at all can't raise a field, so that is Danger too.
        public static EmitterStatus GetEmitterStatus(Ship ship)
        {
            return GetEmitterStatus(GetInstalledEmitters(ship));
        }

        public static EmitterStatus GetEmitterStatus(List<CondOwner> installed)
        {
            if (installed.Count == 0 || installed.Any(co => co.HasCond("IsDamaged"))) return EmitterStatus.Danger;
            return installed.Any(co => co.GetDamageState() <= NormalEmitterCondition) ? EmitterStatus.Warn : EmitterStatus.Normal;
        }

        //In kWh, what the working emitters need at their current settings
        public static double GetTotalFieldEnergy(Ship ship)
        {
            return GetTotalFieldEnergy(GetEmitterCOs(ship));
        }

        public static double GetTotalFieldEnergy(List<CondOwner> emitters)
        {
            return emitters.Sum(co => GetFieldEnergy(co, GetOffset(co)));
        }

        //Everything in the dock group, flagged if the field would lose it. Nothing is flagged without a contour.
        public static List<MapObject> GetMapObjects(Ship ship)
        {
            HashSet<CondOwner> lost = new HashSet<CondOwner>(Classify(ship).SelectMany(result => result.Outside.Concat(result.Intersecting)));
            List<MapObject> objects = new List<MapObject>();

            foreach (Ship member in GetDockGroup(ship))
            {
                foreach (CondOwner co in member.GetCOs(null, bSubObjects: false, bAllowDocked: false, bAllowLocked: true))
                {
                    if (co == null || co.ship != member || co.Item == null || member.GetRoomByID(co.strID) != null) continue;
                    objects.Add(new MapObject { Center = co.GetPos(), Width = co.Item.nWidthInTiles, Height = co.Item.nHeightInTiles, Outside = lost.Contains(co) });
                }
            }
            return objects;
        }

        //Working emitters installed anywhere in the ship's dock group
        public static List<Emitter> GetEmitters(Ship ship)
        {
            return GetEmitters(GetEmitterCOs(ship));
        }

        public static List<Emitter> GetEmitters(List<CondOwner> emitters)
        {
            //The item's exterior tile is at the top of its footprint, and rotating the item rotates its transform
            return emitters.Select(co => new Emitter(co.GetPos(), co.tf.up, GetOffset(co), GetMaxSegmentLength(co), co)).ToList();
        }

        public static List<CondOwner> GetEmitterCOs(Ship ship)
        {
            return GetWorkingEmitters(GetInstalledEmitters(ship));
        }

        public static List<CondOwner> GetWorkingEmitters(List<CondOwner> installed)
        {
            return installed.Where(co => !co.HasCond("IsDamaged")).ToList();
        }

        //Every emitter installed in the ship's dock group, damaged ones included
        public static List<CondOwner> GetInstalledEmitters(Ship ship)
        {
            List<CondOwner> installed = new List<CondOwner>();
            foreach (Ship member in GetDockGroup(ship))
            {
                installed.AddRange(member.GetCOs(null, bSubObjects: false, bAllowDocked: false, bAllowLocked: true)
                    .Where(co => co != null && co.HasCond(Constants.EmitterCond) && co.HasCond("IsInstalled")));
            }
            return installed;
        }

        //From 0 to 1, how far the emitter is pushed past its normal limits
        public static float GetReinforcement(CondOwner co)
        {
            return Mathf.Clamp01(GetSetting(co, Constants.FieldReinforcementProp, 0f));
        }

        private static float GetSetting(CondOwner co, string key, float defaultValue)
        {
            if (co.mapGUIPropMaps.TryGetValue(Constants.EmitterGUIPropMap, out Dictionary<string, string> props)
                && props.TryGetValue(key, out string value)
                && float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out float saved))
            {
                return saved;
            }
            return defaultValue;
        }

        //The saved value can be above the limit if the limit was lowered after it was set
        public static float GetOffset(CondOwner co)
        {
            float offset = GetSetting(co, Constants.FieldOffsetProp, Constants.MinFieldOffset);

            float limit = (float)co.GetCondAmount(Constants.FieldOffsetMaxCond);
            if (limit > Constants.MinFieldOffset) offset = Mathf.Min(offset, limit);
            return Mathf.Max(offset, Constants.MinFieldOffset);
        }

        //Moves each emitter straight out along its facing by its offset, then joins the points in angle order around their center.
        //Returns the corners in world tiles, the last connects back to the first. Empty if there are too few to enclose an area.
        public static List<Vector2> Calculate(IEnumerable<Emitter> emitters)
        {
            return Order(emitters).Select(emitter => emitter.ContourPoint).ToList();
        }

        //Emitters in the order their points join to form the contour
        private static List<Emitter> Order(IEnumerable<Emitter> emitters)
        {
            List<Emitter> list = emitters?.ToList() ?? new List<Emitter>();
            if (list.Count < MinPoints) return new List<Emitter>();

            Vector2 center = Vector2.zero;
            foreach (Emitter emitter in list) center += emitter.ContourPoint;
            center /= list.Count;

            return list
                .OrderBy(emitter => Mathf.Atan2(emitter.ContourPoint.y - center.y, emitter.ContourPoint.x - center.x))
                .ThenBy(emitter => (emitter.ContourPoint - center).sqrMagnitude)
                .ToList();
        }

        //One segment per emitter in contour order, measured along the smooth curve. Empty if there is no contour.
        public static List<Segment> CalculateSegments(IEnumerable<Emitter> emitters)
        {
            List<Emitter> ordered = Order(emitters);
            List<Segment> segments = new List<Segment>();
            if (ordered.Count == 0) return segments;

            List<Vector2> curve = Smooth(ordered.Select(emitter => emitter.ContourPoint).ToList());
            int count = ordered.Count;
            float[] spans = new float[count];
            for (int i = 0; i < curve.Count; i++)
            {
                spans[i / SmoothSegments] += (curve[(i + 1) % curve.Count] - curve[i]).magnitude * CrewSim.M_PER_TILE;
            }

            for (int i = 0; i < count; i++)
            {
                segments.Add(new Segment(ordered[i], (spans[(i + count - 1) % count] + spans[i]) / 2f));
            }
            return segments;
        }

        public static List<Segment> CalculateSegments(Ship ship)
        {
            return CalculateSegments(GetEmitters(ship));
        }

        //One emitter's segment, false if it is not part of a contour
        public static bool TryGetSegment(CondOwner emitter, out Segment segment)
        {
            segment = default;
            if (emitter == null) return false;

            foreach (Segment candidate in CalculateSegments(emitter.ship))
            {
                if (candidate.Emitter.CO == null || candidate.Emitter.CO.strID != emitter.strID) continue;
                segment = candidate;
                return true;
            }
            return false;
        }

        //Strength of one emitter's segment, 0 if it is not part of a contour
        public static float GetStrength(CondOwner emitter)
        {
            return TryGetSegment(emitter, out Segment segment) ? segment.Strength : 0f;
        }

        public static float GetMaxOffset(CondOwner co)
        {
            float limit = (float)co.GetCondAmount(Constants.FieldOffsetMaxCond);
            return limit > Constants.MinFieldOffset ? limit : Constants.DefaultMaxFieldOffset;
        }

        //energy = min * (max / min)^t, where t is the offset's position in its range
        public static float GetFieldEnergy(CondOwner co, float offset)
        {
            float min = (float)co.GetCondAmount(Constants.FieldEnergyMinCond);
            float max = (float)co.GetCondAmount(Constants.FieldEnergyMaxCond);
            if (min <= 0f || max <= 0f) return 0f;

            float energy = min * Mathf.Pow(max / min, Mathf.InverseLerp(Constants.MinFieldOffset, GetMaxOffset(co), offset));
            return energy * Mathf.Lerp(1f, Constants.ReinforcementEnergyBoost, GetReinforcement(co));
        }

        //The limit from the emitter's condition, before reinforcement
        public static float GetBaseMaxSegmentLength(CondOwner co)
        {
            float limit = (float)co.GetCondAmount(Constants.FieldSegmentMaxCond);
            return limit > 0f ? limit : DefaultMaxSegmentLength;
        }

        //The emitter's effective limit, the condition itself is not changed by reinforcement
        public static float GetMaxSegmentLength(CondOwner co)
        {
            return GetBaseMaxSegmentLength(co) * Mathf.Lerp(1f, Constants.ReinforcementLengthBoost, GetReinforcement(co));
        }

        private static float GetStrength(float length, float limit)
        {
            if (length >= limit) return 0f;
            return 1f + 99f * (1f - length / limit);
        }

        public static List<Vector2> Calculate(Ship ship)
        {
            return Calculate(GetEmitters(ship));
        }

        //The ship and everything docked to it, through other docked ships but never through a station.
        //Only ships loaded enough to have COs and tiles count. A station has no group.
        public static List<Ship> GetDockGroup(Ship ship)
        {
            return GetDockTree(ship, out _);
        }

        //Searched from the ship outwards, so a ship is always listed after the one it is reached through
        private static List<Ship> GetDockTree(Ship ship, out Dictionary<Ship, Ship> parents)
        {
            parents = new Dictionary<Ship, Ship>();
            List<Ship> group = new List<Ship>();
            if (!IsInField(ship)) return group;

            group.Add(ship);
            for (int i = 0; i < group.Count; i++)
            {
                foreach (Ship docked in group[i].GetDockedShips())
                {
                    if (!IsInField(docked) || group.Contains(docked)) continue;
                    group.Add(docked);
                    parents[docked] = group[i];
                }
            }
            return group;
        }

        private static bool IsInField(Ship ship)
        {
            return ship != null && !ship.bDestroyed && !ship.IsStation(bIgnoreDocks: true) && ship.LoadState >= Ship.Loaded.Edit;
        }

        //What RemoveOutside would do
        public struct RemovalReport
        {
            public int Destroyed; //Top-level COs with every tile outside the contour, contained COs go with their container
            public int Broken; //Those the contour passes through, with tiles on both sides of it
            public int Crew; //Characters who would fall away
            public int Undocked; //Docked ships that would leave rather than having their COs removed
        }

        public static RemovalReport GetRemovalReport(Ship ship)
        {
            List<ShipResult> results = Classify(ship);
            RemovalReport report = new RemovalReport { Undocked = FindDetached(ship, results, out HashSet<Ship> detached, out _).Count };

            foreach (ShipResult result in results)
            {
                if (detached.Contains(result.Ship)) continue;

                HashSet<string> keep = GetKeptPorts(result.Ship);
                report.Crew += result.Outside.Count(co => co.IsHumanOrRobot);
                report.Destroyed += result.Outside.Count(co => !co.IsHumanOrRobot && !IsProtected(co, keep));
                report.Broken += result.Intersecting.Count(co => !co.IsHumanOrRobot && !IsProtected(co, keep));
            }
            return report;
        }

        //Read once, the ship picks a new primary as soon as the current one is gone.
        //Ports in use are kept too, losing one would break the dock group.
        private static HashSet<string> GetKeptPorts(Ship ship)
        {
            HashSet<string> keep = new HashSet<string>(ship.GetDockedShipsAndPortIDs().Keys);
            keep.Add(ship.PrimaryDockingPortID);
            return keep;
        }

        private static bool IsProtected(CondOwner co, HashSet<string> keptPorts)
        {
            return co.HasCond("IsImmuneAnnihilation") || keptPorts.Contains(co.strID);
        }

        private class ShipResult
        {
            public Ship Ship;
            public int Inside; //COs fully inside
            public List<CondOwner> Outside = new List<CondOwner>();
            public List<CondOwner> Intersecting = new List<CondOwner>();

            public bool FullyOutside => Inside == 0 && Intersecting.Count == 0;
        }

        //Finds nothing if the emitters do not form a contour, rather than treating the whole group as outside
        private static List<ShipResult> Classify(Ship ship)
        {
            List<ShipResult> results = new List<ShipResult>();

            List<Vector2> curve = Smooth(Calculate(ship));
            if (curve.Count < MinPoints) return results;

            foreach (Ship member in GetDockGroup(ship))
            {
                ShipResult result = new ShipResult { Ship = member };
                results.Add(result);

                bool[] inside = GetInsideTiles(member, curve);
                foreach (CondOwner co in member.GetCOs(null, bSubObjects: false, bAllowDocked: false, bAllowLocked: true))
                {
                    if (co == null || co.ship != member) continue;

                    //GetCOs lists each room's own CO. A ship can't be saved once a room has lost it.
                    if (member.GetRoomByID(co.strID) != null) continue;

                    GetTileCounts(member, co, inside, out int tilesInside, out int tilesOutside);
                    if (tilesInside == 0) result.Outside.Add(co);
                    else if (tilesOutside > 0) result.Intersecting.Add(co);
                    else result.Inside++;
                }
            }
            return results;
        }

        //Returns the ships to undock. Detached also has everything still docked to them, which leaves with them.
        //The player's ship is never sent away, it would unload around them, so its COs are handled as normal instead.
        private static List<Ship> FindDetached(Ship ship, List<ShipResult> results, out HashSet<Ship> detached, out Dictionary<Ship, Ship> parents)
        {
            detached = new HashSet<Ship>();
            List<Ship> toUndock = new List<Ship>();

            List<Ship> group = GetDockTree(ship, out parents);
            foreach (Ship member in group)
            {
                if (member == ship) continue;

                if (detached.Contains(parents[member]))
                {
                    detached.Add(member);
                    continue;
                }

                ShipResult result = results.FirstOrDefault(entry => entry.Ship == member);
                if (result == null || !result.FullyOutside || CarriesPlayer(member, group, parents)) continue;

                toUndock.Add(member);
                detached.Add(member);
            }
            return toUndock;
        }

        private static bool CarriesPlayer(Ship ship, List<Ship> group, Dictionary<Ship, Ship> parents)
        {
            Ship playerShip = CrewSim.coPlayer?.ship;
            if (playerShip == null || !group.Contains(playerShip)) return false;

            while (playerShip != null)
            {
                if (playerShip == ship) return true;
                parents.TryGetValue(playerShip, out playerShip);
            }
            return false;
        }

        //Tiles off the ship's grid count as outside
        private static void GetTileCounts(Ship ship, CondOwner co, bool[] inside, out int tilesInside, out int tilesOutside)
        {
            tilesInside = 0;
            tilesOutside = 0;

            Vector2 center = co.GetPos();
            int width = co.Item != null ? co.Item.nWidthInTiles : 1;
            int height = co.Item != null ? co.Item.nHeightInTiles : 1;
            float left = center.x - (width / 2f - 0.5f);
            float top = center.y + (height / 2f - 0.5f);

            for (int row = 0; row < height; row++)
            {
                for (int col = 0; col < width; col++)
                {
                    int index = ship.GetTileIndexAtWorldCoords(left + col, top - row);
                    if (index >= 0 && inside[index]) tilesInside++; else tilesOutside++;
                }
            }
        }

        //Same steps as the docking screen
        private static void Undock(Ship ship, Ship docked)
        {
            if (ship.IsMooredWith(docked)) CrewSim.UnMoorShip(ship, docked);
            else CrewSim.UndockShip(ship, docked, bPushback: true);
        }

        //Destroys with the same steps as an explosion annihilating a CO. Characters fall away from the ship instead.
        //Docked ships entirely outside the contour are undocked and keep their COs.
        //Ship.RemoveCO flags room and power checks for the ship's next update, so they only run once for the whole batch.
        public static int RemoveOutside(Ship ship, out int lost, out int damaged, out int undocked)
        {
            int removed = 0;
            lost = 0;
            damaged = 0;
            undocked = 0;

            List<ShipResult> results = Classify(ship);
            if (results.Count == 0) return removed;

            List<Ship> toUndock = FindDetached(ship, results, out HashSet<Ship> detached, out Dictionary<Ship, Ship> parents);
            foreach (Ship docked in toUndock)
            {
                Undock(parents[docked], docked);
                undocked++;
            }

            List<CondOwner> characters = new List<CondOwner>();
            foreach (ShipResult result in results)
            {
                Ship member = result.Ship;
                if (detached.Contains(member)) continue;

                HashSet<string> keep = GetKeptPorts(member);

                foreach (CondOwner co in result.Outside)
                {
                    if (co == null || co.bDestroyed || co.ship != member) continue;
                    if (co.IsHumanOrRobot)
                    {
                        characters.Add(co);
                        continue;
                    }
                    if (IsProtected(co, keep)) continue;

                    co.RemoveFromCurrentHome(bForce: true);
                    co.Destroy();
                    removed++;
                }

                foreach (CondOwner co in result.Intersecting)
                {
                    if (co == null || co.bDestroyed || co.ship != member) continue;
                    if (co.IsHumanOrRobot || IsProtected(co, keep)) continue;
                    if (DestroyLikeWeapon(co)) damaged++;
                }
            }

            //Last, so the player being lost cannot interrupt the removal
            foreach (CondOwner character in characters)
            {
                if (character == null || character.bDestroyed || character.HasCond(FallingCond)) continue;
                if (character.HasCond("IsPlayer") && character.HasCond("IsDead")) continue;

                character.LogMessage(character.strName + DataHandler.GetString("OBJV_GRAV_LOSS"), "SignalRed", character.strID);
                character.SetCondAmount(FallingCond, 1.0);
                character.StartCoroutine(FallAway(character));
                lost++;
            }
            return removed;
        }

        //The steps Explosion.DamageRadius takes, with enough damage to break the CO. The change to its broken state runs as an urgent interaction.
        private static bool DestroyLikeWeapon(CondOwner co)
        {
            Destructable destructable = co.GetComponent<Destructable>();
            if (destructable == null) return false;

            double damage = destructable.DmgLeft("StatDamage");
            if (damage <= 0.0) return false;

            co.AddCondAmount("StatDamage", damage);
            destructable.DamageCheck();
            co.EndTurn();
            return true;
        }

        //Copy of CondOwner._FallAway. The game's version needs gravity and cancels itself over a ship tile, here the field has already
        //decided the character is gone, so that has to happen in zero-g too. The player ends the game, anyone else is removed.
        private static IEnumerator FallAway(CondOwner co)
        {
            double timeStart = co.Clock.Epoch;
            double timeElapsed = 0.0;
            while (timeElapsed < FallDuration)
            {
                if (co == null || co.ship == null || co.bDestroyed) yield break;

                double step = co.Clock.Epoch - timeStart - timeElapsed;
                timeElapsed += step;
                co.tf.Rotate(0f, 0f, (float)step);
                co.tf.localScale = Vector3.one / (1f + (float)timeElapsed);
                yield return null;
            }

            if (co.HasCond("IsPlayer"))
            {
                co.LogMessage(co.strName + DataHandler.GetString("OBJV_GRAV_LOST"), "SignalRed", co.strID);
                CanvasManager.instance.GameOver(co);
                yield break;
            }

            co.Kill = true;
            yield return null;
            co.RemoveFromCurrentHome(bForce: true);
            co.Destroy();
        }

        //Fills the contour row by row with the even-odd rule, once per call rather than once per CO
        private static bool[] GetInsideTiles(Ship ship, IList<Vector2> polygon)
        {
            bool[] inside = new bool[ship.nCols * ship.nRows];
            List<float> crossings = new List<float>();

            for (int row = 0; row < ship.nRows; row++)
            {
                float y = ship.vShipPos.y - row;

                crossings.Clear();
                for (int i = 0; i < polygon.Count; i++)
                {
                    Vector2 a = polygon[i];
                    Vector2 b = polygon[(i + 1) % polygon.Count];
                    if ((a.y > y) != (b.y > y)) crossings.Add(a.x + (y - a.y) / (b.y - a.y) * (b.x - a.x));
                }
                crossings.Sort();

                for (int i = 0; i + 1 < crossings.Count; i += 2)
                {
                    int start = Mathf.Max(Mathf.CeilToInt(crossings[i] - ship.vShipPos.x), 0);
                    int end = Mathf.Min(Mathf.FloorToInt(crossings[i + 1] - ship.vShipPos.x), ship.nCols - 1);
                    for (int col = start; col <= end; col++) inside[col + row * ship.nCols] = true;
                }
            }
            return inside;
        }

        //Closed centripetal Catmull-Rom curve through the corners, which stays close to them without loops or large overshoots
        public static List<Vector2> Smooth(IList<Vector2> corners, int segmentsPerCorner = SmoothSegments)
        {
            return BuildCurve(corners, (before, start, end, after) => segmentsPerCorner);
        }

        //The same curve with each stretch divided by its length and how sharply it turns, whichever needs more lines
        public static List<Vector2> SmoothAdaptive(IList<Vector2> corners)
        {
            return BuildCurve(corners, GetSegmentCount);
        }

        private static List<Vector2> BuildCurve(IList<Vector2> corners, Func<Vector2, Vector2, Vector2, Vector2, int> getSegments)
        {
            List<Vector2> curve = new List<Vector2>();
            if (corners == null || corners.Count < MinPoints) return curve;

            int count = corners.Count;
            for (int i = 0; i < count; i++)
            {
                Vector2 before = corners[(i + count - 1) % count];
                Vector2 start = corners[i];
                Vector2 end = corners[(i + 1) % count];
                Vector2 after = corners[(i + 2) % count];

                int segments = getSegments(before, start, end, after);
                for (int segment = 0; segment < segments; segment++)
                {
                    curve.Add(CurvePoint(before, start, end, after, (float)segment / segments));
                }
            }
            return curve;
        }

        //The same closed loop with evenly spaced points, about this many tiles apart
        public static List<Vector2> Resample(IList<Vector2> loop, float spacing)
        {
            List<Vector2> even = new List<Vector2>();
            if (loop == null || loop.Count < MinPoints) return even;

            float length = 0f;
            for (int i = 0; i < loop.Count; i++) length += (loop[(i + 1) % loop.Count] - loop[i]).magnitude;

            int count = Mathf.Max(Mathf.RoundToInt(length / spacing), MinPoints);
            float step = length / count;
            float walked = 0f;
            int line = 0;
            for (int i = 0; i < count; i++)
            {
                float target = i * step;
                float lineLength = (loop[(line + 1) % loop.Count] - loop[line]).magnitude;
                while (walked + lineLength < target && line < loop.Count - 1)
                {
                    walked += lineLength;
                    line++;
                    lineLength = (loop[(line + 1) % loop.Count] - loop[line]).magnitude;
                }

                float amount = lineLength > 0f ? Mathf.Clamp01((target - walked) / lineLength) : 0f;
                even.Add(Vector2.Lerp(loop[line], loop[(line + 1) % loop.Count], amount));
            }
            return even;
        }

        private static int GetSegmentCount(Vector2 before, Vector2 start, Vector2 end, Vector2 after)
        {
            float length = 0f;
            float turning = 0f;
            Vector2 last = start;
            Vector2 lastDirection = Vector2.zero;

            for (int i = 1; i <= AdaptiveProbes; i++)
            {
                Vector2 point = CurvePoint(before, start, end, after, (float)i / AdaptiveProbes);
                Vector2 direction = point - last;
                length += direction.magnitude;
                if (i > 1) turning += Vector2.Angle(lastDirection, direction);
                last = point;
                lastDirection = direction;
            }

            int byLength = Mathf.CeilToInt(length / AdaptiveTilesPerLine);
            int byTurning = Mathf.CeilToInt(turning / AdaptiveDegreesPerLine);
            return Mathf.Clamp(Mathf.Max(byLength, byTurning), AdaptiveMinLines, AdaptiveMaxLines);
        }

        private static Vector2 CurvePoint(Vector2 p0, Vector2 p1, Vector2 p2, Vector2 p3, float amount)
        {
            float t0 = 0f;
            float t1 = t0 + Knot(p0, p1);
            float t2 = t1 + Knot(p1, p2);
            float t3 = t2 + Knot(p2, p3);
            float t = Mathf.Lerp(t1, t2, amount);

            Vector2 a1 = Blend(p0, p1, t0, t1, t);
            Vector2 a2 = Blend(p1, p2, t1, t2, t);
            Vector2 a3 = Blend(p2, p3, t2, t3, t);
            Vector2 b1 = Blend(a1, a2, t0, t2, t);
            Vector2 b2 = Blend(a2, a3, t1, t3, t);
            return Blend(b1, b2, t1, t2, t);
        }

        //Square root of the distance, the minimum keeps emitters at the same point from dividing by zero
        private static float Knot(Vector2 from, Vector2 to)
        {
            return Mathf.Max(Mathf.Sqrt((to - from).magnitude), MinKnot);
        }

        private static Vector2 Blend(Vector2 from, Vector2 to, float start, float end, float t)
        {
            return Vector2.LerpUnclamped(from, to, (t - start) / (end - start));
        }
    }
}
