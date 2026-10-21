namespace GameLogic
{
    /// <summary>
    /// 音效资源名（唯一真源）。常量值必须与
    /// GameUnity/Assets/BundleAssets/Audios/{sfx,bgm,amb}/ 下的文件名完全一致，
    /// 因为 YooAsset 的 Address 规则是 AddressByFileName，文件名就是加载地址。
    ///
    /// 替换音效时直接覆盖同名文件即可，无需改动本文件。
    /// 清单与命名规范见 docs/音效清单与替换指南.md。
    /// </summary>
    public static class SfxNames
    {
        // ---------------- UI 交互（AudioType.UISound）----------------
        /// <summary>按钮点击（按下缩放反馈）</summary>
        public const string UiClick = "sfx_ui_click";
        /// <summary>菜单项悬停高亮</summary>
        public const string UiHover = "sfx_ui_hover";
        /// <summary>窗口 / 弹窗弹入</summary>
        public const string UiPanelOpen = "sfx_ui_panel_open";
        /// <summary>弹窗弹出 / 关闭</summary>
        public const string UiPanelClose = "sfx_ui_panel_close";
        /// <summary>错误、条件不满足、被锁定</summary>
        public const string UiError = "sfx_ui_error";
        /// <summary>图鉴等页签切换</summary>
        public const string UiTabSwitch = "sfx_ui_tab_switch";
        /// <summary>滑块拖动（同时作为音量试听）</summary>
        public const string UiSliderTick = "sfx_ui_slider_tick";

        // ---------------- 叙事玩法（AudioType.Sound）----------------
        /// <summary>打字机逐字（按字间隔节流触发）</summary>
        public const string StoryText = "sfx_story_text";
        /// <summary>对话气泡出现</summary>
        public const string StoryBubbleIn = "sfx_story_bubble_in";
        /// <summary>回复选项出现</summary>
        public const string StoryOptionAppear = "sfx_story_option_appear";
        /// <summary>选中情绪</summary>
        public const string StoryEmotionSelect = "sfx_story_emotion_select";
        /// <summary>提交回复（核心操作）</summary>
        public const string StoryReplyConfirm = "sfx_story_reply_confirm";
        /// <summary>好感上升</summary>
        public const string StoryAffectionUp = "sfx_story_affection_up";
        /// <summary>好感下降</summary>
        public const string StoryAffectionDown = "sfx_story_affection_down";
        /// <summary>情绪涌现</summary>
        public const string StoryEmotionEmerge = "sfx_story_emotion_emerge";
        /// <summary>一段 Session 结束</summary>
        public const string StorySessionEnd = "sfx_story_session_end";
        /// <summary>进入日终</summary>
        public const string StoryDayEnd = "sfx_story_day_end";
        /// <summary>结局</summary>
        public const string StoryEnding = "sfx_story_ending";

        // ---------------- 记忆系统（AudioType.Sound）----------------
        /// <summary>获得记忆，飞入动画起飞</summary>
        public const string MemoryGet = "sfx_memory_get";
        /// <summary>记忆落入槽位</summary>
        public const string MemoryLand = "sfx_memory_land";
        /// <summary>删除记忆（长按满 0.7 秒、卡片碎掉的那一刻）</summary>
        public const string MemoryDelete = "sfx_memory_delete";
        /// <summary>记忆破碎：泡泡破裂的「啵」，比消散的 Puff 更短更脆（0.19s）</summary>
        public const string MemoryBreak = "sfx_memory_break";
        /// <summary>记忆容量溢出需清理</summary>
        public const string MemoryOverflow = "sfx_memory_overflow";
        /// <summary>记忆门槛锁定，回复不可选</summary>
        public const string MemoryLocked = "sfx_memory_locked";

        // ---------------- 记忆涌现过场（AudioType.Sound）----------------
        /// <summary>过场·两记忆的光点汇聚上升（0.91s 钢片琴滑音，必须"往上走"）。</summary>
        public const string EmergeRise = "sfx_emerge_rise";
        /// <summary>过场·情绪圆成形绽放（C#5+G#5 纯五度钢片琴，无三度音所以不甜腻）。</summary>
        public const string EmergeBloom = "sfx_emerge_bloom";

        // ---------------- 物件 / 场景（AudioType.Sound）----------------
        /// <summary>检查物件部件</summary>
        public const string ItemInspect = "sfx_item_inspect";
        /// <summary>物品面板滑入</summary>
        public const string ItemPanelIn = "sfx_item_panel_in";
        /// <summary>物品飞进物品栏、落袋那一记「叮」</summary>
        public const string ItemGain = "sfx_item_gain";
        /// <summary>NPC 立绘表情切换</summary>
        public const string PortraitSwitch = "sfx_portrait_switch";
        /// <summary>场景背景切换</summary>
        public const string SceneSwitch = "sfx_scene_switch";

        // ---------------- 音乐 / 环境（AudioType.Music）----------------
        /// <summary>标题页 / 主菜单</summary>
        public const string BgmTitle = "bgm_title";
        /// <summary>局内叙事</summary>
        public const string BgmStory = "bgm_story";
        /// <summary>日终独白</summary>
        public const string BgmDayEnd = "bgm_dayend";
        /// <summary>结局</summary>
        public const string BgmEnding = "bgm_ending";
        /// <summary>局内室内环境底噪（循环）</summary>
        public const string AmbStoryRoom = "amb_story_room";
    }
}
