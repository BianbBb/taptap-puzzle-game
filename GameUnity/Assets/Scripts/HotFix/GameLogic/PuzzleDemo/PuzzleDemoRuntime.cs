using System;
using System.Collections.Generic;
using UnityEngine;

namespace GameLogic
{
    public enum PuzzleDemoEventType
    {
        StateChanged,
        InventoryChanged,
        SceneChanged,
        DemoCompleted
    }

    public sealed class PuzzleDemoEvent
    {
        public PuzzleDemoEventType Type { get; private set; }
        public string Key { get; private set; }
        public string Value { get; private set; }

        public PuzzleDemoEvent(PuzzleDemoEventType type, string key, string value)
        {
            Type = type;
            Key = key;
            Value = value;
        }
    }

    /// <summary>Small typed event bus owned by one demo session; subscriptions cannot leak between sessions.</summary>
    public sealed class PuzzleDemoEventBus
    {
        private readonly Dictionary<PuzzleDemoEventType, Action<PuzzleDemoEvent>> m_handlers =
            new Dictionary<PuzzleDemoEventType, Action<PuzzleDemoEvent>>();

        public void Subscribe(PuzzleDemoEventType type, Action<PuzzleDemoEvent> handler)
        {
            if (handler == null) return;
            Action<PuzzleDemoEvent> current;
            m_handlers.TryGetValue(type, out current);
            m_handlers[type] = current + handler;
        }

        public void Unsubscribe(PuzzleDemoEventType type, Action<PuzzleDemoEvent> handler)
        {
            Action<PuzzleDemoEvent> current;
            if (!m_handlers.TryGetValue(type, out current)) return;
            current -= handler;
            if (current == null) m_handlers.Remove(type);
            else m_handlers[type] = current;
        }

        public void Publish(PuzzleDemoEvent evt)
        {
            Action<PuzzleDemoEvent> handler;
            if (m_handlers.TryGetValue(evt.Type, out handler)) handler?.Invoke(evt);
        }
    }

    [Serializable]
    public sealed class PuzzleDemoSaveData
    {
        public bool drawerOpen;
        public bool safeCollected;
        public bool candleLit;
        public bool boxDropped;
        public bool boxMinigameSolved;
        public bool whiteGemCollected;
        public bool safeUnlocked;
        public bool blackGemCollected;
        public bool whiteGemSubmitted;
        public bool blackGemSubmitted;
        public bool completed;
        public List<string> inventory = new List<string>();
        public string selectedItem;
        public int[] sceneStack;
    }

    /// <summary>Persistent facts that drive every scene's appearance and interaction availability.</summary>
    public sealed class PuzzleDemoGameState
    {
        public const string SaveKey = "PuzzleTestDemo.State.v1";
        public bool DrawerOpen { get; private set; }
        public bool SafeCollected { get; private set; }
        public bool CandleLit { get; private set; }
        public bool BoxDropped { get; private set; }
        public bool BoxMinigameSolved { get; private set; }
        public bool WhiteGemCollected { get; private set; }
        public bool SafeUnlocked { get; private set; }
        public bool BlackGemCollected { get; private set; }
        public bool WhiteGemSubmitted { get; private set; }
        public bool BlackGemSubmitted { get; private set; }
        public bool Completed { get; private set; }

        public void Load(PuzzleDemoSaveData data)
        {
            if (data == null) return;
            DrawerOpen = data.drawerOpen;
            SafeCollected = data.safeCollected;
            CandleLit = data.candleLit;
            BoxDropped = data.boxDropped;
            BoxMinigameSolved = data.boxMinigameSolved;
            WhiteGemCollected = data.whiteGemCollected;
            SafeUnlocked = data.safeUnlocked;
            BlackGemCollected = data.blackGemCollected;
            WhiteGemSubmitted = data.whiteGemSubmitted;
            BlackGemSubmitted = data.blackGemSubmitted;
            Completed = data.completed;
        }

