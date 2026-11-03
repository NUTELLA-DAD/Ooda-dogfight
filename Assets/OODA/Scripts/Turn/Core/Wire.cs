using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.Serialization;
using Ooda.Flight.Core;

namespace Ooda.Turn.Core
{
    /// <summary>
    /// Turns the battle, or a turn's plans, into bytes and back, for playing over a network: a snapshot of the whole
    /// battle (to bring a client back in step with the host) and a hash of it (to check, each turn, that both machines
    /// flew the same turn). It walks the objects' fields itself, so a field added to Battle, FlightModel, a Bullet or
    /// the Strike is carried without anyone remembering to add it here.
    ///
    /// What each machine already has is not sent: the aircraft and gun numbers, the air hazards' shapes, the ground
    /// and obstacle functions. Restoring keeps the receiver's own copies of those, so the receiver must be flying
    /// the same mission with the same aircraft (the same build of the game, with the same scenario set up).
    ///
    /// Pure C#, no Unity. Both machines must run the same build: the bytes follow the fields in declaration order.
    /// </summary>
    public static class Wire
    {
        /// <summary>Types each machine has its own copy of (set up by the scenario and never changed by a turn): never sent, never overwritten.</summary>
        static readonly HashSet<Type> Local = new HashSet<Type> { typeof(AircraftNumbers), typeof(CombatNumbers), typeof(WireSet), typeof(HeightGrid) };

        static bool Skipped(Type t)
        {
            if (typeof(Delegate).IsAssignableFrom(t) || Local.Contains(t)) return true;
            if (t.IsArray) return Skipped(t.GetElementType());
            if (t.IsGenericType && t.GetGenericTypeDefinition() == typeof(List<>)) return Skipped(t.GetGenericArguments()[0]);
            return false;
        }

        static readonly Dictionary<Type, FieldInfo[]> fieldCache = new Dictionary<Type, FieldInfo[]>();

        /// <summary>Every instance field, the base type's first, each type's in declaration order.</summary>
        static FieldInfo[] Fields(Type t)
        {
            lock (fieldCache)
            {
                if (fieldCache.TryGetValue(t, out var got)) return got;
                var list = new List<FieldInfo>();
                var chain = new List<Type>();
                for (var c = t; c != null && c != typeof(object) && c != typeof(ValueType); c = c.BaseType) chain.Insert(0, c);
                foreach (var c in chain)
                {
                    var own = c.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
                    Array.Sort(own, (a, b) => a.MetadataToken.CompareTo(b.MetadataToken));
#pragma warning disable SYSLIB0050
                    foreach (var f in own) if (!f.IsNotSerialized) list.Add(f);
#pragma warning restore SYSLIB0050
                }
                var arr = list.ToArray();
                fieldCache[t] = arr;
                return arr;
            }
        }

        // ---------- writing ----------

        /// <summary>A value (a battle, a plan, an array of plans) as bytes.</summary>
        public static byte[] Encode<T>(T value)
        {
            using (var ms = new MemoryStream())
            using (var w = new BinaryWriter(ms))
            {
                Write(w, value, typeof(T));
                w.Flush();
                return ms.ToArray();
            }
        }

        static void Write(BinaryWriter w, object v, Type t)
        {
            if (Skipped(t)) return;
            if (t == typeof(double)) { w.Write((double)v); return; }
            if (t == typeof(int)) { w.Write((int)v); return; }
            if (t == typeof(uint)) { w.Write((uint)v); return; }
            if (t == typeof(bool)) { w.Write((bool)v); return; }
            if (t == typeof(long)) { w.Write((long)v); return; }
            if (t == typeof(float)) { w.Write((float)v); return; }
            if (t == typeof(byte)) { w.Write((byte)v); return; }
            if (t.IsEnum) { w.Write(Convert.ToInt64(v)); return; }
            if (t == typeof(string)) { w.Write(v != null); if (v != null) w.Write((string)v); return; }
            var under = Nullable.GetUnderlyingType(t);
            if (under != null) { w.Write(v != null); if (v != null) Write(w, v, under); return; }
            if (t.IsValueType)
            {
                if (t.IsPrimitive) throw new NotSupportedException($"Wire: no encoding for {t}");
                foreach (var f in Fields(t)) Write(w, f.GetValue(v), f.FieldType);
                return;
            }
            // a reference: null or not, then its contents
            w.Write(v != null);
            if (v == null) return;
            if (t.IsArray)
            {
                var a = (Array)v; var et = t.GetElementType();
                if (a.Rank != 1) throw new NotSupportedException($"Wire: no encoding for {t}");
                w.Write(a.Length);
                for (int i = 0; i < a.Length; i++) Write(w, a.GetValue(i), et);
                return;
            }
            if (t.IsGenericType && t.GetGenericTypeDefinition() == typeof(List<>))
            {
                var l = (IList)v; var et = t.GetGenericArguments()[0];
                w.Write(l.Count);
                foreach (var e in l) Write(w, e, et);
                return;
            }
            if (typeof(IEnumerable).IsAssignableFrom(t)) throw new NotSupportedException($"Wire: no encoding for {t}");
            var rt = v.GetType();
            if (!t.IsSealed) w.Write(rt == t ? "" : rt.FullName);   // a Missile in a list of Bullets
            foreach (var f in Fields(rt)) Write(w, f.GetValue(v), f.FieldType);
        }

