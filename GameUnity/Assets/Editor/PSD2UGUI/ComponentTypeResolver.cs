#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace DGame.PSD2UGUI
{
    /// <summary>
    /// 通过类名(可带命名空间)反射查找组件类型。结果会缓存。
    /// </summary>
    public static class ComponentTypeResolver
    {
        private static readonly Dictionary<string, Type> s_typeCache = new Dictionary<string, Type>();

        public static Type Resolve(string typeName, Type fallback)
        {
            if (string.IsNullOrEmpty(typeName)) return fallback;
            if (s_typeCache.TryGetValue(typeName, out var cached)) return cached ?? fallback;

            Type result = null;

            // 全限定名直接尝试
            result = Type.GetType(typeName);
            if (result == null)
            {
                foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    if (asm.IsDynamic) continue;
                    try
                    {
                        // 1) 全限定名
                        var t = asm.GetType(typeName, false);
                        if (t != null && IsComponentLike(t)) { result = t; break; }

                        // 2) 仅按简单名匹配
                        foreach (var type in asm.GetTypes())
                        {
                            if (!IsComponentLike(type)) continue;
                            if (type.FullName == typeName || type.Name == typeName)
                            {
                                result = type;
                                break;
                            }
                        }
                        if (result != null) break;
                    }
                    catch
                    {
                        // 忽略反射异常的程序集
                    }
                }
            }

            if (result == null)
            {
                Debug.LogWarning($"[PSD2UGUI] 找不到组件类型: {typeName}, 回退到 {fallback?.FullName}");
            }
            s_typeCache[typeName] = result;
            return result ?? fallback;
        }

        private static bool IsComponentLike(Type t)
        {
            return t != null && typeof(Component).IsAssignableFrom(t) && !t.IsAbstract;
        }

        public static void ClearCache() => s_typeCache.Clear();
    }
}
#endif
