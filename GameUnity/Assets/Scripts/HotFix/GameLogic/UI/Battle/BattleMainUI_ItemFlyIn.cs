using Cysharp.Threading.Tasks;
using DGame;
using DG.Tweening;
using GameProto;
using UnityEngine;
using UnityEngine.UI;

namespace GameLogic
{
	/// <summary>
	/// 物品入库演出：GrantItem 一发生，物品图标从「玩家拿到它的地方」飞进左上角物品栏，
	/// 落袋那一刻才点亮槽位图标 + 弹一下 + 叮一声。
	///
	/// 为什么要有这一段：物品栏容量只有 1，入库原本毫无提示，玩家一路点下去根本不知道
	/// 自己多了件东西，下一句 NPC 就已经在问「你看出来什么了」。
	/// 演出必须**先空、后飞、最后才亮**：槽位如果提前亮起，飞行图标会像第二件物品。
	/// </summary>
	public partial class BattleMainUI
	{
		/// <summary>飞行段时长（不含起手放大）。</summary>
		private const float ItemGrantFlySeconds = 0.46f;
		/// <summary>起手在起点放大淡入，让眼睛先找到它。</summary>
		private const float ItemGrantRiseSeconds = 0.16f;
		/// <summary>起点尺寸倍率（相对 <see cref="ItemGrantIconSize"/>）。</summary>
		private const float ItemGrantFlyStartScale = 1.05f;
		/// <summary>落袋尺寸倍率：190 × 0.62 ≈ 118，和槽位里的 124 差不多大。</summary>
		private const float ItemGrantFlyEndScale = 0.62f;
		/// <summary>弧线拱高（世界单位，按 1920 参考分辨率量）。</summary>
		private const float ItemGrantFlyArcHeight = 150f;
		/// <summary>飞行图标初始边长。</summary>
		private const float ItemGrantIconSize = 190f;
		/// <summary>落袋光晕的直径。</summary>
		private const float ItemGrantHaloSize = 130f;
		/// <summary>落袋后槽位弹一下的幅度。</summary>
		private const float ItemGrantPunchScale = 0.24f;
		/// <summary>槽位弹一下的时长。</summary>
		private const float ItemGrantPunchSeconds = 0.28f;
		/// <summary>落袋后等一帧再弹：RefreshItemSlot 的图标是异步加载的，太早弹会弹在空图上。</summary>
		private const float ItemGrantPunchDelay = 0.04f;
		/// <summary>整段演出的超时上限，超了就按「已落袋」收尾，别把槽位永远锁在空态。</summary>
		private const float ItemGrantFlyTimeout = 1.2f;

		private static readonly Color ItemGrantHaloColor = new Color(1f, 0.88f, 0.62f, 1f);

		/// <summary>
		/// 飞入进行中：这段时间里 <see cref="RefreshItemSlot"/> 只刷空态。
		/// 不加这个守卫的话，场景拾取那条链上 RefreshUI 会先把图标点亮，然后飞行图标再飞一次。
		/// </summary>
		private bool m_itemGrantFlyActive;