        public bool Set(string key, bool value)
        {
            bool before;
            switch (key)
            {
                case "drawerOpen": before = DrawerOpen; DrawerOpen = value; break;
                case "safeCollected": before = SafeCollected; SafeCollected = value; break;
                case "candleLit": before = CandleLit; CandleLit = value; break;
                case "boxDropped": before = BoxDropped; BoxDropped = value; break;
                case "boxMinigameSolved": before = BoxMinigameSolved; BoxMinigameSolved = value; break;
                case "whiteGemCollected": before = WhiteGemCollected; WhiteGemCollected = value; break;
                case "safeUnlocked": before = SafeUnlocked; SafeUnlocked = value; break;
                case "blackGemCollected": before = BlackGemCollected; BlackGemCollected = value; break;
                case "whiteGemSubmitted": before = WhiteGemSubmitted; WhiteGemSubmitted = value; break;
                case "blackGemSubmitted": before = BlackGemSubmitted; BlackGemSubmitted = value; break;
                case "completed": before = Completed; Completed = value; break;
                default: throw new ArgumentException("Unknown puzzle state: " + key, "key");
            }
            return before != value;
        }

        public PuzzleDemoSaveData ToSaveData()
        {
            return new PuzzleDemoSaveData
            {
                drawerOpen = DrawerOpen,
                safeCollected = SafeCollected,
                candleLit = CandleLit,
                boxDropped = BoxDropped,
                boxMinigameSolved = BoxMinigameSolved,
                whiteGemCollected = WhiteGemCollected,
                safeUnlocked = SafeUnlocked,
                blackGemCollected = BlackGemCollected,
                whiteGemSubmitted = WhiteGemSubmitted,
                blackGemSubmitted = BlackGemSubmitted,
                completed = Completed
            };
        }
    }

    public sealed class PuzzleDemoInventory
    {
        private readonly HashSet<string> m_items = new HashSet<string>();
        public event Action Changed;
        public string Selected { get; private set; }
        public int Count { get { return m_items.Count; } }
        public IEnumerable<string> Items { get { return m_items; } }

        public bool Contains(string item) { return m_items.Contains(item); }

        public bool Add(string item)
        {
            if (!m_items.Add(item)) return false;
            Changed?.Invoke();
            return true;
        }

        public bool Remove(string item)
        {
            if (!m_items.Remove(item)) return false;
            if (Selected == item) Selected = null;
            Changed?.Invoke();
            return true;
        }

        public void ToggleSelected(string item)
        {
            if (!m_items.Contains(item)) return;
            Selected = Selected == item ? null : item;
            Changed?.Invoke();
        }

        public void Restore(IEnumerable<string> items, string selected)
        {
            m_items.Clear();
            if (items != null) foreach (string item in items) if (!string.IsNullOrEmpty(item)) m_items.Add(item);
            Selected = !string.IsNullOrEmpty(selected) && m_items.Contains(selected) ? selected : null;
        }

        public List<string> CopyItems() { return new List<string>(m_items); }
    }

    /// <summary>Logical scene tree and return stack; game facts stay in PuzzleDemoGameState across scene changes.</summary>
    public sealed class PuzzleDemoSceneManager
    {
        public const int MainRoom = 1001;
        public const int Nightstand = 1002;
        public const int Drawer = 1003;
        public const int PictureFrame = 1004;
        public const int Wardrobe = 1005;
        public const int Box = 1006;
        public const int Shadow = 1007;
        private readonly List<int> m_stack = new List<int> { MainRoom };
        public int Current { get { return m_stack[m_stack.Count - 1]; } }
        public event Action<int, int> Changed;

        public bool CanEnter(int target, PuzzleDemoGameState state)
        {
            switch (target)
            {
                case Nightstand: return true;
                case Drawer: return state.DrawerOpen && !state.SafeCollected;
                case PictureFrame: return true;
                case Wardrobe: return true;
                case Box: return state.BoxDropped && !state.WhiteGemCollected;
                case Shadow: return state.CandleLit && !state.Completed;
                default: return false;
            }
        }

