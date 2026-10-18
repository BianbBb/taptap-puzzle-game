using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using DGame;
using GameProto;

namespace GameLogic
{
	public partial class MainWindow
	{
		public override bool CanEscClose => false;
		protected override void OnCreate()
		{
			m_tmpTitle.text = "记忆终会消散";
			m_tmpTitle.color = new Color(0.98f, 0.91f, 0.72f, 1f);
			RefreshUI();
		}

		protected override void OnRefresh()
		{
			RefreshUI();
		}

		public void RefreshUI()
		{
			TextMeshProUGUI startLabel = m_btnStartGame == null ? null :
				m_btnStartGame.GetComponentInChildren<TextMeshProUGUI>(true);
			if (startLabel == null) return;
			NarrativeSaveData saveData = NarrativeSaveData.Get;
			bool hasProgress = !string.IsNullOrWhiteSpace(saveData.CurrentNodeId) ||
				saveData.CompletedSessionIds.Count > 0;
			startLabel.text = hasProgress ? "继续游戏" : "开始游戏";
		}

		#region 事件

		private partial void OnClickStartGameBtn()
		{
			Close();
			GameModule.UIModule.ShowWindowAsync<BattleMainUI>();
		}

		private partial void OnClickQuitGameBtn()
		{
			Application.Quit();
		}

		#endregion
	}
}
