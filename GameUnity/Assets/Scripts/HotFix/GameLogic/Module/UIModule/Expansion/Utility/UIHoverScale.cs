using DG.Tweening;
using UnityEngine;
using UnityEngine.EventSystems;

namespace GameLogic
{
	/// <summary>
	/// 悬停轻微放大，移出复位。只做视觉反馈，不拦截点击（不实现 IPointerClickHandler）。
	/// 射线由子节点的 Image 提供，PointerEnter/Exit 冒泡到这里。
	/// </summary>
	[DisallowMultipleComponent]
	public sealed class UIHoverScale : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
	{
		[SerializeField, Min(1f), Tooltip("悬停时的放大倍数")]
		private float m_hoverScale = 1.08f;

		[SerializeField, Min(0f), Tooltip("放大/复位时长（秒）")]
		private float m_duration = 0.12f;

		private Vector3 m_baseScale;
		private Tween m_tween;
		private bool m_inited;

		private void Awake()
		{
			Init();
		}

		public void OnPointerEnter(PointerEventData eventData)
		{
			Play(m_baseScale * m_hoverScale);
		}

		public void OnPointerExit(PointerEventData eventData)
		{
			Play(m_baseScale);
		}

		/// <summary>外部改了尺寸/缩放后调用，重新记录基准，避免复位到旧值。</summary>
		public void ResetBaseScale()
		{
			m_baseScale = transform.localScale;
			m_inited = true;
		}

		private void Init()
		{
			if (m_inited)
			{
				return;
			}

			m_inited = true;
			m_baseScale = transform.localScale;
		}

		private void Play(Vector3 target)
		{
			Init();
			m_tween?.Kill();
			m_tween = transform.DOScale(target, m_duration)
				.SetUpdate(true)
				.SetEase(Ease.OutQuad)
				.SetLink(gameObject);
		}

		private void OnDisable()
		{
			m_tween?.Kill();
			m_tween = null;
			// 切页/关窗时可能停在放大状态，复位。
			if (m_inited)
			{
				transform.localScale = m_baseScale;
			}
		}
	}
}
