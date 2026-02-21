using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

public class PMTilesReader
{
    private string filePath;
    private PMTilesHeader header;
    private List<TileEntry> rootEntries;

    // Single shared stream + lock for concurrent reads
    private FileStream _sharedStream;
    private readonly SemaphoreSlim _streamLock = new SemaphoreSlim(1, 1);

    public struct PMTilesHeader
    {
        public ulong RootOffset, RootLength;
        public ulong MetadataOffset, MetadataLength;
        public ulong LeafDirectoryOffset, LeafDirectoryLength;
        public ulong TileDataOffset, TileDataLength;
        public byte InternalCompression, TileCompression;
        public byte MinZoom, MaxZoom;
    }

    public struct TileEntry
    {
        public ulong TileId, Offset;
        public uint Length, RunLength;
    }

    public async Task Initialize()
    {
        filePath = Path.Combine(Application.streamingAssetsPath, "Rupelstreek.pmtiles");

        // Open once, keep open for the lifetime of this reader
        _sharedStream = new FileStream(
            filePath, FileMode.Open, FileAccess.Read,
            FileShare.Read, bufferSize: 65536, useAsync: true);

        byte[] headerBytes = await ReadBytesAsync(0, 127);
        header = ParseHeader(headerBytes);

        byte[] rootDirRaw = await ReadBytesAsync((long)header.RootOffset, (int)header.RootLength);
        byte[] rootDirBytes = Decompress(rootDirRaw, header.InternalCompression);
        rootEntries = ParseDirectory(rootDirBytes);

        Debug.Log($"PMTiles loaded. Zoom: {header.MinZoom}-{header.MaxZoom}");
    }

    PMTilesHeader ParseHeader(byte[] d)
    {
        if (d[0] != 0x50 || d[1] != 0x4D) throw new Exception("Not a PMTiles file");
        return new PMTilesHeader
        {
            RootOffset          = BitConverter.ToUInt64(d, 8),
            RootLength          = BitConverter.ToUInt64(d, 16),
            MetadataOffset      = BitConverter.ToUInt64(d, 24),
            MetadataLength      = BitConverter.ToUInt64(d, 32),
            LeafDirectoryOffset = BitConverter.ToUInt64(d, 40),
            LeafDirectoryLength = BitConverter.ToUInt64(d, 48),
            TileDataOffset      = BitConverter.ToUInt64(d, 56),
            TileDataLength      = BitConverter.ToUInt64(d, 64),
            InternalCompression = d[97],
            TileCompression     = d[98],
            MinZoom             = d[100],
            MaxZoom             = d[101],
        };
    }

    List<TileEntry> ParseDirectory(byte[] data)
    {
        var entries = new List<TileEntry>();
        int pos = 0;
        int numEntries = (int)ReadVarint(data, ref pos);

        ulong[] tileIds = new ulong[numEntries];
        ulong lastId = 0;
        for (int i = 0; i < numEntries; i++) { lastId += ReadVarint(data, ref pos); tileIds[i] = lastId; }

        uint[] runLengths = new uint[numEntries];
        for (int i = 0; i < numEntries; i++) runLengths[i] = (uint)ReadVarint(data, ref pos);

        uint[] lengths = new uint[numEntries];
        for (int i = 0; i < numEntries; i++) lengths[i] = (uint)ReadVarint(data, ref pos);

        ulong[] offsets = new ulong[numEntries];
        for (int i = 0; i < numEntries; i++)
        {
            ulong raw = ReadVarint(data, ref pos);
            offsets[i] = (i > 0 && raw == 0) ? offsets[i - 1] + lengths[i - 1] : raw - 1;
        }

        for (int i = 0; i < numEntries; i++)
            entries.Add(new TileEntry { TileId = tileIds[i], Offset = offsets[i], Length = lengths[i], RunLength = runLengths[i] });

        return entries;
    }

    public async Task<byte[]> GetTile(int z, int x, int y)
    {
        ulong tileId = ZxyToId(z, x, y);
        var entry = FindEntry(rootEntries, tileId);
        if (entry == null) return null;

        if (entry.Value.RunLength == 0)
        {
            byte[] leafRaw = await ReadBytesAsync((long)(header.LeafDirectoryOffset + entry.Value.Offset), (int)entry.Value.Length);
            byte[] leafBytes = Decompress(leafRaw, header.InternalCompression);
            var leafEntries = ParseDirectory(leafBytes);
            entry = FindEntry(leafEntries, tileId);
            if (entry == null) return null;
        }

        byte[] tileRaw = await ReadBytesAsync((long)(header.TileDataOffset + entry.Value.Offset), (int)entry.Value.Length);
        return Decompress(tileRaw, header.TileCompression);
    }

