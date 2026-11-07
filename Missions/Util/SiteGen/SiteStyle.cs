namespace Freznel.FzAdditions.Util.SiteGen
{
    //The base game items a site is built from. These two are each kind's defaults; a corporation's style from
    //data/fz/sites/styles (SiteData) changes any of them.
    public class SiteStyle
    {
        public string HullWall;
        public string TunnelWall;      //Walls of passages cut through rock
        public string[] RockWalls;     //Solid rock, picked by tile so it doesn't repeat
        public string RoomFloor;
        public string CorridorFloor;
        public string CoreFloor;
        public string VaultFloor;
        public string DockFloor;
        public string TunnelFloor;
        public string Dock = "ItmDockSys02Closed";
        //Every door is locked, with its own PIN (SiteShell); players hack the lock or get through some other way
        public string Door = "ItmDoor01ClosedLocked";
        public string DoorFloor = "ItmFloorGrate01";
        public string PoweredDoor = "ItmDoor01ClosedOnLocked";
        public string Light = "ItmLitWall1x0White";

        //Clean corporate white, as on the base game's MSS-built stations
        public static readonly SiteStyle Relay = new SiteStyle
        {
            HullWall = "ItmWallMSSLFWhite",
            TunnelWall = "ItmWallMSSLFWhite",
            RockWalls = new[] { "ItmWallRock011x1", "ItmWallRock051x1" },
            RoomFloor = "ItmFloorMSSLFWhite01",
            CorridorFloor = "ItmFloorTSDO02",
            CoreFloor = "ItmFloorTSDO02",
            VaultFloor = "ItmFloorGrate01",
            DockFloor = "ItmFloorGrate01",
            TunnelFloor = "ItmFloorGrate01"
        };

        //Dark industrial plate inside, bare rock outside
        public static readonly SiteStyle BlackSite = new SiteStyle
        {
            HullWall = "ItmWallTSDO01",
            TunnelWall = "ItmWallRock011x1",
            RockWalls = new[] { "ItmWallRock051x1", "ItmWallRock011x1" },
            RoomFloor = "ItmFloorTSDO02",
            CorridorFloor = "ItmFloorGrate01",
            CoreFloor = "ItmFloorTSDO02",
            VaultFloor = "ItmFloorGrate01",
            DockFloor = "ItmFloorGrate01",
            TunnelFloor = "ItmFloorRock021x1",
            Light = "ItmLitWall1x0Orange"
        };

        public static SiteStyle For(SiteKind kind) => kind == SiteKind.Relay ? Relay : BlackSite;

        //A style from fz/sites data, over the kind's own: anything it leaves out stays as the kind has it
        public static SiteStyle From(JsonSiteStyle data, SiteKind kind)
        {
            SiteStyle style = (SiteStyle)For(kind).MemberwiseClone();
            if (data == null) return style;
            style.HullWall = data.strHullWall ?? style.HullWall;
            style.TunnelWall = data.strTunnelWall ?? style.TunnelWall;
            if (data.aRockWalls != null && data.aRockWalls.Length > 0) style.RockWalls = data.aRockWalls;
            style.RoomFloor = data.strRoomFloor ?? style.RoomFloor;
            style.CorridorFloor = data.strCorridorFloor ?? style.CorridorFloor;
            style.CoreFloor = data.strCoreFloor ?? style.CoreFloor;
            style.VaultFloor = data.strVaultFloor ?? style.VaultFloor;
            style.DockFloor = data.strDockFloor ?? style.DockFloor;
            style.TunnelFloor = data.strTunnelFloor ?? style.TunnelFloor;
            style.Light = data.strLight ?? style.Light;
            return style;
        }
    }
}
