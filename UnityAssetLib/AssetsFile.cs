﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;
using UnityAssetLib.Util;
using System.Collections.ObjectModel;

namespace UnityAssetLib
{
    public class AssetsFile: IDisposable
    {
        public readonly EndianBinaryReader buf;
        private readonly string path;
        private readonly string basepath;

        public readonly uint metadata_size;
        public readonly uint file_size;
        public readonly uint format;
        public readonly uint data_offset;

        public readonly string version;
        public readonly int platform;

        public readonly bool longObjectIDs = false;

        private readonly long objectsIndexOffset;

        public TypeMetaData typeMetaData;
        public IReadOnlyDictionary<long, AssetInfo> assets;
        public List<ScriptType> scriptTypes;
        public List<AssetReference> references;

        private Dictionary<long, byte[]> replaceDict = new Dictionary<long, byte[]>();
        
        public static AssetsFile Open(string path)
        {
            return new AssetsFile(path);
        }

        public void Dispose()
        {
            buf.Dispose();
        }

        private AssetsFile(string path)
        {
            this.path = path;

            buf = new EndianBinaryReader(File.OpenRead(path));
            basepath = Path.GetDirectoryName(path);

            buf.endian = EndianType.BigEndian;

            metadata_size = buf.ReadUInt32();
            file_size = buf.ReadUInt32();
            format = buf.ReadUInt32();
            data_offset = buf.ReadUInt32();

            if (format >= 9 && buf.ReadUInt32() == 0)
                buf.endian = EndianType.LittleEndian;

            version = buf.ReadStringToNull();
            platform = buf.ReadInt32();

            buf.endian = EndianType.LittleEndian;

            typeMetaData = new TypeMetaData(format, buf);

            if (format >= 7 && format < 14)
                longObjectIDs = buf.ReadInt32() != 0;

            // Read obejct info table
            int objectCount = buf.ReadInt32();

            // Save position for saving file
            objectsIndexOffset = buf.Position;

            var assetsDict = new Dictionary<long, AssetInfo>(objectCount);

            for (int i = 0; i < objectCount; i++)
            {
                var assetInfo = new AssetInfo();

                assetInfo.asset = this;

                assetInfo.pathID     = ReadPathID();
                assetInfo.dataOffset = buf.ReadUInt32();
                assetInfo.size       = buf.ReadUInt32();
                assetInfo.typeID     = buf.ReadInt32();

                if (format < 16)
                    assetInfo.classID = buf.ReadUInt16();
                else
                    assetInfo.classID = typeMetaData.ClassIDs[assetInfo.typeID].classID;

                if (format < 16)
                    assetInfo.isDestroyed = buf.ReadUInt16() != 0;

                if (format == 15 || format == 16)
                    assetInfo.stripped = buf.ReadByte();

                assetsDict.Add(assetInfo.pathID, assetInfo);
            }

            assets = assetsDict;


            if (format >= 11)
            {
                int scriptCount = buf.ReadInt32();
                scriptTypes = new List<ScriptType>(scriptCount);
                for (int i = 0; i < scriptCount; i++)
                {
                    var add = new ScriptType
                    {
                        index = buf.ReadInt32(),
                        ID = ReadPathID()
                    };
                    scriptTypes.Add(add);
                }
            }

            if (format >= 6)
            {
                int referenceCount = buf.ReadInt32();
                references = new List<AssetReference>(referenceCount);
                for (int i = 0; i < referenceCount; i++)
                {
                    references.Add(AssetReference.Read(buf));
                }
            }
        }

        public AssetInfo GetAssetByName(string name)
        {
            foreach (var objInfo in assets.Values)
            {
                var objName = objInfo.TryGetName();

                if (objName != null && objName.Equals(name))
                {
                    return objInfo;
                }
            }

            return null;
        }

        public void ReplaceAsset(long pathID, byte[] data)
        {
            this.replaceDict[pathID] = data;
        }

        public void Save(string path)
        {
            var fourByteArray = new byte[4];

            byte[] buffer = new byte[0x8000];
            int size;
            int read = 0;

            using(BinaryWriter w = new BinaryWriter(File.OpenWrite(path)))
            {
                buf.Position = 0;
                w.Write(buf.ReadBytes((int)data_offset));

                int objCount = assets.Count;

                uint[] offsetArray = new uint[objCount];
                uint[] sizeArray = new uint[objCount];

                for (int i = 0; i < objCount; i++)
                {
                    var objInfo = assets.ElementAt(i).Value;

                    offsetArray[i] = (uint)(w.BaseStream.Position - data_offset);

                    if (replaceDict.ContainsKey(objInfo.pathID))
                    {
                        w.Write(replaceDict[objInfo.pathID]);
                        sizeArray[i] = (uint)replaceDict[objInfo.pathID].Length;
                    }
                    else
                    {
                        buf.Position = objInfo.dataOffset + data_offset;
                        size = (int)objInfo.size;
                        while (size > 0 && (read = buf.Read(buffer, 0, Math.Min(buffer.Length, size))) > 0)
                        {
                            w.Write(buffer, 0, read);
                            size -= read;
                        }
                        sizeArray[i] = objInfo.size;
                    }

                    w.Write(fourByteArray);
                }

                uint totalSize = (uint)w.BaseStream.Position;

                w.BaseStream.Position = objectsIndexOffset;

                for (int i = 0; i < objCount; i++)
                {
                    if (format >= 14)
                    {
                        w.AlignStream();
                        w.Seek(8, SeekOrigin.Current);
                    }
                    else if (longObjectIDs)
                    {
                        w.Seek(8, SeekOrigin.Current);
                    }
                    else
                    {
                        w.Seek(4, SeekOrigin.Current);
                    }

                    w.Write(offsetArray[i]);
                    w.Write(sizeArray[i]);

                    w.Seek(4, SeekOrigin.Current);

                    if (format < 16)
                        w.Seek(4, SeekOrigin.Current);

                    if (format == 15 || format == 16)
                        w.Seek(1, SeekOrigin.Current);
                }

                w.Seek(4, SeekOrigin.Begin);

                var sizeBytes = BitConverter.GetBytes(totalSize);

                Array.Reverse(sizeBytes);

                w.Write(sizeBytes);
            }
        }

        private long ReadPathID()
        {
            if (format >= 14)
            {
                buf.AlignStream();
                return buf.ReadInt64();
            }
            else if (longObjectIDs)
            {
                return buf.ReadInt64();
            }
            else
            {
                return buf.ReadInt32();
            }
        }

        public class ScriptType
        {
            public int index;
            public long ID;
        }
    }
}
