using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace DGame
{
    /// <summary>
    /// 把 Assets/AppIcon 下的图标套用到 Player Settings。
    /// 图标素材按「前缀_尺寸.png」命名，菜单和构建脚本共用 <see cref="Apply"/>。
    ///
    /// 说明：本机可能没装 Android Build Support，无法使用 Android 专属的 PlatformIcon API，
    /// 因此 Android 各槽位直接写 ProjectSettings 的 m_BuildTargetPlatformIcons 序列化字段，
    /// 走 Unity 自己的 SerializedObject 通道，不依赖目标平台模块，也不会被编辑器回写覆盖。
    /// </summary>
    public static class AppIconSetter
    {
        public const string IconDir = "Assets/AppIcon";

        private const string MasterPath = IconDir + "/icon_master_1024.png";
        private const string ProjectSettingsPath = "ProjectSettings/ProjectSettings.asset";

        // m_Kind：0 = Legacy/Application，1 = Round，2 = Adaptive（自适应前景+背景，需两张图，暂不自动填）
        private const int KindLegacy = 0;
        private const int KindRound = 1;

        [MenuItem("DGame Tools/Build/应用游戏图标 (App Icon)", priority = 155)]
        public static void ApplyFromMenu()
        {
            bool ok = Apply();
            EditorUtility.DisplayDialog(
                "应用游戏图标",
                ok
                    ? "已写入 Player Settings：\nDefault Icon + Android（Legacy / Round 共 12 个槽位）"
                    : "部分图标写入失败，详见 Console。",
                "好");
        }

        /// <summary>
        /// 同步 Player Settings 里的图标字段，可被 Jenkins / 构建脚本直接调用。
        /// </summary>
        public static bool Apply()
        {
            var master = AssetDatabase.LoadAssetAtPath<Texture2D>(MasterPath);
            if (master == null)
            {
                Debug.LogError($"[AppIcon] 找不到母版图标：{MasterPath}");
                return false;
            }

            ConfigureImporters();

            var index = BuildSizeIndex();
            bool ok = ApplyDefaultIcon(master);
            ok &= ApplyAndroidIcons(index);

            AssetDatabase.SaveAssets();
            Debug.Log(ok ? "[AppIcon] 游戏图标已应用。" : "[AppIcon] 游戏图标应用过程中有失败项，见上方日志。");
            return ok;
        }

        /// <summary>
        /// Default Icon：所有平台的兜底图标。
        /// </summary>
        private static bool ApplyDefaultIcon(Texture2D master)
        {
            try
            {
                PlayerSettings.SetIconsForTargetGroup(BuildTargetGroup.Unknown, new[] { master });

                var back = PlayerSettings.GetIconsForTargetGroup(BuildTargetGroup.Unknown);
                if (back == null || back.Length == 0 || back[0] != master)
                {
                    Debug.LogError("[AppIcon] Default Icon 写入后读回不一致");
                    return false;
                }

                Debug.Log($"[AppIcon] Default Icon 已写入 {master.width}x{master.height}");
                return true;
            }
            catch (Exception e)
            {
                Debug.LogError($"[AppIcon] Default Icon 写入失败：{e.Message}");
                return false;
            }
        }

        /// <summary>
        /// Android：Legacy（36~192）与 Round（36~192）两组槽位。
        /// Adaptive 槽位需要背景/前景两张图成对写入，顺序无法在无 Android 模块的机器上验证，
        /// 这里刻意不动它——留空时 Android 会用 Legacy 图做遮罩，显示正常。
        /// </summary>
        private static bool ApplyAndroidIcons(Dictionary<string, Dictionary<int, Texture2D>> index)
        {
            var assets = AssetDatabase.LoadAllAssetsAtPath(ProjectSettingsPath);
            if (assets == null || assets.Length == 0)
            {
                Debug.LogError($"[AppIcon] 读不到 {ProjectSettingsPath}");
                return false;
            }

            var serialized = new SerializedObject(assets[0]);
            var platforms = serialized.FindProperty("m_BuildTargetPlatformIcons");
            if (platforms == null)
            {
                Debug.LogError("[AppIcon] ProjectSettings 缺少 m_BuildTargetPlatformIcons 字段");
                return false;
            }

            bool foundPlatform = false;
            int filled = 0;

            for (int p = 0; p < platforms.arraySize; p++)
            {
                var platform = platforms.GetArrayElementAtIndex(p);
                var buildTarget = platform.FindPropertyRelative("m_BuildTarget");
                if (buildTarget == null || buildTarget.stringValue != "Android")
                {
                    continue;
                }

                foundPlatform = true;
                var icons = platform.FindPropertyRelative("m_Icons");
                if (icons == null)
                {
                    continue;
                }

                for (int i = 0; i < icons.arraySize; i++)
                {
                    var icon = icons.GetArrayElementAtIndex(i);
                    int width = icon.FindPropertyRelative("m_Width").intValue;
                    int kind = icon.FindPropertyRelative("m_Kind").intValue;

                    string prefix;
                    if (kind == KindLegacy)
                    {
                        prefix = "android_app";
                    }
                    else if (kind == KindRound)
                    {
                        prefix = "android_round";
                    }
                    else
                    {
                        continue;
                    }

                    var texture = Pick(index, prefix, width);
                    if (texture == null)
                    {
                        return false;
                    }

                    var textures = icon.FindPropertyRelative("m_Textures");
                    textures.arraySize = 1;
                    textures.GetArrayElementAtIndex(0).objectReferenceValue = texture;
                    filled++;
                }
            }

            serialized.ApplyModifiedProperties();

            if (!foundPlatform)
            {
                Debug.LogError("[AppIcon] ProjectSettings 里没有 Android 图标定义");
                return false;
            }

            Debug.Log($"[AppIcon] Android 已写入 {filled} 个槽位（Legacy + Round）");
            return filled > 0;
        }

        /// <summary>
        /// 图标不需要 mipmap / 压缩，且大图不能被导入时的 Max Size 截断。
        /// </summary>
        private static void ConfigureImporters()
        {
            foreach (string path in Directory.GetFiles(IconDir, "*.png", SearchOption.TopDirectoryOnly))
            {
                var importer = AssetImporter.GetAtPath(path) as TextureImporter;
                if (importer == null)
                {
                    continue;
                }

                bool dirty = false;
                if (importer.textureType != TextureImporterType.Default) { importer.textureType = TextureImporterType.Default; dirty = true; }
                if (importer.npotScale != TextureImporterNPOTScale.None) { importer.npotScale = TextureImporterNPOTScale.None; dirty = true; }
                if (importer.maxTextureSize < 2048) { importer.maxTextureSize = 2048; dirty = true; }
                if (importer.mipmapEnabled) { importer.mipmapEnabled = false; dirty = true; }
                if (!importer.alphaIsTransparency) { importer.alphaIsTransparency = true; dirty = true; }
                if (!importer.sRGBTexture) { importer.sRGBTexture = true; dirty = true; }
                if (importer.wrapMode != TextureWrapMode.Clamp) { importer.wrapMode = TextureWrapMode.Clamp; dirty = true; }
                if (importer.textureCompression != TextureImporterCompression.Uncompressed) { importer.textureCompression = TextureImporterCompression.Uncompressed; dirty = true; }

                if (dirty)
                {
                    importer.SaveAndReimport();
                }
            }
        }

        /// <summary>
        /// 目录里的 png 按「前缀_尺寸」建索引。
        /// </summary>
        private static Dictionary<string, Dictionary<int, Texture2D>> BuildSizeIndex()
        {
            var index = new Dictionary<string, Dictionary<int, Texture2D>>(StringComparer.Ordinal);

            foreach (string path in Directory.GetFiles(IconDir, "*.png", SearchOption.TopDirectoryOnly))
            {
                string name = Path.GetFileNameWithoutExtension(path);
                int cut = name.LastIndexOf('_');
                if (cut <= 0 || !int.TryParse(name.Substring(cut + 1), out int size))
                {
                    continue;
                }

                var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
                if (tex == null)
                {
                    continue;
                }

                string prefix = name.Substring(0, cut);
                if (!index.TryGetValue(prefix, out var bySize))
                {
                    bySize = new Dictionary<int, Texture2D>();
                    index[prefix] = bySize;
                }

                bySize[size] = tex;
            }

            return index;
        }

        /// <summary>
        /// 优先取精确尺寸；缺失时退到最近的更大尺寸，最后退到该前缀最大的一张。
        /// </summary>
        private static Texture2D Pick(Dictionary<string, Dictionary<int, Texture2D>> index, string prefix, int size)
        {
            if (!index.TryGetValue(prefix, out var bySize) || bySize.Count == 0)
            {
                Debug.LogError($"[AppIcon] 没有任何 {prefix}_*.png 素材");
                return null;
            }

            if (bySize.TryGetValue(size, out var exact))
            {
                return exact;
            }

            Texture2D nearest = null;
            int nearestDelta = int.MaxValue;
            foreach (var pair in bySize)
            {
                if (pair.Key >= size && pair.Key - size < nearestDelta)
                {
                    nearestDelta = pair.Key - size;
                    nearest = pair.Value;
                }
            }

            if (nearest != null)
            {
                Debug.LogWarning($"[AppIcon] {prefix} 缺 {size}px，用 {nearest.width}px 顶替（Unity 会缩放）");
                return nearest;
            }

            int max = 0;
            foreach (var pair in bySize)
            {
                if (pair.Key > max)
                {
                    max = pair.Key;
                    nearest = pair.Value;
                }
            }

            return nearest;
        }
    }
}
