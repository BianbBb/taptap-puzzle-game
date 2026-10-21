#if SPINE_UNITY && SPINE_CSHARP

using System;
using System.Collections.Generic;
using DGame;
using UnityEngine;
using UnityEngine.UI;
using Spine.Unity;
using Spine;

namespace GameLogic
{
    public class UISpineWidget : UIWidget
    {
        #region 脚本工具生成的代码

        private Transform m_tfUISpineRoot;
        private GameObject m_goSpineModel;
        private Transform m_tfEffRoot;

        protected override void ScriptGenerator()
        {
            m_tfUISpineRoot = FindChild("m_tfUISpineRoot");
            m_goSpineModel = FindChild("m_tfUISpineRoot/m_goSpineModel").gameObject;
            m_tfEffRoot = FindChild("m_tfEffRoot");
        }

        #endregion

        #region Override

        protected override void BindMemberProperty()
        {
            m_skeletonGraphic = m_goSpineModel.GetComponent<SkeletonGraphic>();
            // 预制体上是 allowMultipleCanvasRenderers=1 + canvasRenderers=[] 这个坏组合：
            // SkeletonGraphic.Rebuild 里只要 allowMultipleCanvasRenderers 为真，就会把自己那个
            // CanvasRenderer.Clear() 掉，然后把网格画进 canvasRenderers 里的**子**渲染器 ——
            // 而列表是空的，等于网格谁都不画。症状就是「骨骼在播、皮肤和动作都对、就是不显示」。
            // 关掉它走单渲染器路径：UpdateMeshSingleCanvasRenderer 会 SetMesh + SetTexture。
            if (m_skeletonGraphic != null)
            {
                m_skeletonGraphic.allowMultipleCanvasRenderers = false;
            }

            EnsureAnimationState();
        }

        protected override void OnDestroy()
        {
            CancelTimer();
            m_clickAction = null;
            m_skeletonGraphic = null;
            m_curAnimState = null;
        }

        #endregion

        #region 字段

        private float m_curScale = 1;
        /// <summary>上一次 FitToSlot 用的 slot 尺寸，同尺寸不重算（换表情时会跳）。</summary>
        private Vector2 m_fittedSlotSize;
        private bool m_fittedOnce;
        private GameTimer m_gameTimer;
        private Action m_clickAction;
        private SkeletonGraphic m_skeletonGraphic;
        private Spine.AnimationState m_curAnimState;
        private Skin m_view;
        private List<string> m_currentSkinNames = new List<string>();

        public bool HasValidSkeleton
        {
            get
            {
                if (m_skeletonGraphic == null || m_skeletonGraphic.SkeletonDataAsset == null ||
                    m_skeletonGraphic.SkeletonDataAsset.GetSkeletonData(true) == null)
                {
                    return false;
                }

                EnsureAnimationState();
                return m_skeletonGraphic != null && m_skeletonGraphic.Skeleton != null &&
                    m_curAnimState?.Data?.SkeletonData != null;
            }
        }

        #endregion

        #region 函数

        /// <summary>
        /// 修改 Spine 模型缩放
        /// </summary>
        /// <param name="scale">缩放比例</param>
        public void ChangeScale(float scale)
        {
            if(scale != m_curScale && m_goSpineModel != null)
            {
                m_curScale = scale;
                m_goSpineModel.transform.localScale = Vector3.one * m_curScale;
            }
        }

        /// <summary>
        /// 镜像翻转 Spine 模型
        /// </summary>
        /// <param name="isMirror">是否镜像，true=水平翻转</param>
        public void ChangeMirror(bool isMirror)
        {
            if (m_goSpineModel != null)
            {
                m_goSpineModel.transform.localScale = new Vector3((isMirror ? -1 : 1) * m_curScale, m_curScale, 1);
            }
        }

        /// <summary>
        /// 修改 Spine 模型位置
        /// </summary>
        /// <param name="pos">目标位置</param>
        public void ChangeLocalPos(Vector3 pos)
        {
            if (m_goSpineModel != null)
            {
                m_goSpineModel.transform.localPosition = pos;
            }
        }

