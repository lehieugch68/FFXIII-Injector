using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;
using System.Runtime.InteropServices;
using System.IO.Compression;
using System.Globalization;
using zlib;
using System.Runtime.InteropServices.ComTypes;

namespace FFXIII_Injector
{
    internal static class Injector
    {
        private struct FilelistHeader
        {
            public byte[] Unk;

            public int DataSize;

            public int MagicNumber;

            public int ZlibInfoPointer;

            public int ZlibDataPointer;

            public int FileCount;

            public int ChunkCount;

            public WhiteInfoEntry[] FileInfos;

            public ChunkInfoEntry[] ChunkInfos;
        }
        private struct WhiteInfoEntry
        {
            public int PackageIndex;

            public ushort ChunkStringPointer;

            public byte ChunkIndex;

            public int ChunkComponent;

            public byte TypeFlag;

            public WhiteDataEntry WhiteData;
        }
        private struct WhiteDataEntry
        {
            public int Address;

            public int DecompressedSize;

            public int CompressedSize;

            public string FilePath;
        }
        private struct ChunkInfoEntry
        {

            public int DecompressedChunkSize;

            public int CompressedChunkSize;

            public int ChunkPointer;

            public byte[] ChunkData;
        }
        private static FilelistHeader ReadHeader(ref BinaryReader br)
        {
            br.BaseStream.Seek(0, SeekOrigin.Begin);
            FilelistHeader header = new FilelistHeader();
            header.Unk = br.ReadBytes(0x10);
            header.DataSize = br.ReadInt32();
            header.MagicNumber = br.ReadInt32();
            br.BaseStream.Position += 8;
            header.ZlibInfoPointer = br.ReadInt32();
            header.ZlibDataPointer = br.ReadInt32();
            header.FileCount = br.ReadInt32();
            header.FileInfos = new WhiteInfoEntry[header.FileCount];
            for (int i = 0; i < header.FileCount; i++)
            {
                header.FileInfos[i].PackageIndex = br.ReadInt32();
                header.FileInfos[i].ChunkStringPointer = br.ReadUInt16();
                header.FileInfos[i].ChunkIndex = br.ReadByte();
                header.FileInfos[i].TypeFlag = br.ReadByte();
                header.FileInfos[i].WhiteData = new WhiteDataEntry();
            }
            header.ChunkCount = ((0x20 + header.ZlibDataPointer) - (0x20 + header.ZlibInfoPointer)) / 0xC;
            header.ChunkInfos = new ChunkInfoEntry[header.ChunkCount];
            int index = 0;
            for (int i = 0; i < header.ChunkCount; i++)
            {
                header.ChunkInfos[i].DecompressedChunkSize = br.ReadInt32();
                header.ChunkInfos[i].CompressedChunkSize = br.ReadInt32();
                header.ChunkInfos[i].ChunkPointer = br.ReadInt32();
                long temp = br.BaseStream.Position;
                br.BaseStream.Position = 0x20 + header.ZlibDataPointer + header.ChunkInfos[i].ChunkPointer;
                header.ChunkInfos[i].ChunkData = Decompress(br.ReadBytes(header.ChunkInfos[i].CompressedChunkSize));
                string[] infoStrings = Encoding.ASCII.GetString(header.ChunkInfos[i].ChunkData).Split((char)0);
                for (int x = 0; index < header.FileCount && x < infoStrings.Length; x++)
                {
                    string[] infos = infoStrings[x].Split(':');
                    if (infos.Length <= 1) continue;
                    header.FileInfos[index].WhiteData.Address = int.Parse(infos[0], NumberStyles.HexNumber);
                    header.FileInfos[index].WhiteData.DecompressedSize = int.Parse(infos[1], NumberStyles.HexNumber);
                    header.FileInfos[index].WhiteData.CompressedSize = int.Parse(infos[2], NumberStyles.HexNumber);
                    header.FileInfos[index].WhiteData.FilePath = infos[3];
                    header.FileInfos[index].ChunkComponent = i;
                    index++;
                }
                br.BaseStream.Position = temp;
            }
            return header;
        }
        private static byte[] Decompress(byte[] input)
        {
            MemoryStream result = new MemoryStream();
            MemoryStream stream = new MemoryStream(input);
            stream.Seek(2, SeekOrigin.Begin);
            using (var deflateStream = new DeflateStream(stream, CompressionMode.Decompress, true))
            {
                deflateStream.CopyTo(result);
            }
            stream.Close();
            return result.ToArray();
        }
        public static byte[] Compress(byte[] data)
        {
            MemoryStream compressed = new MemoryStream();
            ZOutputStream outputStream = new ZOutputStream(compressed, 9);
            outputStream.Write(data, 0, data.Length);
            outputStream.Close();
            byte[] result = compressed.ToArray();
            return result;
        }
        public static void Inject(string filelist, string white, string dir)
        {
            Utils.DecryptFilelist(filelist);
            var stream = File.Open(filelist, FileMode.Open, FileAccess.Read);
            var br = new BinaryReader(stream);
            FilelistHeader header = ReadHeader(ref br);
            string[] files = Directory.GetFiles(dir, "*.*", SearchOption.AllDirectories);
            using (var fs = File.Open(white, FileMode.Open, FileAccess.Write))
            {
                using (var bw = new BinaryWriter(fs))
                {
                    foreach (var file in files)
                    {
                        string wpath = file.Replace(dir, "").Replace("\\", "/").TrimStart('/');
                        int index = Array.FindIndex(header.FileInfos, f => f.WhiteData.FilePath == wpath);
                        if (index < 0) continue;
                        byte[] data = File.ReadAllBytes(file);
                        byte[] compressed = header.FileInfos[index].WhiteData.CompressedSize == header.FileInfos[index].WhiteData.DecompressedSize ?
                            data : Compress(data);
                        bw.BaseStream.Position = data.Length > header.FileInfos[index].WhiteData.CompressedSize ?
                            bw.BaseStream.Length : (header.FileInfos[index].WhiteData.Address * 0x800);
                        header.FileInfos[index].WhiteData.Address = (int)bw.BaseStream.Position / 0x800;
                        header.FileInfos[index].WhiteData.DecompressedSize = data.Length;
                        header.FileInfos[index].WhiteData.CompressedSize = compressed.Length;
                        bw.Write(compressed);
                        if (compressed.Length % 0x800 != 0)
                        {
                            bw.Write(new byte[0x800 - (compressed.Length % 0x800)]);
                        }
                    }
                }
            }
            br.Close();
            stream.Close();
            var mstream = new MemoryStream();
            using (var bw = new BinaryWriter(mstream))
            {
                int index = 0;
                for (int i = 0; i < header.ChunkCount; i++)
                {
                    var cstream = new MemoryStream();
                    while (index < header.FileInfos.Length && header.FileInfos[index].ChunkComponent == i)
                    {
                        string sinfo = header.FileInfos[index].WhiteData.Address.ToString("x") + ":" +
                            header.FileInfos[index].WhiteData.DecompressedSize.ToString("x") + ":" +
                            header.FileInfos[index].WhiteData.CompressedSize.ToString("x") + ":" + header.FileInfos[index].WhiteData.FilePath;
                        header.FileInfos[index].ChunkStringPointer = (ushort)((i % 2 == 0 ? 0 : 0x8000) + cstream.Length);
                        byte[] binfo = Encoding.ASCII.GetBytes(sinfo);
                        byte[] arr = new byte[binfo.Length + 1];
                        binfo.CopyTo(arr, 0);
                        cstream.Write(arr, 0, arr.Length);
                        index++;
                    }
                    if (i == header.ChunkCount - 1)
                    {
                        byte[] endstring = Encoding.ASCII.GetBytes("end");
                        byte[] arr = new byte[endstring.Length + 1];
                        endstring.CopyTo(arr, 0);
                        cstream.Write(arr, 0, arr.Length);
                    }
                    byte[] uncompressed = cstream.ToArray();
                    byte[] compressed = Compress(uncompressed);
                    header.ChunkInfos[i].ChunkPointer = (int)bw.BaseStream.Position;
                    header.ChunkInfos[i].DecompressedChunkSize = uncompressed.Length;
                    header.ChunkInfos[i].CompressedChunkSize = compressed.Length;
                    bw.Write(compressed);
                }
            }
            var result = new MemoryStream();
            using (var bw = new BinaryWriter(result))
            {
                bw.Write(header.Unk);
                bw.Write(new byte[4]);
                bw.Write(header.MagicNumber);
                bw.Write(new byte[0x10]);
                bw.Write(header.FileCount);
                for (int i = 0; i < header.FileCount; i++)
                {
                    bw.Write(header.FileInfos[i].PackageIndex);
                    bw.Write(header.FileInfos[i].ChunkStringPointer);
                    bw.Write(header.FileInfos[i].ChunkIndex);
                    bw.Write(header.FileInfos[i].TypeFlag);
                }
                header.ZlibInfoPointer = (int)bw.BaseStream.Position - 0x20;
                for (int i = 0; i < header.ChunkCount; i++)
                {
                    bw.Write(header.ChunkInfos[i].DecompressedChunkSize);
                    bw.Write(header.ChunkInfos[i].CompressedChunkSize);
                    bw.Write(header.ChunkInfos[i].ChunkPointer);
                }
                header.ZlibDataPointer = (int)bw.BaseStream.Position - 0x20;
                bw.Write(mstream.ToArray());
                if (bw.BaseStream.Length % 8 != 0)
                {
                    bw.Write(new byte[8 - (bw.BaseStream.Length % 8)]);
                }
                header.DataSize = (int)bw.BaseStream.Length - 0x20;
                bw.Write(header.DataSize);
                bw.Write(new byte[0xC]);
                bw.BaseStream.Seek(0x10, SeekOrigin.Begin);
                bw.Write(Utils.ArrayReverse(BitConverter.GetBytes(header.DataSize)));
                bw.BaseStream.Seek(0x20, SeekOrigin.Begin);
                bw.Write(header.ZlibInfoPointer);
                bw.Write(header.ZlibDataPointer);
            }
            mstream.Close();
            File.WriteAllBytes(filelist, result.ToArray());
            Utils.WriteChecksum(filelist, header.DataSize + 0x20);
            Utils.EncryptFilelist(filelist);
        }
    }
}
