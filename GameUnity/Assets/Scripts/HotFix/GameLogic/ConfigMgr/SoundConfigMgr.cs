using System.Collections.Generic;
using DGame;
using GameProto;
using UnityEngine;

namespace GameLogic
{
    public class SoundConfigMgr : Singleton<SoundConfigMgr>
    {
        private sealed class SoundRandomData { public string[] Locations; public float[] Weights; }
        private readonly Dictionary<int, SoundRandomData> m_soundRandomDataCache = new Dictionary<int, SoundRandomData>();

        /// <summary>Location（= 音频文件名）→ 配置行。首次使用时构建。</summary>
        private Dictionary<string, SoundConfig> m_configByLocation;

        /// <summary>已经告警过的缺失配置，避免每帧刷屏。</summary>
        private readonly HashSet<string> m_warnedMissingConfig = new HashSet<string>();

        /// <summary>当前正在播放的 BGM 资源名，用于避免同曲重播。</summary>
        private string m_currentBgm;

        public Dictionary<int, SoundConfig> DataMap => TbSoundConfig.DataMap;
        public List<SoundConfig> DataLis => TbSoundConfig.DataList;
        public bool TryGetValue(int soundId, out SoundConfig cfg) => TbSoundConfig.TryGetValue(soundId, out cfg);
        public SoundConfig GetOrDefault(int soundId) => TbSoundConfig.GetOrDefault(soundId);
        public bool ContainsKey(int soundId) => TbSoundConfig.ContainsKey(soundId);

        public DGame.AudioSourceAgent Play(int soundId, DGame.AudioType audioType, bool isAsync = true, bool isInPool = true, Vector3 position = default)
        {
            if (!TryGetValue(soundId, out var cfg) || string.IsNullOrEmpty(cfg.Location)) return null;
            var soundRandomData = GetSoundRandomData(soundId, cfg.Location);
            var options = DGame.AudioPlayOptions.CreateDefault();
            options.SoundId = cfg.ID;
            options.PlaybackMode = (DGame.AudioPlaybackMode)cfg.PlayMode;
            options.UsePlaybackMode = true;
            options.VolumeRange = new Vector2(cfg.VolumeMin, cfg.VolumeMax);
            options.PitchRange = new Vector2(cfg.PitchMin, cfg.PitchMax);
            options.FadeInTime = cfg.FadeInTime;
            options.FadeOutTime = cfg.FadeOutTime;
            options.EndPauseTime = cfg.EndPauseTime;
            options.SpatialBlend = cfg.SpatialBlend;
            options.MinDistance = cfg.MinDistance;
            options.MaxDistance = cfg.MaxDistance;
            options.Priority = cfg.Priority;
            options.MaxPlayCount = cfg.MaxPlayCount;
            options.Position = position;
            options.UseRandomSelection = soundRandomData.Locations.Length > 1 || soundRandomData.Locations[0] != cfg.Location;
            options.RandomLocations = soundRandomData.Locations;
            options.RandomWeights = soundRandomData.Weights;
            return GameModule.AudioModule.Play(audioType, SelectSoundRandomLocation(soundRandomData), options, isAsync, isInPool);
        }

        /// <summary>
        /// 按资源名（= 文件名）播放短音效。音效配置表里有对应 Location 就用表里的参数，
        /// 没有就用默认参数兜底播放，只告警一次。这样替换/新增音效不需要先导表。
        /// </summary>
        public AudioSourceAgent PlaySfx(string location, DGame.AudioType audioType = DGame.AudioType.Sound,
            float volume = 1f, float pitch = 1f)
        {
            if (string.IsNullOrEmpty(location)) return null;

            SoundConfig cfg = FindByLocation(location);
            if (cfg != null)
            {
                return Play(cfg.ID, audioType, isAsync: true, isInPool: true);
            }

            WarnMissingConfig(location);
            var options = AudioPlayOptions.CreateDefault();
            options.Volume = volume;
            options.PitchRange = new Vector2(pitch, pitch);
            options.FadeInTime = 0f;
            options.FadeOutTime = 0.15f;
            return GameModule.AudioModule.Play(audioType, location, options, true, true);
        }

