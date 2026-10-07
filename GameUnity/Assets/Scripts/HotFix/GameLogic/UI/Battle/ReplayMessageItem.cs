using TMPro;
using UnityEngine;
using UnityEngine.UI;
using DGame;
using GameProto;

namespace GameLogic
{
	public partial class ReplayMessageItem
	{
		public ReplyOptionConfig Reply { get; private set; }
		public bool IsUnlocked { get; private set; }

		public void SetData(ReplyOptionConfig reply, bool isUnlocked)
		{
			Reply = reply;
			IsUnlocked = isUnlocked;
			m_tmpContent.text = isUnlocked ? reply.ReplyText : reply.LockedText;
		}

		#region 事件

		#endregion
	}
}
