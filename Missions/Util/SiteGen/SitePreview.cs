using System;
using System.IO;
using System.Linq;
using System.IO.Compression;
using System.Text;

namespace Freznel.FzAdditions.Util.SiteGen
{
    //Draws a generated site to a PNG, a block of pixels per tile coloured by zone, so shapes can be checked without
    //spawning anything. Docks and mounts get a white bar on the side they face. The PNG is written by hand (zlib-wrapped
    //deflate and CRCs), so it needs nothing from Unity and runs outside the game too.
    public static class SitePreview
    {
        private const int Scale = 8; //Pixels per tile

        private static (byte r, byte g, byte b) Colour(SiteZone zone)
        {
            switch (zone)
            {
                case SiteZone.Rock: return (92, 78, 66);
                case SiteZone.Wall: return (150, 156, 166);
                case SiteZone.Corridor: return (70, 110, 170);
                case SiteZone.Room: return (60, 150, 120);
                case SiteZone.Spoke: return (90, 140, 210);
                case SiteZone.CoreRoom: return (190, 150, 60);
                case SiteZone.Vault: return (220, 70, 200);
                case SiteZone.DockPod: return (110, 190, 90);
                case SiteZone.Dock: return (120, 255, 120);
                case SiteZone.Mount: return (230, 60, 50);
                case SiteZone.Tunnel: return (120, 120, 150);
                case SiteZone.Checkpoint: return (230, 140, 60);
                case SiteZone.Door: return (250, 230, 120);
                case SiteZone.Strut: return (95, 100, 110);
                default: return (12, 14, 22);
            }
        }

        //With a furnished layout, fixtures, lights and conduits are drawn over the zones
        public static void Write(SiteGrid grid, string path, SiteLayout layout = null)
        {
            int width = grid.Width * Scale, height = grid.Height * Scale;
            var pixels = new byte[height, width * 3];
            for (int tx = 0; tx < grid.Width; tx++)
            {
                for (int ty = 0; ty < grid.Height; ty++)
                {
                    var (r, g, b) = Colour(grid[tx, ty]);
                    for (int px = 0; px < Scale; px++)
                    {
                        for (int py = 0; py < Scale; py++)
                        {
                            //A faint grid line on each tile's top and left edge
                            bool line = (px == 0 || py == 0) && grid[tx, ty] != SiteZone.Space;
                            int i = (tx * Scale + px) * 3;
                            pixels[ty * Scale + py, i] = (byte)(line ? r * 0.8 : r);
                            pixels[ty * Scale + py, i + 1] = (byte)(line ? g * 0.8 : g);
                            pixels[ty * Scale + py, i + 2] = (byte)(line ? b * 0.8 : b);
                        }
                    }
                }
            }
            //A cross on walkable tiles no dock can reach
            bool[,] reachable = grid.Reachable();
            for (int tx = 0; tx < grid.Width; tx++)
            {
                for (int ty = 0; ty < grid.Height; ty++)
                {
                    if (!SiteGrid.IsWalkable(grid[tx, ty]) || reachable[tx, ty]) continue;
                    for (int p = 1; p < Scale - 1; p++)
                    {
                        foreach (int q in new[] { p, Scale - 1 - p })
                        {
                            int i = (tx * Scale + q) * 3;
                            pixels[ty * Scale + p, i] = 255;
                            pixels[ty * Scale + p, i + 1] = 255;
                            pixels[ty * Scale + p, i + 2] = 0;
                        }
                    }
                }
            }
            if (layout != null) DrawFurnishing(pixels, layout);
            foreach (SiteDock dock in grid.Docks) MarkFacing(pixels, dock.X, dock.Y, dock.Facing);
            foreach (SiteMount mount in grid.Mounts) MarkFacing(pixels, mount.X, mount.Y, mount.Facing);

            Directory.CreateDirectory(Path.GetDirectoryName(path));
            using (var file = File.Create(path)) WritePng(file, pixels, width, height);
        }