        /// <summary>
        /// 播放循环 BGM。同一首不重复触发；Music 分组只有 1 个代理，
        /// 换曲时旧曲会先淡出（优先级设为 100，高于默认 128 才能抢占该代理）。
        /// </summary>
        public AudioSourceAgent PlayBgm(string location, float fadeIn = 1f, float fadeOut = 1.2f)
        {
            if (string.IsNullOrEmpty(location)) return null;
            if (m_currentBgm == location) return null;

            m_currentBgm = location;
            var options = AudioPlayOptions.CreateDefault();
            options.PlaybackMode = AudioPlaybackMode.Loop;
            options.UsePlaybackMode = true;
            options.IsLoop = true;
            options.Priority = 100;
            options.FadeInTime = Mathf.Max(0f, fadeIn);
            options.FadeOutTime = Mathf.Max(0f, fadeOut);
            return GameModule.AudioModule.Play(DGame.AudioType.Music, location, options, true, true);
        }

        public void StopBgm(bool fadeOut = true)
        {
            m_currentBgm = null;
            GameModule.AudioModule.Stop(DGame.AudioType.Music, fadeOut);
        }

        /// <summary>把高频音效预热进池，避免首次播放的异步加载延迟。</summary>
        public void Preload(params string[] locations)
        {
            if (locations == null || locations.Length == 0) return;
            var list = new List<string>(locations.Length);
            for (int i = 0; i < locations.Length; i++)
            {
                if (!string.IsNullOrEmpty(locations[i])) list.Add(locations[i]);
            }
            if (list.Count > 0) GameModule.AudioModule.PreLoadPutInAudioPool(list);
        }

        private SoundConfig FindByLocation(string location)
        {
            if (TbSoundConfig.DataList == null) return null;

            if (m_configByLocation == null)
            {
                m_configByLocation = new Dictionary<string, SoundConfig>(TbSoundConfig.DataList.Count);
                var list = TbSoundConfig.DataList;
                for (int i = 0; i < list.Count; i++)
                {
                    var cfg = list[i];
                    if (cfg == null || string.IsNullOrEmpty(cfg.Location)) continue;
                    m_configByLocation[cfg.Location] = cfg;
                }
            }

            return m_configByLocation.TryGetValue(location, out var found) ? found : null;
        }

        private void WarnMissingConfig(string location)
        {
            if (!m_warnedMissingConfig.Add(location)) return;
            DLogger.Warning($"[SoundConfigMgr] 音效配置表缺少 Location={location}，已用默认参数播放；补表可调音量/音调/淡入淡出。");
        }

        private SoundRandomData GetSoundRandomData(int soundId, string fallback)
        {
            if (m_soundRandomDataCache.TryGetValue(soundId, out var cached)) return cached;
            var rows = TbSoundRandomConfig.GetListBySoundID(soundId);
            var locations = new List<string>();
            var weights = new List<float>();
            if (rows != null) for (int i = 0; i < rows.Count; i++)
                if (rows[i].Weight > 0f && !string.IsNullOrEmpty(rows[i].Location)) { locations.Add(rows[i].Location); weights.Add(rows[i].Weight); }
            if (locations.Count == 0) { locations.Add(fallback); weights.Add(1f); }
            cached = new SoundRandomData { Locations = locations.ToArray(), Weights = weights.ToArray() };
            m_soundRandomDataCache[soundId] = cached;
            return cached;
        }

        private static string SelectSoundRandomLocation(SoundRandomData snapshot)
        {
            float total = 0f; for (int i = 0; i < snapshot.Weights.Length; i++) total += snapshot.Weights[i];
            float roll = Random.value * total;
            for (int i = 0; i < snapshot.Locations.Length; i++) { roll -= snapshot.Weights[i]; if (roll <= 0f) return snapshot.Locations[i]; }
            return snapshot.Locations[snapshot.Locations.Length - 1];
        }
    }
}
