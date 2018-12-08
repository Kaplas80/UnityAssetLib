﻿using System;
using System.Collections;
using System.IO;
using System.Text;
using UnityAssetLib.Util;
using UnityAssetLib.Types;

namespace UnityAssetLib.Serialization
{
    public class UnitySerializer
    {
        public static object Deserialize(Type classType, byte[] data)
        {
            using (var ms = new MemoryStream(data, false))
            using (var reader = new BinaryReader(ms))
            {
                return Deserialize(classType, reader);
            }
        }

        public static T Deserialize<T>(AssetInfo assetInfo) where T : Types.Object
        {
            var ret = (T)Deserialize(typeof(T), assetInfo);
            ret.asset = assetInfo.asset;
            return ret;
        }

        public static object Deserialize(Type classType, AssetInfo assetInfo)
        {
            var reader = assetInfo.InitReader();

            var startPos = reader.Position;

            var ret = Deserialize(classType, reader);

            long readSize = (reader.Position - startPos);

            if (readSize != assetInfo.size)
            {
                throw new UnitySerializationException("Failed to fully deserialize " + classType.FullName);
            }

            return ret;
        }

        public static object Deserialize(Type classType, BinaryReader reader, object obj=null)
        {
            if (!Attribute.IsDefined(classType, typeof(UnitySerializableAttribute)))
            {
                throw new UnitySerializationException("Not deserializable type : " + classType.FullName);
            }

            if (obj == null)
            {
                obj = Activator.CreateInstance(classType);
            }

            // Deserialize base type first because Type.GetFields() returns base fields last
            if (Attribute.IsDefined(classType.BaseType, typeof(UnitySerializableAttribute)))
            {
                Deserialize(classType.BaseType, reader, obj);
            }

            foreach (var field in classType.GetFields())
            {
                if (!field.DeclaringType.Equals(classType))
                {
                    continue;
                }

                Type fieldType = field.FieldType;
                
                object value = null;

                if (fieldType.IsValueType)
                {
                    value = ReadValueType(fieldType, reader, Attribute.IsDefined(field, typeof(UnityDoNotAlignAttribute)));
                }
                else if (fieldType.IsArray && fieldType.GetElementType().IsValueType)
                {
                    value = ReadValueArray(fieldType.GetElementType(), reader);
                }
                else if (fieldType == typeof(string))
                {
                    value = reader.ReadAlignedString();
                }
                else if(fieldType.IsClass || Attribute.IsDefined(fieldType, typeof(UnitySerializableAttribute)))
                {
                    if (fieldType.IsArray)
                    {
                        var elementType = fieldType.GetElementType();
                        int size = reader.ReadInt32();

                        if (size > 0x10000)
                        {
                            throw new IOException("Size exceeds limit : " + size);
                        }
                        if (elementType == typeof(string))
                        {
                            var valueArray = new string[size];

                            for (int i = 0; i < size; i++)
                            {
                                valueArray[i] = reader.ReadAlignedString();
                            }

                            value = valueArray;
                        }
                        else
                        {
                            var valueArray = Array.CreateInstance(elementType, size);

                            for (int i = 0; i < size; i++)
                            {
                                valueArray.SetValue(Deserialize(elementType, reader), i);
                            }

                            value = valueArray;
                        }
                    }
                    else
                    {
                        value = Deserialize(fieldType, reader);
                    }
                }
                else
                {
                    throw new IOException("Failed to deserialize, unknown type : " + fieldType.ToString());
                }
                
                field.SetValue(obj, value);
            }

            return obj;
        }

        private static object ReadValueArray(Type valueType, BinaryReader reader)
        {
            int size = reader.ReadInt32();

            if (valueType == typeof(byte) || valueType == typeof(Byte))
            {
                var byteArray = reader.ReadBytes(size);
                reader.AlignStream();
                return byteArray;
            }

            var ret = new object[size];

            for (int i = 0; i < size; i++)
            {
                ret[i] = ReadValueType(valueType, reader, true);
            }

            reader.AlignStream();

            return ret;
        }

        private static object ReadValueType(Type valueType, BinaryReader reader, bool noAlign=false)
        {
            object ret = null;

            if (valueType == typeof(string))
            {
                ret = reader.ReadAlignedString();
            }
            else if (valueType == typeof(int) || valueType == typeof(Int32))
            {
                ret = reader.ReadInt32();
            }
            else if (valueType == typeof(uint) || valueType == typeof(UInt32))
            {
                ret = reader.ReadUInt32();
            }
            else if (valueType == typeof(long) || valueType == typeof(Int64))
            {
                ret = reader.ReadInt64();
            }
            else if (valueType == typeof(ulong) || valueType == typeof(UInt64))
            {
                ret = reader.ReadUInt64();
            }
            else if (valueType == typeof(short) || valueType == typeof(Int16))
            {
                ret = reader.ReadInt16();
                if(!noAlign)
                    reader.AlignStream();
            }
            else if (valueType == typeof(ushort) || valueType == typeof(UInt16))
            {
                ret = reader.ReadUInt16();
                if (!noAlign)
                    reader.AlignStream();
            }
            else if (valueType == typeof(byte) || valueType == typeof(Byte))
            {
                ret = reader.ReadByte();
                if (!noAlign)
                    reader.AlignStream();
            }
            else if (valueType == typeof(sbyte) || valueType == typeof(SByte))
            {
                ret = reader.ReadSByte();
                if (!noAlign)
                    reader.AlignStream();
            }
            else if (valueType == typeof(bool) || valueType == typeof(Boolean))
            {
                ret = reader.ReadBoolean();
                if (!noAlign)
                    reader.AlignStream();
            }
            else if (valueType == typeof(double) || valueType == typeof(Double))
            {
                ret = reader.ReadDouble();
            }
            else if (valueType == typeof(float) || valueType == typeof(Single))
            {
                ret = reader.ReadSingle();
            }

