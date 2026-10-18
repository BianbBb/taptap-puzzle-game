using System.Collections.Generic;
using UnityEngine;

namespace GameLogic
{
	internal static partial class NarrativeLayoutDefaults
	{
		internal struct Entry
		{
			internal readonly bool HasPosition;
			internal readonly Vector2 Position;
			internal readonly bool HasScale;
			internal readonly float Scale;
			internal readonly bool HasLayer;
			internal readonly int Layer;
			internal readonly bool HasMirror;
			internal readonly bool Mirror;

			internal Entry(bool hasPosition, float x, float y, bool hasScale, float scale, bool hasLayer, int layer,
				bool hasMirror = false, bool mirror = false)
			{
				HasPosition = hasPosition;
				Position = new Vector2(x, y);
				HasScale = hasScale;
				Scale = scale;
				HasLayer = hasLayer;
				Layer = layer;
				HasMirror = hasMirror;
				Mirror = mirror;
			}
		}

		private static readonly Dictionary<string, Entry> Entries = CreateEntries();

		private static Dictionary<string, Entry> CreateEntries()
		{
			var entries = new Dictionary<string, Entry>();
			AddGeneratedEntries(entries);
			return entries;
		}

		static partial void AddGeneratedEntries(Dictionary<string, Entry> entries);

		internal static bool TryGetPosition(string part, out Vector2 position)
		{
			if (Entries.TryGetValue(part, out Entry entry) && entry.HasPosition)
			{
				position = entry.Position;
				return true;
			}
			position = default;
			return false;
		}

		internal static bool TryGetScale(string part, out float scale)
		{
			if (Entries.TryGetValue(part, out Entry entry) && entry.HasScale)
			{
				scale = entry.Scale;
				return true;
			}
			scale = default;
			return false;
		}

		internal static bool TryGetLayer(string part, out int layer)
		{
			if (Entries.TryGetValue(part, out Entry entry) && entry.HasLayer)
			{
				layer = entry.Layer;
				return true;
			}
			layer = default;
			return false;
		}

		internal static bool TryGetMirror(string part, out bool mirror)
		{
			if (Entries.TryGetValue(part, out Entry entry) && entry.HasMirror)
			{
				mirror = entry.Mirror;
				return true;
			}
			mirror = false;
			return false;
		}
	}
}
