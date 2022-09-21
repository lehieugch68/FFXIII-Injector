using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;
using System.Runtime.InteropServices;
using System.IO.Compression;
using ICSharpCode.SharpZipLib.Zip.Compression;
using ICSharpCode.SharpZipLib.Zip.Compression.Streams;
using System.Globalization;

namespace FFXIII_Injector
{
    internal static class Injector
    {
        private struct FilelistHeader
        {
            public byte[] Unk;

            public int DataSize;

            public int MagicNumber;

            public int Padding01;

            public int Padding02;

            public int ZlibInfoPointer;

            public int ZlibDataPointer;

            public int FileCount;

            public WhiteInfoEntry[] FileInfos;

            public ChunkInfoEntry[] ChunkInfos;
        }
        private struct WhiteInfoEntry
        {
            public int PackageIndex;

            public short ChunkStringPointer;

            public byte ChunkIndex;

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
            header.Padding01 = br.ReadInt32();
            header.Padding02 = br.ReadInt32();
            header.ZlibInfoPointer = br.ReadInt32();
            header.ZlibDataPointer = br.ReadInt32();
            header.FileCount = br.ReadInt32();
            header.FileInfos = new WhiteInfoEntry[header.FileCount];
            for (int i = 0; i < header.FileCount; i++)
            {
                header.FileInfos[i].PackageIndex = br.ReadInt32();
                header.FileInfos[i].ChunkStringPointer = br.ReadInt16();
                header.FileInfos[i].ChunkIndex = br.ReadByte();
                header.FileInfos[i].TypeFlag = br.ReadByte();
                header.FileInfos[i].WhiteData = new WhiteDataEntry();
            }
            int chunkCount = (32 + header.ZlibDataPointer - (32 + header.ZlibInfoPointer)) / 12;
            header.ChunkInfos = new ChunkInfoEntry[chunkCount];
            for (int i = 0; i < chunkCount; i++)
            {
                header.ChunkInfos[i].DecompressedChunkSize = br.ReadInt32();
                header.ChunkInfos[i].CompressedChunkSize = br.ReadInt32();
                header.ChunkInfos[i].ChunkPointer = br.ReadInt32();
                long temp = br.BaseStream.Position;
                br.BaseStream.Position = 32 + header.ZlibDataPointer + header.ChunkInfos[i].ChunkPointer;
                header.ChunkInfos[i].ChunkData = Decompress(br.ReadBytes(header.ChunkInfos[i].CompressedChunkSize));
                br.BaseStream.Position = temp;
            }
            using (var filelistData = new MemoryStream())
            {
                var bw = new BinaryWriter(filelistData);
                for (int i = 0; i < header.ChunkInfos.Length; i++)
                {
                    bw.Write(header.ChunkInfos[i].ChunkData);
                }
                bw.Flush();
                filelistData.Position = 0;
                string[] infoStrings = Encoding.ASCII.GetString(filelistData.ToArray()).Split((char)0);
                for (int i = 0; i < header.FileCount; i++)
                {
                    string[] infos = infoStrings[i].Split(':');
                    header.FileInfos[i].WhiteData.Address = int.Parse(infos[0], NumberStyles.HexNumber);
                    header.FileInfos[i].WhiteData.DecompressedSize = int.Parse(infos[1], NumberStyles.HexNumber);
                    header.FileInfos[i].WhiteData.CompressedSize = int.Parse(infos[2], NumberStyles.HexNumber);
                    header.FileInfos[i].WhiteData.FilePath = infos[3];
                }
            }
            return header;
        }
        private static byte[] Decompress(byte[] data)
        {
            var outputStream = new MemoryStream();
            using (var compressedStream = new MemoryStream(data))
            using (var inputStream = new InflaterInputStream(compressedStream))
            {
                inputStream.CopyTo(outputStream);
                outputStream.Position = 0;
                return outputStream.ToArray();
            }
        }
        private static byte[] Compress(byte[] input)
        {
            byte[] size = BitConverter.GetBytes(Convert.ToUInt32(input.Length));
            Deflater compressor = new Deflater();
            compressor.SetLevel(Deflater.BEST_COMPRESSION);
            compressor.SetInput(input);
            compressor.Finish();
            MemoryStream bos = new MemoryStream(input.Length);
            byte[] buf = new byte[1024];
            while (!compressor.IsFinished)
            {
                int count = compressor.Deflate(buf);
                bos.Write(buf, 0, count);
            }
            MemoryStream result = new MemoryStream();
            BinaryWriter bw = new BinaryWriter(result);
            bw.Write(size);
            bw.Write(bos.ToArray());
            bw.Close();
            return result.ToArray();
        }
        public static void Inject(string filelist, string white, string dir)
        {
            using (var stream = File.Open(filelist, FileMode.Open, FileAccess.ReadWrite))
            {
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
                using (var mstream = new MemoryStream())
                {
                    using (var bw = new BinaryWriter(mstream))
                    {

                    }
                }
            }
        }
    }
}