        public bool Enter(int target, PuzzleDemoGameState state)
        {
            if (!CanEnter(target, state)) return false;
            int from = Current;
            m_stack.Add(target);
            Changed?.Invoke(from, target);
            return true;
        }

        public bool Back()
        {
            if (m_stack.Count <= 1) return false;
            int from = Current;
            m_stack.RemoveAt(m_stack.Count - 1);
            Changed?.Invoke(from, Current);
            return true;
        }

        public int[] CopyStack() { return m_stack.ToArray(); }

        public void Restore(int[] stack)
        {
            m_stack.Clear();
            m_stack.Add(PuzzleDemoSceneManager.MainRoom);
            if (stack == null) return;
            for (int i = 1; i < stack.Length; i++)
            {
                int candidate = stack[i];
                if (candidate >= Nightstand && candidate <= Shadow) m_stack.Add(candidate);
            }
        }
    }

    /// <summary>Enforces selected-item, enter-child-scene, then ordinary-click priority for every hotspot.</summary>
    public static class PuzzleDemoInteractionResolver
    {
        public static bool Resolve(string selectedItem, bool selectedActionExists, bool childSceneAvailable,
            Action selectedAction, Action enterChildScene, Action ordinaryAction)
        {
            if (!string.IsNullOrEmpty(selectedItem))
            {
                if (!selectedActionExists) return false;
                selectedAction?.Invoke();
                return true;
            }
            if (childSceneAvailable)
            {
                enterChildScene?.Invoke();
                return true;
            }
            ordinaryAction?.Invoke();
            return true;
        }
    }

    /// <summary>One session object coordinates state, inventory, events, and the logical scene tree.</summary>
    public sealed class PuzzleDemoRuntime
    {
        public PuzzleDemoGameState State { get; private set; }
        public PuzzleDemoInventory Inventory { get; private set; }
        public PuzzleDemoSceneManager Scenes { get; private set; }
        public PuzzleDemoEventBus Events { get; private set; }

        public PuzzleDemoRuntime(PuzzleDemoSaveData restored)
        {
            State = new PuzzleDemoGameState();
            Inventory = new PuzzleDemoInventory();
            Scenes = new PuzzleDemoSceneManager();
            Events = new PuzzleDemoEventBus();
            State.Load(restored);
            if (restored != null)
            {
                Inventory.Restore(restored.inventory, restored.selectedItem);
                Scenes.Restore(restored.sceneStack);
            }
            Inventory.Changed += OnInventoryChanged;
            Scenes.Changed += OnSceneChanged;
        }

        public bool ChangeState(string key, bool value)
        {
            if (!State.Set(key, value)) return false;
            Events.Publish(new PuzzleDemoEvent(PuzzleDemoEventType.StateChanged, key, value.ToString()));
            Save();
            return true;
        }

        public void Save()
        {
            PuzzleDemoSaveData data = State.ToSaveData();
            data.inventory = Inventory.CopyItems();
            data.selectedItem = Inventory.Selected;
            data.sceneStack = Scenes.CopyStack();
            PlayerPrefs.SetString(PuzzleDemoGameState.SaveKey, JsonUtility.ToJson(data));
            PlayerPrefs.Save();
        }

        public static PuzzleDemoSaveData ReadSave()
        {
            if (!PlayerPrefs.HasKey(PuzzleDemoGameState.SaveKey)) return null;
            try { return JsonUtility.FromJson<PuzzleDemoSaveData>(PlayerPrefs.GetString(PuzzleDemoGameState.SaveKey)); }
            catch { return null; }
        }

        private void OnInventoryChanged()
        {
            Events.Publish(new PuzzleDemoEvent(PuzzleDemoEventType.InventoryChanged,
                "selected", Inventory.Selected ?? string.Empty));
            Save();
        }

        private void OnSceneChanged(int from, int to)
        {
            Events.Publish(new PuzzleDemoEvent(PuzzleDemoEventType.SceneChanged, from.ToString(), to.ToString()));
            Save();
        }
    }
}