		/// <summary>
		/// 播一次物品入库演出。
		/// <paramref name="source"/> 是「物品从哪来」（场景热点 / 物品页部件）；
		/// 传 null 表示节点自动授予，从对话纸中心起飞。
		/// 没有图、物品栏不可见、正在布局编辑时直接落袋，不硬演。
		/// </summary>
		private void PlayItemGrantFlyIn(string itemId, RectTransform source = null)
		{
			if (IsDestroyed || string.IsNullOrWhiteSpace(itemId))
			{
				RefreshItemSlot();
				return;
			}

			NarrativeItemConfig item = NarrativeConfigMgr.Instance.GetNarrativeItem(itemId);
			string imageId = item == null ? null : item.ImageId;
			bool canFly = !string.IsNullOrWhiteSpace(imageId) && imageId != "placeholder" &&
				GameModule.ResourceModule.CheckLocationValid(imageId) &&
				!m_layoutEditMode && m_btnItemCollection != null &&
				m_btnItemCollection.gameObject.activeInHierarchy;
			if (!canFly)
			{
				RefreshItemSlot();
				return;
			}

			m_itemGrantFlyActive = true;
			// 先把槽位刷成空态：落袋之前背包还不能是「已经打开了」的样子。
			RefreshItemSlot();

			var fly = new GameObject("ItemGrantFlyIn", typeof(RectTransform), typeof(Image),
				typeof(CanvasGroup));
			fly.transform.SetParent(transform, false);
			fly.transform.SetAsLastSibling();
			RectTransform rect = fly.GetComponent<RectTransform>();
			rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
			rect.pivot = new Vector2(0.5f, 0.5f);
			rect.sizeDelta = new Vector2(ItemGrantIconSize, ItemGrantIconSize);
			rect.localScale = Vector3.one * 0.4f;
			Vector3 start = ResolveItemGrantFlyStart(source);
			rect.position = start;

			CanvasGroup group = fly.GetComponent<CanvasGroup>();
			group.alpha = 0f;
			group.blocksRaycasts = false;
			group.interactable = false;

			Image icon = fly.GetComponent<Image>();
			icon.preserveAspect = true;
			icon.raycastTarget = false;
			icon.color = Color.white;

			// 光晕垫在图标后面，随图标一起缩小；落袋那一下它就是槽位周围的亮圈。
			var halo = new GameObject("Halo", typeof(RectTransform), typeof(Image));
			halo.transform.SetParent(rect, false);
			RectTransform haloRect = halo.GetComponent<RectTransform>();
			haloRect.anchorMin = haloRect.anchorMax = new Vector2(0.5f, 0.5f);
			haloRect.pivot = new Vector2(0.5f, 0.5f);
			haloRect.sizeDelta = new Vector2(ItemGrantHaloSize, ItemGrantHaloSize);
			haloRect.SetAsFirstSibling();
			Image haloImage = halo.GetComponent<Image>();
			haloImage.sprite = GetEmergeHaloSprite();
			haloImage.color = new Color(ItemGrantHaloColor.r, ItemGrantHaloColor.g, ItemGrantHaloColor.b, 0f);
			haloImage.raycastTarget = false;

			bool finished = false;
			void Finish()
			{
				if (finished)
				{
					return;
				}

				finished = true;
				if (fly != null)
				{
					UnityEngine.Object.Destroy(fly);
				}

				m_itemGrantFlyActive = false;
				if (IsDestroyed)
				{
					return;
				}

				// 落袋这一刻才把背包刷成「有物品」：图标 + 打开的包 + 呼吸。
				RefreshItemSlot();
				// 飞的过程中物品已经被 OnExit 收走了（比如进了节点立刻被点掉），就别再庆祝。
				if (!IsHoldingNarrativeItem(itemId))
				{
					return;
				}

				SoundConfigMgr.Instance.PlaySfx(SfxNames.ItemGain);
				PunchItemSlotIcon();
			}

			// 兜底：资源加载失败时（bundle 未就绪 / 地址拼错）SetSprite 的回调不会触发，
			// 不补这一手 m_itemGrantFlyActive 会永久为 true，背包再也亮不起来。
			DOVirtual.DelayedCall(ItemGrantFlyTimeout, Finish, false);

			icon.SetSprite(imageId, false, loaded =>
			{
				if (IsDestroyed || finished || loaded == null || loaded.sprite == null)
				{
					return;
				}
				loaded.preserveAspect = true;
				loaded.raycastTarget = false;
				loaded.color = Color.white;

				Vector3 end = m_imgItemSlot != null
					? m_imgItemSlot.transform.position : m_btnItemCollection.transform.position;
				Vector3 control = (start + end) * 0.5f + Vector3.up * ItemGrantFlyArcHeight;

				DOTween.Sequence()
					.Append(rect.DOScale(ItemGrantFlyStartScale, ItemGrantRiseSeconds).SetEase(Ease.OutBack))
					.Join(group.DOFade(1f, ItemGrantRiseSeconds))
					.Join(DOTween.To(() => 0f, alpha =>
						{
							if (haloImage != null)
							{
								haloImage.color = new Color(ItemGrantHaloColor.r, ItemGrantHaloColor.g,
									ItemGrantHaloColor.b, alpha * 0.55f);
							}
						}, 1f, ItemGrantRiseSeconds))
					.Append(DOVirtual.Float(0f, 1f, ItemGrantFlySeconds, progress =>
					{
						if (rect == null)
						{
							return;
						}

						rect.position = EvaluateQuadraticBezier(start, control, end, progress);
						float scale = Mathf.Lerp(ItemGrantFlyStartScale, ItemGrantFlyEndScale, progress);
						rect.localScale = new Vector3(scale, scale, 1f);
					}).SetEase(Ease.InOutCubic))
					.Append(group.DOFade(0f, 0.1f))
					.OnComplete(Finish)
					// SetLink 的目标被销毁（切场景 / 重开）时 tween 会被 kill，这里负责把槽位补亮。
					.OnKill(Finish)
					.SetUpdate(true)
					.SetLink(fly);
			}, gameObject.GetCancellationTokenOnDestroy());
		}

		/// <summary>
		/// 起飞点：有具体来源就用来源中心，否则用对话纸中心（玩家视线本来就落在那里）。
		/// </summary>
		private Vector3 ResolveItemGrantFlyStart(RectTransform source)
		{
			if (source != null)
			{
				return source.TransformPoint(source.rect.center);
			}

			RectTransform host = m_dialoguePanel != null ? m_dialoguePanel.transform as RectTransform : null;
			if (host != null)
			{
				return host.TransformPoint(host.rect.center);
			}

			RectTransform self = transform as RectTransform;
			return self != null ? self.TransformPoint(self.rect.center) : transform.position;
		}

		/// <summary>二次贝塞尔：起点 → 拱顶控制点 → 落袋点。</summary>
		private static Vector3 EvaluateQuadraticBezier(Vector3 start, Vector3 control, Vector3 end,
			float progress)
		{
			float inverse = 1f - progress;
			return inverse * inverse * start + 2f * inverse * progress * control + progress * progress * end;
		}

		private static bool IsHoldingNarrativeItem(string itemId)
		{
			if (string.IsNullOrWhiteSpace(itemId))
			{
				return false;
			}

			System.Collections.Generic.IReadOnlyList<string> owned =
				NarrativeSaveData.Get.GetNarrativeItemIdsInStableOrder();
			for (int i = 0; i < owned.Count; i++)
			{
				if (owned[i] == itemId)
				{
					return true;
				}
			}

			return false;
		}

		/// <summary>
		/// 槽位弹一下。只动图标子节点，不动按钮本身 —— 按钮上挂着呼吸缩放，两套动画抢 localScale 会抖。
		/// </summary>
		private void PunchItemSlotIcon()
		{
			if (m_imgItemSlot == null)
			{
				return;
			}

			Transform target = m_imgItemSlot.transform;
			DOVirtual.DelayedCall(ItemGrantPunchDelay, () =>
			{
				if (target == null)
				{
					return;
				}

				target.DOKill();
				target.localScale = Vector3.one;
				target.DOPunchScale(new Vector3(ItemGrantPunchScale, ItemGrantPunchScale, 0f),
						ItemGrantPunchSeconds, 9, 0.6f)
					.SetUpdate(true)
					.SetLink(target.gameObject);
			}, false);
		}
	}
}