            return ret;
        }

        public static byte[] Serialize(object obj)
        {
            using (var ms = new MemoryStream())
            using (var writer = new BinaryWriter(ms))
            {
                Serialize(obj, writer);

                return ms.ToArray();
            }
        }

        public static void Serialize(object obj, BinaryWriter writer, Type objType = null)
        {
            if (obj == null)
            {
                throw new ArgumentNullException("valueObj cannot be null");
            }
            
            if (objType == null)
            {
                objType = obj.GetType();
            }

            if (objType.IsArray)
            {
                Type elemType = objType.GetElementType();

                if (elemType == typeof(byte) || elemType == typeof(Byte))
                {
                    writer.Write(obj as byte[]);
                }
                else
                {
                    var arrayObj = obj as Array;
                    int length = arrayObj.Length;

                    writer.Write(length);

                    if (elemType.IsValueType)
                    {
                        foreach (object element in arrayObj)
                        {
                            WriteValueType(element, writer, noAlign:true);
                        }

                        writer.AlignStream();
                    }
                    else
                    {
                        foreach (object element in arrayObj)
                        {
                            Serialize(element, writer);
                        }
                    }
                }
            }
            else if (objType.IsValueType)
            {
                WriteValueType(obj, writer);
            }
            else if (objType == typeof(string))
            {
                writer.WriteAlignedString(obj as string);
            }
            else if (objType.IsClass)
            {
                if (!Attribute.IsDefined(objType, typeof(UnitySerializableAttribute)))
                {
                    throw new Exception("not serializable type : " + objType.ToString());
                }

                if (Attribute.IsDefined(objType.BaseType, typeof(UnitySerializableAttribute)))
                {
                    Serialize(obj, writer, objType.BaseType);
                }

                foreach (var field in objType.GetFields())
                {
                    if (field.DeclaringType.Equals(objType.BaseType))
                    {
                        continue;
                    }
                    var fieldValue = field.GetValue(obj);
                    var fieldType = field.FieldType;

                    if (fieldType.IsValueType)
                    {
                        WriteValueType(fieldValue, writer, Attribute.IsDefined(field, typeof(UnityDoNotAlignAttribute)));
                    }
                    else
                    {
                        Serialize(fieldValue, writer);
                    }

                }
            }
        }

        private static void WriteValueType(object valueObj, BinaryWriter writer, bool noAlign = false)
        {
            if (valueObj == null)
            {
                throw new ArgumentNullException("valueObj cannot be null");
            }
            var valueType = valueObj.GetType();

            if (valueType == typeof(int) || valueType == typeof(Int32))
            {
                writer.Write((int)valueObj);
            }
            else if (valueType == typeof(uint) || valueType == typeof(UInt32))
            {
                writer.Write((uint)valueObj);
            }
            else if (valueType == typeof(long) || valueType == typeof(Int64))
            {
                writer.Write((long)valueObj);
            }
            else if (valueType == typeof(ulong) || valueType == typeof(UInt64))
            {
                writer.Write((ulong)valueObj);
            }
            else if (valueType == typeof(short) || valueType == typeof(Int16))
            {
                writer.Write((short)valueObj);
                if (!noAlign)
                    writer.AlignStream();
            }
            else if (valueType == typeof(ushort) || valueType == typeof(UInt16))
            {
                writer.Write((ushort)valueObj);
                if (!noAlign)
                    writer.AlignStream();
            }
            else if (valueType == typeof(byte) || valueType == typeof(Byte))
            {
                writer.Write((byte)valueObj);
                if (!noAlign)
                    writer.AlignStream();
            }
            else if (valueType == typeof(sbyte) || valueType == typeof(SByte))
            {
                writer.Write((sbyte)valueObj);
                if (!noAlign)
                    writer.AlignStream();
            }
            else if (valueType == typeof(bool) || valueType == typeof(Boolean))
            {
                writer.Write((bool)valueObj);
                if (!noAlign)
                    writer.AlignStream();
            }
            else if (valueType == typeof(double) || valueType == typeof(Double))
            {
                writer.Write((double)valueObj);
            }
            else if (valueType == typeof(float) || valueType == typeof(Single))
            {
                writer.Write((float)valueObj);
            }
        }
    }

    class UnitySerializationException : Exception
    {
        public UnitySerializationException(string message) : base(message) { }
    }
}
