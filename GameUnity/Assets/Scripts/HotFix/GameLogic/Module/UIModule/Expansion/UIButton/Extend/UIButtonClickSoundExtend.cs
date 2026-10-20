using GameProto;
using UnityEngine;

namespace GameLogic
{
    [System.Serializable]
    public class UIButtonClickSoundExtend
    {
        [SerializeField] private bool m_isUseClickSound = true;
        [SerializeField] private int m_clickSoundID = (int)SysSoundID.BTN_CLICK;

        public void OnPointerClick()
        {

        }

        public void OnPointerDown()
        {
            if (!m_isUseClickSound)
            {
                return;
            }

            // 默认音走资源名（配置文件缺失也能响）；prefab 上自定义过的 ID 仍按 ID 播放
            if (m_clickSoundID == (int)SysSoundID.BTN_CLICK)
            {
                SoundConfigMgr.Instance.PlaySfx(SfxNames.UiClick, DGame.AudioType.UISound);
                return;
            }

            SoundConfigMgr.Instance.Play(m_clickSoundID, DGame.AudioType.UISound, isInPool: true);
        }

        public void OnPointerUp()
        {

        }

        public void SetClickSoundID(int soundID)
        {
            m_clickSoundID = soundID;
        }
    }
}