        // ---------- reading ----------

        /// <summary>Bytes back into a new value.</summary>
        public static T Decode<T>(byte[] data)
        {
            using (var r = new BinaryReader(new MemoryStream(data)))
                return (T)Read(r, typeof(T), null);
        }

        /// <summary>
        /// Overwrite a battle with a snapshot (Encode of a battle), keeping the receiver's aircraft and gun numbers,
        /// hazards and ground. Throws if the snapshot is of a different set of aircraft.
        /// </summary>
        public static void Restore(Battle into, byte[] snapshot)
        {
            int count = into.Count;
            using (var r = new BinaryReader(new MemoryStream(snapshot)))
                Read(r, typeof(Battle), into);
            if (into.Count != count || into.combat.Length != into.Count) throw new InvalidDataException("Wire: the snapshot has a different set of aircraft");
            foreach (var p in into.planes) if (p == null || p.numbers == null) throw new InvalidDataException("Wire: the snapshot has a different set of aircraft");
        }

        static object Read(BinaryReader r, Type t, object existing)
        {
            if (Skipped(t)) return existing;
            if (t == typeof(double)) return r.ReadDouble();
            if (t == typeof(int)) return r.ReadInt32();
            if (t == typeof(uint)) return r.ReadUInt32();
            if (t == typeof(bool)) return r.ReadBoolean();
            if (t == typeof(long)) return r.ReadInt64();
            if (t == typeof(float)) return r.ReadSingle();
            if (t == typeof(byte)) return r.ReadByte();
            if (t.IsEnum) return Enum.ToObject(t, r.ReadInt64());
            if (t == typeof(string)) return r.ReadBoolean() ? r.ReadString() : null;
            var under = Nullable.GetUnderlyingType(t);
            if (under != null) return r.ReadBoolean() ? Read(r, under, existing) : null;
            if (t.IsValueType)
            {
                object box = existing ?? Activator.CreateInstance(t);
                foreach (var f in Fields(t)) f.SetValue(box, Read(r, f.FieldType, f.GetValue(box)));
                return box;
            }
            if (!r.ReadBoolean()) return null;
            if (t.IsArray)
            {
                var et = t.GetElementType(); int n = r.ReadInt32();
                var old = existing as Array;
                var a = Array.CreateInstance(et, n);
                for (int i = 0; i < n; i++) a.SetValue(Read(r, et, old != null && i < old.Length ? old.GetValue(i) : null), i);
                return a;
            }
            if (t.IsGenericType && t.GetGenericTypeDefinition() == typeof(List<>))
            {
                var et = t.GetGenericArguments()[0]; int n = r.ReadInt32();
                var old = existing as IList;
                var l = (IList)Activator.CreateInstance(t, n);
                for (int i = 0; i < n; i++) l.Add(Read(r, et, old != null && i < old.Count ? old[i] : null));
                return l;
            }
            var rt = t;
            if (!t.IsSealed)
            {
                string name = r.ReadString();
                if (name != "") rt = t.Assembly.GetType(name) ?? throw new InvalidDataException($"Wire: unknown type {name}");
                if (!t.IsAssignableFrom(rt)) throw new InvalidDataException($"Wire: {name} is not a {t}");
            }
            // the object already there keeps what is not sent (a FlightModel its numbers); a new one starts blank
#pragma warning disable SYSLIB0050
            object obj = existing != null && existing.GetType() == rt ? existing : FormatterServices.GetUninitializedObject(rt);
#pragma warning restore SYSLIB0050
            foreach (var f in Fields(rt)) f.SetValue(obj, Read(r, f.FieldType, f.GetValue(obj)));
            return obj;
        }

        // ---------- hashing ----------

        /// <summary>FNV-1a, 64 bits, of some bytes.</summary>
        public static ulong Hash(byte[] data)
        {
            ulong h = 14695981039346656037UL;
            foreach (byte b in data) { h ^= b; h *= 1099511628211UL; }
            return h;
        }

        /// <summary>The battle's hash as it stands, in its current order (see Seating.Hash for the order both machines share).</summary>
        public static ulong Hash(Battle b) => Hash(Encode(b));
    }
}
