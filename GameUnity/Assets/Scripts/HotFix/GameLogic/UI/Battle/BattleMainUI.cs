using TMPro;
using UnityEngine;
using UnityEngine.UI;
using DGame;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using GameProto;
using SuperScrollView;

namespace GameLogic
{
	public partial class BattleMainUI
	{
		private const string FIRST_STORY_NODE_ID = "N01";
		private readonly List<ReplayMessageItem> m_replyItems = new List<ReplayMessageItem>();
		private readonly List<(bool IsPlayer, string Text)> m_messages = new List<(bool, string)>();
		private UILoopListViewWidget m_messageList;
		private ReplyOptionConfig m_selectedReply;
		private bool m_isBusy;
		private bool m_hasReplied;

		#region Override

		public override bool FullScreen => true;

		protected override void OnCreate()
		{
			m_tfReplayMessageNode.gameObject.SetActive(false);
			m_tfReplayEmoNode.gameObject.SetActive(false);
			m_messageList = CreateWidget<UILoopListViewWidget>(m_scrollMessage.gameObject);
			m_messageList.LoopRectView.InitListView(0, OnGetMessageItemByIndex);
			StartStoryAsync().Forget();
		}

		protected override void OnRefresh()
		{
			RefreshUI();
		}

		protected override void OnDestroy()
		{
			m_replyItems.Clear();
			m_messages.Clear();
			m_messageList = null;
			m_selectedReply = null;
		}

		#endregion

		/// <summary>
		/// 记忆发生变化或重新显示窗口时刷新回复的解锁状态。
		/// </summary>
		public void RefreshUI()
		{
			if (IsDestroyed || m_isBusy || m_hasReplied || m_selectedReply != null)
			{
				return;
			}

			foreach (var item in m_replyItems)
			{
				item.SetData(item.Reply, NarrativeSaveData.Get.HasAllMemories(item.Reply.RequiredMemory));
			}
		}

		private async UniTask StartStoryAsync()
		{
			m_isBusy = true;
			try
			{
				var story = NarrativeConfigMgr.Instance.GetStoryNode(FIRST_STORY_NODE_ID);
				if (story == null)
				{
					DLogger.Error("Story node not found: {0}", FIRST_STORY_NODE_ID);
					return;
				}

				AddMessage(false, story.NpcText);

				foreach (string replyId in story.Replies)
				{
					var reply = NarrativeConfigMgr.Instance.GetReply(replyId);
					if (reply == null)
					{
						DLogger.Error("Reply option not found: {0}", replyId);
						return;
					}

					var item = await CreateWidgetByTypeAsync<ReplayMessageItem>(m_tfReplayMessageNode, false);
					if (IsDestroyed || item == null)
					{
						return;
					}
					item.SetData(reply, NarrativeSaveData.Get.HasAllMemories(reply.RequiredMemory));
					item.BindClickEvent(OnClickReply, needEmptyImg: true);
					m_replyItems.Add(item);
					item.Show(true);
				}

				m_tfReplayMessageNode.gameObject.SetActive(m_replyItems.Count > 0);
			}
			finally
			{
				m_isBusy = false;
			}
		}

		private void OnClickReply(ReplayMessageItem item)
		{
			if (IsDestroyed || m_isBusy || m_hasReplied || m_selectedReply != null)
			{
				return;
			}

			// 点击时再次检查记忆，避免显示状态与当前存档不一致。
			item.SetData(item.Reply, NarrativeSaveData.Get.HasAllMemories(item.Reply.RequiredMemory));
			if (!item.IsUnlocked)
			{
				return;
			}

			SelectReplyAsync(item.Reply).Forget();
		}

		private async UniTask SelectReplyAsync(ReplyOptionConfig reply)
		{
			m_isBusy = true;
			m_selectedReply = reply;
			try
			{
				m_tfReplayMessageNode.gameObject.SetActive(false);
				m_tfReplayEmoNode.gameObject.SetActive(true);
				// 奖励在选择解锁回复时发放，不等待情绪选择。
				NarrativeSaveData.Get.AddMemories(reply.RewardMemory);
				foreach (string emotionId in reply.AllowedEmotion)
				{
					var emotion = NarrativeConfigMgr.Instance.GetEmotion(emotionId);
					if (emotion == null)
					{
						DLogger.Error("Emotion config not found: {0}", emotionId);
						return;
					}

					var item = await CreateWidgetByTypeAsync<ReplayEmoItem>(m_tfReplayEmoNode, false);
					if (IsDestroyed || item == null)
					{
						return;
					}
					item.SetData(emotion);
					item.BindClickEvent(OnClickEmotion, needEmptyImg: true);
					item.Show(true);
				}
			}
			finally
			{
				m_isBusy = false;
			}
		}

		private void OnClickEmotion(ReplayEmoItem item)
		{
			if (IsDestroyed || m_isBusy || m_hasReplied || m_selectedReply == null ||
				!m_selectedReply.AllowedEmotion.Contains(item.Emotion.Id))
			{
				return;
			}

			SendReply();
		}

		private void SendReply()
		{
			string replyText = m_selectedReply.ReplyText;
			m_hasReplied = true;
			m_selectedReply = null;
			m_tfReplayEmoNode.gameObject.SetActive(false);
			AddMessage(true, replyText);
		}

		private LoopListViewItem2 OnGetMessageItemByIndex(LoopListView2 listView, int index)
		{
			if (IsDestroyed || index < 0 || index >= m_messages.Count)
			{
				return null;
			}

			var message = m_messages[index];
			UILoopItemWidget item;
			if (message.IsPlayer)
			{
				var right = m_messageList.CreateItem<RightMessageItem>();
				if (right == null)
				{
					return null;
				}
				right.SetMessage(message.Text);
				item = right;
			}
			else
			{
				var left = m_messageList.CreateItem<LeftMessageItem>();
				if (left == null)
				{
					return null;
				}
				left.SetMessage(message.Text);
				item = left;
			}

			item.SetItemIndex(index);
			item.UpdateItem(index);
			item.LoopItem.Padding = 20f;
			LayoutRebuilder.ForceRebuildLayoutImmediate(item.rectTransform);
			return item.LoopItem;
		}

		private void AddMessage(bool isPlayer, string text)
		{
			m_messages.Add((isPlayer, text));
			var listView = m_messageList.LoopRectView;
			listView.SetListItemCount(m_messages.Count, false);
			listView.MovePanelToItemIndex(m_messages.Count - 1, 0f);
		}
		
		#region 事件

		private partial void OnClickSettingsBtn()
		{
		}

		private partial void OnClickEmoCollectionBtn()
		{
		}

		#endregion
	}
}
