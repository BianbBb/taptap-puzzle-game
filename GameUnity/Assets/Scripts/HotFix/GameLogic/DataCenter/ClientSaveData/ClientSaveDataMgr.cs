using System;
using System.Collections.Generic;
using System.Reflection;
using Cysharp.Threading.Tasks;
using DGame;

namespace GameLogic
{
    public class ClientSaveDataMgr : Singleton<ClientSaveDataMgr>
    {
        private readonly Dictionary<string, BaseClientSaveData> m_saveDataDict
            = new Dictionary<string, BaseClientSaveData>();
        private readonly Dictionary<Type, ClientSaveDataAttribute> m_cacheAttributeDict
            = new Dictionary<Type, ClientSaveDataAttribute>();
        // 记录最终存储key对应的类型，用于提前发现两个SaveData类型误用同一个key。
        private readonly Dictionary<string, Type> m_storageKeyTypeDict
            = new Dictionary<string, Type>();

        /// <summary>
        /// 获取指定类型的保存数据实例
        /// </summary>
        /// <typeparam name="T">保存数据类型，必须继承自BaseClientSaveData且有无参构造函数</typeparam>
        /// <returns>保存数据实例</returns>
        public T GetSaveData<T>() where T : BaseClientSaveData, new()
        {
            ClientSaveDataAttribute attr = GetSaveDataAttribute<T>();
            string key = GetStorageKey(typeof(T), attr);
            if (!m_saveDataDict.TryGetValue(key, out var saveData))
            {
                saveData = new T();
                saveData.Init(key, attr.StorageMode);
                m_saveDataDict[key] = saveData;
            }

            return saveData as T;
        }

        /// <summary>
        /// 保存所有客户端数据到本地存储
        /// </summary>
        public void SaveAllClientData()
        {
            foreach (var saveData in m_saveDataDict.Values)
            {
                saveData.Save();
            }
        }

        /// <summary>
        /// 异步保存所有客户端数据到本地存储
        /// </summary>
        public async UniTask SaveAllClientDataAsync()
        {
            var saveDataList = new List<BaseClientSaveData>(m_saveDataDict.Values);
            foreach (var saveData in saveDataList)
            {
                await saveData.SaveAsync();
            }
        }

        private ClientSaveDataAttribute GetSaveDataAttribute<T>() where T : BaseClientSaveData, new()
        {
            var type = typeof(T);
            if (!m_cacheAttributeDict.TryGetValue(type, out var attr))
            {
                attr = type.GetCustomAttribute<ClientSaveDataAttribute>();
                if (attr == null)
                {
                    throw new DGameException($"未标记 SaveData: {type.Name}");
                }

                m_cacheAttributeDict[type] = attr;
            }

            return attr;
        }

        /// <summary>
        /// 生成最终存储key，并校验不同SaveData类型之间的key冲突。
        /// </summary>
        private string GetStorageKey(Type type, ClientSaveDataAttribute attr)
        {
            // Attribute中未显式传key时，使用类名作为默认key。
            string saveKey = string.IsNullOrWhiteSpace(attr.SaveKey) ? type.Name : attr.SaveKey;
            string storageKey;
            if (attr.PerRoleID && DataCenterSys.Instance.TryGetCurRoleID(out var roleID))
            {
                storageKey = $"saveData_{saveKey}_{roleID}";
            }
            else
            {
                storageKey = $"saveData_{saveKey}";
            }

            if (m_storageKeyTypeDict.TryGetValue(storageKey, out var cacheType) && cacheType != type)
            {
                throw new DGameException($"ClientSaveData key冲突: key={storageKey}, type={type.Name}, cacheType={cacheType.Name}");
            }

            m_storageKeyTypeDict[storageKey] = type;

            return storageKey;
        }

        protected override void OnDestroy()
        {
            m_saveDataDict.Clear();
            m_cacheAttributeDict.Clear();
            m_storageKeyTypeDict.Clear();
        }
    }
}
