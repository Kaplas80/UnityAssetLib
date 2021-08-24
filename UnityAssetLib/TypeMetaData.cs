﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using UnityAssetLib.IO;

namespace UnityAssetLib
{
    public class TypeMetaData
    {
        public List<ClassInfo> ClassIDs = new List<ClassInfo>();
        public Dictionary<int, byte[]> Hashes = new Dictionary<int, byte[]>();

        public bool hasTypeTrees;
        public Dictionary<int, TypeTreeNode> TypeTrees = new Dictionary<int, TypeTreeNode>();

        public TypeMetaData(uint format, ExtendedBinaryReader buf)
        {
            if (format >= 13)
            {
                hasTypeTrees = buf.ReadBoolean();

                int types_count = buf.ReadInt32();

                for (int i = 0; i < types_count; i++)
                {
                    int classID = buf.ReadInt32();
                    int scriptID;

                    if (format >= 17)
                    {
                        byte unk0 = buf.ReadByte();
                        scriptID = -1 - buf.ReadInt16();
                    }
                    else
                    {
                        scriptID = classID;
                    }

                    ClassIDs.Add(new ClassInfo {scriptID=scriptID, classID=classID});

                    byte[] hash;
                    if ((format < 16 && classID < 0) || (format >= 16 && classID == 114))
                        hash = buf.ReadBytes(0x20);
                    else
                        hash = buf.ReadBytes(0x10);

                    Hashes[classID] = hash;

                    if (hasTypeTrees)
                    {
                        TypeTrees.Add(classID, TypeTreeNode.ReadTypeTree(format, buf));

                        if (format >= 21)
                        {
                            int dependenciesCount = buf.ReadInt32();
                            var TypeDependencies = (int[])buf.ReadValueArray<int>(dependenciesCount);
                        }
                    }
                }
            }
            else
            {
                int fieldsCount = buf.ReadInt32();
                for (int i = 0; i < fieldsCount; i++)
                {
                    int classID = buf.ReadInt32();
                    TypeTrees.Add(classID, TypeTreeNode.ReadTypeTree(format, buf));
                }
            }
        }

        [DebuggerDisplay("classID = {classID}, scriptID = {scriptID}")]
        public struct ClassInfo
        {
            public int classID;
            public int scriptID;
        }
    }
}
