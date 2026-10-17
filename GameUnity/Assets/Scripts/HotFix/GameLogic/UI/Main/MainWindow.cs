using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using DGame;
using GameProto;

namespace GameLogic
{
	public partial class MainWindow
	{
		protected override void OnCreate()
		{
			m_tmpTitle.text = "记忆终会消散";
			m_tmpTitle.color = new Color(0.98f, 0.91f, 0.72f, 1f);
		}

		public void RefreshUI()
		{
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
