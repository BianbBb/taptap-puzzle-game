using GameLogic;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace GameEditor
{
	/// <summary>
	/// 图鉴详情卡的「外部改了 prefab 之后怎么让编辑器立刻看到」工具。
	/// 场景：用脚本/离线改过 CollectionWindow.prefab，但编辑器里还是旧版式 ——
	/// 多数是因为 prefab 开在 Prefab Mode（舞台里那份内存副本不会自动重载）。
	/// 菜单：DGame Tools/图鉴/重新导入并校验 CollectionWindow
	/// </summary>
	public static class CollectionDetailPrefabTools
	{
		private const string PrefabPath = "Assets/BundleAssets/UI/CollectionWindow.prefab";

		[MenuItem("DGame Tools/图鉴/重新导入并校验 CollectionWindow")]
		public static void ReimportAndVerify()
		{
			// 1) 关掉 Prefab Mode 舞台，否则它那份内存副本会一直盖着磁盘内容
			PrefabStage stage = PrefabStageUtility.GetCurrentPrefabStage();
			if (stage != null && stage.assetPath == PrefabPath)
			{
				if (stage.scene.isDirty)
				{
					bool discard = EditorUtility.DisplayDialog("Prefab 有未保存改动",
						"CollectionWindow 正在 Prefab Mode 里且有未保存改动。\n" +
						"点「放弃并重载」会用磁盘上的版本覆盖当前舞台；点「取消」则什么都不做。",
						"放弃并重载", "取消");
					if (!discard)
					{
						Debug.Log("[CollectionDetail] 已取消。");
						return;
					}
				}

				StageUtility.GoToMainStage();
				Debug.Log("[CollectionDetail] 已退出 Prefab Mode。");
			}

			// 2) 强制重导入
			AssetDatabase.ImportAsset(PrefabPath, ImportAssetOptions.ForceUpdate);
			AssetDatabase.Refresh();
			Debug.Log("[CollectionDetail] 已强制重导入 " + PrefabPath);

			// 3) 回读校验，把关键数值打到 Console
			GameObject root = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
			if (root == null)
			{
				Debug.LogError("[CollectionDetail] 加载 prefab 失败：" + PrefabPath);
				return;
			}

			Transform detail = root.transform.Find("m_goDetail");
			RectTransform card = detail != null ? detail.Find("DetailCard") as RectTransform : null;
			if (card == null)
			{
				Debug.LogError("[CollectionDetail] 找不到 m_goDetail/DetailCard");
				return;
			}

			Debug.Log(string.Format(
				"[CollectionDetail] DetailCard = {0}x{1} @({2},{3}) rotZ={4:0.###} 子节点 {5} 个",
				card.sizeDelta.x, card.sizeDelta.y,
				card.anchoredPosition.x, card.anchoredPosition.y,
				card.localEulerAngles.z, card.transform.childCount));

			RectTransform stamp = card.Find("StampGroup") as RectTransform;
			if (stamp != null)
			{
				Debug.Log(string.Format("[CollectionDetail] 邮票 {0}x{1} @({2},{3})",
					stamp.sizeDelta.x, stamp.sizeDelta.y,
					stamp.anchoredPosition.x, stamp.anchoredPosition.y));
			}

			TextMeshProUGUI title = card.Find("m_tmpDetailTitle") != null
				? card.Find("m_tmpDetailTitle").GetComponent<TextMeshProUGUI>()
				: null;
			TextMeshProUGUI body = card.Find("m_tmpDetailBody") != null
				? card.Find("m_tmpDetailBody").GetComponent<TextMeshProUGUI>()
				: null;
			Debug.Log(string.Format("[CollectionDetail] 标题字号 {0} / 正文字号 {1} 行距 {2}",
				title != null ? title.fontSize : -1,
				body != null ? body.fontSize : -1,
				body != null ? body.lineSpacing : -1));

			UIBindComponent bind = root.GetComponent<UIBindComponent>();
			if (bind != null)
			{
				Debug.Log("[CollectionDetail] 绑定数 " + GetBindCount(bind));
			}

			Selection.activeObject = root;
		}

		private static int GetBindCount(UIBindComponent bind)
		{
			var field = typeof(UIBindComponent).GetField("m_components",
				System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
			var list = field?.GetValue(bind) as System.Collections.ICollection;
			return list?.Count ?? -1;
		}
	}
}
