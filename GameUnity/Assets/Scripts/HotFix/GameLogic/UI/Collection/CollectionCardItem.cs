using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using DGame;
using GameProto;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace GameLogic
{
	/// <summary>
	/// 图鉴记忆卡（拍立得卡片）。卡体 221×298，照片位 175×169，名称在下、黄笔下划线。
	/// 布局数值取自美术参考图像素测量。
	/// </summary>
	public partial class CollectionCardItem : UIEventItem<CollectionCardItem>
	{
		private static readonly Color NameColor = new Color32(0x4E, 0x39, 0x36, 0xFF);
		private static readonly Color NameColorLocked = new Color32(0x9A, 0x94, 0x8C, 0xFF);
		private static readonly Color LineColor = new Color32(0xFC, 0xD8, 0x64, 0xFF);
		private static readonly Color LineColorLocked = new Color32(0xC9, 0xC6, 0xBE, 0xFF);

		private MemoryConfig m_entry;
		private bool m_owned;
		private Action<CollectionCardItem> m_clickAction;

		public void SetData(MemoryConfig entry, bool owned, Action<CollectionCardItem> onClick)
		{
			m_entry = entry;
			m_owned = owned;
			m_clickAction = onClick;

			bool hasEntry = entry != null;

			// 已获得：照片位 + 记忆名 + 黄线；未获得：灰色照片位 + 问号 + 灰线，不显示名字。
			if (m_imgPhoto != null) m_imgPhoto.gameObject.SetActive(owned);
			if (m_imgPhotoLocked != null) m_imgPhotoLocked.gameObject.SetActive(!owned);
			if (m_tmpLockMark != null) m_tmpLockMark.gameObject.SetActive(!owned);

			if (m_tmpName != null)
			{
				m_tmpName.gameObject.SetActive(owned);
				if (owned)
				{
					m_tmpName.text = hasEntry ? entry.MemoryName : string.Empty;
					m_tmpName.color = NameColor;
				}
				else
				{
					m_tmpName.color = NameColorLocked;
				}
			}

			Color lineColor = owned ? LineColor : LineColorLocked;
			if (m_imgLineLong != null) m_imgLineLong.color = lineColor;
			if (m_imgLineShort != null) m_imgLineShort.color = lineColor;

			if (m_imgPhotoContent != null) m_imgPhotoContent.color = new Color(1f, 1f, 1f, 0f);

			if (owned && hasEntry && !string.IsNullOrWhiteSpace(entry.ImageId) &&
			    GameModule.ResourceModule.CheckLocationValid(entry.ImageId))
			{
				CancellationToken token = gameObject.GetCancellationTokenOnDestroy();
				m_imgPhotoContent.SetSprite(entry.ImageId, false, loaded =>
				{
					if (loaded == null || IsDestroyed || loaded.sprite == null) return;
					loaded.color = Color.white;
					loaded.preserveAspect = true;
				}, token);
			}
		}

		private partial void OnClickCardBtn()
		{
			m_clickAction?.Invoke(this);
		}
	}
}
