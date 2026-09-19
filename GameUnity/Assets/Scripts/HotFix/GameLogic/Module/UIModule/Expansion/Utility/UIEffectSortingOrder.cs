using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace GameLogic
{
    public class UIEffectSortingOrder : MonoBehaviour
    {
        public enum ApplyTarget
        {
            Auto,
            SortingGroup,
            Renderer,
            Both
        }

        [SerializeField] private ApplyTarget m_applyTarget = ApplyTarget.Auto;

        [SerializeField] private int m_orderOffset = 0;

        [SerializeField] private bool m_followCanvasOrder = true;

        [SerializeField] private bool m_keepChildOrder = false;
        
        private Canvas m_cachedCanvas;
        
        private Dictionary<Object, int> m_childBaseOrder = new Dictionary<Object, int>();

        private void OnEnable()
        {
            RefreshOrder();
        }

        public void ResetCache()
        {
            m_cachedCanvas = null;
        }

        public void RecaptureChildOrder()
        {
            m_childBaseOrder.Clear();
        }

        public void SetOrderOffset(int offset)
        {
            m_orderOffset = offset;
            RefreshOrder();
        }

        public void RefreshOrder()
        {
            int canvasOrder = 0;

            if (m_followCanvasOrder)
            {
                if (m_cachedCanvas == null)
                {
                    m_cachedCanvas = GetComponentInParent<Canvas>();
                }

                if (m_cachedCanvas == null)
                {
                    return;
                }
                
                canvasOrder = m_cachedCanvas.sortingOrder;
            }
            int targetOrder = canvasOrder + m_orderOffset;

            switch (m_applyTarget)
            {
                case ApplyTarget.SortingGroup:
                    ApplyToSortingGroups(targetOrder);
                    break;

                case ApplyTarget.Renderer:
                    ApplyToRenderers(targetOrder);
                    break;

                case ApplyTarget.Both:
                    ApplyToSortingGroups(targetOrder);
                    ApplyToRenderers(targetOrder);
                    break;
                
                case ApplyTarget.Auto:
                default:
                    if (!ApplyToSortingGroups(targetOrder))
                    {
                        ApplyToRenderers(targetOrder);
                    }
                    break;
            }
        }

        private void ApplyToRenderers(int targetOrder)
        {
            var renderers = GetComponentsInChildren<Renderer>(true);
            if (renderers == null)
            {
                return;
            }
            for (int i = 0; i < renderers.Length; i++)
            {
                renderers[i].sortingOrder =
                    targetOrder + GetChildBaseOrder(renderers[i], renderers[i].sortingOrder);
            }
        }

        private bool ApplyToSortingGroups(int targetOrder)
        {
            var sortingGroups = GetComponentsInChildren<SortingGroup>(true);
            if (sortingGroups == null)
            {
                return false;
            }
            for (int i = 0; i < sortingGroups.Length; i++)
            {
                sortingGroups[i].sortingOrder =
                    targetOrder + GetChildBaseOrder(sortingGroups[i], sortingGroups[i].sortingOrder);
            }
            return sortingGroups.Length > 0;
        }

        private int GetChildBaseOrder(Object element, int curOrder)
        {
            if (!m_keepChildOrder)
            {
                return 0;
            }

            if (!m_childBaseOrder.TryGetValue(element, out var baseOrder))
            {
                baseOrder = curOrder;
                m_childBaseOrder[element] = baseOrder;
            }
            return baseOrder;
        }
    }
}