        /// <summary>
        /// 把骨骼按原始比例缩进 slot 内，底对齐、水平居中。
        /// 同一个 slot 尺寸只算一次：换表情时骨骼包围盒会变（陈果 kaixin 位移 19%），
        /// 每句重算会让角色忽大忽小地跳。
        /// </summary>
        public void FitToSlot(Vector2 slotSize)
        {
            StretchFull(rectTransform);
            StretchFull(m_tfUISpineRoot as RectTransform);
            StretchFull(m_tfEffRoot as RectTransform);

            EnsureAnimationState();
            if (m_skeletonGraphic == null || m_skeletonGraphic.Skeleton == null ||
                slotSize.x < 1f || slotSize.y < 1f)
            {
                return;
            }

            if (m_fittedSlotSize == slotSize && m_fittedOnce)
            {
                return;
            }

            // 必须用「实际网格的包围盒」算缩放，不能用 Skeleton.GetBounds。
            // GetBounds 返回的是按 SkeletonDataAsset 的 scale（本工程 0.01）缩过的骨架空间
            // （约 13 x 22），而 SkeletonGraphic 的网格顶点是未缩的像素空间（约 1336 x 2172），
            // 两者差 100 倍 —— 拿 GetBounds 算出来的 scale 是 43 而不是 0.44，
            // 整个模型被放大一百倍飞出屏幕（症状：骨骼在播、皮肤动作都对、画面里一个人都没有）。
            m_skeletonGraphic.UpdateMesh();
            Mesh mesh = m_skeletonGraphic.GetLastMesh();
            if (mesh == null || mesh.vertexCount == 0)
            {
                return;
            }

            Bounds bounds = mesh.bounds;
            if (bounds.size.x < 0.01f || bounds.size.y < 0.01f)
            {
                return;
            }

            float scale = Mathf.Min(slotSize.x / bounds.size.x, slotSize.y / bounds.size.y);
            ChangeScale(scale);
            // localPosition 在父空间里，不受自身 localScale 影响，所以偏移量要乘 scale 换算回父空间。
            // 底对齐：包围盒底边 = slot 底边；水平居中：包围盒中心 x = slot 中心 x。
            float offsetX = -bounds.center.x * scale;
            float offsetY = -slotSize.y * 0.5f - (bounds.center.y - bounds.extents.y) * scale;
            ChangeLocalPos(new Vector3(offsetX, offsetY, 0f));
            m_fittedSlotSize = slotSize;
            m_fittedOnce = true;
            m_skeletonGraphic.raycastTarget = false;
        }

        private static void StretchFull(RectTransform rt)
        {
            if (rt == null)
            {
                return;
            }

            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            rt.localScale = Vector3.one;
            rt.localRotation = Quaternion.identity;
            rt.anchoredPosition = Vector2.zero;
        }

        /// <summary>
        /// 播放 Spine 动画
        /// </summary>
        /// <param name="animName">动画名称</param>
        /// <param name="loop">是否循环播放</param>
        /// <param name="forceReplay">是否强制重播，true=清除当前动画并从头播放</param>
        public void SetAnimation(string animName, bool loop, bool forceReplay)
        {
            CancelTimer();

            EnsureAnimationState();

            if (m_curAnimState == null || m_skeletonGraphic == null || string.IsNullOrEmpty(animName))
            {
                return;
            }
            if (forceReplay)
            {
                m_curAnimState.ClearTracks();
                m_skeletonGraphic.Skeleton?.SetToSetupPose();
            }
            bool canFindAnim = m_curAnimState.Data?.SkeletonData?.FindAnimation(animName) != null;

            if (canFindAnim)
            {
                m_curAnimState.SetAnimation(0, animName, loop);
            }
        }

        /// <summary>
        /// 延迟播放 Spine 动画
        /// </summary>
        /// <param name="animName">动画名称</param>
        /// <param name="loop">是否循环播放</param>
        /// <param name="forceReplay">是否强制重播</param>
        /// <param name="dealy">延迟时间（秒）</param>
        public void SetAnimationDelay(string animName, bool loop, bool forceReplay, float dealy = 0)
        {
            if (dealy > 0 && !string.IsNullOrEmpty(animName))
            {
                CancelTimer();

                if (GameTimer.IsNull(m_gameTimer))
                {
                    m_gameTimer = GameModule.GameTimerModule.CreateUnscaledOnceGameTimer(dealy,
                        _ => { SetAnimation(animName, loop, forceReplay); });
                }
            }
        }

        /// <summary>
        /// 获取动画时长（秒），考虑 TimeScale 影响
        /// </summary>
        /// <param name="animName">动画名称</param>
        /// <returns>动画时长，未找到或出错返回 0</returns>
        public float GetAnimationDuration(string animName)
        {
            EnsureAnimationState();

            if (m_curAnimState == null || m_skeletonGraphic == null || string.IsNullOrEmpty(animName))
            {
                return 0;
            }

            if (m_curAnimState.TimeScale <= 0)
            {
                return 0;
            }
            var anim = m_curAnimState.Data?.SkeletonData?.FindAnimation(animName);

            if (anim != null)
            {
                return anim.Duration * (1 / m_curAnimState.TimeScale);
            }

            return 0;
        }

        /// <summary>
        /// 骨架整体的点击/拖拽响应（Spine 接管角色后原来的立绘 Image 会被关掉，
        /// F2 布局编辑要靠这里继续能拖动人物）
        /// </summary>
        public void SetRaycastTarget(bool raycast)
        {
            if (m_skeletonGraphic != null)
            {
                m_skeletonGraphic.raycastTarget = raycast;
            }
        }

