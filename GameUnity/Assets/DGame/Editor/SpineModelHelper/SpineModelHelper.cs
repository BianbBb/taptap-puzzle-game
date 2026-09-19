#if UNITY_EDITOR && SPINE_UNITY && SPINE_CSHARP

using System.IO;
using Spine;
using Spine.Unity;
using Spine.Unity.Editor;
using UnityEditor;
using UnityEngine;

namespace DGame
{
    public static partial class SpineModelHelper
    {
        private static readonly string[] s_defaultMainModelAnimations =
        {
            "idle",
            "run",
            "walk",
        };

        private static readonly string[] s_ikDummyNames =
        {
            "ACTOR_WEAPON_TARGET",
        };

        [MenuItem("Assets/Spine/处理模型主体")]
        private static void GenSpineMainModel()
        {
            if (Selection.assetGUIDs.Length <= 0)
            {
                return;
            }

            for (int i = 0; i < Selection.assetGUIDs.Length; i++)
            {
                var guid = Selection.assetGUIDs[i];
                var onePath = AssetDatabase.GUIDToAssetPath(guid);
                Debug.LogFormat("开始处理模型主体: {0}", onePath);

                if (!File.Exists(onePath))
                {
                    continue;
                }

                var skeletonDataAsset = AssetDatabase.LoadAssetAtPath<SkeletonDataAsset>(onePath);
                if (skeletonDataAsset == null)
                {
                    continue;
                }

                if (skeletonDataAsset.GetSkeletonData(true) == null)
                {
                    EditorUtility.DisplayDialog("Invalid SkeletonDataAsset",
                        "Unable to create Spine GameObject.\n\nPlease check your SkeletonDataAsset.", "Ok");
                    continue;
                }

                var assetName = skeletonDataAsset.name.Replace(AssetUtility.SkeletonDataSuffix, "");
                var newSkeletonComponent = EditorInstantiation.InstantiateSkeletonAnimation(skeletonDataAsset);
                newSkeletonComponent.AnimationName = GetDefaultMainModelAnimation(skeletonDataAsset);

                var newGameObject = newSkeletonComponent.gameObject;
                newGameObject.name = assetName;

                SpawnDummy(newGameObject);
                SetExpandedRecursive(newGameObject, true);

                var savePath = GetSavePath(onePath);
                savePath += $"/{assetName}";
                if (string.IsNullOrEmpty(savePath))
                {
                    Debug.Log("savePath error.");
                    Object.DestroyImmediate(newGameObject);
                    continue;
                }

                if (!Directory.Exists(savePath))
                {
                    Directory.CreateDirectory(savePath);
                }

                savePath = $"{savePath}/{assetName}.prefab";
                PrefabUtility.SaveAsPrefabAsset(newGameObject, savePath);
                Debug.LogFormat("模型主体生成结束. {0}", savePath);
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }

        private static string GetDefaultMainModelAnimation(SkeletonDataAsset skeletonDataAsset)
        {
            var skeletonData = skeletonDataAsset.GetSkeletonData(false);
            if (skeletonData == null)
            {
                return string.Empty;
            }

            foreach (var animationName in s_defaultMainModelAnimations)
            {
                if (skeletonData.FindAnimation(animationName) != null)
                {
                    return animationName;
                }
            }

            return skeletonData.Animations.Count > 0
                ? skeletonData.Animations.Items[0].Name
                : string.Empty;
        }

        private static void SpawnDummy(GameObject spineObj)
        {
            if (spineObj == null)
            {
                return;
            }

            Debug.Log("开始处理骨骼.");

            var skeletonUtility = spineObj.GetComponent<SkeletonUtility>();
            if (skeletonUtility == null)
            {
                skeletonUtility = spineObj.AddComponent<SkeletonUtility>();
                var boneRoot = skeletonUtility.GetBoneRoot();
                var skeleton = skeletonUtility.SkeletonComponent.Skeleton;
                SpawnBoneRecursively(skeletonUtility, skeleton.RootBone, boneRoot, true);
                skeletonUtility.CollectBones();
            }

            Debug.Log("处理骨骼结束.");
        }

        private static void SpawnBoneRecursively(SkeletonUtility skeletonUtility, Bone bone, Transform parent,
            bool hasSpecialDummy)
        {
            var mode = SkeletonUtilityBone.Mode.Follow;
            if (hasSpecialDummy && IsIkDummy(bone.Data.Name))
            {
                mode = SkeletonUtilityBone.Mode.Override;
            }

            var go = skeletonUtility.SpawnBone(bone, parent, mode, true, true, true);
            ExposedList<Bone> childrenBones = bone.Children;
            for (int i = 0, n = childrenBones.Count; i < n; i++)
            {
                var child = childrenBones.Items[i];
                SpawnBoneRecursively(skeletonUtility, child, go.transform, hasSpecialDummy);
            }
        }

        private static bool IsIkDummy(string boneName)
        {
            for (int i = 0; i < s_ikDummyNames.Length; i++)
            {
                if (s_ikDummyNames[i] == boneName)
                {
                    return true;
                }
            }

            return false;
        }
    }
}

#endif