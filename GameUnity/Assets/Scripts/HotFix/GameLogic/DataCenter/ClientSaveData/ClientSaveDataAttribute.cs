using System;

namespace GameLogic
{
    [AttributeUsage(AttributeTargets.Class)]
    public sealed class ClientSaveDataAttribute : Attribute
    {
        /// <summary>
        /// 保存数据的键名；为空时由ClientSaveDataMgr使用类名兜底
        /// </summary>
        public string SaveKey { get; private set; }

        /// <summary>
        /// 是否按角色ID区分存储
        /// </summary>
        public bool PerRoleID { get; private set; }

        /// <summary>
        /// 存储方式
        /// </summary>
        public ClientSaveDataStorageMode StorageMode { get; private set; }

        /// <summary>
        /// 构造客户端保存数据特性
        /// </summary>
        /// <param name="saveKey">保存数据的键名，为空时使用类名</param>
        /// <param name="perRoleID">是否按角色ID区分存储，默认为false</param>
        /// <param name="storageMode">存储方式，默认使用PlayerPrefs</param>
        public ClientSaveDataAttribute(string saveKey = "", bool perRoleID = false,
            ClientSaveDataStorageMode storageMode = ClientSaveDataStorageMode.PlayerPrefs)
        {
            // Attribute拿不到被标记类型，所以空key的类名兜底放在ClientSaveDataMgr处理。
            SaveKey = string.IsNullOrWhiteSpace(saveKey)
                ? saveKey : saveKey.Replace('/', '_').Replace('\\', '_');
            PerRoleID = perRoleID;
            StorageMode = storageMode;
        }

        /// <summary>
        /// 构造客户端保存数据特性
        /// </summary>
        /// <param name="saveKey">保存数据的键名</param>
        /// <param name="storageMode">存储方式</param>
        public ClientSaveDataAttribute(string saveKey, ClientSaveDataStorageMode storageMode)
            : this(saveKey, false, storageMode)
        {
        }
    }
}
