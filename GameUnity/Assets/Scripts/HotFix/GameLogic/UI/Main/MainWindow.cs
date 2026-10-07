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
		}

		#endregion
	}
}
