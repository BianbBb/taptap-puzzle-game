using System;
using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace GameLogic
{
    /// <summary>Canvas based visual room-puzzle prototype. World objects, inventory items and dials are image targets.</summary>
    public sealed class PuzzleDemoController : MonoBehaviour
    {
        private enum PuzzleArt { Nightstand, Photo, Matchbox, Candle, Wardrobe, Box, Safe, WhiteGem, BlackGem, Shadow, Magnifier, Dial }

        [SerializeField] private Texture2D roomBackground;
        [SerializeField] private Font uiFont;

        private const float CanvasWidth = 1600f;
        private const float CanvasHeight = 900f;
        private PuzzleDemoRuntime m_runtime;
        private Canvas m_canvas;
        private RectTransform m_contentRoot;
        private Sprite m_roomSprite;
        private string m_notice = "";
        private float m_noticeUntil;
        private bool m_inspectSafe;
        private readonly int[] m_code = new int[3];
        private float m_shadowHintReadyAt;
        private int m_shadowDialogueState;
        private bool m_ready;
        private Coroutine m_refreshRoutine;

        private static Sprite s_whiteSprite;
        private static Sprite s_diamondSprite;
        private static Sprite s_circleSprite;

        private PuzzleDemoGameState State { get { return m_runtime.State; } }
        private PuzzleDemoInventory Inventory { get { return m_runtime.Inventory; } }
        private PuzzleDemoSceneManager Scenes { get { return m_runtime.Scenes; } }

        private void Awake()
        {
            m_runtime = new PuzzleDemoRuntime(PuzzleDemoRuntime.ReadSave());
            m_runtime.Events.Subscribe(PuzzleDemoEventType.SceneChanged, _ => m_inspectSafe = false);
            CreateCanvas();
            m_notice = "点击卧室里的物件开始调查。";
            m_noticeUntil = Time.unscaledTime + 5f;
            m_ready = true;
            RebuildView();
        }

        private void OnDestroy()
        {
            if (m_roomSprite != null) Destroy(m_roomSprite);
        }

        private void CreateCanvas()
        {
            GameObject canvasObject = new GameObject("PuzzleDemoCanvas", typeof(RectTransform), typeof(Canvas),
                typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasObject.transform.SetParent(transform, false);
            m_canvas = canvasObject.GetComponent<Canvas>();
            // Keep the UI in the camera render so both Game View and camera-based
            // preview paths show the actual puzzle interface.
            Camera uiCamera = Camera.main;
            if (uiCamera == null) uiCamera = FindObjectOfType<Camera>();
            m_canvas.renderMode = uiCamera != null ? RenderMode.ScreenSpaceCamera : RenderMode.ScreenSpaceOverlay;
            m_canvas.worldCamera = uiCamera;
            m_canvas.planeDistance = 1f;
            m_canvas.sortingOrder = 100;
            m_canvas.pixelPerfect = false;
            CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(CanvasWidth, CanvasHeight);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = .5f;

            if (roomBackground != null)
                m_roomSprite = Sprite.Create(roomBackground, new Rect(0, 0, roomBackground.width, roomBackground.height), new Vector2(.5f, .5f));
            if (uiFont == null) uiFont = Resources.GetBuiltinResource<Font>("Arial.ttf");
            EnsureSprites();
        }

        private static void EnsureSprites()
        {
            if (s_whiteSprite == null)
            {
                Texture2D texture = new Texture2D(1, 1, TextureFormat.RGBA32, false);
                texture.hideFlags = HideFlags.HideAndDontSave;
                texture.SetPixel(0, 0, Color.white);
                texture.Apply();
                s_whiteSprite = Sprite.Create(texture, new Rect(0, 0, 1, 1), new Vector2(.5f, .5f));
                s_whiteSprite.hideFlags = HideFlags.HideAndDontSave;
            }
            if (s_diamondSprite == null) s_diamondSprite = CreateMaskSprite(true);
            if (s_circleSprite == null) s_circleSprite = CreateMaskSprite(false);
        }

        private static Sprite CreateMaskSprite(bool diamond)
        {
            const int size = 64;
            Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
            texture.hideFlags = HideFlags.HideAndDontSave;
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float dx = (x + .5f - size * .5f) / (size * .5f);
                float dy = (y + .5f - size * .5f) / (size * .5f);
                bool inside = diamond ? Mathf.Abs(dx) + Mathf.Abs(dy) <= .92f : dx * dx + dy * dy <= .84f;
                texture.SetPixel(x, y, inside ? Color.white : Color.clear);
            }
            texture.Apply();
            Sprite sprite = Sprite.Create(texture, new Rect(0, 0, size, size), new Vector2(.5f, .5f));
            sprite.hideFlags = HideFlags.HideAndDontSave;
            return sprite;
        }

        private void RefreshView()
        {
            if (!m_ready || m_canvas == null) return;
            // Disable stale controls immediately, then rebuild after the current
            // EventSystem click dispatch has completed. Rebuilding inline here
            // could destroy the Button that is still processing pointer events.
            if (m_contentRoot != null) m_contentRoot.gameObject.SetActive(false);
            if (m_refreshRoutine == null) m_refreshRoutine = StartCoroutine(RefreshViewAfterInput());
        }

        private IEnumerator RefreshViewAfterInput()
        {
            yield return null;
            m_refreshRoutine = null;
            RebuildView();
        }

        private void RebuildView()
        {
            if (!m_ready || m_canvas == null) return;
            if (m_contentRoot != null)
            {
                Destroy(m_contentRoot.gameObject);
            }

            m_contentRoot = CreateRect("PuzzleDemoView", m_canvas.transform, 0, 0, CanvasWidth, CanvasHeight);
            m_contentRoot.anchorMin = Vector2.zero;
            m_contentRoot.anchorMax = Vector2.one;
            m_contentRoot.pivot = new Vector2(.5f, .5f);
            m_contentRoot.anchoredPosition = Vector2.zero;
            m_contentRoot.sizeDelta = Vector2.zero;
            BuildBackground(m_contentRoot);

            if (State.Completed)
            {
                BuildCompletion(m_contentRoot);
                return;
            }
            if (m_inspectSafe)
            {
                BuildSafeInspection(m_contentRoot);
                return;
            }

            BuildHeader(m_contentRoot);
            if (Scenes.Current == PuzzleDemoSceneManager.MainRoom) BuildRoom(m_contentRoot);
            else BuildDetailScene(m_contentRoot);
            BuildInventory(m_contentRoot);
            if (Time.unscaledTime < m_noticeUntil) BuildNotice(m_contentRoot);
        }

        private void BuildBackground(RectTransform parent)
        {
            Image background = CreateImage("RoomBackground", parent, new Rect(0, 0, CanvasWidth, CanvasHeight),
                Color.white, m_roomSprite, true);
            background.preserveAspect = true;
            CreatePanel(parent, "HeaderShade", new Rect(0, 0, CanvasWidth, 82), new Color(.08f, .07f, .06f, .76f));
            CreatePanel(parent, "InventoryShade", new Rect(0, 760, CanvasWidth, 140), new Color(.08f, .07f, .06f, .9f));
        }

        private void BuildHeader(RectTransform parent)
        {
            CreateText(parent, "Title", "房间谜题测试 Demo", new Rect(30, 17, 390, 50), 28, Color.white, FontStyle.Bold);
            CreateText(parent, "SceneId", "场景 " + Scenes.Current + " · " + SceneName(Scenes.Current),
                new Rect(440, 18, 500, 48), 21, new Color(1f, .96f, .86f), FontStyle.Normal);
            if (Scenes.Current != PuzzleDemoSceneManager.MainRoom)
                CreateTextButton(parent, "Back", "← 返回", new Rect(1228, 15, 145, 52), () => Scenes.Back(), true);
            CreateTextButton(parent, "Reset", "重置", new Rect(1390, 15, 175, 52), ResetDemo, true);
        }

        private void BuildRoom(RectTransform parent)
        {
            // Transparent image targets align with furniture already visible in the room illustration.
            CreateRoomHotspot(parent, "BedsideTable", new Rect(955, 568, 285, 165), () => RouteToChildScene(PuzzleDemoSceneManager.Nightstand));
            CreateRoomHotspot(parent, "PhotoFrame", new Rect(1358, 300, 190, 185), () => RouteToChildScene(PuzzleDemoSceneManager.PictureFrame));
            CreateRoomHotspot(parent, "WardrobeShelf", new Rect(1398, 88, 192, 202), () => RouteToChildScene(PuzzleDemoSceneManager.Wardrobe));
            if (State.CandleLit)
            {
                ObjectImageButton(parent, "WallShadow", new Rect(1060, 170, 190, 270), PuzzleArt.Shadow,
                    () => RouteToChildScene(PuzzleDemoSceneManager.Shadow), State.WhiteGemSubmitted,
                    Inventory.Selected == "whiteGem" || Inventory.Selected == "blackGem");
            }
            CreateText(parent, "RoomHint", "点击房间中带金色边框的物件", new Rect(28, 96, 520, 38), 17, Color.white);
            CreateText(parent, "ArtNote", "", new Rect(0, 0, 1, 1), 1, Color.clear);
        }

        private void BuildDetailScene(RectTransform parent)
        {
            CreatePanel(parent, "DetailVeil", new Rect(280, 106, 1040, 620), new Color(.1f, .08f, .06f, .74f));
            switch (Scenes.Current)
            {
                case PuzzleDemoSceneManager.Nightstand: BuildNightstand(parent); break;
                case PuzzleDemoSceneManager.Drawer: BuildDrawer(parent); break;
                case PuzzleDemoSceneManager.PictureFrame: BuildPictureFrame(parent); break;
                case PuzzleDemoSceneManager.Wardrobe: BuildWardrobe(parent); break;
                case PuzzleDemoSceneManager.Box: BuildBox(parent); break;
                case PuzzleDemoSceneManager.Shadow: BuildShadow(parent); break;
            }
        }

        private void BuildNightstand(RectTransform parent)
        {
            CreateText(parent, "Hint", "点抽屉：先打开，再点一次进入", new Rect(430, 122, 740, 48), 23, Color.white);
            ObjectImageButton(parent, "Nightstand", new Rect(570, 190, 460, 430), PuzzleArt.Nightstand, () =>
            {
                bool entered = false;
                bool consumed = PuzzleDemoInteractionResolver.Resolve(Inventory.Selected, false,
                    Scenes.CanEnter(PuzzleDemoSceneManager.Drawer, State), null,
                    () => entered = Scenes.Enter(PuzzleDemoSceneManager.Drawer, State),
                    () => StateChange("drawerOpen", true, "抽屉打开了。再点一次抽屉查看里面。"));
                if (!consumed) Notice("先点背包中高亮的道具取消选择，再点抽屉。", 2.5f);
                else if (entered) Notice("进入抽屉俯视角。");
            }, State.DrawerOpen);
        }

        private void BuildDrawer(RectTransform parent)
        {
            CreateText(parent, "Hint", State.SafeCollected ? "抽屉空了" : "点击抽屉里的密码箱拾取", new Rect(430, 122, 740, 48), 23, Color.white);
            if (!State.SafeCollected)
                ObjectImageButton(parent, "SafePickup", new Rect(620, 220, 360, 360), PuzzleArt.Safe, () => RunOrdinaryObjectAction(() =>
                {
                    StateChange("safeCollected", true, "获得密码箱。可在背包点击放大镜检查。");
                    Inventory.Add("safe");
                    Scenes.Back();
                }));
        }

        private void BuildPictureFrame(RectTransform parent)
        {
            CreateText(parent, "Hint", State.CandleLit ? "烛光下，衣服上的数字线索显现" : "调查相框，找到火柴并点亮烛台", new Rect(360, 118, 880, 48), 22, Color.white);
            ObjectImageButton(parent, "Photo", new Rect(610, 190, 390, 340), PuzzleArt.Photo,
                () => Notice(State.CandleLit ? "第二人的衣服上是 2，第三人的衣服上是 7。" : "三个人穿着颜色不同的衣服。", 3f), State.CandleLit);
            if (!Inventory.Contains("match"))
                ObjectImageButton(parent, "Matches", new Rect(430, 475, 150, 145), PuzzleArt.Matchbox, () => RunOrdinaryObjectAction(() =>
                {
                    Inventory.Add("match");
                    Notice("获得火柴。选中火柴后，点击烛台。");
                }));

            bool canLight = !State.CandleLit;
            ObjectImageButton(parent, "Candle", new Rect(1040, 438, 145, 185), PuzzleArt.Candle, () =>
            {
                if (!canLight) return;
                bool used = false;
                bool consumed = PuzzleDemoInteractionResolver.Resolve(Inventory.Selected,
                    Inventory.Selected == "match", false,
                    () => { used = true; StateChange("candleLit", true, "烛台亮了，相框显出 27，墙上出现影子。"); }, null,
                    () => Notice("烛台需要火柴点燃。"));
                if (!consumed) Notice("选中的道具不能用于烛台。点一次背包图标取消选择。", 2.5f);
                else if (used) Inventory.ToggleSelected("match");
            }, State.CandleLit);
        }

        private void BuildWardrobe(RectTransform parent)
        {
            CreateText(parent, "Hint", State.BoxDropped ? "盒子掉在地上了" : "衣柜顶部似乎放着什么", new Rect(430, 122, 740, 48), 23, Color.white);
            if (!State.BoxDropped)
                ObjectImageButton(parent, "Wardrobe", new Rect(620, 180, 360, 420), PuzzleArt.Wardrobe,
                    () => RunOrdinaryObjectAction(() => StateChange("boxDropped", true, "盒子掉到了地上。再点盒子查看。")));
            else
                ObjectImageButton(parent, "DroppedBox", new Rect(650, 230, 300, 300), PuzzleArt.Box,
                    () => ResolveChildSceneClick(PuzzleDemoSceneManager.Box));
        }

        private void BuildBox(RectTransform parent)
        {
            CreateText(parent, "Hint", State.BoxMinigameSolved ? "盒子打开了" : "点盒盖完成占位小游戏", new Rect(430, 122, 740, 48), 23, Color.white);
            if (!State.BoxMinigameSolved)
                ObjectImageButton(parent, "BoxGame", new Rect(630, 190, 340, 390), PuzzleArt.Box,
                    () => StateChange("boxMinigameSolved", true, "盒子打开了，里面有一颗白宝石。"), true);
            else if (!State.WhiteGemCollected)
                ObjectImageButton(parent, "WhiteGemPickup", new Rect(710, 255, 180, 180), PuzzleArt.WhiteGem,
                    () => RunOrdinaryObjectAction(() =>
                    {
                        StateChange("whiteGemCollected", true, "获得白宝石。点亮烛台后，将它交给影子。");
                        Inventory.Add("whiteGem");
                        Scenes.Back();
                    }));
        }

        private void BuildShadow(RectTransform parent)
        {
            string dialogue = !State.WhiteGemSubmitted
                ? (m_shadowDialogueState == 0 ? "影子：密码是……" : "影子等待你递出东西。")
                : (Time.unscaledTime < m_shadowHintReadyAt ? "影子：" : "影子：密码是 8。");
            CreateText(parent, "Dialogue", dialogue, new Rect(505, 175, 590, 100), 28, Color.white, FontStyle.Bold);
            ObjectImageButton(parent, "Shadow", new Rect(690, 280, 230, 320), PuzzleArt.Shadow, ClickShadow,
                State.WhiteGemSubmitted, Inventory.Selected == "whiteGem" || Inventory.Selected == "blackGem");
            if (State.BlackGemSubmitted)
                CreateText(parent, "Thanks", "影子：谢谢你。", new Rect(920, 390, 330, 64), 25, Color.white);
        }

        private void BuildInventory(RectTransform parent)
        {
            CreateText(parent, "InventoryTitle", "背包", new Rect(28, 782, 100, 40), 21, Color.white, FontStyle.Bold);
            float x = 138;
            foreach (string item in Inventory.CopyItems())
            {
                Rect slot = new Rect(x, 777, 104, 92);
                Action inventoryAction = item == "safe" ? (Action)InspectSafe : () => Inventory.ToggleSelected(item);
                ObjectImageButton(parent, "Inventory_" + item, slot, ItemArt(item), inventoryAction,
                    false, Inventory.Selected == item);
                CreateText(parent, "InventoryLabel_" + item, ItemName(item), new Rect(x - 4, 866, 112, 24), 13, Color.white);
                if (item == "safe")
                    ObjectImageButton(parent, "InspectSafe", new Rect(x + 62, 802, 42, 42), PuzzleArt.Magnifier, InspectSafe);
                x += 126;
            }
            if (Inventory.Count > 1)
                CreateTextButton(parent, "NextItem", "›", new Rect(1470, 800, 82, 54), SelectNext, true);
        }

        private void BuildSafeInspection(RectTransform parent)
        {
            CreatePanel(parent, "ModalShade", new Rect(0, 0, CanvasWidth, CanvasHeight), new Color(.03f, .025f, .02f, .82f));
            CreatePanel(parent, "SafePanel", new Rect(395, 116, 810, 640), new Color(.92f, .88f, .79f, .98f));
            CreateText(parent, "SafeTitle", "密码箱", new Rect(495, 142, 550, 56), 30, new Color(.18f, .15f, .11f), FontStyle.Bold);
            CreateTextButton(parent, "CloseSafe", "×", new Rect(1080, 130, 76, 58), () => m_inspectSafe = false, false);

            if (State.BlackGemCollected)
            {
                CreateText(parent, "EmptySafe", "箱内已经空了", new Rect(505, 250, 590, 48), 22, new Color(.2f, .18f, .14f));
                ObjectImageButton(parent, "SafeEmpty", new Rect(670, 318, 250, 250), PuzzleArt.Safe, null);
                return;
            }
            if (State.SafeUnlocked)
            {
                CreateText(parent, "SafeOpenHint", "密码正确，点击箱内黑宝石拾取", new Rect(500, 230, 600, 48), 22, new Color(.2f, .18f, .14f));
                ObjectImageButton(parent, "BlackGemPickup", new Rect(705, 300, 190, 190), PuzzleArt.BlackGem, () =>
                {
                    StateChange("blackGemCollected", true, "获得黑宝石。点亮烛台后，带回影子处提交。");
                    Inventory.Add("blackGem");
                    m_inspectSafe = false;
                });
                CreateTextButton(parent, "CloseSafeLater", "稍后再取", new Rect(650, 580, 300, 62), () => m_inspectSafe = false, false);
                return;
            }

            CreateText(parent, "DialHint", "点每个数字拨轮，依次输入线索组合", new Rect(500, 220, 600, 54), 20, new Color(.2f, .18f, .14f));
            for (int i = 0; i < 3; i++)
            {
                int index = i;
                ObjectImageButton(parent, "Dial_" + i, new Rect(520 + i * 190, 305, 155, 190), PuzzleArt.Dial, () =>
                    m_code[index] = (m_code[index] + 1) % 10, false);
                CreateText(parent, "Digit_" + i, m_code[index].ToString(), new Rect(538 + i * 190, 318, 120, 60), 48,
                    Color.white, FontStyle.Bold, TextAnchor.MiddleCenter);
                CreateText(parent, "DialArrow_" + i, "▼", new Rect(538 + i * 190, 410, 120, 38), 20,
                    new Color(.7f, .77f, .79f), FontStyle.Bold, TextAnchor.MiddleCenter);
            }
            CreateTextButton(parent, "Unlock", "确认", new Rect(620, 545, 360, 70), () =>
            {
                if (m_code[0] == 8 && m_code[1] == 2 && m_code[2] == 7)
                    StateChange("safeUnlocked", true, "密码箱打开了！线索 27 + 8 = 827。");
                else Notice("密码不正确。组合相框的 27 和影子给出的数字。", 3f);
            }, true);
            CreateTextButton(parent, "CloseSafe", "关闭", new Rect(650, 635, 300, 54), () => m_inspectSafe = false, false);
        }

        private void BuildCompletion(RectTransform parent)
        {
            CreatePanel(parent, "CompletionShade", new Rect(0, 0, CanvasWidth, CanvasHeight), new Color(.03f, .025f, .02f, .84f));
            CreatePanel(parent, "CompletionPanel", new Rect(380, 235, 840, 410), new Color(.95f, .91f, .82f, .98f));
            CreateText(parent, "CompleteTitle", "测试 Demo 完成", new Rect(485, 285, 630, 70), 34, new Color(.18f, .15f, .11f), FontStyle.Bold);
            CreateText(parent, "CompleteSummary", "线索推理、道具交互与宝石提交流程完成。", new Rect(485, 380, 630, 54), 22, new Color(.25f, .21f, .15f));
            CreateTextButton(parent, "Restart", "重新开始", new Rect(605, 490, 390, 72), ResetDemo, true);
        }

        private void BuildNotice(RectTransform parent)
        {
            CreatePanel(parent, "NoticeBg", new Rect(320, 705, 960, 48), new Color(.08f, .07f, .05f, .9f));
            CreateText(parent, "NoticeText", m_notice, new Rect(340, 708, 920, 40), 17, new Color(1f, .96f, .84f));
        }

        private void CreateRoomHotspot(RectTransform parent, string name, Rect rect, Action action)
        {
            GameObject go = NewRect(name, parent, rect);
            Image image = go.AddComponent<Image>();
            image.sprite = WhiteSprite;
            image.color = new Color(1f, .72f, .18f, .01f);
            Button button = go.AddComponent<Button>();
            button.targetGraphic = image;
            button.transition = Selectable.Transition.ColorTint;
            button.colors = CreateButtonColors(new Color(1f, .72f, .18f, .01f), new Color(1f, .72f, .18f, .24f), new Color(1f, .82f, .3f, .36f));
            button.onClick.AddListener(() => { action?.Invoke(); RefreshView(); });
            AddCornerGuide(go.transform, rect.width, rect.height);
        }

        private void ObjectImageButton(RectTransform parent, string name, Rect rect, PuzzleArt art, Action action,
            bool activeState = false, bool selected = false)
        {
            GameObject go = NewRect(name, parent, rect);
            Image target = go.AddComponent<Image>();
            target.sprite = WhiteSprite;
            target.color = new Color(1f, 1f, 1f, .015f);
            Button button = go.AddComponent<Button>();
            button.targetGraphic = target;
            button.transition = Selectable.Transition.ColorTint;
            button.colors = CreateButtonColors(target.color, new Color(1f, .8f, .32f, .18f), new Color(1f, .76f, .23f, .32f));
            if (action != null) button.onClick.AddListener(() => { action(); RefreshView(); });
            DrawObjectArt(go.transform, rect.width, rect.height, art, activeState);
            if (selected) AddOutline(go.transform, rect.width, rect.height, new Color(1f, .72f, .18f, .98f), 5f, 0f, 0f, 1f, 1f);
        }

        private void DrawObjectArt(Transform parent, float width, float height, PuzzleArt art, bool activeState)
        {
            Color wood = new Color(.45f, .25f, .13f);
            Color lightWood = new Color(.73f, .48f, .27f);
            Color dark = new Color(.17f, .16f, .17f);
            switch (art)
            {
                case PuzzleArt.Nightstand:
                    Shape(parent, "Top", new Rect(width * .06f, height * .08f, width * .88f, height * .14f), lightWood);
                    Shape(parent, "Body", new Rect(width * .12f, height * .22f, width * .76f, height * .62f), wood);
                    Shape(parent, "Drawer", new Rect(width * .18f, height * (activeState ? .32f : .31f), width * .64f, height * .21f), activeState ? dark : lightWood);
                    Shape(parent, "BottomDrawer", new Rect(width * .18f, height * .59f, width * .64f, height * .17f), lightWood);
                    Shape(parent, "Knob", new Rect(width * .46f, height * .4f, width * .08f, height * .07f), new Color(.98f, .85f, .52f), CircleSprite);
                    Shape(parent, "LegL", new Rect(width * .2f, height * .82f, width * .1f, height * .14f), dark);
                    Shape(parent, "LegR", new Rect(width * .7f, height * .82f, width * .1f, height * .14f), dark);
                    break;
                case PuzzleArt.Photo:
                    Shape(parent, "Frame", new Rect(width * .04f, height * .03f, width * .92f, height * .92f), new Color(.28f, .17f, .1f));
                    Shape(parent, "Mat", new Rect(width * .12f, height * .11f, width * .76f, height * .75f), new Color(.91f, .84f, .68f));
                    for (int i = 0; i < 3; i++)
                    {
                        float x = width * (.24f + .25f * i);
                        Shape(parent, "Head" + i, new Rect(x, height * .22f, width * .13f, height * .18f), new Color(.89f, .66f, .49f), CircleSprite);
                        Shape(parent, "Shirt" + i, new Rect(x - width * .035f, height * .43f, width * .2f, height * .32f),
                            activeState && i > 0 ? new Color(.87f, .35f, .2f) : new Color(.34f, .47f, .56f));
                        if (activeState && i > 0)
                            CreateText(parent, "HiddenDigit" + i, i == 1 ? "2" : "7",
                                new Rect(x - width * .01f, height * .48f, width * .16f, height * .22f), 28, Color.white, FontStyle.Bold, TextAnchor.MiddleCenter);
                    }
                    break;
                case PuzzleArt.Matchbox:
                    Shape(parent, "Box", new Rect(width * .1f, height * .2f, width * .8f, height * .62f), new Color(.7f, .18f, .12f));
                    AddOutline(parent, width, height, new Color(.93f, .71f, .4f), 3f, .1f, .2f, .8f, .62f);
                    for (int i = 0; i < 3; i++)
                    {
                        float y = height * (.32f + i * .15f);
                        Shape(parent, "Stick" + i, new Rect(width * .26f, y, width * .56f, height * .035f), new Color(.93f, .78f, .54f));
                        Shape(parent, "Head" + i, new Rect(width * .16f, y - height * .02f, width * .13f, height * .075f), new Color(.86f, .24f, .14f), CircleSprite);
                    }
                    break;
                case PuzzleArt.Candle:
                    Shape(parent, "Holder", new Rect(width * .2f, height * .78f, width * .6f, height * .1f), wood);
                    Shape(parent, "Wax", new Rect(width * .4f, height * .28f, width * .2f, height * .5f), new Color(.94f, .85f, .65f));
                    Shape(parent, "Wick", new Rect(width * .485f, height * .2f, width * .03f, height * .11f), dark);
                    if (activeState) Shape(parent, "Flame", new Rect(width * .38f, height * .02f, width * .24f, height * .24f), new Color(1f, .67f, .12f), DiamondSprite);
                    break;
                case PuzzleArt.Wardrobe:
                    Shape(parent, "Body", new Rect(width * .1f, height * .06f, width * .8f, height * .88f), wood);
                    AddOutline(parent, width, height, lightWood, 6f, .1f, .06f, .8f, .88f);
                    Shape(parent, "Center", new Rect(width * .49f, height * .12f, width * .02f, height * .75f), lightWood);
                    Shape(parent, "HandleL", new Rect(width * .42f, height * .48f, width * .06f, height * .07f), new Color(.96f, .8f, .45f), CircleSprite);
                    Shape(parent, "HandleR", new Rect(width * .56f, height * .48f, width * .06f, height * .07f), new Color(.96f, .8f, .45f), CircleSprite);
                    break;
                case PuzzleArt.Box:
                    Shape(parent, "Base", new Rect(width * .13f, height * .31f, width * .74f, height * .56f), lightWood);
                    Shape(parent, "Lid", new Rect(width * .08f, height * .16f, width * .84f, height * .24f), wood);
                    if (activeState) Shape(parent, "Inside", new Rect(width * .2f, height * .54f, width * .6f, height * .28f), new Color(.19f, .17f, .14f));
                    AddOutline(parent, width, height, new Color(.38f, .22f, .12f), 5f, .13f, .31f, .74f, .56f);
                    break;
                case PuzzleArt.Safe:
                    Shape(parent, "Case", new Rect(width * .06f, height * .05f, width * .88f, height * .9f), new Color(.2f, .24f, .28f));
                    AddOutline(parent, width, height, new Color(.6f, .66f, .7f), 6f, .06f, .05f, .88f, .9f);
                    Shape(parent, "Display", new Rect(width * .18f, height * .15f, width * .64f, height * .2f), new Color(.1f, .12f, .14f));
                    Shape(parent, "DisplayLit", new Rect(width * .27f, height * .2f, width * .46f, height * .1f), new Color(.49f, .8f, .9f));
                    Shape(parent, "Lock", new Rect(width * .37f, height * .45f, width * .26f, height * .26f), new Color(.73f, .75f, .76f), CircleSprite);
                    Shape(parent, "LockHole", new Rect(width * .455f, height * .535f, width * .09f, height * .09f), dark, CircleSprite);
                    break;
                case PuzzleArt.Dial:
                    Shape(parent, "DialBody", new Rect(width * .04f, height * .03f, width * .92f, height * .94f), new Color(.27f, .32f, .36f));
                    AddOutline(parent, width, height, new Color(.58f, .64f, .68f), 5f, .04f, .03f, .92f, .94f);
                    Shape(parent, "DialScreen", new Rect(width * .12f, height * .1f, width * .76f, height * .3f), new Color(.08f, .12f, .15f));
                    Shape(parent, "DialScreenGlow", new Rect(width * .18f, height * .15f, width * .64f, height * .2f), new Color(.16f, .68f, .78f));
                    Shape(parent, "DialKnob", new Rect(width * .34f, height * .53f, width * .32f, height * .25f), new Color(.68f, .73f, .75f), CircleSprite);
                    Shape(parent, "DialKnobCenter", new Rect(width * .43f, height * .59f, width * .14f, height * .13f), new Color(.34f, .39f, .42f), CircleSprite);
                    break;
                case PuzzleArt.WhiteGem:
                    Shape(parent, "Gem", new Rect(width * .12f, height * .08f, width * .76f, height * .82f), new Color(.86f, .98f, 1f), DiamondSprite);
                    AddOutline(parent, width, height, new Color(.47f, .84f, .97f), 4f, .12f, .08f, .76f, .82f);
                    break;
                case PuzzleArt.BlackGem:
                    Shape(parent, "Gem", new Rect(width * .12f, height * .08f, width * .76f, height * .82f), new Color(.1f, .13f, .2f), DiamondSprite);
                    AddOutline(parent, width, height, new Color(.55f, .5f, .72f), 4f, .12f, .08f, .76f, .82f);
                    break;
                case PuzzleArt.Shadow:
                    Shape(parent, "Head", new Rect(width * .35f, height * .04f, width * .3f, height * .24f), new Color(.1f, .1f, .14f, .94f), CircleSprite);
                    Shape(parent, "Body", new Rect(width * .22f, height * .27f, width * .56f, height * .69f), new Color(.1f, .1f, .14f, .94f), CircleSprite);
                    break;
                case PuzzleArt.Magnifier:
                    Shape(parent, "Lens", new Rect(width * .08f, height * .08f, width * .65f, height * .65f), new Color(.9f, .92f, .83f), CircleSprite);
                    Shape(parent, "LensInner", new Rect(width * .2f, height * .2f, width * .42f, height * .42f), new Color(.28f, .48f, .57f), CircleSprite);
                    Shape(parent, "Handle", new Rect(width * .62f, height * .62f, width * .3f, height * .16f), new Color(.9f, .92f, .83f));
                    break;
            }
        }

        private void AddCornerGuide(Transform parent, float width, float height)
        {
            Color color = new Color(1f, .83f, .38f, .52f);
            float thickness = 3f;
            float length = Mathf.Min(25f, Mathf.Min(width, height) * .22f);
            Shape(parent, "GuideTLH", new Rect(0, 0, length, thickness), color);
            Shape(parent, "GuideTLV", new Rect(0, 0, thickness, length), color);
            Shape(parent, "GuideTRH", new Rect(width - length, 0, length, thickness), color);
            Shape(parent, "GuideTRV", new Rect(width - thickness, 0, thickness, length), color);
            Shape(parent, "GuideBLH", new Rect(0, height - thickness, length, thickness), color);
            Shape(parent, "GuideBLV", new Rect(0, height - length, thickness, length), color);
            Shape(parent, "GuideBRH", new Rect(width - length, height - thickness, length, thickness), color);
            Shape(parent, "GuideBRV", new Rect(width - thickness, height - length, thickness, length), color);
        }

        private void AddOutline(Transform parent, float width, float height, Color color, float thickness,
            float x, float y, float w, float h)
        {
            float px = width * x, py = height * y, pw = width * w, ph = height * h;
            Shape(parent, "OutlineTop", new Rect(px, py, pw, thickness), color);
            Shape(parent, "OutlineBottom", new Rect(px, py + ph - thickness, pw, thickness), color);
            Shape(parent, "OutlineLeft", new Rect(px, py, thickness, ph), color);
            Shape(parent, "OutlineRight", new Rect(px + pw - thickness, py, thickness, ph), color);
        }

        private void CreateTextButton(RectTransform parent, string name, string label, Rect rect, Action action, bool highlight)
        {
            GameObject go = NewRect(name, parent, rect);
            Image image = go.AddComponent<Image>();
            image.sprite = WhiteSprite;
            image.color = highlight ? new Color(.78f, .56f, .27f, .96f) : new Color(.94f, .9f, .8f, .96f);
            Button button = go.AddComponent<Button>();
            button.targetGraphic = image;
            button.transition = Selectable.Transition.ColorTint;
            button.colors = CreateButtonColors(image.color, new Color(1f, .78f, .36f, 1f), new Color(.82f, .57f, .24f, 1f));
            if (action != null) button.onClick.AddListener(() => { action(); RefreshView(); });
            CreateText(go.transform, "Label", label, new Rect(4, 3, rect.width - 8, rect.height - 6),
                19, highlight ? Color.white : new Color(.18f, .15f, .11f), FontStyle.Bold, TextAnchor.MiddleCenter);
        }

        private void CreatePanel(Transform parent, string name, Rect rect, Color color)
        {
            CreateImage(name, parent, rect, color, WhiteSprite, false);
        }

        private Image CreateImage(string name, Transform parent, Rect rect, Color color, Sprite sprite, bool raycastTarget)
        {
            GameObject go = NewRect(name, parent, rect);
            Image image = go.AddComponent<Image>();
            image.sprite = sprite != null ? sprite : WhiteSprite;
            image.color = color;
            image.raycastTarget = raycastTarget;
            return image;
        }

        private void Shape(Transform parent, string name, Rect rect, Color color, Sprite sprite = null)
        {
            Image image = CreateImage(name, parent, rect, color, sprite != null ? sprite : WhiteSprite, false);
            image.preserveAspect = sprite != null;
        }

        private Text CreateText(Transform parent, string name, string value, Rect rect, int size, Color color,
            FontStyle style = FontStyle.Normal, TextAnchor alignment = TextAnchor.MiddleLeft)
        {
            GameObject go = NewRect(name, parent, rect);
            Text text = go.AddComponent<Text>();
            text.font = uiFont;
            text.fontSize = size;
            text.fontStyle = style;
            text.alignment = alignment;
            text.color = color;
            text.text = value;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Truncate;
            text.raycastTarget = false;
            return text;
        }

        private static GameObject NewRect(string name, Transform parent, Rect rect)
        {
            GameObject go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            RectTransform rt = go.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0, 1);
            rt.anchorMax = new Vector2(0, 1);
            rt.pivot = new Vector2(0, 1);
            rt.anchoredPosition = new Vector2(rect.x, -rect.y);
            rt.sizeDelta = new Vector2(rect.width, rect.height);
            return go;
        }

        private static RectTransform CreateRect(string name, Transform parent, float x, float y, float width, float height)
        {
            return NewRect(name, parent, new Rect(x, y, width, height)).GetComponent<RectTransform>();
        }

        private static ColorBlock CreateButtonColors(Color normal, Color highlighted, Color pressed)
        {
            ColorBlock colors = ColorBlock.defaultColorBlock;
            colors.normalColor = normal;
            colors.highlightedColor = highlighted;
            colors.pressedColor = pressed;
            colors.selectedColor = highlighted;
            colors.disabledColor = new Color(normal.r, normal.g, normal.b, .5f);
            colors.colorMultiplier = 1f;
            colors.fadeDuration = .08f;
            return colors;
        }

        private Sprite WhiteSprite { get { return s_whiteSprite; } }
        private Sprite DiamondSprite { get { return s_diamondSprite; } }
        private Sprite CircleSprite { get { return s_circleSprite; } }

        private void RouteToChildScene(int id)
        {
            bool consumed = PuzzleDemoInteractionResolver.Resolve(Inventory.Selected, false,
                Scenes.CanEnter(id, State), null, () => Scenes.Enter(id, State),
                () => Notice("这个入口当前不可进入。", 2f));
            if (!consumed) Notice("选中的道具不能用于这个物件。再次点背包图标可取消选择。", 2.5f);
        }

        private void ResolveChildSceneClick(int id)
        {
            bool consumed = PuzzleDemoInteractionResolver.Resolve(Inventory.Selected, false,
                Scenes.CanEnter(id, State), null, () => Scenes.Enter(id, State),
                () => Notice("这里暂时没有可进入的子场景。", 2f));
            if (!consumed) Notice("选中的道具不能用于这个物件。再次点背包图标可取消选择。", 2.5f);
        }

        private bool RunOrdinaryObjectAction(Action ordinaryAction)
        {
            bool consumed = PuzzleDemoInteractionResolver.Resolve(Inventory.Selected, false, false,
                null, null, ordinaryAction);
            if (!consumed) Notice("选中的道具不能用于这个物件。再次点背包图标可取消选择。", 2.5f);
            return consumed;
        }

        private void ClickShadow()
        {
            if (!State.WhiteGemSubmitted)
            {
                bool submitted = false;
                bool consumed = PuzzleDemoInteractionResolver.Resolve(Inventory.Selected,
                    Inventory.Selected == "whiteGem", false, () => submitted = true, null,
                    () => m_shadowDialogueState = 1);
                if (!consumed)
                {
                    Notice("先选择白宝石，再点击影子；再次点击高亮图标可取消选择。", 2.5f);
                    return;
                }
                if (submitted)
                {
                    Inventory.Remove("whiteGem");
                    StateChange("whiteGemSubmitted", true, "影子停顿了一秒，留下数字 8。和相框的 27 合起来是 827。");
                    m_shadowHintReadyAt = Time.unscaledTime + 1f;
                    m_shadowDialogueState = 2;
                }
                return;
            }
            if (State.BlackGemCollected && !State.BlackGemSubmitted)
            {
                bool consumed = PuzzleDemoInteractionResolver.Resolve(Inventory.Selected,
                    Inventory.Selected == "blackGem", false, () =>
                    {
                        Inventory.Remove("blackGem");
                        StateChange("blackGemSubmitted", true, "黑宝石归位。谜题 Demo 完成。");
                        StateChange("completed", true, "完成");
                        m_runtime.Events.Publish(new PuzzleDemoEvent(PuzzleDemoEventType.DemoCompleted, "ending", "complete"));
                    }, null, () => Notice("影子正在等待黑宝石。"));
                if (!consumed) Notice("选择黑宝石后，再点影子图像提交。", 2.5f);
            }
            else Notice("影子：密码是 8。", 2f);
        }

        private void StateChange(string key, bool value, string notice)
        {
            if (m_runtime.ChangeState(key, value)) Notice(notice);
        }

        private void InspectSafe()
        {
            if (State.SafeCollected && Inventory.Contains("safe")) m_inspectSafe = true;
        }

        private void SelectNext()
        {
            string[] items = Inventory.CopyItems().ToArray();
            if (items.Length == 0) return;
            int index = Array.IndexOf(items, Inventory.Selected);
            Inventory.ToggleSelected(items[(index + 1 + items.Length) % items.Length]);
        }

        private void ResetDemo()
        {
            PlayerPrefs.DeleteKey(PuzzleDemoGameState.SaveKey);
            PlayerPrefs.Save();
            m_runtime = new PuzzleDemoRuntime(null);
            m_code[0] = m_code[1] = m_code[2] = 0;
            m_inspectSafe = false;
            m_shadowDialogueState = 0;
            Notice("Demo 已重置。", 2f);
        }

        private void Notice(string text, float duration = 4f)
        {
            m_notice = text;
            m_noticeUntil = Time.unscaledTime + duration;
        }

        private PuzzleArt ItemArt(string id)
        {
            switch (id)
            {
                case "match": return PuzzleArt.Matchbox;
                case "safe": return PuzzleArt.Safe;
                case "whiteGem": return PuzzleArt.WhiteGem;
                case "blackGem": return PuzzleArt.BlackGem;
                default: return PuzzleArt.Box;
            }
        }

        private static string ItemName(string id)
        {
            switch (id)
            {
                case "match": return "火柴";
                case "safe": return "密码箱";
                case "whiteGem": return "白宝石";
                case "blackGem": return "黑宝石";
                default: return id;
            }
        }

        private static string SceneName(int id)
        {
            switch (id)
            {
                case PuzzleDemoSceneManager.MainRoom: return "卧室主场景";
                case PuzzleDemoSceneManager.Nightstand: return "床头柜";
                case PuzzleDemoSceneManager.Drawer: return "抽屉俯视角";
                case PuzzleDemoSceneManager.PictureFrame: return "相框";
                case PuzzleDemoSceneManager.Wardrobe: return "衣柜 / 盒子";
                case PuzzleDemoSceneManager.Box: return "盒子俯视角";
                case PuzzleDemoSceneManager.Shadow: return "影子";
                default: return "未知场景";
            }
        }
    }
}