    TileEntry? FindEntry(List<TileEntry> entries, ulong tileId)
    {
        int lo = 0, hi = entries.Count - 1;
        while (lo <= hi)
        {
            int mid = (lo + hi) / 2;
            if (entries[mid].TileId == tileId) return entries[mid];
            if (entries[mid].TileId < tileId) lo = mid + 1;
            else hi = mid - 1;
        }
        if (lo > 0)
        {
            var prev = entries[lo - 1];
            if (prev.RunLength > 0 && tileId < prev.TileId + prev.RunLength) return prev;
        }
        return null;
    }

    public static ulong ZxyToId(int z, int x, int y)
    {
        ulong acc = 0;
        for (int i = 0; i < z; i++) acc += (ulong)(1L << (2 * i));
        int a = z - 1;
        long tx = x, ty = y;
        while (a >= 0)
        {
            int s = 1 << a;
            long rx = (tx & s) > 0 ? 1 : 0;
            long ry = (ty & s) > 0 ? 1 : 0;
            acc += (ulong)((3L * rx ^ ry) * (1L << (2 * a)));
            if (ry == 0) { if (rx == 1) { tx = s - 1 - tx; ty = s - 1 - ty; } long t = tx; tx = ty; ty = t; }
            a--;
        }
        return acc;
    }

    public static (int z, int x, int y) IdToZxy(ulong id)
    {
        ulong acc = 0;
        for (int z = 0; z < 32; z++)
        {
            ulong numTiles = (ulong)(1L << (2 * z));
            if (acc + numTiles > id)
            {
                ulong pos = id - acc;
                long tx = 0, ty = 0;
                for (long s = 1; s < (1 << z); s *= 2)
                {
                    long rx = (long)(pos / 2) & 1;
                    long ry = (long)(pos ^ (ulong)rx) & 1;
                    if (ry == 0) { if (rx == 1) { tx = s - 1 - tx; ty = s - 1 - ty; } long t = tx; tx = ty; ty = t; }
                    tx += rx * s; ty += ry * s; pos /= 4;
                }
                return (z, (int)tx, (int)ty);
            }
            acc += numTiles;
        }
        return (0, 0, 0);
    }

    byte[] Decompress(byte[] data, byte compression)
    {
        if (compression <= 1) return data;
        if (compression == 2)
        {
            using var input  = new MemoryStream(data);
            using var gz     = new GZipStream(input, CompressionMode.Decompress);
            using var output = new MemoryStream();
            gz.CopyTo(output);
            return output.ToArray();
        }
        throw new Exception($"Unsupported compression: {compression}");
    }

    // Thread-safe read using one shared stream + lock
    async Task<byte[]> ReadBytesAsync(long offset, int length)
    {
        byte[] buffer = new byte[length];
        await _streamLock.WaitAsync();
        try
        {
            _sharedStream.Seek(offset, SeekOrigin.Begin);
            int read = 0;
            while (read < length)
            {
                int n = await _sharedStream.ReadAsync(buffer, read, length - read);
                if (n == 0) break;
                read += n;
            }
        }
        finally { _streamLock.Release(); }
        return buffer;
    }

    ulong ReadVarint(byte[] data, ref int pos)
    {
        ulong result = 0; int shift = 0;
        while (true) { byte b = data[pos++]; result |= (ulong)(b & 0x7F) << shift; if ((b & 0x80) == 0) break; shift += 7; }
        return result;
    }

    public List<TileEntry> GetRootEntries() => rootEntries;

    public List<(int x, int y)> GetAllTilesAtZoom(int zoom)
    {
        var result = new List<(int x, int y)>();
        foreach (var entry in rootEntries)
        {
            if (entry.RunLength == 0) continue;
            var (z, x, y) = IdToZxy(entry.TileId);
            if (z == zoom) result.Add((x, y));
            for (uint r = 1; r < entry.RunLength; r++)
            {
                var (rz, rx, ry) = IdToZxy(entry.TileId + r);
                if (rz == zoom) result.Add((rx, ry));
            }
        }
        return result;
    }

    public void Dispose()
    {
        _sharedStream?.Dispose();
    }
}