        private static void DrawFurnishing(byte[,] pixels, SiteLayout layout)
        {
            void Fill(int tx, int ty, int inset, (byte r, byte g, byte b) colour)
            {
                if (!layout.Grid.InBounds(tx, ty)) return;
                for (int px = inset; px < Scale - inset; px++)
                {
                    for (int py = inset; py < Scale - inset; py++)
                    {
                        int i = (tx * Scale + px) * 3;
                        pixels[ty * Scale + py, i] = colour.r;
                        pixels[ty * Scale + py, i + 1] = colour.g;
                        pixels[ty * Scale + py, i + 2] = colour.b;
                    }
                }
            }
            foreach (Placement p in layout.Placed.Where(p => p.Shape.Name.StartsWith("ItmConduit")))
            {
                var (x, y) = p.Tile(0, 0);
                Fill(x, y, 3, (40, 220, 230));
            }
            foreach (Placement p in layout.Placed.Where(p => !p.Shape.Name.StartsWith("ItmConduit") && !p.Shape.Name.StartsWith("ItmLit")))
            {
                foreach (var (x, y) in p.Footprint()) Fill(x, y, 1, (120, 70, 30));
            }
            foreach (Placement p in layout.Placed.Where(p => p.Shape.Name.StartsWith("ItmLit")))
            {
                var (x, y) = p.Tile(0, 0);
                Fill(x, y, 2, (255, 240, 80));
            }
        }

        private static void MarkFacing(byte[,] pixels, int tx, int ty, Facing facing)
        {
            for (int a = 0; a < Scale; a++)
            {
                for (int b = 0; b < 2; b++)
                {
                    int px, py;
                    switch (facing)
                    {
                        case Facing.North: px = a; py = b; break;
                        case Facing.South: px = a; py = Scale - 1 - b; break;
                        case Facing.East: px = Scale - 1 - b; py = a; break;
                        default: px = b; py = a; break;
                    }
                    int i = (tx * Scale + px) * 3;
                    pixels[ty * Scale + py, i] = 255;
                    pixels[ty * Scale + py, i + 1] = 255;
                    pixels[ty * Scale + py, i + 2] = 255;
                }
            }
        }

        private static void WritePng(Stream stream, byte[,] pixels, int width, int height)
        {
            stream.Write(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }, 0, 8);

            var header = new MemoryStream();
            WriteInt(header, width);
            WriteInt(header, height);
            header.WriteByte(8); //Bit depth
            header.WriteByte(2); //Colour type: RGB
            header.WriteByte(0);
            header.WriteByte(0);
            header.WriteByte(0);
            WriteChunk(stream, "IHDR", header.ToArray());

            //Rows with filter type 0, deflated and wrapped as zlib
            var raw = new MemoryStream();
            int rowBytes = width * 3;
            var row = new byte[rowBytes];
            for (int y = 0; y < height; y++)
            {
                raw.WriteByte(0);
                for (int i = 0; i < rowBytes; i++) row[i] = pixels[y, i];
                raw.Write(row, 0, rowBytes);
            }
            byte[] data = raw.ToArray();
            var zlib = new MemoryStream();
            zlib.WriteByte(0x78);
            zlib.WriteByte(0x9C);
            using (var deflate = new DeflateStream(zlib, CompressionLevel.Optimal, leaveOpen: true)) deflate.Write(data, 0, data.Length);
            WriteInt(zlib, (int)Adler32(data));
            WriteChunk(stream, "IDAT", zlib.ToArray());

            WriteChunk(stream, "IEND", new byte[0]);
        }

        private static void WriteChunk(Stream stream, string type, byte[] data)
        {
            WriteInt(stream, data.Length);
            byte[] typeBytes = Encoding.ASCII.GetBytes(type);
            stream.Write(typeBytes, 0, 4);
            stream.Write(data, 0, data.Length);
            uint crc = Crc32(typeBytes, 0xFFFFFFFFu);
            crc = Crc32(data, crc);
            WriteInt(stream, (int)(crc ^ 0xFFFFFFFFu));
        }

        private static void WriteInt(Stream stream, int value)
        {
            stream.WriteByte((byte)(value >> 24));
            stream.WriteByte((byte)(value >> 16));
            stream.WriteByte((byte)(value >> 8));
            stream.WriteByte((byte)value);
        }

        private static uint[] crcTable;

        private static uint Crc32(byte[] data, uint crc)
        {
            if (crcTable == null)
            {
                crcTable = new uint[256];
                for (uint n = 0; n < 256; n++)
                {
                    uint c = n;
                    for (int k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
                    crcTable[n] = c;
                }
            }
            foreach (byte b in data) crc = crcTable[(crc ^ b) & 0xFF] ^ (crc >> 8);
            return crc;
        }

        private static uint Adler32(byte[] data)
        {
            uint a = 1, b = 0;
            foreach (byte d in data)
            {
                a = (a + d) % 65521;
                b = (b + a) % 65521;
            }
            return (b << 16) | a;
        }
    }
}
