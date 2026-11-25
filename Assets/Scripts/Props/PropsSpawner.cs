using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace AnotherOne.Props
{
    /// <summary>
    /// Расставляет существующие предметы (префабы из Assets/Prefabs) по данным из JSON.
    /// Позиции и повороты совпадают с теми, что были прописаны в сцене вручную,
    /// поэтому геймплей не меняется — меняется только способ появления объектов.
    ///
    /// Rigidbody предметов НЕ трогаем: они остаются dynamic и их можно подобрать.
    /// Новый предмет = новая запись в файле данных.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(-100)]
    public class PropsSpawner : MonoBehaviour
    {
        [Header("Данные")]
        public string configFileName = "props_config.json";
        public string extendedConfigFileName = "props_config.extended.json";

        [Header("Реестр префабов (имя из JSON -> ассет)")]
        public PropPrefab[] prefabs = new PropPrefab[0];

        [Header("Отладка")]
        public bool logState = true;

        private readonly List<GameObject> _spawned = new List<GameObject>();
        private readonly Dictionary<string, GameObject> _map = new Dictionary<string, GameObject>();
        private string _currentFile;

        public int SpawnedCount => _spawned.Count;

        private void Awake()
        {
            BuildMap();
            Rebuild(configFileName);
        }

        private void Update()
        {
            if (!Input.GetKeyDown(KeyCode.F2)) return;
            string next = _currentFile == configFileName ? extendedConfigFileName : configFileName;
            Debug.Log("[Props] Переключение набора данных: " + next);
            Rebuild(next);
        }

        private void BuildMap()
        {
            _map.Clear();
            foreach (var p in prefabs)
            {
                if (p == null || p.prefab == null || string.IsNullOrEmpty(p.name)) continue;
                _map[p.name] = p.prefab;
            }
        }

        public PropsConfig LoadConfig(string fileName)
        {
            _currentFile = fileName;
            string path = Path.Combine(Application.streamingAssetsPath, fileName);

            if (!File.Exists(path))
            {
                Debug.LogWarning("[Props] Файл данных не найден: " + path + " — предметы не создаются.");
                return new PropsConfig();
            }

            PropsConfig cfg;
            try
            {
                cfg = JsonUtility.FromJson<PropsConfig>(File.ReadAllText(path));
            }
            catch (System.Exception e)
            {
                Debug.LogError("[Props] Данные повреждены (" + e.Message + ") — предметы не создаются.");
                return new PropsConfig();
            }

            return cfg == null ? new PropsConfig() : Sanitize(cfg);
        }

        /// <summary>Битые записи пропускаются по одной, остальные создаются.</summary>
        private PropsConfig Sanitize(PropsConfig cfg)
        {
            var ok = new List<PropEntry>();
            var used = new HashSet<string>();

            if (cfg.props != null)
            {
                foreach (var p in cfg.props)
                {
                    if (p == null) { Warn("пустая запись"); continue; }
                    if (string.IsNullOrEmpty(p.prefab)) { Warn("не указан prefab"); continue; }
                    if (!_map.ContainsKey(p.prefab)) { Warn("префаб не найден в реестре: " + p.prefab); continue; }
                    if (!used.Add(p.prefab)) { Warn("дубликат предмета: " + p.prefab); continue; }
                    if (float.IsNaN(p.x) || float.IsNaN(p.y) || float.IsNaN(p.z)) { Warn("некорректные координаты у " + p.prefab); continue; }
                    ok.Add(p);
                }
            }

            cfg.props = ok.ToArray();
            return cfg;
        }

        private static void Warn(string m) => Debug.LogWarning("[Props] Данные: " + m);

        public void Rebuild(string fileName)
        {
            PropsConfig cfg = LoadConfig(fileName);

            foreach (var go in _spawned) if (go != null) Destroy(go);
            _spawned.Clear();

            foreach (var p in cfg.props)
            {
                GameObject prefab = _map[p.prefab];
                GameObject inst = Instantiate(prefab, transform);
                inst.name = prefab.name;
                inst.transform.position = new Vector3(p.x, p.y, p.z);
                inst.transform.rotation = Quaternion.Euler(p.rotX, p.rotY, p.rotZ);
                if (logState) Debug.Log("[Props] " + prefab.name + " -> " + inst.transform.position.ToString("F2"));
                _spawned.Add(inst);
            }

            if (logState)
                Debug.Log("[Props] " + fileName + " | предметов: " + _spawned.Count);
        }
    }
}
