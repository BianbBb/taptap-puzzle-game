using TMPro;
using UnityEngine;
using UnityEngine.UI;
using DGame;
using GameProto;

namespace GameLogic
{
	public partial class ReplayEmoItem
	{
		public EmotionConfig Emotion { get; private set; }

		public void SetData(EmotionConfig emotion)
		{
			Emotion = emotion;
			m_tmpName.text = emotion.EmotionName;
		}

		#region 事件

		#endregion
	}
}
