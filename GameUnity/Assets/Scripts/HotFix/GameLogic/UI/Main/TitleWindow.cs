using DGame;

namespace GameLogic
{
	/// <summary>
	/// 开始界面首屏（标题页）：整屏 bg + 花体「进入游戏」按钮。
	/// 点击后切到 <see cref="StartMenuWindow"/>。
	/// </summary>
	public partial class TitleWindow
	{
		public override bool FullScreen => true;

		public override bool CanEscClose => false;

		protected override void OnCreate()
		{
			SoundConfigMgr.Instance.PlayBgm(SfxNames.BgmTitle, fadeIn: 1.5f);
		}

		#region 事件

		private partial void OnClickEnterGameBtn()
		{
			Close();
			GameModule.UIModule.ShowWindowAsync<StartMenuWindow>();
		}

		#endregion
	}
}