        /// <summary>
        /// 骨架里是否有这个皮肤。这三个角色的表情做在动作里、没有同名皮肤，
        /// 换皮前先判断一下，免得刷 "未找到皮肤" 的错误日志。
        /// </summary>
        public bool HasSkin(string skinName)
        {
            if (string.IsNullOrEmpty(skinName) || m_skeletonGraphic == null ||
                m_skeletonGraphic.SkeletonDataAsset == null)
            {
                return false;
            }

            var data = m_skeletonGraphic.SkeletonDataAsset.GetSkeletonData(true);
            return data != null && data.FindSkin(skinName) != null;
        }

        /// <summary>
        /// 直接换皮
        /// </summary>
        /// <param name="skinName"></param>
        public void SwitchSkin(string skinName)
        {
            if(m_skeletonGraphic == null || string.IsNullOrEmpty(skinName))
            {
                return;
            }
            var skin = m_skeletonGraphic.Skeleton.Data.FindSkin(skinName);
            if (skin != null)
            {
                m_skeletonGraphic.Skeleton.SetSkin(skin);
                m_skeletonGraphic.Skeleton.SetSlotsToSetupPose();
            }
            else
            {
                DLogger.Error($"未找到皮肤: {skinName}");
            }
        }

        /// <summary>
        /// 组合皮
        /// </summary>
        /// <param name="skinNames"></param>
        public void SwitchSkin(List<string> skinNames)
        {
            if(m_skeletonGraphic == null || skinNames == null || skinNames.Count <= 0)
            {
                return;
            }
            if (IsSameSkinList(skinNames))
            {
                return;
            }
            if(m_view == null)
            {
                m_view = new Skin("View");
            }
            m_view.Clear();
            m_currentSkinNames.Clear();

            for (int i = 0; i < skinNames.Count; i++)
            {
                var skinName = skinNames[i];
                if (string.IsNullOrEmpty(skinName))
                {
                    continue;
                }
                var skin = m_skeletonGraphic.Skeleton.Data.FindSkin(skinName);
                if (skin != null)
                {
                    m_view.AddSkin(skin);
                    m_currentSkinNames.Add(skinName);
                }
                else
                {
                    DLogger.Error($"未找到皮肤: {skinName}");
                }
            }
            m_skeletonGraphic.Skeleton.SetSkin(m_view);
            m_skeletonGraphic.Skeleton.SetSlotsToSetupPose();
        }

        private bool IsSameSkinList(List<string> skinNames)
        {
            if (m_currentSkinNames.Count != skinNames.Count)
            {
                return false;
            }
            var currentSet = new HashSet<string>(m_currentSkinNames);
            var targetSet = new HashSet<string>(skinNames);
            return currentSet.SetEquals(targetSet);
        }

        /// <summary>
        /// 设置 Spine 颜色
        /// </summary>
        /// <param name="color">目标颜色</param>
        public void SetSpineColor(Color color)
        {
            if (m_skeletonGraphic != null)
            {
                m_skeletonGraphic.color = color;
            }
        }

        private void CancelTimer()
        {
            GameModule.GameTimerModule.DestroyGameTimer(m_gameTimer);
            m_gameTimer = null;
        }

        private void EnsureAnimationState()
        {
            if (m_skeletonGraphic == null)
            {
                return;
            }

            if (!m_skeletonGraphic.IsValid)
            {
                if (m_skeletonGraphic.SkeletonDataAsset == null ||
                    m_skeletonGraphic.SkeletonDataAsset.GetSkeletonData(true) == null)
                {
                    return;
                }

                m_skeletonGraphic.Initialize(false);
            }

            m_curAnimState = m_skeletonGraphic.AnimationState;
        }

        /// <summary>
        /// 绑定 Spine 点击事件，自动设置点击区域和响应
        /// </summary>
        /// <param name="clickAction">点击回调</param>
        /// <param name="clickRange">点击区域大小</param>
        public void BindClickEvent(Action clickAction, Vector2 clickRange)
        {
            if (rectTransform != null)
            {
                rectTransform.sizeDelta = clickRange;
            }

            var image = transform.GetComponent<EmptyGraph>();

            if (image == null)
            {
                image = DGame.Utility.UnityUtil.AddMonoBehaviour<EmptyGraph>(gameObject);
                image.raycastTarget = true;
            }

            if (m_clickAction == null)
            {
                var button = DGame.Utility.UnityUtil.AddMonoBehaviour<UIButton>(gameObject);
                button.transition = Selectable.Transition.None;
                button.onClick.AddListener(OnSpineItemClick);
            }

            m_clickAction = clickAction;
        }

        private void OnSpineItemClick()
        {
            m_clickAction?.Invoke();
        }

        #endregion
    }
}

#endif
