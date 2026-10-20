using System.Collections.Generic;
using DGame;
using UnityEngine;

namespace GameLogic
{
	/// <summary>
	/// NPC 说话人名字配色。字幕面板的字号、位置、排版仍以 prefab 为唯一真源，
	/// 这里只覆盖颜色：每个 npcId 一种色相，明度统一压在深色区间，
	/// 保证米白纸面上对比度都在 8:1 以上，同时比正文更深、更跳一点，一眼能认出说话人换了。
	/// </summary>
	internal static class NpcSpeakerPalette
	{
		/// <summary>表里没有的 npcId 用它，值等于 prefab 里 SpeakerLabel 原本的深棕。</summary>
		internal static readonly Color Fallback = new Color(0.243f, 0.161f, 0.086f, 1f);

		/// <summary>
		/// key 是剧情配置表的 npcId（直接就是显示名）。色相分配按人物气质：
		/// 暖色给人味（老周、陈果、陈父），冷色给压迫感和非人感（陈先生、AI），紫给回忆（妻子）。
		/// </summary>
		private static readonly Dictionary<string, Color> Colors = new Dictionary<string, Color>
		{
			// 老周：砖红褐，店里最暖的一盏灯。
			{ "老周", new Color32(0x6B, 0x2E, 0x22, 0xFF) },
			// 陈先生：藏蓝灰，西装、公文包、公事公办的冷。
			{ "陈先生", new Color32(0x2B, 0x3A, 0x4E, 0xFF) },
			// 陈果：松绿，年轻、有生气，压暗过所以不刺眼。
			{ "陈果", new Color32(0x35, 0x52, 0x2E, 0xFF) },
			// AI：深青，非人的电子感，但不用亮青，免得像 UI 提示语。
			{ "AI", new Color32(0x1D, 0x4A, 0x55, 0xFF) },
			// 妻子：藕紫，只出现在回忆里。
			{ "妻子", new Color32(0x4E, 0x3A, 0x5C, 0xFF) },
			// 陈父：橄榄褐，旧年代的克己，比老周更黄更暗，两者不会混。
			{ "陈父", new Color32(0x4A, 0x3D, 0x1E, 0xFF) },
		};

		/// <summary>同一个漏配的 npcId 只警告一次，避免每句话刷一条日志。</summary>
		private static readonly HashSet<string> WarnedIds = new HashSet<string>();

		internal static Color Resolve(string npcId)
		{
			if (string.IsNullOrEmpty(npcId))
			{
				return Fallback;
			}

			if (Colors.TryGetValue(npcId, out Color color))
			{
				return color;
			}

			if (WarnedIds.Add(npcId))
			{
				DLogger.Warning($"NPC「{npcId}」没有配置名字颜色，暂用默认深棕。新增角色请补到 NpcSpeakerPalette.Colors。");
			}

			return Fallback;
		}
	}
}
