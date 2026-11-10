using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;

namespace RimWorldAccess
{
    /// <summary>
    /// Resolves a compat module's reflection view of another mod's internals and answers
    /// <see cref="Ready"/> once at the end. Every type missing means the mod is absent: the
    /// surface goes quietly not-ready and member lookups against those types stay silent. A
    /// missing member on a present type, or a missing type beside a resolved one, means the mod
    /// changed shape: Ready logs every missing name, once, so a mod update is diagnosable from
    /// the log. Version-optional types and members must not go through the surface; resolve
    /// them with raw AccessTools.
    /// </summary>
    public sealed class ReflectionSurface
    {
        private readonly string context;
        private readonly List<string> missingMembers = new List<string>();
        private readonly List<string> missingTypes = new List<string>();
        private bool anyTypeResolved;
        private bool reported;

        public ReflectionSurface(string context)
        {
            this.context = context;
        }

        public Type Type(string fullName)
        {
            Type t = AccessTools.TypeByName(fullName);
            if (t == null)
            {
                missingTypes.Add(fullName);
            }
            else
            {
                anyTypeResolved = true;
            }
            return t;
        }

        /// <summary>
        /// Declares a type the caller obtained elsewhere (a constructor parameter, a registry
        /// lookup) as part of this surface. Without this, a class that resolves no types by
        /// name would decline silently on member drift instead of naming what went missing.
        /// </summary>
        public Type Supplied(string label, Type type)
        {
            if (type == null)
            {
                missingTypes.Add(label);
            }
            else
            {
                anyTypeResolved = true;
            }
            return type;
        }

        public PropertyInfo Property(Type type, string name)
        {
            return Record(type, name, type == null ? null : AccessTools.Property(type, name));
        }

        public FieldInfo Field(Type type, string name)
        {
            return Record(type, name, type == null ? null : AccessTools.Field(type, name));
        }

        public MethodInfo Method(Type type, string name, Type[] parameters = null)
        {
            return Record(type, name, type == null ? null : AccessTools.Method(type, name, parameters));
        }

        public ConstructorInfo Constructor(Type type, Type[] parameters = null)
        {
            ConstructorInfo ctor = type == null ? null : AccessTools.Constructor(type, parameters);
            if (type != null && ctor == null)
            {
                missingMembers.Add(type.Name + "..ctor");
            }
            return ctor;
        }

        /// <summary>
        /// Records a required binding the other resolvers cannot express (a hand-closed
        /// generic method, a boxed enum value): null marks the surface not-ready under
        /// <paramref name="label"/>.
        /// </summary>
        public T Required<T>(string label, T value) where T : class
        {
            if (value == null)
            {
                missingMembers.Add(label);
            }
            return value;
        }

        public object EnumValue(Type enumType, string name)
        {
            if (enumType == null)
            {
                return null;
            }
            if (enumType.IsEnum && Enum.IsDefined(enumType, name))
            {
                return Enum.Parse(enumType, name);
            }
            missingMembers.Add(enumType.Name + "." + name);
            return null;
        }

        /// <summary>A member that some mod versions declare as a field and others as a property.</summary>
        public MemberInfo FieldOrProperty(Type type, string name)
        {
            if (type == null)
            {
                return null;
            }
            MemberInfo member = (MemberInfo)AccessTools.Field(type, name) ?? AccessTools.Property(type, name);
            return Record(type, name, member);
        }

        /// <summary>
        /// Non-recording lookup for version-optional members: a miss returns null without
        /// marking any surface not-ready. Use the instance methods for required members.
        /// </summary>
        public static MemberInfo TryFieldOrProperty(Type type, string name)
        {
            if (type == null)
            {
                return null;
            }
            return (MemberInfo)AccessTools.Field(type, name) ?? AccessTools.Property(type, name);
        }

        public static object ValueOf(MemberInfo member, object instance)
        {
            if (member is FieldInfo field)
            {
                return field.GetValue(instance);
            }
            if (member is PropertyInfo property)
            {
                return property.GetValue(instance, null);
            }
            return null;
        }

        private T Record<T>(Type type, string name, T member) where T : MemberInfo
        {
            if (type != null && member == null)
            {
                missingMembers.Add(type.Name + "." + name);
            }
            return member;
        }

        public bool Ready
        {
            get
            {
                if (missingTypes.Count == 0 && missingMembers.Count == 0)
                {
                    return true;
                }
                if (!anyTypeResolved)
                {
                    // The mod is absent altogether; declining is not news.
                    return false;
                }
                if (!reported)
                {
                    reported = true;
                    var missing = new List<string>(missingTypes);
                    missing.AddRange(missingMembers);
                    ModLogger.Error(context + ": mod surface changed, missing " + string.Join(", ", missing.ToArray()) + "; declining.");
                }
                return false;
            }
        }
    }
}
