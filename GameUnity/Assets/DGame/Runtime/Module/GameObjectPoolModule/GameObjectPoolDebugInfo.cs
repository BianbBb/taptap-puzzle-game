using System.Collections.Generic;
using UnityEngine;

namespace DGame
{
    public sealed class GameObjectPoolDebugInfo
    {
        public readonly List<GameObjectPoolObjectDebugInfo> Objects = new List<GameObjectPoolObjectDebugInfo>();

        public string Location;
        public int Count;
        public int SpawnedCount;
        public int NoSpawnCount;
        public int MaxCapacity;
        public float AutoDestroyTime;
        public float IdleTime;
        public bool DontDestroy;
        public bool MarkedForDestroy;
        public bool IsDestroyed;
        public bool CanAutoDestroy;
    }

    public sealed class GameObjectPoolObjectDebugInfo
    {
        public string Name;
        public string PoolKey;
        public bool Spawned;
        public bool ActiveSelf;
        public Transform Parent;
        public GameObject GameObject;
    }
}